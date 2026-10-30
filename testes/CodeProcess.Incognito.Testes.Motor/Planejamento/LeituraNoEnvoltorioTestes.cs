using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A sessão de leitura pelo envoltório: a abertura traduz a recusa como erro do plano e a falha com o código; a sessão que
/// se contradiz é descartada antes da falha; o destino nunca trava; os leitores leem coluna e nada da mensagem do dialeto
/// sai deles; o descarte nunca lança. E a abertura pela carga do plano: as copiadas travadas, o modo da configuração e a
/// sessão descartada quando a releitura do esquema falha.
/// </summary>
public sealed class LeituraNoEnvoltorioTestes
{
    private const string Sentinela = "zqLEITURAqz";

    private static readonly ReferenciaColuna _coluna = new(new NomeObjeto("loja", "itens"), "pedido");

    private static readonly PedidoLeitura _dois = new() { Leitores = 2, Modo = ModoConsistencia.MelhorEsforco };

    [Fact]
    public async Task A_abertura_traduz_a_recusa_como_erro_do_plano_e_a_falha_com_o_codigo()
    {
        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => AbrirAsync(new Comportamento { AoAbrir = new DialetoRecusouException("INC3399", "A tabela loja.itens não existe.") }));
        var erro = Assert.Single(recusa.Erros);
        Assert.Equal(("INC3399", "A tabela loja.itens não existe."), (erro.Codigo, erro.Mensagem));

