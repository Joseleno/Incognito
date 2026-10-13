using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// A primitiva de cada rodada, isolada, para o ADR 3: o HMAC-SHA256 como a permutação usa (uma instância reaproveitada) e
/// de uma vez (<see cref="HMACSHA256.HashData(byte[], byte[])"/>), e o AES-256 de um bloco pela plataforma: com o
/// transformador ECB reaproveitado (1, 16 e 1024 blocos por chamada: o custo fixo da chamada sai da diferença) e com o
/// <see cref="SymmetricAlgorithm.EncryptEcb(ReadOnlySpan{byte}, Span{byte}, PaddingMode)"/>, que recria o contexto a cada
/// chamada. O ECB aqui só calcula uma função de rodada sobre um bloco, nunca cifra dados.
/// </summary>
[MemoryDiagnoser]
public class RodadaBenchmarks : IDisposable
{
    // A mensagem de uma rodada da permutação: "R", rodada, radix, a, b, tamanho do ajuste, ajuste de 20 bytes e o valor.
    private readonly byte[] _mensagem = RandomNumberGenerator.GetBytes(1 + 1 + 4 + 8 + 8 + 2 + 20 + 8);
    private readonly byte[] _chave = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _resumo = new byte[32];
    private readonly byte[] _blocos = RandomNumberGenerator.GetBytes(16 * 1_024);
    private readonly byte[] _cifrados = new byte[16 * 1_024];
    private IncrementalHash? _hmac;
    private Aes? _aes;
    private ICryptoTransform? _cifrador;

    [GlobalSetup]
    public void Preparar()
    {
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _chave);
        _aes = Aes.Create();
        _aes.Key = _chave;
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _cifrador = _aes.CreateEncryptor();
    }

    [Benchmark(Baseline = true)]
    public int HmacReaproveitado()
    {
        _hmac!.AppendData(_mensagem);
        return _hmac.GetHashAndReset(_resumo);
    }

    [Benchmark]
    public int HmacDeUmaVez() => HMACSHA256.HashData(_chave, _mensagem, _resumo);

    [Benchmark]
    public int AesEncryptEcbDeUmBloco() => _aes!.EncryptEcb(_blocos.AsSpan(0, 16), _cifrados, PaddingMode.None);

    [Benchmark]
    public int AesReaproveitadoUmBloco() => _cifrador!.TransformBlock(_blocos, 0, 16, _cifrados, 0);

    [Benchmark(OperationsPerInvoke = 16)]
    public int AesReaproveitado16Blocos() => _cifrador!.TransformBlock(_blocos, 0, 16 * 16, _cifrados, 0);

    [Benchmark(OperationsPerInvoke = 1_024)]
    public int AesReaproveitado1024Blocos() => _cifrador!.TransformBlock(_blocos, 0, 16 * 1_024, _cifrados, 0);

    [GlobalCleanup]
    public void Dispose()
    {
        _hmac?.Dispose();
        _cifrador?.Dispose();
        _aes?.Dispose();
        CryptographicOperations.ZeroMemory(_chave);
        GC.SuppressFinalize(this);
    }
}
