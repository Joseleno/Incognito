using BenchmarkDotNet.Attributes;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>O <see cref="ValidadorBr"/> (que não aloca) e o <see cref="GeradorBr"/>, por valor.</summary>
[MemoryDiagnoser]
public class DocumentoBenchmarks
{
    private string[] _cpfs = [];
    private string[] _cnpjs = [];
    private string[] _chaves = [];
    private GeradorBr? _gerador;

    [GlobalSetup]
    public void Preparar()
    {
        var gerador = new GeradorBr(Lote.Semente);
        _cpfs = [.. Enumerable.Range(0, Lote.Tamanho).Select(_ => gerador.Cpf())];
        _cnpjs = [.. Enumerable.Range(0, Lote.Tamanho).Select(i => gerador.Cnpj(alfanumerico: i % 2 == 0))];
        _chaves = [.. Enumerable.Range(0, Lote.Tamanho).Select(_ => gerador.ChaveDfe(55))];
        _gerador = new GeradorBr(Lote.Semente + 1);
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ValidarCpf() => _cpfs.Count(ValidadorBr.Cpf);

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ValidarCnpj() => _cnpjs.Count(ValidadorBr.Cnpj);

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ValidarChaveDfe() => _chaves.Count(ValidadorBr.ChaveDfe);

    [Benchmark]
    public string GerarCpf() => _gerador!.Cpf();

    [Benchmark]
    public string GerarCnpjAlfanumerico() => _gerador!.Cnpj(alfanumerico: true);

    [Benchmark]
    public string GerarChaveDfe() => _gerador!.ChaveDfe(55);
}
