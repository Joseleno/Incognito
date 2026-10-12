using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Regressão das colunas de CPF e CNPJ até existirem os vetores fixos: o resumo SHA-256 das saídas de documentos
/// sorteados com semente fixa (DV certo e errado, numérico e alfanumérico, com e sem <c>permutarOrdem</c>) não muda.
/// A chave de DF-e usa as mesmas instâncias, então uma mudança na coluna passaria despercebida nos testes da chave. Só
/// o resumo fica no código, nenhum documento. Mudar os geradores de documentos dos testes também muda o resumo, que então
/// se recalcula com revisão.
/// </summary>
public sealed class SaidasDasColunasTestes : IDisposable
{
    private const int Quantidade = 3_000;

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Saidas_da_coluna_de_CPF_nao_mudam()
    {
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(2801);
        var saidas = new StringBuilder();
        for (var i = 0; i < Quantidade; i++)
        {
            var entrada = ComDvTrocado(sorteio, CpfValido(sorteio));
            var desfecho = cpf.Pseudonimizar(entrada, out var saida);
            saidas.Append(saida).Append(' ').Append(desfecho).Append('\n');
        }

        Assert.Equal("9282AC2968E0EB6A963ADA2CC613BDDA8ABC259898F2CC376A78A7B508B2E9DF", Resumo(saidas));
    }

    [Theory]
    [InlineData(false, "7907F6E2E45C0E693F6509BBEF42D43818F52510A47F50AE47520759D9249C0C")]
    [InlineData(true, "CFB385014AD1108825A215AE14F88E8377AFBDFD1803339BD9EF13D1A43F4755")]
    public void Saidas_da_coluna_de_CNPJ_nao_mudam(bool permutarOrdem, string esperado)
    {
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", permutarOrdem, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(permutarOrdem ? 2803 : 2802);
        var saidas = new StringBuilder();
        for (var i = 0; i < Quantidade; i++)
        {
            var entrada = ComDvTrocado(sorteio, CnpjValido(sorteio, alfanumerico: sorteio.Next(2) == 0));
            var desfecho = cnpj.Pseudonimizar(entrada, out var saida);
            saidas.Append(saida).Append(' ').Append(desfecho).Append('\n');
        }

        Assert.Equal(esperado, Resumo(saidas));
    }

    // Um em cada quatro com o DV deslocado, para o caminho do DV errado também ficar preso.
    private static string ComDvTrocado(Random sorteio, string documento)
    {
        if (sorteio.Next(4) != 0)
        {
            return documento;
        }

        var dv = int.Parse(documento[^2..], CultureInfo.InvariantCulture);
        return documento[..^2] + ((dv + 1 + sorteio.Next(99)) % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string Resumo(StringBuilder saidas) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(saidas.ToString())));

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }
}
