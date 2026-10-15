using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Testes;

/// <summary>A variável da mutação noturna só vale no workflow <c>noturno</c>, e só os dados ficam fora da mutação.</summary>
public sealed class MutacaoTestes
{
    // Os únicos arquivos, relativos a src/, que citam o Stryker (comentário de desligar a mutação, em qualquer grafia que
    // ele aceite) ou o ExcludeFromCodeCoverage (que o Stryker também respeita): lógica fora da mutação passaria sem teste.
    private static readonly string[] _foraDaMutacao = ["CodeProcess.Incognito/Normas/ReferenciasNormativas.cs"];

    [Fact]
    public void No_CI_a_mutacao_so_vale_no_workflow_noturno()
    {
        // Fora do noturno, a variável encolheria ou pularia os testes pesados sem aviso, em qualquer job que rode a suíte.
        var noCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var workflow = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW");

        Assert.False(noCi && Mutacao.Ativa && workflow != "noturno", "INCOGNITO_MUTACAO definida fora do workflow noturno.");
    }

    [Fact]
    public void So_os_dados_do_catalogo_normativo_ficam_fora_da_mutacao()
    {
        var src = Path.Combine(ArquivosDeTeste.RaizDoRepositorio(), "src");
        var comComentario = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(arquivo => Path.GetRelativePath(src, arquivo).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relativo => !relativo.Contains("/bin/", StringComparison.Ordinal) && !relativo.Contains("/obj/", StringComparison.Ordinal))
            .Where(relativo => Regex.IsMatch(File.ReadAllText(Path.Combine(src, relativo)), "Stryker|ExcludeFromCodeCoverage", RegexOptions.IgnoreCase))
            .Order(StringComparer.Ordinal);

        Assert.Equal(_foraDaMutacao, comComentario);
    }
}
