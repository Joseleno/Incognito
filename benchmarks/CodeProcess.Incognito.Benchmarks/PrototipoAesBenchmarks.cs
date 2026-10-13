using System.Buffers.Binary;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// Comparação do ADR 3: a permutação de 12 rodadas com HMAC (a do núcleo, linha de base) contra um protótipo com a função
/// de rodada em AES-256 à moda do FF1 (um HMAC do cabeçalho e do ajuste por valor, depois um bloco AES por rodada com o
/// transformador reaproveitado). O protótipo existe só aqui, nunca no núcleo, e antes de medir confere a bijeção
/// exaustiva em 100 × 100 e em 100 × 1000 (lados diferentes, como no caso medido) e que a saída depende da chave e do
/// valor: um protótipo errado pareceria mais rápido. Ele deixa de fora custos que o núcleo tem (o <c>ThreadLocal</c>, a
/// validação e a limpeza dos buffers, dezenas de ns), o que favorece o AES: o lado conservador para a decisão do ADR. A
/// mesma chave no HMAC e no AES só serve ao benchmark.
/// </summary>
[MemoryDiagnoser]
public class PrototipoAesBenchmarks : IDisposable
{
    private readonly ulong[] _entradas = new ulong[Lote.Tamanho];
    private readonly byte[] _ajuste = RandomNumberGenerator.GetBytes(20);
    private PermutacaoFeistel? _hmac;
    private FeistelComAes? _aes;

    [GlobalSetup]
    public void Preparar()
    {
        var chave = RandomNumberGenerator.GetBytes(32);
        var outra = RandomNumberGenerator.GetBytes(32);
        try
        {
            _hmac = new PermutacaoFeistel(chave, 10, 10_000, 100_000);
            _aes = new FeistelComAes(chave, 10_000, 100_000);
            ConferirBijecao(chave, 100, 100);
            ConferirBijecao(chave, 100, 1_000);
            ConferirDependencia(chave, outra);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chave);
            CryptographicOperations.ZeroMemory(outra);
        }

        var fonte = SequenciaXoshiro.DaSemente(Lote.Semente);
        for (var i = 0; i < _entradas.Length; i++)
        {
            _entradas[i] = fonte.Uniforme(1_000_000_000);
        }
    }

    // Uma função de rodada constante, que ignorasse o valor ou pulasse o AES ainda daria bijeção: a saída precisa mudar
    // com a chave e quase nunca repetir a entrada (uma permutação aleatória de 10⁴ valores tem em média 1 ponto fixo).
    private void ConferirDependencia(byte[] chave, byte[] outra)
    {
        using var primeira = new FeistelComAes(chave, 100, 100);
        using var segunda = new FeistelComAes(outra, 100, 100);
        var iguaisEntreChaves = 0;
        var pontosFixos = 0;
        for (var x = 0UL; x < 10_000; x++)
        {
            var y = primeira.Permutar(x, _ajuste);
            iguaisEntreChaves += y == segunda.Permutar(x, _ajuste) ? 1 : 0;
            pontosFixos += y == x ? 1 : 0;
        }

        if (iguaisEntreChaves > 20 || pontosFixos > 20)
        {
            throw new InvalidOperationException("O protótipo com AES não depende da chave ou do valor.");
        }
    }

    private void ConferirBijecao(byte[] chave, ulong a, ulong b)
    {
        using var pequeno = new FeistelComAes(chave, a, b);
        var imagens = new HashSet<ulong>();
        for (var x = 0UL; x < a * b; x++)
        {
            var y = pequeno.Permutar(x, _ajuste);
            if (y >= a * b || !imagens.Add(y))
            {
                throw new InvalidOperationException("O protótipo com AES não é uma bijeção.");
            }
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Lote.Tamanho)]
    public ulong FeistelComHmac()
    {
        var soma = 0UL;
        foreach (var x in _entradas)
        {
            soma += _hmac!.Permutar(x, _ajuste);
        }

        return soma;
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public ulong FeistelComAesPorRodada()
    {
        var soma = 0UL;
        foreach (var x in _entradas)
        {
            soma += _aes!.Permutar(x, _ajuste);
        }

        return soma;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _hmac?.Dispose();
        _aes?.Dispose();
        GC.SuppressFinalize(this);
    }

    // Feistel modular de 12 rodadas sobre Z_a × Z_b: T = HMAC(K, a ‖ b ‖ |ajuste| ‖ ajuste)[0..16] uma vez por valor;
    // F_i(v) = u64be(AES_K(T ⊕ (i ‖ u64be(v) ‖ 0…))[0..8]) mod lado. Uma instância só, sem ThreadLocal: o benchmark roda
    // numa thread.
    private sealed class FeistelComAes : IDisposable
    {
        private const int Rodadas = 12;
        private readonly ulong _a;
        private readonly ulong _b;
        private readonly IncrementalHash _hmac;
        private readonly Aes _aes;
        private readonly ICryptoTransform _cifrador;
        private readonly byte[] _bloco = new byte[16];
        private readonly byte[] _cifrado = new byte[16];
        private readonly byte[] _cabecalho = new byte[18];

        internal FeistelComAes(byte[] chave, ulong a, ulong b)
        {
            _a = a;
            _b = b;
            _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, chave);
            _aes = Aes.Create();
            _aes.Key = chave;
            _aes.Mode = CipherMode.ECB;
            _aes.Padding = PaddingMode.None;
            _cifrador = _aes.CreateEncryptor();
            BinaryPrimitives.WriteUInt64BigEndian(_cabecalho, a);
            BinaryPrimitives.WriteUInt64BigEndian(_cabecalho.AsSpan(8), b);
        }

        internal ulong Permutar(ulong x, byte[] ajuste)
        {
            BinaryPrimitives.WriteUInt16BigEndian(_cabecalho.AsSpan(16), (ushort)ajuste.Length);
            _hmac.AppendData(_cabecalho);
            _hmac.AppendData(ajuste);
            Span<byte> resumo = stackalloc byte[32];
            _hmac.GetHashAndReset(resumo);
            var (esquerda, direita) = Math.DivRem(x, _b);
            for (var rodada = 0; rodada < Rodadas; rodada++)
            {
                var modulo = rodada % 2 == 0 ? _a : _b;
                resumo[..16].CopyTo(_bloco);
                _bloco[0] ^= (byte)rodada;
                var valor = BinaryPrimitives.ReadUInt64BigEndian(_bloco.AsSpan(1)) ^ direita;
                BinaryPrimitives.WriteUInt64BigEndian(_bloco.AsSpan(1), valor);
                _cifrador.TransformBlock(_bloco, 0, 16, _cifrado, 0);
                var f = BinaryPrimitives.ReadUInt64BigEndian(_cifrado) % modulo;
                (esquerda, direita) = (direita, (esquerda + f) % modulo);
            }

            return (esquerda * _b) + direita;
        }

        public void Dispose()
        {
            _hmac.Dispose();
            _cifrador.Dispose();
            _aes.Dispose();
        }
    }
}
