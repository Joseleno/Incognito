using System.Security.Cryptography;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// HKDF e HMAC da plataforma conferidos contra os vetores das RFC 5869 e 4231, em <c>testes/ancoras/</c>. A derivação
/// e a função de rodada da permutação dependem deles.
/// </summary>
public sealed class AncorasRfcTestes
{
    private const string Pasta = "ancoras";
    private const string Hkdf = "rfc5869-hkdf-sha256.json";
    private const string Hmac = "rfc4231-hmac-sha256.json";

    public static TheoryData<string> CasosHkdf => ArquivosDeTeste.NomesDosCasos(Pasta, Hkdf, "casos");

    public static TheoryData<string> CasosHmac => ArquivosDeTeste.NomesDosCasos(Pasta, Hmac, "casos");

    [Theory]
    [MemberData(nameof(CasosHkdf))]
    public void HKDF_confere_com_a_RFC_5869(string nome)
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, Hkdf);
        var caso = ArquivosDeTeste.Caso(documento, "casos", nome);
        var okm = new byte[caso.GetProperty("tamanho").GetInt32()];

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ArquivosDeTeste.Hex(caso, "ikm"), ArquivosDeTeste.Hex(caso, "sal"));
        HKDF.Expand(HashAlgorithmName.SHA256, prk, okm, ArquivosDeTeste.Hex(caso, "info"));

        Assert.Equal(ArquivosDeTeste.Texto(caso, "prk"), Convert.ToHexStringLower(prk));
        Assert.Equal(ArquivosDeTeste.Texto(caso, "okm"), Convert.ToHexStringLower(okm));
    }

    [Theory]
    [MemberData(nameof(CasosHmac))]
    public void HMAC_SHA256_confere_com_a_RFC_4231(string nome)
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, Hmac);
        var caso = ArquivosDeTeste.Caso(documento, "casos", nome);
        var chave = ArquivosDeTeste.Hex(caso, "chave");
        var dados = ArquivosDeTeste.Hex(caso, "dados");
        var tamanho = caso.GetProperty("bytes").GetInt32();
        var esperado = ArquivosDeTeste.Texto(caso, "hmac");

        Assert.Equal(esperado, Convert.ToHexStringLower(HMACSHA256.HashData(chave, dados).AsSpan(0, tamanho)));

        // A permutação reutiliza uma instância por thread: o segundo cálculo na mesma instância tem de dar o mesmo.
        using var reutilizado = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, chave);
        for (var vez = 0; vez < 2; vez++)
        {
            reutilizado.AppendData(dados);
            Assert.Equal(esperado, Convert.ToHexStringLower(reutilizado.GetHashAndReset().AsSpan(0, tamanho)));
        }
    }
}
