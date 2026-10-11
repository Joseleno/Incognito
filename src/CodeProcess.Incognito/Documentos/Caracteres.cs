namespace CodeProcess.Incognito.Documentos;

/// <summary>Conferência dos caracteres de documentos: só dígitos ASCII, ou dígitos e letras maiúsculas ASCII.</summary>
/// <remarks>
/// Laços próprios em vez dos métodos genéricos de <see cref="MemoryExtensions"/>: <c>ContainsAnyExceptInRange</c>
/// alocava 96 bytes por chamada enquanto estava em tier-0 do JIT, o que o teste de alocação do
/// <see cref="ValidadorBr"/> pega. Não trocar de volta.
/// </remarks>
internal static class Caracteres
{
    /// <summary>Se todos os caracteres são dígitos ASCII (<c>0</c> a <c>9</c>).</summary>
    /// <param name="texto">O texto conferido.</param>
    /// <returns><see langword="true"/> se só há dígitos ASCII (inclusive texto vazio).</returns>
    internal static bool SoDigitos(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (!char.IsAsciiDigit(caractere))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Se todos os caracteres são dígitos ou letras maiúsculas ASCII (<c>0-9</c>, <c>A-Z</c>).</summary>
    /// <param name="texto">O texto conferido.</param>
    /// <returns><see langword="true"/> se só há dígitos e letras maiúsculas ASCII (inclusive texto vazio).</returns>
    internal static bool SoDigitosEMaiusculas(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (!char.IsAsciiDigit(caractere) && !char.IsAsciiLetterUpper(caractere))
            {
                return false;
            }
        }

        return true;
    }
}
