using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A injetividade contra a igualdade do destino (INC3050 a INC3053, avisos INC3532 e INC3550 a INC3555): índice único,
/// chave primária lida direto, exclusão e chave estrangeira, pela colação do elemento e pelas marcas do tipo.
/// </summary>
public sealed class UnicidadeNoPlanoTestes
{
    private static readonly ColacaoEsquema _numerica = new() { Nome = "kn", Esquema = "public", Deterministica = false, TemRegras = false, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly ColacaoEsquema _comRegras = new() { Nome = "regras", Esquema = "public", Deterministica = false, TemRegras = true, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly ColacaoEsquema _deterministicaComRegras = new() { Nome = "regras-det", Esquema = "public", Deterministica = true, TemRegras = true, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly ColacaoEsquema _semCaixa = new() { Nome = "ci", Esquema = "public", Deterministica = false, TemRegras = false, IgnoraCaixa = true, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly ColacaoEsquema _semMarcas = new() { Nome = "sem", Esquema = "public", Deterministica = false, TemRegras = false, IgnoraCaixa = null, IgnoraAcento = false, IgnoraPontuacao = false };

    public static TheoryData<string> Lugares => ["pk", "unique", "expressao", "gerada", "chave-estrangeira", "pk-sem-indice"];

    [Theory]
    [MemberData(nameof(Lugares))]
    public async Task Transformador_sem_injetividade_em_PK_sai_com_codigo_2(string lugar)
    {
        var esquema = lugar switch
        {
            "pk" => ComChave(),
            "unique" => ComIndice(Unico("uq_doc", Elemento("doc"))),
            "expressao" => ComIndice(Unico("uq_expr", Elemento(null, Padrao)), referencias: ["doc"]),
            "gerada" => ComIndice(Unico("uq_gerada", Elemento("doc_limpo")), extras: [Gerada("doc_limpo", "doc")]),
            "chave-estrangeira" => ComChaveEstrangeira(),
            _ => BancoFalso.Clonar(ComChave(), (nameof(EsquemaBanco.Tabelas), ComChave().Tabelas.Select(tabela => tabela.Nome.Nome == "clientes" ? BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>())) : tabela).ToArray())),
        };

        var erros = await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), esquema, Registro());

        // Um erro por coluna e causa: a chave estrangeira não repete o do índice, e a filha recebe a regra pela chave.
        var esperados = lugar switch
        {
            "pk" => new[] { ("clientes", "doc", (string?)"pk_clientes") },
            "unique" => [("clientes", "doc", "uq_doc")],
            "expressao" => [("clientes", "doc", "uq_expr")],
            "gerada" => [("clientes", "doc", "uq_gerada")],
            "chave-estrangeira" => [("clientes", "doc", null), ("pedidos", "doc", null)],
            _ => [("clientes", "doc", null)],
        };
        Assert.All(erros, erro => Assert.Equal("INC3050", erro.Codigo));
        Assert.Equal(esperados, erros.Select(erro => (erro.Tabela!.Tabela, erro.Coluna!, erro.Objeto)));
        if (lugar == "pk-sem-indice")
        {
            Assert.Contains("está na chave primária", erros[0].Mensagem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Transformador_injetivo_em_PK_com_colacao_deterministica_passa_e_sem_injetividade_fora_do_indice_tambem()
    {
        var plano = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), ComChave());
        var foraDoIndice = await PlanejarAsync(Doc("{ transformador: br.cep@1, derivarDe: nome }"), ComIndice());

        Assert.Equal(Injetividade.Garantida, Regra(plano).Injetividade);
        Assert.Null(Regra(foraDoIndice).Propagacao);
    }

    public static TheoryData<string> Indeterminadas =>
    [
        "sem-colacao", "com-regras", "deterministica-com-regras", "sem-marcas", "acento-nulo", "pontuacao-nula", "marca-do-tipo-nula", "espaco-do-tipo-nulo",
        "sem-caixa", "expressao-sem-colacao", "expressao-numerica", "gerada-sem-colacao", "gerada-numerica", "cnpj-numerica", "terceiro-numerica",
    ];

    [Theory]
    [MemberData(nameof(Indeterminadas))]
    public async Task Igualdade_que_o_planejar_nao_decide_da_INC3051(string caso)
    {
        var (esquema, regra) = caso switch
        {
            "sem-colacao" => (ComIndice(Unico("uq_doc", Elemento("doc", colacao: null))), "{ transformador: br.cpf@1 }"),
            "com-regras" => (ComIndice(Unico("uq_doc", Elemento("doc", _comRegras))), "{ transformador: br.cpf@1 }"),
            "deterministica-com-regras" => (ComIndice(Unico("uq_doc", Elemento("doc", _deterministicaComRegras))), "{ transformador: br.cpf@1 }"),
            "sem-marcas" => (ComIndice(Unico("uq_doc", Elemento("doc", _semMarcas))), "{ transformador: br.cpf@1 }"),
            "acento-nulo" => (ComIndice(Unico("uq_doc", Elemento("doc", BancoFalso.Clonar(_numerica, (nameof(ColacaoEsquema.IgnoraAcento), null))))), "{ transformador: br.cpf@1 }"),
            "pontuacao-nula" => (ComIndice(Unico("uq_doc", Elemento("doc", BancoFalso.Clonar(_numerica, (nameof(ColacaoEsquema.IgnoraPontuacao), null))))), "{ transformador: br.cpf@1 }"),
            "espaco-do-tipo-nulo" => (ComIndice(Unico("uq_doc", Elemento("doc")), doc: BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), null))), "{ transformador: br.cpf@1 }"),
            "sem-caixa" => (ComIndice(Unico("uq_doc", Elemento("doc", _semCaixa)), doc: Texto("doc", 100)), "{ transformador: email@1 }"),
            "expressao-numerica" => (ComIndice(Unico("uq_expr", Elemento(null, _numerica)), referencias: ["doc"]), "{ transformador: br.cpf@1 }"),
            "gerada-sem-colacao" => (ComIndice(Unico("uq_gerada", Elemento("doc_limpo", colacao: null)), extras: [Gerada("doc_limpo", "doc")]), "{ transformador: br.cpf@1 }"),
            "gerada-numerica" => (ComIndice(Unico("uq_gerada", Elemento("doc_limpo", _numerica)), extras: [Gerada("doc_limpo", "doc")]), "{ transformador: br.cpf@1 }"),
            "marca-do-tipo-nula" => (ComIndice(Unico("uq_doc", Elemento("doc")), doc: BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), null))), "{ transformador: br.cpf@1 }"),
            "expressao-sem-colacao" => (ComIndice(Unico("uq_expr", Elemento(null, colacao: null)), referencias: ["doc"]), "{ transformador: br.cpf@1 }"),
            "cnpj-numerica" => (ComIndice(Unico("uq_doc", Elemento("doc", _numerica))), "{ transformador: br.cnpj@1 }"),
            _ => (ComIndice(Unico("uq_doc", Elemento("doc", _numerica))), "{ transformador: exemplo.injetiva@1 }"),
        };

        var erro = Assert.Single(await RecusaAsync(Doc(regra), esquema, Registro()));

        Assert.Equal(("INC3051", "doc"), (erro.Codigo, erro.Coluna));
    }

    [Fact]
    public async Task Colacao_numerica_medida_aceita_so_a_tabela_de_formato_fixo_com_a_injetividade_probabilistica()
    {
        var esquema = ComIndice(Unico("uq_doc", Elemento("doc", _numerica)));

        var plano = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), esquema);

        Assert.Equal(Injetividade.Probabilistica, Regra(plano).Injetividade);
        Assert.Equal(Injetividade.Garantida, plano.Execucao.Unidades[0].Tratadas.Single().Fabrica.Injetividade);
    }

    [Theory]
    [InlineData(false, "anular", true)]
    [InlineData(true, "anular", false)]
    [InlineData(false, "falhar", false)]
    public async Task Regra_que_produz_NULL_em_indice_com_NULLs_iguais_da_INC3052(bool nulosDistintos, string politica, bool recusa)
    {
        var esquema = ComIndice(BancoFalso.Clonar(Unico("uq_doc", Elemento("doc")), (nameof(IndiceUnico.NulosDistintos), nulosDistintos)));
        var yaml = Doc($"{{ transformador: br.cpf@1, entradaMalformada: {politica} }}");

        if (recusa)
        {
            Assert.Equal("INC3052", Assert.Single(await RecusaAsync(yaml, esquema)).Codigo);
        }
        else
        {
            await PlanejarAsync(yaml, esquema);
        }
    }

    [Fact]
    public async Task Exclusao_que_cita_tratada_da_INC3053_sem_injetividade_e_o_aviso_INC3553_com_ela()
    {
        var exclusao = new RestricaoExclusao { Nome = "ex_doc", Referencias = new ReferenciasDaExpressao { Colunas = ["doc"], Incompletas = false } };
        var esquema = ComIndice(exclusoes: [exclusao]);

        var erro = Assert.Single(await RecusaAsync(Doc("{ transformador: br.cep@1, derivarDe: nome }"), esquema));
        var plano = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), esquema);

        Assert.Equal(("INC3053", "ex_doc"), (erro.Codigo, erro.Objeto));
        Assert.Equal(("INC3553", "ex_doc"), (Assert.Single(plano.Avisos).Codigo, plano.Avisos[0].Objeto));
    }

    [Theory]
    [InlineData("doc", false, true)]
    [InlineData("nome", true, true)]
    [InlineData("nome", false, false)]
    public async Task Predicado_que_cita_tratada_da_o_aviso_INC3550(string coluna, bool incompletas, bool avisa)
    {
        var esquema = ComIndice(BancoFalso.Clonar(Unico("uq_id", Elemento("id")), (nameof(IndiceUnico.Predicado), new ReferenciasDaExpressao { Colunas = [coluna], Incompletas = incompletas })));

        var plano = await PlanejarAsync(Doc("{ transformador: br.telefone@1 }"), esquema);

        Assert.Equal(avisa ? ["INC3550"] : [], plano.Avisos.Select(aviso => aviso.Codigo));
    }

    [Fact]
    public async Task Tratada_injetiva_em_expressao_deterministica_ou_em_gerada_indexada_da_o_aviso_INC3551()
    {
        var expressao = await PlanejarAsync(Doc("{ transformador: br.telefone@1 }"), ComIndice(Unico("uq_expr", Elemento(null, Padrao)), referencias: ["doc"]));
        var gerada = await PlanejarAsync(Doc("{ transformador: br.telefone@1 }"), ComIndice(Unico("uq_gerada", Elemento("doc_limpo")), extras: [Gerada("doc_limpo", "doc")]));

        Assert.Equal(("INC3551", "uq_expr"), (Assert.Single(expressao.Avisos).Codigo, expressao.Avisos[0].Objeto));
        Assert.Equal(("INC3551", "uq_gerada"), (Assert.Single(gerada.Avisos).Codigo, gerada.Avisos[0].Objeto));
    }

    [Fact]
    public async Task Colisao_pela_normalizacao_da_o_aviso_INC3552_e_o_tipo_sem_caixa_a_evita()
    {
        var cpf = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), ComIndice(Unico("uq_doc", Elemento("doc"))));
        var email = await PlanejarAsync(Doc("{ transformador: email@1 }"), ComIndice(Unico("uq_doc", Elemento("doc")), doc: Texto("doc", 100)));
        var semCaixa = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), true));
        var citext = await PlanejarAsync(Doc("{ transformador: email@1 }"), ComIndice(Unico("uq_doc", Elemento("doc")), doc: semCaixa));

        Assert.Contains("completam os zeros", Assert.Single(cpf.Avisos, aviso => aviso.Codigo == "INC3552").Mensagem, StringComparison.Ordinal);
        Assert.Contains("sem a caixa", Assert.Single(email.Avisos, aviso => aviso.Codigo == "INC3552").Mensagem, StringComparison.Ordinal);
        Assert.Empty(citext.Avisos);
    }

    [Fact]
    public async Task Terceiro_em_igualdade_sem_a_caixa_da_o_aviso_INC3554_e_ligado_sem_igualdade_exata_o_INC3532()
    {
        var semCaixa = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), true));
        var normalizada = await PlanejarAsync(Doc("{ transformador: exemplo.injetiva@1 }"), ComIndice(Unico("uq_doc", Elemento("doc")), doc: semCaixa), Registro());
        var numerica = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.Colacao), _numerica));
        var ligada = await PlanejarAsync(Doc("{ transformador: exemplo.injetiva@1 }"), ComChaveEstrangeira(doc: numerica), Registro());

        Assert.Equal("INC3554", Assert.Single(normalizada.Avisos).Codigo);
        Assert.Contains(ligada.Avisos, aviso => aviso.Codigo == "INC3532" && aviso.Coluna == "doc");
    }

    [Fact]
    public async Task Entrada_preservada_no_indice_com_pseudonimos_da_o_aviso_INC3555_so_sem_a_prova_da_imagem_disjunta()
    {
        var numerica = await PlanejarAsync(Doc("{ transformador: br.cpf@1, entradaMalformada: preservar }"), ComIndice(Unico("uq_doc", Elemento("doc", _numerica))));
        var terceiro = await PlanejarAsync(Doc("{ transformador: exemplo.injetiva@1, entradaMalformada: preservar }"), ComIndice(Unico("uq_doc", Elemento("doc"))), Registro());
        var exata = await PlanejarAsync(Doc("{ transformador: br.telefone@1, entradaMalformada: preservar }"), ComIndice(Unico("uq_doc", Elemento("doc"))));

        Assert.Contains(numerica.Avisos, aviso => aviso.Codigo == "INC3555");
        Assert.Contains(terceiro.Avisos, aviso => aviso.Codigo == "INC3555");
        Assert.DoesNotContain(exata.Avisos, aviso => aviso.Codigo == "INC3555");
    }

    [Theory]
    [InlineData("anular", "INC3052")]
    [InlineData("falhar", null)]
    public async Task O_predicado_nao_salva_o_indice_com_NULLs_iguais(string politica, string? erro)
    {
        // Dois NULL dentro do predicado colidem: o 3052 vale mesmo no índice parcial, e o predicado avisa.
        var indice = BancoFalso.Clonar(
            Unico("uq_doc", Elemento("doc")),
            (nameof(IndiceUnico.NulosDistintos), false),
            (nameof(IndiceUnico.Predicado), new ReferenciasDaExpressao { Colunas = ["doc"], Incompletas = false }));
        var yaml = Doc($"{{ transformador: br.cpf@1, entradaMalformada: {politica} }}");

        if (erro is null)
        {
            Assert.Contains((await PlanejarAsync(yaml, ComIndice(indice))).Avisos, aviso => aviso.Codigo == "INC3550");
        }
        else
        {
            Assert.Equal(erro, Assert.Single(await RecusaAsync(yaml, ComIndice(indice))).Codigo);
        }
    }

    public static TheoryData<string> PelaGerada => ["expressao", "exclusao", "expressao-sem-referencias", "referencias-incompletas", "exclusao-incompleta", "gerada-sem-referencias", "gerada-incompleta", "gerada-de-gerada"];

    [Theory]
    [MemberData(nameof(PelaGerada))]
    public async Task A_tratada_alcanca_o_indice_por_expressao_exclusao_ou_gerada_tambem_pelo_fecho(string caso)
    {
        var limpo = Gerada("doc_limpo", "doc");
        var exclusao = (string[] colunas, bool incompletas) => new RestricaoExclusao { Nome = "ex_doc", Referencias = new ReferenciasDaExpressao { Colunas = colunas, Incompletas = incompletas } };
        var esquema = caso switch
        {
            "expressao" => ComIndice(Unico("uq_expr", Elemento(null, Padrao)), referencias: ["doc_limpo"], extras: [limpo]),
            "exclusao" => ComIndice(exclusoes: [exclusao(["doc_limpo"], false)], extras: [limpo]),
            "expressao-sem-referencias" => ComIndice(Unico("uq_expr", Elemento(null, Padrao))),
            "referencias-incompletas" => ComIndice(BancoFalso.Clonar(Unico("uq_expr", Elemento(null, Padrao)), (nameof(IndiceUnico.ReferenciasDasExpressoes), new ReferenciasDaExpressao { Colunas = [], Incompletas = true }))),
            "exclusao-incompleta" => ComIndice(exclusoes: [exclusao([], true)]),
            "gerada-sem-referencias" => ComIndice(Unico("uq_gerada", Elemento("doc_limpo")), extras: [BancoFalso.Clonar(limpo, (nameof(ColunaEsquema.ReferenciasDaGeracao), null))]),
            "gerada-incompleta" => ComIndice(Unico("uq_gerada", Elemento("doc_limpo")), extras: [BancoFalso.Clonar(limpo, (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = [], Incompletas = true }))]),
            _ => ComIndice(Unico("uq_gerada", Elemento("doc_final")), extras: [limpo, Gerada("doc_final", "doc_limpo")]),
        };

        var erro = Assert.Single(await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), esquema, Registro()));

        Assert.Equal((caso.StartsWith("exclusao", StringComparison.Ordinal) ? "INC3053" : "INC3050", "doc"), (erro.Codigo, erro.Coluna));
    }

    [Fact]
    public async Task O_tipo_que_ignora_os_espacos_finais_e_normalizado_e_o_terceiro_avisa()
    {
        var bpchar = BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), true));

        var plano = await PlanejarAsync(Doc("{ transformador: exemplo.injetiva@1 }"), ComIndice(Unico("uq_doc", Elemento("doc")), doc: bpchar), Registro());

        Assert.Equal("INC3554", Assert.Single(plano.Avisos).Codigo);
    }

    [Fact]
    public async Task O_indice_declarado_direto_na_particao_tambem_e_conferido()
    {
        var particionada = Trocar(ComIndice(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Tipo), TipoTabela.Particionada), (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = ["id"], ColunasIncompletas = false })));
        var particao = BancoFalso.Clonar(
            particionada.Tabelas.Single(tabela => tabela.Nome.Nome == "clientes"),
            (nameof(TabelaEsquema.Nome), Nome("clientes_p1")),
            (nameof(TabelaEsquema.Tipo), TipoTabela.Particao),
            (nameof(TabelaEsquema.Pai), Nome("clientes")),
            (nameof(TabelaEsquema.ChaveParticao), null),
            (nameof(TabelaEsquema.IndicesUnicos), new[] { Unico("uq_p1_doc", Elemento("doc")) }));
        var esquema = BancoFalso.Clonar(particionada, (nameof(EsquemaBanco.Tabelas), particionada.Tabelas.Append(particao).ToArray()), (nameof(EsquemaBanco.Acesso), Acesso(particionada.Tabelas.Append(particao), particionada.Sequences, [])));

        var erro = Assert.Single(await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), esquema, Registro()));

        Assert.Equal(("INC3050", "uq_p1_doc"), (erro.Codigo, erro.Objeto));
    }

    [Fact]
    public async Task A_chave_primaria_coberta_so_pela_exclusao_sai_uma_vez_pela_exclusao()
    {
        // A chave com sobreposição proibida: o leitor a entrega na chave primária e nas exclusões, sem índice primário.
        var temporal = Trocar(ComIndice(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.ChavePrimaria), (string[])["doc", "id"]),
            (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>()),
            (nameof(TabelaEsquema.RestricoesExclusao), new[] { new RestricaoExclusao { Nome = "pk_temporal", Referencias = new ReferenciasDaExpressao { Colunas = ["doc", "id"], Incompletas = false } } })));

        var erro = Assert.Single(await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), temporal, Registro()));

        Assert.Equal(("INC3053", "pk_temporal"), (erro.Codigo, erro.Objeto));
    }

    [Fact]
    public async Task O_aviso_INC3552_diz_o_motivo_de_cada_transformador_e_nao_vale_fora_do_texto()
    {
        var pix = await PlanejarAsync(Doc("{ transformador: br.chave-pix@1 }"), ComIndice(Unico("uq_doc", Elemento("doc")), doc: Texto("doc", 100)));
        var cpfInteiro = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), ComIndice(Unico("uq_doc", Elemento("doc", colacao: null)), doc: Inteiro("doc")));

        Assert.Contains("a chave Pix de e-mail e a aleatória", Assert.Single(pix.Avisos, aviso => aviso.Codigo == "INC3552").Mensagem, StringComparison.Ordinal);
        Assert.Empty(cpfInteiro.Avisos);
    }

    [Fact]
    public async Task O_terceiro_ligado_so_por_heranca_nao_avisa()
    {
        var numerica = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.Colacao), _numerica));
        var pessoa = new TabelaEsquema { Nome = Nome("pessoa"), Colunas = [numerica] };
        var funcionario = new TabelaEsquema { Nome = Nome("funcionario"), Tipo = TipoTabela.Herdeira, Pai = Nome("pessoa"), Colunas = [numerica] };
        var esquema = BancoFalso.Clonar(ComTabelas(pessoa, funcionario), (nameof(EsquemaBanco.Sequences), Array.Empty<SequenceEsquema>()), (nameof(EsquemaBanco.Acesso), Acesso([pessoa, funcionario], [], [])));

        var plano = await PlanejarAsync("tabelas:\n  loja.pessoa:\n    colunas:\n      doc: { transformador: exemplo.injetiva@1 }\n", esquema, Registro());

        Assert.DoesNotContain(plano.Avisos, aviso => aviso.Codigo == "INC3532");
    }

    [Fact]
    public async Task A_chave_primaria_com_exclusao_de_referencias_incompletas_tambem_e_conferida_direto()
    {
        // A exclusão com as referências incompletas não prova que cobre a chave: a chave primária sai também, sem objeto.
        var temporal = Trocar(ComIndice(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.ChavePrimaria), (string[])["doc", "id"]),
            (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>()),
            (nameof(TabelaEsquema.RestricoesExclusao), new[] { new RestricaoExclusao { Nome = "pk_temporal", Referencias = new ReferenciasDaExpressao { Colunas = ["doc", "id"], Incompletas = true } } })));

        var erros = await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), temporal, Registro());

        Assert.Equal(2, erros.Count);
        Assert.Contains(erros, erro => (erro.Codigo, erro.Objeto) == ("INC3053", "pk_temporal"));
        Assert.Contains(erros, erro => (erro.Codigo, erro.Objeto) == ("INC3050", null));
    }

    [Fact]
    public async Task Terceiro_ligado_por_chave_sem_a_caixa_da_o_aviso_INC3554_e_nao_o_INC3532()
    {
        var semCaixa = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), true));

        var plano = await PlanejarAsync(Doc("{ transformador: exemplo.injetiva@1 }"), ComChaveEstrangeira(doc: semCaixa), Registro());

        Assert.Equal([("clientes", "INC3554"), ("pedidos", "INC3554")], plano.Avisos.Select(aviso => (aviso.Tabela!.Tabela, aviso.Codigo)));
    }

    [Fact]
    public async Task A_embutida_ligada_por_chave_sem_igualdade_exata_nao_avisa()
    {
        var numerica = BancoFalso.Clonar(Texto("doc", 100), (nameof(ColunaEsquema.Colacao), _numerica));

        var plano = await PlanejarAsync(Doc("{ transformador: br.cpf@1 }"), ComChaveEstrangeira(doc: numerica));

        Assert.DoesNotContain(plano.Avisos, aviso => aviso.Codigo is "INC3532" or "INC3554");
    }

    [Fact]
    public async Task A_coluna_na_chave_primaria_e_na_chave_estrangeira_da_um_INC3050_por_causa()
    {
        // clientes.doc é a chave primária e é referenciada: o 3050 do índice basta, e o da chave sai só na filha.
        var esquema = Trocar(ComChave(), "pedidos", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Inteiro("cliente"), Texto("doc", 20) }),
            (nameof(TabelaEsquema.ChavesEstrangeiras), tabela.ChavesEstrangeiras.Append(new ChaveEstrangeira { Nome = "fk_doc", Colunas = ["doc"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["doc"], Imposta = true, Validada = true, DesligadaSemRestauracao = false }).ToArray())));

        var erros = await RecusaAsync(Doc("{ transformador: exemplo.fixa@1 }"), esquema, Registro());

        Assert.All(erros, erro => Assert.Equal("INC3050", erro.Codigo));
        Assert.Equal([("clientes", (string?)"pk_clientes"), ("pedidos", null)], erros.Select(erro => (erro.Tabela!.Tabela, erro.Objeto)));
    }

    private static string Doc(string regra) => $"tabelas:\n  loja.clientes:\n    colunas:\n      doc: {regra}\n";

    private static RegraColuna Regra(Plano plano) => plano.Tabelas[0].Colunas.Single(coluna => coluna.Nome == "doc").Regra!;

    private static ElementoIndice Elemento(string? coluna, ColacaoEsquema? colacao = null) => new() { Coluna = coluna, Colacao = colacao };

    // O elemento de coluna de texto vem com a colação padrão, salvo quando o caso a troca.
    private static ElementoIndice Elemento(string coluna) => new() { Coluna = coluna, Colacao = coluna == "id" ? null : Padrao };

    private static IndiceUnico Unico(string nome, params ElementoIndice[] elementos) => new() { Nome = nome, Primario = false, Elementos = elementos, NulosDistintos = true };

    private static ColunaEsquema Gerada(string nome, string de) => BancoFalso.Clonar(
        Texto(nome, 20),
        (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
        (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = [de], Incompletas = false }));

    // clientes(id, doc, nome) com o índice, as exclusões e as colunas extras dadas.
    private static EsquemaBanco ComIndice(
        IndiceUnico? indice = null, string[]? referencias = null, ColunaEsquema[]? extras = null, RestricaoExclusao[]? exclusoes = null, ColunaEsquema? doc = null)
    {
        var indices = new List<IndiceUnico> { new() { Nome = "pk_clientes", Primario = true, Elementos = [new ElementoIndice { Coluna = "id" }], NulosDistintos = true } };
        if (indice is not null)
        {
            indices.Add(referencias is null ? indice : BancoFalso.Clonar(indice, (nameof(IndiceUnico.ReferenciasDasExpressoes), new ReferenciasDaExpressao { Colunas = referencias, Incompletas = false })));
        }

        return Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), doc ?? Texto("doc", 20), Texto("nome", 100) }.Concat(extras ?? []).ToArray()),
            (nameof(TabelaEsquema.IndicesUnicos), indices.ToArray()),
            (nameof(TabelaEsquema.RestricoesExclusao), exclusoes ?? [])));
    }

    // A chave primária na coluna de documento.
    private static EsquemaBanco ComChave() => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.Anulavel), false)), Texto("nome", 100) }),
        (nameof(TabelaEsquema.ChavePrimaria), (string[])["doc"]),
        (nameof(TabelaEsquema.IndicesUnicos), new[] { new IndiceUnico { Nome = "pk_clientes", Primario = true, Elementos = [new ElementoIndice { Coluna = "doc", Colacao = Padrao }], NulosDistintos = true } })));

    // pedidos.doc referencia clientes.doc.
    private static EsquemaBanco ComChaveEstrangeira(ColunaEsquema? doc = null)
    {
        var comDoc = ComIndice(doc: doc);
        return Trocar(comDoc, "pedidos", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Inteiro("cliente"), BancoFalso.Clonar(doc ?? Texto("doc", 20), (nameof(ColunaEsquema.Nome), "doc")) }),
            (nameof(TabelaEsquema.ChavesEstrangeiras), tabela.ChavesEstrangeiras.Append(new ChaveEstrangeira { Nome = "fk_doc", Colunas = ["doc"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["doc"], Imposta = true, Validada = true, DesligadaSemRestauracao = false }).ToArray())));
    }

    private static RegistroTransformadores Registro()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaInjetiva());
        registro.Adicionar(new FabricaInjetiva("exemplo.fixa", Injetividade.Nenhuma));
        return registro;
    }

    private static Task<Plano> PlanejarAsync(string yaml, EsquemaBanco esquema, RegistroTransformadores? registro = null) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(esquema, esquema), yaml: Yaml.Minimo + yaml, registro: registro);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(string yaml, EsquemaBanco esquema, RegistroTransformadores? registro = null) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(yaml, esquema, registro)), exactMatch: true).Erros;

    // Uma fábrica de fora de texto, injetiva ou não, que devolve o texto como veio (o planejar só a cria e descarta).
    private sealed class FabricaInjetiva(string nome = "exemplo.injetiva", Injetividade injetividade = Injetividade.Garantida) : FabricaTransformador
    {
        public override string Nome => nome;

        public override int Versao => 1;

        public override Injetividade Injetividade => injetividade;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Mesma(nome, injetividade);

        private sealed class Mesma(string nome, Injetividade injetividade) : Transformador
        {
            public override string Nome => nome;

            public override int Versao => 1;

            public override Injetividade Injetividade => injetividade;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);
        }
    }
}
