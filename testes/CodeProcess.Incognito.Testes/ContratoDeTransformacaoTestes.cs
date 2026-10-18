using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// O contrato de extensão: <see cref="DominioPseudonimo"/> (construtor, <c>TryParse</c>, <c>default</c> e igualdade),
/// regra, contextos e resultado (validação e nada de valor no <c>ToString</c>) e o método-modelo
/// <see cref="FabricaTransformador.Criar"/> com uma fábrica de fora do núcleo.
/// </summary>
public sealed class ContratoDeTransformacaoTestes : IDisposable
{
    private const string Sentinela = "SENTINELA-123.456.789-09";

    private readonly ChaveIncognito _chave = ChaveIncognito.Gerar();

    public static TheoryData<string?> DominiosForaDaForma =>
        [(string?)null, "", "cpf", "@1", "cpf@", "cpf@0", "cpf@01", "cpf@+1", "cpf@-1", "cpf@ 1", "cpf@1 ", "Cpf@1", "cpf@2147483648", "cpf@1.0", "cp f@1", "cpf@١"];

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Dominio_le_e_escreve_nome_arroba_versao()
    {
        Assert.True(DominioPseudonimo.TryParse("cpf@1", out var cpf));
        Assert.True(DominioPseudonimo.TryParse("dfe-numero@12", out var numero));
        Assert.True(DominioPseudonimo.TryParse("a.b_c-d@2147483647", out var maior));

        Assert.Equal(("cpf", 1), (cpf.Nome, cpf.Versao));
        Assert.Equal(("dfe-numero", 12), (numero.Nome, numero.Versao));
        Assert.Equal(int.MaxValue, maior.Versao);
        Assert.Equal("cpf@1", cpf.ToString());
        Assert.Equal(new DominioPseudonimo("cpf", 1), cpf);
    }

    [Theory]
    [MemberData(nameof(DominiosForaDaForma))]
    public void Dominio_fora_da_forma_nao_e_lido(string? texto)
    {
        Assert.False(DominioPseudonimo.TryParse(texto, out var dominio));
        Assert.Equal(default, dominio);
    }

    [Fact]
    public void Dominio_recusa_nome_fora_da_regra_e_versao_menor_que_um_sem_ecoar_o_nome()
    {
        var excecao = Assert.Throws<ArgumentException>(() => new DominioPseudonimo(Sentinela, 1));
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new DominioPseudonimo(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DominioPseudonimo("cpf", 0));
    }

    [Fact]
    public void Dominio_default_lanca_no_uso_mas_compara_e_tem_hash()
    {
        var vazio = default(DominioPseudonimo);

        Assert.Throws<InvalidOperationException>(() => vazio.Nome);
        Assert.Throws<InvalidOperationException>(() => vazio.Versao);
        Assert.Equal("", vazio.ToString());
        Assert.True(vazio == default);
        Assert.True(vazio != new DominioPseudonimo("cpf", 1));
        _ = vazio.GetHashCode();
        Assert.Throws<ArgumentException>(() => new RegraTransformacao(vazio));
    }

    [Fact]
    public void Dominio_compara_nome_ordinal_e_versao()
    {
        var cpf = new DominioPseudonimo("cpf", 1);

        Assert.NotEqual(cpf, new DominioPseudonimo("cpf", 2));
        Assert.NotEqual(cpf, new DominioPseudonimo("cpf-cliente", 1));
        Assert.Equal(cpf.GetHashCode(), new DominioPseudonimo("cpf", 1).GetHashCode());
        Assert.False(cpf.Equals((object)"cpf@1"));

        // Hashes de nomes diferentes não são todos iguais: o hash não ignora o nome.
        string[] nomes = ["cpf", "cnpj", "email", "telefone", "cep", "dfe-numero", "chave-pix", "texto.substituir"];
        Assert.True(nomes.Select(nome => new DominioPseudonimo(nome, 1).GetHashCode()).Distinct().Count() > 1, "O hash ignora o nome.");
    }

