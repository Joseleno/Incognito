using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.AcessoNoPlanoTestes;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// Nulo nunca aprova: toda propriedade anulável do modelo do esquema e da identidade está em exatamente uma de quatro
/// listas. Com consumidor, o cenário em que o <c>planejar</c> a lê, e o item (código, tabela, coluna e objeto) que o nulo
/// produz e o valor otimista não; isenta, sem consumidor no <c>planejar</c>, com o motivo e a tarefa dona; nulo estrutural,
/// em que o nulo quer dizer ausência, com a frase do XML que o define; pendente, vazia. Um campo novo entra aqui sozinho, pela
/// reflexão, e quebra o teste enquanto não estiver numa lista.
/// </summary>
public sealed class NuloNuncaAprovaTestes
{
    private const string CpfTratado = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";
    private const string DocTratado = "tabelas:\n  loja.clientes:\n    colunas:\n      doc: { transformador: br.cpf@1 }\n";

    private static readonly ColacaoEsquema _numerica = new() { Nome = "kn", Esquema = "public", Deterministica = false, TemRegras = false, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };
    private static readonly NullabilityInfoContext _anulabilidade = new();

    // (a) Com consumidor.
    private static readonly Dictionary<string, Cenario> _comConsumidor = new(StringComparer.Ordinal)
    {
        ["ChaveEstrangeira.Imposta"] = new("", nulo => new(ComChave(imposta: nulo ? null : true, validada: true), Coerente()), new("INC3530", "loja.pedidos", null, "fk_pedidos_clientes")),
        ["ChaveEstrangeira.Validada"] = new("", nulo => new(ComChave(imposta: true, validada: nulo ? null : true), Coerente()), new("INC3530", "loja.pedidos", null, "fk_pedidos_clientes")),
        ["ColacaoEsquema.IgnoraAcento"] = Numerica(nameof(ColacaoEsquema.IgnoraAcento)),
        ["ColacaoEsquema.IgnoraCaixa"] = Numerica(nameof(ColacaoEsquema.IgnoraCaixa)),
        ["ColacaoEsquema.IgnoraPontuacao"] = Numerica(nameof(ColacaoEsquema.IgnoraPontuacao)),

        // A chave primária lida direto, sem o índice primário, compara pela colação da coluna.
        ["ColunaEsquema.Colacao"] = new(DocTratado, nulo => Mesmo(ChavePrimariaNoDoc(BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.Anulavel), false), (nameof(ColunaEsquema.Colacao), nulo ? null : Padrao)))), new("INC3051", "loja.clientes", "doc", null)),
        ["ColunaEsquema.Comprimento"] = new(DocTratado, nulo => Mesmo(ComDoc(BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.Comprimento), nulo ? null : new LimiteComprimento { Valor = 20, Unidade = UnidadeComprimento.PontosDeCodigo })))), new("INC3044", "loja.clientes", "doc", null)),
        ["ColunaEsquema.Escala"] = new(ValorTratado, nulo => Mesmo(ComDoc(Decimal(10, nulo ? null : 2))), new("INC3047", "loja.clientes", "valor", null), ComDecimal()),
        ["ColunaEsquema.Faixa"] = new(DocTratado, nulo => Mesmo(ComDoc(BancoFalso.Clonar(Inteiro("doc"), (nameof(ColunaEsquema.Faixa), nulo ? null : new FaixaInteira { Minimo = long.MinValue, Maximo = long.MaxValue })))), new("INC3047", "loja.clientes", "doc", null)),
        ["ColunaEsquema.IgnoraCaixaPeloTipo"] = new(DocTratado, nulo => Mesmo(ComDoc(BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), nulo ? null : false)), UnicoNoDoc(Padrao))), new("INC3051", "loja.clientes", "doc", "uq_doc")),
        ["ColunaEsquema.IgnoraEspacoFinalPeloTipo"] = new(DocTratado, nulo => Mesmo(ComDoc(BancoFalso.Clonar(Texto("doc", 20), (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), nulo ? null : false)), UnicoNoDoc(Padrao))), new("INC3051", "loja.clientes", "doc", "uq_doc")),
        ["ColunaEsquema.Precisao"] = new(ValorTratado, nulo => Mesmo(ComDoc(nulo ? Decimal(null, null) : Decimal(10, 2))), new("INC3047", "loja.clientes", "valor", null), ComDecimal()),

        // A gerada sem as referências conta como calculada da tratada, e o CHECK que a cita avisa.
        ["ColunaEsquema.ReferenciasDaGeracao"] = new(CpfTratado, nulo => Mesmo(ComGeradaNoCheck(nulo ? null : new ReferenciasDaExpressao { Colunas = ["nome"], Incompletas = false })), new("INC3541", "loja.clientes", null, "ck_calculada")),
        ["ColunaEsquema.RestritaPeloTipo"] = new(CpfTratado, nulo => new(Coerente(), TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.RestritaPeloTipo), nulo ? null : false))), new("INC3540", "loja.clientes", "cpf", null)),
        ["ColunaEsquema.Tipo"] = new(CpfTratado, nulo => Mesmo(TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Tipo), nulo ? null : TipoColuna.Texto))), new("INC3041", "loja.clientes", "cpf", null)),
        ["ElementoIndice.Colacao"] = new(DocTratado, nulo => Mesmo(ComDoc(Texto("doc", 20), UnicoNoDoc(nulo ? null : Padrao))), new("INC3051", "loja.clientes", "doc", "uq_doc")),
        ["ChaveEstrangeira.DesligadaSemRestauracao"] = new("", nulo => Mesmo(ComChaveRestauravel(nulo ? null : false)), new("INC3077", "loja.pedidos", null, "fk_pedidos_clientes")),
        ["EsquemaBanco.Acesso"] = new("", nulo => new(Coerente(), BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Acesso), nulo ? null : Coerente().Acesso))), new("INC3062", null, null, null)),
        ["EsquemaBanco.ObjetosNoEsquemaReservado"] = new("", nulo => new(Coerente(), BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosNoEsquemaReservado), nulo ? null : Array.Empty<ObjetoReservado>()))), new("INC3073", null, null, null)),
        ["EsquemaBanco.TriggersDeEvento"] = new("", nulo => new(Coerente(), BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.TriggersDeEvento), nulo ? null : Array.Empty<string>()))), new("INC3585", null, null, null)),
        ["EsquemaBanco.Propriedades"] = new("", nulo => new(Coerente(), BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Propriedades), nulo ? null : Coerente().Propriedades))), new("INC3082", null, null, null)),
        // O nulo na origem é o pior caso, um destino preparado; no destino, ele só avisa no planejar (INC3583).
        ["IdentidadeLida.Marcador"] = new("", nulo => new(Coerente(), Coerente(), Cifrado(true, true), MarcadorDaOrigemNulo: nulo), new("INC3602", null, null, null)),
        ["IdentidadeLida.Transporte"] = new("", nulo => new(Coerente(), Coerente(), nulo ? null : Cifrado(true, true)), new("INC3560", null, null, null)),
        ["ObjetoResidual.DependenciasIncompletas"] = new("", nulo => new(Coerente(), ComResidual(Visao(dependenciasIncompletas: nulo ? null : false, populada: true), podeLimpar: true)), new("INC3570", null, null, "mv_clientes")),
        ["ObjetoResidual.Populada"] = new("", nulo => new(Coerente(), ComResidual(Visao(dependenciasIncompletas: false, populada: nulo ? null : false), podeLimpar: false)), new("INC3070", null, null, "mv_clientes")),
        ["PermissoesResidual.PodeLimpar"] = new("", nulo => new(Coerente(), ComResidual(Visao(dependenciasIncompletas: false, populada: true), podeLimpar: nulo ? null : true)), new("INC3070", null, null, "mv_clientes")),
        ["PermissoesSequence.PodeAjustar"] = new("", nulo => new(Coerente(), ComAjuste(Coerente(), nulo ? null : true)), new("INC3067", "loja.clientes", null, "clientes_id_seq")),
        ["PermissoesTabela.EscritaRestritaPorLinha"] = NoDestino("clientes", nameof(PermissoesTabela.EscritaRestritaPorLinha), false, new("INC3061", "loja.clientes", null, null)),
        ["PermissoesTabela.LeituraFiltradaPorLinha"] = new("", nulo => new(ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.LeituraFiltradaPorLinha), nulo ? null : false)), Coerente()), new("INC3060", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeAlterarRestricoes"] = NoDestino("pedidos", nameof(PermissoesTabela.PodeAlterarRestricoes), true, new("INC3066", "loja.pedidos", null, "fk_pedidos_clientes")),
        ["PermissoesTabela.PodeDesligarTriggers"] = new("", nulo => new(Coerente(), ComPermissao(ComTrigger(), "clientes", (nameof(PermissoesTabela.PodeDesligarTriggers), nulo ? null : true))), new("INC3065", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeEsvaziar"] = NoDestino("clientes", nameof(PermissoesTabela.PodeEsvaziar), true, new("INC3064", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeGravarIdentidade"] = new("", nulo => new(ComIdentidadeSempre(), ComPermissao(ComIdentidadeSempre(), "clientes", (nameof(PermissoesTabela.PodeGravarIdentidade), nulo ? null : true))), new("INC3064", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeInserir"] = NoDestino("clientes", nameof(PermissoesTabela.PodeInserir), true, new("INC3064", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeLer"] = new("", nulo => new(ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeLer), nulo ? null : true)), Coerente()), new("INC3063", "loja.clientes", null, null)),
        ["PermissoesTabela.PodeReferenciar"] = NoDestino("clientes", nameof(PermissoesTabela.PodeReferenciar), true, new("INC3066", "loja.pedidos", null, "fk_pedidos_clientes")),
        ["TabelaEsquema.Assinada"] = new("", nulo => new(Coerente(), Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Assinada), nulo ? null : false)))), new("INC3076", "loja.clientes", null, null)),
        ["TabelaEsquema.Publicada"] = new("", nulo => new(Coerente(), Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), nulo ? null : false)))), new("INC3074", "loja.clientes", null, null)),
        ["TransporteConexao.Cifrado"] = new("", nulo => new(Coerente(), Coerente(), Cifrado(nulo ? null : true, true)), new("INC3560", null, null, null)),
        ["TransporteConexao.ServidorVerificado"] = new("", nulo => new(Coerente(), Coerente(), Cifrado(true, nulo ? null : true)), new("INC3560", null, null, null)),
    };

    // (b) Isentos: sem consumidor no planejar, com o motivo e a tarefa dona.
    private static readonly Dictionary<string, string> _isentos = new(StringComparer.Ordinal)
    {
        ["PermissoesBanco.PodeCriarEsquema"] = "só o preparar-destino cria o esquema do diário (decisão 96, D12): o planejar só avisa do diário ausente (INC3584)",
        ["PermissoesBanco.PodeMarcarBanco"] = "só o preparar-destino marca o banco (INC-035)",
        ["TabelaEsquema.Extensao"] = "só para mensagem: a tabela adotada por extensão é classificada e copiada como as outras (relatório, INC-041)",
        ["ColunaEsquema.TipoBase"] = "só para mensagem e relatório: a semântica da coluna já vem pelo fim da cadeia de domínios (relatório, INC-041)",
    };

    // (c) Nulo estrutural: o nulo quer dizer ausência, com a frase do XML que o define.
    private static readonly Dictionary<string, string> _estruturais = new(StringComparer.Ordinal)
    {
        ["ColacaoEsquema.Esquema"] = "nulo onde ela não tem esquema",
        ["ElementoIndice.Coluna"] = "nula num elemento de expressão",
        ["EntradaDiario.Nome"] = "nulo nas triggers",
        ["IndiceUnico.Predicado"] = "nulo se o índice não é parcial",
        ["IndiceUnico.ReferenciasDasExpressoes"] = "nulo se o índice não tem elemento de expressão",
        ["LimiteComprimento.Valor"] = "nulo se o tipo não declara limite",
        ["MarcadorLido.GravadoEm"] = "só no válido",
        ["MarcadorLido.Identificador"] = "só no válido",
        ["TabelaEsquema.ChaveParticao"] = "A chave de partição de uma particionada, também da partição intermediária (que é particionada); nula nas outras",
        ["TabelaEsquema.Historico"] = "O histórico de uma temporal; nulo nas outras",
        ["TabelaEsquema.Pai"] = "A particionada de uma partição, ou a mãe de uma herdeira; nulo nas outras",
    };

    // (d) Pendentes: o consumidor chega num PR posterior. Vazia no último PR do planejar.
    private static readonly Dictionary<string, string> _pendentes = new(StringComparer.Ordinal);

    private static string ValorTratado => "tabelas:\n  loja.clientes:\n    colunas:\n      valor: { transformador: exemplo.decimal@1 }\n";

    public static TheoryData<string> ComConsumidor => [.. _comConsumidor.Keys.Order(StringComparer.Ordinal)];

    public static TheoryData<string> Estruturais => [.. _estruturais.Keys.Order(StringComparer.Ordinal)];

    [Fact]
    public void Toda_propriedade_anulavel_do_modelo_esta_em_exatamente_uma_lista()
    {
        var listas = _comConsumidor.Keys.Concat(_isentos.Keys).Concat(_estruturais.Keys).Concat(_pendentes.Keys).ToList();

        Assert.Empty(_pendentes);
        Assert.Equal(Anulaveis(), listas.Order(StringComparer.Ordinal));
        Assert.Equal(listas.Count, listas.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(ComConsumidor))]
    public async Task O_nulo_produz_o_item_e_o_valor_otimista_nao(string propriedade)
    {
        var cenario = _comConsumidor[propriedade];

        var otimista = await ItensAsync(cenario, nulo: false);
        var nulo = await ItensAsync(cenario, nulo: true);

        // O otimista aprova: um erro dele esconderia os avisos, e o item ausente não provaria nada.
        Assert.False(otimista.Recusado, string.Join("; ", otimista.Itens));
        Assert.DoesNotContain(cenario.Esperado, otimista.Itens);
        Assert.Contains(cenario.Esperado, nulo.Itens);
    }

    [Fact]
    public async Task A_escala_nula_com_a_precisao_tem_mensagem_propria()
    {
        var provedor = new ProvedorDoPlanejar(ComDoc(Decimal(10, null)), ComDoc(Decimal(10, null)));

        var recusa = Assert.IsType<PlanoRecusadoException>(
            await Assert.ThrowsAnyAsync<Exception>(() => DialetoProtegidoTestes.PlanejarAsync(provedor, Yaml.Minimo + ValorTratado, registro: ComDecimal())),
            exactMatch: true);

        var erro = Assert.Single(recusa.Erros);
        Assert.Equal(("INC3047", "valor"), (erro.Codigo, erro.Coluna?.ToString()));
        Assert.Contains("não informou a escala", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Estruturais))]
    public void O_nulo_estrutural_esta_no_XML_da_propriedade(string propriedade)
    {
        // A frase sai do XML gerado do Motor, com os espaços juntados: o nulo estrutural é o que o contrato diz.
        var documentacao = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "CodeProcess.Incognito.Motor.xml"));
        var membro = $"P:{typeof(EsquemaBanco).Namespace}.{propriedade}";
        var resumo = documentacao.Descendants("member").Single(elemento => (string?)elemento.Attribute("name") == membro).Element("summary")!.Value;

        Assert.Contains(_estruturais[propriedade], Regex.Replace(resumo, @"\s+", " "), StringComparison.Ordinal);
    }

    // As propriedades anuláveis de cada tipo do modelo (com init), fora as exceções.
    private static IEnumerable<string> Anulaveis() =>
        typeof(EsquemaBanco).Assembly.GetTypes()
            .Where(tipo => (tipo.IsPublic || tipo.IsNestedPublic) && !tipo.IsEnum && !tipo.IsInterface && tipo.Namespace == typeof(EsquemaBanco).Namespace && !typeof(Exception).IsAssignableFrom(tipo))
            .SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(propriedade => propriedade.SetMethod is { } set && set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                .Where(propriedade => _anulabilidade.Create(propriedade).ReadState == NullabilityState.Nullable)
                .Select(propriedade => $"{tipo.Name}.{propriedade.Name}"))
            .Order(StringComparer.Ordinal);

    // Os itens do plano: os erros, se recusado; os avisos, se não.
    private static async Task<(List<Item> Itens, bool Recusado)> ItensAsync(Cenario cenario, bool nulo)
    {
        var leitura = cenario.Montar(nulo);
        var provedor = new ProvedorDoPlanejar(leitura.Origem, leitura.Destino)
        {
            Identidades = destino => destino
                ? BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Transporte), leitura.TransporteDoDestino))
                : BancoFalso.Clonar(Identidade("loja"), (nameof(IdentidadeLida.Marcador), leitura.MarcadorDaOrigemNulo ? null : Ausente)),
        };
        try
        {
            var plano = await DialetoProtegidoTestes.PlanejarAsync(provedor, Yaml.Minimo + cenario.Yaml, registro: cenario.Registro);
            return ([.. plano.Avisos.Select(aviso => new Item(aviso.Codigo, aviso.Tabela?.ToString(), aviso.Coluna?.ToString(), aviso.Objeto))], false);
        }
        catch (PlanoRecusadoException recusa)
        {
            return ([.. recusa.Erros.Select(erro => new Item(erro.Codigo, erro.Tabela?.ToString(), erro.Coluna?.ToString(), erro.Objeto))], true);
        }
    }

    private static Leitura Mesmo(EsquemaBanco esquema) => new(esquema, esquema);

    private static TransporteConexao Cifrado(bool? cifrado, bool? verificado) => new() { Cifrado = cifrado, ServidorVerificado = verificado, Local = false };

    // O doc num índice único com br.cpf numa colação numérica medida: só passa com as três marcas falsas.
    private static Cenario Numerica(string marca) =>
        new(DocTratado, nulo => Mesmo(ComDoc(Texto("doc", 20), UnicoNoDoc(nulo ? BancoFalso.Clonar(_numerica, (marca, null)) : _numerica))), new("INC3051", "loja.clientes", "doc", "uq_doc"));

    private static Cenario NoDestino(string tabela, string permissao, bool otimista, Item esperado) =>
        new("", nulo => new(Coerente(), ComPermissao(Coerente(), tabela, (permissao, nulo ? null : otimista))), esperado);

    private static EsquemaBanco ComChaveRestauravel(bool? restauravel) => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { BancoFalso.Clonar(tabela.ChavesEstrangeiras[0], (nameof(ChaveEstrangeira.DesligadaSemRestauracao), restauravel)) })));

    private static EsquemaBanco ComChave(bool? imposta, bool? validada) => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { new ChaveEstrangeira { Nome = "fk_pedidos_clientes", Colunas = ["cliente"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["id"], Imposta = imposta, Validada = validada, DesligadaSemRestauracao = false } })));

    // clientes com o doc no lugar do cpf, e os índices dados além do primário.
    private static EsquemaBanco ComDoc(ColunaEsquema doc, params IndiceUnico[] indices) => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), doc, Texto("nome", 100) }),
        (nameof(TabelaEsquema.IndicesUnicos), tabela.IndicesUnicos.Concat(indices).ToArray())));

    private static IndiceUnico UnicoNoDoc(ColacaoEsquema? colacao) =>
        new() { Nome = "uq_doc", Primario = false, Elementos = [new ElementoIndice { Coluna = "doc", Colacao = colacao }], NulosDistintos = true };

    // A chave primária no doc, sem o índice primário (um dialeto que só informa a chave).
    private static EsquemaBanco ChavePrimariaNoDoc(ColunaEsquema doc) => Trocar(ComDoc(doc), "clientes", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.ChavePrimaria), (string[])["doc"]),
        (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>())));

    private static ColunaEsquema Decimal(int? precisao, int? escala) => new()
    {
        Nome = "valor",
        TipoNativo = "numeric",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Decimal,
        Anulavel = true,
        Precisao = precisao,
        Escala = escala,
        RestritaPeloTipo = false,
    };

    // A gerada calculada a partir das referências dadas, citada por um CHECK imposto.
    private static EsquemaBanco ComGeradaNoCheck(ReferenciasDaExpressao? referencias)
    {
        var gerada = BancoFalso.Clonar(Texto("calculada", 20), (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada), (nameof(ColunaEsquema.ReferenciasDaGeracao), referencias));
        return Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(gerada).ToArray()),
            (nameof(TabelaEsquema.RestricoesCheck), new[] { new RestricaoCheck { Nome = "ck_calculada", Referencias = new ReferenciasDaExpressao { Colunas = ["calculada"], Incompletas = false }, Imposta = true } })));
    }

    private static EsquemaBanco ComTrigger() => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.Triggers), new[] { new TriggerEsquema { Nome = "tg_clientes", Ligada = true, Herdada = false } })));

    private static EsquemaBanco ComIdentidadeSempre() => TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre));

    private static ObjetoResidual Visao(bool? dependenciasIncompletas, bool? populada) =>
        new() { Nome = Nome("mv_clientes"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependenciasIncompletas = dependenciasIncompletas, Populada = populada };

    private static RegistroTransformadores ComDecimal()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDecimal());
        return registro;
    }

    /// <summary>Um cenário em que o campo é lido: o plano com ele no valor otimista e com ele nulo, e o item que o nulo produz.</summary>
    private sealed record Cenario(string Yaml, Func<bool, Leitura> Montar, Item Esperado, RegistroTransformadores? Registro = null);

    /// <summary>As leituras de um cenário: os dois esquemas, o transporte do destino e se o marcador da origem é nulo.</summary>
    private sealed record Leitura(EsquemaBanco Origem, EsquemaBanco Destino, TransporteConexao? TransporteDoDestino, bool MarcadorDaOrigemNulo = false)
    {
        internal Leitura(EsquemaBanco origem, EsquemaBanco destino)
            : this(origem, destino, Cifrado(true, true))
        {
        }
    }

    /// <summary>Um erro ou aviso, pelo código, pela tabela, pela coluna e pelo objeto.</summary>
    private sealed record Item(string Codigo, string? Tabela, string? Coluna, string? Objeto);

    // Uma fábrica de decimal que devolve o valor como veio (o planejar só a cria e descarta).
    private sealed class FabricaDecimal : FabricaTransformador
    {
        public override string Nome => "exemplo.decimal";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Decimal };

        public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Copia();

        private sealed class Copia : Transformador
        {
            public override string Nome => "exemplo.decimal";

            public override int Versao => 1;

            public override Injetividade Injetividade => Injetividade.Garantida;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);
        }
    }
}
