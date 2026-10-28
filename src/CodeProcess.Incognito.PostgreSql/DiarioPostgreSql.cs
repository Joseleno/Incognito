using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// O diário de execução do PostgreSQL, no esquema <c>incognito</c> do destino (versão 1): a tabela <c>execucao</c> e a
/// <c>restricao_removida</c>, com uma linha por chave estrangeira removida (a definição de <c>pg_get_constraintdef</c>, lida
/// no servidor, e o estado de validação e de imposição) e uma por trigger de usuário desligada (o <c>tgenabled</c>, por
/// tabela física).
/// </summary>
/// <remarks>
/// <para>
/// Cada operação é uma transação na conexão de controle, com <c>lock_timeout</c> de 5 s, que começa conferindo, no
/// <c>pg_locks</c>, que a sessão do bloqueio desta instância ainda tem o lock, e que o diário é o esperado: o esquema e as
/// tabelas do login, nenhum papel além dele e do superuser podendo escrever nelas (só ler: o CI lê a situação), as colunas,
/// os tipos e as restrições exatos, e nada que rode código numa gravação (trigger, regra, política, default, herança) nem
/// outro objeto no esquema. A remoção trava as tabelas antes de ler o estado, e a definição vai do catálogo ao diário
/// dentro do servidor.
/// </para>
/// <para>
/// A recriação executa a definição guardada: só depois de conferi-la por uma gramática fechada (a forma que o
/// <c>pg_get_constraintdef</c> dá a uma chave estrangeira, sem vírgula fora de parênteses, aspas simples, cifrão, ponto e
/// vírgula nem comentário), num comando só (<see cref="NpgsqlBatch"/>, que o servidor recusa com mais de um), e conferindo no
/// servidor, na mesma transação, que a chave recriada tem a definição guardada. Uma violação de integridade (a classe 23,
/// cuja mensagem traz o valor da linha) é resultado, nunca exceção, e a mensagem do servidor nunca sai daqui.
/// </para>
/// </remarks>
internal sealed class DiarioPostgreSql(ConexaoPostgreSql conexao, BloqueioPostgreSql bloqueio, Action lancarSeDescartado) : DiarioExecucao
{
    /// <summary>A versão do diário que este dialeto grava e lê (as anteriores também; uma mais nova é recusada).</summary>
    internal const int Versao = 1;

    private const string Esquema = "incognito";

    private const string CriarTabelas = """
        CREATE TABLE IF NOT EXISTS incognito.execucao (
            id pg_catalog.uuid NOT NULL,
            versao_diario pg_catalog.int4 NOT NULL,
            marcador pg_catalog.uuid NOT NULL,
            iniciada_em pg_catalog.timestamptz NOT NULL,
            encerrada_em pg_catalog.timestamptz,
            situacao pg_catalog.text NOT NULL,
            CONSTRAINT execucao_pkey PRIMARY KEY (id));
        CREATE TABLE IF NOT EXISTS incognito.restricao_removida (
            execucao pg_catalog.uuid NOT NULL,
            tipo pg_catalog.text NOT NULL,
            esquema pg_catalog.text NOT NULL,
            tabela pg_catalog.text NOT NULL,
            nome pg_catalog.text NOT NULL,
            raiz_esquema pg_catalog.text NOT NULL,
            raiz_tabela pg_catalog.text NOT NULL,
            definicao pg_catalog.text NOT NULL,
            validada pg_catalog.bool,
            imposta pg_catalog.bool,
            situacao pg_catalog.text NOT NULL,
            CONSTRAINT restricao_removida_pkey PRIMARY KEY (tipo, esquema, tabela, nome))
        """;

    // Os papéis que podem escrever no diário, além do login e do superuser (os predefinidos, como pg_write_all_data, valem
    // pelos membros, que são papéis comuns); o PUBLIC, pelo efeito em todos.
    private const string PapeisQueEscrevem = """
        SELECT r.rolname
        FROM pg_catalog.pg_roles r
        WHERE NOT r.rolsuper AND r.rolname <> current_user AND r.rolname NOT LIKE 'pg\_%'
          AND (EXISTS (SELECT FROM pg_catalog.pg_namespace n
                       WHERE n.nspname = 'incognito' AND pg_catalog.has_schema_privilege(r.oid, n.oid, 'CREATE'))
               OR EXISTS (SELECT FROM pg_catalog.pg_class c
                          JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname = 'incognito' AND c.relname IN ('execucao', 'restricao_removida')
                            AND (pg_catalog.has_table_privilege(r.oid, c.oid, 'INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                                 OR pg_catalog.has_any_column_privilege(r.oid, c.oid, 'INSERT, UPDATE'))))
        """;

