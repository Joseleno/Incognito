using System.Numerics;

namespace CodeProcess.Incognito.Geracao;

/// <summary>
/// xoshiro256** (Blackman e Vigna, 2018, domínio público), com o estado inicial expandido da semente pelo SplitMix64,
/// como os autores recomendam: as quatro palavras são as quatro primeiras saídas do SplitMix64 a partir de
/// <c>(ulong)semente</c>. O estado nunca sai todo zero: a mistura do SplitMix64 é bijetiva e os quatro contadores são
/// distintos (o incremento é ímpar), então as quatro palavras são distintas e no máximo uma é zero. Toda a aritmética
/// é módulo 2⁶⁴.
/// </summary>
internal sealed class SequenciaXoshiro : FonteDeSorteio
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Começa do estado dado.</summary>
    /// <param name="s0">Primeira palavra do estado.</param>
    /// <param name="s1">Segunda palavra do estado.</param>
    /// <param name="s2">Terceira palavra do estado.</param>
    /// <param name="s3">Quarta palavra do estado.</param>
    /// <exception cref="ArgumentException">Estado todo zero, do qual a sequência nunca sai.</exception>
    internal SequenciaXoshiro(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw new ArgumentException("O estado do xoshiro256** não pode ser todo zero.", nameof(s0));
        }

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    /// <summary>Expande a semente pelo SplitMix64.</summary>
    /// <param name="semente">A semente; uma negativa vale pelos mesmos 64 bits sem sinal.</param>
    /// <returns>A sequência.</returns>
    internal static SequenciaXoshiro DaSemente(long semente)
    {
        var x = (ulong)semente;
        return new SequenciaXoshiro(SplitMix64(ref x), SplitMix64(ref x), SplitMix64(ref x), SplitMix64(ref x));
    }

    /// <summary>Uma saída do SplitMix64 (Steele, Lea e Flood, 2014), avançando o contador.</summary>
    /// <param name="x">O contador.</param>
    /// <returns>A saída.</returns>
    internal static ulong SplitMix64(ref ulong x)
    {
        unchecked
        {
            var z = x += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <inheritdoc/>
    internal override ulong Proximo()
    {
        var resultado = unchecked(BitOperations.RotateLeft(_s1 * 5, 7) * 9);
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return resultado;
    }
}
