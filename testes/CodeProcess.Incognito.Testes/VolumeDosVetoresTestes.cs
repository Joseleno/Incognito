using System.Text;
using System.Text.Json;
using CodeProcess.Incognito.Testes.Vetores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Vetor de volume: o resumo das saídas de milhares de CPFs e CNPJs não muda. Pega um erro que atinge só uma fração
/// pequena dos valores, que os pares dirigidos dos outros vetores deixariam passar. A referência em Python refaz as mesmas
/// entradas e o mesmo resumo de forma independente.
/// </summary>
public sealed class VolumeDosVetoresTestes
{
    public static TheoryData<string> Casos => [.. VolumeDeVetores.Casos.Select(c => c.Nome)];

    [Fact]
    public void O_arquivo_tem_exatamente_os_casos_e_a_descricao_esperados()
    {
        using var documento = Ler();
        var raiz = documento.RootElement;

        Assert.Equal(["descricao", "chave", "casos"], raiz.EnumerateObject().Select(p => p.Name));
        Assert.Equal(VolumeDeVetores.Descricao, raiz.GetProperty("descricao").GetString());
        Assert.Equal(VolumeDeVetores.Casos.Select(c => c.Nome), raiz.GetProperty("casos").EnumerateArray().Select(c => c.GetProperty("nome").GetString()!));
        using var derivacao = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        Assert.True(
            raiz.GetProperty("chave").GetString() == ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(derivacao, "chaves", "aleatoria"), "texto"),
            $"{VolumeDeVetores.Arquivo}: chave diferente da chave pública de teste.");
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void O_resumo_das_saidas_nao_muda(string nome)
    {
        var esperado = VolumeDeVetores.Casos.Single(c => c.Nome == nome);
        using var documento = Ler();
        var caso = documento.RootElement.GetProperty("casos").EnumerateArray().Single(c => c.GetProperty("nome").GetString() == nome);
        string[] propriedades = esperado.PermutarOrdem is null
            ? ["nome", "transformador", "versao", "dominio", "ambiente", "quantidade", "resumo"]
            : ["nome", "transformador", "versao", "dominio", "ambiente", "permutarOrdem", "quantidade", "resumo"];
        Assert.Equal(propriedades, caso.EnumerateObject().Select(p => p.Name));
        Assert.Equal(esperado.Transformador, caso.GetProperty("transformador").GetString());
        Assert.Equal(1, caso.GetProperty("versao").GetInt32());
        Assert.Equal(esperado.Dominio, caso.GetProperty("dominio").GetString());
        Assert.Equal(esperado.Quantidade, caso.GetProperty("quantidade").GetInt32());
        if (esperado.PermutarOrdem is { } ordem)
        {
            Assert.Equal(ordem, caso.GetProperty("permutarOrdem").GetBoolean());
        }

        using var chave = ChaveIncognito.Ler(documento.RootElement.GetProperty("chave").GetString());
        var resumo = VolumeDeVetores.Resumo(
            chave, nome, esperado.Transformador, esperado.Dominio, caso.GetProperty("ambiente").GetString()!, esperado.PermutarOrdem, esperado.Quantidade);

        Assert.True(resumo == caso.GetProperty("resumo").GetString(), $"{VolumeDeVetores.Arquivo}, caso '{nome}': o resumo das saídas mudou.");
    }

    private static JsonDocument Ler() =>
        JsonDocument.Parse(
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(Path.Combine(EsquemaDeVetores.PastaNaSaida, VolumeDeVetores.Arquivo))),
            EsquemaDeVetores.Opcoes);
}
