using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A cópia de uma tabela (seção 4.4): o tratamento por linha (C5), as políticas do valor sem representação (D3), o
/// <c>derivarDe</c> com o original, a primeira falha que vence, o bloqueio conferido fora dos lotes, a leitura que termina
/// antes de concluir, os lotes descartados e o pico limitado (C1).
/// </summary>
public sealed class CopiaDaTabelaTestes
{
    private const string Sentinela = "Sentinela7f3a9cQ";
    private static readonly NomeTabela _clientes = new("loja", "clientes");

    [Fact]
    public async Task Copia_com_a_tratada_transformada_os_opacos_como_vieram_e_as_contagens()
    {
        var fabrica = new FabricaDeFuncao(valor => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, Linhas(5));

        var contagem = await cenario.CopiarAsync();

        // Com K tratamentos, os lotes chegam à gravação em qualquer ordem: a ordem das linhas não é contrato.
        var gravadas = cenario.Gravador.Confirmadas["clientes"].OrderBy(linha => (long)linha[0]!).ToArray();
        Assert.Equal(5, gravadas.Length);
        Assert.All(gravadas.Select((linha, indice) => (linha, indice)), par =>
        {
            Assert.Equal((long)(par.indice + 1), par.linha[0]);
            Assert.Equal("TRATADO", par.linha[1]);
            Assert.Equal($"Pessoa {par.indice + 1}", par.linha[2]);
        });
        Assert.Equal((5L, 5L), (contagem.Lidas, contagem.Gravadas));
        Assert.Equal(5L, Assert.Single(contagem.Colunas).Desfechos[Desfecho.Substituido]);
        Assert.Equal([1], cenario.Leitor.Pedidos.Single().Convertidas.Select(convertida => convertida.Indice));
        Assert.Equal(["id", "cpf", "nome"], cenario.Gravador.Pedidos.Single().Colunas);
        Assert.Equal([1], cenario.Gravador.Pedidos.Single().Tratadas.Select(tratada => tratada.Indice));
        Assert.All(cenario.Leitor.Lotes, lote => Assert.Equal(1, lote.Descartes));
        Assert.Equal(0, cenario.Contador.Vivos);
        Assert.Equal(1, cenario.Leitor.LeiturasDescartadas);
    }

    [Fact]
    public async Task Tabela_sem_tratada_vai_direto_a_gravacao_e_a_esvaziada_so_esvazia()
    {
        var cenario = await CenarioAsync(null, Linhas(3));

        await cenario.CopiarAsync(Unidade(cenario, []));
        var esvaziada = await cenario.CopiarAsync(new UnidadeDeCopia(_clientes, AcaoTabela.Esvaziar, _clientes, [], [], []));

        Assert.Equal(3, cenario.Gravador.Confirmadas["clientes"].Count);
        Assert.Empty(cenario.Leitor.Pedidos.Single().Convertidas);
        Assert.Equal(["clientes"], cenario.Gravador.Esvaziadas);
        Assert.Equal((0L, 0L), (esvaziada.Lidas, esvaziada.Gravadas));
        Assert.Equal(1, cenario.Leitor.Aberturas);
        Assert.Equal(3, cenario.Bloqueios);
    }

    [Fact]
    public async Task Falha_do_transformador_aponta_a_linha_exata_do_segundo_lote_so_com_o_tipo_e_nada_fica()
    {
        var fabrica = new FabricaDeFuncao(valor => (string)valor! == "cpf-4" ? throw new FormatException(Sentinela) : new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, Linhas(5));

        var falha = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => cenario.CopiarAsync());

