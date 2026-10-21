using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A coerência do esquema no motor (decisão 78c), igual à da suíte de conformidade nas regras de coerência: um teste
/// diferencial roda os dois sobre cada defeito do dialeto falso, e cada regra sem defeito no falso tem caso próprio.
/// </summary>
public sealed class ConsistenciaDoEsquemaTestes
{
    // Os defeitos do falso que a suíte pega na consistência, classificados: coerência (o motor também recusa, INC3002) ou só
    // presença e formato (o motor aceita: trata o campo nulo como o pior caso no ponto de uso, e a ordem é da suíte).
    private static readonly Dictionary<Defeito, bool> _coerencia = new()
    {
        [Defeito.ChaveDeParticaoAusente] = true,
        [Defeito.ChaveDeParticaoComColunaInexistente] = true,
        [Defeito.ChaveDeParticaoVaziaCompleta] = true,
        [Defeito.ChaveDesbalanceada] = true,
        [Defeito.ChaveEstrangeiraRepetida] = true,
        [Defeito.ChavePrimariaForaDeOrdem] = true,
        [Defeito.CheckCitaColunaInexistente] = true,
        [Defeito.ColacaoDoElementoIncoerente] = true,
        [Defeito.ColacaoEmColunaInteira] = true,
        [Defeito.ColacaoEmElementoNaoTexto] = true,
        [Defeito.ColacaoPadraoComMarcaNula] = true,
        [Defeito.ComprimentoForaDoTipo] = true,
        [Defeito.DependenciaCircular] = true,
        [Defeito.DependenciaDeObjetosGrandes] = true,
        [Defeito.DependenciaDeResidualInexistente] = true,
        [Defeito.DependenciasForaDeOrdem] = false,
        [Defeito.DeterministicaComMarcaNula] = true,
        [Defeito.DeterministicaQueIgnoraCaixa] = true,
        [Defeito.DoisIndicesPrimarios] = true,
        [Defeito.ElementoDeColunaInexistente] = true,
        [Defeito.ElementoDeExpressaoSemReferencias] = false,
        [Defeito.ElementoTextoSemColacao] = false,
        [Defeito.ExclusaoForaDeOrdem] = false,
        [Defeito.ExclusaoQueNaoCobreAPrimaria] = false,
        [Defeito.ExpressoesSemElementoDeExpressao] = true,
        [Defeito.FaixaForaDoTipo] = true,
        [Defeito.FaixaInvertida] = true,
        [Defeito.FaixaSemPositivos] = true,
        [Defeito.GeracaoCitaAPropria] = true,
        [Defeito.GeracaoCitaColunaInexistente] = true,
        [Defeito.GeradaNaoMarcada] = true,
        [Defeito.GeradaSemReferencias] = false,
        [Defeito.HerancaSemMae] = true,
        [Defeito.IndicePrimarioDivergenteDaChave] = true,
        [Defeito.InteiroSemFaixa] = false,
        [Defeito.LimiteEmPontosSemCodificacao] = true,
        [Defeito.MaeDeTipoErrado] = true,
        [Defeito.ObjetosGrandesComMarcas] = true,
        [Defeito.OutroPaiForaDoEsquema] = true,
        [Defeito.ParticaoSemPai] = true,
        [Defeito.PrecisaoForaDeDecimal] = true,
        [Defeito.PredicadoCitaColunaInexistente] = true,
        [Defeito.PrimariaSemIndiceForaDeParticao] = false,
        [Defeito.ReferenciasDoIndiceForaDeOrdem] = false,
        [Defeito.ReferenciasForaDeOrdem] = false,
        [Defeito.ReferenciasSemGeracao] = true,
        [Defeito.SequenceComColunaRepetida] = true,
        [Defeito.TabelaDuplicada] = true,
        [Defeito.TemporalSemHistorico] = true,
        [Defeito.TextoSemColacao] = false,
        [Defeito.TipoCanonicoForaDoSuportado] = true,
        [Defeito.TipoDeTabelaTrocado] = true,
        [Defeito.TipoMalClassificado] = false,
        [Defeito.TriggerHerdadaForaDeParticao] = true,
    };

