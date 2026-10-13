using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Fachada <see cref="Pseudonimizador"/>: cada método dá o mesmo que o pseudonimizador interno do domínio padrão, com as
/// políticas fixas (malformado lança <c>INC1003</c> sem o valor; DV errado mantido); chave Pix e chave de DF-e usam o
/// CPF e o CNPJ da própria fachada; o ambiente separa; nulo, vazio, descarte e uso concorrente. Documentos sorteados na
/// hora; a chave é a pública do vetor de derivação.
/// </summary>
public sealed class PseudonimizadorTestes : IDisposable
{
    private const string Ambiente = "homologacao";

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> AmbientesForaDaRegra => ["", "Homologacao", "homolog acao", "-homologacao", "homologação", new string('a', 65)];

    public static TheoryData<string> Metodos => ["Cpf", "Cnpj", "Telefone", "Email", "ChaveDfe", "ChavePix"];

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Cada_metodo_da_o_mesmo_que_o_pseudonimizador_do_dominio_padrao()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, Ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, Ambiente, false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var telefone = new PseudonimizadorTelefone(_chave, "telefone", 1, Ambiente, PoliticaEntradaMalformada.Falhar);
        using var email = new PseudonimizadorEmail(_chave, "email", 1, Ambiente, PoliticaEntradaMalformada.Falhar);
        using var pix = new PseudonimizadorChavePix(_chave, "chave-pix", 1, Ambiente, cpf, cnpj, email, telefone, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = new PseudonimizadorChaveDfe(_chave, 1, Ambiente, cpf, cnpj, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(2401);
        for (var vez = 0; vez < 200; vez++)
        {
            var umCpf = ComDvDeslocado(gerador.Cpf(), vez % 4 == 0);
            var umCnpj = ComDvDeslocado(gerador.Cnpj(alfanumerico: vez % 3 == 0), vez % 4 == 1);
            var umTelefone = gerador.Telefone(celular: vez % 2 == 0, comPais: vez % 3 == 0, formatado: vez % 5 == 0);
            var umEmail = gerador.Email();
            var umaChave = gerador.ChaveDfe(55 + (vez % 4 == 0 ? 2 : 0), alfanumerico: vez % 3 == 0);
            var umaAleatoria = gerador.ChavePix(Geracao.TipoChavePix.Aleatoria);

            Assert.True(pseudonimizador.Cpf(umCpf) == Saida(cpf, umCpf) && pseudonimizador.Cpf(umCpf) != umCpf, "O CPF difere do domínio padrão.");
            Assert.True(pseudonimizador.Cnpj(umCnpj) != umCnpj, "O CNPJ não mudou.");
            Assert.True(pseudonimizador.Cnpj(umCnpj) == Saida(cnpj, umCnpj), "O CNPJ difere do domínio padrão.");
            Assert.True(pseudonimizador.Telefone(umTelefone) == Saida(telefone, umTelefone), "O telefone difere do domínio padrão.");
            Assert.True(pseudonimizador.Email(umEmail) == Saida(email, umEmail), "O e-mail difere do domínio padrão.");
            Assert.True(pseudonimizador.ChaveDfe(umaChave) == Saida(chaveDfe, umaChave), "A chave de DF-e difere do domínio padrão.");
            Assert.True(pseudonimizador.ChavePix(umaAleatoria) == Saida(pix, umaAleatoria), "A chave Pix difere do domínio padrão.");
        }
    }

    [Fact]
    public void Chave_Pix_e_chave_de_DF_e_usam_o_CPF_e_o_CNPJ_da_propria_fachada()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var gerador = new GeradorBr(2402);
        for (var vez = 0; vez < 100; vez++)
        {
            var cpf = gerador.Cpf();
            var cnpj = gerador.Cnpj(alfanumerico: vez % 2 == 0);
            var chave = gerador.ChaveDfe(57, alfanumerico: vez % 2 == 0);

            Assert.True(pseudonimizador.ChavePix(cpf) == pseudonimizador.Cpf(cpf), "A chave Pix de CPF difere do CPF.");
            Assert.True(pseudonimizador.ChavePix(cnpj) == pseudonimizador.Cnpj(cnpj), "A chave Pix de CNPJ difere do CNPJ.");
            Assert.True(pseudonimizador.ChaveDfe(chave)[6..20] == pseudonimizador.Cnpj(chave[6..20]), "O emitente da chave difere do CNPJ.");
        }
    }