        var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => AbrirAsync(new Comportamento { AoAbrir = new IncognitoException("INC3398", "A trava não veio.") }));
        Assert.Equal(("INC3398", "A trava não veio."), (falha.Codigo, falha.Message));

        var doDriver = await Assert.ThrowsAsync<FalhaDialetoException>(() => AbrirAsync(new Comportamento { AoAbrir = new InvalidOperationException(Sentinela) }));
        Assert.Equal(CodigosPlanejar.FalhaDoDialeto, doDriver.Codigo);
        Assert.DoesNotContain(Sentinela, doDriver.ToString(), StringComparison.Ordinal);

        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => AbrirAsync(new Comportamento { AoAbrir = new OperationCanceledException(cancelado.Token) }, cancelamento: cancelado.Token));
    }

    [Fact]
    public async Task A_sessao_que_se_contradiz_e_descartada_antes_da_falha()
    {
        Comportamento[] casos =
        [
            new() { SemSessao = true },
            new() { Obtida = ConsistenciaObtida.Indefinida },
            new() { Obtida = (ConsistenciaObtida)9 },
            new() { Obtida = ConsistenciaObtida.PorTabela, Exigir = true },
            new() { LeitoresDevolvidos = 1 },
            new() { FalharNaConsistencia = true },
        ];

        foreach (var caso in casos)
        {
            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => AbrirAsync(caso));

            Assert.DoesNotContain(Sentinela, falha.ToString(), StringComparison.Ordinal);
            Assert.Equal(caso.SemSessao ? 0 : 1, caso.Descartes);
            Assert.True(!caso.SemSessao || falha.Message.Contains("não devolveu nada", StringComparison.Ordinal), $"A sessão nula saiu como: {falha.Message}");
        }

        // Por tabela no melhor esforço é coerente.
        var porTabela = new Comportamento { Obtida = ConsistenciaObtida.PorTabela };
        await using var sessao = await AbrirAsync(porTabela);
        Assert.Equal((ConsistenciaObtida.PorTabela, 2), (sessao.Obtida, sessao.Leitores.Count));
    }

    [Fact]
    public async Task O_destino_nunca_trava_e_le_pelos_leitores()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comportamento = new Comportamento();
        await using var destino = await ConectarAsync(comportamento, LadoBanco.Destino);

        await Assert.ThrowsAsync<InvalidOperationException>(() => destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco, Travadas = [_coluna.Tabela] }, cancelamento));
        Assert.Equal(0, comportamento.Aberturas);

        await using var sessao = await destino.AbrirLeituraAsync(_dois, cancelamento);
        Assert.Equal(31L, await sessao.Leitores[1].LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task Os_leitores_traduzem_sem_mensagem_e_recusam_a_leitura_concorrente()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        Exception[] falhas =
        [
            new IncognitoException("INC3397", "O valor " + Sentinela + " não coube."),
            new DialetoRecusouException("INC3396", "A coluna " + Sentinela + " não é inteira."),
            new InvalidOperationException(Sentinela),
        ];

        foreach (var lancada in falhas)
        {
            await using var sessao = await AbrirAsync(new Comportamento { AoLer = lancada });
            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => sessao.Leitores[0].LerMaiorValorAsync(_coluna, cancelamento));

            Assert.Equal(lancada is IncognitoException incognito ? incognito.Codigo : CodigosPlanejar.FalhaDoDialeto, falha.Codigo);
            Assert.DoesNotContain(Sentinela, falha.ToString(), StringComparison.Ordinal);
        }

        var segurar = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var presa = await AbrirAsync(new Comportamento { Segurar = segurar.Task });
        var primeira = presa.Leitores[0].LerMaiorValorAsync(_coluna, cancelamento);
        await Assert.ThrowsAsync<InvalidOperationException>(() => presa.Leitores[0].LerMaiorValorAsync(_coluna, cancelamento));
        var outro = presa.Leitores[1].LerMaiorValorAsync(_coluna, cancelamento);
        segurar.SetResult(31);

        Assert.Equal((31L, 31L), (await primeira, await outro));
        Assert.Equal(31L, await presa.Leitores[0].LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task A_releitura_e_a_idade_sao_conferidas_e_o_descarte_nunca_lanca()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var recusa = new Comportamento { AoReler = new DialetoRecusouException("INC3399", "O esquema não pôde ser relido.") };
        await using (var sessao = await AbrirAsync(recusa))
        {
            await Assert.ThrowsAsync<PlanoRecusadoException>(() => sessao.LerEsquemaAsync(cancelamento));
        }

        var negativa = new Comportamento { Idade = -1, FalharNoDescarte = true };
        var comIdade = await AbrirAsync(negativa);
        await Assert.ThrowsAsync<FalhaDialetoException>(() => comIdade.LerIdadeAsync(cancelamento));
        await comIdade.DisposeAsync();
        await comIdade.DisposeAsync();
        Assert.Equal(1, negativa.Descartes);

        await using var semIdade = await AbrirAsync(new Comportamento { Idade = null });
        Assert.Null(await semIdade.LerIdadeAsync(cancelamento));
        Assert.Empty((await semIdade.LerEsquemaAsync(cancelamento)).Tabelas);
    }

    [Fact]
    public void O_pedido_da_carga_trava_as_copiadas_com_o_modo_da_configuracao()
    {
        var carga = CargaSintetica.Para(
            Yaml.Com("politica:\n  consistencia: exigir\n"),
            Mapa("clientes"),
            Mapa("historico", AcaoTabela.Esvaziar),
            Mapa("pedidos"),
            Mapa("rascunhos", AcaoTabela.Ignorar));

        var pedido = LeituraConsistente.Pedido(carga, 4);

        Assert.Equal((4, ModoConsistencia.Exigir), (pedido.Leitores, pedido.Modo));
        Assert.Equal([new NomeObjeto("loja", "clientes"), new NomeObjeto("loja", "pedidos")], pedido.Travadas);
        Assert.Equal(ModoConsistencia.MelhorEsforco, LeituraConsistente.Pedido(CargaSintetica.Para(Yaml.Minimo, Mapa("clientes")), 1).Modo);
        Assert.Throws<ArgumentOutOfRangeException>(() => LeituraConsistente.Pedido(carga, 0));
    }

    [Fact]
    public async Task A_abertura_pela_carga_rele_o_esquema_e_descarta_a_sessao_se_a_releitura_falha()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var carga = CargaSintetica.Para(Yaml.Minimo, Mapa("clientes"));
        var certo = new Comportamento();
        await using (var origem = await ConectarAsync(certo, LadoBanco.Origem))
        {
            var aberta = await LeituraConsistente.AbrirAsync(origem, carga, 2, cancelamento);
            await using (aberta.Sessao)
            {
                Assert.Empty(aberta.Esquema.Tabelas);
                Assert.Equal([new NomeObjeto("loja", "clientes")], certo.Pedido!.Travadas);
                Assert.Equal("LeituraAberta", aberta.ToString());
            }
        }

        var falha = new Comportamento { AoReler = new IncognitoException("INC3399", "A releitura falhou.") };
        await using var outra = await ConectarAsync(falha, LadoBanco.Origem);
        await Assert.ThrowsAsync<FalhaDialetoException>(() => LeituraConsistente.AbrirAsync(outra, carga, 2, cancelamento));
        Assert.Equal(1, falha.Descartes);
    }

    private static MapaTabela Mapa(string tabela, AcaoTabela acao = AcaoTabela.Copiar) =>
        new(new NomeTabela("loja", tabela), acao, acaoEscrita: false, [], posicao: null);

    private static async Task<DialetoProtegido.SessaoProtegida> AbrirAsync(Comportamento comportamento, CancellationToken? cancelamento = null)
    {
        var token = cancelamento ?? TestContext.Current.CancellationToken;
        await using var origem = await ConectarAsync(comportamento, LadoBanco.Origem);
        var pedido = comportamento.Exigir ? new PedidoLeitura { Leitores = 2, Modo = ModoConsistencia.Exigir } : _dois;
        return await origem.AbrirLeituraAsync(pedido, token);
    }

    private static Task<DialetoProtegido> ConectarAsync(Comportamento comportamento, LadoBanco lado) =>
        DialetoProtegido.ConectarAsync(new ProvedorDeLeitura(comportamento), "postgresql", lado, new SegredoConexao("leitura"), TestContext.Current.CancellationToken);

    // O que o dialeto de leitura faz em cada teste.
    private sealed class Comportamento
    {
        internal Exception? AoAbrir { get; init; }

        internal bool SemSessao { get; init; }

        internal ConsistenciaObtida Obtida { get; init; } = ConsistenciaObtida.Instantaneo;

        internal bool Exigir { get; init; }

        internal int? LeitoresDevolvidos { get; init; }

        internal bool FalharNaConsistencia { get; init; }

        internal Exception? AoLer { get; init; }

        internal Task<long?>? Segurar { get; init; }

        internal Exception? AoReler { get; init; }

        internal long? Idade { get; init; } = 0;

        internal bool FalharNoDescarte { get; init; }

        internal int Aberturas { get; set; }

        internal int Descartes { get; set; }

        internal PedidoLeitura? Pedido { get; set; }
    }

    private sealed class ProvedorDeLeitura(Comportamento comportamento) : ProvedorDialeto
    {
        public override string Nome => "postgresql";

        public override Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
            Task.FromResult<DialetoOrigem>(new OrigemDeLeitura(comportamento));

        public override Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
            Task.FromResult<DialetoDestino>(new DestinoDeLeitura(comportamento));
    }

    private sealed class OrigemDeLeitura(Comportamento comportamento) : DialetoOrigem
    {
        public override LeitorEsquema Esquema => throw new NotSupportedException();

        public override IdentidadeBanco Identidade => throw new NotSupportedException();

        public override DialetoSql Sql => throw new NotSupportedException();

        public override LeitorDadosOrigem Dados => throw new NotSupportedException();

        protected override Task<SessaoLeitura> AbrirLeituraAsyncCore(PedidoLeitura pedido, CancellationToken cancelamento) => SessaoDeLeitura.AbrirAsync(comportamento, pedido);
    }

    private sealed class DestinoDeLeitura(Comportamento comportamento) : DialetoDestino
    {
        public override LeitorEsquema Esquema => throw new NotSupportedException();

        public override IdentidadeBanco Identidade => throw new NotSupportedException();

        public override DialetoSql Sql => throw new NotSupportedException();

        public override BloqueioExecucao Bloqueio => throw new NotSupportedException();

        public override MarcadorDestino Marcador => throw new NotSupportedException();

        public override LeitorDadosDestino Dados => throw new NotSupportedException();

        public override DiarioExecucao Diario => throw new NotSupportedException();

        protected override Task<SessaoLeitura> AbrirLeituraAsyncCore(PedidoLeitura pedido, CancellationToken cancelamento) => SessaoDeLeitura.AbrirAsync(comportamento, pedido);
    }

    private sealed class SessaoDeLeitura(Comportamento comportamento, IReadOnlyList<LeitorDaSessao> leitores) : SessaoLeitura(leitores)
    {
        public override ConsistenciaObtida Obtida => comportamento.FalharNaConsistencia ? throw new InvalidOperationException(Sentinela) : comportamento.Obtida;

        internal static Task<SessaoLeitura> AbrirAsync(Comportamento comportamento, PedidoLeitura pedido)
        {
            comportamento.Aberturas++;
            comportamento.Pedido = pedido;
            if (comportamento.AoAbrir is { } falha)
            {
                return Task.FromException<SessaoLeitura>(falha);
            }

            var leitores = Enumerable.Range(0, comportamento.LeitoresDevolvidos ?? pedido.Leitores).Select(_ => new LeitorDeLeitura(comportamento)).ToArray();
            return Task.FromResult<SessaoLeitura>(comportamento.SemSessao ? null! : new SessaoDeLeitura(comportamento, leitores));
        }

        protected override Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento) =>
            comportamento.AoReler is { } falha ? Task.FromException<EsquemaBanco>(falha) : Task.FromResult(new EsquemaBanco { Tabelas = [] });

        protected override Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento) => Task.FromResult(comportamento.Idade);

        protected override ValueTask DisposeAsyncCore()
        {
            comportamento.Descartes++;
            return comportamento.FalharNoDescarte ? ValueTask.FromException(new InvalidOperationException(Sentinela)) : ValueTask.CompletedTask;
        }
    }

    private sealed class LeitorDeLeitura(Comportamento comportamento) : LeitorDaSessao
    {
        protected override Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento) =>
            comportamento.AoLer is { } falha ? Task.FromException<long?>(falha) : comportamento.Segurar ?? Task.FromResult<long?>(31);
    }
}
