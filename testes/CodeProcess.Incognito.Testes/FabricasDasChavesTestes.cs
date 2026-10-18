using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// As fábricas da chave Pix e da chave de DF-e: os parâmetros de domínio e os padrões, a forma de <c>modelos</c>, o
/// domínio fixo da DF-e, a justificativa do <c>preservar</c> na chave Pix, o domínio-alvo procurado na sessão e as
/// instâncias compartilhadas com as colunas dos documentos. Documentos sorteados na hora pelo <see cref="GeradorBr"/>.
/// </summary>
public sealed class FabricasDasChavesTestes : IDisposable
{
    private const string Ambiente = "homologacao";
    private const string Sentinela = "SENTINELA-987654321-XX";
    private const string OsNove = "55,57,58,62,63,64,65,66,67";

    private readonly ChaveIncognito _chave = ChaveIncognito.Gerar();

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void As_chaves_declaram_os_parametros_de_dominio_e_as_outras_embutidas_nenhum()
    {
        Assert.Equal(
            new Dictionary<string, string> { ["dominioCpf"] = "br.cpf", ["dominioCnpj"] = "br.cnpj", ["dominioEmail"] = "email", ["dominioTelefone"] = "br.telefone" },
            Fabrica("br.chave-pix").ParametrosDeDominio);
        Assert.Equal(new Dictionary<string, string> { ["dominioCpf"] = "br.cpf", ["dominioCnpj"] = "br.cnpj" }, Fabrica("br.chave-dfe").ParametrosDeDominio);
        Assert.All(
            TransformadoresEmbutidos.Fabricas.Where(fabrica => fabrica.Nome is not ("br.chave-pix" or "br.chave-dfe")),
            fabrica => Assert.Empty(fabrica.ParametrosDeDominio));
    }

    [Fact]
    public void Os_dominios_referenciados_nao_sao_parametros_de_identidade_e_o_padrao_e_o_da_fachada()
    {
        var pix = Fabrica("br.chave-pix");
        var dfe = Fabrica("br.chave-dfe");

        Assert.Empty(pix.ParametrosDeIdentidade);
        Assert.Empty(dfe.ParametrosDeIdentidade);
        Assert.Equal(
            new Dictionary<string, string> { ["dominioCpf"] = "cpf", ["dominioCnpj"] = "cnpj", ["dominioEmail"] = "email", ["dominioTelefone"] = "telefone" },
            pix.Normalizar(Regra(pix)));
        Assert.Equal(
            new Dictionary<string, string> { ["dominioCpf"] = "cpf", ["dominioCnpj"] = "cnpj", ["modelos"] = OsNove },
            dfe.Normalizar(Regra(dfe)));
    }

