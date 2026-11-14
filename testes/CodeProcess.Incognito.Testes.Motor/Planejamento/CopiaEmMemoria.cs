using System.Collections.Concurrent;
using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// Dublês da cópia em memória, para os testes do pipeline: a leitura entrega as linhas de cada tabela em lotes de tamanho
/// fixo, e a gravação guarda o que recebe e só confirma no <c>ConcluirAsync</c>. Cada linha é a lista dos valores na ordem
/// das colunas do pedido; <see cref="SemForma"/> marca o valor sem representação na forma canônica.
/// </summary>
internal static class CopiaEmMemoria
{
    /// <summary>O valor sem representação na forma canônica.</summary>
    internal static readonly object SemForma = new();

    /// <summary>Uma sessão com um leitor só.</summary>
    /// <param name="leitor">O leitor.</param>
    /// <returns>A sessão, que liga o leitor a ela.</returns>
    internal static SessaoLeitura Sessao(LeitorDaSessao leitor) => new SessaoDeCopia([leitor], new EsquemaBanco { Tabelas = [] });

    /// <summary>Uma sessão com os leitores dados e o esquema que ela relê no instante.</summary>
    /// <param name="leitores">Os leitores.</param>
    /// <param name="esquema">O esquema do instante.</param>
    /// <param name="aoDescartar">Chamado no descarte da sessão.</param>
    /// <param name="naIdade">Lançada na leitura da idade do instante; nula devolve 7.</param>
    /// <returns>A sessão.</returns>
    internal static SessaoLeitura Sessao(IReadOnlyList<LeitorDaSessao> leitores, EsquemaBanco esquema, Action? aoDescartar = null, Exception? naIdade = null) =>
        new SessaoDeCopia(leitores, esquema, aoDescartar, naIdade);

    private sealed class SessaoDeCopia(IReadOnlyList<LeitorDaSessao> leitores, EsquemaBanco esquema, Action? aoDescartar = null, Exception? naIdade = null) : SessaoLeitura(leitores)
    {
        public override ConsistenciaObtida Obtida => ConsistenciaObtida.Instantaneo;

        protected override Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento) => Task.FromResult(esquema);