    // A integridade do diário: se o esquema existe, quantas das tabelas existem, e se o que existe é o esperado.
    private const string Integridade = $$"""
        WITH login AS (SELECT r.oid FROM pg_catalog.pg_roles r WHERE r.rolname = current_user),
        esquema AS (SELECT n.oid, n.nspowner FROM pg_catalog.pg_namespace n WHERE n.nspname = 'incognito'),
        tabelas AS (
            SELECT c.oid, c.relname, c.relkind, c.relpersistence, c.relowner, c.relrowsecurity, c.relhasrules
            FROM pg_catalog.pg_class c
            JOIN esquema e ON c.relnamespace = e.oid
            WHERE c.relname IN ('execucao', 'restricao_removida')),
        esperadas (tabela, coluna, tipo, obrigatoria) AS (VALUES
            ('execucao', 'id', 'pg_catalog.uuid'::pg_catalog.regtype, true),
            ('execucao', 'versao_diario', 'pg_catalog.int4'::pg_catalog.regtype, true),
            ('execucao', 'marcador', 'pg_catalog.uuid'::pg_catalog.regtype, true),
            ('execucao', 'iniciada_em', 'pg_catalog.timestamptz'::pg_catalog.regtype, true),
            ('execucao', 'encerrada_em', 'pg_catalog.timestamptz'::pg_catalog.regtype, false),
            ('execucao', 'situacao', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'execucao', 'pg_catalog.uuid'::pg_catalog.regtype, true),
            ('restricao_removida', 'tipo', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'esquema', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'tabela', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'nome', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'raiz_esquema', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'raiz_tabela', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'definicao', 'pg_catalog.text'::pg_catalog.regtype, true),
            ('restricao_removida', 'validada', 'pg_catalog.bool'::pg_catalog.regtype, false),
            ('restricao_removida', 'imposta', 'pg_catalog.bool'::pg_catalog.regtype, false),
            ('restricao_removida', 'situacao', 'pg_catalog.text'::pg_catalog.regtype, true))
        SELECT EXISTS (SELECT FROM esquema),
               (SELECT pg_catalog.count(*) FROM tabelas)::pg_catalog.int4,
               COALESCE((SELECT e.nspowner = (SELECT l.oid FROM login l) FROM esquema e), true)
               AND NOT EXISTS (SELECT FROM tabelas t
                               WHERE t.relkind <> 'r' OR t.relpersistence <> 'p' OR t.relowner <> (SELECT l.oid FROM login l)
                                  OR t.relrowsecurity OR t.relhasrules)
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_trigger g WHERE g.tgrelid IN (SELECT t.oid FROM tabelas t))
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy p WHERE p.polrelid IN (SELECT t.oid FROM tabelas t))
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_inherits h
                               WHERE h.inhrelid IN (SELECT t.oid FROM tabelas t) OR h.inhparent IN (SELECT t.oid FROM tabelas t))
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_attrdef d WHERE d.adrelid IN (SELECT t.oid FROM tabelas t))
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a
                               JOIN tabelas t ON t.oid = a.attrelid
                               WHERE a.attnum > 0 AND NOT a.attisdropped
                                 AND NOT EXISTS (SELECT FROM esperadas x
                                                 WHERE x.tabela = t.relname AND x.coluna = a.attname
                                                   AND x.tipo = a.atttypid AND x.obrigatoria = a.attnotnull))
               AND NOT EXISTS (SELECT FROM esperadas x
                               JOIN tabelas t ON t.relname = x.tabela
                               WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a
                                                 WHERE a.attrelid = t.oid AND a.attname = x.coluna AND a.attnum > 0 AND NOT a.attisdropped))
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_constraint k
                               JOIN tabelas t ON t.oid = k.conrelid
                               WHERE NOT (k.contype = 'n' OR (k.contype = 'p' AND k.conname = t.relname || '_pkey')))
               AND (SELECT pg_catalog.count(*) FROM pg_catalog.pg_constraint k
                    JOIN tabelas t ON t.oid = k.conrelid
                    WHERE k.contype = 'p') = (SELECT pg_catalog.count(*) FROM tabelas)
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_index i
                               JOIN tabelas t ON t.oid = i.indrelid
                               WHERE NOT i.indisprimary OR i.indexprs IS NOT NULL OR i.indpred IS NOT NULL)
               AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend d
                               JOIN esquema e ON d.refobjid = e.oid
                               WHERE d.refclassid = 'pg_catalog.pg_namespace'::pg_catalog.regclass
                                 AND NOT (d.classid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.objid IN (SELECT t.oid FROM tabelas t)))
               AND NOT EXISTS ({{PapeisQueEscrevem}})
        """;

    // O lock da sessão do bloqueio desta instância (a forma de dois int4 é a de objsubid = 2).
    private const string Bloqueio = """
        SELECT EXISTS (
            SELECT FROM pg_catalog.pg_locks l
            WHERE l.locktype = 'advisory' AND l.pid = $1 AND l.granted
              AND l.database = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database())
              AND l.classid = $2::pg_catalog.oid AND l.objid = $3::pg_catalog.oid AND l.objsubid = 2)
        """;

    // Uma tabela do usuário pelo nome, comum ou particionada, fora dos esquemas do sistema e do diário: o oid, se é
    // particionada e se o login pode alterá-la.
    private const string TabelaDoUsuario = """
        SELECT c.oid, c.relkind = 'p', pg_catalog.pg_has_role(c.relowner, 'USAGE')
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relname = $2 AND c.relkind IN ('r', 'p')
          AND n.nspname NOT IN ('incognito', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
        """;

    // A chave estrangeira original (não a cópia numa partição) de uma tabela, pelo nome: o oid, a tabela referenciada e o
    // estado.
    private const string ChaveDaTabela = """
        SELECT k.oid, rn.nspname, r.relname
        FROM pg_catalog.pg_constraint k
        JOIN pg_catalog.pg_class r ON r.oid = k.confrelid
        JOIN pg_catalog.pg_namespace rn ON rn.oid = r.relnamespace
        WHERE k.conrelid = $1 AND k.conname = $2 AND k.contype = 'f' AND k.conparentid = 0
        """;

    // Se a chave de agora tem a definição guardada: igual (0), igual mais NOT VALID (1), ou outra (2).
    private const string CompararComOGuardado = """
        SELECT CASE WHEN pg_catalog.pg_get_constraintdef(k.oid) = r.definicao THEN 0
                    WHEN pg_catalog.pg_get_constraintdef(k.oid) = r.definicao || ' NOT VALID' THEN 1
                    ELSE 2 END
        FROM pg_catalog.pg_constraint k, incognito.restricao_removida r
        WHERE k.oid = $1 AND k.contype = 'f' AND r.tipo = 'chave' AND r.esquema = $2 AND r.tabela = $3 AND r.nome = $4
        """;

    private static readonly IReadOnlyList<ObjetoReservado> _objetos =
    [
        Objeto("execucao", TipoObjetoReservado.Tabela),
        Objeto("execucao_pkey", TipoObjetoReservado.Indice),
        Objeto("restricao_removida", TipoObjetoReservado.Tabela),
        Objeto("restricao_removida_pkey", TipoObjetoReservado.Indice),
    ];

    public override IReadOnlyList<ObjetoReservado> ObjetosDoDiario => _objetos;

