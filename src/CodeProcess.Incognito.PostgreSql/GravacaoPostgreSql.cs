using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A gravação das tabelas do destino no PostgreSQL: cada chamada numa conexão própria pela mesma fonte da de controle
/// (<see cref="ConexaoPostgreSql.AbrirParaGravacaoAsync"/>). A gravação esvazia e grava numa transação só, com o
/// <c>COPY FREEZE</c> fora da particionada: sem a conclusão, a tabela fica como estava.
/// </summary>
internal sealed class GravadorPostgreSql(ConexaoPostgreSql controle, Action lancarSeDescartado) : GravadorEmLote
{
    // A tabela pelo nome: se é particionada (para o ONLY: a particionada é esvaziada com as partições; as outras, sem as
    // herdeiras) e se a segurança por linha vale para o login (o COPY FROM a recusa): ligada, e o login não é dono (ou ela é
    // forçada), nem superusuário, nem a contorna.
    private const string FormaDaTabela = """
        SELECT c.relkind = 'p',
               c.relrowsecurity AND (c.relforcerowsecurity OR NOT pg_catalog.pg_has_role(c.relowner, 'USAGE'))
                   AND NOT (SELECT r.rolsuper OR r.rolbypassrls FROM pg_catalog.pg_roles r WHERE r.rolname = current_user)
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relname = $2 AND c.relkind IN ('r', 'p')
        """;

    // Uma partição estrangeira na árvore da particionada: o TRUNCATE e o COPY chegariam a outro banco. Conferida depois do
    // TRUNCATE, com a árvore travada, e antes de qualquer confirmação.
    private const string ParticaoEstrangeira = """
        SELECT EXISTS (
            SELECT 1
            FROM pg_catalog.pg_partition_tree(pg_catalog.to_regclass($1)) t
            JOIN pg_catalog.pg_class c ON c.oid = t.relid
            WHERE c.relkind = 'f')
        """;

    // A sessão como foi aberta: de escrita, com o texto do COPY, o caminho de busca e o papel impostos.
    private const string SessaoIntacta = "SELECT pg_catalog.current_setting('transaction_read_only') = 'off' AND " + ConexaoPostgreSql.ConferenciaDoTexto;

    // O pool dos buffers da gravação, só deste destino: os valores escritos passam por ele, limpos a cada devolução.
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Create(maxArrayLength: 4 * TextoParaOCopy.Bloco, maxArraysPerBucket: 64);
    private readonly ConcurrentDictionary<GravacaoPostgreSql, byte> _vivas = new();
    private int _fechado;

    /// <summary>Gancho dos testes do dialeto, entre o fim do COPY de uma gravação e a conferência da sessão.</summary>
    internal Func<NpgsqlConnection, CancellationToken, Task>? AntesDaConclusao { get; set; }

    /// <summary>Gancho dos testes do dialeto: o fluxo do COPY de uma gravação logo antes do cancelamento no descarte.</summary>
    internal Action<NpgsqlRawCopyStream>? AntesDoCancelamento { get; set; }

