namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// O código do README é o que os testes compilam e rodam: cada bloco <c>csharp</c> das seções conferidas é igual, linha a
/// linha, ao trecho marcado em <see cref="ExemploDeUsoDiretoTestes"/>, depois do <c>using</c> que o README mostra.
/// </summary>
public sealed class ReadmeTestes
{
    private const string UsingDoReadme = "using CodeProcess.Incognito;";

    [Theory]
    [InlineData("## Uso direto, sem banco", "do exemplo do README")]
    [InlineData("### Criar uma chave", "da criação da chave do README")]
    public void O_codigo_do_README_e_o_que_os_testes_rodam(string titulo, string marcador)
    {
        var raiz = Repositorio.Raiz();
        var bloco = BlocoCsharp(File.ReadAllLines(Path.Combine(raiz, "README.md")), titulo);
        var exemplo = File.ReadAllLines(Path.Combine(raiz, "testes", "CodeProcess.Incognito.Testes.Arquitetura", "ExemploDeUsoDiretoTestes.cs"));

        Assert.True(bloco.Count >= 2 && bloco[0] == UsingDoReadme && bloco[1].Length == 0, $"O bloco de \"{titulo}\" não começa com \"{UsingDoReadme}\" e uma linha em branco.");
        var esperado = Normalizar(bloco.Skip(2));
        var testado = Normalizar(Trecho(exemplo, marcador));

        for (var linha = 0; linha < Math.Max(esperado.Count, testado.Count); linha++)
        {
            var noReadme = linha < esperado.Count ? esperado[linha] : "(fim do bloco)";
            var noTeste = linha < testado.Count ? testado[linha] : "(fim do trecho)";
            Assert.True(noReadme == noTeste, $"\"{titulo}\", linha {linha + 1} do bloco: no README \"{noReadme}\", no teste \"{noTeste}\".");
        }
    }

    // O único bloco de C# da seção (do título exato até o próximo título).
    private static List<string> BlocoCsharp(string[] linhas, string titulo)
    {
        var inicioDoTitulo = Array.IndexOf(linhas, titulo);
        Assert.True(inicioDoTitulo >= 0, $"O README não tem o título \"{titulo}\".");
        var proximoTitulo = Array.FindIndex(linhas, inicioDoTitulo + 1, linha => linha.StartsWith('#'));
        var secao = linhas[(inicioDoTitulo + 1)..(proximoTitulo < 0 ? linhas.Length : proximoTitulo)];
        var deCsharp = Enumerable.Range(0, secao.Length)
            .Where(indice => secao[indice].Trim().ToLowerInvariant() is "```csharp" or "```cs" or "```c#")
            .ToArray();
        Assert.True(deCsharp.Length == 1 && secao[deCsharp[0]] == "```csharp", $"A seção \"{titulo}\" precisa de um, e só um, bloco de C# (```csharp).");
        var fechamento = Array.FindIndex(secao, deCsharp[0] + 1, linha => linha == "```");
        Assert.True(fechamento > deCsharp[0], $"O bloco csharp de \"{titulo}\" não fecha.");

        return secao[(deCsharp[0] + 1)..fechamento].Select(linha => linha.TrimEnd()).ToList();
    }

    private static string[] Trecho(string[] linhas, string marcador)
    {
        var inicio = Array.FindIndex(linhas, linha => linha.Trim() == $"// início {marcador}");
        var fim = Array.FindIndex(linhas, linha => linha.Trim() == $"// fim {marcador}");
        Assert.True(inicio >= 0 && fim > inicio, $"O ExemploDeUsoDiretoTestes não tem os marcadores \"início {marcador}\" e \"fim {marcador}\".");

        return linhas[(inicio + 1)..fim];
    }

    // Sem a indentação comum e sem espaço à direita.
    private static List<string> Normalizar(IEnumerable<string> linhas)
    {
        var lista = linhas.Select(linha => linha.TrimEnd()).ToList();
        var indentacao = lista.Where(linha => linha.Length > 0).Min(linha => linha.Length - linha.TrimStart().Length);
        return lista.Select(linha => linha.Length == 0 ? linha : linha[indentacao..]).ToList();
    }
}
