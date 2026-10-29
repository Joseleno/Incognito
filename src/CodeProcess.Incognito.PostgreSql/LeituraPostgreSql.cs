using System.Collections.Concurrent;
using System.Data;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A sessão de leitura do PostgreSQL: o coordenador e os leitores, cada um numa conexão nova da mesma fonte, todos numa
/// transação <c>REPEATABLE READ</c> só de leitura com <c>row_security = off</c>. O coordenador trava as tabelas pedidas em
/// <c>ACCESS SHARE</c>, uma por comando, antes de exportar o instante (<c>pg_export_snapshot()</c>), e o segura até o
/// descarte; os leitores o importam (<c>SET TRANSACTION SNAPSHOT</c>). A consistência é sempre o instante único.
/// </summary>
/// <remarks>
/// <para>
/// A ordem importa: o instante de uma transação <c>REPEATABLE READ</c> nasce na primeira consulta, e as conferências da
/// conexão rodam antes do <c>BEGIN</c>; depois dele, só ajustes e travas até o export. Sem as travas, um <c>TRUNCATE</c> ou
/// uma reescrita confirmada depois do instante deixaria a leitura vazia, sem erro (eles não respeitam o instante).
/// </para>
/// <para>
/// Cada leitura de um leitor roda num ponto de salvamento (a falha volta a ele, e o leitor segue no mesmo instante), trava a
/// tabela no próprio leitor sem esperar (um DDL na fila faria a leitura esperar atrás dele, e ele espera o coordenador) e
/// confere a guarda: o oid pelo nome e os arquivos da tabela e de cada partição, pelo instante e pelo catálogo atual. Assim
/// a queda do coordenador, que solta as travas, nunca deixa uma leitura vazia em silêncio, e a partição anexada depois do
/// instante, que a leitura pelo pai traria, é recusada.
/// </para>
/// <para>
/// Nenhuma mensagem traz texto do servidor, o identificador do instante ou algo das outras sessões: só textos fixos e os
/// nomes das tabelas pedidas.
/// </para>
/// </remarks>
internal sealed partial class SessaoLeituraPostgreSql : SessaoLeitura
{
    private const string Idade = """
        SELECT pg_catalog.age(a.backend_xmin)::pg_catalog.int8
        FROM pg_catalog.pg_stat_activity a
        WHERE a.pid = pg_catalog.pg_backend_pid()
        """;

    private const string FormaDasTravadas = """
        SELECT c.relkind = 'p'
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relname = $2 AND c.relkind IN ('r', 'p')
        """;

    private readonly NpgsqlConnection _coordenador;
    private readonly IReadOnlyList<LeitorDaSessaoPostgreSql> _leitores;
    private readonly LeitorEsquemaPostgreSql _esquema;
    private readonly bool _emReplica;
    private readonly Action<SessaoLeituraPostgreSql> _aoDescartar;

    private SessaoLeituraPostgreSql(
        ConexaoPostgreSql controle, NpgsqlConnection coordenador, IReadOnlyList<LeitorDaSessaoPostgreSql> leitores, bool emReplica, Action<SessaoLeituraPostgreSql> aoDescartar)
        : base(leitores)
    {
        _coordenador = coordenador;
        _leitores = leitores;
        _emReplica = emReplica;
        _aoDescartar = aoDescartar;
        _esquema = new LeitorEsquemaPostgreSql(controle, LancarSeDescartado, coordenador);
    }

    public override ConsistenciaObtida Obtida => ConsistenciaObtida.Instantaneo;

    /// <summary>Se a sessão lê uma réplica (<c>pg_is_in_recovery()</c> na abertura).</summary>
    internal bool EmReplica => _emReplica;

    /// <summary>As conexões da sessão, o coordenador primeiro, para os testes do dialeto.</summary>
    internal IEnumerable<NpgsqlConnection> Conexoes => [_coordenador, .. _leitores.Select(leitor => leitor.Conexao)];