    public static TheoryData<Defeito> DefeitosDaConsistencia => [.. SuiteDeConformidadeTestes.DefeitosQueCitam(nameof(SuiteFalsa.O_esquema_lido_e_consistente))];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task O_esquema_do_falso_certo_e_coerente(bool completo)
    {
        Assert.Empty(ConsistenciaDoEsquema.Problemas(await EsquemaDoFalso.LerAsync(completo, Defeito.Nenhum)));
        Assert.Empty(ConsistenciaDoEsquema.Problemas(Coerente()));
    }

    [Theory]
    [MemberData(nameof(DefeitosDaConsistencia))]
    public async Task O_motor_e_a_suite_dao_o_mesmo_veredito_nos_defeitos_de_coerencia(Defeito defeito)
    {
        Assert.True(_coerencia.TryGetValue(defeito, out var coerencia), $"{defeito} cita a consistência e não está classificado.");
        var completo = SuiteDeConformidadeTestes.Variante(defeito);

        var problemas = ConsistenciaDoEsquema.Problemas(await EsquemaDoFalso.LerAsync(completo, defeito));

        Assert.True(coerencia ? problemas.Count > 0 : problemas.Count == 0, $"{defeito}: {string.Join("; ", problemas)}");
    }

    [Fact]
    public void Todo_defeito_classificado_cita_a_consistencia()
    {
        Assert.Equal(
            SuiteDeConformidadeTestes.DefeitosQueCitam(nameof(SuiteFalsa.O_esquema_lido_e_consistente)).Order(),
            _coerencia.Keys.Order());
    }

    // As regras de coerência sem defeito no falso, uma por caso, com o objeto citado.
    public static TheoryData<string, string> Regras => new()
    {
        { "coluna repetida", "coluna loja.clientes.nome repetida" },
        { "chave primária com coluna que não existe", "chave primária de loja.clientes com a coluna sumida, que não existe" },
        { "escala sem precisão", "coluna loja.clientes.id com escala sem precisão" },
        { "marca de texto fora de texto", "coluna loja.clientes.id com marca de texto fora de texto" },
        { "colação fora de texto", "coluna loja.clientes.id com colação fora de texto" },
        { "colação determinística com marca nula", "coluna loja.clientes.cpf com colação determinística e marca que não é falsa" },
        { "comprimento fora de texto", "coluna loja.clientes.id com comprimento fora de texto e binário" },
        { "faixa vazia", "coluna loja.clientes.id com faixa vazia" },
        { "faixa sem positivos", "coluna loja.clientes.id com faixa sem positivos" },
        { "faixa fora de inteiro", "coluna loja.clientes.cpf com faixa fora de inteiro" },
        { "referências da geração sem ser gerada", "coluna loja.clientes.nome com referências da geração sem ser gerada" },
        { "geração da própria coluna", "coluna loja.clientes.nome com referência da geração a ela mesma" },
        { "tipo canônico fora de suportado", "coluna loja.clientes.id com tipo canônico fora de 'suportado'" },
        { "dois índices primários", "tabela loja.clientes com 2 índices primários" },
        { "índice com coluna que não existe", "índice uq_sumida de loja.clientes com o elemento sumida, coluna que não existe" },
        { "residual casado por nome e tipo", "permissões do objeto residual loja.resumo (VisaoMaterializada) lidas 0 vezes" },
        { "permissão de tabela que não foi lida", "permissões de tabela loja.sumida, que não foi lida" },
        { "permissão de tabela repetida", "permissões da tabela loja.pedidos lidas 2 vezes" },
        { "colação padrão com marca nula", "colação padrão do banco determinística com marca que não é falsa" },
        { "sequence repetida", "objeto loja.clientes_id_seq repetido" },
        { "residual repetido", "objeto loja.resumo repetido" },
        { "índice repetido", "índice uq_cpf de loja.clientes repetido" },
        { "CHECK repetido", "CHECK ck_id de loja.clientes repetido" },
        { "exclusão repetida", "exclusão ex_id de loja.clientes repetida" },
        { "trigger repetida", "trigger tg_clientes de loja.clientes repetida" },
        { "FK com coluna que não existe", "chave fk_pedidos_clientes de loja.pedidos com coluna que não existe" },
        { "sequence com coluna que não existe", "sequence loja.clientes_id_seq usada pela coluna loja.clientes.sumida, que não existe" },
        { "residual que depende dele mesmo", "residual loja.resumo depende dele mesmo" },
        { "exclusão com coluna que não existe", "exclusão ex_sumida de loja.clientes citando sumida, coluna que não existe" },
        { "CHECK com coluna que não existe", "CHECK ck_sumida de loja.clientes citando sumida, coluna que não existe" },
        { "expressão do índice com coluna que não existe", "índice uq_expressao de loja.clientes com expressões citando sumida, coluna que não existe" },
        { "predicado do índice com coluna que não existe", "índice uq_parcial de loja.clientes com predicado citando sumida, coluna que não existe" },
        { "permissão de sequence repetida", "permissões da sequence loja.clientes_id_seq lidas 2 vezes" },
        { "pai fora do esquema", "pai de loja.pedidos (Herdeira) ausente, sobrando ou fora do esquema" },
        { "chave de partição sobrando", "chave de partição de loja.clientes (Comum) ausente ou sobrando" },
        { "outros pais com o pai", "outros pais de loja.pedidos (Herdeira) fora de herdeira, repetidos, com o pai ou fora do esquema" },
        { "outros pais repetidos", "outros pais de loja.pedidos (Herdeira) fora de herdeira, repetidos, com o pai ou fora do esquema" },
        { "histórico sem temporal", "histórico loja.clientes sem uma temporal só que o aponte" },
        { "trigger herdada fora de partição", "trigger tg_clientes de loja.clientes herdada fora de partição" },
    };