    /// <summary>Fecha as gravações vivas (o descarte do dialeto); as abertas depois disso são fechadas na hora.</summary>
    /// <returns>A tarefa.</returns>
    internal async ValueTask FecharAsync()
    {
        Volatile.Write(ref _fechado, 1);
        foreach (var gravacao in _vivas.Keys.ToArray())
        {
            await gravacao.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected override async Task EsvaziarAsyncCore(NomeObjeto tabela, CancellationToken cancelamento)
    {
        lancarSeDescartado();
        NpgsqlConnection? conexao = null;
        try
        {
            // A abertura também traduzida: o servidor que recusa mais conexões responde já no pacote de início.
            conexao = await controle.AbrirParaGravacaoAsync(cancelamento).ConfigureAwait(false);
            await using var transacao = await conexao.BeginTransactionAsync(cancelamento).ConfigureAwait(false);
            await PrepararAsync(conexao, tabela, cancelamento).ConfigureAwait(false);

            // Com o token: o servidor confirma o COMMIT que já recebeu; se a conexão cair antes da resposta, a dúvida fica com
            // o diário.
            await transacao.CommitAsync(cancelamento).ConfigureAwait(false);
        }
        catch (Exception erro) when (erro is not IncognitoException)
        {
            throw ErrosDaGravacao.Traduzir(erro, tabela.ToString(), cancelamento);
        }
        finally
        {
            await Descarte.DepoisDaFalhaAsync(conexao).ConfigureAwait(false);
        }
    }

    protected override async Task<GravacaoTabela> AbrirAsyncCore(PedidoGravacao pedido, CancellationToken cancelamento)
    {
        lancarSeDescartado();
        NpgsqlConnection? conexao = null;
        try
        {
            conexao = await controle.AbrirParaGravacaoAsync(cancelamento).ConfigureAwait(false);
            var transacao = await conexao.BeginTransactionAsync(cancelamento).ConfigureAwait(false);
            var particionada = await PrepararAsync(conexao, pedido.Tabela, cancelamento).ConfigureAwait(false);

            // O início do COPY vai sem o token: o cancelamento antes da primeira resposta deixaria a conexão inutilizável. O
            // FREEZE vale porque o TRUNCATE foi na mesma transação; a particionada não o aceita. O COPY FROM numa mãe de herança
            // grava só nela (o ONLY é do TRUNCATE).
            cancelamento.ThrowIfCancellationRequested();
            var colunas = string.Join(", ", pedido.Colunas.Select(CitacaoPostgreSql.Citar));
            var importacao = await conexao.BeginTextImportAsync(
                $"COPY {CitacaoPostgreSql.Citar(pedido.Tabela)} ({colunas}) FROM STDIN" + (particionada ? "" : " (FREEZE)"), CancellationToken.None).ConfigureAwait(false);
            var gravacao = new GravacaoPostgreSql(
                conexao, transacao, importacao, _pool, pedido.Tabela.ToString(), AntesDaConclusao, AntesDoCancelamento, viva => _vivas.TryRemove(viva, out _));
            _vivas[gravacao] = 0;
            if (Volatile.Read(ref _fechado) != 0)
            {
                await gravacao.DisposeAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(DialetoBanco));
            }

            return gravacao;
        }
        catch (Exception erro)
        {
            // Fechar a conexão desfaz a transação, com o esvaziamento.
            await Descarte.DepoisDaFalhaAsync(conexao).ConfigureAwait(false);
            if (erro is IncognitoException or ObjectDisposedException)
            {
                throw;
            }

            throw ErrosDaGravacao.Traduzir(erro, pedido.Tabela.ToString(), cancelamento);
        }
    }

    // Na transação aberta: o tempo da trava, a sessão conferida, a forma da tabela, o esvaziamento e, na particionada, a
    // árvore sem partição estrangeira. Devolve se é particionada.
    private static async Task<bool> PrepararAsync(NpgsqlConnection conexao, NomeObjeto tabela, CancellationToken cancelamento)
    {
        await SessaoLeituraPostgreSql.ExecutarAsync(conexao, "SET LOCAL lock_timeout = '5s'", cancelamento).ConfigureAwait(false);
        await using (var sessao = new NpgsqlCommand(SessaoIntacta, conexao))
        {
            if (await sessao.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                throw ErrosDaLeitura.TextoTrocado();
            }
        }

        bool particionada;
        await using (var forma = new NpgsqlCommand(FormaDaTabela, conexao))
        {
            forma.Parameters.Add(new NpgsqlParameter { Value = tabela.Esquema, NpgsqlDbType = NpgsqlDbType.Text });
            forma.Parameters.Add(new NpgsqlParameter { Value = tabela.Nome, NpgsqlDbType = NpgsqlDbType.Text });
            await using var leitor = await forma.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                throw new DialetoRecusouException(
                    CodigosPostgreSql.TabelaInexistente,
                    $"A tabela {tabela} do destino não existe, ou não é uma tabela comum ou particionada: nada foi gravado.");
            }

            particionada = leitor.GetBoolean(0);
            if (leitor.GetBoolean(1))
            {
                throw ErrosDaGravacao.Recusada(tabela.ToString());
            }
        }

        await SessaoLeituraPostgreSql.ExecutarAsync(conexao, $"TRUNCATE {(particionada ? "" : "ONLY ")}{CitacaoPostgreSql.Citar(tabela)}", cancelamento).ConfigureAwait(false);
        if (particionada)
        {
            await using var estrangeira = new NpgsqlCommand(ParticaoEstrangeira, conexao);
            estrangeira.Parameters.Add(new NpgsqlParameter { Value = CitacaoPostgreSql.Citar(tabela), NpgsqlDbType = NpgsqlDbType.Text });
            if (await estrangeira.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not false)
            {
                throw ErrosDaGravacao.NaoAceita(tabela.ToString());
            }
        }

        return particionada;
    }
}

