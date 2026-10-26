using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.ProtecaoDoDestinoTestes;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O <c>preparar-destino</c>: grava um marcador com identificador aleatório e a data em segundos só depois da confirmação e
/// com o bloqueio vivo, e confere a gravação relendo; é idempotente com o marcador válido; nunca sobrescreve um comentário
/// alheio ou ilegível; confere o <c>bancoEsperado</c> e recusa o destino que é o próprio banco da origem.
/// </summary>
public sealed class PreparadorDoDestinoTestes
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 26, 9, 34, 56, 789, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Grava_um_marcador_aleatorio_com_a_data_em_segundos_depois_da_confirmacao()
    {
        var identificadores = new List<Guid>();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var provedor = QueGrava();

            var resultado = await PrepararAsync(provedor);

            var (identificador, gravadoEm) = Assert.Single(provedor.Gravados);
            Assert.Equal((SituacaoDoPreparo.Gravado, identificador), (resultado.Situacao, resultado.Identificador));
            Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 34, 56, TimeSpan.Zero), gravadoEm);
            Assert.Equal(TimeSpan.Zero, gravadoEm.Offset);
            Assert.Equal(4, identificador.Version);
            Assert.True(Assert.Single(provedor.Concessoes).Liberada);

            // O batimento vem depois da confirmação e antes da gravação.
            var chamadas = provedor.Chamadas;
            Assert.True(chamadas.IndexOf("ConferirBloqueio:destino") < chamadas.IndexOf("GravarMarcador:destino"));
            identificadores.Add(identificador);
        }

        Assert.NotEqual(identificadores[0], identificadores[1]);
    }

    [Fact]
    public async Task Sem_a_confirmacao_nada_e_gravado()
    {
        var provedor = QueGrava();

        var resultado = await PrepararAsync(provedor, confirmar: (_, _) => Task.FromResult(false));

        Assert.Equal((SituacaoDoPreparo.Negado, null), (resultado.Situacao, resultado.Identificador));
        Assert.Empty(provedor.Gravados);
        Assert.True(Assert.Single(provedor.Concessoes).Liberada);
    }

    [Fact]
    public async Task Com_o_marcador_valido_nao_grava_nem_pergunta()
    {
        var provedor = new ProvedorDoPlanejar();
        var perguntas = 0;

        var resultado = await PrepararAsync(provedor, confirmar: (_, _) =>
        {
            perguntas++;
            return Task.FromResult(true);
        });

        Assert.Equal((SituacaoDoPreparo.JaPreparado, Marcado.Identificador), (resultado.Situacao, resultado.Identificador));
        Assert.Equal(0, perguntas);
        SemEscrita(provedor);
        Assert.True(Assert.Single(provedor.Concessoes).Liberada);
    }

    [Fact]
    public async Task O_comentario_alheio_ilegivel_ou_nao_informado_e_recusado_sem_sobrescrever()
    {
        foreach (var marcador in new MarcadorLido?[] { new() { Situacao = SituacaoMarcador.Alheio }, new() { Situacao = SituacaoMarcador.Invalido }, null })
        {
            var provedor = new ProvedorDoPlanejar { Identidades = lado => lado ? Identidade("loja_homolog").Com(marcador) : Identidade("loja") };

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PrepararAsync(provedor));

            Assert.Equal("INC3605", Assert.Single(recusa.Erros).Codigo);
            Assert.Equal(2, ClassificadorDeSaida.De(recusa));
            SemEscrita(provedor);
            Assert.True(Assert.Single(provedor.Concessoes).Liberada);
        }
    }

    [Fact]
    public async Task O_banco_esperado_diferente_e_recusado_mesmo_com_o_marcador_valido()
    {
        foreach (var marcador in new[] { Ausente, Marcado })
        {
            var provedor = new ProvedorDoPlanejar { Identidades = lado => lado ? BancoFalso.Clonar(Identidade("producao"), (nameof(IdentidadeLida.Marcador), marcador)) : Identidade("loja") };

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PrepararAsync(provedor));

            Assert.Equal("INC3603", Assert.Single(recusa.Erros).Codigo);
            SemEscrita(provedor);
        }
    }

    [Fact]
    public async Task O_destino_que_e_a_propria_origem_e_recusado_e_a_linhagem_igual_vai_para_a_confirmacao()
    {
        var linhagem = new Dictionary<string, string> { ["cluster"] = "7", ["banco"] = "16384" };
        var origem = BancoFalso.Clonar(Identidade("loja"), (nameof(IdentidadeLida.Linhagem), linhagem));
        var mesmo = BancoFalso.Clonar(origem, (nameof(IdentidadeLida.Banco), "loja_homolog"));

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PrepararAsync(QueGrava(origem, mesmo)));
        Assert.Equal("INC3606", Assert.Single(recusa.Erros).Codigo);

        // A cópia física em outro endereço: aceita, com o aviso na confirmação e no resultado.
        var copia = BancoFalso.Clonar(mesmo, (nameof(IdentidadeLida.Servidor), "destino-sintetico:5432"));
        var provedor = QueGrava(origem, copia);
        ResumoDoDestino? resumo = null;

        var resultado = await PrepararAsync(provedor, confirmar: (dado, _) =>
        {
            resumo = dado;
            return Task.FromResult(true);
        });

        Assert.Equal(SituacaoDoPreparo.Gravado, resultado.Situacao);
        Assert.Equal("INC3582", Assert.Single(resultado.Avisos).Codigo);
        Assert.Equal(resultado.Avisos, resumo!.Avisos);
        Assert.Equal(("destino-sintetico:5432", "loja_homolog"), (resumo.Servidor, resumo.Banco));
    }

    [Fact]
    public async Task Sem_a_origem_configurada_nada_e_comparado_com_ela()
    {
        var provedor = QueGrava(origem: Identidade("loja").Com(Marcado));
        var (_, destino) = await ConectarAsync(provedor);
        await using (destino)
        {
            var resultado = await PreparadorDoDestino.PrepararAsync(destino, null, "loja_homolog", Confirmar, new RelogioFixo(_agora), TestContext.Current.CancellationToken);

            Assert.Equal(SituacaoDoPreparo.Gravado, resultado.Situacao);
            Assert.DoesNotContain(provedor.Chamadas, chamada => chamada == "LerIdentidade:origem");
        }
    }

    [Fact]
    public async Task A_releitura_que_nao_confirma_e_falha_do_dialeto()
    {
        // O destino que grava e continua lendo o marcador ausente, ou lê outro identificador, ou o gravado com outra data.
        var gravadoEm = new DateTimeOffset(2026, 10, 26, 12, 34, 56, TimeSpan.Zero);
        var relidos = new Func<ProvedorDoPlanejar, MarcadorLido>[]
        {
            _ => Ausente,
            _ => new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = Guid.NewGuid(), GravadoEm = gravadoEm },
            provedor => new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = provedor.Gravados[0].Identificador, GravadoEm = gravadoEm.AddSeconds(1) },
        };
        foreach (var relido in relidos)
        {
            var leituras = 0;
            ProvedorDoPlanejar? provedor = null;
            provedor = new ProvedorDoPlanejar { Identidades = lado => lado ? Identidade("loja_homolog").Com(leituras++ == 0 ? Ausente : relido(provedor!)) : Identidade("loja") };

            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => PrepararAsync(provedor));

            Assert.Equal("INC3002", falha.Codigo);
            Assert.Single(provedor.Gravados);
            Assert.True(Assert.Single(provedor.Concessoes).Liberada);
        }
    }

    [Fact]
    public async Task Com_o_bloqueio_ocupado_sai_com_codigo_1_sem_ler_nem_gravar()
    {
        var provedor = new ProvedorDoPlanejar { Ocupado = true };

        var ocupado = await Assert.ThrowsAsync<BloqueioDestinoException>(() => PrepararAsync(provedor));

        Assert.Equal("INC3004", ocupado.Codigo);
        Assert.Equal(1, ClassificadorDeSaida.De(ocupado));
        Assert.DoesNotContain(provedor.Chamadas, chamada => chamada.StartsWith("LerIdentidade", StringComparison.Ordinal));
        SemEscrita(provedor);
    }

    [Fact]
    public async Task O_bloqueio_perdido_durante_a_confirmacao_nao_deixa_gravar()
    {
        var provedor = QueGrava();

        var perdido = await Assert.ThrowsAsync<BloqueioDestinoException>(() => PrepararAsync(provedor, confirmar: (_, _) =>
        {
            provedor.Concessoes[0].Perder();
            return Task.FromResult(true);
        }));

        Assert.Equal("INC4001", perdido.Codigo);
        Assert.Equal(1, ClassificadorDeSaida.De(perdido));
        Assert.Empty(provedor.Gravados);
        Assert.True(provedor.Concessoes[0].Liberada);
    }

    // O destino que relê o que gravou: ausente até gravar, e o último gravado depois.
    private static ProvedorDoPlanejar QueGrava(IdentidadeLida? origem = null, IdentidadeLida? destino = null)
    {
        ProvedorDoPlanejar? provedor = null;
        provedor = new ProvedorDoPlanejar
        {
            Identidades = lado =>
            {
                if (!lado)
                {
                    return origem ?? Identidade("loja");
                }

                lock (provedor!.Gravados)
                {
                    var marcador = provedor.Gravados.Count == 0
                        ? Ausente
                        : new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = provedor.Gravados[^1].Identificador, GravadoEm = provedor.Gravados[^1].GravadoEm };
                    return (destino ?? Identidade("loja_homolog")).Com(marcador);
                }
            },
        };
        return provedor;
    }

    private static Task<bool> Confirmar(ResumoDoDestino resumo, CancellationToken cancelamento) => Task.FromResult(true);

    private static async Task<ResultadoDoPreparo> PrepararAsync(ProvedorDoPlanejar provedor, Func<ResumoDoDestino, CancellationToken, Task<bool>>? confirmar = null)
    {
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            return await PreparadorDoDestino.PrepararAsync(destino, origem, "loja_homolog", confirmar ?? Confirmar, new RelogioFixo(_agora), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Um relógio parado.</summary>
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora.ToUniversalTime();
    }
}