    [Fact]
    public void Regra_copia_os_parametros_e_valida_as_politicas()
    {
        var parametros = new Dictionary<string, string>(StringComparer.Ordinal) { ["permutarOrdem"] = "true" };
        var regra = new RegraTransformacao(new DominioPseudonimo("cnpj", 1)) { Parametros = parametros };
        parametros["permutarOrdem"] = "false";
        parametros["outro"] = "x";

        Assert.Equal("true", regra.Parametros["permutarOrdem"]);
        Assert.Single(regra.Parametros);
        Assert.Equal(PoliticaEntradaInvalida.Preservar, regra.EntradaInvalida);
        Assert.Equal(PoliticaEntradaMalformada.Falhar, regra.EntradaMalformada);
        Assert.Null(regra.TemDerivarDe);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegraTransformacao(regra.Dominio) { EntradaInvalida = (PoliticaEntradaInvalida)9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegraTransformacao(regra.Dominio) { EntradaMalformada = (PoliticaEntradaMalformada)9 });
        Assert.Throws<ArgumentNullException>(() => new RegraTransformacao(regra.Dominio) { Parametros = null! });
        Assert.Throws<ArgumentException>(() => new RegraTransformacao(regra.Dominio) { Parametros = new Dictionary<string, string> { [""] = "x" } });
        Assert.Throws<ArgumentNullException>(() => new RegraTransformacao(regra.Dominio) { Parametros = new Dictionary<string, string> { ["a"] = null! } });
    }

    [Fact]
    public void Contexto_de_criacao_recusa_comprimento_nao_positivo_e_tipos_fora_do_enum()
    {
        using var sessao = new SessaoTransformacao(_chave, "homologacao");
        var regra = new RegraTransformacao(new DominioPseudonimo("cpf", 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextoCriacao(regra, TipoColuna.Texto, sessao) { ComprimentoMaximo = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextoCriacao(regra, TipoColuna.Texto, sessao) { ComprimentoMaximo = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextoCriacao(regra, (TipoColuna)99, sessao));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextoCriacao(regra, TipoColuna.Texto, sessao) { TipoReferencia = (TipoColuna)99 });
        Assert.Throws<ArgumentNullException>(() => new ContextoCriacao(null!, TipoColuna.Texto, sessao));
        Assert.Throws<ArgumentNullException>(() => new ContextoCriacao(regra, TipoColuna.Texto, null!));
        Assert.Equal("homologacao", new ContextoCriacao(regra, TipoColuna.Texto, sessao).Ambiente);
    }

    [Fact]
    public void Resultado_e_contexto_nunca_mostram_o_valor()
    {
        var resultado = new ResultadoTransformacao(Sentinela, Desfecho.CopiadoEmClaro);
        var contexto = new ContextoTransformacao(Sentinela);

        Assert.DoesNotContain(Sentinela, resultado.ToString(), StringComparison.Ordinal);
        Assert.Equal("ResultadoTransformacao(CopiadoEmClaro)", resultado.ToString());
        Assert.DoesNotContain(Sentinela, contexto.ToString(), StringComparison.Ordinal);
        Assert.Equal(Desfecho.Indefinido, default(ResultadoTransformacao).Desfecho);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultadoTransformacao(null, Desfecho.Indefinido));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultadoTransformacao(null, (Desfecho)99));
    }

    [Fact]
    public void Sessao_valida_o_ambiente_e_recusa_chave_descartada()
    {
        var descartada = ChaveIncognito.Gerar();
        descartada.Dispose();

        Assert.Throws<ArgumentNullException>(() => new SessaoTransformacao(null!, "homologacao"));
        Assert.Throws<ArgumentException>(() => new SessaoTransformacao(_chave, "Homologacao"));
        Assert.Throws<ObjectDisposedException>(() => new SessaoTransformacao(descartada, "homologacao"));
    }

