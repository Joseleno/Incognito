using BenchmarkDotNet.Running;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// Benchmarks do núcleo (BenchmarkDotNet). Só em Release:
/// <c>dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --filter "*"</c>.
/// </summary>
internal static class Programa
{
    // Sai com 1 se nenhum benchmark rodou ou se algum falhou (projeto gerado que não compila, GlobalSetup que lança,
    // filtro vazio, processo que terminou com erro depois de medir): o BenchmarkSwitcher só devolve os resumos, e o job
    // do CI ficaria verde sem isso.
    private static int Main(string[] argumentos)
    {
        var resumos = BenchmarkSwitcher.FromAssembly(typeof(Programa).Assembly).Run(argumentos).ToList();
        return resumos.Count == 0 || resumos.Any(resumo => resumo.HasCriticalValidationErrors || resumo.Reports.Any(relatorio => !relatorio.Success || relatorio.ExecuteResults.Any(execucao => execucao.ExitCode is not 0))) ? 1 : 0;
    }
}