        Assert.Equal(("INC4007", "clientes", "cpf", 4L), (falha.Codigo, falha.Tabela.Tabela, falha.Coluna, falha.Linha));
        Assert.Equal("loja.clientes.cpf, linha 4: o transformador exemplo.funcao@1 lançou FormatException.", falha.Message);
        Assert.Null(falha.InnerException);
        DialetoProtegidoTestes.SemVazamento(falha);
        Assert.Equal(1, ClassificadorDeSaida.De(falha));
        cenario.NadaFicou();
    }

    [Theory]
    [MemberData(nameof(ResultadosInvalidos))]
    public async Task Resultado_invalido_da_INC4005_sem_gravar(string caso, object? valor, int desfecho)
    {
        var fabrica = new FabricaDeFuncao(_ => Resultado(valor, (Desfecho)desfecho));
        var cenario = await CenarioAsync(fabrica, Linhas(1));

        var falha = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => cenario.CopiarAsync());

        Assert.True(falha.Codigo == "INC4005", caso);
        Assert.Equal(1L, falha.Linha);
        Assert.DoesNotContain(Sentinela, falha.Message, StringComparison.Ordinal);
        cenario.NadaFicou();
    }

    public static TheoryData<string, object?, int> ResultadosInvalidos() => new()
    {
        { "desfecho desconhecido", "ok", 999 },
        { "desfecho indefinido", "ok", (int)Desfecho.Indefinido },
        { "pseudonimizado nulo", null, (int)Desfecho.Pseudonimizado },
        { "anulado com valor", "ok", (int)Desfecho.AnuladoMalformado },
        { "nulo para entrada não nula", null, (int)Desfecho.Nulo },
        { "acima do comprimento", "123456789012", (int)Desfecho.Pseudonimizado },
        { "tipo diferente", 12L, (int)Desfecho.Pseudonimizado },
        { "texto com NUL", "a\0" + Sentinela[..2], (int)Desfecho.Pseudonimizado },
        { "surrogate isolado", "a" + (char)0xD800, (int)Desfecho.Pseudonimizado },
    };

    [Fact]
    public async Task Null_sem_TransformaNulo_sai_null_sem_chamar_e_com_TransformaNulo_chama()
    {
        var linhas = new[] { Linha(1, null), Linha(2, "cpf-2") };
        var semNulo = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var comNulo = new FabricaDeFuncao(valor => valor is null ? new ResultadoTransformacao(null, Desfecho.Nulo) : new ResultadoTransformacao("TRATADO", Desfecho.Substituido)) { TransformaNulo = true };
        var cenario = await CenarioAsync(semNulo, linhas);
        var outro = await CenarioAsync(comNulo, linhas);

        var contagem = await cenario.CopiarAsync();
        await outro.CopiarAsync();

        Assert.Null(cenario.Gravador.Confirmadas["clientes"][0][1]);
        Assert.Equal(1, semNulo.Chamadas);
        Assert.Equal(2, comNulo.Chamadas);
        Assert.Equal((1L, 1L), (contagem.Colunas[0].Desfechos[Desfecho.Nulo], contagem.Colunas[0].Desfechos[Desfecho.Substituido]));
    }

    [Fact]
    public async Task DerivarDe_recebe_o_original_da_referencia_mesmo_quando_ela_tambem_e_tratada()
    {
        var referencias = new ConcurrentBag<object?>();
        var cpf = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido), referencias);
        var cenario = await CenarioAsync(cpf, Linhas(3));
        var unidade = Unidade(cenario, [Tratada(cenario, "cpf", "nome"), Tratada(cenario, "nome", texto: 100)]);

        await cenario.CopiarAsync(unidade);

        // A mesma fábrica trata o nome, sem derivarDe: essas chamadas recebem a referência nula.
        Assert.Equal(["Pessoa 1", "Pessoa 2", "Pessoa 3"], referencias.OfType<string>().Order(StringComparer.Ordinal));
        Assert.Equal(3, referencias.Count(referencia => referencia is null));
        Assert.All(cenario.Gravador.Confirmadas["clientes"], linha => Assert.Equal("TRATADO", linha[2]));
        Assert.Equal([1, 2], cenario.Leitor.Pedidos.Single().Convertidas.Select(convertida => convertida.Indice));
    }

    [Fact]
    public async Task Todos_os_originais_da_linha_sao_lidos_antes_de_qualquer_escrita()
    {
        // A referência (cpf) vem antes no catálogo e é tratada antes: o nome derivado dela ainda recebe o CPF original.
        var referencias = new ConcurrentBag<object?>();
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido), referencias);
        var cenario = await CenarioAsync(fabrica, Linhas(3));
        var unidade = Unidade(cenario, [Tratada(cenario, "cpf"), Tratada(cenario, "nome", "cpf", texto: 100)]);

        await cenario.CopiarAsync(unidade);

        Assert.Equal(["cpf-1", "cpf-2", "cpf-3"], referencias.OfType<string>().Order(StringComparer.Ordinal));
        Assert.All(cenario.Gravador.Confirmadas["clientes"], linha => Assert.Equal(("TRATADO", "TRATADO"), (linha[1], linha[2])));
    }

    [Theory]
    [InlineData(PoliticaEntradaMalformada.Anular, null, Desfecho.AnuladoMalformado)]
    [InlineData(PoliticaEntradaMalformada.Preservar, "mantido", Desfecho.CopiadoEmClaro)]
    public async Task Valor_sem_representacao_segue_a_politica_da_tratada(PoliticaEntradaMalformada politica, string? esperado, Desfecho desfecho)
    {
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, [Linha(1, CopiaEmMemoria.SemForma)]);

        var contagem = await cenario.CopiarAsync(Unidade(cenario, [Tratada(cenario, "cpf", politica: politica)]));

        var lote = Assert.Single(cenario.Leitor.Lotes);
        Assert.Equal(esperado is null ? (object?)null : CopiaEmMemoria.SemForma, cenario.Gravador.Confirmadas["clientes"][0][1]);
        Assert.Equal(esperado is not null, lote.Mantido(0, 1));
        Assert.Equal(1L, contagem.Colunas[0].Desfechos[desfecho]);
        Assert.Equal(0, fabrica.Chamadas);
    }

    [Fact]
    public async Task Valor_sem_representacao_com_falhar_e_referencia_sem_representacao_dao_INC4006()
    {
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var naTratada = await CenarioAsync(fabrica, [Linha(1, CopiaEmMemoria.SemForma)]);
        var naReferencia = await CenarioAsync(fabrica, [[1L, "cpf-1", CopiaEmMemoria.SemForma]]);

        var falhar = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => naTratada.CopiarAsync(Unidade(naTratada, [Tratada(naTratada, "cpf", politica: PoliticaEntradaMalformada.Falhar)])));
        var referencia = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => naReferencia.CopiarAsync(Unidade(naReferencia, [Tratada(naReferencia, "cpf", "nome", politica: PoliticaEntradaMalformada.Preservar)])));

        Assert.Equal(("INC4006", 1L), (falhar.Codigo, falhar.Linha));
        Assert.Equal(("INC4006", "cpf"), (referencia.Codigo, referencia.Coluna));
        Assert.Contains("'derivarDe'", referencia.Message, StringComparison.Ordinal);
        naTratada.NadaFicou();
        naReferencia.NadaFicou();
    }

    [Theory]
    [InlineData(PoliticaEntradaMalformada.Anular)]
    [InlineData(PoliticaEntradaMalformada.Preservar)]
    public async Task Texto_substituir_sem_representacao_falha_com_INC4006_qualquer_que_seja_a_politica(PoliticaEntradaMalformada politica)
    {
        // O texto.substituir ignora a entradaMalformada: anular ou preservar sairia com o valor em claro ou sem a coluna.
        var cenario = await CenarioAsync(null, [Linha(1, CopiaEmMemoria.SemForma)]);
        Assert.True(cenario.Registro.TentarObter("texto.substituir", 1, out var substituir));
        var regra = new RegraTransformacao(new DominioPseudonimo("texto.substituir", 1)) { EntradaMalformada = politica };
        var tratada = new ColunaTratada("cpf", substituir, regra, new Dictionary<string, string>(), new ContextoDaColuna(TipoColuna.Texto, 11, null, null, null, null, null, null), null, Injetividade.Nenhuma);

        var falha = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => cenario.CopiarAsync(Unidade(cenario, [tratada])));

        Assert.Equal(("INC4006", 1L), (falha.Codigo, falha.Linha));
        cenario.NadaFicou();
    }

    [Fact]
    public async Task O_pedido_de_gravacao_leva_a_tabela_do_destino()
    {
        // As colunas vão com os nomes do destino, que no PostgreSQL são os mesmos da origem (o par é exato); a base da gravação
        // confere o lote contra eles (registro para o SQL Server, INC-053).
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, Linhas(1));
        var unidade = new UnidadeDeCopia(_clientes, AcaoTabela.Copiar, new NomeTabela("homolog", "Clientes"), ["id", "cpf", "nome"], ["id", "cpf", "nome"], [Tratada(cenario, "cpf")]);

        await cenario.CopiarAsync(unidade);

        Assert.Equal(new NomeObjeto("homolog", "Clientes"), Assert.Single(cenario.Gravador.Pedidos).Tabela);
        Assert.Equal(_clientes.Tabela, cenario.Leitor.Pedidos.Single().Tabela.Nome);
    }

    [Fact]
    public async Task CopiadoEmClaro_do_transformador_mantem_a_celula_como_veio_sem_escrever()
    {
        var fabrica = new FabricaDeFuncao(valor => new ResultadoTransformacao(valor, Desfecho.CopiadoEmClaro));
        var cenario = await CenarioAsync(fabrica, Linhas(1));

        var contagem = await cenario.CopiarAsync();

        var lote = Assert.Single(cenario.Leitor.Lotes);
        Assert.True(lote.Mantido(0, 1));
        Assert.Equal("cpf-1", cenario.Gravador.Confirmadas["clientes"][0][1]);
        Assert.Equal(1L, contagem.Colunas[0].Desfechos[Desfecho.CopiadoEmClaro]);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Imita a falta de memória de um transformador de fora.")]
    public async Task Excecao_de_terceiro_nunca_traz_valor_e_o_cancelamento_sem_pedido_e_falha()
    {
        var comCodigo = new IncognitoException("INC9123", Sentinela, new InvalidOperationException(Sentinela));
        comCodigo.Data["valor"] = Sentinela;
        var casos = new (Func<object?, ResultadoTransformacao> Lancar, string Esperado)[]
        {
            (_ => throw comCodigo, "lançou IncognitoException, INC9123."),
            (_ => throw new IncognitoException("INC" + Sentinela, Sentinela), "lançou IncognitoException."),
            (_ => throw new OperationCanceledException(Sentinela), "lançou OperationCanceledException."),
        };

        foreach (var (lancar, esperado) in casos)
        {
            var cenario = await CenarioAsync(new FabricaDeFuncao(lancar), Linhas(1));
            var falha = await Assert.ThrowsAsync<FalhaTransformacaoException>(() => cenario.CopiarAsync());
            Assert.EndsWith(esperado, falha.Message, StringComparison.Ordinal);
            DialetoProtegidoTestes.SemVazamento(falha);
        }

        var semMemoria = await CenarioAsync(new FabricaDeFuncao(_ => throw new OutOfMemoryException(Sentinela)), Linhas(1));
        var oom = await Assert.ThrowsAsync<OutOfMemoryException>(() => semMemoria.CopiarAsync());
        Assert.DoesNotContain(Sentinela, oom.ToString(), StringComparison.Ordinal);
        semMemoria.NadaFicou();
    }

    [Fact]
    public async Task Cancelamento_da_execucao_sai_como_cancelamento_e_nada_fica()
    {
        using var fonte = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fabrica = new FabricaDeFuncao(valor =>
        {
            if ((string)valor! == "cpf-3")
            {
                fonte.Cancel();
            }

            return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
        });
        var cenario = await CenarioAsync(fabrica, Linhas(6));

        var cancelado = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cenario.CopiarAsync(cancelamento: fonte.Token));

        Assert.Equal(fonte.Token, cancelado.CancellationToken);
        cenario.NadaFicou();
    }

    [Fact]
    public async Task Falha_na_leitura_na_gravacao_do_lote_e_no_concluir_nao_confirma_nada()
    {
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var naLeitura = await CenarioAsync(fabrica, Linhas(6), leitor: tabelas => new LeitorEmMemoria(tabelas) { NoLote = numero => numero == 2 ? new IOException(Sentinela) : null });
        var naGravacao = await CenarioAsync(fabrica, Linhas(6), gravador: new GravadorEmMemoria { NoLote = numero => numero == 2 ? new IOException(Sentinela) : null });
        var noConcluir = await CenarioAsync(fabrica, Linhas(6), gravador: new GravadorEmMemoria { NoConcluir = new IOException(Sentinela) });

        foreach (var cenario in new[] { naLeitura, naGravacao, noConcluir })
        {
            var falha = await Assert.ThrowsAsync<FalhaDialetoException>(() => cenario.CopiarAsync());
            Assert.Equal("INC3001", falha.Codigo);
            DialetoProtegidoTestes.SemVazamento(falha);
            cenario.NadaFicou();
        }
    }

    [Fact]
    public async Task O_bloqueio_e_conferido_antes_de_abrir_e_antes_de_concluir_e_a_leitura_termina_antes_de_concluir()
    {
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, Linhas(7));
        cenario.Gravador.Ordem = cenario.Leitor.Ordem;

        await cenario.CopiarAsync();
        var perdido = await CenarioAsync(fabrica, Linhas(7));
        perdido.PerderBloqueioNa = 2;
        var perda = await Assert.ThrowsAsync<BloqueioDestinoException>(() => perdido.CopiarAsync());

        Assert.Equal(2, cenario.Bloqueios);
        Assert.Equal(["fim da leitura", "concluir"], cenario.Leitor.Ordem);
        Assert.Equal("INC4001", perda.Codigo);
        perdido.NadaFicou();
    }

    [Fact]
    public async Task O_bloqueio_perdido_antes_de_abrir_ou_esvaziar_nao_abre_nem_esvazia()
    {
        // No PostgreSQL, a abertura da gravação já faz o TRUNCATE: a conferência vem antes de qualquer escrita.
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var copiada = await CenarioAsync(fabrica, Linhas(3));
        copiada.PerderBloqueioNa = 1;
        var esvaziada = await CenarioAsync(null, Linhas(3));
        esvaziada.PerderBloqueioNa = 1;

        await Assert.ThrowsAsync<BloqueioDestinoException>(() => copiada.CopiarAsync());
        await Assert.ThrowsAsync<BloqueioDestinoException>(() => esvaziada.CopiarAsync(new UnidadeDeCopia(_clientes, AcaoTabela.Esvaziar, _clientes, [], [], [])));

        Assert.Empty(copiada.Gravador.Pedidos);
        Assert.Equal(0, copiada.Leitor.Aberturas);
        Assert.Empty(esvaziada.Gravador.Esvaziadas);
    }

    [Fact]
    public async Task O_pico_de_lotes_vivos_nao_cresce_com_a_tabela()
    {
        var fabrica = new FabricaDeFuncao(_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var cenario = await CenarioAsync(fabrica, Linhas(400), leitor: tabelas => new LeitorEmMemoria(tabelas) { LinhasPorLote = 1 }, gravador: new GravadorEmMemoria { Atraso = TimeSpan.FromMilliseconds(1) });

        await cenario.CopiarAsync(tratamentos: 3);

        // Lido e esperando o canal 1, dois no canal 1, três tratando, dois no canal 2 e um gravando.
        Assert.InRange(cenario.Contador.Pico, 1, 1 + CopiaDaTabela.Capacidade + 3 + CopiaDaTabela.Capacidade + 1);
        Assert.Equal(400, cenario.Gravador.Confirmadas["clientes"].Count);
        Assert.Equal(0, cenario.Contador.Vivos);
    }

    [Fact]
    public async Task Nenhuma_tarefa_fica_sem_observar_na_falha()
    {
        var naoObservadas = new ConcurrentBag<Exception>();
        // Só as deste teste: o manipulador vale para o processo, e as outras classes rodam em paralelo.
        void Anotar(object? _, UnobservedTaskExceptionEventArgs evento)
        {
            if (evento.Exception.Flatten().InnerExceptions.Any(interna => interna is FormatException or FalhaTransformacaoException))
            {
                naoObservadas.Add(evento.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += Anotar;
        try
        {
            var fabrica = new FabricaDeFuncao(valor => (string)valor! == "cpf-5" ? throw new FormatException() : new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
            var cenario = await CenarioAsync(fabrica, Linhas(40), leitor: tabelas => new LeitorEmMemoria(tabelas) { LinhasPorLote = 1 });
            await Assert.ThrowsAsync<FalhaTransformacaoException>(() => cenario.CopiarAsync(tratamentos: 4));
            cenario.NadaFicou();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Anotar;
        }

        Assert.Empty(naoObservadas);
    }

    // O resultado como um transformador de fora o devolveria, também com o desfecho que o construtor recusa (o default, ou
    // um valor de uma versão mais nova do enum).
    private static ResultadoTransformacao Resultado(object? valor, Desfecho desfecho)
    {
        if (desfecho != Desfecho.Indefinido && Enum.IsDefined(desfecho))
        {
            return new ResultadoTransformacao(valor, desfecho);
        }

        object caixa = default(ResultadoTransformacao);
        typeof(ResultadoTransformacao).GetField("<Valor>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(caixa, valor);
        typeof(ResultadoTransformacao).GetField("<Desfecho>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(caixa, desfecho);
        return (ResultadoTransformacao)caixa;
    }

    private static object?[] Linha(long id, object? cpf) => [id, cpf, $"Pessoa {id}"];

    private static object?[][] Linhas(int quantas) => [.. Enumerable.Range(1, quantas).Select(id => (object?[])[(long)id, $"cpf-{id}", $"Pessoa {id}"])];

    private static async Task<Cenario> CenarioAsync(
        FabricaDeFuncao? fabrica, object?[][] linhas, Func<IReadOnlyDictionary<string, object?[][]>, LeitorEmMemoria>? leitor = null, GravadorEmMemoria? gravador = null)
    {
        var registro = new RegistroTransformadores();
        if (fabrica is not null)
        {
            registro.Adicionar(fabrica);
        }

        var tabelas = new Dictionary<string, object?[][]> { ["clientes"] = linhas };
        var emMemoria = leitor?.Invoke(tabelas) ?? new LeitorEmMemoria(tabelas);
        var sessao = CopiaEmMemoria.Sessao(emMemoria);
        gravador ??= new GravadorEmMemoria();
        var destino = await DialetoProtegido.ConectarAsync(new ProvedorDoPlanejar { Gravador = gravador }, "postgresql", LadoBanco.Destino, DialetoProtegidoTestes.Conexao(), TestContext.Current.CancellationToken);
        return new Cenario(registro, fabrica, emMemoria, sessao, gravador, destino);
    }

    private static UnidadeDeCopia Unidade(Cenario cenario, IReadOnlyList<ColunaTratada>? tratadas = null) =>
        new(_clientes, AcaoTabela.Copiar, _clientes, ["id", "cpf", "nome"], ["id", "cpf", "nome"], tratadas ?? [Tratada(cenario, "cpf")]);

    private static ColunaTratada Tratada(Cenario cenario, string coluna, string? referencia = null, PoliticaEntradaMalformada politica = PoliticaEntradaMalformada.Falhar, int texto = 11)
    {
        Assert.True(cenario.Registro.TentarObter("exemplo.funcao", 1, out var fabrica));
        var regra = new RegraTransformacao(new DominioPseudonimo("exemplo.funcao", 1)) { EntradaMalformada = politica };
        var contexto = new ContextoDaColuna(TipoColuna.Texto, texto, null, null, null, null, null, referencia is null ? null : TipoColuna.Texto);
        return new ColunaTratada(coluna, fabrica, regra, new Dictionary<string, string>(), contexto, referencia, Injetividade.Nenhuma);
    }

    private sealed class Cenario(RegistroTransformadores registro, FabricaDeFuncao? fabrica, LeitorEmMemoria leitor, SessaoLeitura sessao, GravadorEmMemoria gravador, DialetoProtegido destino)
    {
        private int _bloqueios;

        internal RegistroTransformadores Registro => registro;

        internal LeitorEmMemoria Leitor => leitor;

        internal GravadorEmMemoria Gravador => gravador;

        internal ContadorDoPipeline Contador { get; } = new();

        internal int Bloqueios => Volatile.Read(ref _bloqueios);

        internal int? PerderBloqueioNa { get; set; }

        internal async Task<ContagemDaTabela> CopiarAsync(UnidadeDeCopia? unidade = null, int tratamentos = 2, CancellationToken? cancelamento = null)
        {
            unidade ??= Unidade(this, fabrica is null ? [] : null);
            var carga = new DadosDeExecucao(
                ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Minimo)),
                new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja")),
                new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja_homolog")),
                [unidade],
                [],
                [],
                []);
            using var chave = ChaveIncognito.Gerar();
            var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);
            try
            {
                return await CopiaDaTabela.CopiarAsync(
                    unidade,
                    new DialetoProtegido.LeitorProtegido(sessao.Leitores[0], "postgresql", LadoBanco.Origem),
                    destino,
                    transformadores,
                    tratamentos,
                    _ =>
                    {
                        var vez = Interlocked.Increment(ref _bloqueios);
                        return vez == PerderBloqueioNa ? Task.FromException(new BloqueioDestinoException("INC4001", "O bloqueio do destino se perdeu.")) : Task.CompletedTask;
                    },
                    Contador,
                    TimeProvider.System,
                    cancelamento ?? TestContext.Current.CancellationToken);
            }
            finally
            {
                transformadores.Descartar();
            }
        }

        // Nada confirmado, toda gravação aberta descartada, todo lote descartado uma vez e o leitor livre.
        internal void NadaFicou()
        {
            Assert.Empty(gravador.Confirmadas);
            Assert.Equal(gravador.Pedidos.Count, Volatile.Read(ref gravador.Descartadas));
            Assert.All(leitor.Lotes, lote => Assert.Equal(1, lote.Descartes));
            Assert.Equal(0, Contador.Vivos);
            Assert.Equal(leitor.Aberturas, Volatile.Read(ref leitor.LeiturasDescartadas));
        }
    }

    // Um transformador de fora pela função dada, que conta as chamadas e guarda as referências.
    private sealed class FabricaDeFuncao(Func<object?, ResultadoTransformacao> funcao, ConcurrentBag<object?>? referencias = null) : FabricaTransformador
    {
        private int _chamadas;

        public override string Nome => "exemplo.funcao";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        internal bool TransformaNulo { get; init; }

        internal int Chamadas => Volatile.Read(ref _chamadas);

        public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Criado(this);

        private sealed class Criado(FabricaDeFuncao fabrica) : Transformador
        {
            public override string Nome => "exemplo.funcao";

            public override int Versao => 1;

            public override Injetividade Injetividade => Injetividade.Nenhuma;

            public override bool TransformaNulo => fabrica.TransformaNulo;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto)
            {
                Interlocked.Increment(ref fabrica._chamadas);
                fabrica.Referencias?.Add(contexto.Referencia);
                return fabrica.Funcao(valor);
            }
        }

        private ConcurrentBag<object?>? Referencias => referencias;

        private Func<object?, ResultadoTransformacao> Funcao => funcao;
    }
}
