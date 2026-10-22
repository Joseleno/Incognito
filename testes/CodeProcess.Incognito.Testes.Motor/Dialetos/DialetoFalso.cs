using System.Reflection;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>Um defeito do dialeto falso (ou da declaração da fixture), cada um ligado aos testes da suíte que precisam pegá-lo.</summary>
public enum Defeito
{
    Nenhum,
    CasoSemDeclaracao,
    TipoMalClassificado,
    TipoCanonicoForaDoSuportado,
    ParticaoSemPai,
    HerancaSemMae,
    SequenceComUmaColuna,
    SequenceComColunaRepetida,
    GeradaNaoMarcada,
    GeradaNaCopia,
    ChaveForaDeOrdem,
    ChaveDesbalanceada,
    TabelaDuplicada,
    TemporalSemHistorico,
    ResiduoAusente,
    SegurancaDeLinhaAusente,
    PublicacaoAusente,
    PublicacaoNaoInformada,
    PublicacaoNaRaiz,
    AssinaturaAusente,
    AssinaturaNaoInformada,
    MascaraAusente,
    NomeNaCaixaErrada,
    CitacaoSemEscape,
    CitacaoErradaNoDestino,
    DobraComToLowerInvariant,
    IgnoraAspas,
    SemLimite,
    LimiteEmCaracteres,
    EsquemaDoIncognitoLido,
    IncognitoForaDoEsquema,
    EscondeEsquemaReservado,
    NaoInformaEsquemaReservado,
    ReservadoComOutroEsquema,
    TabelasForaDeOrdem,
    LeituraInstavel,
    IdentidadeInstavel,
    BancoErrado,
    LinhagemVazia,
    InventaRecursos,

    // O inventário: cada um mexe num objeto que nenhum outro caso declara.
    InventarioVazio,
    TabelaOmitida,
    TabelaOmitidaNoDestino,
    ColunaAMais,
    ColunaAMenos,
    ColunasForaDeOrdem,
    PropriedadeDeColunaTrocada,
    ChaveEstrangeiraRepetida,
    ChaveEstrangeiraComReferenciadaErrada,
    ChavePrimariaForaDeOrdem,
    TipoDeTabelaTrocado,
    SequenceOmitida,
    SituacaoTrocadaNoDestino,
    SituacaoTrocadaForaDaMatriz,
    MascaraTrocadaNoDestino,
    TipoCanonicoTrocadoForaDaMatriz,

    // A estrutura que só a consistência e o inventário conferem.
    OutroPaiForaDoEsquema,
    ChaveDeParticaoAusente,
    ChaveDeParticaoComColunaInexistente,
    ChaveDeParticaoVaziaCompleta,
    MaeDeTipoErrado,

    // A semântica da coluna e da colação: a consistência e o inventário.
    ColacaoEmColunaInteira,
    TextoSemColacao,
    InteiroSemFaixa,
    FaixaInvertida,
    PrecisaoForaDeDecimal,
    LimiteEmPontosSemCodificacao,
    GeradaSemReferencias,
    GeracaoCitaAPropria,
    DeterministicaQueIgnoraCaixa,
    ComprimentoForaDoTipo,
    FaixaForaDoTipo,
    ReferenciasSemGeracao,
    FaixaSemPositivos,
    DeterministicaComMarcaNula,
    ColacaoPadraoComMarcaNula,
    GeracaoCitaColunaInexistente,
    ReferenciasForaDeOrdem,

    // As propriedades do banco, a matriz e o sentinela.
    PropriedadesAusentes,
    MatrizDescoberta,
    SentinelaNumNome,
    SemSentinelas,
    SentinelaVazio,
    SentinelaNaIdentidadeDaOrigem,
    SentinelaNoTextoDoDestino,
    PropriedadesAusentesNoDestino,
    MatrizContraditoria,
    MatrizSemMotivo,

    // Os objetos da tabela e as dependências dos residuais: o inventário e a consistência.
    IndiceUnicoOmitido,
    IndiceAnexadoDaParticaoListado,
    ExclusaoComoIndiceUnico,
    TriggerInternaListada,
    CheckHerdadoNaParticao,
    CheckInventado,
    FkComRiDesligadaComoImposta,
    ElementoDeExpressaoSemReferencias,
    PredicadoCitaColunaInexistente,
    IndicePrimarioDivergenteDaChave,
    DoisIndicesPrimarios,
    DependenciaCircular,
    DependenciaDeResidualInexistente,
    TriggerHerdadaForaDeParticao,
    ElementoTextoSemColacao,
    PrimariaSemIndiceForaDeParticao,
    ColacaoEmElementoNaoTexto,
    ElementoDeColunaInexistente,
    ExpressoesSemElementoDeExpressao,
    ReferenciasDoIndiceForaDeOrdem,
    CheckCitaColunaInexistente,
    ExclusaoForaDeOrdem,
    DependenciasForaDeOrdem,
    ColacaoDoElementoIncoerente,
    ExclusaoQueNaoCobreAPrimaria,
    DependenciaDeObjetosGrandes,
    ObjetosGrandesComMarcas,

    // As permissões do login e o transporte: o fato de cada um.
    AcessoAusente,
    AcessoDoLeitorNoDestino,
    PermissaoDeTabelaInexistente,
    PodeLerComColunaSemLeitura,
    ColunaSemLeituraInexistente,
    ColunasSemLeituraForaDeOrdem,
    ExcecaoDeAcessoInexistente,
    TransporteAusente,
    TransporteOtimista,
    TransporteVerificadoSemCifra,
    TransporteLocalTrocado,
    TransporteVerificadoTrocado,
    TransporteOtimistaNoDestino,
    PermissaoDeTabelaRepetida,
    SegurancaDeLinhaVazia,
    PublicacaoVazia,
    AssinaturaVazia,

    // A recusa da conexão inaceitável: com o tipo comum, com o que estava na string, fora da faixa, ou não recusa.
    RecusaComoExcecaoComum,
    RecusaComAString,
    RecusaForaDaFaixa,
    ConexaoInaceitavelAceita,

    // A classe concreta da suíte aninhada em outra, ou aberta.
    SuiteAninhada,
    SuiteAberta,
}

/// <summary>
/// O banco do dialeto falso: um esquema completo (todos os recursos do catálogo) ou um sem os recursos opcionais, e o
/// que a fixture declara sobre cada um. Nomes fictícios.
/// </summary>
internal static class BancoFalso
{
    /// <summary>O texto que nenhuma leitura pode trazer.</summary>
    internal const string Sentinela = "zqFALSOqz";

    /// <summary>O segundo texto que nenhuma leitura pode trazer.</summary>
    internal const string SentinelaCurto = "zqcurto";

    private const string Loja = "loja";

    internal static readonly ColacaoEsquema Padrao = new() { Nome = "default", Deterministica = true, TemRegras = false, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };

