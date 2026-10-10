using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Derivação de <c>K_dominio</c> e <c>K_semente</c> por HKDF-SHA256. O vetor fixo em <c>testes/vetores/</c> foi
/// calculado por uma implementação independente e é contrato: se um valor mudar, a derivação ganhou versão nova.
/// Fora do vetor, as asserções não imprimem subchaves quando falham.
/// </summary>
public sealed class SubchavesTestes
{
    private const string Pasta = "vetores";
    private const string Vetor = "derivacao@1.json";
    private const int ChavesDeDominioPorChave = 6;
    private const int SementesPorChave = 4;

    private static readonly Dictionary<string, string> _nomesInvalidos = new()
    {
        ["vazio"] = "",
        ["maiuscula"] = "Homologacao",
        ["acento em NFC"] = "produção",
        ["acento em NFD"] = "produção",
        ["espaco no fim"] = "homologacao ",
        ["espaco no inicio"] = " homologacao",
        ["espaco de largura zero"] = "homologacao​",
        ["BOM no inicio"] = "﻿homologacao",
        ["quebra de linha no fim"] = "homologacao\n",
        ["nulo no meio"] = "homo\0logacao",
        ["surrogate isolado"] = "homologacao\uD800",
        ["letra de largura total"] = "ｃｐｆ",
        ["arroba"] = "cpf@1",
        ["barra"] = "br/cep",
        ["separador no inicio"] = ".cpf",
        ["separador no fim"] = "cpf-",
        ["separadores seguidos"] = "br..cep",
        ["65 caracteres"] = new string('a', 65),
    };

    private static readonly Dictionary<string, string> _nomesValidos = new()
    {
        ["uma letra"] = "a",
        ["um digito"] = "0",
        ["sublinhado"] = "pedido_id",
        ["ponto e hifen"] = "br.nome-completo",
        ["todos os separadores"] = "a.b_c-d",
        ["64 caracteres"] = new string('a', 64),
    };

    public static TheoryData<string> ChavesDoVetor => ArquivosDeTeste.NomesDosCasos(Pasta, Vetor, "chaves");

    public static TheoryData<string, string> NomesInvalidosPorCampo => PorCampo(_nomesInvalidos.Keys);

    public static TheoryData<string, string> NomesValidosPorCampo => PorCampo(_nomesValidos.Keys);

    public static TheoryData<string> VariantesInvalidas => ["", "Num", "num ", "cnf-8", "numerico"];

    public static TheoryData<string> CamposNulos => ["dominio", "variante", "ambiente", "transformador"];