    private static readonly ReferenciasDaExpressao _doId = new() { Colunas = ["id"], Incompletas = false };
    private static readonly ReferenciasDaExpressao _sumida = new() { Colunas = ["sumida"], Incompletas = false };

    [Theory]
    [MemberData(nameof(Regras))]
    public void Cada_regra_de_coerencia_recusa_citando_o_objeto(string regra, string problema)
    {
        var esquema = regra switch
        {
            "coluna repetida" => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(Texto("nome", 10)).ToArray()))),
            "chave primária com coluna que não existe" => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavePrimaria), (string[])["sumida"]), (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>()))),
            "escala sem precisão" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Tipo), null), (nameof(ColunaEsquema.Faixa), null), (nameof(ColunaEsquema.Escala), 2)),
            "marca de texto fora de texto" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), false)),
            "colação fora de texto" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Colacao), Padrao)),
            "colação determinística com marca nula" => TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Colacao), BancoFalso.Clonar(Padrao, (nameof(ColacaoEsquema.IgnoraAcento), null)))),
            "comprimento fora de texto" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 8, Unidade = UnidadeComprimento.Bytes })),
            "faixa vazia" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 5, Maximo = 5 })),
            "faixa sem positivos" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = -10, Maximo = 0 })),
            "faixa fora de inteiro" => TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 0, Maximo = 10 })),
            "referências da geração sem ser gerada" => TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["cpf"], Incompletas = false })),
            "geração da própria coluna" => TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada), (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["nome"], Incompletas = false })),
            "tipo canônico fora de suportado" => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva)),
            "dois índices primários" => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), (IndiceUnico[])[.. tabela.IndicesUnicos, BancoFalso.Clonar(tabela.IndicesUnicos[0], (nameof(IndiceUnico.Nome), "pk_outro"))]))),
            "índice com coluna que não existe" => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), tabela.IndicesUnicos.Append(new IndiceUnico { Nome = "uq_sumida", Primario = false, Elementos = [new ElementoIndice { Coluna = "sumida" }], NulosDistintos = true }).ToArray()))),
            "residual casado por nome e tipo" => ComResidualDeOutroTipo(),
            "permissão de tabela que não foi lida" => ComPermissoes(acesso => acesso.Tabelas.Append(BancoFalso.Clonar(acesso.Tabelas[0], (nameof(PermissoesTabela.Tabela), Nome("sumida"))))),
            "permissão de tabela repetida" => ComPermissoes(acesso => acesso.Tabelas.Append(acesso.Tabelas[1])),
            "colação padrão com marca nula" => BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Propriedades), BancoFalso.Clonar(Coerente().Propriedades!, (nameof(PropriedadesBanco.Colacao), BancoFalso.Clonar(Padrao, (nameof(ColacaoEsquema.IgnoraCaixa), null)))))),
            "sequence repetida" => BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Sequences), (SequenceEsquema[])[Coerente().Sequences[0], Coerente().Sequences[0]])),
            "residual repetido" => ComResiduais(Resumo(), Resumo()),
            "índice repetido" => NaTabela(nameof(TabelaEsquema.IndicesUnicos), (IndiceUnico[])[.. Clientes().IndicesUnicos, UnicoDoCpf(), UnicoDoCpf()]),
            "CHECK repetido" => NaTabela(nameof(TabelaEsquema.RestricoesCheck), (RestricaoCheck[])[Check("ck_id", _doId), Check("ck_id", _doId)]),
            "exclusão repetida" => NaTabela(nameof(TabelaEsquema.RestricoesExclusao), (RestricaoExclusao[])[new() { Nome = "ex_id", Referencias = _doId }, new() { Nome = "ex_id", Referencias = _doId }]),
            "trigger repetida" => NaTabela(nameof(TabelaEsquema.Triggers), (TriggerEsquema[])[Trigger(herdada: false), Trigger(herdada: false)]),
            "FK com coluna que não existe" => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), (ChaveEstrangeira[])[BancoFalso.Clonar(tabela.ChavesEstrangeiras[0], (nameof(ChaveEstrangeira.Colunas), (string[])["sumida"]))]))),
            "sequence com coluna que não existe" => BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Sequences), (SequenceEsquema[])[BancoFalso.Clonar(Coerente().Sequences[0], (nameof(SequenceEsquema.Colunas), (ReferenciaColuna[])[new ReferenciaColuna(Nome("clientes"), "sumida")]))])),
            "residual que depende dele mesmo" => ComResiduais(BancoFalso.Clonar(Resumo(), (nameof(ObjetoResidual.DependeDe), (NomeObjeto[])[Nome("resumo")]))),
            "exclusão com coluna que não existe" => NaTabela(nameof(TabelaEsquema.RestricoesExclusao), (RestricaoExclusao[])[new() { Nome = "ex_sumida", Referencias = _sumida }]),
            "CHECK com coluna que não existe" => NaTabela(nameof(TabelaEsquema.RestricoesCheck), (RestricaoCheck[])[Check("ck_sumida", _sumida)]),
            "expressão do índice com coluna que não existe" => NaTabela(nameof(TabelaEsquema.IndicesUnicos), (IndiceUnico[])[.. Clientes().IndicesUnicos, new() { Nome = "uq_expressao", Primario = false, Elementos = [new ElementoIndice()], ReferenciasDasExpressoes = _sumida, NulosDistintos = true }]),
            "predicado do índice com coluna que não existe" => NaTabela(nameof(TabelaEsquema.IndicesUnicos), (IndiceUnico[])[.. Clientes().IndicesUnicos, BancoFalso.Clonar(UnicoDoCpf(), (nameof(IndiceUnico.Nome), "uq_parcial"), (nameof(IndiceUnico.Predicado), _sumida))]),
            "permissão de sequence repetida" => BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(Coerente().Acesso!, (nameof(AcessoLido.Sequences), (PermissoesSequence[])[.. Coerente().Acesso!.Sequences, .. Coerente().Acesso!.Sequences])))),
            "pai fora do esquema" => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Tipo), TipoTabela.Herdeira), (nameof(TabelaEsquema.Pai), Nome("sumida")))),
            "chave de partição sobrando" => NaTabela(nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = ["id"], ColunasIncompletas = false }),
            "outros pais com o pai" => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Tipo), TipoTabela.Herdeira), (nameof(TabelaEsquema.Pai), Nome("clientes")), (nameof(TabelaEsquema.OutrosPais), (NomeObjeto[])[Nome("clientes")]))),
            "outros pais repetidos" => Trocar(
                ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("enderecos"), Colunas = [Inteiro("id")] }),
                "pedidos",
                tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Tipo), TipoTabela.Herdeira), (nameof(TabelaEsquema.Pai), Nome("clientes")), (nameof(TabelaEsquema.OutrosPais), (NomeObjeto[])[Nome("enderecos"), Nome("enderecos")]))),
            "histórico sem temporal" => NaTabela(nameof(TabelaEsquema.Tipo), TipoTabela.HistoricoDeTemporal),
            "trigger herdada fora de partição" => NaTabela(nameof(TabelaEsquema.Triggers), (TriggerEsquema[])[Trigger(herdada: true)]),
            _ => throw new ArgumentOutOfRangeException(nameof(regra)),
        };

        Assert.Contains(problema, ConsistenciaDoEsquema.Problemas(esquema));
    }

    [Fact]
    public void A_presenca_nunca_recusa_no_motor()
    {
        // Texto sem colação, inteiro sem faixa, gerada sem as referências, chave primária sem índice, propriedades e
        // permissões não informadas: o pior caso no ponto de uso, nunca defeito.
        var esquema = Trocar(
            TrocarColuna(
                TrocarColuna(
                    TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Colacao), null)),
                    "clientes", "id", (nameof(ColunaEsquema.Faixa), null)),
                "clientes", "nome", (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada)),
            "pedidos",
            tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>())));
        esquema = TrocarColuna(esquema, "clientes", "nome", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 10, Unidade = UnidadeComprimento.PontosDeCodigo }));
        esquema = BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Propriedades), null), (nameof(EsquemaBanco.Acesso), null));

        Assert.Empty(ConsistenciaDoEsquema.Problemas(esquema));
    }

    [Fact]
    public void As_permissoes_sao_casadas_pelo_objeto_e_nunca_pelo_texto_do_nome()
    {
        // "a.b"."c" e "a"."b.c" escrevem o mesmo texto e são tabelas diferentes.
        TabelaEsquema Tabela(string esquema, string nome) => new() { Nome = new NomeObjeto(esquema, nome), Colunas = [Inteiro("id")] };
        var tabelas = new[] { Tabela("a.b", "c"), Tabela("a", "b.c") };
        var coerente = BancoFalso.Clonar(ComTabelas(tabelas), (nameof(EsquemaBanco.Acesso), Acesso(tabelas, [], [])));
        var trocada = BancoFalso.Clonar(ComTabelas(tabelas[..1]), (nameof(EsquemaBanco.Acesso), Acesso(tabelas[1..], [], [])));

        Assert.Empty(ConsistenciaDoEsquema.Problemas(coerente));
        Assert.Equal(["permissões da tabela a.b.c lidas 0 vezes", "permissões de tabela a.b.c, que não foi lida"], ConsistenciaDoEsquema.Problemas(trocada));
    }

    [Fact]
    public void O_ciclo_entre_visoes_e_sempre_recusado()
    {
        ObjetoResidual Visao(string nome, string outra, bool? incompletas) => new()
        {
            Nome = Nome(nome),
            Tipo = TipoObjetoResidual.VisaoMaterializada,
            DependeDe = [Nome(outra)],
            DependenciasIncompletas = incompletas,
            Populada = true,
        };

        EsquemaBanco Com(bool? incompletas)
        {
            var residuais = new[] { Visao("a", "b", incompletas), Visao("b", "a", incompletas) };
            var coerente = Coerente();
            return BancoFalso.Clonar(coerente, (nameof(EsquemaBanco.ObjetosResiduais), residuais), (nameof(EsquemaBanco.Acesso), Acesso(coerente.Tabelas, coerente.Sequences, residuais)));
        }

        Assert.Contains("residual loja.a numa dependência circular", ConsistenciaDoEsquema.Problemas(Com(false)));
        Assert.Contains("residual loja.a numa dependência circular", ConsistenciaDoEsquema.Problemas(Com(null)));
        // O DependeDe nunca tem ciclo: num ciclo real, o leitor tira a dependência que o fecha (decisão 84a).
        Assert.Contains("residual loja.a numa dependência circular", ConsistenciaDoEsquema.Problemas(Com(true)));
    }

    [Fact]
    public void Os_nomes_saem_escapados()
    {
        var esquema = TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Nome), "id\u202E\n"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 1, Maximo = 1 }));
        esquema = Trocar(esquema, "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavePrimaria), (string[])["id\u202E\n"]), (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>())));

        Assert.Contains("coluna loja.clientes.id\\u202E\\u000A com faixa vazia", ConsistenciaDoEsquema.Problemas(esquema));
    }

    private static EsquemaBanco NaTabela(string propriedade, object? valor) =>
        Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (propriedade, valor)));

    private static IndiceUnico UnicoDoCpf() => new() { Nome = "uq_cpf", Primario = false, Elementos = [new ElementoIndice { Coluna = "cpf", Colacao = Padrao }], NulosDistintos = true };

    private static RestricaoCheck Check(string nome, ReferenciasDaExpressao referencias) => new() { Nome = nome, Referencias = referencias, Imposta = true };

    private static TriggerEsquema Trigger(bool herdada) => new() { Nome = "tg_clientes", Ligada = true, Herdada = herdada };

    private static ObjetoResidual Resumo() => new() { Nome = Nome("resumo"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependeDe = [], DependenciasIncompletas = false, Populada = true };

    private static EsquemaBanco ComResiduais(params ObjetoResidual[] residuais)
    {
        var coerente = Coerente();
        return BancoFalso.Clonar(coerente, (nameof(EsquemaBanco.ObjetosResiduais), residuais), (nameof(EsquemaBanco.Acesso), Acesso(coerente.Tabelas, coerente.Sequences, residuais.DistinctBy(residual => residual.Nome))));
    }

    private static EsquemaBanco ComPermissoes(Func<AcessoLido, IEnumerable<PermissoesTabela>> tabelas)
    {
        var coerente = Coerente();
        return BancoFalso.Clonar(coerente, (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(coerente.Acesso!, (nameof(AcessoLido.Tabelas), tabelas(coerente.Acesso!).ToArray()))));
    }

    // A permissão do residual com o mesmo nome e outro tipo não casa.
    private static EsquemaBanco ComResidualDeOutroTipo()
    {
        var coerente = Coerente();
        var residual = new ObjetoResidual { Nome = Nome("resumo"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependeDe = [], DependenciasIncompletas = false, Populada = true };
        var acesso = Acesso(coerente.Tabelas, coerente.Sequences, [BancoFalso.Clonar(residual, (nameof(ObjetoResidual.Tipo), TipoObjetoResidual.ObjetosGrandes), (nameof(ObjetoResidual.DependenciasIncompletas), null), (nameof(ObjetoResidual.Populada), null))]);
        return BancoFalso.Clonar(coerente, (nameof(EsquemaBanco.ObjetosResiduais), new[] { residual }), (nameof(EsquemaBanco.Acesso), acesso));
    }
}