    // Não determinística com as marcas medidas, e com regras (marcas nulas).
    private static readonly ColacaoEsquema _semCaixa = new() { Nome = "sem_caixa", Esquema = Loja, Deterministica = false, TemRegras = false, IgnoraCaixa = true, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly ColacaoEsquema _comRegras = new() { Nome = "com_regras", Esquema = Loja, Deterministica = false, TemRegras = true };

    internal static NomeObjeto Nome(string nome) => new(Loja, nome);

    internal static ReferenciaColuna Coluna(string tabela, string coluna) => new(Nome(tabela), coluna);

    /// <summary>O esquema que o banco tem de verdade, inclusive o do Incognito, fora de ordem.</summary>
    internal static List<TabelaEsquema> Tabelas(bool completo)
    {
        var tabelas = new List<TabelaEsquema>
        {
            Tabela(
                "tipos",
                Texto("texto"),
                Comum("dados", "jsonb", SituacaoTipo.ComRessalva),
                Comum("versao", "rowversion", SituacaoTipo.SuportadoPorExclusao),
                Clonar(Texto("codigo"), (nameof(ColunaEsquema.TipoNativo), "loja.codigo"), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva), (nameof(ColunaEsquema.Tipo), null),
                    (nameof(ColunaEsquema.TipoBase), "text"), (nameof(ColunaEsquema.RestritaPeloTipo), true))),
            Tabela("Cliente", Texto("nome")),
            Tabela("cliente", Texto("nome")),
            Tabela("select", Texto("nome")),
            Tabela("nota fiscal", Texto("numero")),
            new()
            {
                Nome = Nome("pedidos"),
                Tipo = completo ? TipoTabela.Particionada : TipoTabela.Comum,
                ChaveParticao = completo ? new ChaveParticao { Colunas = ["loja", "id"], ColunasIncompletas = false } : null,
                Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")],
                ChavePrimaria = ["loja", "id"],
                IndicesUnicos = [Indice("pk_pedidos", primario: true, Elemento("loja"), Elemento("id"))],
                RestricoesCheck = completo ? [new() { Nome = "ck_pedidos_loja", Referencias = Referencias("loja"), Imposta = true }] : [],
                Triggers = completo ? [new() { Nome = "tg_pedidos", Ligada = true, Herdada = false }] : [],
            },
            new()
            {
                Nome = Nome("itens"),
                Colunas = [Inteiro("pedido", anulavel: false), Inteiro("loja", anulavel: false), Inteiro("numero")],
                ChavesEstrangeiras =
                [
                    new() { Nome = "fk_itens_pedidos", Colunas = ["loja", "pedido"], Referenciada = Nome("pedidos"), ColunasReferenciadas = ["loja", "id"], Imposta = true, Validada = true },
                ],
            },
            new() { Nome = new NomeObjeto("incognito", "execucao"), Colunas = [Texto("status")] },
        };

        if (completo)
        {
            tabelas.AddRange(
            [
                // As partições têm a chave primária, com o índice anexado ao do pai (que fica no pai), e a cópia da trigger
                // do pai.
                new()
                {
                    Nome = Nome("pedidos_2025"),
                    Tipo = TipoTabela.Particao,
                    Pai = Nome("pedidos"),
                    Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")],
                    ChavePrimaria = ["loja", "id"],
                    Triggers = [new() { Nome = "tg_pedidos", Ligada = true, Herdada = true }],
                },
                new()
                {
                    Nome = Nome("pedidos_2026"),
                    Tipo = TipoTabela.Particao,
                    Pai = Nome("pedidos"),
                    Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")],
                    ChavePrimaria = ["loja", "id"],
                    Triggers = [new() { Nome = "tg_pedidos", Ligada = true, Herdada = true }],
                },
                Tabela("pessoa", Texto("nome")),
                new() { Nome = Nome("funcionario"), Tipo = TipoTabela.Herdeira, Pai = Nome("pessoa"), Colunas = [Texto("nome"), Texto("cargo")] },
                Tabela("contato", Texto("canal")),
                Tabela("endereco", Texto("cidade")),
                Tabela("telefone", Texto("numero")),
                new()
                {
                    Nome = Nome("ficha"),
                    Tipo = TipoTabela.Herdeira,
                    Pai = Nome("contato"),
                    OutrosPais = [Nome("endereco"), Nome("telefone")],
                    Colunas = [Texto("canal"), Texto("cidade"), Texto("numero")],
                    ChavesEstrangeiras =
                    [
                        new() { Nome = "fk_ficha_contato", Colunas = ["canal"], Referenciada = Nome("contato"), ColunasReferenciadas = ["canal"], Imposta = false, Validada = false },
                    ],
                },

                // Os objetos da tabela: um índice de dois elementos (um de expressão, com colação não determinística),
                // parcial e com um NULL só; um CHECK incompleto e não imposto; exclusões; uma trigger ligada e uma desligada.
                new()
                {
                    Nome = Nome("indexada"),
                    Colunas =
                    [
                        Inteiro("id", anulavel: false),
                        Texto("apelido"),
                        Texto("email"),
                        Inteiro("ini"),
                        Inteiro("fim"),
                        Clonar(Texto("codigo"), (nameof(ColunaEsquema.TipoNativo), "loja.codigo"), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva), (nameof(ColunaEsquema.Tipo), null)),
                    ],
                    ChavePrimaria = ["id"],
                    IndicesUnicos =
                    [
                        Indice("ix_indexada_codigo", primario: false, Elemento("codigo", Padrao)),
                        new()
                        {
                            Nome = "ix_indexada_expressoes",
                            Primario = false,
                            Elementos = [Elemento("apelido", _semCaixa), Elemento(null, _semCaixa)],
                            ReferenciasDasExpressoes = Referencias("email", "ini"),
                            Predicado = Referencias("id", "fim"),
                            NulosDistintos = false,
                        },
                        Indice("pk_indexada", primario: true, Elemento("id")),
                    ],
                    RestricoesCheck =
                    [
                        new() { Nome = "ck_indexada_faixa", Referencias = Referencias("ini", "fim"), Imposta = true },
                        new() { Nome = "ck_indexada_linha", Referencias = new() { Colunas = [], Incompletas = true }, Imposta = false },
                    ],
                    RestricoesExclusao =
                    [
                        new() { Nome = "ex_indexada", Referencias = Referencias("ini", "fim") },
                        new() { Nome = "ex_indexada_email", Referencias = Referencias("email") },
                    ],
                    Triggers = [new() { Nome = "tg_desligada", Ligada = false, Herdada = false }, new() { Nome = "tg_ligada", Ligada = true, Herdada = false }],
                },

                // A chave primária com sobreposição proibida: uma exclusão que cita todas as colunas dela, sem índice primário.
                new()
                {
                    Nome = Nome("periodos"),
                    Colunas = [Inteiro("sala", anulavel: false), Inteiro("inicio", anulavel: false)],
                    ChavePrimaria = ["sala", "inicio"],
                    RestricoesExclusao = [new() { Nome = "pk_periodos", Referencias = Referencias("sala", "inicio") }],
                },

                // Uma coluna por tipo canônico, as três unidades de comprimento e as colações não determinísticas.
                Tabela(
                    "canonicos",
                    Clonar(Comum("valor", "numeric", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Decimal), (nameof(ColunaEsquema.Precisao), 12), (nameof(ColunaEsquema.Escala), 2)),
                    Clonar(Comum("dia", "date", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Data)),
                    Clonar(Comum("momento", "timestamp", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.DataHora)),
                    Clonar(Comum("instante", "timestamptz", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.DataHoraComFuso)),
                    Clonar(Comum("conteudo", "bytea", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Binario), (nameof(ColunaEsquema.Comprimento), Limite(null, UnidadeComprimento.Bytes))),
                    Clonar(Comum("ativo", "boolean", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Booleano)),
                    Clonar(Comum("identificador", "uuid", SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Uuid)),
                    Clonar(Texto("curto"), (nameof(ColunaEsquema.TipoNativo), "varchar"), (nameof(ColunaEsquema.Colacao), _semCaixa), (nameof(ColunaEsquema.Comprimento), Limite(20, UnidadeComprimento.PontosDeCodigo))),
                    Clonar(Texto("fixo"), (nameof(ColunaEsquema.TipoNativo), "char"), (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), true), (nameof(ColunaEsquema.Comprimento), Limite(3, UnidadeComprimento.PontosDeCodigo))),
                    Clonar(Texto("email"), (nameof(ColunaEsquema.TipoNativo), "citext"), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), true)),
                    Clonar(Texto("largo"), (nameof(ColunaEsquema.TipoNativo), "nvarchar"), (nameof(ColunaEsquema.Colacao), _comRegras), (nameof(ColunaEsquema.Comprimento), Limite(30, UnidadeComprimento.UnidadesUtf16))),
                    Clonar(Inteiro("pequeno"), (nameof(ColunaEsquema.TipoNativo), "smallint"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = short.MinValue, Maximo = short.MaxValue }))),
                Tabela(
                    "calculos",
                    Inteiro("base"),
                    Inteiro("extra"),
                    Clonar(Comum("dobro", "integer", SituacaoTipo.SuportadoPorExclusao), (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
                        (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = int.MinValue, Maximo = int.MaxValue }),
                        (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["base", "extra"], Incompletas = false }))),
                Tabela("contadores", Clonar(Inteiro("sempre", anulavel: false), (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre))),
                new() { Nome = Nome("contratos"), Tipo = TipoTabela.Temporal, Historico = Nome("contratos_historico"), Colunas = [Texto("numero")] },
                new() { Nome = Nome("contratos_historico"), Tipo = TipoTabela.HistoricoDeTemporal, Colunas = [Texto("numero")] },
                new() { Nome = Nome("segredos"), SegurancaDeLinha = true, Colunas = [Texto("valor")] },
                Tabela("contatos", Clonar(Texto("email"), (nameof(ColunaEsquema.Mascarada), true))),
                new() { Nome = Nome("externa"), Estrangeira = true, Colunas = [Clonar(Texto("linha"), (nameof(ColunaEsquema.Situacao), SituacaoTipo.NaoSuportado), (nameof(ColunaEsquema.Tipo), null))] },
                new() { Nome = Nome("da_extensao"), Extensao = "extensao_falsa", Colunas = [Inteiro("id")] },
            ]);
        }

        // O banco completo informa a publicação e a assinatura de cada tabela, com uma partição publicada sozinha e outra
        // tabela assinada; o incompleto não informa.
        return completo
            ? [.. tabelas.Select(tabela => Clonar(tabela, (nameof(TabelaEsquema.Publicada), tabela.Nome.Equals(Nome("pedidos_2026"))), (nameof(TabelaEsquema.Assinada), tabela.Nome.Equals(Nome("contatos")))))]
            : tabelas;
    }

    // Os objetos do esquema do diário (a tabela e a sequence dele), em ordem ordinal de nome.
    internal static NomeObjeto[] Reservado(bool completo) =>
    [
        .. Tabelas(completo).Select(tabela => tabela.Nome).Concat(Sequences(completo).Select(sequence => sequence.Nome))
            .Where(nome => nome.Esquema == "incognito")
            .OrderBy(nome => nome.Nome, StringComparer.Ordinal),
    ];

    internal static List<SequenceEsquema> Sequences(bool completo)
    {
        var sequences = new List<SequenceEsquema>
        {
            new() { Nome = Nome("seq_numero"), Colunas = completo ? [Coluna("pedidos", "numero"), Coluna("itens", "numero")] : [Coluna("pedidos", "numero")] },
            new() { Nome = new NomeObjeto("incognito", "seq_execucao"), Colunas = [] },
        };
        if (completo)
        {
            sequences.Add(new() { Nome = Nome("seq_avulsa"), Colunas = [] });
            sequences.Add(new() { Nome = Nome("contadores_sempre_seq"), Colunas = [Coluna("contadores", "sempre")] });
        }

        return sequences;
    }

    // Uma materialized view que lê outras duas (uma vazia) e passa por função.
    internal static List<ObjetoResidual> Residuais(bool completo) =>
        completo
            ?
            [
                new() { Nome = Nome("mv_base"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependeDe = [], DependenciasIncompletas = false, Populada = false },
                new() { Nome = Nome("mv_clientes"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependeDe = [], DependenciasIncompletas = false, Populada = true },
                new()
                {
                    Nome = Nome("resumo_vendas"),
                    Tipo = TipoObjetoResidual.VisaoMaterializada,
                    DependeDe = [Nome("mv_base"), Nome("mv_clientes")],
                    DependenciasIncompletas = true,
                    Populada = true,
                },
                new() { Nome = new NomeObjeto("pg_catalog", "pg_largeobject"), Tipo = TipoObjetoResidual.ObjetosGrandes },
            ]
            : [];

    // Uma codificação que não é Unicode, para a perturbação ter o que alterar em cada campo.
    internal static PropriedadesBanco? Propriedades(bool completo) => completo
        ? new()
        {
            Versao = new Version(18, 6),
            Codificacao = new CodificacaoTexto { Nome = "LATIN1", Definida = true, Unicode = false, BytesPorCaractereMaximo = 1 },
            Colacao = Padrao,
        }
        : null;

    // As permissões de cada lado: o leitor da origem só lê (menos as colunas de uma tabela) e tem a leitura filtrada por linha
    // numa; o dono do destino pode tudo, menos numa tabela e num objeto de outro dono. Toda marca preenchida e listas de dois
    // itens, para a perturbação ter o que alterar e reordenar.
    internal static LadoDoAcesso Lado(bool destino, Defeito defeito = Defeito.Nenhum) => destino
        ? new()
        {
            Banco = new(PodeMarcarBanco: true, PodeCriarEsquema: true),
            Tabelas = new(true, true, true, true, true, true, true, false, false),
            ExcecoesDasTabelas = new Dictionary<NomeObjeto, PermissoesTabelaEsperadas>
            {
                [Nome("externa")] = new(false, false, false, false, false, false, false, false, false) { ColunasSemLeitura = ["linha"] },
            },
            PodeLimpar = true,
            ExcecoesDosResiduais = new Dictionary<NomeObjeto, bool?> { [Nome("mv_clientes")] = false },
            PodeAjustar = true,
            ExcecoesDasSequences = new Dictionary<NomeObjeto, bool?> { [Nome("contadores_sempre_seq")] = false, [Nome("seq_avulsa")] = false },
        }
        : new()
        {
            Banco = new(PodeMarcarBanco: false, PodeCriarEsquema: false),
            Tabelas = new(true, false, false, false, false, false, false, false, false),
            ExcecoesDasTabelas = ExcecoesDaOrigem(defeito),
            PodeLimpar = false,
            PodeAjustar = false,
        };

    // Com a exceção que a fixture declara para uma tabela que o banco não tem.
    private static Dictionary<NomeObjeto, PermissoesTabelaEsperadas> ExcecoesDaOrigem(Defeito defeito)
    {
        var excecoes = new Dictionary<NomeObjeto, PermissoesTabelaEsperadas>
        {
            [Nome("canonicos")] = new(false, false, false, false, false, false, false, false, false) { ColunasSemLeitura = ["valor", "dia"] },
            [Nome("segredos")] = new(true, false, false, false, false, false, false, true, true),
        };
        if (defeito == Defeito.ExcecaoDeAcessoInexistente)
        {
            excecoes[Nome("fantasma")] = new(true, false, false, false, false, false, false, false, false);
        }

        return excecoes;
    }

    /// <summary>As permissões de um lado sobre o esquema lido, uma por objeto e na ordem dele.</summary>
    internal static AcessoLido Acesso(LadoDoAcesso lado, IEnumerable<TabelaEsquema> tabelas, IEnumerable<ObjetoResidual> residuais, IEnumerable<SequenceEsquema> sequences) => new()
    {
        Banco = new() { PodeMarcarBanco = lado.Banco.PodeMarcarBanco, PodeCriarEsquema = lado.Banco.PodeCriarEsquema },
        Tabelas =
        [
            .. tabelas.Select(tabela => (tabela.Nome, Permissoes: lado.ExcecoesDasTabelas.GetValueOrDefault(tabela.Nome) ?? lado.Tabelas)).Select(par => new PermissoesTabela
            {
                Tabela = par.Nome,
                PodeLer = par.Permissoes.PodeLer,
                ColunasSemLeitura = par.Permissoes.ColunasSemLeitura,
                PodeInserir = par.Permissoes.PodeInserir,
                PodeGravarIdentidade = par.Permissoes.PodeGravarIdentidade,
                PodeEsvaziar = par.Permissoes.PodeEsvaziar,
                PodeReferenciar = par.Permissoes.PodeReferenciar,
                PodeAlterarRestricoes = par.Permissoes.PodeAlterarRestricoes,
                PodeDesligarTriggers = par.Permissoes.PodeDesligarTriggers,
                LeituraFiltradaPorLinha = par.Permissoes.LeituraFiltradaPorLinha,
                EscritaRestritaPorLinha = par.Permissoes.EscritaRestritaPorLinha,
            }),
        ],
        Residuais =
        [
            .. residuais.Select(residual => new PermissoesResidual
            {
                Nome = residual.Nome,
                Tipo = residual.Tipo,
                PodeLimpar = lado.ExcecoesDosResiduais.TryGetValue(residual.Nome, out var limpar) ? limpar : lado.PodeLimpar,
            }),
        ],
        Sequences =
        [
            .. sequences.Select(sequence => new PermissoesSequence
            {
                Nome = sequence.Nome,
                PodeAjustar = lado.ExcecoesDasSequences.TryGetValue(sequence.Nome, out var ajustar) ? ajustar : lado.PodeAjustar,
            }),
        ],
    };

    /// <summary>O transporte das duas conexões do falso: em claro, sem verificação, remoto.</summary>
    internal static TransporteConexao Transporte() => new() { Cifrado = false, ServidorVerificado = false, Local = false };

    /// <summary>O que a fixture declara sobre o banco.</summary>
    internal static EsquemaDeConformidade Esperado(bool completo, Defeito defeito) => new()
    {
        Inventario = defeito == Defeito.InventarioVazio ? Inventario(completo) with { Tabelas = [] } : Inventario(completo),
        Inexistentes = completo ? new Dictionary<Enum, string>() : Ausentes(defeito),
        Propriedades = completo
            ? Caso.Presente(PropriedadesEsperadas.De(Propriedades(completo))!)
            : Caso.Inexistente<PropriedadesEsperadas>("banco falso sem propriedades"),
        Sentinelas = defeito switch
        {
            Defeito.SemSentinelas => [],
            Defeito.SentinelaVazio => [Sentinela, ""],
            _ => [Sentinela, SentinelaCurto],
        },
        Particao = completo
            ? Caso.Presente(new Hierarquia(Nome("pedidos"), [Nome("pedidos_2025"), Nome("pedidos_2026")]))
            : Caso.Inexistente<Hierarquia>("banco falso sem partições"),
        Heranca = completo
            ? Caso.Presente(new Hierarquia(Nome("pessoa"), [Nome("funcionario")]))
            : Caso.Inexistente<Hierarquia>("banco falso sem herança"),
        SequenceCompartilhada = completo
            ? Caso.Presente(new SequenceEsperada(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")]))
            : Caso.Inexistente<SequenceEsperada>("banco falso sem sequence compartilhada"),
        ColunasGeradas = completo
            ? Caso.Presente<IReadOnlyList<ColunaGeradaEsperada>>([new(Coluna("calculos", "dobro"), GeracaoColuna.Armazenada)])
            : Caso.Inexistente<IReadOnlyList<ColunaGeradaEsperada>>("banco falso sem colunas geradas"),
        ChaveEstrangeiraComposta = new(Nome("itens"), "fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"]),
        Temporal = completo
            ? Caso.Presente(new TemporalEsperada(Nome("contratos"), Nome("contratos_historico")))
            : Caso.Inexistente<TemporalEsperada>("banco falso sem temporais"),
        SegurancaDeLinha = completo
            ? Caso.Presente<IReadOnlyList<NomeObjeto>>(defeito == Defeito.SegurancaDeLinhaVazia ? [] : [Nome("segredos")])
            : Caso.Inexistente<IReadOnlyList<NomeObjeto>>("banco falso sem segurança por linha"),
        Publicacao = completo
            ? Caso.Presente<IReadOnlyList<NomeObjeto>>(defeito == Defeito.PublicacaoVazia ? [] : [Nome("pedidos_2026")])
            : Caso.Inexistente<IReadOnlyList<NomeObjeto>>("banco falso que não informa a publicação"),
        Assinatura = completo
            ? Caso.Presente<IReadOnlyList<NomeObjeto>>(defeito == Defeito.AssinaturaVazia ? [] : [Nome("contatos")])
            : Caso.Inexistente<IReadOnlyList<NomeObjeto>>("banco falso que não informa a assinatura"),
        Mascara = completo ? Caso.Presente(Coluna("contatos", "email")) : Caso.Inexistente<ReferenciaColuna>("banco falso sem máscara"),

        // Um caso sem declaração: o 'required' não impede null!.
        NomesExatos = defeito == Defeito.CasoSemDeclaracao ? null! : [Nome("Cliente"), Nome("cliente"), Nome("select"), Nome("nota fiscal")],
        Citacoes = [new("Cliente", "\"Cliente\""), new("a\"b", "\"a\"\"b\"")],
        ComparacoesDeNome = ComparacoesDeNome(),
        ObjetoDoIncognito = defeito == Defeito.IncognitoForaDoEsquema ? Nome("execucao") : new NomeObjeto("incognito", "execucao"),
        Acesso = completo
            ? Caso.Presente(new AcessoEsperado(Lado(destino: false, defeito), Lado(destino: true)))
            : Caso.Inexistente<AcessoEsperado>("banco falso sem permissões"),
        Transporte = completo
            ? Caso.Presente(TransporteEsperado.De(Transporte())!)
            : Caso.Inexistente<TransporteEsperado>("banco falso sem transporte"),
    };

    // Como num PostgreSQL UTF-8: sem aspas, só A-Z dobra; entre aspas, nada; mais de 63 bytes não corresponde.
    internal static IReadOnlyList<NomeComparado> ComparacoesDeNome() =>
    [
        new("Cliente", false, "cliente", CorrespondenciaNome.Corresponde),
        new("Cliente", false, "Cliente", CorrespondenciaNome.NaoCorresponde),
        new("Cliente", true, "Cliente", CorrespondenciaNome.Corresponde),
        new("Cliente", true, "cliente", CorrespondenciaNome.NaoCorresponde),
        new("AÇÃO", false, "aÇÃo", CorrespondenciaNome.Corresponde),
        new("AÇÃO", false, "ação", CorrespondenciaNome.NaoCorresponde),
        new(new string('a', 63), true, new string('a', 63), CorrespondenciaNome.Corresponde),
        new(new string('a', 64), true, new string('a', 64), CorrespondenciaNome.NaoCorresponde),
        new(new string('a', 64), false, new string('a', 64), CorrespondenciaNome.NaoCorresponde),
        new(new string('ç', 31) + "a", true, new string('ç', 31) + "a", CorrespondenciaNome.Corresponde),
        new(new string('ç', 32), true, new string('ç', 32), CorrespondenciaNome.NaoCorresponde),
    ];

    // Os valores da matriz que o banco sem recursos não tem; com a matriz descoberta, falta um motivo.
    private static Dictionary<Enum, string> Ausentes(Defeito defeito)
    {
        var ausentes = new Dictionary<Enum, string>
        {
            [SituacaoTipo.NaoSuportado] = "banco falso sem foreign table",
            [TipoColuna.Decimal] = "banco falso sem decimal",
            [TipoColuna.Data] = "banco falso sem data",
            [TipoColuna.DataHora] = "banco falso sem data e hora",
            [TipoColuna.DataHoraComFuso] = "banco falso sem data e hora com fuso",
            [TipoColuna.Binario] = "banco falso sem binário",
            [TipoColuna.Booleano] = "banco falso sem booleano",
            [TipoColuna.Uuid] = "banco falso sem uuid",
            [UnidadeComprimento.UnidadesUtf16] = "banco falso sem texto em UTF-16",
            [UnidadeComprimento.Bytes] = "banco falso sem binário",
        };
        if (defeito == Defeito.MatrizDescoberta)
        {
            ausentes.Remove(TipoColuna.Uuid);
        }
        else if (defeito == Defeito.MatrizSemMotivo)
        {
            ausentes[TipoColuna.Uuid] = " ";
        }
        else if (defeito == Defeito.MatrizContraditoria)
        {
            ausentes[SituacaoTipo.Suportado] = "declarado inexistente por engano";
        }

        return ausentes;
    }

    /// <summary>
    /// Uma cópia do objeto do modelo com as propriedades trocadas: copia toda propriedade <c>init</c>, para que um campo
    /// novo do modelo nunca suma em silêncio de um defeito.
    /// </summary>
    internal static T Clonar<T>(T objeto, params (string Propriedade, object? Valor)[] trocas)
        where T : class
    {
        var copia = Activator.CreateInstance(objeto.GetType())!;
        foreach (var propriedade in objeto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(propriedade => propriedade.SetMethod is { IsPublic: true }))
        {
            var troca = trocas.Where(troca => troca.Propriedade == propriedade.Name).ToArray();
            propriedade.SetValue(copia, troca.Length == 1 ? troca[0].Valor : propriedade.GetValue(objeto));
        }

        if (trocas.Select(troca => troca.Propriedade).Except(objeto.GetType().GetProperties().Select(propriedade => propriedade.Name)).FirstOrDefault() is { } inexistente)
        {
            throw new InvalidOperationException($"{objeto.GetType().Name} não tem a propriedade {inexistente}.");
        }

        return (T)copia;
    }

    // O inventário do falso é a projeção do banco certo (no dialeto de verdade, ele é escrito à mão a partir do DDL).
    private static Inventario Inventario(bool completo) => new(
        [
            .. Tabelas(completo).Where(tabela => tabela.Nome.Esquema != "incognito").Select(tabela => new TabelaInventariada
            {
                Nome = tabela.Nome,
                Tipo = tabela.Tipo,
                Pai = tabela.Pai,
                OutrosPais = tabela.OutrosPais,
                Historico = tabela.Historico,
                ChaveParticao = tabela.ChaveParticao is { } chave ? new ChaveParticaoEsperada(chave.Colunas, chave.ColunasIncompletas) : null,
                Estrangeira = tabela.Estrangeira,
                Extensao = tabela.Extensao,
                SegurancaDeLinha = tabela.SegurancaDeLinha,
                Publicada = tabela.Publicada,
                Assinada = tabela.Assinada,
                Colunas = [.. tabela.Colunas.Select(Projetar)],
                ChavePrimaria = tabela.ChavePrimaria,
                ChavesEstrangeiras =
                [
                    .. tabela.ChavesEstrangeiras.Select(chave => new ChaveEstrangeiraInventariada(chave.Nome, chave.Colunas, chave.Referenciada, chave.ColunasReferenciadas, chave.Imposta, chave.Validada)),
                ],
                IndicesUnicos = [.. tabela.IndicesUnicos.Select(IndiceInventariado.De)],
                Checks = [.. tabela.RestricoesCheck.Select(CheckInventariado.De)],
                Exclusoes = [.. tabela.RestricoesExclusao.Select(ExclusaoInventariada.De)],
                Triggers = [.. tabela.Triggers.Select(TriggerInventariada.De)],
            }),
        ],
        [.. Sequences(completo).Where(sequence => sequence.Nome.Esquema != "incognito").Select(sequence => new SequenceEsperada(sequence.Nome, sequence.Colunas))],
        [
            .. Residuais(completo).Select(residual =>
                new ResidualInventariado(residual.Nome, residual.Tipo, residual.DependenciasIncompletas, residual.Populada) { DependeDe = residual.DependeDe }),
        ],
        Reservado(completo));

    private static ColunaInventariada Projetar(ColunaEsquema coluna) => new()
    {
        Nome = coluna.Nome,
        TipoNativo = coluna.TipoNativo,
        Situacao = coluna.Situacao,
        Tipo = coluna.Tipo,
        Anulavel = coluna.Anulavel,
        Identidade = coluna.Identidade,
        Geracao = coluna.Geracao,
        Mascarada = coluna.Mascarada,
        TipoBase = coluna.TipoBase,
        Colacao = ColacaoEsperada.De(coluna.Colacao),
        IgnoraCaixaPeloTipo = coluna.IgnoraCaixaPeloTipo,
        IgnoraEspacoFinalPeloTipo = coluna.IgnoraEspacoFinalPeloTipo,
        Comprimento = LimiteEsperado.De(coluna.Comprimento),
        Precisao = coluna.Precisao,
        Escala = coluna.Escala,
        Faixa = FaixaEsperada.De(coluna.Faixa),
        ReferenciasDaGeracao = ReferenciasEsperadas.De(coluna.ReferenciasDaGeracao),
        RestritaPeloTipo = coluna.RestritaPeloTipo,
    };

    private static TabelaEsquema Tabela(string nome, params ColunaEsquema[] colunas) => new() { Nome = Nome(nome), Colunas = colunas };

    private static LimiteComprimento Limite(int? valor, UnidadeComprimento unidade) => new() { Valor = valor, Unidade = unidade };

    private static ReferenciasDaExpressao Referencias(params string[] colunas) => new() { Colunas = colunas, Incompletas = false };

    private static ElementoIndice Elemento(string? coluna, ColacaoEsquema? colacao = null) => new() { Coluna = coluna, Colacao = colacao };

    private static IndiceUnico Indice(string nome, bool primario, params ElementoIndice[] elementos) =>
        new() { Nome = nome, Primario = primario, Elementos = elementos, NulosDistintos = true };

    // A semântica que o leitor de verdade devolve em toda coluna de texto: colação, as duas marcas do tipo e o limite.
    private static ColunaEsquema Texto(string nome) => new()
    {
        Nome = nome,
        TipoNativo = "text",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Texto,
        Anulavel = true,
        Colacao = Padrao,
        IgnoraCaixaPeloTipo = false,
        IgnoraEspacoFinalPeloTipo = false,
        Comprimento = Limite(null, UnidadeComprimento.PontosDeCodigo),
        RestritaPeloTipo = false,
    };

    private static ColunaEsquema Inteiro(string nome, bool anulavel = true) => new()
    {
        Nome = nome,
        TipoNativo = "bigint",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Inteiro,
        Anulavel = anulavel,
        Faixa = new FaixaInteira { Minimo = long.MinValue, Maximo = long.MaxValue },
        RestritaPeloTipo = false,
    };

    private static ColunaEsquema Comum(string nome, string tipoNativo, SituacaoTipo situacao) =>
        new() { Nome = nome, TipoNativo = tipoNativo, Situacao = situacao, Anulavel = true, RestritaPeloTipo = false };
}

/// <summary>O dialeto falso: lê o banco em memória e aplica o defeito pedido e a perturbação, se há.</summary>
// A perturbação recebe o esquema lido e se é o destino.
internal sealed class ProvedorFalso(bool completo, Defeito defeito, Func<EsquemaBanco, bool, EsquemaBanco>? perturbar, bool privilegiado = false) : ProvedorDialeto
{
    /// <summary>O começo da string de conexão que o falso recusa.</summary>
    internal const string Inaceitavel = "inaceitavel;";

    public override string Nome => "falso";

    public override Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Recusar<DialetoOrigem>(conexao) ?? Task.FromResult<DialetoOrigem>(new OrigemFalsa(conexao.Revelar(), new LeitorFalso(completo, defeito, perturbar, destino: false, privilegiado), completo, defeito, privilegiado));

    public override Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Recusar<DialetoDestino>(conexao) ?? Task.FromResult<DialetoDestino>(new DestinoFalso(conexao.Revelar(), new LeitorFalso(completo, defeito, perturbar, destino: true, privilegiado), completo, defeito, privilegiado));

    // A string inaceitável é recusada pelo tipo do contrato, com os defeitos da recusa.
    private Task<T>? Recusar<T>(SegredoConexao conexao)
    {
        var texto = conexao.Revelar();
        if (!texto.StartsWith(Inaceitavel, StringComparison.Ordinal) || defeito == Defeito.ConexaoInaceitavelAceita)
        {
            return null;
        }

        Exception recusa = defeito switch
        {
            Defeito.RecusaComoExcecaoComum => new IncognitoException("INC3399", "A conexão do falso foi recusada."),
            Defeito.RecusaComAString => new DialetoRecusouException("INC3399", "A conexão do falso foi recusada: " + texto),
            Defeito.RecusaForaDaFaixa => new DialetoRecusouException("INC2001", "A conexão do falso foi recusada."),
            _ => new DialetoRecusouException("INC3399", "A conexão do falso foi recusada."),
        };
        return Task.FromException<T>(recusa);
    }
}

internal sealed class OrigemFalsa(string banco, LeitorEsquema leitor, bool completo, Defeito defeito, bool privilegiado = false) : DialetoOrigem
{
    public override LeitorEsquema Esquema { get; } = leitor;

    // Só no par comum: o sentinela, por extenso, na identidade da origem.
    public override IdentidadeBanco Identidade { get; } =
        new IdentidadeFalsa(banco, completo, defeito, vaza: defeito == Defeito.SentinelaNaIdentidadeDaOrigem && !privilegiado, origem: true);

    public override DialetoSql Sql { get; } = new SqlFalso(defeito is Defeito.CitacaoSemEscape, defeito);
}

// O destino da suíte tem o mesmo esquema da origem, lido pelo dono.
internal sealed class DestinoFalso(string banco, LeitorEsquema leitor, bool completo, Defeito defeito, bool privilegiado = false) : DialetoDestino
{
    public override LeitorEsquema Esquema { get; } = leitor;

    public override IdentidadeBanco Identidade { get; } = new IdentidadeFalsa(banco, completo, defeito, vaza: false, origem: false);

    public override DialetoSql Sql { get; } = new SqlFalso(defeito is Defeito.CitacaoSemEscape or Defeito.CitacaoErradaNoDestino, defeito);

    // Só no par comum: o primeiro sentinela, em hexadecimal minúsculo, no texto do destino.
    public override string ToString() => defeito == Defeito.SentinelaNoTextoDoDestino && !privilegiado
        ? "destino " + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(BancoFalso.Sentinela)).ToLowerInvariant()
        : base.ToString()!;
}

internal sealed class LeitorFalso(bool completo, Defeito defeito, Func<EsquemaBanco, bool, EsquemaBanco>? perturbar, bool destino, bool privilegiado) : LeitorEsquema
{
    private int _leituras;

    public override Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        _leituras++;
        var comRecursos = completo || defeito == Defeito.InventaRecursos;
        var tabelas = BancoFalso.Tabelas(comRecursos);
        var sequences = BancoFalso.Sequences(comRecursos);
        var residuais = BancoFalso.Residuais(comRecursos);
        var propriedades = BancoFalso.Propriedades(comRecursos);

        if (defeito != Defeito.EsquemaDoIncognitoLido)
        {
            tabelas.RemoveAll(tabela => tabela.Nome.Esquema == "incognito");
            sequences.RemoveAll(sequence => sequence.Nome.Esquema == "incognito");
        }

        Aplicar(tabelas, sequences, residuais, ref propriedades);

        // Em ordem de esquema e nome, como o contrato pede.
        var ordem = Comparer<TabelaEsquema>.Create((uma, outra) => Comparar(uma.Nome, outra.Nome));
        tabelas.Sort(ordem);
        sequences.Sort((uma, outra) => Comparar(uma.Nome, outra.Nome));
        residuais.Sort((um, outro) => Comparar(um.Nome, outro.Nome));
        if (defeito == Defeito.TabelasForaDeOrdem || (defeito == Defeito.LeituraInstavel && _leituras > 1))
        {
            tabelas.Reverse();
        }

        var esquema = new EsquemaBanco
        {
            Tabelas = tabelas,
            Sequences = sequences,
            ObjetosResiduais = residuais,
            ObjetosNoEsquemaReservado = defeito switch
            {
                Defeito.EscondeEsquemaReservado => [],
                Defeito.NaoInformaEsquemaReservado => null,
                Defeito.ReservadoComOutroEsquema => [.. BancoFalso.Reservado(comRecursos), BancoFalso.Nome("intrusa")],
                _ => BancoFalso.Reservado(comRecursos),
            },
            Propriedades = propriedades,
            Acesso = Acesso(comRecursos, tabelas, residuais, sequences),
        };
        return Task.FromResult(perturbar is null ? esquema : perturbar(esquema, destino));
    }

    // As permissões do lado sobre o esquema lido, com os defeitos delas (só na origem, fora o do destino).
    private AcessoLido? Acesso(bool comRecursos, List<TabelaEsquema> tabelas, List<ObjetoResidual> residuais, List<SequenceEsquema> sequences)
    {
        if (!comRecursos || (defeito == Defeito.AcessoAusente && !destino))
        {
            return null;
        }

        var acesso = BancoFalso.Acesso(BancoFalso.Lado(destino && defeito != Defeito.AcessoDoLeitorNoDestino), tabelas, residuais, sequences);
        if (destino)
        {
            return acesso;
        }

        PermissoesTabela Canonicos(params (string Propriedade, object? Valor)[] trocas) =>
            BancoFalso.Clonar(acesso.Tabelas.Single(permissoes => permissoes.Tabela.Equals(BancoFalso.Nome("canonicos"))), trocas);
        var trocada = defeito switch
        {
            Defeito.PodeLerComColunaSemLeitura => Canonicos((nameof(PermissoesTabela.PodeLer), true)),
            Defeito.ColunaSemLeituraInexistente => Canonicos((nameof(PermissoesTabela.ColunasSemLeitura), (string[])["valor", "fantasma"])),
            Defeito.ColunasSemLeituraForaDeOrdem => Canonicos((nameof(PermissoesTabela.ColunasSemLeitura), (string[])["dia", "valor"])),
            _ => null,
        };
        return defeito switch
        {
            _ when trocada is not null => BancoFalso.Clonar(acesso, (nameof(AcessoLido.Tabelas), acesso.Tabelas.Select(permissoes => permissoes.Tabela.Equals(trocada.Tabela) ? trocada : permissoes).ToArray())),
            Defeito.PermissaoDeTabelaInexistente => BancoFalso.Clonar(acesso, (nameof(AcessoLido.Tabelas), acesso.Tabelas.Append(new PermissoesTabela { Tabela = BancoFalso.Nome("fantasma") }).ToArray())),
            Defeito.PermissaoDeTabelaRepetida => BancoFalso.Clonar(acesso, (nameof(AcessoLido.Tabelas), acesso.Tabelas.Append(Canonicos()).ToArray())),
            _ => acesso,
        };
    }

    private void Aplicar(List<TabelaEsquema> tabelas, List<SequenceEsquema> sequences, List<ObjetoResidual> residuais, ref PropriedadesBanco? propriedades)
    {
        switch (defeito)
        {
            case Defeito.TipoMalClassificado:
                TrocarColuna(tabelas, "tipos", "dados", (nameof(ColunaEsquema.Situacao), SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Texto));
                break;
            case Defeito.TipoCanonicoForaDoSuportado:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.Tipo), TipoColuna.Inteiro));
                break;
            case Defeito.ParticaoSemPai:
                Trocar(tabelas, "pedidos_2026", (nameof(TabelaEsquema.Pai), null));
                break;
            case Defeito.HerancaSemMae:
                Trocar(tabelas, "funcionario", (nameof(TabelaEsquema.Pai), null));
                break;
            case Defeito.SequenceComUmaColuna:
                TrocarSequence(sequences, "seq_numero", s => BancoFalso.Clonar(s, (nameof(SequenceEsquema.Colunas), new[] { s.Colunas[0] })));
                break;
            case Defeito.SequenceComColunaRepetida:
                TrocarSequence(sequences, "seq_numero", s => BancoFalso.Clonar(s, (nameof(SequenceEsquema.Colunas), s.Colunas.Append(s.Colunas[0]).ToArray())));
                break;
            case Defeito.GeradaNaoMarcada:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.Geracao), GeracaoColuna.Nenhuma));
                break;
            case Defeito.GeradaNaCopia:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.Situacao), SituacaoTipo.Suportado), (nameof(ColunaEsquema.Tipo), TipoColuna.Inteiro));
                break;
            case Defeito.ChaveForaDeOrdem:
                TrocarChave(tabelas, f => [BancoFalso.Clonar(f, (nameof(ChaveEstrangeira.Colunas), f.Colunas.Reverse().ToArray()), (nameof(ChaveEstrangeira.ColunasReferenciadas), f.ColunasReferenciadas.Reverse().ToArray()))]);
                break;
            case Defeito.ChaveDesbalanceada:
                TrocarChave(tabelas, f => [BancoFalso.Clonar(f, (nameof(ChaveEstrangeira.ColunasReferenciadas), new[] { f.ColunasReferenciadas[0] }))]);
                break;
            case Defeito.TabelaDuplicada:
                tabelas.Add(tabelas.Single(tabela => tabela.Nome.Equals(BancoFalso.Nome("itens"))));
                break;
            case Defeito.TemporalSemHistorico:
                Trocar(tabelas, "contratos", (nameof(TabelaEsquema.Historico), null));
                break;
            case Defeito.ResiduoAusente:
                residuais.Clear();
                break;
            case Defeito.SegurancaDeLinhaAusente:
                Trocar(tabelas, "segredos", (nameof(TabelaEsquema.SegurancaDeLinha), false));
                break;
            case Defeito.PublicacaoAusente:
                Trocar(tabelas, "pedidos_2026", (nameof(TabelaEsquema.Publicada), false));
                break;
            case Defeito.PublicacaoNaoInformada:
                Trocar(tabelas, "itens", (nameof(TabelaEsquema.Publicada), null));
                break;
            case Defeito.PublicacaoNaRaiz:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.Publicada), true));
                break;
            case Defeito.AssinaturaAusente:
                Trocar(tabelas, "contatos", (nameof(TabelaEsquema.Assinada), false));
                break;
            case Defeito.AssinaturaNaoInformada:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.Assinada), null));
                break;
            case Defeito.MascaraAusente:
                TrocarColuna(tabelas, "contatos", "email", (nameof(ColunaEsquema.Mascarada), false));
                break;
            case Defeito.NomeNaCaixaErrada:
                Trocar(tabelas, "Cliente", (nameof(TabelaEsquema.Nome), BancoFalso.Nome("CLIENTE")));
                break;
            case Defeito.TabelaOmitida:
            case Defeito.TabelaOmitidaNoDestino when destino:
                tabelas.RemoveAll(tabela => tabela.Nome.Equals(BancoFalso.Nome("da_extensao")));
                break;
            case Defeito.ColunaAMais:
                Trocar(tabelas, "endereco", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(BancoFalso.Clonar(tabela.Colunas[0], (nameof(ColunaEsquema.Nome), "bairro"))).ToArray())));
                break;
            case Defeito.ColunaAMenos:
                Trocar(tabelas, "ficha", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[] { tabela.Colunas[0] })));
                break;
            case Defeito.ColunasForaDeOrdem:
                Trocar(tabelas, "ficha", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Reverse().ToArray())));
                break;
            case Defeito.PropriedadeDeColunaTrocada:
                TrocarColuna(tabelas, "contato", "canal", (nameof(ColunaEsquema.Anulavel), false));
                break;
            case Defeito.ChaveEstrangeiraRepetida:
                TrocarChave(tabelas, f => [f, f]);
                break;
            case Defeito.ChaveEstrangeiraComReferenciadaErrada:
                TrocarChave(tabelas, f => [BancoFalso.Clonar(f, (nameof(ChaveEstrangeira.Referenciada), BancoFalso.Nome("ficha")))], "ficha");
                break;
            case Defeito.ChavePrimariaForaDeOrdem:
                Trocar(tabelas, "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavePrimaria), tabela.ChavePrimaria.Reverse().ToArray())));
                break;
            case Defeito.TipoDeTabelaTrocado:
                Trocar(tabelas, "contadores", (nameof(TabelaEsquema.Tipo), TipoTabela.Herdeira));
                break;
            case Defeito.SituacaoTrocadaNoDestino when destino:
                TrocarColuna(tabelas, "tipos", "dados", (nameof(ColunaEsquema.Situacao), SituacaoTipo.NaoSuportado));
                break;
            case Defeito.SituacaoTrocadaForaDaMatriz:
                TrocarColuna(tabelas, "canonicos", "dia", (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva), (nameof(ColunaEsquema.Tipo), null));
                break;
            case Defeito.TipoCanonicoTrocadoForaDaMatriz:
                TrocarColuna(tabelas, "canonicos", "momento", (nameof(ColunaEsquema.Tipo), TipoColuna.DataHoraComFuso));
                break;
            case Defeito.SequenceOmitida:
                sequences.RemoveAll(sequence => sequence.Nome.Equals(BancoFalso.Nome("seq_avulsa")));
                break;
            case Defeito.OutroPaiForaDoEsquema:
                Trocar(tabelas, "ficha", (nameof(TabelaEsquema.OutrosPais), new[] { BancoFalso.Nome("endereco_antigo") }));
                break;
            case Defeito.ChaveDeParticaoAusente:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.ChaveParticao), null));
                break;
            case Defeito.ChaveDeParticaoComColunaInexistente:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = ["filial"], ColunasIncompletas = false }));
                break;
            case Defeito.ChaveDeParticaoVaziaCompleta:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = [], ColunasIncompletas = false }));
                break;
            case Defeito.MaeDeTipoErrado:
                Trocar(tabelas, "ficha", (nameof(TabelaEsquema.OutrosPais), new[] { BancoFalso.Nome("endereco"), BancoFalso.Nome("pedidos_2025") }));
                break;
            case Defeito.ColacaoEmColunaInteira:
                TrocarColuna(tabelas, "canonicos", "pequeno", (nameof(ColunaEsquema.Colacao), BancoFalso.Padrao));
                break;
            case Defeito.TextoSemColacao:
                TrocarColuna(tabelas, "endereco", "cidade", (nameof(ColunaEsquema.Colacao), null));
                break;
            case Defeito.InteiroSemFaixa:
                TrocarColuna(tabelas, "itens", "numero", (nameof(ColunaEsquema.Faixa), null));
                break;
            case Defeito.FaixaInvertida:
                TrocarColuna(tabelas, "canonicos", "pequeno", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 5, Maximo = 3 }));
                break;
            case Defeito.PrecisaoForaDeDecimal:
                TrocarColuna(tabelas, "canonicos", "dia", (nameof(ColunaEsquema.Precisao), 5));
                break;
            case Defeito.LimiteEmPontosSemCodificacao:
                propriedades = BancoFalso.Clonar(propriedades!, (nameof(PropriedadesBanco.Codificacao), BancoFalso.Clonar(propriedades!.Codificacao, (nameof(CodificacaoTexto.Definida), false))));
                break;
            case Defeito.GeradaSemReferencias:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.ReferenciasDaGeracao), null));
                break;
            case Defeito.GeracaoCitaAPropria:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["base", "dobro"], Incompletas = false }));
                break;
            case Defeito.DeterministicaQueIgnoraCaixa:
                Trocar(tabelas, "canonicos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas
                    .Select(c => c.Nome == "curto" ? BancoFalso.Clonar(c, (nameof(ColunaEsquema.Colacao), BancoFalso.Clonar(c.Colacao!, (nameof(ColacaoEsquema.Deterministica), true)))) : c)
                    .ToArray())));
                break;
            case Defeito.ComprimentoForaDoTipo:
                TrocarColuna(tabelas, "canonicos", "dia", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 10, Unidade = UnidadeComprimento.PontosDeCodigo }));
                break;
            case Defeito.FaixaForaDoTipo:
                TrocarColuna(tabelas, "canonicos", "valor", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 0, Maximo = 9 }));
                break;
            case Defeito.ReferenciasSemGeracao:
                TrocarColuna(tabelas, "calculos", "base", (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["extra"], Incompletas = false }));
                break;
            case Defeito.PropriedadesAusentes:
            case Defeito.PropriedadesAusentesNoDestino when destino:
                propriedades = null;
                break;
            case Defeito.MascaraTrocadaNoDestino when destino:
                TrocarColuna(tabelas, "contatos", "email", (nameof(ColunaEsquema.Mascarada), false));
                break;
            case Defeito.FaixaSemPositivos:
                TrocarColuna(tabelas, "canonicos", "pequeno", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = -10, Maximo = -5 }));
                break;
            case Defeito.DeterministicaComMarcaNula:
                TrocarColuna(tabelas, "endereco", "cidade", (nameof(ColunaEsquema.Colacao), BancoFalso.Clonar(BancoFalso.Padrao, (nameof(ColacaoEsquema.IgnoraCaixa), null))));
                break;
            case Defeito.ColacaoPadraoComMarcaNula:
                propriedades = BancoFalso.Clonar(propriedades!, (nameof(PropriedadesBanco.Colacao), BancoFalso.Clonar(BancoFalso.Padrao, (nameof(ColacaoEsquema.IgnoraAcento), null))));
                break;
            case Defeito.GeracaoCitaColunaInexistente:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["base", "fantasma"], Incompletas = false }));
                break;
            case Defeito.ReferenciasForaDeOrdem:
                TrocarColuna(tabelas, "calculos", "dobro", (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["extra", "base"], Incompletas = false }));
                break;
            case Defeito.IndiceUnicoOmitido:
                Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), tabela.IndicesUnicos.Skip(1).ToArray())));
                break;
            case Defeito.IndiceAnexadoDaParticaoListado:
                Trocar(tabelas, "pedidos_2025", (nameof(TabelaEsquema.IndicesUnicos), new[] { BancoFalso.Clonar(Objeto(tabelas, "pedidos").IndicesUnicos[0], (nameof(IndiceUnico.Nome), "pedidos_2025_pkey")) }));
                break;
            case Defeito.ExclusaoComoIndiceUnico:
                TrocarIndice(tabelas, indices => [.. indices, new() { Nome = "ex_indexada", Primario = false, Elementos = [new() { Coluna = "ini" }, new() { Coluna = "fim" }], NulosDistintos = true }]);
                break;
            case Defeito.TriggerInternaListada:
                Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Triggers), tabela.Triggers.Prepend(new() { Nome = "RI_ConstraintTrigger_c_1", Ligada = true, Herdada = false }).ToArray())));
                break;
            case Defeito.CheckHerdadoNaParticao:
                Trocar(tabelas, "pedidos_2025", (nameof(TabelaEsquema.RestricoesCheck), Objeto(tabelas, "pedidos").RestricoesCheck.ToArray()));
                break;
            case Defeito.CheckInventado:
                Trocar(tabelas, "endereco", (nameof(TabelaEsquema.RestricoesCheck), new RestricaoCheck[] { new() { Nome = "ck_endereco_cidade", Referencias = new() { Colunas = ["cidade"], Incompletas = false }, Imposta = true } }));
                break;
            case Defeito.FkComRiDesligadaComoImposta:
                TrocarChave(tabelas, f => [BancoFalso.Clonar(f, (nameof(ChaveEstrangeira.Imposta), true))], "ficha");
                break;
            case Defeito.ElementoDeExpressaoSemReferencias:
                TrocarIndice(tabelas, indices => [indices[0], BancoFalso.Clonar(indices[1], (nameof(IndiceUnico.ReferenciasDasExpressoes), null)), indices[2]]);
                break;
            case Defeito.PredicadoCitaColunaInexistente:
                TrocarIndice(tabelas, indices => [indices[0], BancoFalso.Clonar(indices[1], (nameof(IndiceUnico.Predicado), new ReferenciasDaExpressao { Colunas = ["id", "fantasma"], Incompletas = false })), indices[2]]);
                break;
            case Defeito.IndicePrimarioDivergenteDaChave:
                TrocarIndice(tabelas, indices => [indices[0], indices[1], BancoFalso.Clonar(indices[2], (nameof(IndiceUnico.Elementos), new ElementoIndice[] { new() { Coluna = "ini" } }))]);
                break;
            case Defeito.DoisIndicesPrimarios:
                TrocarIndice(tabelas, indices => [indices[0], BancoFalso.Clonar(indices[1], (nameof(IndiceUnico.Primario), true)), indices[2]]);
                break;
            case Defeito.ElementoTextoSemColacao:
                TrocarIndice(tabelas, indices => [indices[0], BancoFalso.Clonar(indices[1], (nameof(IndiceUnico.Elementos), indices[1].Elementos.Select((e, i) => i == 0 ? BancoFalso.Clonar(e, (nameof(ElementoIndice.Colacao), null)) : e).ToArray())), indices[2]]);
                break;
            case Defeito.ColacaoEmElementoNaoTexto:
                TrocarIndice(tabelas, indices => [indices[0], indices[1], BancoFalso.Clonar(indices[2], (nameof(IndiceUnico.Elementos), new ElementoIndice[] { new() { Coluna = "id", Colacao = BancoFalso.Padrao } }))]);
                break;
            case Defeito.PrimariaSemIndiceForaDeParticao:
                Trocar(tabelas, "pedidos", (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>()));
                break;
            case Defeito.TriggerHerdadaForaDeParticao:
                Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Triggers), tabela.Triggers.Select(t => t.Nome == "tg_ligada" ? BancoFalso.Clonar(t, (nameof(TriggerEsquema.Herdada), true)) : t).ToArray())));
                break;
            case Defeito.ElementoDeColunaInexistente:
                TrocarIndice(tabelas, indices => [indices[0], indices[1], BancoFalso.Clonar(indices[2], (nameof(IndiceUnico.Elementos), new ElementoIndice[] { new() { Coluna = "fantasma" } }))]);
                break;
            case Defeito.ExpressoesSemElementoDeExpressao:
                TrocarIndice(tabelas, indices => [indices[0], indices[1], BancoFalso.Clonar(indices[2], (nameof(IndiceUnico.ReferenciasDasExpressoes), new ReferenciasDaExpressao { Colunas = ["id"], Incompletas = false }))]);
                break;
            case Defeito.ReferenciasDoIndiceForaDeOrdem:
                TrocarIndice(tabelas, indices => [indices[0], BancoFalso.Clonar(indices[1], (nameof(IndiceUnico.ReferenciasDasExpressoes), new ReferenciasDaExpressao { Colunas = ["ini", "email"], Incompletas = false })), indices[2]]);
                break;
            case Defeito.CheckCitaColunaInexistente:
                Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.RestricoesCheck), tabela.RestricoesCheck
                    .Select(c => c.Nome == "ck_indexada_faixa" ? BancoFalso.Clonar(c, (nameof(RestricaoCheck.Referencias), new ReferenciasDaExpressao { Colunas = ["ini", "fantasma"], Incompletas = false })) : c).ToArray())));
                break;
            case Defeito.ExclusaoForaDeOrdem:
                Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.RestricoesExclusao), tabela.RestricoesExclusao
                    .Select(e => e.Nome == "ex_indexada" ? BancoFalso.Clonar(e, (nameof(RestricaoExclusao.Referencias), new ReferenciasDaExpressao { Colunas = ["fim", "ini"], Incompletas = false })) : e).ToArray())));
                break;
            case Defeito.DependenciasForaDeOrdem:
                TrocarResidual(residuais, "resumo_vendas", r => BancoFalso.Clonar(r, (nameof(ObjetoResidual.DependeDe), r.DependeDe.Reverse().ToArray())));
                break;
            case Defeito.ColacaoDoElementoIncoerente:
                TrocarIndice(tabelas, indices => [BancoFalso.Clonar(indices[0], (nameof(IndiceUnico.Elementos), new ElementoIndice[] { new() { Coluna = "codigo", Colacao = BancoFalso.Clonar(BancoFalso.Padrao, (nameof(ColacaoEsquema.IgnoraCaixa), true)) } })), indices[1], indices[2]]);
                break;
            case Defeito.ExclusaoQueNaoCobreAPrimaria:
                Trocar(tabelas, "periodos", (nameof(TabelaEsquema.RestricoesExclusao), new RestricaoExclusao[] { new() { Nome = "pk_periodos", Referencias = new() { Colunas = ["sala"], Incompletas = false } } }));
                break;
            case Defeito.DependenciaDeObjetosGrandes:
                TrocarResidual(residuais, "resumo_vendas", r => BancoFalso.Clonar(r, (nameof(ObjetoResidual.DependeDe), r.DependeDe.Append(new NomeObjeto("pg_catalog", "pg_largeobject")).ToArray())));
                break;
            case Defeito.ObjetosGrandesComMarcas:
                residuais[residuais.FindIndex(residual => residual.Tipo == TipoObjetoResidual.ObjetosGrandes)] =
                    BancoFalso.Clonar(residuais.Single(residual => residual.Tipo == TipoObjetoResidual.ObjetosGrandes), (nameof(ObjetoResidual.Populada), true));
                break;
            case Defeito.DependenciaCircular:
                TrocarResidual(residuais, "mv_base", r => BancoFalso.Clonar(r, (nameof(ObjetoResidual.DependeDe), new[] { BancoFalso.Nome("resumo_vendas") })));
                break;
            case Defeito.DependenciaDeResidualInexistente:
                TrocarResidual(residuais, "resumo_vendas", r => BancoFalso.Clonar(r, (nameof(ObjetoResidual.DependeDe), r.DependeDe.Append(BancoFalso.Nome("mv_sumida")).ToArray())));
                break;

            // Só o administrador vê: o sentinela, em bytes decimais, num nome que o modelo devolve.
            case Defeito.SentinelaNumNome when privilegiado:
                TrocarColuna(tabelas, "tipos", "texto", (nameof(ColunaEsquema.TipoNativo), "text " + string.Join(" ", System.Text.Encoding.UTF8.GetBytes(BancoFalso.Sentinela))));
                break;
        }
    }

    private static int Comparar(NomeObjeto um, NomeObjeto outro)
    {
        var esquema = string.CompareOrdinal(um.Esquema, outro.Esquema);
        return esquema != 0 ? esquema : string.CompareOrdinal(um.Nome, outro.Nome);
    }

    private static void Trocar(List<TabelaEsquema> tabelas, string nome, Func<TabelaEsquema, TabelaEsquema> trocar)
    {
        var indice = tabelas.FindIndex(tabela => tabela.Nome.Equals(BancoFalso.Nome(nome)));
        tabelas[indice] = trocar(tabelas[indice]);
    }

    private static void Trocar(List<TabelaEsquema> tabelas, string nome, params (string Propriedade, object? Valor)[] trocas) =>
        Trocar(tabelas, nome, tabela => BancoFalso.Clonar(tabela, trocas));

    private static TabelaEsquema Objeto(List<TabelaEsquema> tabelas, string nome) => tabelas.Single(tabela => tabela.Nome.Equals(BancoFalso.Nome(nome)));

    // Os índices da loja.indexada: o da coluna de domínio, o de expressões e o primário, nessa ordem.
    private static void TrocarIndice(List<TabelaEsquema> tabelas, Func<IReadOnlyList<IndiceUnico>, IndiceUnico[]> trocar) =>
        Trocar(tabelas, "indexada", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.IndicesUnicos), trocar(tabela.IndicesUnicos))));

    private static void TrocarResidual(List<ObjetoResidual> residuais, string nome, Func<ObjetoResidual, ObjetoResidual> trocar)
    {
        var indice = residuais.FindIndex(residual => residual.Nome.Equals(BancoFalso.Nome(nome)));
        residuais[indice] = trocar(residuais[indice]);
    }

    private static void TrocarSequence(List<SequenceEsquema> sequences, string nome, Func<SequenceEsquema, SequenceEsquema> trocar)
    {
        var indice = sequences.FindIndex(sequence => sequence.Nome.Equals(BancoFalso.Nome(nome)));
        sequences[indice] = trocar(sequences[indice]);
    }

    private static void TrocarColuna(List<TabelaEsquema> tabelas, string tabela, string coluna, params (string Propriedade, object? Valor)[] trocas) =>
        Trocar(tabelas, tabela, lida => BancoFalso.Clonar(lida, (nameof(TabelaEsquema.Colunas), lida.Colunas.Select(c => c.Nome == coluna ? BancoFalso.Clonar(c, trocas) : c).ToArray())));

    private static void TrocarChave(List<TabelaEsquema> tabelas, Func<ChaveEstrangeira, IEnumerable<ChaveEstrangeira>> trocar, string tabela = "itens") =>
        Trocar(tabelas, tabela, lida => BancoFalso.Clonar(lida, (nameof(TabelaEsquema.ChavesEstrangeiras), lida.ChavesEstrangeiras.SelectMany(trocar).ToArray())));
}