    [Fact]
    public void Ambientes_diferentes_dao_pseudonimos_diferentes_e_o_mesmo_ambiente_o_mesmo()
    {
        using var homologacao = new Pseudonimizador(_chave, Ambiente);
        using var outraHomologacao = new Pseudonimizador(_chave, Ambiente);
        using var desenvolvimento = new Pseudonimizador(_chave, "desenvolvimento");
        var cpf = new GeradorBr(2403).Cpf();

        Assert.True(homologacao.Cpf(cpf) == outraHomologacao.Cpf(cpf), "O mesmo ambiente deu pseudônimos diferentes.");
        Assert.True(homologacao.Cpf(cpf) != desenvolvimento.Cpf(cpf), "Ambientes diferentes deram o mesmo pseudônimo.");
        Assert.Equal(Ambiente, homologacao.Ambiente);
        Assert.Equal("desenvolvimento", desenvolvimento.Ambiente);
    }

    [Theory]
    [MemberData(nameof(AmbientesForaDaRegra))]
    public void Ambiente_fora_da_regra_dos_nomes_e_recusado_sem_o_valor(string ambiente)
    {
        var excecao = Assert.Throws<ArgumentException>(nameof(ambiente), () => new Pseudonimizador(_chave, ambiente));
        Assert.True(ambiente.Length == 0 || !excecao.Message.Contains(ambiente, StringComparison.Ordinal), "A mensagem traz o ambiente.");
    }

