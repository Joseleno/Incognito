namespace CodeProcess.Incognito.Geracao;

/// <summary>
/// Fonte de números de 64 bits para o <see cref="GeradorBr"/>, com o sorteio uniforme num intervalo. Não é
/// criptográfica. Não é segura para várias threads: quem a usa sincroniza.
/// </summary>
internal abstract class FonteDeSorteio
{
    /// <summary>O próximo número de 64 bits.</summary>
    /// <returns>O número.</returns>
    internal abstract ulong Proximo();

    /// <summary>
    /// Um inteiro uniforme de 0 a <paramref name="n"/> − 1, pelo método de Lemire com rejeição: os 64 bits altos de
    /// <c>x · n</c>, rejeitando <c>x</c> quando os 64 baixos ficam abaixo de <c>(2⁶⁴ − n) mod n</c>. Sem viés, e cada
    /// tentativa consome um número. A divisão só é feita quando a parte baixa fica abaixo de <c>n</c>, o único caso em
    /// que ela pode estar abaixo do limite; a saída é a mesma.
    /// </summary>
    /// <param name="n">O tamanho do intervalo, a partir de 1.</param>
    /// <returns>O inteiro sorteado.</returns>
    internal ulong Uniforme(ulong n)
    {
        ArgumentOutOfRangeException.ThrowIfZero(n);
        var alto = Math.BigMul(Proximo(), n, out var baixo);
        if (baixo < n)
        {
            var limite = unchecked(0 - n) % n;
            while (baixo < limite)
            {
                alto = Math.BigMul(Proximo(), n, out baixo);
            }
        }

        return alto;
    }
}