internal sealed class IdentidadeFalsa(string banco, bool completo, Defeito defeito, bool vaza, bool origem) : IdentidadeBanco
{
    private int _leituras;

    public override Task<IdentidadeLida> LerAsync(CancellationToken cancelamento)
    {
        _leituras++;
        return Task.FromResult(new IdentidadeLida
        {
            Banco = defeito == Defeito.BancoErrado ? "outro-banco" : banco,
            Servidor = defeito == Defeito.IdentidadeInstavel ? $"servidor-{_leituras}" : vaza ? "servidor-" + BancoFalso.SentinelaCurto : "servidor-falso",
            Linhagem = defeito == Defeito.LinhagemVazia ? new Dictionary<string, string>() : new Dictionary<string, string> { ["identificador"] = "7" },
            Transporte = Transporte(),
        });
    }

    // O transporte do falso com os defeitos dele, só na origem.
    private TransporteConexao? Transporte()
    {
        if (!completo && defeito != Defeito.InventaRecursos)
        {
            return null;
        }

        var transporte = BancoFalso.Transporte();
        if (!origem)
        {
            return defeito == Defeito.TransporteOtimistaNoDestino ? BancoFalso.Clonar(transporte, (nameof(TransporteConexao.Cifrado), true)) : transporte;
        }

        return defeito switch
        {
            Defeito.TransporteAusente => null,
            Defeito.TransporteOtimista => BancoFalso.Clonar(transporte, (nameof(TransporteConexao.Cifrado), true)),
            Defeito.TransporteVerificadoSemCifra => BancoFalso.Clonar(transporte, (nameof(TransporteConexao.ServidorVerificado), true), (nameof(TransporteConexao.Cifrado), null)),
            Defeito.TransporteLocalTrocado => BancoFalso.Clonar(transporte, (nameof(TransporteConexao.Local), true)),
            Defeito.TransporteVerificadoTrocado => BancoFalso.Clonar(transporte, (nameof(TransporteConexao.Cifrado), true), (nameof(TransporteConexao.ServidorVerificado), true)),
            _ => transporte,
        };
    }
}

