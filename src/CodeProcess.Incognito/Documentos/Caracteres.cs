using System.Diagnostics;

namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Conferência e leitura dos caracteres de documentos: só dígitos ASCII, só zeros, ou dígitos e letras maiúsculas
/// ASCII; o valor de um número em dígitos ASCII.
/// </summary>
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

    /// <summary>Se todos os caracteres são o dígito <c>0</c>.</summary>
    /// <param name="texto">O texto conferido.</param>
    /// <returns><see langword="true"/> se só há zeros (inclusive texto vazio).</returns>
    internal static bool SoZeros(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (caractere != '0')
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

    /// <summary>O número escrito em dígitos ASCII; quem chama já conferiu que são só dígitos.</summary>
    /// <param name="digitos">Até 9 dígitos ASCII.</param>
    /// <returns>O valor.</returns>
    internal static int Valor(ReadOnlySpan<char> digitos)
    {
        Debug.Assert(digitos.Length <= 9 && SoDigitos(digitos), "O valor é lido de até 9 dígitos ASCII.");
        var valor = 0;
        foreach (var digito in digitos)
        {
            valor = (valor * 10) + (digito - '0');
        }

        return valor;
    }
}