/// <summary>
/// A gravação de uma tabela no PostgreSQL: o <c>COPY ... FROM STDIN</c> em texto, escrito em bytes crus pela
/// <see cref="StreamWriter.BaseStream"/>, na transação que esvaziou a tabela. As linhas sem célula escrita vão em bytes,
/// como vieram; nas outras, as células escritas são formatadas e o resto vai como veio. O token do chamador nunca vai ao
/// fluxo do <c>COPY</c>: o cancelamento é conferido entre os blocos e, se o servidor não responde, a gravação desiste da
/// conexão depois de <see cref="LeituraTabelaPostgreSql.PrazoDoCancelamento"/>, também na conclusão e no descarte.
/// </summary>
internal sealed class GravacaoPostgreSql : GravacaoTabela
{
    // A folga do descarte além do prazo do fluxo, para o cancelamento nunca correr junto com o fechamento da conexão.
    private static readonly TimeSpan _folgaDoDescarte = TimeSpan.FromSeconds(5);

    // O texto do COPY, o caminho de busca e o papel ainda como impostos, depois do COPY.
    private const string TextoIntacto = "SELECT " + ConexaoPostgreSql.ConferenciaDoTexto;

    private readonly NpgsqlConnection _conexao;
    private readonly NpgsqlTransaction _transacao;
    private readonly NpgsqlRawCopyStream _fluxo;
    private readonly TextoParaOCopy _texto;
    private readonly string _tabela;
    private readonly Func<NpgsqlConnection, CancellationToken, Task>? _antesDaConclusao;
    private readonly Action<NpgsqlRawCopyStream>? _antesDoCancelamento;
    private readonly Action<GravacaoPostgreSql> _aoDescartar;
    private Task? _fechamento;
    private bool _importacaoFechada;
    private bool _abandonada;
    private bool _concluida;

    internal GravacaoPostgreSql(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        TextWriter importacao,
        ArrayPool<byte> pool,
        string tabela,
        Func<NpgsqlConnection, CancellationToken, Task>? antesDaConclusao,
        Action<NpgsqlRawCopyStream>? antesDoCancelamento,
        Action<GravacaoPostgreSql> aoDescartar)
    {
        _conexao = conexao;
        _transacao = transacao;
        _fluxo = (NpgsqlRawCopyStream)((StreamWriter)importacao).BaseStream;
        _texto = new TextoParaOCopy(pool);
        _tabela = tabela;
        _antesDaConclusao = antesDaConclusao;
        _antesDoCancelamento = antesDoCancelamento;
        _aoDescartar = aoDescartar;
    }

    protected override async Task GravarAsyncCore(LoteLinhas lote, CancellationToken cancelamento)
    {
        if (lote is not LoteLinhasPostgreSql linhas)
        {
            throw new InvalidOperationException("O lote não foi lido pelo PostgreSQL: só um lote do mesmo dialeto se grava.");
        }

        try
        {
            using var desistir = new CancellationTokenSource();
            using var registro = cancelamento.Register(static estado => ((CancellationTokenSource)estado!).CancelAfter(LeituraTabelaPostgreSql.PrazoDoCancelamento), desistir);
            for (var linha = 0; linha < linhas.Quantidade; linha++)
            {
                if (!linhas.TemEscrita(linha))
                {
                    _texto.Acrescentar(linhas.Linha(linha));
                }
                else
                {
                    for (var coluna = 0; coluna < linhas.Colunas; coluna++)
                    {
                        if (coluna > 0)
                        {
                            _texto.Acrescentar((byte)'\t');
                        }

                        if (linhas.Escrito(linha, coluna, out var valor))
                        {
                            _texto.AcrescentarValor(valor);
                        }
                        else
                        {
                            _texto.Acrescentar(linhas.CampoBruto(linha, coluna));
                        }
                    }
                }

                _texto.Acrescentar((byte)'\n');
                if (_texto.Usados >= TextoParaOCopy.Bloco)
                {
                    await EnviarAsync(cancelamento, desistir.Token).ConfigureAwait(false);
                }
            }

            await EnviarAsync(cancelamento, desistir.Token).ConfigureAwait(false);
        }
        catch (Exception erro) when (erro is not IncognitoException)
        {
            throw ErrosDaGravacao.Traduzir(erro, _tabela, cancelamento);
        }
        finally
        {
            _texto.Esvaziar();
        }
    }