internal sealed class SqlFalso(bool semEscape, Defeito defeito = Defeito.Nenhum) : DialetoSql
{
    public override string Citar(string identificador) =>
        "\"" + (semEscape ? identificador : identificador.Replace("\"", "\"\"", StringComparison.Ordinal)) + "\"";

    // Como num PostgreSQL UTF-8, com os defeitos de quem dobra demais ou esquece as aspas.
    public override CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo)
    {
        var tamanho = defeito == Defeito.LimiteEmCaracteres ? escrito.Length : System.Text.Encoding.UTF8.GetByteCount(escrito);
        if (defeito != Defeito.SemLimite && tamanho > 63)
        {
            return CorrespondenciaNome.NaoCorresponde;
        }

        var nome = entreAspas && defeito != Defeito.IgnoraAspas
            ? escrito
            : defeito == Defeito.DobraComToLowerInvariant
                ? escrito.ToLowerInvariant()
                : string.Concat(escrito.Select(caractere => char.IsAsciiLetterUpper(caractere) ? char.ToLowerInvariant(caractere) : caractere));
        return nome == doCatalogo ? CorrespondenciaNome.Corresponde : CorrespondenciaNome.NaoCorresponde;
    }
}

/// <summary>A fixture do dialeto falso: um par de bancos em memória por teste, com o mesmo esquema nos dois lados.</summary>
internal sealed class FixtureFalsa(bool completo, Defeito defeito, Func<EsquemaBanco, bool, EsquemaBanco>? perturbar = null) : FixtureDialeto
{
    private readonly ProvedorFalso _provedor = new(completo, defeito, perturbar);
    private readonly ProvedorFalso _privilegiado = new(completo, defeito, perturbar, privilegiado: true);

    public override EsquemaDeConformidade Esperado { get; } = BancoFalso.Esperado(completo, defeito);

    public override ValueTask<ParDeBancos> CriarParAsync(CancellationToken cancelamento) => CriarAsync(_provedor, cancelamento);

    public override ValueTask<ParDeBancos> CriarParPrivilegiadoAsync(CancellationToken cancelamento) => CriarAsync(_privilegiado, cancelamento);

    public override ProvedorDialeto Provedor => _provedor;

    public override SegredoConexao ConexaoRecusada(string sentinela) => new(ProvedorFalso.Inaceitavel + "senha=" + sentinela);

    private static async ValueTask<ParDeBancos> CriarAsync(ProvedorFalso provedor, CancellationToken cancelamento)
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var origem = await provedor.ConectarOrigemAsync(new SegredoConexao($"origem-{sufixo}"), cancelamento);
        var destino = await provedor.ConectarDestinoAsync(new SegredoConexao($"destino-{sufixo}"), cancelamento);
        return new ParDeBancos(origem, destino, $"origem-{sufixo}", $"destino-{sufixo}", () => ValueTask.CompletedTask);
    }
}