        protected override Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento) => naIdade is null ? Task.FromResult<long?>(7) : Task.FromException<long?>(naIdade);

        protected override ValueTask DisposeAsyncCore()
        {
            aoDescartar?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>O leitor em memória: as linhas de cada tabela, pelo nome, em lotes de <see cref="LinhasPorLote"/>.</summary>
internal sealed class LeitorEmMemoria(IReadOnlyDictionary<string, object?[][]> tabelas) : LeitorDaSessao
{
    private int _aberturas;

    internal int LinhasPorLote { get; init; } = 2;

    /// <summary>Lança na leitura do lote de número dado (a partir de 1); nulo não lança.</summary>
    internal Func<int, Exception?> NoLote { get; init; } = _ => null;

    /// <summary>Chamado a cada lote lido (o número, a partir de 1), antes de entregá-lo.</summary>
    internal Func<int, CancellationToken, Task>? AntesDoLote { get; init; }

    internal int Aberturas => Volatile.Read(ref _aberturas);

    internal ConcurrentQueue<string> Ordem { get; } = new();

    internal ConcurrentBag<LoteEmMemoria> Lotes { get; } = [];

    internal ConcurrentBag<PedidoLeituraTabela> Pedidos { get; } = [];

    internal int LeiturasDescartadas;

    protected override Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento) => Task.FromResult<long?>(0);

    protected override Task<LeituraTabela> AbrirTabelaAsyncCore(PedidoLeituraTabela pedido, CancellationToken cancelamento)
    {
        Interlocked.Increment(ref _aberturas);
        Pedidos.Add(pedido);
        return Task.FromResult<LeituraTabela>(new LeituraEmMemoria(this, pedido, tabelas[pedido.Tabela.Nome]));
    }

    private sealed class LeituraEmMemoria(LeitorEmMemoria leitor, PedidoLeituraTabela pedido, object?[][] linhas) : LeituraTabela
    {
        private int _proxima;
        private int _lotes;

        protected override async Task<LoteLinhas?> LerLoteAsyncCore(CancellationToken cancelamento)
        {
            if (_proxima >= linhas.Length)
            {
                leitor.Ordem.Enqueue("fim da leitura");
                return null;
            }

            var numero = ++_lotes;
            if (leitor.NoLote(numero) is { } excecao)
            {
                throw excecao;
            }

            if (leitor.AntesDoLote is { } antes)
            {
                await antes(numero, cancelamento);
            }

            var parte = linhas.Skip(_proxima).Take(leitor.LinhasPorLote).ToArray();
            _proxima += parte.Length;
            var lote = new LoteEmMemoria(pedido, parte);
            leitor.Lotes.Add(lote);
            return lote;
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Interlocked.Increment(ref leitor.LeiturasDescartadas);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Um lote em memória: guarda o que foi escrito e mantido, e diz a linha como iria ao destino.</summary>
internal sealed class LoteEmMemoria(PedidoLeituraTabela pedido, object?[][] linhas) : LoteLinhas(pedido, linhas.Length)
{
    private readonly ConcurrentDictionary<(int Linha, int Coluna), object?> _escritos = new();
    private readonly ConcurrentDictionary<(int Linha, int Coluna), bool> _mantidos = new();
    private int _descartes;

    internal int Descartes => Volatile.Read(ref _descartes);

    internal bool Mantido(int linha, int coluna) => _mantidos.ContainsKey((linha, coluna));

    /// <summary>A linha como vai ao destino: o escrito onde houve escrita, o original no resto.</summary>
    /// <param name="linha">A linha no lote.</param>
    /// <returns>Os valores, na ordem das colunas.</returns>
    internal object?[] Final(int linha) =>
        [.. Enumerable.Range(0, linhas[linha].Length).Select(coluna => _escritos.TryGetValue((linha, coluna), out var escrito) ? escrito : linhas[linha][coluna])];

    protected override bool TentarLerCore(int linha, int coluna, out object? valor)
    {
        var original = linhas[linha][coluna];
        if (ReferenceEquals(original, CopiaEmMemoria.SemForma))
        {
            valor = null;
            return false;
        }

        valor = original is byte[] bytes ? bytes.ToArray() : original;
        return true;
    }

    protected override void EscreverCore(int linha, int coluna, object? valor) => _escritos[(linha, coluna)] = valor;

    protected override void ManterCore(int linha, int coluna) => _mantidos[(linha, coluna)] = true;

    protected override void DisposeCore() => Interlocked.Increment(ref _descartes);
}

/// <summary>O gravador em memória: o esvaziamento e as gravações, confirmadas só no <c>ConcluirAsync</c>.</summary>
internal sealed class GravadorEmMemoria : GravadorEmLote
{
    internal ConcurrentDictionary<string, IReadOnlyList<object?[]>> Confirmadas { get; } = new();

    internal ConcurrentQueue<string> Esvaziadas { get; } = new();

    internal ConcurrentQueue<string> Ordem { get; set; } = new();

    internal ConcurrentBag<PedidoGravacao> Pedidos { get; } = [];

    /// <summary>Lança na gravação do lote de número dado (a partir de 1).</summary>
    internal Func<int, Exception?> NoLote { get; init; } = _ => null;

    /// <summary>Espera antes de gravar cada lote.</summary>
    internal TimeSpan Atraso { get; init; }

    internal Exception? NoConcluir { get; init; }

    /// <summary>Chamado no esvaziamento e na abertura, com <c>esvaziar:tabela</c> ou <c>abrir:tabela</c>.</summary>
    internal Action<string>? Registrar { get; init; }

    internal int Descartadas;

    protected override Task EsvaziarAsyncCore(NomeObjeto tabela, CancellationToken cancelamento)
    {
        Registrar?.Invoke("esvaziar:" + tabela.Nome);
        Esvaziadas.Enqueue(tabela.Nome);
        return Task.CompletedTask;
    }

    protected override Task<GravacaoTabela> AbrirAsyncCore(PedidoGravacao pedido, CancellationToken cancelamento)
    {
        Registrar?.Invoke("abrir:" + pedido.Tabela.Nome);
        Pedidos.Add(pedido);
        return Task.FromResult<GravacaoTabela>(new GravacaoEmMemoria(this, pedido));
    }

    private sealed class GravacaoEmMemoria(GravadorEmMemoria gravador, PedidoGravacao pedido) : GravacaoTabela
    {
        private readonly List<object?[]> _linhas = [];
        private int _lotes;

        protected override async Task GravarAsyncCore(LoteLinhas lote, CancellationToken cancelamento)
        {
            if (lote is not LoteEmMemoria emMemoria)
            {
                throw new InvalidOperationException("Lote de outro dialeto.");
            }

            if (gravador.Atraso > TimeSpan.Zero)
            {
                await Task.Delay(gravador.Atraso, cancelamento);
            }

            if (gravador.NoLote(++_lotes) is { } excecao)
            {
                throw excecao;
            }

            _linhas.AddRange(Enumerable.Range(0, lote.Quantidade).Select(emMemoria.Final));
        }

        protected override Task ConcluirAsyncCore(CancellationToken cancelamento)
        {
            gravador.Ordem.Enqueue("concluir");
            if (gravador.NoConcluir is { } excecao)
            {
                throw excecao;
            }

            gravador.Confirmadas[pedido.Tabela.Nome] = [.. _linhas];
            return Task.CompletedTask;
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Interlocked.Increment(ref gravador.Descartadas);
            return ValueTask.CompletedTask;
        }
    }
}
