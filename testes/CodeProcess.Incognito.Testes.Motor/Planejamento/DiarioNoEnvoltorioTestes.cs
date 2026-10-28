using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O diário pelo envoltório: só com a concessão viva; a perda do bloqueio, antes ou durante a operação, sai como
/// <c>INC4001</c> (saída 1), nunca como cancelamento; nenhuma mensagem do dialeto sai, e a recusa é falha; o que o dialeto
/// devolve e se contradiz é falha, nunca pulado.
/// </summary>
public sealed class DiarioNoEnvoltorioTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    private static readonly NomeObjeto _tabela = EsquemaSintetico.Nome("pedidos");

    private static readonly EntradaDiario _chave = new() { Execucao = Guid.Empty, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = _tabela, Nome = "fk", Situacao = SituacaoEntrada.Removida };

    private static readonly EntradaDiario _triggers = new() { Execucao = Guid.Empty, Tipo = TipoEntrada.Triggers, Tabela = _tabela, Situacao = SituacaoEntrada.Removida };

    // Cada operação do diário pelo envoltório, com a operação do dialeto que ela chama.
    private static readonly Dictionary<Operacao, Func<DialetoProtegido, DialetoProtegido.ConcessaoProtegida, CancellationToken, Task>> _operacoes = new()
    {
        [Operacao.CriarDiario] = (destino, concessao, token) => destino.CriarDiarioAsync(concessao, token),
        [Operacao.LerDiario] = (destino, concessao, token) => destino.LerDiarioAsync(concessao, token),
        [Operacao.IniciarExecucao] = (destino, concessao, token) => destino.IniciarExecucaoAsync(concessao, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, token),
        [Operacao.RemoverChave] = (destino, concessao, token) => destino.RemoverChaveAsync(concessao, Guid.NewGuid(), _tabela, "fk", token),
        [Operacao.DesligarTriggers] = (destino, concessao, token) => destino.DesligarTriggersAsync(concessao, Guid.NewGuid(), _tabela, token),
        [Operacao.ReligarTriggers] = (destino, concessao, token) => destino.ReligarTriggersAsync(concessao, _triggers, token),
        [Operacao.RecriarChave] = (destino, concessao, token) => destino.RecriarChaveAsync(concessao, _chave, token),
        [Operacao.ValidarChave] = (destino, concessao, token) => destino.ValidarChaveAsync(concessao, _chave, token),
        [Operacao.DescartarEntrada] = (destino, concessao, token) => destino.DescartarEntradaAsync(concessao, _chave, token),
        [Operacao.EncerrarExecucao] = (destino, concessao, token) => destino.EncerrarExecucaoAsync(concessao, Guid.NewGuid(), SituacaoExecucao.Falhou, DateTimeOffset.UnixEpoch, token),
    };

    public static TheoryData<Operacao> Operacoes => [.. _operacoes.Keys];

    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task Com_o_bloqueio_perdido_a_operacao_sai_com_INC4001_sem_chamar_o_dialeto(Operacao operacao)
    {
        var provedor = new ProvedorDoPlanejar();
        await using var destino = await ConectarAsync(provedor);
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;
        provedor.Concessoes[0].Perder();

        var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => _operacoes[operacao](destino, concessao, TestContext.Current.CancellationToken));

        Assert.Equal(("INC4001", 1), (perda.Codigo, ClassificadorDeSaida.De(perda)));
        Assert.DoesNotContain($"{operacao}:destino", provedor.Chamadas);
    }

    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task A_perda_durante_a_operacao_sai_com_INC4001_e_nao_como_cancelamento(Operacao operacao)
    {
        // O dialeto recebe o token ligado à perda: ele cancela a operação, e o envoltório diz que foi a perda.
        ProvedorDoPlanejar? provedor = null;
        provedor = new ProvedorDoPlanejar
        {
            NoDiario = async (_, token) =>
            {
                provedor!.Concessoes[0].Perder();
                await Task.Delay(Timeout.Infinite, token);
            },
        };
        await using var destino = await ConectarAsync(provedor);
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

        // Com prazo: sem o token ligado, a operação esperaria para sempre em vez de falhar.
        var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => _operacoes[operacao](destino, concessao, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Equal(("INC4001", 1), (perda.Codigo, ClassificadorDeSaida.De(perda)));
    }

    [Fact]
    public async Task O_cancelamento_de_quem_chama_continua_cancelamento()
    {
        var provedor = new ProvedorDoPlanejar { NoDiario = (_, token) => Task.Delay(Timeout.Infinite, token) };
        await using var destino = await ConectarAsync(provedor);
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;
        using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var leitura = destino.LerDiarioAsync(concessao, cancelar.Token);

        await cancelar.CancelAsync();
        var cancelado = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leitura.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Equal(130, ClassificadorDeSaida.De(cancelado));
    }

    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task Nenhuma_mensagem_do_dialeto_sai_e_a_recusa_e_falha(Operacao operacao)
    {
        foreach (var lancada in new Exception[] { new IncognitoException("INC3399", Sentinela), new DialetoRecusouException("INC3399", Sentinela), new InvalidOperationException(Sentinela) })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = (chamada, _) => chamada == operacao ? lancada : null };
            await using var destino = await ConectarAsync(provedor);
            await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => _operacoes[operacao](destino, concessao, TestContext.Current.CancellationToken));

            Assert.DoesNotContain(Sentinela, falha.Message, StringComparison.Ordinal);
            Assert.Equal(lancada is IncognitoException ? "INC3399" : "INC3001", falha.Codigo);
            Assert.Equal(1, ClassificadorDeSaida.De(falha));
        }
    }

    public static TheoryData<string> Contradicoes => [.. _contradicoes.Keys];

    // O diário lido que se contradiz.
    private static readonly Dictionary<string, EstadoDiario> _contradicoes = new()
    {
        ["sem diário, com entrada"] = new() { Existe = false, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.ChaveEstrangeira, "fk")] },
        ["chave sem nome"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.ChaveEstrangeira, null)] },
        ["triggers com nome"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.Triggers, "tg")] },
        ["triggers sem validação"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.Triggers, null, SituacaoEntrada.SemValidacao)] },
        ["chave antes das triggers"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.ChaveEstrangeira, "fk"), Entrada(1, TipoEntrada.Triggers, null)] },
        ["entrada de execução não lida"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(2, TipoEntrada.ChaveEstrangeira, "fk")] },
        ["entrada repetida"] = new() { Existe = true, Execucoes = [Execucao(1)], Pendentes = [Entrada(1, TipoEntrada.ChaveEstrangeira, "fk"), Entrada(1, TipoEntrada.ChaveEstrangeira, "fk")] },
        ["execução repetida"] = new() { Existe = true, Execucoes = [Execucao(1), Execucao(1)] },
    };

    [Theory]
    [MemberData(nameof(Contradicoes))]
    public async Task O_diario_lido_que_se_contradiz_e_falha(string contradicao)
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = _contradicoes[contradicao] };
        await using var destino = await ConectarAsync(provedor);
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

        var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => destino.LerDiarioAsync(concessao, TestContext.Current.CancellationToken));

        Assert.Equal("INC3002", falha.Codigo);
    }

    [Fact]
    public async Task O_diario_coerente_passa_e_a_situacao_desconhecida_e_falha()
    {
        var coerente = new EstadoDiario
        {
            Existe = true,
            Execucoes = [Execucao(1), Execucao(2)],
            Pendentes = [Entrada(1, TipoEntrada.Triggers, null), Entrada(2, TipoEntrada.ChaveEstrangeira, "fk", SituacaoEntrada.SemValidacao)],
        };
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = coerente, NaRestauracao = (SituacaoEntrada)7 };
        await using var destino = await ConectarAsync(provedor);
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(2, (await destino.LerDiarioAsync(concessao, TestContext.Current.CancellationToken)).Pendentes.Count);
        Assert.Equal("INC3002", (await Assert.ThrowsAsync<FalhaDialetoException>(() => destino.RecriarChaveAsync(concessao, _chave, TestContext.Current.CancellationToken))).Codigo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => destino.RecriarChaveAsync(concessao, _triggers, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => destino.ReligarTriggersAsync(concessao, _chave, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Os_objetos_do_diario_fora_do_esquema_reservado_ou_repetidos_sao_falha()
    {
        var fora = new ObjetoReservado { Nome = EsquemaSintetico.Nome("execucao"), Tipo = TipoObjetoReservado.Tabela };
        var dentro = new ObjetoReservado { Nome = new NomeObjeto("incognito", "execucao"), Tipo = TipoObjetoReservado.Tabela };
        var repetido = new ObjetoReservado { Nome = new NomeObjeto("incognito", "execucao"), Tipo = TipoObjetoReservado.Indice };

        foreach (var objetos in new[] { new[] { fora }, [dentro, repetido] })
        {
            await using var destino = await ConectarAsync(new ProvedorDoPlanejar { ObjetosDoDiario = objetos });

            Assert.Equal("INC3002", Assert.Throws<FalhaDialetoException>(destino.ObjetosDoDiario).Codigo);
        }

        await using var certo = await ConectarAsync(new ProvedorDoPlanejar { ObjetosDoDiario = [dentro] });
        Assert.Equal([dentro], certo.ObjetosDoDiario());
    }

    private static ExecucaoLida Execucao(int numero) =>
        new() { Id = new Guid(numero, 0, 0, new byte[8]), Marcador = Guid.Empty, IniciadaEm = DateTimeOffset.UnixEpoch, Situacao = SituacaoExecucao.EmAndamento };

    private static EntradaDiario Entrada(int execucao, TipoEntrada tipo, string? nome, SituacaoEntrada situacao = SituacaoEntrada.Removida) =>
        new() { Execucao = new Guid(execucao, 0, 0, new byte[8]), Tipo = tipo, Tabela = _tabela, Nome = nome, Situacao = situacao };

    private static Task<DialetoProtegido> ConectarAsync(ProvedorDoPlanejar provedor) =>
        DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Destino, new SegredoConexao("destino"), TestContext.Current.CancellationToken);
}