    /// <summary>
    /// Abre a sessão: classifica as travadas pela conexão de controle, abre o coordenador, trava e exporta o instante, e abre
    /// os leitores, em série. Se algo falha, fecha o que já abriu.
    /// </summary>
    /// <param name="controle">A conexão de controle do dialeto.</param>
    /// <param name="pedido">O pedido.</param>
    /// <param name="depoisDoExport">Gancho dos testes, entre o export e a primeira importação.</param>
    /// <param name="aoDescartar">Chamado no descarte da sessão.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A sessão aberta.</returns>
    internal static async Task<SessaoLeituraPostgreSql> AbrirAsync(
        ConexaoPostgreSql controle, PedidoLeitura pedido, Func<CancellationToken, Task>? depoisDoExport, Action<SessaoLeituraPostgreSql> aoDescartar, CancellationToken cancelamento)
    {
        var travadas = await ClassificarAsync(controle, pedido.Travadas, cancelamento).ConfigureAwait(false);
        var abertas = new List<NpgsqlConnection>();
        var emReplica = false;
        try
        {
            var coordenador = await controle.AbrirParaLeituraAsync(cancelamento).ConfigureAwait(false);
            abertas.Add(coordenador);
            emReplica = await EscalarAsync<bool>(coordenador, "SELECT pg_catalog.pg_is_in_recovery()", cancelamento).ConfigureAwait(false);

            await coordenador.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancelamento).ConfigureAwait(false);
            await ExecutarAsync(coordenador, "SET TRANSACTION READ ONLY; SET LOCAL lock_timeout = '5s'; SET LOCAL row_security = off", cancelamento).ConfigureAwait(false);
            foreach (var (nome, particionada) in travadas)
            {
                await TravarAsync(coordenador, nome, particionada, cancelamento).ConfigureAwait(false);
            }

            var instante = await EscalarAsync<string>(coordenador, "SELECT pg_catalog.pg_export_snapshot()", cancelamento).ConfigureAwait(false);
            if (!FormaDoInstante().IsMatch(instante))
            {
                throw new IncognitoException(CodigosPostgreSql.SessaoDivergente, "O servidor PostgreSQL devolveu um instante exportado fora da forma esperada.");
            }

            if (depoisDoExport is not null)
            {
                await depoisDoExport(cancelamento).ConfigureAwait(false);
            }

            var leitores = new List<LeitorDaSessaoPostgreSql>(pedido.Leitores);
            for (var i = 0; i < pedido.Leitores; i++)
            {
                var conexao = await controle.AbrirParaLeituraAsync(cancelamento).ConfigureAwait(false);
                abertas.Add(conexao);
                await ImportarAsync(conexao, instante, cancelamento).ConfigureAwait(false);
                leitores.Add(new LeitorDaSessaoPostgreSql(conexao, emReplica));
            }

            return new SessaoLeituraPostgreSql(controle, coordenador, leitores, emReplica, aoDescartar);
        }
        catch (Exception erro)
        {
            abertas.Reverse();
            await Descarte.DepoisDaFalhaAsync([.. abertas]).ConfigureAwait(false);
            if (ErrosDaLeitura.Traduzir(erro, emReplica, naAbertura: true) is { } traduzida)
            {
                throw traduzida;
            }

            throw;
        }
    }

    protected override async Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento)
    {
        try
        {
            return await _esquema.LerAsync(cancelamento).ConfigureAwait(false);
        }
        catch (Exception erro) when (ErrosDaLeitura.Traduzir(erro, _emReplica, naAbertura: false) is { } traduzida)
        {
            throw traduzida;
        }
    }

    protected override async Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento)
    {
        try
        {
            return await LeitorEsquemaPostgreSql.NoPontoAsync(
                _coordenador,
                "idade",
                async ct =>
                {
                    await using var comando = new NpgsqlCommand(Idade, _coordenador);
                    return await comando.ExecuteScalarAsync(ct).ConfigureAwait(false) is long idade ? idade : (long?)null;
                },
                cancelamento).ConfigureAwait(false);
        }
        catch (Exception erro) when (ErrosDaLeitura.Traduzir(erro, _emReplica, naAbertura: false) is { } traduzida)
        {
            throw traduzida;
        }
    }

    // Os leitores antes do coordenador, que segura as travas até o fim; fechar a conexão desfaz a transação.
    protected override async ValueTask DisposeAsyncCore()
    {
        _aoDescartar(this);
        await Descarte.DepoisDaFalhaAsync([.. _leitores.Select(leitor => leitor.Conexao).Reverse(), _coordenador]).ConfigureAwait(false);
    }

    // Cada travada pela conexão de controle, antes do coordenador: a forma decide o ONLY (a particionada trava com as
    // partições; as outras, sem as herdeiras, como a cópia lê). Uma consulta no coordenador tiraria o instante antes das
    // travas.
    private static async Task<IReadOnlyList<(NomeObjeto Nome, bool Particionada)>> ClassificarAsync(
        ConexaoPostgreSql controle, IReadOnlyList<NomeObjeto> travadas, CancellationToken cancelamento)
    {
        if (travadas.Count == 0)
        {
            return [];
        }

        var classificadas = new List<(NomeObjeto, bool)>(travadas.Count);
        var transacao = await TransacaoDeLeitura.AbrirAsync(controle.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            foreach (var nome in travadas)
            {
                await using var comando = new NpgsqlCommand(FormaDasTravadas, controle.Conexao);
                comando.Parameters.Add(new NpgsqlParameter { Value = nome.Esquema, NpgsqlDbType = NpgsqlDbType.Text });
                comando.Parameters.Add(new NpgsqlParameter { Value = nome.Nome, NpgsqlDbType = NpgsqlDbType.Text });
                if (await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not bool particionada)
                {
                    throw TravadaInvalida(nome);
                }

                classificadas.Add((nome, particionada));
            }
        }

        return classificadas;
    }

    private static async Task TravarAsync(NpgsqlConnection coordenador, NomeObjeto nome, bool particionada, CancellationToken cancelamento)
    {
        try
        {
            await ExecutarAsync(
                coordenador,
                $"LOCK TABLE {(particionada ? "" : "ONLY ")}{CitacaoPostgreSql.Citar(nome)} IN ACCESS SHARE MODE",
                cancelamento).ConfigureAwait(false);
        }
        catch (PostgresException erro) when (erro.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected)
        {
            throw new IncognitoException(
                CodigosPostgreSql.TravaNaoObtida,
                $"A tabela {nome} não foi liberada em 5 s na abertura da leitura: outra sessão a segura (uma alteração da tabela, VACUUM FULL, TRUNCATE).");
        }
        catch (PostgresException erro) when (erro.SqlState is PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.UndefinedTable)
        {
            throw TravadaInvalida(nome);
        }
    }

    private static DialetoRecusouException TravadaInvalida(NomeObjeto nome) => new(
        CodigosPostgreSql.TravadaInvalida,
        $"A tabela {nome} não pôde ser travada para a leitura: não existe, não é uma tabela comum ou particionada, ou o login não tem SELECT nela.");

    private static async Task ImportarAsync(NpgsqlConnection conexao, string instante, CancellationToken cancelamento)
    {
        await conexao.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancelamento).ConfigureAwait(false);
        await ExecutarAsync(conexao, "SET TRANSACTION READ ONLY", cancelamento).ConfigureAwait(false);
        try
        {
            // O SET não aceita parâmetro; o identificador já foi conferido na forma estrita.
            await ExecutarAsync(conexao, $"SET TRANSACTION SNAPSHOT '{instante}'", cancelamento).ConfigureAwait(false);
        }
        catch (PostgresException erro) when (erro.SqlState is PostgresErrorCodes.FeatureNotSupported or PostgresErrorCodes.InvalidParameterValue or PostgresErrorCodes.UndefinedObject)
        {
            throw new IncognitoException(
                CodigosPostgreSql.LeituraEmOutroBanco,
                "Uma conexão da sessão de leitura não importou o instante da leitura: chegou a outro banco ou servidor, ou o coordenador caiu.");
        }

        await ExecutarAsync(conexao, "SET LOCAL row_security = off; SET LOCAL lock_timeout = '5s'", cancelamento).ConfigureAwait(false);
    }

    internal static async Task ExecutarAsync(NpgsqlConnection conexao, string sql, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    private static async Task<T> EscalarAsync<T>(NpgsqlConnection conexao, string sql, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        return (T)(await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false))!;
    }

    // O identificador do instante: hexadecimal, hexadecimal e decimal, só ASCII, curto.
    [GeneratedRegex("^[0-9A-F]{8}-[0-9A-F]{8}-[0-9]{1,10}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDoInstante();
}

/// <summary>Um leitor da sessão do PostgreSQL: uma conexão no instante da sessão.</summary>
internal sealed class LeitorDaSessaoPostgreSql(NpgsqlConnection conexao, bool emReplica) : LeitorDaSessao
{
    // A sessão ainda como aberta: código do usuário (a função de saída de um tipo) pode trocar os dois dentro da transação.
    private const string Sessao = """
        SELECT pg_catalog.current_setting('row_security') = 'off' AND pg_catalog.current_setting('transaction_read_only') = 'on'
        """;

    // A tabela é a do instante: o mesmo oid pelo nome no catálogo atual e os mesmos arquivos da tabela e de cada partição
    // (o TRUNCATE e as reescritas trocam o arquivo; a partição anexada ou destacada muda o conjunto). O instante lê o
    // catálogo pela transação; o catálogo atual, pelas funções.
    private const string Guarda = """
        WITH RECURSIVE instante(oid, arquivo) AS (
            SELECT c.oid, c.relfilenode FROM pg_catalog.pg_class c WHERE c.oid = $1
            UNION ALL
            SELECT c.oid, c.relfilenode
            FROM pg_catalog.pg_inherits i
            JOIN instante a ON i.inhparent = a.oid
            JOIN pg_catalog.pg_class c ON c.oid = i.inhrelid
            WHERE $2),
        atual(oid, arquivo) AS (
            SELECT t.relid, COALESCE(pg_catalog.pg_relation_filenode(t.relid), 0::pg_catalog.oid)
            FROM pg_catalog.pg_partition_tree($1) t
            WHERE $2
            UNION ALL
            SELECT $1, COALESCE(pg_catalog.pg_relation_filenode($1), 0::pg_catalog.oid)
            WHERE NOT $2)
        SELECT COALESCE(pg_catalog.to_regclass($3)::pg_catalog.oid = $1, false)
            AND NOT EXISTS (SELECT oid, arquivo FROM instante EXCEPT SELECT oid, arquivo FROM atual)
            AND NOT EXISTS (SELECT oid, arquivo FROM atual EXCEPT SELECT oid, arquivo FROM instante)
        """;

    /// <summary>A conexão do leitor.</summary>
    internal NpgsqlConnection Conexao => conexao;

    protected override async Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        try
        {
            return await LeitorEsquemaPostgreSql.NoPontoAsync(conexao, "leitura", ct => LerNoPontoAsync(coluna, ct), cancelamento).ConfigureAwait(false);
        }
        catch (Exception erro) when (ErrosDaLeitura.Traduzir(erro, emReplica, naAbertura: false) is { } traduzida)
        {
            throw traduzida;
        }
    }

    private async Task<long?> LerNoPontoAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        await using (var sessao = new NpgsqlCommand(Sessao, conexao))
        {
            if (await sessao.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                throw new IncognitoException(
                    CodigosPostgreSql.SessaoDivergente,
                    "A sessão de leitura do PostgreSQL não está mais só de leitura com a segurança por linha desligada: algo a trocou dentro da transação.");
            }
        }

        uint oid;
        bool particionada;
        await using (var conferir = new NpgsqlCommand(LeitorDadosOrigemPostgreSql.ConsultaColuna, conexao))
        {
            conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Tabela.Esquema, NpgsqlDbType = NpgsqlDbType.Text });
            conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Tabela.Nome, NpgsqlDbType = NpgsqlDbType.Text });
            conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Coluna, NpgsqlDbType = NpgsqlDbType.Text });
            await using var leitor = await conferir.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false) || leitor.IsDBNull(1) || !leitor.GetBoolean(1))
            {
                throw new DialetoRecusouException(
                    CodigosPostgreSql.ColunaNaoInteira,
                    "O maior valor só se lê de uma coluna smallint, integer ou bigint de uma tabela: a tabela ou a coluna não existe, ou é de outro tipo.");
            }

            particionada = leitor.GetBoolean(0);
            oid = leitor.GetFieldValue<uint>(2);
        }

        var tabela = (particionada ? "" : "ONLY ") + CitacaoPostgreSql.Citar(coluna.Tabela);
        await SessaoLeituraPostgreSql.ExecutarAsync(conexao, $"LOCK TABLE {tabela} IN ACCESS SHARE MODE NOWAIT", cancelamento).ConfigureAwait(false);
        await using (var guarda = new NpgsqlCommand(Guarda, conexao))
        {
            guarda.Parameters.Add(new NpgsqlParameter { Value = oid, NpgsqlDbType = NpgsqlDbType.Oid });
            guarda.Parameters.Add(new NpgsqlParameter { Value = particionada, NpgsqlDbType = NpgsqlDbType.Boolean });
            guarda.Parameters.Add(new NpgsqlParameter { Value = CitacaoPostgreSql.Citar(coluna.Tabela), NpgsqlDbType = NpgsqlDbType.Text });
            if (await guarda.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                throw ErrosDaLeitura.TabelaMudou();
            }
        }

        var citada = CitacaoPostgreSql.Citar(coluna.Coluna);
        await using var comando = new NpgsqlCommand($"SELECT pg_catalog.max({citada})::pg_catalog.int8 FROM {tabela}", conexao);
        return await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is long valor ? valor : null;
    }
}