    protected override async Task ConcluirAsyncCore(CancellationToken cancelamento)
    {
        try
        {
            // Fechar o fluxo conclui o COPY (as violações do destino aparecem aqui). O Npgsql não dá token a esse fechamento:
            // cancelado o chamador e sem resposta no prazo, a gravação abandona a conexão sem confirmar nada. Pelo fluxo, e não
            // pelo escritor de texto: o DisposeAsync do escritor é o do TextWriter, que fecha de forma síncrona e prenderia a
            // thread com o servidor parado, antes de o prazo começar.
            cancelamento.ThrowIfCancellationRequested();
            _importacaoFechada = true;
            _fechamento = _fluxo.DisposeAsync().AsTask();
            await EsperarAsync(_fechamento, cancelamento).ConfigureAwait(false);
            if (_antesDaConclusao is not null)
            {
                await _antesDaConclusao(_conexao, cancelamento).ConfigureAwait(false);
            }

            // As restrições adiadas falham agora, e não no COMMIT; depois, a sessão conferida (código do banco pode ter
            // trocado o texto do COPY no meio da gravação).
            await SessaoLeituraPostgreSql.ExecutarAsync(_conexao, "SET CONSTRAINTS ALL IMMEDIATE", cancelamento).ConfigureAwait(false);
            await using (var conferir = new NpgsqlCommand(TextoIntacto, _conexao))
            {
                if (await conferir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
                {
                    throw ErrosDaLeitura.TextoTrocado();
                }
            }

            // Com o token: o servidor confirma o COMMIT que já recebeu (preso na réplica síncrona, ele espera o
            // cancelamento); se a conexão cair antes da resposta, a dúvida fica com o diário.
            await _transacao.CommitAsync(cancelamento).ConfigureAwait(false);
            _concluida = true;
        }
        catch (Exception erro) when (erro is not IncognitoException)
        {
            throw ErrosDaGravacao.Traduzir(erro, _tabela, cancelamento);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A conexão abandonada fecha quando o fechamento preso termina, como quer que termine; nada mais a observa.")]
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            if (_abandonada)
            {
                // A conexão está presa no fechamento do COPY: só se fecha quando ele terminar, sem duas operações nela ao mesmo
                // tempo (o servidor desfaz a transação ao perdê-la).
                _ = _fechamento!.ContinueWith(
                    static (fechamento, conexao) =>
                    {
                        _ = fechamento.Exception;
                        try
                        {
                            ((NpgsqlConnection)conexao!).Dispose();
                        }
                        catch
                        {
                        }
                    },
                    _conexao,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                return;
            }

            if (!_concluida)
            {
                // Sem a conclusão: pede ao servidor que descarte o COPY e desfaz, tudo com prazo; sem resposta, só fecha a
                // conexão (o servidor desfaz a transação ao perdê-la). O fluxo ganha o prazo antes: com o tempo-limite de
                // comando desligado, o cancelamento e o fechamento da conexão esperariam o servidor para sempre.
                using var prazo = new CancellationTokenSource(LeituraTabelaPostgreSql.PrazoDoCancelamento + _folgaDoDescarte);
                var respondeu = true;
                if (!_importacaoFechada)
                {
                    _fluxo.ReadTimeout = _fluxo.WriteTimeout = (int)LeituraTabelaPostgreSql.PrazoDoCancelamento.TotalMilliseconds;
                    _antesDoCancelamento?.Invoke(_fluxo);
                    respondeu = await CancelarAsync(prazo.Token).ConfigureAwait(false);
                }

                if (respondeu)
                {
                    try
                    {
                        await _transacao.RollbackAsync(prazo.Token).ConfigureAwait(false);
                    }
                    catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or OperationCanceledException or ObjectDisposedException or IOException)
                    {
                    }
                }
            }
        }
        finally
        {
            _texto.Devolver();
            if (!_abandonada)
            {
                await Descarte.DepoisDaFalhaAsync(_conexao).ConfigureAwait(false);
            }

            _aoDescartar(this);
        }
    }

    // Uma operação do Npgsql sem token: cancelado o chamador, espera mais o prazo e então abandona a conexão.
    private async Task EsperarAsync(Task operacao, CancellationToken cancelamento)
    {
        using var desistir = new CancellationTokenSource();
        using var registro = cancelamento.Register(static estado => ((CancellationTokenSource)estado!).CancelAfter(LeituraTabelaPostgreSql.PrazoDoCancelamento), desistir);
        try
        {
            await operacao.WaitAsync(desistir.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (desistir.IsCancellationRequested && !operacao.IsCompleted)
        {
            _abandonada = true;
            throw new OperationCanceledException(cancelamento);
        }
    }

    // O bloco formatado vai ao servidor; o token só desiste da conexão, depois do prazo do cancelamento.
    private async Task EnviarAsync(CancellationToken cancelamento, CancellationToken desistir)
    {
        cancelamento.ThrowIfCancellationRequested();
        if (_texto.Usados > 0)
        {
            await _fluxo.WriteAsync(_texto.Escrito, desistir).ConfigureAwait(false);
            _texto.Esvaziar();
        }
    }

    // O Cancel do Npgsql é síncrono e espera a rede: numa tarefa à parte, com prazo; nunca lança.
    private async Task<bool> CancelarAsync(CancellationToken prazo)
    {
        try
        {
            await Task.Run(_fluxo.Cancel, CancellationToken.None).WaitAsync(prazo).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or ObjectDisposedException or IOException)
        {
            return true;
        }
    }
}

/// <summary>
/// O texto de um bloco de linhas para o <c>COPY ... FROM STDIN</c>, num buffer do pool que cresce e é limpo no trecho usado a
/// cada envio e na devolução. Os valores escritos saem com os parâmetros impostos (<see cref="ConexaoPostgreSql.OpcoesDoTexto"/>):
/// a data em ISO, o <c>timestamptz</c> com <c>+00</c>, o <c>bytea</c> em hexadecimal, NULL como <c>\N</c>, e a barra, a
/// tabulação e as quebras escapadas.
/// </summary>
[DebuggerDisplay("TextoParaOCopy ({Usados} bytes)")]
internal sealed class TextoParaOCopy(ArrayPool<byte> pool)
{
    /// <summary>Quanto juntar antes de enviar ao servidor.</summary>
    internal const int Bloco = 64 * 1024;

    private static readonly SearchValues<char> _escapadas = SearchValues.Create("\\\t\n\r");

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private byte[] _buffer = pool.Rent(Bloco);
    private int _usados;

    /// <summary>Quantos bytes o bloco tem.</summary>
    internal int Usados => _usados;

    /// <summary>Os bytes do bloco.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal ReadOnlyMemory<byte> Escrito => _buffer.AsMemory(0, _usados);

    /// <summary>Acrescenta bytes como vieram.</summary>
    /// <param name="bytes">Os bytes.</param>
    internal void Acrescentar(ReadOnlySpan<byte> bytes)
    {
        Garantir(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_usados));
        _usados += bytes.Length;
    }

    /// <summary>Acrescenta um byte.</summary>
    /// <param name="valor">O byte.</param>
    internal void Acrescentar(byte valor)
    {
        Garantir(1);
        _buffer[_usados++] = valor;
    }

    /// <summary>Acrescenta um valor na forma canônica, formatado como o <c>COPY</c> o lê.</summary>
    /// <param name="valor">O valor, ou nulo para NULL.</param>
    /// <exception cref="InvalidOperationException">Um tipo fora da forma canônica (a base já o recusa na escrita).</exception>
    internal void AcrescentarValor(object? valor)
    {
        switch (valor)
        {
            case null:
                Acrescentar("\\N"u8);
                break;
            case string texto:
                AcrescentarTexto(texto);
                break;
            case byte[] bytes:
                AcrescentarBinario(bytes);
                break;
            case bool logico:
                Acrescentar(logico ? (byte)'t' : (byte)'f');
                break;
            case DateTime dataHora:
                AcrescentarDataHora(dataHora);
                break;
            case DateTimeOffset comFuso:
                AcrescentarDataHora(comFuso.DateTime);
                Acrescentar("+00"u8);
                break;
            case IUtf8SpanFormattable formatavel when valor is long or decimal or DateOnly or Guid:
                Formatar(formatavel, valor is DateOnly ? "yyyy-MM-dd" : valor is Guid ? "D" : default);
                break;
            default:
                throw new InvalidOperationException("Um valor escrito está fora da forma canônica: a gravação não o formata.");
        }
    }

    /// <summary>Limpa o trecho usado e volta ao começo.</summary>
    internal void Esvaziar()
    {
        _buffer.AsSpan(0, _usados).Clear();
        _usados = 0;
    }

    /// <summary>Limpa e devolve o buffer, uma vez.</summary>
    internal void Devolver()
    {
        if (_buffer.Length > 0)
        {
            Buffers.Devolver(pool, _buffer, _usados);
            _buffer = [];
            _usados = 0;
        }
    }

    // O texto em UTF-8, com a barra, a tabulação e as quebras escapadas (o resto vai cru: o COPY só lê esses).
    private void AcrescentarTexto(string texto)
    {
        var resto = texto.AsSpan();
        while (!resto.IsEmpty)
        {
            var especial = resto.IndexOfAny(_escapadas);
            var trecho = especial < 0 ? resto : resto[..especial];
            if (!trecho.IsEmpty)
            {
                Garantir(Encoding.UTF8.GetMaxByteCount(trecho.Length));
                _usados += Encoding.UTF8.GetBytes(trecho, _buffer.AsSpan(_usados));
            }

            if (especial < 0)
            {
                break;
            }

            Acrescentar((byte)'\\');
            Acrescentar(resto[especial] switch
            {
                '\\' => (byte)'\\',
                '\t' => (byte)'t',
                '\n' => (byte)'n',
                _ => (byte)'r',
            });
            resto = resto[(especial + 1)..];
        }
    }

    // O bytea em hexadecimal; a barra do \x é escapada no texto do COPY.
    private void AcrescentarBinario(byte[] bytes)
    {
        Acrescentar("\\\\x"u8);
        Garantir(checked(bytes.Length * 2));
        foreach (var valor in bytes)
        {
            _buffer[_usados++] = Hexa(valor >> 4);
            _buffer[_usados++] = Hexa(valor & 0xF);
        }
    }

    // A data e hora com os microssegundos que o PostgreSQL guarda; a fração só quando há.
    private void AcrescentarDataHora(DateTime dataHora)
    {
        Formatar(dataHora, "yyyy-MM-dd HH:mm:ss");
        var fracao = dataHora.Ticks % TimeSpan.TicksPerSecond / TimeSpan.TicksPerMicrosecond;
        if (fracao > 0)
        {
            Acrescentar((byte)'.');
            Formatar(fracao, "D6");
        }
    }

    private void Formatar(IUtf8SpanFormattable valor, ReadOnlySpan<char> formato)
    {
        Garantir(64);
        if (!valor.TryFormat(_buffer.AsSpan(_usados), out var escritos, formato, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Um valor escrito não coube no texto do COPY.");
        }

        _usados += escritos;
    }

    private void Garantir(int mais)
    {
        var preciso = checked(_usados + mais);
        if (preciso <= _buffer.Length)
        {
            return;
        }

        var maior = pool.Rent(Math.Max(preciso, _buffer.Length * 2));
        _buffer.AsSpan(0, _usados).CopyTo(maior);
        Buffers.Devolver(pool, _buffer, _usados);
        _buffer = maior;
    }

    private static byte Hexa(int valor) => (byte)(valor < 10 ? '0' + valor : 'a' + valor - 10);
}

/// <summary>A tradução dos erros da gravação em códigos do dialeto, só pelo SQLSTATE e pelos nomes do erro, nunca pelo texto do servidor.</summary>
internal static class ErrosDaGravacao
{
    /// <summary>A falha traduzida; o cancelamento pedido pelo chamador sai como cancelamento novo.</summary>
    /// <param name="erro">A falha.</param>
    /// <param name="tabela">A tabela, como o catálogo a guarda.</param>
    /// <param name="cancelamento">O token do chamador.</param>
    /// <returns>A falha traduzida.</returns>
    internal static Exception Traduzir(Exception erro, string tabela, CancellationToken cancelamento)
    {
        if (cancelamento.IsCancellationRequested && erro is OperationCanceledException or NpgsqlException or IOException)
        {
            return new OperationCanceledException(cancelamento);
        }

        return erro switch
        {
            PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected } => new IncognitoException(
                CodigosPostgreSql.DestinoEmUso,
                $"A tabela {tabela} do destino estava em uso por outra sessão (ou pelo autovacuum contra o wraparound) e não foi liberada em 5 s: nada dela foi gravado."),
            PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } => Recusada(tabela),
            PostgresException { SqlState: PostgresErrorCodes.FeatureNotSupported or PostgresErrorCodes.InvalidColumnReference } => NaoAceita(tabela),
            PostgresException { SqlState: PostgresErrorCodes.UndefinedFunction or PostgresErrorCodes.UndefinedTable } => new IncognitoException(
                CodigosPostgreSql.FuncaoSemCaminho,
                $"Uma função chamada na gravação da tabela {tabela} (num CHECK, DEFAULT, índice, domínio ou trigger) depende do caminho de busca, que o Incognito fixa em pg_catalog: fixe SET search_path na função. Nada da tabela foi gravado."),
            PostgresException { SqlState: PostgresErrorCodes.TooManyConnections } => new IncognitoException(
                CodigosPostgreSql.ConexoesDemais,
                "O servidor PostgreSQL do destino recusou mais conexões: a gravação usa uma conexão por tabela em curso; reduza o paralelismo ou aumente o limite."),
            PostgresException { SqlState: var estado } violacao when estado.StartsWith("23", StringComparison.Ordinal) || estado.StartsWith("22", StringComparison.Ordinal) || estado == PostgresErrorCodes.ProgramLimitExceeded =>
                ValorRecusado(tabela, violacao),
            _ => new IncognitoException(
                CodigosPostgreSql.LinhasNaoGravadas,
                $"A gravação das linhas da tabela {tabela} falhou no servidor PostgreSQL, ou a conexão caiu no meio dela: nada da tabela foi gravado."),
        };
    }

    /// <summary>O login não esvazia ou não grava a tabela, ou a segurança por linha vale para ele.</summary>
    /// <param name="tabela">A tabela, como o catálogo a guarda.</param>
    /// <returns>A falha.</returns>
    internal static IncognitoException Recusada(string tabela) => new(
        CodigosPostgreSql.GravacaoRecusada,
        $"O login do destino não pode esvaziar ou gravar a tabela {tabela} (TRUNCATE e INSERT), ou a segurança por linha vale para ele: nada dela foi gravado.");

    /// <summary>A tabela não aceita a gravação como o Incognito a faz.</summary>
    /// <param name="tabela">A tabela, como o catálogo a guarda.</param>
    /// <returns>A falha.</returns>
    internal static IncognitoException NaoAceita(string tabela) => new(
        CodigosPostgreSql.TabelaNaoAceitaGravacao,
        $"A tabela {tabela} do destino não aceita o esvaziamento e a gravação: é referenciada por uma chave estrangeira, tem uma partição estrangeira, ou uma coluna gerada entrou na lista. Nada dela foi gravado.");

    // Só os nomes que o servidor dá em campos próprios (a restrição e a coluna), nunca o texto, o detalhe ou o contexto, que
    // trazem o valor da linha.
    private static IncognitoException ValorRecusado(string tabela, PostgresException violacao)
    {
        var restricao = string.IsNullOrEmpty(violacao.ConstraintName) ? "" : $", restrição {CitacaoPostgreSql.Citar(violacao.ConstraintName)}";
        var coluna = string.IsNullOrEmpty(violacao.ColumnName) ? "" : $", coluna {CitacaoPostgreSql.Citar(violacao.ColumnName)}";
        return new IncognitoException(
            CodigosPostgreSql.ValorRecusado,
            $"Uma linha gravada na tabela {tabela} do destino foi recusada por uma restrição dele (unicidade, NOT NULL, CHECK, tamanho ou tipo{restricao}{coluna}): nada da tabela foi gravado.");
    }
}
