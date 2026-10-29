using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// As regras da sessão de leitura que a base do contrato garante, sem depender do dialeto: o pedido conferido, os leitores
/// fixos e distintos, o descarte único que nunca lança, a recusa de uso depois do descarte e a recusa da chamada
/// concorrente no mesmo leitor e no coordenador.
/// </summary>
public sealed class SessaoLeituraTestes
{
    private static readonly ReferenciaColuna _coluna = new(new NomeObjeto("loja", "itens"), "pedido");

    [Fact]
    public void O_pedido_confere_os_leitores_o_modo_e_as_travadas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PedidoLeitura { Leitores = 0, Modo = ModoConsistencia.Exigir });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PedidoLeitura { Leitores = PedidoLeitura.MaximoDeLeitores + 1, Modo = ModoConsistencia.Exigir });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PedidoLeitura { Leitores = 1, Modo = (ModoConsistencia)7 });
        Assert.Throws<ArgumentNullException>(() => new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.Exigir, Travadas = null! });
        Assert.Throws<ArgumentNullException>(() => new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.Exigir, Travadas = [null!] });
        Assert.Throws<ArgumentException>(() => new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.Exigir, Travadas = [_coluna.Tabela, new NomeObjeto("loja", "itens")] });

        var travadas = new List<NomeObjeto> { _coluna.Tabela };
        var pedido = new PedidoLeitura { Leitores = PedidoLeitura.MaximoDeLeitores, Modo = ModoConsistencia.MelhorEsforco, Travadas = travadas };
        travadas.Add(new NomeObjeto("loja", "outra"));

        Assert.Equal((64, ModoConsistencia.MelhorEsforco, 1), (pedido.Leitores, pedido.Modo, pedido.Travadas.Count));
        Assert.Empty(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.Exigir }.Travadas);
        Assert.DoesNotContain("itens", pedido.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Os_leitores_sao_fixos_distintos_e_de_uma_sessao_so()
    {
        var leitor = new LeitorContado();
        var lista = new List<LeitorDaSessao> { leitor, new LeitorContado() };
        var sessao = new SessaoContada(lista);
        lista.Clear();

        Assert.Equal(2, sessao.Leitores.Count);
        Assert.Throws<ArgumentException>(() => new SessaoContada([]));
        Assert.Throws<ArgumentNullException>(() => new SessaoContada(null!));
        Assert.Throws<ArgumentNullException>(() => new SessaoContada([null!]));
        var outro = new LeitorContado();
        Assert.Throws<ArgumentException>(() => new SessaoContada([outro, outro]));

        // O leitor de uma sessão não entra noutra; o da sessão recusada continua livre.
        var livre = new LeitorContado();
        Assert.Throws<ArgumentException>(() => new SessaoContada([livre, leitor]));
        Assert.Single(new SessaoContada([livre]).Leitores);
        Assert.Single(new SessaoContada([outro]).Leitores);
    }

    [Fact]
    public async Task O_descarte_roda_uma_vez_so_nunca_lanca_e_recusa_o_uso_depois()
    {
        var leitor = new LeitorContado();
        var sessao = new SessaoContada([leitor]) { FalharNoDescarte = true };

        await sessao.DisposeAsync();
        await sessao.DisposeAsync();

        Assert.Equal(1, sessao.Descartes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sessao.LerEsquemaAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sessao.LerIdadeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => leitor.LerMaiorValorAsync(_coluna, TestContext.Current.CancellationToken));
        Assert.Equal(0, leitor.Leituras);
    }

    [Fact]
    public async Task O_mesmo_leitor_e_o_coordenador_recusam_a_chamada_concorrente()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorContado();
        var outro = new LeitorContado();
        var sessao = new SessaoContada([leitor, outro]);

        var primeira = leitor.LerMaiorValorAsync(_coluna, cancelamento);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.LerMaiorValorAsync(_coluna, cancelamento));
        var emParalelo = outro.LerMaiorValorAsync(_coluna, cancelamento);
        var esquema = sessao.LerEsquemaAsync(cancelamento);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sessao.LerIdadeAsync(cancelamento));

        leitor.Liberar();
        outro.Liberar();
        sessao.Liberar();
        Assert.Equal((31L, 31L), (await primeira, await emParalelo));
        await esquema;

        // Terminada a chamada, o leitor e o coordenador aceitam a seguinte; a que falha também libera.
        leitor.Falhar = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.LerMaiorValorAsync(_coluna, cancelamento));
        leitor.Falhar = false;
        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
        Assert.Equal(7L, await sessao.LerIdadeAsync(cancelamento));
        await Assert.ThrowsAsync<ArgumentNullException>(() => leitor.LerMaiorValorAsync(null!, cancelamento));
    }

    [Fact]
    public async Task A_abertura_confere_o_pedido_o_lado_e_o_descarte_do_dialeto()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var travada = new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco, Travadas = [_coluna.Tabela] };
        var provedor = new ProvedorFalso(completo: true, Defeito.Nenhum, perturbar: null);
        var (bancoOrigem, bancoDestino) = ("origem-" + Guid.NewGuid().ToString("N"), "destino-" + Guid.NewGuid().ToString("N"));
        try
        {
            var origem = await provedor.ConectarOrigemAsync(new SegredoConexao(bancoOrigem), cancelamento);
            await using (var destino = await provedor.ConectarDestinoAsync(new SegredoConexao(bancoDestino), cancelamento))
            {
                await Assert.ThrowsAsync<ArgumentNullException>(() => origem.AbrirLeituraAsync(null!, cancelamento));
                await Assert.ThrowsAsync<ArgumentException>(() => destino.AbrirLeituraAsync(travada, cancelamento));
                await (await origem.AbrirLeituraAsync(travada, cancelamento)).DisposeAsync();
            }

            await origem.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => origem.AbrirLeituraAsync(travada, cancelamento));
        }
        finally
        {
            BancoFalso.Esquecer(bancoOrigem);
            BancoFalso.Esquecer(bancoDestino);
        }
    }

    private sealed class SessaoContada(IReadOnlyList<LeitorDaSessao> leitores) : SessaoLeitura(leitores)
    {
        private readonly TaskCompletionSource _portao = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _segurar = true;

        internal int Descartes { get; private set; }

        internal bool FalharNoDescarte { get; init; }

        public override ConsistenciaObtida Obtida => ConsistenciaObtida.Instantaneo;

        internal void Liberar()
        {
            _segurar = false;
            _portao.TrySetResult();
        }

        protected override async Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento)
        {
            if (_segurar)
            {
                await _portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
            }

            return new EsquemaBanco { Tabelas = [] };
        }

        protected override Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento) => Task.FromResult<long?>(7);

        protected override ValueTask DisposeAsyncCore()
        {
            Descartes++;
            return FalharNoDescarte ? ValueTask.FromException(new InvalidOperationException("O descarte falhou.")) : ValueTask.CompletedTask;
        }
    }

    private sealed class LeitorContado : LeitorDaSessao
    {
        private readonly TaskCompletionSource _portao = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _segurar = true;

        internal int Leituras { get; private set; }

        internal bool Falhar { get; set; }

        internal void Liberar()
        {
            _segurar = false;
            _portao.TrySetResult();
        }

        protected override async Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento)
        {
            Leituras++;
            if (_segurar)
            {
                await _portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
            }

            return Falhar ? throw new InvalidOperationException("A leitura falhou.") : 31;
        }
    }
}
