using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// As fábricas embutidas: nome, versão, domínio padrão, injetividade e tipos; parâmetros recusados fora da lista e da
/// forma; <c>derivarDe</c>; comprimento mínimo da coluna; NULL; valor fora do tipo sem o valor na exceção; políticas
/// chegando ao pseudonimizador; instâncias compartilhadas na sessão e o descarte; <c>texto.substituir</c>. Documentos
/// sorteados na hora pelo <see cref="GeradorBr"/>.
/// </summary>
public sealed class FabricasEmbutidasTestes : IDisposable
{
    private const string Ambiente = "homologacao";
    private const string Sentinela = "SENTINELA-987.654.321-00";

    private readonly ChaveIncognito _chave = ChaveIncognito.Gerar();
    private readonly SessaoTransformacao _sessao;

    public FabricasEmbutidasTestes() => _sessao = new SessaoTransformacao(_chave, Ambiente);

    public static TheoryData<string> Nomes => [.. TransformadoresEmbutidos.Fabricas.Select(fabrica => fabrica.Nome)];

    public void Dispose()
    {
        _sessao.Dispose();
        _chave.Dispose();
    }

    [Fact]
    public void As_embutidas_sao_as_seis_da_versao_1_com_os_dominios_padrao()
    {
        var esperadas = new (string Nome, string Dominio, Injetividade Injetividade, TipoColuna[] Tipos)[]
        {
            ("br.cpf", "cpf", Injetividade.Garantida, [TipoColuna.Texto, TipoColuna.Inteiro]),
            ("br.cnpj", "cnpj", Injetividade.Garantida, [TipoColuna.Texto, TipoColuna.Inteiro]),
            ("br.telefone", "telefone", Injetividade.Garantida, [TipoColuna.Texto, TipoColuna.Inteiro]),
            ("email", "email", Injetividade.Probabilistica, [TipoColuna.Texto]),
            ("br.cep", "cep", Injetividade.Nenhuma, [TipoColuna.Texto, TipoColuna.Inteiro]),
            ("texto.substituir", "texto.substituir", Injetividade.Nenhuma, [TipoColuna.Texto]),
        };

        Assert.Equal(esperadas.Length, TransformadoresEmbutidos.Fabricas.Count);
        foreach (var (esperada, fabrica) in esperadas.Zip(TransformadoresEmbutidos.Fabricas))
        {
            Assert.Equal((esperada.Nome, 1, esperada.Dominio, esperada.Injetividade), (fabrica.Nome, fabrica.Versao, fabrica.DominioPadrao, fabrica.Injetividade));
            Assert.True(fabrica.TiposSuportados.SetEquals(esperada.Tipos), $"{fabrica.Nome}: tipos diferentes.");
        }

        Assert.Throws<NotSupportedException>(() => ((IList<FabricaTransformador>)TransformadoresEmbutidos.Fabricas)[0] = null!);
    }

    [Theory]
    [MemberData(nameof(Nomes))]
    public void Parametro_desconhecido_e_recusado_pela_fabrica_com_o_nome_do_parametro(string nome)
    {
        var fabrica = Fabrica(nome);
        var regra = Regra(fabrica, parametros: new() { ["precisao"] = Sentinela });

        var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(regra));

