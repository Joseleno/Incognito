using System.Diagnostics;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>A raiz do repositório e os arquivos versionados nele.</summary>
internal static class Repositorio
{
    /// <summary>A raiz do repositório, subindo a partir da saída dos testes.</summary>
    public static string Raiz()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "Incognito.slnx")) && File.Exists(Path.Combine(pasta.FullName, "global.json")))
            {
                return pasta.FullName;
            }
        }

        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    /// <summary>Os arquivos versionados ou a versionar que casam com o padrão, relativos à raiz (pelo <c>git ls-files</c>).</summary>
    /// <remarks>Os que o git ignora (inclusive pelo <c>.git/info/exclude</c>, como as notas locais) nunca entram.</remarks>
    public static IReadOnlyList<string> Versionados(string padrao)
    {
        var inicio = new ProcessStartInfo("git")
        {
            WorkingDirectory = Raiz(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argumento in (string[])["-c", "core.quotepath=off", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", padrao])
        {
            inicio.ArgumentList.Add(argumento);
        }

        using var git = Process.Start(inicio) ?? throw new InvalidOperationException("O git não iniciou.");
        var saida = git.StandardOutput.ReadToEnd();
        git.StandardError.ReadToEnd();
        git.WaitForExit();
        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files saiu com {git.ExitCode}.");
        }

        // O índice ainda lista um arquivo apagado do disco sem git rm.
        var raiz = Raiz();
        return saida.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(arquivo => File.Exists(Path.Combine(raiz, arquivo))).ToArray();
    }
}
