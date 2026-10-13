using BenchmarkDotNet.Attributes;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// Vazão do CPF com 1, 4 e 8 threads sobre a mesma instância (o motor usa várias threads): mede como o HMAC por thread
/// escala, com o efeito do hyper-threading e da queda de frequência com vários núcleos ativos.
/// </summary>
[MemoryDiagnoser]
public class VazaoBenchmarks : IDisposable
{
    private const int Valores = 8 * Lote.Tamanho;
    private ChaveIncognito? _chave;
    private PseudonimizadorCpf? _cpf;
    private string[] _cpfs = [];

    [Params(1, 4, 8)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Preparar()
    {
        _chave = ChaveIncognito.Gerar();
        _cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(Lote.Semente);
        _cpfs = [.. Enumerable.Range(0, Valores).Select(_ => gerador.Cpf())];
        if (_cpfs.Any(cpf => _cpf.Pseudonimizar(cpf, out _) != Desfecho.Pseudonimizado))
        {
            throw new InvalidOperationException("Uma entrada do benchmark não sai pseudonimizada.");
        }
    }

    [Benchmark(OperationsPerInvoke = Valores)]
    public void Cpf() =>
        Parallel.For(0, _cpfs.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads }, i => _cpf!.Pseudonimizar(_cpfs[i], out _));

    [GlobalCleanup]
    public void Dispose()
    {
        _cpf?.Dispose();
        _chave?.Dispose();
        GC.SuppressFinalize(this);
    }
}