        Assert.Equal(("INC1005", "precisao"), (excecao.Codigo, excecao.Parametro));
        Assert.Contains(nome == "br.cnpj" ? "aceita permutarOrdem" : "não tem parâmetros", excecao.Message, StringComparison.Ordinal);
        Assert.Equal("a", Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, parametros: new() { ["b"] = "1", ["a"] = "1" }))).Parametro);
        Assert.Throws<ArgumentNullException>(() => fabrica.Validar(null!));
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.Ordinal);
        Assert.Throws<RegraInvalidaException>(() => fabrica.Normalizar(regra));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("sim")]
    [InlineData("")]
    [InlineData(" true")]
    public void PermutarOrdem_so_aceita_true_ou_false(string valor)
    {
        var fabrica = Fabrica("br.cnpj");

        var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, parametros: new() { ["permutarOrdem"] = valor })));

        Assert.Equal("permutarOrdem", excecao.Parametro);
        Assert.Contains("true ou false", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizar_preenche_os_padroes_e_permutarOrdem_e_parametro_de_identidade()
    {
        var cnpj = Fabrica("br.cnpj");

        Assert.Equal("false", cnpj.Normalizar(Regra(cnpj))["permutarOrdem"]);
        Assert.Equal("true", cnpj.Normalizar(Regra(cnpj, parametros: new() { ["permutarOrdem"] = "true" }))["permutarOrdem"]);
        Assert.Equal("permutarOrdem", Assert.Single(cnpj.ParametrosDeIdentidade));
        Assert.Empty(Fabrica("br.cpf").Normalizar(Regra(Fabrica("br.cpf"))));
        Assert.All(TransformadoresEmbutidos.Fabricas.Where(f => f.Nome != "br.cnpj"), fabrica => Assert.Empty(fabrica.ParametrosDeIdentidade));
    }

    [Theory]
    [InlineData("br.cpf")]
    [InlineData("br.cnpj")]
    [InlineData("br.telefone")]
    [InlineData("email")]
    [InlineData("texto.substituir")]
    public void DerivarDe_e_recusado_onde_nao_se_usa_e_ignorado_na_declaracao_do_dominio(string nome)
    {
        var fabrica = Fabrica(nome);

        Assert.Throws<RegraInvalidaException>(() => fabrica.Validar(Regra(fabrica, temDerivarDe: true)));
        fabrica.Validar(Regra(fabrica, temDerivarDe: false));
        fabrica.Validar(Regra(fabrica, temDerivarDe: null));
    }

    [Fact]
    public void O_CEP_exige_derivarDe_na_coluna_mas_nao_na_declaracao_do_dominio()
    {
        var cep = Fabrica("br.cep");

        Assert.Throws<RegraInvalidaException>(() => cep.Validar(Regra(cep, temDerivarDe: false)));
        cep.Validar(Regra(cep, temDerivarDe: true));
        cep.Validar(Regra(cep, temDerivarDe: null));
        Assert.Throws<RegraInvalidaException>(() => cep.Criar(new ContextoCriacao(Regra(cep, temDerivarDe: true), TipoColuna.Texto, _sessao)));
        Assert.Throws<RegraInvalidaException>(() => cep.Criar(new ContextoCriacao(Regra(cep, temDerivarDe: true), TipoColuna.Texto, _sessao) { TipoReferencia = TipoColuna.Uuid }));
    }

    [Theory]
    [InlineData("br.cpf", 11, "CPF")]
    [InlineData("br.cnpj", 14, "CNPJ")]
    [InlineData("email", 32, "e-mail")]
    [InlineData("br.cep", 8, "CEP")]
    public void Coluna_de_texto_que_nao_comporta_o_pseudonimo_e_recusada(string nome, int minimo, string documento)
    {
        var fabrica = Fabrica(nome);
        var regra = Regra(fabrica, temDerivarDe: nome == "br.cep" ? true : null);

        var excecao = Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(Contexto(regra, TipoColuna.Texto, minimo - 1)));
        Assert.Contains($"o pseudônimo de {documento} tem {minimo} caracteres", excecao.Message, StringComparison.Ordinal);
        fabrica.Criar(Contexto(regra, TipoColuna.Texto, minimo));
        if (fabrica.TiposSuportados.Contains(TipoColuna.Inteiro))
        {
            fabrica.Criar(Contexto(regra, TipoColuna.Inteiro, 1));
        }
    }

    [Theory]
    [MemberData(nameof(Nomes))]
    public void Tipo_nao_suportado_e_recusado_na_criacao(string nome)
    {
        var fabrica = Fabrica(nome);

        Assert.Throws<RegraInvalidaException>(() => fabrica.Criar(Contexto(Regra(fabrica, temDerivarDe: nome == "br.cep"), TipoColuna.Uuid, null)));
    }

    [Theory]
    [MemberData(nameof(Nomes))]
    public void NULL_sai_NULL_e_valor_fora_do_tipo_lanca_sem_o_valor(string nome)
    {
        var fabrica = Fabrica(nome);
        var transformador = fabrica.Criar(Contexto(Regra(fabrica, temDerivarDe: nome == "br.cep"), TipoColuna.Texto, null));
        var nulo = transformador.Transformar(null, new ContextoTransformacao("entidade"));

        Assert.Equal((null, Desfecho.Nulo), (nulo.Valor, nulo.Desfecho));
        Assert.False(transformador.TransformaNulo);
        Assert.Equal((fabrica.Nome, fabrica.Versao, fabrica.Injetividade), (transformador.Nome, transformador.Versao, transformador.Injetividade));
        var excecao = Assert.Throws<ArgumentException>(() => transformador.Transformar(12345678909L, new ContextoTransformacao("entidade")));
        Assert.DoesNotContain("12345678909", excecao.ToString(), StringComparison.Ordinal);
        Assert.Contains("representação do tipo Texto", excecao.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => transformador.Transformar(Guid.Empty, default));
    }

    [Fact]
    public void A_coluna_inteira_recebe_long_e_devolve_long()
    {
        var cpf = Fabrica("br.cpf").Criar(Contexto(Regra(Fabrica("br.cpf")), TipoColuna.Inteiro, null));
        var numero = long.Parse(new GeradorBr(3101).Cpf(), System.Globalization.CultureInfo.InvariantCulture);

        var resultado = cpf.Transformar(numero, default);

        Assert.IsType<long>(resultado.Valor);
        Assert.Equal(Desfecho.Pseudonimizado, resultado.Desfecho);
        Assert.Throws<ArgumentException>(() => cpf.Transformar(numero.ToString(System.Globalization.CultureInfo.InvariantCulture), default));
        Assert.Throws<ArgumentException>(() => cpf.Transformar((int)1, default));
    }

    [Fact]
    public void As_politicas_da_regra_chegam_ao_pseudonimizador()
    {
        var fabrica = Fabrica("br.cpf");
        var cpf = new GeradorBr(3102).Cpf();
        var comDvErrado = cpf[..9] + ((cpf[9] - '0' + 1) % 10) + cpf[10];

        var anula = fabrica.Criar(Contexto(Regra(fabrica, invalida: PoliticaEntradaInvalida.Anular), TipoColuna.Texto, null));
        var falha = fabrica.Criar(Contexto(Regra(fabrica, invalida: PoliticaEntradaInvalida.Falhar), TipoColuna.Texto, null));
        var preserva = fabrica.Criar(Contexto(Regra(fabrica), TipoColuna.Texto, null));
        var malformadoAnula = fabrica.Criar(Contexto(Regra(fabrica, malformada: PoliticaEntradaMalformada.Anular), TipoColuna.Texto, null));
        var malformadoCopia = fabrica.Criar(Contexto(Regra(fabrica, malformada: PoliticaEntradaMalformada.Preservar), TipoColuna.Texto, null));

        Assert.Equal((null, Desfecho.AnuladoDvInvalido), Tupla(anula.Transformar(comDvErrado, default)));
        Assert.Equal("INC1004", Assert.Throws<IncognitoException>(() => falha.Transformar(comDvErrado, default)).Codigo);
        Assert.Equal(Desfecho.DvInvalidoPreservado, preserva.Transformar(comDvErrado, default).Desfecho);
        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => preserva.Transformar("12", default)).Codigo);
        Assert.Equal((null, Desfecho.AnuladoMalformado), Tupla(malformadoAnula.Transformar("12", default)));
        Assert.Equal(("12", Desfecho.CopiadoEmClaro), Tupla(malformadoCopia.Transformar("12", default)));
    }

    [Fact]
    public void Mesma_identidade_usa_a_mesma_instancia_e_identidades_diferentes_nao()
    {
        using var sessao = new SessaoTransformacao(_chave, Ambiente);
        var cnpj = Fabrica("br.cnpj");
        var valor = new GeradorBr(3103).Cnpj();

        var padrao = cnpj.Criar(new ContextoCriacao(Regra(cnpj), TipoColuna.Texto, sessao));
        Assert.Equal(1, sessao.Instancias);

        // O padrão escrito é o mesmo universo, em texto ou em número.
        var escrito = cnpj.Criar(new ContextoCriacao(Regra(cnpj, parametros: new() { ["permutarOrdem"] = "false" }), TipoColuna.Inteiro, sessao));
        Assert.Equal(1, sessao.Instancias);

        var comOrdem = cnpj.Criar(new ContextoCriacao(Regra(cnpj, parametros: new() { ["permutarOrdem"] = "true" }), TipoColuna.Texto, sessao));
        var outroDominio = cnpj.Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("cnpj-fornecedor", 1)), TipoColuna.Texto, sessao));
        var outraPolitica = cnpj.Criar(new ContextoCriacao(Regra(cnpj, invalida: PoliticaEntradaInvalida.Anular), TipoColuna.Texto, sessao));
        Assert.Equal(4, sessao.Instancias);

        Assert.NotEqual(padrao.Transformar(valor, default).Valor, outroDominio.Transformar(valor, default).Valor);
        Assert.Equal(padrao.Transformar(valor, default).Valor, outraPolitica.Transformar(valor, default).Valor);
        Assert.Equal(Desfecho.Pseudonimizado, comOrdem.Transformar(valor, default).Desfecho);
        Assert.Equal(Desfecho.Pseudonimizado, escrito.Transformar(long.Parse(valor, System.Globalization.CultureInfo.InvariantCulture), default).Desfecho);
    }

    [Theory]
    [InlineData("br.telefone")]
    [InlineData("email")]
    [InlineData("br.cep")]
    public void A_politica_de_DV_ignorada_nao_separa_instancias(string nome)
    {
        using var sessao = new SessaoTransformacao(_chave, Ambiente);
        var fabrica = Fabrica(nome);
        var derivar = nome == "br.cep" ? true : (bool?)null;

        fabrica.Criar(new ContextoCriacao(Regra(fabrica, temDerivarDe: derivar), TipoColuna.Texto, sessao) { TipoReferencia = derivar == true ? TipoColuna.Texto : null });
        fabrica.Criar(new ContextoCriacao(Regra(fabrica, temDerivarDe: derivar, invalida: PoliticaEntradaInvalida.Falhar), TipoColuna.Texto, sessao) { TipoReferencia = derivar == true ? TipoColuna.Texto : null });
        Assert.Equal(1, sessao.Instancias);

        fabrica.Criar(new ContextoCriacao(Regra(fabrica, temDerivarDe: derivar, malformada: PoliticaEntradaMalformada.Anular), TipoColuna.Texto, sessao) { TipoReferencia = derivar == true ? TipoColuna.Texto : null });
        Assert.Equal(2, sessao.Instancias);
    }

    [Fact]
    public void Depois_do_descarte_da_sessao_nada_e_criado_nem_transformado()
    {
        using var chave = ChaveIncognito.Gerar();
        var sessao = new SessaoTransformacao(chave, Ambiente);
        var cpf = Fabrica("br.cpf");
        var transformador = cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, sessao));
        var valor = new GeradorBr(3104).Cpf();
        _ = transformador.Transformar(valor, default);

        sessao.Dispose();
        sessao.Dispose();

        Assert.Throws<ObjectDisposedException>(() => transformador.Transformar(valor, default));
        Assert.Throws<ObjectDisposedException>(() => cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, sessao)));
    }

    [Fact]
    public void A_sessao_descarta_as_instancias_na_ordem_inversa_uma_vez_e_continua_depois_de_uma_falha()
    {
        using var chave = ChaveIncognito.Gerar();
        var sessao = new SessaoTransformacao(chave, Ambiente);
        var ordem = new List<string>();
        sessao.Obter("a", (_, _) => new Descartavel(() => ordem.Add("a")));
        sessao.Obter("b", (_, _) => new Descartavel(() => throw new InvalidOperationException("primeira")));
        sessao.Obter("c", (_, _) => new Descartavel(() => ordem.Add("c")));
        sessao.Obter("d", (_, _) => new Descartavel(() => throw new InvalidOperationException("segunda")));

        Assert.Equal("segunda", Assert.Throws<InvalidOperationException>(sessao.Dispose).Message);
        sessao.Dispose();
        Assert.Equal(["c", "a"], ordem);
    }

    [Fact]
    public void A_sessao_recusa_entregar_instancia_depois_do_descarte_da_chave()
    {
        var chave = ChaveIncognito.Gerar();
        using var sessao = new SessaoTransformacao(chave, Ambiente);
        var cpf = Fabrica("br.cpf");
        cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, sessao));

        chave.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, sessao)));
    }

    [Fact]
    public void Criar_e_transformar_em_varias_threads_usa_uma_instancia_so()
    {
        var cpf = Fabrica("br.cpf");
        var gerador = new GeradorBr(3105);
        var valores = Enumerable.Range(0, 2_000).Select(_ => gerador.Cpf()).ToArray();
        object?[] esperados;
        using (var referencia = new SessaoTransformacao(_chave, Ambiente))
        {
            var transformador = cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, referencia));
            esperados = [.. valores.Select(valor => transformador.Transformar(valor, default).Valor)];
        }

        using var sessao = new SessaoTransformacao(_chave, Ambiente);
        var obtidos = new object?[valores.Length];
        Parallel.For(0, valores.Length, i => obtidos[i] = cpf.Criar(new ContextoCriacao(Regra(cpf), TipoColuna.Texto, sessao)).Transformar(valores[i], default).Valor);

        Assert.Equal(esperados, obtidos);
        Assert.Equal(1, sessao.Instancias);
    }

    [Theory]
    [InlineData(null, "Texto removido pelo Incognito.")]
    [InlineData(1, "T")]
    [InlineData(29, "Texto removido pelo Incognito")]
    [InlineData(30, "Texto removido pelo Incognito.")]
    [InlineData(31, "Texto removido pelo Incognito.")]
    [InlineData(4000, "Texto removido pelo Incognito.")]
    public void Texto_substituir_respeita_o_comprimento_maximo_recebido_no_contexto(int? comprimento, string esperado)
    {
        var fabrica = Fabrica("texto.substituir");
        var transformador = fabrica.Criar(Contexto(Regra(fabrica), TipoColuna.Texto, comprimento));

        var resultado = transformador.Transformar(Sentinela + new string('x', 5_000), default);

        Assert.Equal((esperado, Desfecho.Substituido), Tupla(resultado));
        Assert.True(comprimento is null || ((string)resultado.Valor!).Length <= comprimento);
        Assert.True(((string)resultado.Valor!).All(char.IsAscii), "O texto fixo precisa ser ASCII.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    public void Texto_substituir_devolve_o_vazio_como_veio(string vazio)
    {
        var fabrica = Fabrica("texto.substituir");
        var transformador = fabrica.Criar(Contexto(Regra(fabrica), TipoColuna.Texto, 10));

        Assert.Equal((vazio, Desfecho.Vazio), Tupla(transformador.Transformar(vazio, default)));
        Assert.Equal(Desfecho.Substituido, transformador.Transformar("\t", default).Desfecho);
        Assert.Equal(Desfecho.Substituido, transformador.Transformar(" a ", default).Desfecho);
    }

    [Fact]
    public void Texto_substituir_nao_depende_da_entrada_nem_da_chave()
    {
        using var outraChave = ChaveIncognito.Gerar();
        using var outraSessao = new SessaoTransformacao(outraChave, "desenvolvimento");
        var fabrica = Fabrica("texto.substituir");
        var umTransformador = fabrica.Criar(Contexto(Regra(fabrica), TipoColuna.Texto, 20));
        var outroTransformador = fabrica.Criar(new ContextoCriacao(Regra(fabrica), TipoColuna.Texto, outraSessao) { ComprimentoMaximo = 20 });

        Assert.Equal(umTransformador.Transformar("a", default).Valor, umTransformador.Transformar(Sentinela, default).Valor);
        Assert.Equal(umTransformador.Transformar("a", default).Valor, outroTransformador.Transformar("b", default).Valor);
    }

    [Fact]
    public void A_entidade_do_CEP_aceita_texto_inteiro_e_NULL_como_vazio()
    {
        var fabrica = Fabrica("br.cep");
        var regra = Regra(fabrica, temDerivarDe: true);
        var porTexto = fabrica.Criar(new ContextoCriacao(regra, TipoColuna.Texto, _sessao) { TipoReferencia = TipoColuna.Texto });
        var porNumero = fabrica.Criar(new ContextoCriacao(regra, TipoColuna.Texto, _sessao) { TipoReferencia = TipoColuna.Inteiro });
        var cep = new GeradorBr(3106).Cep();

        Assert.Equal(porTexto.Transformar(cep, new ContextoTransformacao("123")).Valor, porNumero.Transformar(cep, new ContextoTransformacao(123L)).Valor);
        Assert.Equal(porTexto.Transformar(cep, new ContextoTransformacao("")).Valor, porTexto.Transformar(cep, new ContextoTransformacao(null)).Valor);
        Assert.Throws<ArgumentException>(() => porTexto.Transformar(cep, new ContextoTransformacao(123L)));
        Assert.Throws<ArgumentException>(() => porNumero.Transformar(cep, new ContextoTransformacao("123")));
    }

    private static (object? Valor, Desfecho Desfecho) Tupla(ResultadoTransformacao resultado) => (resultado.Valor, resultado.Desfecho);

    private static FabricaTransformador Fabrica(string nome) => TransformadoresEmbutidos.Fabricas.Single(fabrica => fabrica.Nome == nome);

    private static RegraTransformacao Regra(
        FabricaTransformador fabrica,
        Dictionary<string, string>? parametros = null,
        bool? temDerivarDe = null,
        PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Preservar,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar) =>
        new(new DominioPseudonimo(fabrica.DominioPadrao, 1))
        {
            Parametros = parametros ?? [],
            TemDerivarDe = temDerivarDe,
            EntradaInvalida = invalida,
            EntradaMalformada = malformada,
        };

    private ContextoCriacao Contexto(RegraTransformacao regra, TipoColuna tipo, int? comprimento) =>
        new(regra, tipo, _sessao) { ComprimentoMaximo = comprimento, TipoReferencia = regra.TemDerivarDe == true ? TipoColuna.Texto : null };

    private sealed class Descartavel(Action aoDescartar) : IDisposable
    {
        public void Dispose() => aoDescartar();
    }
}