    [Theory]
    [InlineData("br.chave-pix", "dominioCpf", "Cpf")]
    [InlineData("br.chave-pix", "dominioEmail", "")]
    [InlineData("br.chave-pix", "dominioTelefone", "telefone@1")]
    [InlineData("br.chave-pix", "dominioCnpj", " cnpj")]
    [InlineData("br.chave-dfe", "dominioCpf", "cpf.")]
    [InlineData("br.chave-dfe", "dominioCnpj", Sentinela)]
    public void Dominio_referenciado_fora_da_regra_dos_nomes_e_recusado_com_o_nome_do_parametro(string nome, string parametro, string valor)
    {
        var fabrica = Fabrica(nome);

        var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, new() { [parametro] = valor })));

        Assert.Equal(parametro, excecao.Parametro);
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("54")]
    [InlineData("055")]
    [InlineData("+55")]
    [InlineData("55,55")]
    [InlineData("55, 65")]
    [InlineData("55,")]
    [InlineData(",55")]
    [InlineData("")]
    [InlineData("59")]
    [InlineData("5")]
    public void Modelos_so_aceita_os_nove_codigos_na_forma_canonica_sem_repeticao(string modelos)
    {
        var fabrica = Fabrica("br.chave-dfe");

        Assert.Equal("modelos", Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, new() { ["modelos"] = modelos }))).Parametro);
    }

    [Theory]
    [InlineData("65,55", "55,65")]
    [InlineData("67,66,65,64,63,62,58,57,55", OsNove)]
    [InlineData("57", "57")]
    public void Modelos_e_normalizado_em_ordem_crescente(string escrito, string normalizado)
    {
        var fabrica = Fabrica("br.chave-dfe");

        Assert.Equal(normalizado, fabrica.Normalizar(Regra(fabrica, new() { ["modelos"] = escrito }))["modelos"]);
    }

    [Fact]
    public void A_chave_de_DFe_so_vive_no_dominio_dfe_numero()
    {
        var fabrica = Fabrica("br.chave-dfe");

        var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(new RegraTransformacao(new DominioPseudonimo("notas", 1))));

        Assert.Null(excecao.Parametro);
        Assert.Contains("dfe-numero", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preservar_malformado_na_chave_Pix_exige_justificativa_na_coluna_e_nao_na_declaracao_do_dominio()
    {
        var fabrica = Fabrica("br.chave-pix");

        Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, malformada: PoliticaEntradaMalformada.Preservar, justificativa: false)));
        fabrica.Validar(Regra(fabrica, malformada: PoliticaEntradaMalformada.Preservar, justificativa: true));
        fabrica.Validar(Regra(fabrica, malformada: PoliticaEntradaMalformada.Preservar, justificativa: null));
        fabrica.Validar(Regra(fabrica, malformada: PoliticaEntradaMalformada.Anular, justificativa: false));
        var dfe = Fabrica("br.chave-dfe");
        dfe.Validar(Regra(dfe, malformada: PoliticaEntradaMalformada.Preservar, justificativa: false));
    }

    [Fact]
    public void A_chave_Pix_e_a_de_DFe_usam_as_instancias_das_colunas_dos_documentos()
    {
        using var sessao = Sessao(Padroes());
        var gerador = new GeradorBr(3201);
        var cpf = gerador.Cpf();

        var colunaDeCpf = Fabrica("br.cpf").Criar(new ContextoCriacao(Regra(Fabrica("br.cpf")), TipoColuna.Texto, sessao));
        Assert.Equal(1, sessao.Instancias);

        // O CPF da coluna, mais CNPJ, e-mail, telefone e a própria chave Pix.
        var pix = Fabrica("br.chave-pix").Criar(new ContextoCriacao(Regra(Fabrica("br.chave-pix")), TipoColuna.Texto, sessao));
        Assert.Equal(5, sessao.Instancias);

        // CPF e CNPJ já existem; só a chave de DF-e é nova.
        var dfe = Fabrica("br.chave-dfe").Criar(new ContextoCriacao(Regra(Fabrica("br.chave-dfe")), TipoColuna.Texto, sessao));
        Assert.Equal(6, sessao.Instancias);

        Assert.Equal(colunaDeCpf.Transformar(cpf, default).Valor, pix.Transformar(cpf, default).Valor);
        Assert.Equal(Desfecho.Pseudonimizado, dfe.Transformar(gerador.ChaveDfe(55), default).Desfecho);
    }

    [Fact]
    public void Outra_politica_de_malformado_na_chave_Pix_cria_outras_instancias_do_mesmo_universo()
    {
        using var sessao = Sessao(Padroes());
        var cpf = new GeradorBr(3202).Cpf();
        var pix = Fabrica("br.chave-pix");

        var falha = pix.Criar(new ContextoCriacao(Regra(pix), TipoColuna.Texto, sessao));
        var anula = pix.Criar(new ContextoCriacao(Regra(pix, malformada: PoliticaEntradaMalformada.Anular), TipoColuna.Texto, sessao));

        // Os quatro documentos e a chave de novo: a política de malformado entra na identidade de todos.
        Assert.Equal(10, sessao.Instancias);
        Assert.Equal(falha.Transformar(cpf, default).Valor, anula.Transformar(cpf, default).Valor);
        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falha.Transformar("x", default)).Codigo);
        Assert.Equal((null, Desfecho.AnuladoMalformado), Tupla(anula.Transformar("x", default)));
    }

    [Fact]
    public void Dentro_da_chave_Pix_vale_a_politica_de_DV_do_dominio_do_documento()
    {
        var dominios = Padroes();
        dominios[0] = new DefinicaoDominio("br.cpf", new RegraTransformacao(new DominioPseudonimo("cpf", 1)) { EntradaInvalida = PoliticaEntradaInvalida.Anular });
        using var sessao = Sessao(dominios);
        var cpf = new GeradorBr(3203).Cpf();
        var comDvErrado = cpf[..9] + ((cpf[9] - '0' + 1) % 10) + cpf[10];
        var pix = Fabrica("br.chave-pix");

        var transformador = pix.Criar(new ContextoCriacao(Regra(pix, invalida: PoliticaEntradaInvalida.Falhar), TipoColuna.Texto, sessao));

        Assert.Equal((null, Desfecho.AnuladoDvInvalido), Tupla(transformador.Transformar(comDvErrado, default)));
    }

    [Fact]
    public void Dominio_referenciado_em_outro_dominio_leva_a_chave_para_o_universo_dele()
    {
        var dominios = Padroes();
        dominios.Add(new DefinicaoDominio("br.cpf", new RegraTransformacao(new DominioPseudonimo("cpf-loja", 1))));
        using var sessao = Sessao(dominios);
        var cpf = new GeradorBr(3204).Cpf();
        var pix = Fabrica("br.chave-pix");

        var daLoja = pix.Criar(new ContextoCriacao(Regra(pix, new() { ["dominioCpf"] = "cpf-loja" }), TipoColuna.Texto, sessao));
        var colunaDaLoja = Fabrica("br.cpf").Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("cpf-loja", 1)), TipoColuna.Texto, sessao));
        var padrao = pix.Criar(new ContextoCriacao(Regra(pix), TipoColuna.Texto, sessao));

        Assert.Equal(colunaDaLoja.Transformar(cpf, default).Valor, daLoja.Transformar(cpf, default).Valor);
        Assert.NotEqual(padrao.Transformar(cpf, default).Valor, daLoja.Transformar(cpf, default).Valor);
    }

    [Theory]
    [InlineData("br.chave-pix", "dominioCpf")]
    [InlineData("br.chave-pix", "dominioTelefone")]
    [InlineData("br.chave-dfe", "dominioCnpj")]
    public void Dominio_referenciado_ausente_da_sessao_e_recusado_na_criacao(string nome, string parametro)
    {
        using var sessao = Sessao(Padroes());
        var fabrica = Fabrica(nome);

        var excecao = Assert.Throws<RegraInvalidaException>(
            () => fabrica.Criar(new ContextoCriacao(Regra(fabrica, new() { [parametro] = "inexistente" }), TipoColuna.Texto, sessao)));

        Assert.Equal(parametro, excecao.Parametro);
        Assert.Equal(0, sessao.Instancias);
    }

    [Fact]
    public void Sessao_sem_a_tabela_de_dominios_nao_cria_as_chaves()
    {
        using var sessao = new SessaoTransformacao(_chave, Ambiente);
        var pix = Fabrica("br.chave-pix");

        Assert.Empty(sessao.Dominios);
        Assert.Equal("dominioCpf", Assert.Throws<RegraInvalidaException>(() => pix.Criar(new ContextoCriacao(Regra(pix), TipoColuna.Texto, sessao))).Parametro);
    }

    [Theory]
    [InlineData("br.cnpj", 1)]
    [InlineData("email", 1)]
    [InlineData("br.cpf", 2)]
    public void Dominio_referenciado_de_outro_transformador_ou_de_outra_versao_e_recusado(string transformador, int versao)
    {
        var dominios = Padroes();
        dominios[0] = new DefinicaoDominio(transformador, new RegraTransformacao(new DominioPseudonimo("cpf", versao)));
        using var sessao = Sessao(dominios);

        foreach (var nome in new[] { "br.chave-pix", "br.chave-dfe" })
        {
            var fabrica = Fabrica(nome);
            var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(new ContextoCriacao(Regra(fabrica), TipoColuna.Texto, sessao)));
            Assert.Equal("dominioCpf", excecao.Parametro);
        }
    }

    [Fact]
    public void A_tabela_de_dominios_recusa_nulo_e_nome_repetido()
    {
        var cpf = new RegraTransformacao(new DominioPseudonimo("cpf", 1));

        Assert.Throws<ArgumentNullException>(() => new SessaoTransformacao(_chave, Ambiente) { Dominios = null! });
        Assert.Throws<ArgumentNullException>(() => new SessaoTransformacao(_chave, Ambiente) { Dominios = [null!] });
        Assert.Throws<ArgumentException>(() => new SessaoTransformacao(_chave, Ambiente) { Dominios = [new("br.cpf", cpf), new("br.cpf", cpf)] });
        Assert.Throws<ArgumentNullException>(() => new DefinicaoDominio("br.cpf", null!));
        Assert.Throws<ArgumentNullException>(() => new DefinicaoDominio(null!, cpf));
        Assert.Throws<ArgumentException>(() => new DefinicaoDominio("Br.Cpf", cpf));

        var tabela = new List<DefinicaoDominio> { new("br.cpf", cpf), new("email", new RegraTransformacao(new DominioPseudonimo("email", 1))) };
        using var sessao = new SessaoTransformacao(_chave, Ambiente) { Dominios = tabela };
        Assert.Equal(tabela, sessao.Dominios);
        Assert.Throws<NotSupportedException>(() => ((ICollection<DefinicaoDominio>)sessao.Dominios).Add(tabela[0]));

        var definicao = new DefinicaoDominio("br.cpf", cpf);
        Assert.Equal(("br.cpf", "cpf"), (definicao.Transformador, definicao.Nome));
        Assert.Same(cpf, definicao.Regra);
    }

    [Fact]
    public void A_chave_de_DFe_devolve_as_ocorrencias_por_dentro_e_so_o_desfecho_pelo_contrato()
    {
        using var sessao = Sessao(Padroes());
        var dfe = Fabrica("br.chave-dfe");
        var transformador = dfe.Criar(new ContextoCriacao(Regra(dfe), TipoColuna.Texto, sessao));
        var chaveDfe = new GeradorBr(3205).ChaveDfe(55);

        var interno = Assert.IsType<Transformadores.Embutidos.TransformadorChaveDfe>(transformador).Transformar(chaveDfe, out _);
        var publico = transformador.Transformar(chaveDfe, default);

        Assert.Equal((interno.Valor, interno.Desfecho), (publico.Valor, publico.Desfecho));
        Assert.Equal((null, Desfecho.Nulo), Tupla(transformador.Transformar(null, default)));
        Assert.Equal(("", Desfecho.Vazio), Tupla(transformador.Transformar("", default)));
    }

    [Fact]
    public void A_politica_de_cDV_separa_as_instancias_da_chave_de_DFe()
    {
        using var sessao = Sessao(Padroes());
        var dfe = Fabrica("br.chave-dfe");
        var chaveDfe = new GeradorBr(3206).ChaveDfe(55);
        var comCdvErrado = chaveDfe[..43] + (char)('0' + ((chaveDfe[43] - '0' + 1) % 10));

        var preserva = dfe.Criar(new ContextoCriacao(Regra(dfe), TipoColuna.Texto, sessao));
        var falha = dfe.Criar(new ContextoCriacao(Regra(dfe, invalida: PoliticaEntradaInvalida.Falhar), TipoColuna.Texto, sessao));

        Assert.Equal(Desfecho.DvInvalidoPreservado, preserva.Transformar(comCdvErrado, default).Desfecho);
        Assert.Equal("INC1004", Assert.Throws<IncognitoException>(() => falha.Transformar(comCdvErrado, default)).Codigo);
    }

    [Fact]
    public void A_chave_Pix_devolve_NULL_e_o_vazio_como_vieram()
    {
        using var sessao = Sessao(Padroes());
        var pix = Fabrica("br.chave-pix").Criar(new ContextoCriacao(Regra(Fabrica("br.chave-pix")), TipoColuna.Texto, sessao));

        Assert.Equal((null, Desfecho.Nulo), Tupla(pix.Transformar(null, default)));
        Assert.Equal(("", Desfecho.Vazio), Tupla(pix.Transformar("", default)));
        Assert.Equal(("   ", Desfecho.Vazio), Tupla(pix.Transformar("   ", default)));
    }

    private static (object? Valor, Desfecho Desfecho) Tupla(ResultadoTransformacao resultado) => (resultado.Valor, resultado.Desfecho);

    private static FabricaTransformador Fabrica(string nome) => TransformadoresEmbutidos.Fabricas.Single(fabrica => fabrica.Nome == nome);

    // Os domínios padrão dos quatro documentos, com as políticas padrão.
    private static List<DefinicaoDominio> Padroes() =>
    [
        new("br.cpf", new RegraTransformacao(new DominioPseudonimo("cpf", 1))),
        new("br.cnpj", new RegraTransformacao(new DominioPseudonimo("cnpj", 1))),
        new("email", new RegraTransformacao(new DominioPseudonimo("email", 1))),
        new("br.telefone", new RegraTransformacao(new DominioPseudonimo("telefone", 1))),
    ];

    private static RegraTransformacao Regra(
        FabricaTransformador fabrica,
        Dictionary<string, string>? parametros = null,
        PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Preservar,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        bool? justificativa = false) =>
        new(new DominioPseudonimo(fabrica.DominioPadrao, 1))
        {
            Parametros = parametros ?? [],
            EntradaInvalida = invalida,
            EntradaMalformada = malformada,
            TemDerivarDe = false,
            TemJustificativa = justificativa,
        };

    private SessaoTransformacao Sessao(List<DefinicaoDominio> dominios) => new(_chave, Ambiente) { Dominios = dominios };
}
