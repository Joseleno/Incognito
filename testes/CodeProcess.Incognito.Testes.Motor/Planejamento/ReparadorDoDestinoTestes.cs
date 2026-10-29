using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.ProtecaoDoDestinoTestes;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O reparo e o diário da execução no motor, pela ordem das chamadas ao dialeto: as triggers antes das chaves, todas as
/// recriações antes de qualquer validação, o bloqueio conferido antes de cada escrita, a perda que para tudo, a falha de
/// uma entrada que não trava as outras, e nenhuma saída com o texto do dialeto.
/// </summary>
public sealed class ReparadorDoDestinoTestes
{
    private static readonly Guid _interrompida = Guid.Parse("6f9619ff-8b86-4011-b42d-00c04fc964ff");

    private static readonly TimeProvider _relogio = TimeProvider.System;

    [Fact]
    public async Task O_reparo_religa_recria_tudo_valida_e_marca_a_interrompida_conferindo_o_bloqueio_antes_de_cada_escrita()
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = Pendente(), NaRestauracao = SituacaoEntrada.SemValidacao };

        var resultado = await RepararAsync(provedor, validar: true);

        Assert.Equal(
            ["ConferirBloqueio", "LerDiario", "ConferirBloqueio", "ReligarTriggers", "ConferirBloqueio", "RecriarChave", "ConferirBloqueio", "RecriarChave", "ConferirBloqueio", "ValidarChave", "ConferirBloqueio", "ValidarChave", "ConferirBloqueio", "EncerrarExecucao"],
            DoDiario(provedor));
        Assert.Equal(["fk_a", "fk_b"], resultado.Restauracao.Pendentes.Select(chave => chave.Nome));
        Assert.All(resultado.Restauracao.Pendentes, chave => Assert.Equal(SituacaoEntrada.SemValidacao, chave.Situacao));
        Assert.Equal((1, 4), (resultado.Interrompidas, ClassificadorDeSaida.De(resultado)));
        Assert.Equal([("INC4002", "fk_a"), ("INC4002", "fk_b")], resultado.Restauracao.Ocorrencias().Select(aviso => (aviso.Codigo, aviso.Objeto!)));
        Assert.All(resultado.Restauracao.Ocorrencias(), aviso => Assert.Equal("loja.vendas", aviso.Tabela?.ToString()));
    }

    [Fact]
    public async Task Sem_validar_as_chaves_que_voltam_sem_validacao_ficam_no_diario()
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = Pendente(), NaRestauracao = SituacaoEntrada.SemValidacao };

        var resultado = await RepararAsync(provedor, validar: false);

        Assert.DoesNotContain("ValidarChave", DoDiario(provedor));
        Assert.Equal(2, resultado.Restauracao.Pendentes.Count);
    }

    [Fact]
    public async Task A_chave_que_volta_inteira_sai_do_resultado_e_a_saida_e_zero()
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = Pendente() };

        var resultado = await RepararAsync(provedor, validar: true);

        Assert.Empty(resultado.Restauracao.Pendentes);
        Assert.Equal(0, ClassificadorDeSaida.De(resultado));
    }

    [Fact]
    public async Task A_perda_do_bloqueio_no_meio_para_tudo_sem_escrever_mais_nada()
    {
        ProvedorDoPlanejar? provedor = null;
        provedor = new ProvedorDoPlanejar
        {
            EstadoDoDiario = Pendente(),
            NaRestauracao = SituacaoEntrada.SemValidacao,
            NoDiario = (operacao, _) =>
            {
                if (operacao == Operacao.RecriarChave)
                {
                    provedor!.Concessoes[0].Perder();
                }

                return Task.CompletedTask;
            },
        };

        var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => RepararAsync(provedor, validar: true));

        Assert.Equal(("INC4001", 1), (perda.Codigo, ClassificadorDeSaida.De(perda)));
        var depois = DoDiario(provedor).SkipWhile(chamada => chamada != "RecriarChave").Skip(1);
        Assert.DoesNotContain(depois, chamada => chamada is "RecriarChave" or "ValidarChave" or "EncerrarExecucao");
    }

    [Fact]
    public async Task A_perda_durante_a_ultima_recriacao_da_fase_sai_como_perda_e_nao_como_falha_da_entrada()
    {
        // Sem batimento depois da última chave da fase: a perda dentro da operação é que precisa subir.
        ProvedorDoPlanejar? provedor = null;
        provedor = new ProvedorDoPlanejar
        {
            EstadoDoDiario = new() { Existe = true, Execucoes = Pendente().Execucoes, Pendentes = [Pendente().Pendentes[^1]] },
            NoDiario = async (operacao, token) =>
            {
                if (operacao == Operacao.RecriarChave)
                {
                    provedor!.Concessoes[0].Perder();
                    await Task.Delay(Timeout.Infinite, token);
                }
            },
        };
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

            var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => DiarioDoDestino.RecriarEValidarAsync(destino, concessao, _interrompida, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            Assert.Equal("INC4001", perda.Codigo);
        }
    }

    [Fact]
    public async Task A_restauracao_da_execucao_so_toca_as_entradas_dela()
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = Pendente() };
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

            var resultado = await DiarioDoDestino.RestaurarAsync(destino, concessao, Guid.NewGuid(), _relogio, TestContext.Current.CancellationToken);

            Assert.Equal(0, ClassificadorDeSaida.De(resultado));
        }

        Assert.DoesNotContain(DoDiario(provedor), chamada => chamada is "ReligarTriggers" or "RecriarChave" or "ValidarChave");
    }

    [Fact]
    public async Task A_falha_de_uma_entrada_nao_trava_as_outras_e_nao_traz_o_texto_do_dialeto()
    {
        var provedor = new ProvedorDoPlanejar
        {
            EstadoDoDiario = Pendente(),
            Lancar = (operacao, _) => operacao == Operacao.RecriarChave ? new DialetoRecusouException("INC3118", "outra definição: " + ProvedorDoPlanejar.Sentinela) : null,
        };

        var resultado = await RepararAsync(provedor, validar: true);

        Assert.Equal(["fk_a", "fk_b"], resultado.Restauracao.Falhas.Select(falha => falha.Entrada.Nome));
        Assert.All(resultado.Restauracao.Falhas, falha => Assert.Equal("INC3118", falha.Codigo));
        Assert.Equal(1, ClassificadorDeSaida.De(resultado));
        Assert.Contains("EncerrarExecucao", DoDiario(provedor));
        Assert.DoesNotContain(ProvedorDoPlanejar.Sentinela, resultado.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ProvedorDoPlanejar.Sentinela, string.Join(";", resultado.Restauracao.Falhas), StringComparison.Ordinal);
        var ocorrencias = resultado.Restauracao.Ocorrencias();
        Assert.Equal(["INC4003", "INC4003"], ocorrencias.Select(aviso => aviso.Codigo));
        Assert.All(ocorrencias, aviso => Assert.Contains("com o código INC3118", aviso.Mensagem, StringComparison.Ordinal));
        Assert.All(ocorrencias, aviso => Assert.DoesNotContain(ProvedorDoPlanejar.Sentinela, aviso.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_trigger_pulada_vira_aviso_com_o_nome_escapado()
    {
        var provedor = new ProvedorDoPlanejar
        {
            EstadoDoDiario = Pendente(),
            Puladas = [new TriggerDoDiario { Tabela = Nome("pedidos_1"), Nome = "tg\u001B[2J" }],
        };

        var aviso = Assert.Single((await RepararAsync(provedor, validar: true)).Restauracao.Avisos);

        Assert.Equal(("INC4004", "loja.pedidos_1"), (aviso.Codigo, aviso.Tabela?.ToString()));
        Assert.DoesNotContain('\u001B', aviso.Mensagem);
        Assert.DoesNotContain('\u001B', aviso.Objeto!);
        Assert.Contains("não foi religada", aviso.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_preparo_registra_remove_as_da_carga_e_as_pendentes_sem_repetir_e_desliga_as_raizes()
    {
        var provedor = new ProvedorDoPlanejar
        {
            EstadoDoDiario = new()
            {
                Existe = true,
                Execucoes = [new() { Id = _interrompida, Marcador = Marcado.Identificador!.Value, IniciadaEm = DateTimeOffset.UnixEpoch, Situacao = SituacaoExecucao.Concluida }],
                Pendentes =
                [
                    new() { Execucao = _interrompida, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = Nome("vendas"), Nome = "fk_a", Situacao = SituacaoEntrada.SemValidacao },
                    new() { Execucao = _interrompida, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = Nome("vendas"), Nome = "fk_c", Situacao = SituacaoEntrada.Removida },
                ],
            },
        };
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;

            await DiarioDoDestino.PrepararAsync(
                destino,
                concessao,
                Marcado.Identificador!.Value,
                [new ChaveRemovida(new NomeTabela("loja", "pedidos"), "fk_pedidos"), new ChaveRemovida(new NomeTabela("loja", "vendas"), "fk_a")],
                [Nome("pedidos")],
                _relogio,
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            ["ConferirBloqueio", "IniciarExecucao", "LerDiario", "ConferirBloqueio", "RemoverChave", "ConferirBloqueio", "RemoverChave", "ConferirBloqueio", "RemoverChave", "ConferirBloqueio", "DesligarTriggers"],
            DoDiario(provedor));
    }

    // Uma execução interrompida com as triggers de uma tabela e duas chaves removidas, na ordem de restauração.
    private static EstadoDiario Pendente() => new()
    {
        Existe = true,
        Execucoes = [new() { Id = _interrompida, Marcador = Marcado.Identificador!.Value, IniciadaEm = DateTimeOffset.UnixEpoch, Situacao = SituacaoExecucao.EmAndamento }],
        Pendentes =
        [
            new() { Execucao = _interrompida, Tipo = TipoEntrada.Triggers, Tabela = Nome("pedidos"), Situacao = SituacaoEntrada.Removida },
            new() { Execucao = _interrompida, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = Nome("vendas"), Nome = "fk_a", Situacao = SituacaoEntrada.Removida },
            new() { Execucao = _interrompida, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = Nome("vendas"), Nome = "fk_b", Situacao = SituacaoEntrada.Removida },
        ],
    };

    private static async Task<ResultadoDoReparo> RepararAsync(ProvedorDoPlanejar provedor, bool validar)
    {
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            await using var concessao = (await destino.TentarAdquirirBloqueioAsync(TestContext.Current.CancellationToken))!;
            return await ReparadorDoDestino.RepararAsync(destino, concessao, Marcado.Identificador!.Value, new OpcoesDoReparo(validar), _relogio, TestContext.Current.CancellationToken);
        }
    }

    // As chamadas ao diário e ao batimento, sem o lado, depois de adquirir o bloqueio.
    private static string[] DoDiario(ProvedorDoPlanejar provedor) =>
    [
        .. provedor.Chamadas
            .SkipWhile(chamada => chamada != "AdquirirBloqueio:destino")
            .Skip(1)
            .Select(chamada => chamada.Split(':')[0])
            .Where(chamada => chamada is not ("LiberarBloqueio" or "Descartar")),
    ];
}
