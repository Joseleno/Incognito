using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.DialetoProtegidoTestes;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// As partes do destino pelo envoltório (INC-035): a identidade com o marcador, a gravação do marcador e a existência de
/// linhas saem sem a mensagem do dialeto (o banco guarda texto de quem o administra, e o destino, dado real); o bloqueio
/// ocupado volta nulo; e a concessão protegida traduz a perda em <c>INC4001</c> (saída 1), nunca em cancelamento nem erro do
/// plano, e nunca lança no descarte.
/// </summary>
public sealed class DestinoNoEnvoltorioTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    private static readonly NomeObjeto _clientes = new("loja", "clientes");

    [Fact]
    public async Task A_identidade_sai_sem_a_mensagem_do_dialeto()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var casos = new (Func<Exception> Criar, bool Recusa, string Codigo)[]
        {
            (() => new DialetoRecusouException("INC3150", "recusa com " + Sentinela), true, "INC3150"),
            (() => new IncognitoException("INC3150", "falha com " + Sentinela), false, "INC3150"),
            (() => new ExcecaoDoDriver(), false, "INC3001"),
        };

        foreach (var (criar, recusa, codigo) in casos)
        {
            foreach (var destino in new[] { false, true })
            {
                var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerIdentidade, destino, criar) };
                await using var banco = await Conectar(provedor, destino);

                var excecao = await Assert.ThrowsAnyAsync<Exception>(() => banco.LerIdentidadeAsync(cancelamento));

                var codigoLido = recusa
                    ? Assert.Single(Assert.IsType<PlanoRecusadoException>(excecao, exactMatch: true).Erros).Codigo
                    : Assert.IsType<FalhaDialetoException>(excecao, exactMatch: true).Codigo;
                Assert.Equal(codigo, codigoLido);
                Assert.Equal(recusa ? 2 : 1, ClassificadorDeSaida.De(excecao));
                SemVazamento(excecao);
                if (excecao is PlanoRecusadoException plano)
                {
                    Assert.All(plano.Erros, erro => Assert.DoesNotContain(Sentinela, erro.ToString(), StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    [Fact]
    public async Task O_marcador_que_se_contradiz_e_falha_do_dialeto_e_o_coerente_passa()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var quando = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var contraditos = new[]
        {
            new MarcadorLido { Situacao = SituacaoMarcador.Valido },
            new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = Guid.Empty, GravadoEm = quando },
            new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = Guid.NewGuid() },
            new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = Guid.NewGuid(), GravadoEm = quando.ToOffset(TimeSpan.FromHours(-3)) },
            new MarcadorLido { Situacao = SituacaoMarcador.Alheio, Identificador = Guid.NewGuid() },
            new MarcadorLido { Situacao = SituacaoMarcador.Ausente, GravadoEm = quando },
            new MarcadorLido { Situacao = (SituacaoMarcador)9 },
        };

        foreach (var marcador in contraditos)
        {
            await using var banco = await Conectar(ComMarcador(marcador), destino: true);
            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => banco.LerIdentidadeAsync(cancelamento));
            Assert.Equal("INC3002", falha.Codigo);
        }

        foreach (var marcador in new MarcadorLido?[]
        {
            null,
            new() { Situacao = SituacaoMarcador.Ausente },
            new() { Situacao = SituacaoMarcador.Alheio },
            new() { Situacao = SituacaoMarcador.Invalido },
            new() { Situacao = SituacaoMarcador.Valido, Identificador = Guid.NewGuid(), GravadoEm = quando },
        })
        {
            await using var banco = await Conectar(ComMarcador(marcador), destino: false);
            Assert.Same(marcador, (await banco.LerIdentidadeAsync(cancelamento)).Marcador);
        }
    }

    [Fact]
    public async Task A_gravacao_do_marcador_e_a_leitura_de_linhas_sao_so_do_destino_e_sem_a_mensagem()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var quando = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var provedor = new ProvedorDoPlanejar { Linhas = tabela => tabela.Equals(_clientes) };
        await using (var destino = await Conectar(provedor, destino: true))
        {
            await destino.GravarMarcadorAsync(Guid.NewGuid(), quando, cancelamento);
            Assert.True(await destino.TemLinhasAsync(_clientes, cancelamento));
            Assert.False(await destino.TemLinhasAsync(new NomeObjeto("loja", "pedidos"), cancelamento));
        }

        // Na origem é defeito do motor: o dialeto nem é chamado.
        await using (var origem = await Conectar(provedor, destino: false))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => origem.GravarMarcadorAsync(Guid.NewGuid(), quando, cancelamento));
            await Assert.ThrowsAsync<InvalidOperationException>(() => origem.TemLinhasAsync(_clientes, cancelamento));
            await Assert.ThrowsAsync<InvalidOperationException>(() => origem.TentarAdquirirBloqueioAsync(cancelamento));
        }

        Assert.DoesNotContain(provedor.Chamadas, chamada => chamada is "GravarMarcador:origem" or "TemLinhas:origem" or "AdquirirBloqueio:origem");
        var casos = new (Func<Exception> Criar, bool Recusa, string Codigo)[]
        {
            (() => new DialetoRecusouException("INC3150", "o banco já tem " + Sentinela), true, "INC3150"),
            (() => new IncognitoException("INC3150", "falha com " + Sentinela), false, "INC3150"),
            (() => new ExcecaoDoDriver(), false, "INC3001"),
        };

        foreach (var (operacao, executar, texto) in new (Operacao, Func<DialetoProtegido, Task>, string)[]
        {
            (Operacao.GravarMarcador, banco => banco.GravarMarcadorAsync(Guid.NewGuid(), quando, cancelamento), "gravar o marcador no destino"),
            (Operacao.ObterMarcador, banco => banco.GravarMarcadorAsync(Guid.NewGuid(), quando, cancelamento), "gravar o marcador no destino"),
            (Operacao.TemLinhas, banco => banco.TemLinhasAsync(_clientes, cancelamento), "ler se uma tabela tem linhas no destino"),
            (Operacao.ObterDados, banco => banco.TemLinhasAsync(_clientes, cancelamento), "ler se uma tabela tem linhas no destino"),
        })
        {
            foreach (var (criar, recusa, codigo) in casos)
            {
                await using var banco = await Conectar(new ProvedorDoPlanejar { Lancar = Em(operacao, true, criar) }, destino: true);

                var excecao = await Assert.ThrowsAnyAsync<Exception>(() => executar(banco));

                var mensagem = recusa
                    ? Assert.Single(Assert.IsType<PlanoRecusadoException>(excecao, exactMatch: true).Erros) is var erro && erro.Codigo == codigo ? erro.Mensagem : throw new InvalidOperationException(erro.Codigo)
                    : Assert.IsType<FalhaDialetoException>(excecao, exactMatch: true) is var falha && falha.Codigo == codigo ? falha.Message : throw new InvalidOperationException(falha.Codigo);
                Assert.Contains(texto, mensagem, StringComparison.Ordinal);
                Assert.DoesNotContain(Sentinela, mensagem, StringComparison.OrdinalIgnoreCase);
                SemVazamento(excecao);
            }
        }
    }

    [Fact]
    public async Task O_bloqueio_ocupado_volta_nulo_e_a_recusa_ao_adquirir_e_falha()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using (var ocupado = await Conectar(new ProvedorDoPlanejar { Ocupado = true }, destino: true))
        {
            Assert.Null(await ocupado.TentarAdquirirBloqueioAsync(cancelamento));
        }

        foreach (var (criar, codigo) in new (Func<Exception>, string)[]
        {
            (() => new DialetoRecusouException("INC3150", "recusa com " + Sentinela), "INC3150"),
            (() => new InvalidOperationException("pedido duplo " + Sentinela), "INC3001"),
        })
        {
            foreach (var operacao in new[] { Operacao.AdquirirBloqueio, Operacao.ObterBloqueio })
            {
                await using var banco = await Conectar(new ProvedorDoPlanejar { Lancar = Em(operacao, true, criar) }, destino: true);
                var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => banco.TentarAdquirirBloqueioAsync(cancelamento));
                Assert.Equal((codigo, 1), (falha.Codigo, ClassificadorDeSaida.De(falha)));
                SemVazamento(falha);
            }
        }
    }

    [Fact]
    public async Task A_perda_do_bloqueio_sai_como_INC4001_com_saida_1_e_nunca_como_cancelamento()
    {
        var cancelamento = TestContext.Current.CancellationToken;

        // A falha do batimento, de qualquer tipo (também a recusa e o cancelamento que o chamador não pediu), é perda.
        foreach (var criar in new Func<Exception>[]
        {
            () => new ExcecaoDoDriver(),
            () => new IncognitoException("INC3110", "perdido com " + Sentinela),
            () => new DialetoRecusouException("INC3110", "recusa com " + Sentinela),
            () => new OperationCanceledException(Sentinela),
        })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.ConferirBloqueio, true, criar) };
            await using var banco = await Conectar(provedor, destino: true);
            await using var concessao = Assert.IsType<DialetoProtegido.ConcessaoProtegida>(await banco.TentarAdquirirBloqueioAsync(cancelamento));

            var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => concessao.ConferirAsync(cancelamento));

            Assert.Equal(("INC4001", 1), (perda.Codigo, ClassificadorDeSaida.De(perda)));
            Assert.Null(perda.InnerException);
            SemVazamento(perda);
            await Perdido(concessao);
        }

        // A perda que o dialeto percebe sozinho (a queda da conexão): o batimento seguinte nem chama o dialeto.
        var queda = new ProvedorDoPlanejar();
        await using (var banco = await Conectar(queda, destino: true))
        {
            await using var concessao = (await banco.TentarAdquirirBloqueioAsync(cancelamento))!;
            await concessao.ConferirAsync(cancelamento);
            Assert.Single(queda.Concessoes).Perder();

            Assert.Equal("INC4001", (await Assert.ThrowsAsync<BloqueioDestinoException>(() => concessao.ConferirAsync(cancelamento))).Codigo);
            Assert.Single(queda.Chamadas, chamada => chamada == "ConferirBloqueio:destino");
            await Perdido(concessao);
        }

        // O cancelamento pedido pelo chamador não é perda.
        using var fonte = new CancellationTokenSource();
        var cancelada = new ProvedorDoPlanejar { Lancar = Em(Operacao.ConferirBloqueio, true, () => new OperationCanceledException(Sentinela, fonte.Token)) };
        await using (var banco = await Conectar(cancelada, destino: true))
        {
            await using var concessao = (await banco.TentarAdquirirBloqueioAsync(cancelamento))!;
            await fonte.CancelAsync();
            var excecao = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => concessao.ConferirAsync(fonte.Token));
            Assert.Null(excecao.InnerException);
            Assert.False(concessao.Perdido.IsCancellationRequested);
            Assert.Equal(130, ClassificadorDeSaida.De(excecao));
        }
    }

    [Fact]
    public async Task O_descarte_da_concessao_libera_uma_vez_e_nunca_lanca()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        foreach (var falhar in new[] { false, true })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = falhar ? Em(Operacao.LiberarBloqueio, true, () => new ExcecaoDoDriver()) : (_, _) => null };
            await using var banco = await Conectar(provedor, destino: true);
            var concessao = (await banco.TentarAdquirirBloqueioAsync(cancelamento))!;

            await concessao.DisposeAsync();
            await concessao.DisposeAsync();

            Assert.True(Assert.Single(provedor.Concessoes).Liberada);
            Assert.Single(provedor.Chamadas, chamada => chamada == "LiberarBloqueio:destino");
            await Assert.ThrowsAsync<ObjectDisposedException>(() => concessao.ConferirAsync(cancelamento));
        }
    }

    // O Perdido protegido é cancelado fora da thread do dialeto: espera até um limite.
    private static async Task Perdido(DialetoProtegido.ConcessaoProtegida concessao)
    {
        var cancelado = new TaskCompletionSource();
        await using var registro = concessao.Perdido.Register(() => cancelado.TrySetResult());
        await cancelado.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static ProvedorDoPlanejar ComMarcador(MarcadorLido? marcador) => new()
    {
        Identidades = destino => new IdentidadeLida
        {
            Banco = destino ? "loja_homolog" : "loja",
            Servidor = "servidor",
            Linhagem = new Dictionary<string, string> { ["identificador"] = "1" },
            Marcador = marcador,
        },
    };

    private static Task<DialetoProtegido> Conectar(ProvedorDoPlanejar provedor, bool destino) =>
        DialetoProtegido.ConectarAsync(
            provedor, "postgresql", destino ? LadoBanco.Destino : LadoBanco.Origem, new SegredoConexao("Host=servidor;Password=" + Sentinela), TestContext.Current.CancellationToken);
}
