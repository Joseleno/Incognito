using System.Text.Json;

namespace CodeProcess.Incognito.Testes;

/// <summary>Leitura das âncoras (<c>testes/ancoras/</c>) e dos vetores fixos (<c>testes/vetores/</c>), copiados para a saída.</summary>
internal static class ArquivosDeTeste
{
    public static JsonDocument Ler(string pasta, string arquivo) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, pasta, arquivo)));

    public static JsonElement Caso(JsonDocument documento, string colecao, string nome)
    {
        var encontrados = documento.RootElement.GetProperty(colecao).EnumerateArray()
            .Where(caso => caso.GetProperty("nome").GetString() == nome)
            .ToList();

        return encontrados.Count switch
        {
            1 => encontrados[0],
            0 => throw new InvalidOperationException($"Caso '{nome}' ausente de '{colecao}'."),
            _ => throw new InvalidOperationException($"Caso '{nome}' repetido em '{colecao}'."),
        };
    }

    public static TheoryData<string> NomesDosCasos(string pasta, string arquivo, string colecao)
    {
        using var documento = Ler(pasta, arquivo);
        var nomes = new TheoryData<string>();
        foreach (var caso in documento.RootElement.GetProperty(colecao).EnumerateArray())
        {
            nomes.Add(caso.GetProperty("nome").GetString()!);
        }

        return nomes;
    }

    public static byte[] Hex(JsonElement caso, string propriedade) =>
        Convert.FromHexString(caso.GetProperty(propriedade).GetString()!);

    public static string Texto(JsonElement caso, string propriedade) => caso.GetProperty(propriedade).GetString()!;
}
