using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// As mensagens do <c>planejar</c> são neutras entre dialetos: nenhum texto do motor cita o catálogo de um banco (ICU,
/// <c>pg_</c>, <c>tgenabled</c>, <c>lc_monetary</c>). A dica de um dialeto vai entre parênteses, pelo nome do recurso, nunca
/// pelo do catálogo.
/// </summary>
public sealed partial class MensagensDoPlanejarTestes
{
    // Os nomes de esquema de sistema que o motor compara (não são mensagens).
    private static readonly string[] _permitidos = ["\"pg_catalog\"", "\"pg_toast\"", "\"pg_\""];

    [Fact]
    public void Nenhum_texto_do_planejar_cita_o_catalogo_de_um_dialeto()
    {
        var raiz = Repositorio.Raiz();
        var arquivos = Repositorio.Versionados("src/CodeProcess.Incognito.Motor/Interno/*.cs").Concat(Repositorio.Versionados("src/CodeProcess.Incognito.Motor/Execucao/*.cs")).ToList();
        Assert.Contains("src/CodeProcess.Incognito.Motor/Interno/MontagemDoPlano.Acesso.cs", arquivos);
        var achados = arquivos
            .SelectMany(arquivo => Literal().Matches(Comentario().Replace(File.ReadAllText(Path.Combine(raiz, arquivo)), "")).Select(literal => $"{arquivo}: {literal.Value}"))
            .Where(achado => Proibido().IsMatch(achado[(achado.IndexOf(": ", StringComparison.Ordinal) + 2)..]) && !_permitidos.Any(permitido => achado.EndsWith(permitido, StringComparison.Ordinal)))
            .ToList();

        Assert.True(achados.Count == 0, string.Join("\n", achados));
    }

    [Fact]
    public void A_guarda_pega_um_texto_que_cita_o_catalogo()
    {
        // O controle: as formas que a guarda precisa pegar, também na caixa trocada e numa interpolação.
        string[] proibidos = ["\"trigger com tgenabled = 'D'\"", "$\"a colação ICU {nome}\"", "\"PG_PUBLICATION\"", "\"lc_monetary\""];

        Assert.All(proibidos, texto => Assert.Matches(Proibido(), Literal().Match(texto).Value));
        Assert.DoesNotMatch(Proibido(), "\"publicação de replicação\"");
    }

    [GeneratedRegex(@"\$?""(?:[^""\\\n]|\\.)*""", RegexOptions.CultureInvariant)]
    private static partial Regex Literal();

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Comentario();

    [GeneratedRegex(@"\bICU\b|pg_|tgenabled|lc_monetary", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Proibido();
}