    [Theory]
    [MemberData(nameof(ChavesDoVetor))]
    public void Subchaves_conferem_com_o_vetor_fixo(string nome)
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, Vetor);
        var caso = ArquivosDeTeste.Caso(documento, "chaves", nome);
        using var chave = ChaveIncognito.Ler(ArquivosDeTeste.Texto(caso, "texto"));
        var prk = new byte[32];
        var subchave = new byte[32];

        chave.ExtrairPrk(prk);
        Assert.Equal(ArquivosDeTeste.Texto(caso, "impressaoDigital"), chave.ImpressaoDigital);
        Assert.Equal(ArquivosDeTeste.Texto(caso, "prk"), Convert.ToHexStringLower(prk));

        // Contar impede que o vetor seja esvaziado sem que o teste perceba.
        var conferidas = 0;
        foreach (var derivacao in caso.GetProperty("feistel").EnumerateArray())
        {
            Subchaves.DerivarChaveDeDominio(
                chave,
                ArquivosDeTeste.Texto(derivacao, "dominio"),
                derivacao.GetProperty("versao").GetInt32(),
                ArquivosDeTeste.Texto(derivacao, "variante"),
                ArquivosDeTeste.Texto(derivacao, "ambiente"),
                subchave);
            Assert.Equal(ArquivosDeTeste.Texto(derivacao, "subchave"), Convert.ToHexStringLower(subchave));
            conferidas++;
        }

        Assert.Equal(ChavesDeDominioPorChave, conferidas);

        conferidas = 0;
        foreach (var derivacao in caso.GetProperty("semente").EnumerateArray())
        {
            Subchaves.DerivarSemente(
                chave,
                ArquivosDeTeste.Texto(derivacao, "transformador"),
                ArquivosDeTeste.Texto(derivacao, "dominio"),
                derivacao.GetProperty("versao").GetInt32(),
                ArquivosDeTeste.Texto(derivacao, "ambiente"),
                subchave);
            Assert.Equal(ArquivosDeTeste.Texto(derivacao, "subchave"), Convert.ToHexStringLower(subchave));
            conferidas++;
        }

        Assert.Equal(SementesPorChave, conferidas);
    }

    [Fact]
    public void Subchaves_sao_o_HKDF_Expand_do_PRK_com_o_info_da_especificacao()
    {
        using var chave = ChaveIncognito.Gerar();
        var prk = new byte[32];
        chave.ExtrairPrk(prk);
        var subchave = new byte[32];

        Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, "num", "homologacao", subchave);
        var esperada = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, "feistel\0cpf@1\0num\0homologacao"u8.ToArray());
        Assert.True(subchave.AsSpan().SequenceEqual(esperada), "A chave de domínio difere do HKDF-Expand esperado.");

        Subchaves.DerivarSemente(chave, "br.cep", "cpf", 1, "homologacao", subchave);
        esperada = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, "semente\0br.cep\0cpf@1\0homologacao"u8.ToArray());
        Assert.True(subchave.AsSpan().SequenceEqual(esperada), "A semente difere do HKDF-Expand esperado.");
    }

    [Fact]
    public void Dominios_versoes_variantes_e_ambientes_diferentes_dao_chaves_de_dominio_diferentes()
    {
        using var chave = ChaveIncognito.Gerar();
        (string Dominio, int Versao, string Variante, string Ambiente)[] casos =
        [
            ("cpf", 1, "num", "homologacao"),
            ("cnpj", 1, "num", "homologacao"),
            ("cpf", 2, "num", "homologacao"),
            ("cpf", 10, "num", "homologacao"),
            ("cpf", 1, "alfa", "homologacao"),
            ("cpf", 1, "num", "desenvolvimento"),
            ("cpf1", 1, "num", "homologacao"),
            ("cpf", 11, "num", "homologacao"),
        ];

        var subchaves = casos.Select(c => DominioEmHex(chave, c.Dominio, c.Versao, c.Variante, c.Ambiente)).ToList();

        Assert.Equal(casos.Length, subchaves.Distinct().Count());
        Assert.True(subchaves[0] == DominioEmHex(chave, "cpf", 1, "num", "homologacao"), "A derivação não é determinística.");
    }

    [Fact]
    public void Cada_variante_da_uma_chave_de_dominio_diferente()
    {
        using var chave = ChaveIncognito.Gerar();
        string[] variantes =
        [
            Variantes.Numerico, Variantes.Alfanumerico, Variantes.Celular, Variantes.Fixo, Variantes.Ordem,
            Variantes.Identificador, Variantes.NumeroDfe, Variantes.DiaNff, Variantes.SequencialNff,
            Variantes.CodigoNumerico8, Variantes.CodigoNumerico7,
        ];

        var subchaves = variantes.Select(v => DominioEmHex(chave, "cpf", 1, v, "homologacao")).ToList();

        Assert.Equal(11, subchaves.Distinct().Count());
    }

    [Fact]
    public void Transformadores_dominios_versoes_e_ambientes_diferentes_dao_sementes_diferentes()
    {
        using var chave = ChaveIncognito.Gerar();
        (string Transformador, string Dominio, int Versao, string Ambiente)[] casos =
        [
            ("br.nome-completo", "cpf", 1, "homologacao"),
            ("br.cep", "cpf", 1, "homologacao"),
            ("br.nome-completo", "cnpj", 1, "homologacao"),
            ("br.nome-completo", "cpf", 2, "homologacao"),
            ("br.nome-completo", "cpf", 1, "producao"),
        ];

        var sementes = casos.Select(c => SementeEmHex(chave, c.Transformador, c.Dominio, c.Versao, c.Ambiente)).ToList();

        Assert.Equal(casos.Length, sementes.Distinct().Count());
        Assert.True(sementes[0] == SementeEmHex(chave, "br.nome-completo", "cpf", 1, "homologacao"), "A derivação não é determinística.");
    }

    [Fact]
    public void Chaves_diferentes_dao_subchaves_diferentes()
    {
        using var primeira = ChaveIncognito.Gerar();
        using var segunda = ChaveIncognito.Gerar();

        Assert.True(
            DominioEmHex(primeira, "cpf", 1, "num", "homologacao") != DominioEmHex(segunda, "cpf", 1, "num", "homologacao"),
            "Chaves diferentes deram a mesma chave de domínio.");
        Assert.True(
            SementeEmHex(primeira, "br.cep", "cpf", 1, "homologacao") != SementeEmHex(segunda, "br.cep", "cpf", 1, "homologacao"),
            "Chaves diferentes deram a mesma semente.");
    }

    [Theory]
    [MemberData(nameof(NomesInvalidosPorCampo))]
    public void Nome_fora_da_regra_e_recusado_sem_tocar_o_destino(string campo, string caso)
    {
        using var chave = ChaveIncognito.Gerar();
        var destino = Enumerable.Repeat((byte)0xEE, 32).ToArray();

        var excecao = Assert.Throws<ArgumentException>(() => Derivar(chave, campo, _nomesInvalidos[caso], destino));

        Assert.Equal(campo.Replace("-semente", "", StringComparison.Ordinal), excecao.ParamName);
        Assert.True(Array.TrueForAll(destino, octeto => octeto == 0xEE), "O destino foi alterado por uma derivação recusada.");
    }

    [Theory]
    [MemberData(nameof(NomesValidosPorCampo))]
    public void Nome_dentro_da_regra_e_aceito(string campo, string caso)
    {
        using var chave = ChaveIncognito.Gerar();
        var destino = new byte[32];

        Derivar(chave, campo, _nomesValidos[caso], destino);

        Assert.True(destino.AsSpan().IndexOfAnyExcept((byte)0) >= 0, "A derivação aceita não escreveu a subchave.");
    }

    [Theory]
    [MemberData(nameof(VariantesInvalidas))]
    public void Variante_desconhecida_e_recusada(string variante)
    {
        using var chave = ChaveIncognito.Gerar();

        Assert.Throws<ArgumentException>(nameof(variante), () => Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, variante, "homologacao", new byte[32]));
    }

    [Theory]
    [MemberData(nameof(CamposNulos))]
    public void Campo_nulo_e_recusado(string campo)
    {
        using var chave = ChaveIncognito.Gerar();

        Assert.Throws<ArgumentNullException>(campo, () => Derivar(chave, campo, null!, new byte[32]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Versao_menor_que_1_e_recusada(int invalida)
    {
        using var chave = ChaveIncognito.Gerar();

        Assert.Throws<ArgumentOutOfRangeException>("versao", () => Subchaves.DerivarChaveDeDominio(chave, "cpf", invalida, "num", "homologacao", new byte[32]));
        Assert.Throws<ArgumentOutOfRangeException>("versao", () => Subchaves.DerivarSemente(chave, "br.cep", "cpf", invalida, "homologacao", new byte[32]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Destino_com_tamanho_diferente_de_32_e_recusado(int tamanho)
    {
        using var chave = ChaveIncognito.Gerar();

        Assert.Throws<ArgumentException>("destino", () => Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, "num", "homologacao", new byte[tamanho]));
        Assert.Throws<ArgumentException>("destino", () => Subchaves.DerivarSemente(chave, "br.cep", "cpf", 1, "homologacao", new byte[tamanho]));
    }

    [Fact]
    public void Chave_descartada_nao_deriva_e_deixa_o_destino_zerado()
    {
        var chave = ChaveIncognito.Gerar();
        chave.Dispose();
        var destino = Enumerable.Repeat((byte)0xEE, 32).ToArray();

        Assert.Throws<ObjectDisposedException>(() => Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, "num", "homologacao", destino));
        Assert.True(Array.TrueForAll(destino, octeto => octeto == 0), "O destino não foi zerado.");

        destino.AsSpan().Fill(0xEE);
        Assert.Throws<ObjectDisposedException>(() => Subchaves.DerivarSemente(chave, "br.cep", "cpf", 1, "homologacao", destino));
        Assert.True(Array.TrueForAll(destino, octeto => octeto == 0), "O destino não foi zerado.");
    }

    [Fact]
    public void Chave_nula_e_recusada()
    {
        Assert.Throws<ArgumentNullException>("chave", () => Subchaves.DerivarChaveDeDominio(null!, "cpf", 1, "num", "homologacao", new byte[32]));
        Assert.Throws<ArgumentNullException>("chave", () => Subchaves.DerivarSemente(null!, "br.cep", "cpf", 1, "homologacao", new byte[32]));
    }

    // Cada nome de teste vale para os campos que seguem a regra: domínio e ambiente nas duas derivações, transformador na semente.
    private static TheoryData<string, string> PorCampo(IEnumerable<string> casos)
    {
        var dados = new TheoryData<string, string>();
        foreach (var caso in casos)
        {
            foreach (var campo in new[] { "dominio", "ambiente", "transformador", "dominio-semente", "ambiente-semente" })
            {
                dados.Add(campo, caso);
            }
        }

        return dados;
    }

    // Deriva com o valor no campo indicado e valores válidos nos demais. "-semente" escolhe a derivação de semente.
    private static void Derivar(ChaveIncognito chave, string campo, string valor, byte[] destino)
    {
        switch (campo)
        {
            case "dominio":
                Subchaves.DerivarChaveDeDominio(chave, valor, 1, "num", "homologacao", destino);
                break;
            case "variante":
                Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, valor, "homologacao", destino);
                break;
            case "ambiente":
                Subchaves.DerivarChaveDeDominio(chave, "cpf", 1, "num", valor, destino);
                break;
            case "transformador":
                Subchaves.DerivarSemente(chave, valor, "cpf", 1, "homologacao", destino);
                break;
            case "dominio-semente":
                Subchaves.DerivarSemente(chave, "br.cep", valor, 1, "homologacao", destino);
                break;
            case "ambiente-semente":
                Subchaves.DerivarSemente(chave, "br.cep", "cpf", 1, valor, destino);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(campo));
        }
    }

    private static string DominioEmHex(ChaveIncognito chave, string dominio, int versao, string variante, string ambiente)
    {
        var subchave = new byte[32];
        Subchaves.DerivarChaveDeDominio(chave, dominio, versao, variante, ambiente, subchave);
        return Convert.ToHexStringLower(subchave);
    }

    private static string SementeEmHex(ChaveIncognito chave, string transformador, string dominio, int versao, string ambiente)
    {
        var subchave = new byte[32];
        Subchaves.DerivarSemente(chave, transformador, dominio, versao, ambiente, subchave);
        return Convert.ToHexStringLower(subchave);
    }
}
