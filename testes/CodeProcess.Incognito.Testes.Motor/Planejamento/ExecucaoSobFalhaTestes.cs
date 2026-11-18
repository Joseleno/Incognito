using System.Collections.Concurrent;
using System.Text;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O executor sob falha (seção 4.6; 9.1 itens 2, 4 e 31): a falha, o Ctrl+C e a perda do bloqueio em cada ponto da cópia, o
/// descarte que espera o <c>Transformar</c> em andamento, e o sigilo: o sentinela da coluna tratada e a chave canário nunca
/// saem, em nenhum desfecho.
/// </summary>
public sealed class ExecucaoSobFalhaTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    // O sentinela como texto, em base64url e em hexadecimal.
    private static readonly string[] _formasDoSentinela =
    [
        Sentinela,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Sentinela)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        Convert.ToHexString(Encoding.UTF8.GetBytes(Sentinela)),
    ];

    [Theory]
    [InlineData("falha")]
    [InlineData("ctrl-c")]
    [InlineData("bloqueio")]
    public async Task Em_cada_ponto_da_copia_o_desfecho_e_o_da_causa_e_nada_fica_sem_observar(string causa)
    {
        var total = await PontosAsync();
        Assert.True(total >= 15, $"pontos: {total}");
        var naoObservadas = new ConcurrentBag<Exception>();
        void Observar(object? remetente, UnobservedTaskExceptionEventArgs evento)
        {
            if (DaVarredura(evento.Exception))
            {
                naoObservadas.Add(evento.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += Observar;
        try
        {
            for (var ponto = 1; ponto <= total; ponto++)
            {
                await NoPontoAsync(causa, ponto);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Observar;
        }

        Assert.Empty(naoObservadas);
    }

    [Fact]
    public async Task Os_transformadores_so_sao_descartados_depois_que_o_Transformar_em_andamento_volta()
    {
        using var entrou = new ManualResetEventSlim();
        using var liberar = new ManualResetEventSlim();
        var ordem = new ConcurrentQueue<string>();
        var emAndamento = 0;
        var descartesComAndamento = 0;
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                Interlocked.Increment(ref emAndamento);
                try
                {
                    switch (CenarioDaExecucao.LinhaDe(valor))
                    {
                        case 1:
                            entrou.Set();
                            liberar.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                            ordem.Enqueue("volta da linha 1");
                            break;
                        case 2:
                            Assert.True(entrou.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
                            _ = Task.Run(
                                async () =>
                                {
                                    await Task.Delay(200, TestContext.Current.CancellationToken);
                                    liberar.Set();
                                },
                                TestContext.Current.CancellationToken);
                            throw new FormatException(Sentinela);
                    }

                    return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
                }
                finally
                {
                    Interlocked.Decrement(ref emAndamento);
                }
            },
        });
        cenario.Fabrica.AoDescartar = () =>
        {
            ordem.Enqueue("descarte");
            if (Volatile.Read(ref emAndamento) > 0)
            {
                Interlocked.Increment(ref descartesComAndamento);
            }
        };

        var resultado = await cenario.ExecutarAsync();

        var falha = Assert.IsType<FalhaTransformacaoException>(resultado.Falha);
        Assert.Equal(2L, falha.Linha);
        Assert.Equal(0, descartesComAndamento);
        // Os descartes do planejar e do replanejamento vêm antes; o da execução, só depois da volta.
        Assert.Equal("descarte", ordem.Last());
        Assert.Equal(["volta da linha 1", "descarte"], ordem.SkipWhile(evento => evento == "descarte"));
        Assert.Equal((3, 3), (cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes));
    }

    [Theory]
    [InlineData("terceiro com o valor na mensagem, nos dados e na interna")]
    [InlineData("incognito de terceiro com o valor na mensagem e no código")]
    [InlineData("embutida com falhar")]
    [InlineData("sem representação com falhar")]
    [InlineData("driver na gravação")]
    [InlineData("driver na conclusão")]
    public async Task O_sentinela_da_coluna_tratada_nunca_sai(string caso)
    {
        var opcoes = caso switch
        {
            "terceiro com o valor na mensagem, nos dados e na interna" => new OpcoesDoCenario { Funcao = valor => throw new ExcecaoComValor((string)valor!) },
            "incognito de terceiro com o valor na mensagem e no código" => new OpcoesDoCenario { Funcao = valor => throw new IncognitoHostil((string)valor!) },
            "embutida com falhar" => new OpcoesDoCenario { Tabelas = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n" },
            "sem representação com falhar" => new OpcoesDoCenario { Cpf = id => id == 2 ? CopiaEmMemoria.SemForma : $"{Sentinela}-{id}" },
            "driver na gravação" => new OpcoesDoCenario { NoLoteGravado = numero => numero == 2 ? new ExcecaoDoDriver() : null },
            _ => new OpcoesDoCenario { NoConcluir = new ExcecaoDoDriver() },
        };
        var cenario = await CenarioDaExecucao.CriarAsync(opcoes);

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(1, resultado.Saida);
        var falha = Assert.IsAssignableFrom<IncognitoException>(resultado.Falha);
        Assert.Equal(
            caso switch
            {
                "sem representação com falhar" => "INC4006",
                "driver na gravação" or "driver na conclusão" => falha.Codigo,
                _ => "INC4007",
            },
            falha.Codigo);
        Assert.Empty(falha.Data);
        if (caso == "embutida com falhar")
        {
            Assert.Matches(@"\(INC[0-9]{4}: ", falha.Message);
        }

        SemSentinela(Textos(resultado));
    }

    [Fact]
    public async Task A_restauracao_que_falha_depois_de_outra_falha_avisa_so_com_o_tipo()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor => CenarioDaExecucao.LinhaDe(valor) == 2 ? throw new ExcecaoComValor((string)valor!) : new ResultadoTransformacao("TRATADO", Desfecho.Substituido),
            Lancar = (operacao, destino) => destino && operacao == Operacao.EncerrarExecucao ? new ExcecaoDoDriver() : null,
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal("INC4007", Assert.IsType<FalhaTransformacaoException>(resultado.Falha).Codigo);
        var aviso = Assert.Single(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.RestauracaoIncompleta);
        Assert.Contains("(FalhaDialetoException)", aviso.Mensagem, StringComparison.Ordinal);
        SemSentinela(Textos(resultado));
    }

    [Fact]
    public async Task A_chave_canario_do_resolvedor_nunca_sai_e_e_descartada_no_fim()
    {
        var bytes = Enumerable.Range(1, 32).Select(numero => (byte)numero).ToArray();
        var texto = "inc1_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string[] formas = [texto, texto[5..], Convert.ToHexString(bytes), Convert.ToBase64String(bytes), string.Join(",", bytes), string.Join(", ", bytes)];
        var chaves = new List<ChaveIncognito>();
        ChaveIncognito Canario(ReferenciaEscrita referencia)
        {
            var chave = ChaveIncognito.Ler(texto);
            chaves.Add(chave);
            return chave;
        }

        var textos = new List<string>();
        foreach (var opcoes in new[]
        {
            new OpcoesDoCenario(),
            new OpcoesDoCenario { Funcao = valor => CenarioDaExecucao.LinhaDe(valor) == 2 ? throw new ExcecaoComValor((string)valor!) : new ResultadoTransformacao("TRATADO", Desfecho.Substituido) },
            new OpcoesDoCenario { NoConcluir = new ExcecaoDoDriver() },
        })
        {
            var cenario = await CenarioDaExecucao.CriarAsync(opcoes);
            textos.AddRange(Textos(await cenario.ExecutarAsync(resolverChave: Canario)));
        }

        Assert.Equal(3, chaves.Count);
        Assert.All(chaves, chave => Assert.Throws<ObjectDisposedException>(() => new SessaoTransformacao(chave, "homologacao")));
        SemProibidos(textos, formas);
        SemSentinela(textos);
    }

    // Quantos pontos a cópia passa no caminho feliz.
    private static async Task<int> PontosAsync()
    {
        var pontos = 0;
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario { Ponto = _ => Interlocked.Increment(ref pontos) });
        Assert.Equal(0, (await cenario.ExecutarAsync()).Saida);
        return pontos;
    }

    private static async Task NoPontoAsync(string causa, int ponto)
    {
        using var usuario = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var contados = 0;
        CenarioDaExecucao? cenario = null;
        cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Ponto = _ =>
            {
                if (Interlocked.Increment(ref contados) != ponto)
                {
                    return;
                }

                switch (causa)
                {
                    case "falha":
                        throw new ExcecaoComValor(Sentinela);
                    case "ctrl-c":
                        usuario.Cancel();
                        break;
                    default:
                        cenario!.Provedor.Concessoes.Single().Perder();
                        break;
                }
            },
        });

        var resultado = await cenario.ExecutarAsync(cancelamento: usuario.Token);

        var onde = $"{causa} no ponto {ponto}: {resultado.Falha?.GetType().Name}";
        switch (causa)
        {
            case "falha":
                Assert.True(resultado.Falha is FalhaTransformacaoException or FalhaDialetoException, onde);
                Assert.True(resultado.Saida == 1 && cenario.Encerrada == SituacaoExecucao.Falhou && resultado.Restauracao is not null, onde);
                break;
            case "ctrl-c":
                Assert.True(resultado.Falha is OperationCanceledException, onde);
                Assert.True(resultado.Saida == 130 && cenario.Encerrada == SituacaoExecucao.Falhou && resultado.Restauracao is not null, onde);
                break;
            default:
                Assert.True(resultado.Falha is BloqueioDestinoException, onde);
                Assert.True(resultado.Saida == 1 && cenario.Encerrada is null && resultado.Restauracao is null, onde);
                break;
        }

        // A tabela em curso não conclui; a do outro trabalhador pode concluir enquanto a causa se propaga.
        Assert.True(cenario.Gravador.Confirmadas.Count <= 1, onde);
        Assert.DoesNotContain("ValidarChave:destino", cenario.Provedor.Chamadas);
        Assert.Contains(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.DestinoIncompleto);
        Assert.True(cenario.Fabrica.Criacoes == 3 && cenario.Fabrica.Descartes == 3, onde);
        SemSentinela(Textos(resultado));
    }

    private static bool DaVarredura(Exception excecao) =>
        excecao is ExcecaoComValor or IncognitoException
        || (excecao is AggregateException agregada && agregada.InnerExceptions.Any(DaVarredura))
        || (excecao.InnerException is { } interna && DaVarredura(interna));

    // Tudo o que o resultado mostra: o texto dele, o de cada aviso, tabela e restauração, e a falha com as internas.
    private static List<string> Textos(ResultadoDaExecucao resultado)
    {
        var textos = new List<string> { resultado.ToString() };
        textos.AddRange(resultado.Avisos.SelectMany(aviso => new[] { aviso.ToString(), aviso.Mensagem }));
        textos.AddRange(resultado.Tabelas.Select(tabela => tabela.ToString()));
        textos.AddRange(resultado.Tabelas.SelectMany(tabela => tabela.Colunas.Select(coluna => coluna.ToString())));
        textos.Add(resultado.Reparo?.ToString() ?? "");
        textos.Add(resultado.Restauracao?.ToString() ?? "");
        for (var atual = resultado.Falha; atual is not null; atual = atual.InnerException)
        {
            textos.Add(atual.ToString());
            textos.Add(atual.Message);
            textos.AddRange(atual.Data.Values.Cast<object?>().Select(valor => valor?.ToString() ?? ""));
            if (atual is IncognitoException incognito)
            {
                textos.Add(incognito.Codigo);
            }
        }

        return textos;
    }

    private static void SemSentinela(IEnumerable<string> textos) => SemProibidos(textos, _formasDoSentinela);

    // Sem eco: a mensagem da falha diz só qual forma e qual texto, nunca o conteúdo.
    private static void SemProibidos(IEnumerable<string> textos, string[] proibidos)
    {
        var lista = textos.ToList();
        for (var texto = 0; texto < lista.Count; texto++)
        {
            for (var proibido = 0; proibido < proibidos.Length; proibido++)
            {
                Assert.False(lista[texto].Contains(proibidos[proibido], StringComparison.OrdinalIgnoreCase), $"o proibido {proibido} aparece no texto {texto}");
            }
        }
    }

    // Uma exceção de terceiro com o valor na mensagem, nos dados e na interna.
    private sealed class ExcecaoComValor : Exception
    {
        internal ExcecaoComValor(string valor)
            : base("valor: " + valor, new InvalidOperationException(valor)) => Data["valor"] = valor;
    }
}