    [Fact]
    public void Fabrica_de_fora_cria_pelo_metodo_modelo_e_e_conferida()
    {
        using var sessao = new SessaoTransformacao(_chave, "homologacao");
        var fabrica = new FabricaMaiusculas();
        var regra = new RegraTransformacao(new DominioPseudonimo(fabrica.DominioPadrao, 1));

        var transformador = fabrica.Criar(new ContextoCriacao(regra, TipoColuna.Texto, sessao));
        var resultado = transformador.Transformar("abc", default);

        Assert.Equal("exemplo.maiusculas", fabrica.DominioPadrao);
        Assert.Empty(fabrica.ParametrosDeIdentidade);
        Assert.Equal(("ABC", Desfecho.Substituido), (resultado.Valor, resultado.Desfecho));
        Assert.False(transformador.TransformaNulo);
        Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(new ContextoCriacao(regra, TipoColuna.Inteiro, sessao)));
        Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("exemplo.maiusculas", 2)), TipoColuna.Texto, sessao)));
        Assert.Equal("x", Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(new RegraTransformacao(regra.Dominio) { Parametros = new Dictionary<string, string> { ["x"] = "1" } })).Parametro);
        Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(new RegraTransformacao(regra.Dominio) { TemDerivarDe = true }));
        Assert.Throws<ArgumentNullException>(() => fabrica.Criar(null!));
        Assert.Throws<ArgumentNullException>(() => fabrica.Validar(null!));
        var comParametro = new RegraTransformacao(regra.Dominio) { Parametros = new Dictionary<string, string> { ["x"] = "1" } };
        Assert.Throws<RegraInvalidaException>(() => fabrica.Normalizar(comParametro));
        Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(new ContextoCriacao(comParametro, TipoColuna.Texto, sessao)));
        Assert.Empty(fabrica.Normalizar(regra));
    }

    [Fact]
    public void Fabrica_que_cria_transformador_de_outro_nome_ou_nulo_e_recusada()
    {
        using var sessao = new SessaoTransformacao(_chave, "homologacao");
        var regra = new RegraTransformacao(new DominioPseudonimo("exemplo.errada", 1));
        Assert.Throws<InvalidOperationException>(() => new FabricaErrada(devolverNulo: false).Criar(new ContextoCriacao(regra, TipoColuna.Texto, sessao)));
        Assert.Throws<InvalidOperationException>(() => new FabricaErrada(devolverNulo: true).Criar(new ContextoCriacao(regra, TipoColuna.Texto, sessao)));
        Assert.Throws<InvalidOperationException>(() => new FabricaDeOutraInjetividade().Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("exemplo.maiusculas", 1)), TipoColuna.Texto, sessao)));
    }

    [Fact]
    public void Uma_fabrica_de_fora_nao_repassa_o_contexto_a_uma_embutida()
    {
        // Sem a reserva, a fábrica de fora receberia um br.cpf com a chave da execução no domínio da regra dela.
        using var sessao = new SessaoTransformacao(_chave, "homologacao");
        var regra = new RegraTransformacao(new DominioPseudonimo("exemplo.oraculo", 1));
        var oraculo = new FabricaOraculo();

        Assert.Throws<InvalidOperationException>(() => oraculo.Criar(new ContextoCriacao(regra, TipoColuna.Texto, sessao)));
        Assert.Equal(0, sessao.Instancias);
    }

    [Fact]
    public void A_mesma_fabrica_pode_usar_o_mesmo_contexto_de_novo()
    {
        using var sessao = new SessaoTransformacao(_chave, "homologacao");
        var fabrica = new FabricaMaiusculas();
        var contexto = new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo(fabrica.DominioPadrao, 1)), TipoColuna.Texto, sessao);

        fabrica.Criar(contexto);
        fabrica.Criar(contexto);
        Assert.Throws<InvalidOperationException>(() => new FabricaMaiusculas().Criar(contexto));
    }

    [Fact]
    public void Regra_invalida_tem_codigo_proprio()
    {
        var excecao = new RegraInvalidaException("mensagem", "parametro");

        Assert.Equal("INC1005", excecao.Codigo);
        Assert.Equal("parametro", excecao.Parametro);
        Assert.Null(new RegraInvalidaException("mensagem").Parametro);
    }

    private sealed class FabricaMaiusculas : FabricaTransformador
    {
        public override string Nome => "exemplo.maiusculas";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Maiusculas();
    }

    private sealed class Maiusculas : Transformador
    {
        public override string Nome => "exemplo.maiusculas";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) =>
            new(((string)valor!).ToUpperInvariant(), Desfecho.Substituido);
    }

    private sealed class FabricaDeOutraInjetividade : FabricaTransformador
    {
        public override string Nome => "exemplo.maiusculas";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Maiusculas();
    }

    private sealed class FabricaOraculo : FabricaTransformador
    {
        public override string Nome => "exemplo.oraculo";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        protected override Transformador CriarCore(ContextoCriacao contexto) =>
            TransformadoresEmbutidos.Fabricas.Single(fabrica => fabrica.Nome == "br.cpf").Criar(contexto);
    }

    private sealed class FabricaErrada(bool devolverNulo) : FabricaTransformador
    {
        public override string Nome => "exemplo.errada";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        protected override Transformador CriarCore(ContextoCriacao contexto) => devolverNulo ? null! : new Maiusculas();
    }
}