/// <summary>A tradução dos erros da sessão de leitura em códigos do dialeto, só pelo SQLSTATE, nunca pelo texto do servidor.</summary>
internal static class ErrosDaLeitura
{
    // snapshot_too_old (classe 72), que o Npgsql não nomeia.
    private const string InstanteAntigoDemais = "72000";

    /// <summary>A falha traduzida, ou nula para sair como veio (o cancelamento e o que o dialeto não reconhece).</summary>
    /// <param name="erro">A falha.</param>
    /// <param name="emReplica">Se a sessão lê uma réplica: só ali o conflito e a conexão perdida são da reprodução.</param>
    /// <param name="naAbertura">Se a falha veio da abertura (o <c>55P03</c> ali é da trava do coordenador, já traduzido).</param>
    /// <returns>A falha traduzida, ou nula.</returns>
    internal static Exception? Traduzir(Exception erro, bool emReplica, bool naAbertura) => erro switch
    {
        PostgresException { SqlState: PostgresErrorCodes.OutOfMemory } => new IncognitoException(
            CodigosPostgreSql.TravasDemais,
            "A tabela de travas do servidor PostgreSQL se esgotou: aumente max_locks_per_transaction (vale também para a réplica), ou copie menos tabelas por vez."),
        PostgresException { SqlState: PostgresErrorCodes.TooManyConnections } => new IncognitoException(
            CodigosPostgreSql.ConexoesDemais,
            "O servidor PostgreSQL recusou mais conexões: a leitura usa uma conexão por leitor e mais uma; reduza o paralelismo ou aumente o limite."),
        PostgresException { SqlState: InstanteAntigoDemais } => new IncognitoException(
            CodigosPostgreSql.InstanteAntigo,
            "O instante da leitura ficou antigo demais para o servidor PostgreSQL (old_snapshot_threshold): desligue-o ou aumente-o para a cópia."),
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } when !naAbertura => new IncognitoException(
            CodigosPostgreSql.LeituraRecusada,
            "O login não lê a tabela, ou a segurança por linha vale para ele: a leitura falhou em vez de voltar filtrada."),
        PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } when !naAbertura => TabelaMudou(),
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.DatabaseDropped } when emReplica => ConflitoNaReplica(),
        PostgresException => null,
        NpgsqlException when emReplica => ConflitoNaReplica(),
        _ => null,
    };

    /// <summary>A tabela mudou depois do início da leitura, ou tem uma alteração pendente.</summary>
    internal static IncognitoException TabelaMudou() => new(
        CodigosPostgreSql.TabelaMudou,
        "A tabela mudou na origem depois do início da leitura (foi esvaziada, reescrita, trocada ou teve partição anexada ou destacada), ou tem uma alteração pendente: a leitura foi interrompida em vez de voltar incompleta.");

    private static IncognitoException ConflitoNaReplica() => new(
        CodigosPostgreSql.ConflitoNaReplica,
        "A leitura na réplica foi interrompida pela reprodução (conflito com a recuperação) ou perdeu a conexão: use uma réplica dedicada com max_standby_streaming_delay = -1, ou um clone.");
}