    [Fact]
    public void Construtor_recusa_chave_ou_ambiente_nulos_e_chave_descartada()
    {
        Assert.Throws<ArgumentNullException>("chave", () => new Pseudonimizador(null!, Ambiente));
        Assert.Throws<ArgumentNullException>("ambiente", () => new Pseudonimizador(_chave, null!));
        var descartada = ChaveIncognito.Gerar();
        descartada.Dispose();
        Assert.Throws<ObjectDisposedException>(() => new Pseudonimizador(descartada, Ambiente));
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public void Malformado_lanca_INC1003_sem_o_valor(string metodo)
    {
        // Valores sentinela fora da forma de cada tipo: nenhum trecho deles aparece na exceção.
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var sentinela = metodo switch
        {
            "Cpf" => "SENTINELA-CPF-7319",
            "Cnpj" => "SENTINELA-CNPJ-7319",
            "Telefone" => "SENTINELA-FONE-7319",
            "Email" => "SENTINELA-EMAIL-7319",
            "ChaveDfe" => "SENTINELA-CHAVE-DE-ACESSO-7319",
            _ => "SENTINELA PIX 7319",
        };

        var excecao = Assert.Throws<IncognitoException>(() => Chamar(pseudonimizador, metodo, sentinela));

        Assert.Equal("INC1003", excecao.Codigo);

        // A mensagem é a da fachada: sem política nem coluna, que quem usa a fachada não configura.
        Assert.DoesNotContain("entradaMalformada", excecao.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("coluna", excecao.Message, StringComparison.Ordinal);
        var texto = excecao.ToString() + excecao.InnerException + string.Join(' ', excecao.Data.Values.Cast<object>());
        Assert.DoesNotContain("SENTINELA", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("7319", texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ChaveDfe", "35260973197319731973199900173197319173197319")]
    [InlineData("ChaveDfe", "CTe35260973197319731973195500173197319173197319")]
    [InlineData("ChaveDfe", "35260973197319731973195599573197319173197319")]
    [InlineData("ChaveDfe", "73197319731973197319731973197319731973197319731973")]
    [InlineData("Cnpj", "7319731973197a")]
    [InlineData("Cpf", "731973197319")]
    [InlineData("ChavePix", "+55 (73) 19731-97319-7")]
    public void Malformado_quase_valido_lanca_INC1003_sem_o_valor(string metodo, string valor)
    {
        // Formas que passam das primeiras conferências (modelo fora, prefixo de outro modelo, série sem emissão, NFS-e,
        // minúscula, tamanho): os dígitos marcadores 7319 não aparecem na exceção.
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);

        var excecao = Assert.Throws<IncognitoException>(() => Chamar(pseudonimizador, metodo, valor));

        Assert.Equal("INC1003", excecao.Codigo);
        Assert.Null(excecao.InnerException);
        Assert.DoesNotContain("entradaMalformada", excecao.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("7319", excecao.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DV_errado_e_mantido_pelo_deslocamento_e_continua_errado()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var gerador = new GeradorBr(2404);
        var cpf = gerador.Cpf();
        var cpfErrado = cpf[..9] + ((int.Parse(cpf[9..], System.Globalization.CultureInfo.InvariantCulture) + 1) % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var chave = gerador.ChaveDfe(55);
        var chaveErrada = chave[..43] + (char)('0' + ((chave[43] - '0' + 1) % 10));
        var cnpj = gerador.Cnpj(alfanumerico: false);
        var alfanumerico = gerador.Cnpj(alfanumerico: true);

        Assert.False(ValidadorBr.Cpf(pseudonimizador.Cpf(cpfErrado)));
        Assert.False(ValidadorBr.ChaveDfe(pseudonimizador.ChaveDfe(chaveErrada)));
        Assert.False(ValidadorBr.Cnpj(pseudonimizador.Cnpj(ComDvDeslocado(cnpj, true))));
        Assert.False(ValidadorBr.Cnpj(pseudonimizador.Cnpj(ComDvDeslocado(alfanumerico, true))));
        Assert.True(ValidadorBr.Cpf(pseudonimizador.Cpf(cpf)), "O CPF válido não continuou válido.");
        Assert.True(ValidadorBr.ChaveDfe(pseudonimizador.ChaveDfe(chave)), "A chave válida não continuou válida.");
        Assert.True(ValidadorBr.Cnpj(pseudonimizador.Cnpj(cnpj)) && ValidadorBr.Cnpj(pseudonimizador.Cnpj(alfanumerico)), "O CNPJ válido não continuou válido.");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public void Vazio_volta_como_veio_e_nulo_e_recusado(string metodo)
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        const string emBranco = "   ";

        Assert.Same(string.Empty, Chamar(pseudonimizador, metodo, string.Empty));
        Assert.Same(emBranco, Chamar(pseudonimizador, metodo, emBranco));
        var parametro = metodo switch { "ChaveDfe" => "chaveDeAcesso", "ChavePix" => "chavePix", _ => char.ToLowerInvariant(metodo[0]) + metodo[1..] };
        Assert.Throws<ArgumentNullException>(parametro, () => Chamar(pseudonimizador, metodo, null!));
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public void Descartado_recusa_uso_e_o_descarte_duplo_e_inofensivo(string metodo)
    {
        var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var valor = new GeradorBr(2405).Cpf();
        pseudonimizador.Dispose();
        pseudonimizador.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Chamar(pseudonimizador, metodo, valor));
    }

    [Fact]
    public void Pontos_fixos_prometidos_voltam_como_vieram()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var cpfRepdigito = "111111111" + DvCpfClassico("111111111").ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var cnpjRaizZero = "000000000000" + DvCnpjClassico("000000000000").ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var zerada = new string('0', 44);

        Assert.Equal(cpfRepdigito, pseudonimizador.Cpf(cpfRepdigito));
        Assert.Equal(cnpjRaizZero, pseudonimizador.Cnpj(cnpjRaizZero));
        Assert.Same(zerada, pseudonimizador.ChaveDfe(zerada));
        foreach (var servico in new[] { "08001234567", "0300 123 4567", "0303 123 4567", "+55 0303 1234567", "4004-1234", "30031234", "(00) 0000-0000" })
        {
            Assert.Equal(servico, pseudonimizador.Telefone(servico));
        }

        // Na chave Pix, 11 dígitos colados são sempre CPF; com separador ou +, é telefone.
        foreach (var telefone in new[] { "+55 0303 1234567", "0800 123 4567", "+5508001234567", "(00) 0000-0000", "+0000000000" })
        {
            Assert.Equal(telefone, pseudonimizador.ChavePix(telefone));
        }
    }

    [Fact]
    public void Formas_prometidas_dao_o_mesmo_pseudonimo_na_mesma_forma()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var gerador = new GeradorBr(2407);
        string cpf;
        do
        {
            cpf = gerador.Cpf();
        }
        while (cpf[0] != '0');

        var chave = gerador.ChaveDfe(55);
        var pura = pseudonimizador.ChaveDfe(chave);
        var grupos = string.Join(' ', Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));

        // CPF sem o zero à esquerda volta completo; os espaços à direita voltam, descontado o zero acrescentado.
        Assert.Equal(pseudonimizador.Cpf(cpf), pseudonimizador.Cpf(cpf[1..]));
        Assert.Equal(pseudonimizador.Cpf(cpf) + "  ", pseudonimizador.Cpf(cpf[1..] + "   "));
        Assert.Equal("NFe" + pura, pseudonimizador.ChaveDfe("NFe" + chave));
        Assert.Equal(string.Join(' ', Enumerable.Range(0, 11).Select(i => pura.Substring(i * 4, 4))), pseudonimizador.ChaveDfe(grupos));
        Assert.Equal(pura + "    ", pseudonimizador.ChaveDfe(chave + "    "));
    }

    [Fact]
    public void Chave_Pix_de_email_e_de_telefone_e_chave_de_DF_e_com_emitente_CPF_batem_com_os_outros_metodos()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var gerador = new GeradorBr(2408);
        var fonte = Geracao.SequenciaXoshiro.DaSemente(2408);
        for (var vez = 0; vez < 50; vez++)
        {
            var email = gerador.Email();
            var telefone = gerador.Telefone(celular: true, comPais: true);
            var comCpf = GeradorChaveDfe.Gerar(fonte, new(2601, 55, 920, '1', EmitenteGerado.Cpf, NumeroGerado.Qualquer, CodigoGerado.Qualquer));

            Assert.True(pseudonimizador.ChavePix(email) == pseudonimizador.Email(email), "A chave Pix de e-mail difere do e-mail.");
            Assert.True(pseudonimizador.ChavePix(telefone) == pseudonimizador.Telefone(telefone), "A chave Pix de telefone difere do telefone.");
            Assert.True(pseudonimizador.ChaveDfe(comCpf)[9..20] == pseudonimizador.Cpf(comCpf.Substring(9, 11)), "O emitente CPF da chave difere do CPF.");
        }
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public void Chave_descartada_recusa_uso(string metodo)
    {
        // A chave precisa viver enquanto a fachada for usada: com ela descartada, nada é calculado.
        var chave = ChaveDoVetor();
        using var pseudonimizador = new Pseudonimizador(chave, Ambiente);
        chave.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Chamar(pseudonimizador, metodo, new GeradorBr(2409).Cpf()));
    }

    [Fact]
    public void Descarte_da_fachada_descarta_as_instancias_internas()
    {
        var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var internas = typeof(Pseudonimizador)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(campo => campo.FieldType.Namespace == typeof(PseudonimizadorCpf).Namespace)
            .Select(campo => campo.GetValue(pseudonimizador))
            .ToArray();
        var cpf = new GeradorBr(2410).Cpf();
        Assert.Equal(6, internas.Length);

        pseudonimizador.Dispose();

        // Cada instância interna recusa uso depois do descarte da fachada.
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorCpf)internas.Single(i => i is PseudonimizadorCpf)!).Pseudonimizar(cpf, out _));
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorCnpj)internas.Single(i => i is PseudonimizadorCnpj)!).Pseudonimizar(cpf, out _));
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorTelefone)internas.Single(i => i is PseudonimizadorTelefone)!).Pseudonimizar("11987654321", out _));
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorEmail)internas.Single(i => i is PseudonimizadorEmail)!).Pseudonimizar("a@b.com", out _));
        // Na chave Pix aleatória e na chave zerada, só a própria instância trabalha: o descarte é dela, não das que ela usa.
        var aleatoria = new GeradorBr(2410).ChavePix(Geracao.TipoChavePix.Aleatoria);
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorChavePix)internas.Single(i => i is PseudonimizadorChavePix)!).Pseudonimizar(aleatoria, out _));
        Assert.Throws<ObjectDisposedException>(() => ((PseudonimizadorChaveDfe)internas.Single(i => i is PseudonimizadorChaveDfe)!).Pseudonimizar(new string('0', 44), out _, out _));
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado()
    {
        using var pseudonimizador = new Pseudonimizador(_chave, Ambiente);
        var gerador = new GeradorBr(2406);
        var cpfs = Enumerable.Range(0, 2_000).Select(_ => gerador.Cpf()).ToArray();
        var chaves = Enumerable.Range(0, 2_000).Select(i => gerador.ChaveDfe(i % 2 == 0 ? 55 : 57)).ToArray();
        var esperados = cpfs.Select(pseudonimizador.Cpf).Concat(chaves.Select(pseudonimizador.ChaveDfe)).ToArray();

        var obtidos = new string[esperados.Length];
        Parallel.For(0, obtidos.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            obtidos[i] = i < cpfs.Length ? pseudonimizador.Cpf(cpfs[i]) : pseudonimizador.ChaveDfe(chaves[i - cpfs.Length]));

        Assert.True(esperados.SequenceEqual(obtidos), "O uso concorrente mudou algum resultado.");
    }

    private static string ComDvDeslocado(string documento, bool deslocar) =>
        !deslocar ? documento : documento[..^2] + ((int.Parse(documento[^2..], System.Globalization.CultureInfo.InvariantCulture) + 1) % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    private static string Chamar(Pseudonimizador pseudonimizador, string metodo, string valor) => metodo switch
    {
        "Cpf" => pseudonimizador.Cpf(valor),
        "Cnpj" => pseudonimizador.Cnpj(valor),
        "Telefone" => pseudonimizador.Telefone(valor),
        "Email" => pseudonimizador.Email(valor),
        "ChaveDfe" => pseudonimizador.ChaveDfe(valor),
        _ => pseudonimizador.ChavePix(valor),
    };

    private static string? Saida(PseudonimizadorCpf cpf, string valor)
    {
        cpf.Pseudonimizar(valor, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorCnpj cnpj, string valor)
    {
        cnpj.Pseudonimizar(valor, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorTelefone telefone, string valor)
    {
        telefone.Pseudonimizar(valor, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorEmail email, string valor)
    {
        email.Pseudonimizar(valor, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorChavePix pix, string valor)
    {
        pix.Pseudonimizar(valor, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorChaveDfe chaveDfe, string valor)
    {
        chaveDfe.Pseudonimizar(valor, out var saida, out _);
        return saida;
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }
}
