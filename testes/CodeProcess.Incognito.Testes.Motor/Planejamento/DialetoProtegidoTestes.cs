using System.Collections;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O motor chama o dialeto só pelo envoltório (critério 7 do planejar): o que o dialeto lança, em cada membro chamado,
/// sai traduzido, sem a mensagem, os dados, a interna nem o texto da exceção original, e com o código de saída pelo tipo.
/// </summary>
public sealed class DialetoProtegidoTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    // Cada membro que o planejar chama, com o lado.
    public static TheoryData<Operacao, bool> Membros => new()
    {
        { Operacao.Nome, false },
        { Operacao.Conectar, false },
        { Operacao.LerEsquema, false },
        { Operacao.LerIdentidade, false },
        { Operacao.Descartar, false },
        { Operacao.Conectar, true },
        { Operacao.LerEsquema, true },
        { Operacao.LerIdentidade, true },
        { Operacao.Descartar, true },
        { Operacao.ObterEsquema, false },
        { Operacao.ObterIdentidade, true },
    };

    // O que o motor pedia, como a mensagem o diz.
    private static readonly Dictionary<Operacao, string> _operacoes = new()
    {
        [Operacao.Nome] = "dizer o nome",
        [Operacao.Conectar] = "conectar",
        [Operacao.LerEsquema] = "ler o esquema",
        [Operacao.ObterEsquema] = "ler o esquema",
        [Operacao.LerIdentidade] = "ler a identidade",
        [Operacao.ObterIdentidade] = "ler a identidade",
        [Operacao.Descartar] = "fechar a conexão",
    };

    [Theory]
    [MemberData(nameof(Membros))]
    public async Task Excecao_do_dialeto_vira_falha_so_com_o_tipo(Operacao operacao, bool destino)
    {
        foreach (var sincrono in new[] { false, true })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = Em(operacao, destino, () => new ExcecaoDoDriver()), Sincrono = sincrono };

            var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

            Assert.Equal("INC3001", falha.Codigo);
            Assert.Contains(typeof(ExcecaoDoDriver).FullName!, falha.Message, StringComparison.Ordinal);
            Assert.Contains(operacao == Operacao.Nome ? "falhou ao dizer o nome (" : $"falhou ao {_operacoes[operacao]} {(destino ? "no destino" : "na origem")}", falha.Message, StringComparison.Ordinal);
            Assert.Null(falha.InnerException);
            Assert.Equal(1, ClassificadorDeSaida.De(falha));
            SemVazamento(falha);
        }
    }

    [Fact]
    public async Task A_excecao_agregada_e_a_citacao_tambem_saem_so_com_o_tipo()
    {
        var agregada = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerEsquema, true, () => new AggregateException(new ExcecaoDoDriver(), new ExcecaoDoDriver())) };
        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(agregada), exactMatch: true);
        Assert.Contains(nameof(AggregateException), falha.Message, StringComparison.Ordinal);
        SemVazamento(falha);

        foreach (var operacao in new[] { Operacao.Citar, Operacao.ObterSql })
        {
            var citacao = new ProvedorDoPlanejar { Lancar = Em(operacao, false, () => new ExcecaoDoDriver()) };
            await using var banco = await DialetoProtegido.ConectarAsync(citacao, "postgresql", LadoBanco.Origem, Conexao(), TestContext.Current.CancellationToken);
            var nome = Assert.Throws<FalhaDialetoException>(() => banco.Citar("cliente"));
            var objeto = Assert.Throws<FalhaDialetoException>(() => banco.Citar(new NomeObjeto("loja", "cliente")));
            Assert.All([nome, objeto], erro => Assert.Contains("citar um nome na origem", erro.Message, StringComparison.Ordinal));
            SemVazamento(nome);
            SemVazamento(objeto);
        }
    }

    [Fact]
    public async Task A_agregada_hostil_nunca_tem_a_mensagem_lida()
    {
        // O Flatten leria a Message, que é virtual: com o cancelamento pedido ou não, nada do dialeto roda na tradução.
        foreach (var cancelado in new[] { false, true })
        {
            using var fonte = new CancellationTokenSource();
            var provedor = new ProvedorDoPlanejar
            {
                Lancar = Em(Operacao.LerEsquema, false, () =>
                {
                    if (cancelado)
                    {
                        fonte.Cancel();
                    }

                    return new AgregadaHostil(new OperationCanceledException(Sentinela, fonte.Token));
                }),
            };

            var excecao = await FalharAsync(provedor, fonte.Token);

            Assert.IsType(cancelado ? typeof(OperationCanceledException) : typeof(FalhaDialetoException), excecao, exactMatch: true);
            SemVazamento(excecao);
        }
    }

    [Fact]
    public async Task A_recusa_so_e_aceita_ao_conectar_e_ler()
    {
        // Ao ler, a recusa (a partição pendente do PostgreSQL) vira erro do plano, e o banco é fechado; ao dizer o nome, ao
        // citar e ao fechar, é falha do dialeto.
        var leitura = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerEsquema, true, () => new DialetoRecusouException("INC3105", new string('p', 700))) };
        var recusa = Assert.IsType<PlanoRecusadoException>(await FalharAsync(leitura), exactMatch: true);
        Assert.Equal("INC3105", Assert.Single(recusa.Erros).Codigo);
        Assert.Equal(new string('p', TextoSeguro.MaximoDaMensagem) + "…", recusa.Erros[0].Mensagem);
        Assert.Contains("Descartar:destino", leitura.Chamadas);

        var nome = new ProvedorDoPlanejar { Lancar = Em(Operacao.Nome, false, () => new DialetoRecusouException("INC3150", "recusa no nome")) };
        Assert.Equal("INC3150", Assert.IsType<FalhaDialetoException>(await FalharAsync(nome), exactMatch: true).Codigo);

        var citacao = new ProvedorDoPlanejar { Lancar = Em(Operacao.Citar, true, () => new DialetoRecusouException("INC3150", "recusa na citação")) };
        await using var banco = await DialetoProtegido.ConectarAsync(citacao, "postgresql", LadoBanco.Destino, Conexao(), TestContext.Current.CancellationToken);
        Assert.Equal("INC3150", Assert.Throws<FalhaDialetoException>(() => banco.Citar("x")).Codigo);
    }

    [Fact]
    public async Task O_fechamento_sai_so_com_o_tipo_e_nunca_toma_o_lugar_do_erro()
    {
        Exception[] noDescarte = [new DialetoRecusouException("INC3150", Sentinela), new IncognitoException("INC3150", Sentinela), new IncognitoHostil("INC3150")];
        foreach (var excecao in noDescarte)
        {
            // Sem erro antes: falha só com o tipo, nunca recusa nem a mensagem do dialeto.
            var sozinho = new ProvedorDoPlanejar { Lancar = Em(Operacao.Descartar, false, () => excecao) };
            var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(sozinho), exactMatch: true);
            Assert.Equal("INC3001", falha.Codigo);
            Assert.Contains("fechar a conexão na origem", falha.Message, StringComparison.Ordinal);
            SemVazamento(falha);

            // Depois de um erro da leitura: sai o da leitura.
            var depois = new ProvedorDoPlanejar
            {
                Lancar = (operacao, destino) => destino ? null : operacao == Operacao.LerIdentidade ? new ExcecaoDoDriver() : operacao == Operacao.Descartar ? excecao : null,
            };
            var primeiro = Assert.IsType<FalhaDialetoException>(await FalharAsync(depois), exactMatch: true);
            Assert.Contains("ler a identidade na origem", primeiro.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task O_token_do_chamador_chega_ao_dialeto_e_a_regra_invalida_do_dialeto_sai_so_com_o_codigo()
    {
        using var fonte = new CancellationTokenSource();
        var provedor = new ProvedorDoPlanejar();
        await PlanejarAsync(provedor, cancelamento: fonte.Token);
        Assert.Equal(6, provedor.Tokens.Count);
        Assert.All(provedor.Tokens, token => Assert.Equal(fonte.Token, token));

        // Uma subclasse de IncognitoException do núcleo vinda do dialeto não se passa por configuração.
        var regra = new ProvedorDoPlanejar { Lancar = Em(Operacao.Conectar, false, () => new Transformadores.RegraInvalidaException(Sentinela)) };
        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(regra), exactMatch: true);
        Assert.Equal(("INC3001", 1), (falha.Codigo, ClassificadorDeSaida.De(falha)));
        SemVazamento(falha);
    }

    [Theory]
    [InlineData(Sentinela)]
    [InlineData("INC31" + Sentinela)]
    [InlineData("INC31١٢")]
    [InlineData("INC2001")]
    [InlineData("INC3001")]
    [InlineData("INC3500")]
    [InlineData("INC31000")]
    [InlineData("inc3101")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Codigo_fora_da_faixa_dos_dialetos_vira_falha_generica(string? codigo)
    {
        foreach (var excecao in new Func<Exception>[]
        {
            () => new IncognitoException(codigo!, Sentinela),
            () => new IncognitoHostil(codigo!),
            () => new DialetoRecusouException(codigo!, Sentinela),
        })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerEsquema, false, excecao) };

            var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

            // Nunca configuração: um dialeto não se passa por erro do YAML nem do planejar.
            Assert.Equal("INC3001", falha.Codigo);
            Assert.Equal(1, ClassificadorDeSaida.De(falha));
            SemVazamento(falha);
        }
    }

    [Fact]
    public async Task A_subclasse_hostil_com_codigo_da_faixa_sai_so_com_o_codigo()
    {
        foreach (var lancaAoLer in new[] { false, true })
        {
            var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.Conectar, true, () => new IncognitoHostil("INC3150", lancaAoLer)) };

            var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

            Assert.Equal("INC3150", falha.Codigo);
            Assert.Null(falha.InnerException);
            SemVazamento(falha);
        }
    }

    [Fact]
    public async Task A_excecao_do_dialeto_de_tipo_exato_guarda_o_codigo_e_a_mensagem_escapada_e_cortada()
    {
        var mensagem = "nome \u001b[31m \\u000A \u202Eatrás\n" + new string('x', 600);
        var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerIdentidade, false, () => new IncognitoException("INC3150", mensagem, new ExcecaoDoDriver())) };

        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

        Assert.Equal("INC3150", falha.Codigo);
        Assert.StartsWith("nome \\u001B[31m \\\\u000A \\u202Eatrás\\u000A", falha.Message, StringComparison.Ordinal);
        Assert.EndsWith("…", falha.Message, StringComparison.Ordinal);
        Assert.True(falha.Message.Length <= TextoSeguro.MaximoDaMensagem + 1);
        Assert.Null(falha.InnerException);
        Assert.Empty(falha.Data);
    }

    [Fact]
    public async Task A_recusa_do_dialeto_vira_erro_do_plano_com_saida_2()
    {
        var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.Conectar, false, () => new DialetoRecusouException("INC3102", "opção recusada \u202E")) };

        var recusa = Assert.IsType<PlanoRecusadoException>(await FalharAsync(provedor), exactMatch: true);

        var erro = Assert.Single(recusa.Erros);
        Assert.Equal(("INC3000", "INC3102", "opção recusada \\u202E"), (recusa.Codigo, erro.Codigo, erro.Mensagem));
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.Null(recusa.InnerException);
    }

    [Fact]
    public async Task O_cancelamento_pedido_sai_como_cancelamento_sem_a_interna_mesmo_por_token_vinculado()
    {
        foreach (var agregado in new[] { false, true })
        {
            using var fonte = new CancellationTokenSource();
            var provedor = new ProvedorDoPlanejar
            {
                Lancar = Em(Operacao.LerEsquema, true, () =>
                {
                    fonte.Cancel();
                    using var vinculado = CancellationTokenSource.CreateLinkedTokenSource(fonte.Token);
                    var cancelado = new OperationCanceledException(Sentinela, new ExcecaoDoDriver(), vinculado.Token);
                    return agregado ? new AggregateException(cancelado, new TaskCanceledException(Sentinela)) : cancelado;
                }),
            };

            var excecao = await FalharAsync(provedor, fonte.Token);

            var cancelamento = Assert.IsType<OperationCanceledException>(excecao, exactMatch: true);
            Assert.Null(cancelamento.InnerException);
            Assert.Equal(fonte.Token, cancelamento.CancellationToken);
            Assert.Equal(130, ClassificadorDeSaida.De(cancelamento));
            SemVazamento(cancelamento);
        }
    }

    [Fact]
    public async Task A_agregada_aninhada_so_com_cancelamentos_e_cancelamento_e_a_mista_e_falha()
    {
        foreach (var (mista, esperado) in new[] { (false, typeof(OperationCanceledException)), (true, typeof(FalhaDialetoException)) })
        {
            using var fonte = new CancellationTokenSource();
            var provedor = new ProvedorDoPlanejar
            {
                Lancar = Em(Operacao.LerIdentidade, false, () =>
                {
                    fonte.Cancel();
                    var cancelado = new OperationCanceledException(Sentinela, fonte.Token);
                    // Na mista, a falha fica dentro da agregada aninhada.
                    return mista
                        ? new AggregateException(new AggregateException(new ExcecaoDoDriver()), cancelado)
                        : new AggregateException(new AggregateException(cancelado), new TaskCanceledException(Sentinela));
                }),
            };

            var excecao = await FalharAsync(provedor, fonte.Token);

            Assert.IsType(esperado, excecao, exactMatch: true);
            SemVazamento(excecao);
        }
    }

    [Fact]
    public async Task O_cancelamento_que_o_chamador_nao_pediu_e_falha_do_dialeto()
    {
        var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.LerEsquema, false, () => new OperationCanceledException(Sentinela, new ExcecaoDoDriver())) };

        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

        Assert.Equal("INC3001", falha.Codigo);
        Assert.Equal(1, ClassificadorDeSaida.De(falha));
        SemVazamento(falha);
    }

    [Fact]
    public async Task O_erro_do_descarte_do_driver_nunca_toma_o_lugar_do_erro_da_leitura()
    {
        var provedor = new ProvedorDoPlanejar
        {
            Lancar = (operacao, destino) => !destino && operacao is Operacao.LerEsquema or Operacao.Descartar ? new ExcecaoDoDriver() : null,
        };

        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

        Assert.Contains("ler o esquema na origem", falha.Message, StringComparison.Ordinal);
        Assert.Contains("Descartar:origem", provedor.Chamadas);
        Assert.DoesNotContain("Conectar:destino", provedor.Chamadas);
    }

    [Fact]
    public async Task O_dialeto_que_devolve_nulo_falha_sem_quebrar_o_motor()
    {
        foreach (var operacao in new[] { Operacao.Conectar, Operacao.LerEsquema, Operacao.LerIdentidade })
        {
            var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(new ProvedorDoPlanejar { Nulo = operacao }), exactMatch: true);

            Assert.Equal("INC3001", falha.Codigo);
            Assert.Contains("não devolveu nada", falha.Message, StringComparison.Ordinal);
        }

        var provedor = new ProvedorDoPlanejar { Nulo = Operacao.Citar };
        await using var banco = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Destino, Conexao(), TestContext.Current.CancellationToken);
        Assert.Equal("INC3001", Assert.Throws<FalhaDialetoException>(() => banco.Citar("x")).Codigo);
    }

    [Fact]
    public async Task O_motor_nunca_chama_o_texto_a_igualdade_nem_o_hash_das_partes()
    {
        // As partes do falso lançam com o sentinela nessas três chamadas: o planejar inteiro passa sem tocá-las.
        var provedor = new ProvedorDoPlanejar();

        var leituras = await PlanejarAsync(provedor);

        Assert.Equal("loja_homolog", leituras.Destino.Identidade.Banco);
        Assert.Equal(
            ["Nome:origem", "Nome:origem", "Conectar:origem", "LerEsquema:origem", "LerIdentidade:origem", "Descartar:origem", "Conectar:destino", "LerEsquema:destino", "LerIdentidade:destino", "Descartar:destino"],
            provedor.Chamadas);
    }

    [Fact]
    public async Task O_descarte_do_envoltorio_e_unico_e_traduz_a_falha()
    {
        var provedor = new ProvedorDoPlanejar { Lancar = Em(Operacao.Descartar, false, () => new ExcecaoDoDriver()) };
        var banco = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Origem, Conexao(), TestContext.Current.CancellationToken);

        var falha = await Assert.ThrowsAsync<FalhaDialetoException>(async () => await banco.DisposeAsync());
        await banco.DisposeAsync();

        Assert.Contains("fechar a conexão na origem", falha.Message, StringComparison.Ordinal);
        Assert.Single(provedor.Chamadas, chamada => chamada == "Descartar:origem");
        SemVazamento(falha);
    }

    internal static Func<Operacao, bool, Exception?> Em(Operacao operacao, bool destino, Func<Exception> excecao) =>
        (chamada, lado) => chamada == operacao && (lado == destino || chamada == Operacao.Nome) ? excecao() : null;

    internal static async Task<Exception> FalharAsync(ProvedorDoPlanejar provedor, CancellationToken? cancelamento = null) =>
        await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(provedor, cancelamento: cancelamento));

    // O token do teste, salvo quando o caso pede outro.
    internal static Task<LeiturasDoPlano> PlanejarAsync(
        ProvedorDoPlanejar? provedor, string yaml = Yaml.Minimo, List<ConexaoBanco>? resolvidas = null, CancellationToken? cancelamento = null) =>
        PlanejadorInterno.PlanejarAsync(
            ConfiguracaoIncognito.LerYaml(yaml),
            new RegistroTransformadores(),
            provedor is null ? new Dictionary<ProvedorBanco, ProvedorDialeto>() : new Dictionary<ProvedorBanco, ProvedorDialeto> { [ProvedorBanco.PostgreSql] = provedor },
            conexao =>
            {
                resolvidas?.Add(conexao);
                return Conexao();
            },
            cancelamento ?? TestContext.Current.CancellationToken);

    // A busca do sentinela ignora a caixa: o Npgsql ecoa a palavra-chave desconhecida em minúsculas.
    internal static void SemVazamento(Exception excecao)
    {
        for (Exception? atual = excecao; atual is not null; atual = atual.InnerException)
        {
            var textos = new[] { atual.Message, atual.ToString() }
                .Concat(atual.Data.Cast<DictionaryEntry>().Select(entrada => $"{entrada.Key}={entrada.Value}"));
            Assert.All(textos, texto => Assert.DoesNotContain(Sentinela, texto, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static SegredoConexao Conexao() => new("Host=servidor;Password=" + Sentinela);
}