    public override Task CriarAsync(CancellationToken cancelamento) => NaTransacaoAsync(
        exigeDiario: false,
        async transacao =>
        {
            var (temEsquema, presentes, integro) = await IntegridadeAsync(transacao, cancelamento).ConfigureAwait(false);
            if (temEsquema && !integro)
            {
                throw Inesperado();
            }

            try
            {
                if (!temEsquema)
                {
                    await ExecutarAsync($"CREATE SCHEMA {Esquema}", transacao, cancelamento).ConfigureAwait(false);
                }

                if (presentes < 2)
                {
                    await ExecutarAsync(CriarTabelas, transacao, cancelamento).ConfigureAwait(false);
                }
            }
            catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new DialetoRecusouException(
                    CodigosPostgreSql.DiarioSemPrivilegio,
                    "O login do destino não pode criar o esquema do diário do Incognito: prepare o destino com um login que tenha CREATE no banco (o dono do banco tem).");
            }

            // As concessões que o banco deu às tabelas novas (ALTER DEFAULT PRIVILEGES) e as do PUBLIC: só a leitura fica.
            await ExecutarAsync(
                "REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON incognito.execucao, incognito.restricao_removida FROM PUBLIC; REVOKE CREATE ON SCHEMA incognito FROM PUBLIC",
                transacao,
                cancelamento).ConfigureAwait(false);
            foreach (var papel in await PapeisAsync(transacao, cancelamento).ConfigureAwait(false))
            {
                // O papel citado do catálogo, nunca uma tabela (a guarda das leituras de tabela procura o FROM interpolado).
                var dePapel = " FROM " + CitacaoPostgreSql.Citar(papel);
                await ExecutarAsync(
                    "REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON incognito.execucao, incognito.restricao_removida" + dePapel + "; REVOKE CREATE ON SCHEMA incognito" + dePapel,
                    transacao,
                    cancelamento).ConfigureAwait(false);
            }

            (_, presentes, integro) = await IntegridadeAsync(transacao, cancelamento).ConfigureAwait(false);
            return presentes == 2 && integro ? true : throw Inesperado();
        },
        cancelamento);

    public override async Task<EstadoDiario> LerAsync(CancellationToken cancelamento)
    {
        EstadoDiario? estado = null;
        await NaTransacaoAsync(
            exigeDiario: false,
            async transacao =>
            {
                var (temEsquema, presentes, integro) = await IntegridadeAsync(transacao, cancelamento).ConfigureAwait(false);
                if (temEsquema && !integro)
                {
                    throw Inesperado();
                }

                estado = presentes < 2 ? new EstadoDiario { Existe = false } : await LerEstadoAsync(transacao, cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento).ConfigureAwait(false);
        return estado!;
    }

    public override Task IniciarAsync(Guid execucao, Guid marcador, DateTimeOffset iniciadaEm, CancellationToken cancelamento)
    {
        ExigirUtc(iniciadaEm, nameof(iniciadaEm));
        return NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                await using (var andamento = Comando("SELECT EXISTS (SELECT FROM incognito.execucao e WHERE e.situacao = 'em-andamento')", transacao))
                {
                    if (await andamento.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is true)
                    {
                        throw Incoerente("Há uma execução do Incognito em andamento no diário do destino: repare antes de começar outra.");
                    }
                }

                await using var inserir = Comando(
                    "INSERT INTO incognito.execucao (id, versao_diario, marcador, iniciada_em, situacao) VALUES ($1, $2, $3, $4, 'em-andamento')",
                    transacao,
                    execucao,
                    Versao,
                    marcador,
                    iniciadaEm);
                await inserir.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento);
    }

    public override Task RemoverChaveAsync(Guid execucao, NomeObjeto tabela, string chave, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentException.ThrowIfNullOrEmpty(chave);
        return NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                await ExigirEmAndamentoAsync(execucao, transacao, cancelamento).ConfigureAwait(false);
                var situacao = await SituacaoDaChaveAsync(tabela, chave, transacao, cancelamento).ConfigureAwait(false);
                var (oid, _, _) = await TabelaAsync(tabela, transacao, cancelamento).ConfigureAwait(false) ?? throw SemAChave();

                // Trava o filho e o pai antes de ler o estado: entre a leitura e a remoção, ninguém muda a chave.
                if (await ChaveAsync(oid, chave, transacao, cancelamento).ConfigureAwait(false) is not { Referenciada: var referenciada })
                {
                    // A chave já fora, com a entrada: a execução nova a adota, com a definição original.
                    if (situacao is null)
                    {
                        throw SemAChave();
                    }

                    await AdotarAsync(execucao, tabela, chave, "removida", transacao, cancelamento).ConfigureAwait(false);
                    return true;
                }

                await ExecutarAsync(
                    $"LOCK TABLE {CitacaoPostgreSql.Citar(tabela)}, {CitacaoPostgreSql.Citar(referenciada)} IN ACCESS EXCLUSIVE MODE",
                    transacao,
                    cancelamento).ConfigureAwait(false);
                var (relido, _) = await ChaveAsync(oid, chave, transacao, cancelamento).ConfigureAwait(false) ?? throw SemAChave();
                if (situacao is null)
                {
                    await using var registrar = Comando(
                        $"""
                        INSERT INTO incognito.restricao_removida
                            (execucao, tipo, esquema, tabela, nome, raiz_esquema, raiz_tabela, definicao, validada, imposta, situacao)
                        SELECT $1, 'chave', $2, $3, $4, $2, $3, pg_catalog.pg_get_constraintdef(k.oid), k.convalidated, {Imposta}, 'removida'
                        FROM pg_catalog.pg_constraint k
                        WHERE k.oid = $5 AND k.contype = 'f'
                        """,
                        transacao,
                        execucao,
                        tabela.Esquema,
                        tabela.Nome,
                        chave,
                        relido);
                    await registrar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                }
                else
                {
                    // A chave que voltou (sem validação, ou recriada igual por alguém) com a entrada: removida de novo, com a
                    // definição original; com outra definição, nada muda.
                    if (await CompararAsync(relido, tabela, chave, transacao, cancelamento).ConfigureAwait(false) == 2)
                    {
                        throw OutraDefinicao();
                    }

                    await AdotarAsync(execucao, tabela, chave, "removida", transacao, cancelamento).ConfigureAwait(false);
                }

                await ExecutarAsync($"ALTER TABLE {CitacaoPostgreSql.Citar(tabela)} DROP CONSTRAINT {CitacaoPostgreSql.Citar(chave)}", transacao, cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento);
    }

    public override Task DesligarTriggersAsync(Guid execucao, NomeObjeto tabela, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        return NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                await ExigirEmAndamentoAsync(execucao, transacao, cancelamento).ConfigureAwait(false);
                await using (var existente = Comando(
                    "SELECT EXISTS (SELECT FROM incognito.restricao_removida r WHERE r.tipo = 'trigger' AND r.raiz_esquema = $1 AND r.raiz_tabela = $2)",
                    transacao,
                    tabela.Esquema,
                    tabela.Nome))
                {
                    if (await existente.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is true)
                    {
                        throw Incoerente("A tabela já tem as triggers no diário do destino: repare antes de desligá-las de novo.");
                    }
                }

                var (oid, particionada, _) = await TabelaAsync(tabela, transacao, cancelamento).ConfigureAwait(false)
                    ?? throw Incoerente("A tabela cujas triggers seriam desligadas não existe no destino, ou não é tabela comum nem particionada.");

                // Trava a árvore antes de ler o estado das triggers: ninguém as muda nem anexa partição no meio.
                await ExecutarAsync($"LOCK TABLE {CitacaoPostgreSql.Citar(tabela)} IN SHARE ROW EXCLUSIVE MODE", transacao, cancelamento).ConfigureAwait(false);
                var fisicas = await TabelasFisicasAsync(oid, particionada, transacao, cancelamento).ConfigureAwait(false);
                await using (var registrar = Comando(
                    """
                    INSERT INTO incognito.restricao_removida
                        (execucao, tipo, esquema, tabela, nome, raiz_esquema, raiz_tabela, definicao, validada, imposta, situacao)
                    SELECT $1, 'trigger', n.nspname, c.relname, t.tgname, $2, $3, t.tgenabled, NULL, NULL, 'removida'
                    FROM pg_catalog.pg_trigger t
                    JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE t.tgrelid = ANY ($4) AND NOT t.tgisinternal
                    """,
                    transacao,
                    execucao,
                    tabela.Esquema,
                    tabela.Nome,
                    fisicas.Select(fisica => fisica.Oid).ToArray()))
                {
                    await registrar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                }

                // Uma tabela física por vez, sem descer: o desligamento pela raiz só alcança as cópias das triggers dela.
                foreach (var fisica in fisicas.Where(fisica => fisica.ComTriggers))
                {
                    await ExecutarAsync($"ALTER TABLE ONLY {CitacaoPostgreSql.Citar(fisica.Nome)} DISABLE TRIGGER USER", transacao, cancelamento).ConfigureAwait(false);
                }

                return true;
            },
            cancelamento);
    }

    public override async Task<IReadOnlyList<TriggerDoDiario>> ReligarTriggersAsync(EntradaDiario entrada, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var puladas = new List<TriggerDoDiario>();
        await NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                var guardadas = new List<(NomeObjeto Tabela, string Nome, string Estado)>();
                await using (var ler = Comando(
                    "SELECT r.esquema, r.tabela, r.nome, r.definicao FROM incognito.restricao_removida r WHERE r.tipo = 'trigger' AND r.raiz_esquema = $1 AND r.raiz_tabela = $2",
                    transacao,
                    entrada.Tabela.Esquema,
                    entrada.Tabela.Nome))
                {
                    await using var leitor = await ler.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
                    while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
                    {
                        guardadas.Add((new NomeObjeto(leitor.GetString(0), leitor.GetString(1)), leitor.GetString(2), leitor.GetString(3)));
                    }
                }

                if (guardadas.Count == 0)
                {
                    return true;
                }

                if (await TabelaAsync(entrada.Tabela, transacao, cancelamento).ConfigureAwait(false) is not null)
                {
                    await ExecutarAsync($"LOCK TABLE {CitacaoPostgreSql.Citar(entrada.Tabela)} IN SHARE ROW EXCLUSIVE MODE", transacao, cancelamento).ConfigureAwait(false);
                }

                foreach (var (fisica, nome, estado) in guardadas.OrderBy(guardada => guardada.Tabela, OrdemDeNomes.Instancia).ThenBy(guardada => guardada.Nome, StringComparer.Ordinal))
                {
                    var acao = estado switch
                    {
                        "O" => "ENABLE TRIGGER",
                        "R" => "ENABLE REPLICA TRIGGER",
                        "A" => "ENABLE ALWAYS TRIGGER",
                        "D" => "DISABLE TRIGGER",
                        _ => throw Inesperado(),
                    };
                    if (!await TriggerExisteAsync(fisica, nome, transacao, cancelamento).ConfigureAwait(false))
                    {
                        puladas.Add(new TriggerDoDiario { Tabela = fisica, Nome = nome });
                        continue;
                    }

                    await ExecutarAsync($"ALTER TABLE ONLY {CitacaoPostgreSql.Citar(fisica)} {acao} {CitacaoPostgreSql.Citar(nome)}", transacao, cancelamento).ConfigureAwait(false);
                }

                await using var apagar = Comando(
                    "DELETE FROM incognito.restricao_removida r WHERE r.tipo = 'trigger' AND r.raiz_esquema = $1 AND r.raiz_tabela = $2",
                    transacao,
                    entrada.Tabela.Esquema,
                    entrada.Tabela.Nome);
                await apagar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento).ConfigureAwait(false);
        return puladas;
    }

    public override async Task<SituacaoEntrada?> RecriarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento)
    {
        var (tabela, chave) = Chave(entrada);
        SituacaoEntrada? depois = null;
        await NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                if (await GuardadaAsync(tabela, chave, transacao, cancelamento).ConfigureAwait(false) is not { } guardada)
                {
                    depois = null;
                    return true;
                }

                var (oid, particionada, doLogin) = await TabelaAsync(tabela, transacao, cancelamento).ConfigureAwait(false)
                    ?? throw Incoerente("A tabela da chave guardada no diário não existe mais no destino: descarte a entrada.");
                if (!doLogin)
                {
                    throw Inesperado();
                }

                if (await ChaveAsync(oid, chave, transacao, cancelamento).ConfigureAwait(false) is { Oid: var existente })
                {
                    depois = await Reconciliar(existente, tabela, chave, guardada, transacao, cancelamento).ConfigureAwait(false);
                    return true;
                }

                if (!FormatoDaDefinicao.Valida(guardada.Definicao))
                {
                    throw Inesperado();
                }

                // Sem validar quando o banco aceita (a validação vem depois, sem travar o pai para escrita); a particionada
                // do 15 ao 17 não aceita NOT VALID, e valida direto.
                var semValidar = guardada.Validada && (!particionada || conexao.Versao >= 180000);
                var esperada = guardada.Definicao + (semValidar ? " NOT VALID" : "");
                await transacao.SaveAsync("recriar", cancelamento).ConfigureAwait(false);
                try
                {
                    await using var lote = new NpgsqlBatch(conexao.Conexao, transacao)
                    {
                        BatchCommands = { new NpgsqlBatchCommand($"ALTER TABLE {CitacaoPostgreSql.Citar(tabela)} ADD CONSTRAINT {CitacaoPostgreSql.Citar(chave)} {esperada}") },
                    };
                    await lote.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                }
                catch (PostgresException erro) when (DeIntegridade(erro))
                {
                    // A validação direta achou linha órfã: a chave continua fora, e a entrada fica.
                    await transacao.RollbackAsync("recriar", cancelamento).ConfigureAwait(false);
                    depois = SituacaoEntrada.Removida;
                    return true;
                }

                await using (var conferir = Comando(
                    "SELECT pg_catalog.pg_get_constraintdef(k.oid) = $3 FROM pg_catalog.pg_constraint k WHERE k.conrelid = $1 AND k.conname = $2 AND k.contype = 'f' AND k.conparentid = 0",
                    transacao,
                    oid,
                    chave,
                    esperada))
                {
                    if (await conferir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
                    {
                        throw new IncognitoException(CodigosPostgreSql.RecriacaoNaoConfere, "A chave recriada no destino não ficou igual à definição guardada no diário: nada foi mudado.");
                    }
                }

                depois = semValidar ? SituacaoEntrada.SemValidacao : null;
                await MarcarAsync(tabela, chave, depois, transacao, cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento).ConfigureAwait(false);
        return depois;
    }

    public override async Task<SituacaoEntrada?> ValidarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento)
    {
        var (tabela, chave) = Chave(entrada);
        SituacaoEntrada? depois = null;
        await NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                if (await GuardadaAsync(tabela, chave, transacao, cancelamento).ConfigureAwait(false) is not { } guardada)
                {
                    depois = null;
                    return true;
                }

                var daTabela = await TabelaAsync(tabela, transacao, cancelamento).ConfigureAwait(false);
                var lida = daTabela is { Oid: var oid } ? await ChaveAsync(oid, chave, transacao, cancelamento).ConfigureAwait(false) : null;
                if (lida is not { Oid: var existente })
                {
                    // A chave sumiu depois de recriada: a entrada volta a removida, para recriar de novo.
                    depois = SituacaoEntrada.Removida;
                    await MarcarAsync(tabela, chave, depois, transacao, cancelamento).ConfigureAwait(false);
                    return true;
                }

                var comparacao = await CompararAsync(existente, tabela, chave, transacao, cancelamento).ConfigureAwait(false);
                if (comparacao != 1 || !guardada.Validada)
                {
                    depois = await Reconciliar(existente, tabela, chave, guardada, transacao, cancelamento).ConfigureAwait(false);
                    return true;
                }

                await transacao.SaveAsync("validar", cancelamento).ConfigureAwait(false);
                try
                {
                    await ExecutarAsync($"ALTER TABLE {CitacaoPostgreSql.Citar(tabela)} VALIDATE CONSTRAINT {CitacaoPostgreSql.Citar(chave)}", transacao, cancelamento).ConfigureAwait(false);
                }
                catch (PostgresException erro) when (DeIntegridade(erro))
                {
                    await transacao.RollbackAsync("validar", cancelamento).ConfigureAwait(false);
                    depois = SituacaoEntrada.SemValidacao;
                    return true;
                }

                depois = null;
                await MarcarAsync(tabela, chave, null, transacao, cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento).ConfigureAwait(false);
        return depois;
    }

    public override Task DescartarAsync(EntradaDiario entrada, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                await using var apagar = entrada.Tipo == TipoEntrada.Triggers
                    ? Comando(
                        "DELETE FROM incognito.restricao_removida r WHERE r.tipo = 'trigger' AND r.raiz_esquema = $1 AND r.raiz_tabela = $2",
                        transacao,
                        entrada.Tabela.Esquema,
                        entrada.Tabela.Nome)
                    : Comando(
                        "DELETE FROM incognito.restricao_removida r WHERE r.tipo = 'chave' AND r.esquema = $1 AND r.tabela = $2 AND r.nome = $3",
                        transacao,
                        entrada.Tabela.Esquema,
                        entrada.Tabela.Nome,
                        entrada.Nome ?? throw new ArgumentException("A entrada de chave sem o nome.", nameof(entrada)));
                await apagar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                return true;
            },
            cancelamento);
    }

    public override Task EncerrarAsync(Guid execucao, SituacaoExecucao situacao, DateTimeOffset encerradaEm, CancellationToken cancelamento)
    {
        var texto = situacao switch
        {
            SituacaoExecucao.Concluida => "concluida",
            SituacaoExecucao.Falhou => "falhou",
            SituacaoExecucao.Interrompida => "interrompida",
            _ => throw new ArgumentOutOfRangeException(nameof(situacao), "Só se encerra uma execução como concluída, falha ou interrompida."),
        };
        ExigirUtc(encerradaEm, nameof(encerradaEm));
        return NaTransacaoAsync(
            exigeDiario: true,
            async transacao =>
            {
                await using var encerrar = Comando(
                    "UPDATE incognito.execucao SET situacao = $2, encerrada_em = $3 WHERE id = $1 AND situacao = 'em-andamento'",
                    transacao,
                    execucao,
                    texto,
                    encerradaEm);
                return await encerrar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false) == 1
                    ? true
                    : throw Incoerente("A execução não está em andamento no diário do destino.");
            },
            cancelamento);
    }

    // O imposto de uma chave: a coluna do 18; antes, toda chave é imposta pela definição.
    private string Imposta => conexao.Versao >= 180000 ? "k.conenforced" : "true";

    private static ObjetoReservado Objeto(string nome, TipoObjetoReservado tipo) => new() { Nome = new NomeObjeto(Esquema, nome), Tipo = tipo };

    private static bool DeIntegridade(PostgresException erro) => erro.SqlState.StartsWith("23", StringComparison.Ordinal);

    private static void ExigirUtc(DateTimeOffset data, string parametro)
    {
        if (data.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A data do diário precisa estar em UTC.", parametro);
        }
    }

    private static (NomeObjeto Tabela, string Chave) Chave(EntradaDiario entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return entrada.Tipo == TipoEntrada.ChaveEstrangeira && !string.IsNullOrEmpty(entrada.Nome)
            ? (entrada.Tabela, entrada.Nome)
            : throw new ArgumentException("A entrada não é de uma chave estrangeira.", nameof(entrada));
    }

    private static DialetoRecusouException Inesperado() => new(
        CodigosPostgreSql.DiarioInesperado,
        "O diário do Incognito no destino não é o esperado (outro dono, escrita permitida a outro papel, estrutura ou versão desconhecida, gatilho, outro objeto no esquema, ou conteúdo fora da forma): o Incognito não o usa. Confira o esquema incognito do destino.");

    private static DialetoRecusouException Incoerente(string mensagem) => new(CodigosPostgreSql.EntradaIncoerente, mensagem);

    private static DialetoRecusouException SemAChave() =>
        Incoerente("A chave estrangeira não existe no destino nem está no diário, ou é a cópia de uma chave da tabela mãe.");

    private static DialetoRecusouException OutraDefinicao() => new(
        CodigosPostgreSql.OutraDefinicao,
        "Há no destino uma chave estrangeira com o nome da que o diário guarda, com outra definição: o Incognito não a troca. Remova uma das duas, ou descarte a entrada do diário.");

    // Uma transação do diário: o tempo-limite de espera, o lock conferido na sessão do bloqueio desta instância e, quando a
    // operação precisa do diário, a integridade dele. O tempo-limite estourado é falha com código, sem a mensagem do servidor.
    private async Task NaTransacaoAsync(bool exigeDiario, Func<NpgsqlTransaction, Task<bool>> trabalho, CancellationToken cancelamento)
    {
        lancarSeDescartado();
        var concessao = bloqueio.Viva;
        if (concessao is null || concessao.Perdida)
        {
            throw new InvalidOperationException("O diário só trabalha com a concessão viva do bloqueio desta instância do destino.");
        }

        var transacao = await conexao.Conexao.BeginTransactionAsync(cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            try
            {
                await ExecutarAsync("SET LOCAL lock_timeout = '5s'", transacao, cancelamento).ConfigureAwait(false);
                await using (var conferir = Comando(Bloqueio, transacao, concessao.Pid, BloqueioPostgreSql.Classe, BloqueioPostgreSql.Execucao))
                {
                    if (await conferir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
                    {
                        concessao.MarcarPerda();
                        throw new IncognitoException(CodigosPostgreSql.BloqueioPerdido, "O bloqueio de execução do destino se perdeu: o diário não foi tocado.");
                    }
                }

                if (exigeDiario)
                {
                    var (temEsquema, presentes, integro) = await IntegridadeAsync(transacao, cancelamento).ConfigureAwait(false);
                    if (!temEsquema || presentes < 2)
                    {
                        throw new DialetoRecusouException(CodigosPostgreSql.SemDiario, "O destino não tem o diário do Incognito: rode o preparar-destino.");
                    }

                    if (!integro)
                    {
                        throw Inesperado();
                    }

                    await ConferirVersaoAsync(transacao, cancelamento).ConfigureAwait(false);
                }

                await trabalho(transacao).ConfigureAwait(false);
                await transacao.CommitAsync(cancelamento).ConfigureAwait(false);
            }
            catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                throw new IncognitoException(CodigosPostgreSql.TempoDeBloqueio, "Outra sessão segurava uma tabela do destino, e o diário não esperou: nada da operação ficou.");
            }
        }
    }

    private async Task<(bool TemEsquema, int Presentes, bool Integro)> IntegridadeAsync(NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(Integridade, transacao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        return (leitor.GetBoolean(0), leitor.GetInt32(1), leitor.GetBoolean(2));
    }

    private async Task<List<string>> PapeisAsync(NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        var papeis = new List<string>();
        await using var comando = Comando(PapeisQueEscrevem, transacao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            papeis.Add(leitor.GetString(0));
        }

        return papeis;
    }

    // Uma versão do diário mais nova que a deste dialeto: o que ele guarda pode não ser o que este dialeto entende.
    private async Task ConferirVersaoAsync(NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var versao = Comando("SELECT EXISTS (SELECT FROM incognito.execucao e WHERE e.versao_diario > $1)", transacao, Versao);
        if (await versao.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not false)
        {
            throw Inesperado();
        }
    }

    // As execuções em andamento e as que têm entrada pendente, e as entradas na ordem de restauração.
    private async Task<EstadoDiario> LerEstadoAsync(NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await ConferirVersaoAsync(transacao, cancelamento).ConfigureAwait(false);

        var execucoes = new List<ExecucaoLida>();
        await using (var ler = Comando(
            """
            SELECT e.id, e.marcador, e.iniciada_em, e.situacao
            FROM incognito.execucao e
            WHERE e.situacao = 'em-andamento' OR e.id IN (SELECT r.execucao FROM incognito.restricao_removida r)
            ORDER BY e.iniciada_em, e.id
            """,
            transacao))
        {
            await using var leitor = await ler.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                execucoes.Add(new ExecucaoLida
                {
                    Id = leitor.GetGuid(0),
                    Marcador = leitor.GetGuid(1),
                    IniciadaEm = leitor.GetFieldValue<DateTimeOffset>(2),
                    Situacao = leitor.GetString(3) switch
                    {
                        "em-andamento" => SituacaoExecucao.EmAndamento,
                        "concluida" => SituacaoExecucao.Concluida,
                        "falhou" => SituacaoExecucao.Falhou,
                        "interrompida" => SituacaoExecucao.Interrompida,
                        _ => throw Inesperado(),
                    },
                });
            }
        }

        var triggers = new List<EntradaDiario>();
        var chaves = new List<EntradaDiario>();
        await using (var ler = Comando(
            """
            SELECT r.tipo, r.execucao, r.raiz_esquema, r.raiz_tabela, r.nome, r.situacao
            FROM incognito.restricao_removida r
            """,
            transacao))
        {
            await using var leitor = await ler.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            var raizes = new HashSet<NomeObjeto>();
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var tabela = new NomeObjeto(leitor.GetString(2), leitor.GetString(3));
                var situacao = leitor.GetString(5) switch
                {
                    "removida" => SituacaoEntrada.Removida,
                    "sem-validacao" => SituacaoEntrada.SemValidacao,
                    _ => throw Inesperado(),
                };
                switch (leitor.GetString(0))
                {
                    case "trigger" when raizes.Add(tabela):
                        triggers.Add(new EntradaDiario { Execucao = leitor.GetGuid(1), Tipo = TipoEntrada.Triggers, Tabela = tabela, Situacao = situacao });
                        break;
                    case "trigger":
                        break;
                    case "chave":
                        chaves.Add(new EntradaDiario { Execucao = leitor.GetGuid(1), Tipo = TipoEntrada.ChaveEstrangeira, Tabela = tabela, Nome = leitor.GetString(4), Situacao = situacao });
                        break;
                    default:
                        throw Inesperado();
                }
            }
        }

        return new EstadoDiario
        {
            Existe = true,
            Execucoes = execucoes,
            Pendentes =
            [
                .. triggers.OrderBy(entrada => entrada.Tabela, OrdemDeNomes.Instancia),
                .. chaves.OrderBy(entrada => entrada.Tabela, OrdemDeNomes.Instancia).ThenBy(entrada => entrada.Nome, StringComparer.Ordinal),
            ],
        };
    }

    private async Task ExigirEmAndamentoAsync(Guid execucao, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando("SELECT EXISTS (SELECT FROM incognito.execucao e WHERE e.id = $1 AND e.situacao = 'em-andamento')", transacao, execucao);
        if (await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
        {
            throw Incoerente("A execução não está em andamento no diário do destino.");
        }
    }

    private async Task<string?> SituacaoDaChaveAsync(NomeObjeto tabela, string chave, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(
            "SELECT r.situacao FROM incognito.restricao_removida r WHERE r.tipo = 'chave' AND r.esquema = $1 AND r.tabela = $2 AND r.nome = $3",
            transacao,
            tabela.Esquema,
            tabela.Nome,
            chave);
        return await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) as string;
    }

    // A entrada guardada de uma chave: a definição (que só entra no processo aqui, para a recriação) e o estado original.
    private async Task<(string Definicao, bool Validada)?> GuardadaAsync(NomeObjeto tabela, string chave, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(
            "SELECT r.definicao, r.validada FROM incognito.restricao_removida r WHERE r.tipo = 'chave' AND r.esquema = $1 AND r.tabela = $2 AND r.nome = $3",
            transacao,
            tabela.Esquema,
            tabela.Nome,
            chave);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            return null;
        }

        return leitor.IsDBNull(1) ? throw Inesperado() : (leitor.GetString(0), leitor.GetBoolean(1));
    }

    private async Task<(uint Oid, bool Particionada, bool DoLogin)?> TabelaAsync(NomeObjeto tabela, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(TabelaDoUsuario, transacao, tabela.Esquema, tabela.Nome);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        return await leitor.ReadAsync(cancelamento).ConfigureAwait(false)
            ? (leitor.GetFieldValue<uint>(0), leitor.GetBoolean(1), leitor.GetBoolean(2))
            : null;
    }

    private async Task<(uint Oid, NomeObjeto Referenciada)?> ChaveAsync(uint tabela, string chave, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(ChaveDaTabela, transacao, tabela, chave);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        return await leitor.ReadAsync(cancelamento).ConfigureAwait(false)
            ? (leitor.GetFieldValue<uint>(0), new NomeObjeto(leitor.GetString(1), leitor.GetString(2)))
            : null;
    }

    private async Task<int> CompararAsync(uint chave, NomeObjeto tabela, string nome, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(CompararComOGuardado, transacao, chave, tabela.Esquema, tabela.Nome, nome);
        return await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is int resultado ? resultado : throw Inesperado();
    }

    // A chave que já existe com o nome guardado: igual à guardada, a entrada sai; igual sem validação, falta validar (se o
    // original era validado); com outra definição, nada muda.
    private async Task<SituacaoEntrada?> Reconciliar(
        uint existente, NomeObjeto tabela, string chave, (string Definicao, bool Validada) guardada, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        SituacaoEntrada? depois = await CompararAsync(existente, tabela, chave, transacao, cancelamento).ConfigureAwait(false) switch
        {
            0 => null,
            1 when guardada.Validada => SituacaoEntrada.SemValidacao,
            _ => throw OutraDefinicao(),
        };
        await MarcarAsync(tabela, chave, depois, transacao, cancelamento).ConfigureAwait(false);
        return depois;
    }

    // A entrada de uma chave depois da operação: apagada (nula) ou com a situação nova.
    private async Task MarcarAsync(NomeObjeto tabela, string chave, SituacaoEntrada? situacao, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = situacao is null
            ? Comando("DELETE FROM incognito.restricao_removida r WHERE r.tipo = 'chave' AND r.esquema = $1 AND r.tabela = $2 AND r.nome = $3", transacao, tabela.Esquema, tabela.Nome, chave)
            : Comando(
                "UPDATE incognito.restricao_removida SET situacao = $4 WHERE tipo = 'chave' AND esquema = $1 AND tabela = $2 AND nome = $3",
                transacao,
                tabela.Esquema,
                tabela.Nome,
                chave,
                situacao == SituacaoEntrada.SemValidacao ? "sem-validacao" : "removida");
        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    private async Task AdotarAsync(Guid execucao, NomeObjeto tabela, string chave, string situacao, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(
            "UPDATE incognito.restricao_removida SET execucao = $4, situacao = $5 WHERE tipo = 'chave' AND esquema = $1 AND tabela = $2 AND nome = $3",
            transacao,
            tabela.Esquema,
            tabela.Nome,
            chave,
            execucao,
            situacao);
        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    // As tabelas físicas de uma árvore (a raiz e as partições comuns e particionadas), com o nome e se têm trigger de usuário.
    private async Task<List<(uint Oid, NomeObjeto Nome, bool ComTriggers)>> TabelasFisicasAsync(uint raiz, bool particionada, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        var arvore = particionada
            ? "SELECT a.relid FROM pg_catalog.pg_partition_tree($1::pg_catalog.oid::pg_catalog.regclass) a"
            : "SELECT $1::pg_catalog.oid";
        var fisicas = new List<(uint, NomeObjeto, bool)>();
        await using var comando = Comando(
            $"""
            SELECT c.oid, n.nspname, c.relname, EXISTS (SELECT FROM pg_catalog.pg_trigger t WHERE t.tgrelid = c.oid AND NOT t.tgisinternal)
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.oid IN ({arvore}) AND c.relkind IN ('r', 'p')
            """,
            transacao,
            raiz);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            fisicas.Add((leitor.GetFieldValue<uint>(0), new NomeObjeto(leitor.GetString(1), leitor.GetString(2)), leitor.GetBoolean(3)));
        }

        return fisicas;
    }

    private async Task<bool> TriggerExisteAsync(NomeObjeto tabela, string nome, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = Comando(
            """
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_trigger t
                JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = $1 AND c.relname = $2 AND t.tgname = $3 AND NOT t.tgisinternal
                  AND n.nspname NOT IN ('incognito', 'information_schema') AND n.nspname NOT LIKE 'pg\_%')
            """,
            transacao,
            tabela.Esquema,
            tabela.Nome,
            nome);
        return await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is true;
    }

    private async Task ExecutarAsync(string sql, NpgsqlTransaction transacao, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand(sql, conexao.Conexao, transacao);
        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    // Um comando com parâmetros posicionais ($1, $2...): o texto nunca é reescrito pelo driver.
    private NpgsqlCommand Comando(string sql, NpgsqlTransaction transacao, params object[] valores)
    {
        var comando = new NpgsqlCommand(sql, conexao.Conexao, transacao);
        foreach (var valor in valores)
        {
            comando.Parameters.Add(valor switch
            {
                uint oid => new NpgsqlParameter { Value = oid, NpgsqlDbType = NpgsqlDbType.Oid },
                uint[] oids => new NpgsqlParameter { Value = oids, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Oid },
                _ => new NpgsqlParameter { Value = valor },
            });
        }

        return comando;
    }
}

/// <summary>
/// A forma de uma definição de chave estrangeira como o <c>pg_get_constraintdef</c> a escreve com o caminho de busca só no
/// <c>pg_catalog</c> (a tabela referenciada sempre com o esquema): <c>FOREIGN KEY (colunas) REFERENCES esquema.tabela(colunas)</c>,
/// seguida, nesta ordem e cada uma no máximo uma vez, de <c>MATCH FULL</c>, <c>ON UPDATE</c> e <c>ON DELETE</c> (com a ação, e
/// as colunas do <c>SET NULL</c> e do <c>SET DEFAULT</c> no <c>ON DELETE</c>), <c>DEFERRABLE</c>, <c>INITIALLY DEFERRED</c>,
/// <c>NOT ENFORCED</c> e <c>NOT VALID</c>; <c>PERIOD</c> antes da última coluna das duas listas. Os nomes, sem aspas só em
/// minúsculas, dígitos e sublinhado; com aspas, qualquer caractere, com a aspa dobrada.
/// </summary>
/// <remarks>
/// Uma definição guardada no diário é texto do destino, que outro papel pode ter mudado: fora desta forma, o diário não a
/// executa. Nenhuma vírgula fora de parênteses (o <c>ALTER TABLE</c> aceitaria outra ação depois da chave), nenhum comentário,
/// aspa simples, cifrão nem ponto e vírgula fora de um nome entre aspas.
/// </remarks>
internal static class FormatoDaDefinicao
{
    private static readonly string[] _acoes = ["RESTRICT", "CASCADE"];

    /// <summary>Se a definição tem a forma de uma chave estrangeira.</summary>
    /// <param name="definicao">A definição guardada.</param>
    /// <returns>Se tem.</returns>
    internal static bool Valida(string definicao)
    {
        if (!Separar(definicao, out var pedacos))
        {
            return false;
        }

        var leitor = new Leitor(pedacos);
        if (!leitor.Palavras("FOREIGN", "KEY") || !Colunas(leitor) || !leitor.Palavras("REFERENCES") || !leitor.Nome() || !leitor.Simbolo(".") || !leitor.Nome() || !Colunas(leitor))
        {
            return false;
        }

        if (leitor.Proxima("MATCH") && !leitor.Palavras("MATCH", "FULL"))
        {
            return false;
        }

        if (leitor.Proxima("ON", "UPDATE") && !(leitor.Palavras("ON", "UPDATE") && Acao(leitor, comColunas: false)))
        {
            return false;
        }

        if (leitor.Proxima("ON", "DELETE") && !(leitor.Palavras("ON", "DELETE") && Acao(leitor, comColunas: true)))
        {
            return false;
        }

        foreach (var opcional in new[] { new[] { "DEFERRABLE" }, ["INITIALLY", "DEFERRED"], ["NOT", "ENFORCED"], ["NOT", "VALID"] })
        {
            if (leitor.Proxima(opcional))
            {
                leitor.Palavras(opcional);
            }
        }

        return leitor.Fim;
    }

    // ( [PERIOD] nome {, [PERIOD] nome} ), com o PERIOD só na última.
    private static bool Colunas(Leitor leitor)
    {
        if (!leitor.Simbolo("("))
        {
            return false;
        }

        while (true)
        {
            var periodo = leitor.Proxima("PERIOD") && leitor.Palavras("PERIOD");
            if (!leitor.Nome())
            {
                return false;
            }

            if (leitor.Simbolo(")"))
            {
                return true;
            }

            if (periodo || !leitor.Simbolo(","))
            {
                return false;
            }
        }
    }

    private static bool Acao(Leitor leitor, bool comColunas)
    {
        if (_acoes.Any(acao => leitor.Proxima(acao)))
        {
            return leitor.Palavra();
        }

        if (!leitor.Palavras("SET") || !(leitor.Proxima("NULL") || leitor.Proxima("DEFAULT")) || !leitor.Palavra())
        {
            return false;
        }

        if (!comColunas || !leitor.ProximoSimbolo("("))
        {
            return true;
        }

        leitor.Simbolo("(");
        while (true)
        {
            if (!leitor.Nome())
            {
                return false;
            }

            if (leitor.Simbolo(")"))
            {
                return true;
            }

            if (!leitor.Simbolo(","))
            {
                return false;
            }
        }
    }

    // Os pedaços: palavras-chave (só maiúsculas), nomes (sem aspas, em minúsculas; ou entre aspas, já sem elas) e os
    // símbolos ( ) , e ponto, separados por espaço. Qualquer outro caractere fora das aspas recusa.
    private static bool Separar(string definicao, out List<(char Tipo, string Texto)> pedacos)
    {
        pedacos = [];
        var i = 0;
        while (i < definicao.Length)
        {
            var c = definicao[i];
            if (c == ' ')
            {
                i++;
            }
            else if (c is '(' or ')' or ',' or '.')
            {
                pedacos.Add(('s', c.ToString()));
                i++;
            }
            else if (c is >= 'A' and <= 'Z')
            {
                var inicio = i;
                while (i < definicao.Length && definicao[i] is >= 'A' and <= 'Z')
                {
                    i++;
                }

                pedacos.Add(('p', definicao[inicio..i]));
            }
            else if (c is >= 'a' and <= 'z' or '_')
            {
                var inicio = i;
                while (i < definicao.Length && definicao[i] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                {
                    i++;
                }

                pedacos.Add(('n', definicao[inicio..i]));
            }
            else if (c == '"')
            {
                var nome = new System.Text.StringBuilder();
                i++;
                while (true)
                {
                    if (i >= definicao.Length)
                    {
                        return false;
                    }

                    if (definicao[i] == '"')
                    {
                        if (i + 1 < definicao.Length && definicao[i + 1] == '"')
                        {
                            nome.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    if (definicao[i] == '\0')
                    {
                        return false;
                    }

                    nome.Append(definicao[i]);
                    i++;
                }

                if (nome.Length == 0)
                {
                    return false;
                }

                pedacos.Add(('n', nome.ToString()));
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Leitor(List<(char Tipo, string Texto)> pedacos)
    {
        private int _posicao;

        internal bool Fim => _posicao == pedacos.Count;

        internal bool Proxima(params string[] palavras) =>
            palavras.Select((palavra, deslocamento) => _posicao + deslocamento < pedacos.Count && pedacos[_posicao + deslocamento] is ('p', var texto) && texto == palavra).All(sim => sim);

        internal bool ProximoSimbolo(string simbolo) => _posicao < pedacos.Count && pedacos[_posicao] is ('s', var texto) && texto == simbolo;

        internal bool Palavras(params string[] palavras)
        {
            if (!Proxima(palavras))
            {
                return false;
            }

            _posicao += palavras.Length;
            return true;
        }

        internal bool Palavra()
        {
            if (_posicao >= pedacos.Count || pedacos[_posicao].Tipo != 'p')
            {
                return false;
            }

            _posicao++;
            return true;
        }

        internal bool Nome()
        {
            if (_posicao >= pedacos.Count || pedacos[_posicao].Tipo != 'n')
            {
                return false;
            }

            _posicao++;
            return true;
        }

        internal bool Simbolo(string simbolo)
        {
            if (!ProximoSimbolo(simbolo))
            {
                return false;
            }

            _posicao++;
            return true;
        }
    }
}