/// <summary>As sessões de leitura vivas de um dialeto: o descarte do dialeto as fecha antes da conexão de controle.</summary>
internal sealed class SessoesVivas(ConexaoPostgreSql controle)
{
    private readonly ConcurrentDictionary<SessaoLeituraPostgreSql, byte> _vivas = new();
    private int _fechadas;

    /// <summary>Gancho dos testes do dialeto, entre o export do instante e a primeira importação.</summary>
    internal Func<CancellationToken, Task>? DepoisDoExport { get; set; }

    /// <summary>Abre uma sessão e a registra.</summary>
    internal async Task<SessaoLeitura> AbrirAsync(PedidoLeitura pedido, CancellationToken cancelamento)
    {
        var sessao = await SessaoLeituraPostgreSql.AbrirAsync(controle, pedido, DepoisDoExport, viva => _vivas.TryRemove(viva, out _), cancelamento).ConfigureAwait(false);
        _vivas[sessao] = 0;
        if (Volatile.Read(ref _fechadas) != 0)
        {
            await sessao.DisposeAsync().ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(DialetoBanco));
        }

        return sessao;
    }

    /// <summary>Fecha as sessões vivas; as abertas depois disso são fechadas na hora.</summary>
    internal async ValueTask FecharAsync()
    {
        Volatile.Write(ref _fechadas, 1);
        foreach (var sessao in _vivas.Keys.ToArray())
        {
            await sessao.DisposeAsync().ConfigureAwait(false);
        }
    }
}
