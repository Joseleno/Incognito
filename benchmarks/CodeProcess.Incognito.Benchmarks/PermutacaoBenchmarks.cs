using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// A permutação Feistel crua, por valor: 10, 12 e 24 rodadas (o custo por rodada sai da inclinação) e ajuste de 0, 20 e
/// 33 bytes (0 é o do CPF; 20 e 33, os do número da chave de DF-e fora e dentro da NFF). É a medida do gatilho do ADR 3:
/// 12 rodadas, ajuste de 20 bytes.
/// </summary>
[MemoryDiagnoser]
public class PermutacaoBenchmarks : IDisposable
{
    private readonly ulong[] _entradas = new ulong[Lote.Tamanho];
    private PermutacaoFeistel? _permutacao;
    private byte[] _ajuste = [];

    [Params(10, 12, 24)]
    public int Rodadas { get; set; }

    [Params(0, 20, 33)]
    public int BytesDeAjuste { get; set; }

    [GlobalSetup]
    public void Preparar()
    {
        var chave = RandomNumberGenerator.GetBytes(Subchaves.Tamanho);
        try
        {
            _permutacao = new PermutacaoFeistel(chave, 10, 10_000, 100_000, Rodadas);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chave);
        }

        _ajuste = RandomNumberGenerator.GetBytes(BytesDeAjuste);
        var fonte = SequenciaXoshiro.DaSemente(Lote.Semente);
        for (var i = 0; i < _entradas.Length; i++)
        {
            _entradas[i] = fonte.Uniforme(1_000_000_000);
        }
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public ulong Permutar()
    {
        var soma = 0UL;
        foreach (var x in _entradas)
        {
            soma += _permutacao!.Permutar(x, _ajuste);
        }

        return soma;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _permutacao?.Dispose();
        GC.SuppressFinalize(this);
    }
}
