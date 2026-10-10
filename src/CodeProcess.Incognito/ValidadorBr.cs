using CodeProcess.Incognito.Documentos;

namespace CodeProcess.Incognito;

/// <summary>Validação de documentos brasileiros: estrutura e dígitos verificadores.</summary>
/// <remarks>
/// <para>
/// Nunca lança: texto nulo ou fora do formato devolve <see langword="false"/>. As sobrecargas com
/// <see cref="ReadOnlySpan{T}"/> não alocam.
/// </para>
/// <para>
/// CPF e CNPJ com todos os caracteres iguais são recusados mesmo com DV válido, porque a Receita não os emite: no CPF,
/// os 10 repdígitos; no CNPJ, os 14 caracteres iguais (com DV válido, só os 14 zeros). Um CNPJ de base repetida com
/// o seu DV continua válido.
/// </para>
/// <para>
/// DV válido não garante que não houve erro de digitação: o módulo 11 deixa passar algumas trocas, como letra e dígito
/// cujos valores ASCII − 48 diferem por múltiplo de 11 no CNPJ alfanumérico.
/// </para>
/// </remarks>
public static class ValidadorBr
{
    private const int TamanhoCpf = 11;
    private const int TamanhoCpfComMascara = 14;
    private const int TamanhoCnpj = 14;
    private const int TamanhoCnpjComMascara = 18;
    private const int TamanhoChaveDfe = 44;

    /// <summary>Valida um CPF com 11 dígitos ou com a máscara <c>000.000.000-00</c>.</summary>
    /// <param name="cpf">O CPF.</param>
    /// <returns><see langword="true"/> se a estrutura e os dois DVs estão corretos.</returns>
    public static bool Cpf(string? cpf) => cpf is not null && Cpf(cpf.AsSpan());

    /// <inheritdoc cref="Cpf(string?)"/>
    public static bool Cpf(ReadOnlySpan<char> cpf)
    {
        Span<char> digitos = stackalloc char[TamanhoCpf];
        if (cpf.Length == TamanhoCpfComMascara)
        {
            if (cpf[3] != '.' || cpf[7] != '.' || cpf[11] != '-')
            {
                return false;
            }

            cpf[..3].CopyTo(digitos);
            cpf[4..7].CopyTo(digitos[3..]);
            cpf[8..11].CopyTo(digitos[6..]);
            cpf[12..].CopyTo(digitos[9..]);
        }
        else if (cpf.Length == TamanhoCpf)
        {
            cpf.CopyTo(digitos);
        }
        else
        {
            return false;
        }

        return SoDigitos(digitos)
            && !EhRepetido(digitos)
            && DigitosVerificadores.Cpf(digitos[..9]) == DoisDigitos(digitos[9..]);
    }

    /// <summary>
    /// Valida um CNPJ numérico ou alfanumérico, com 14 caracteres ou com a máscara <c>00.000.000/0000-00</c>. Raiz e
    /// ordem aceitam dígitos e letras maiúsculas (letra minúscula é recusada); os dois DVs são dígitos.
    /// </summary>
    /// <param name="cnpj">O CNPJ.</param>
    /// <returns><see langword="true"/> se a estrutura e os dois DVs estão corretos.</returns>
    public static bool Cnpj(string? cnpj) => cnpj is not null && Cnpj(cnpj.AsSpan());

    /// <inheritdoc cref="Cnpj(string?)"/>
    public static bool Cnpj(ReadOnlySpan<char> cnpj)
    {
        Span<char> caracteres = stackalloc char[TamanhoCnpj];
        if (cnpj.Length == TamanhoCnpjComMascara)
        {
            if (cnpj[2] != '.' || cnpj[6] != '.' || cnpj[10] != '/' || cnpj[15] != '-')
            {
                return false;
            }

            cnpj[..2].CopyTo(caracteres);
            cnpj[3..6].CopyTo(caracteres[2..]);
            cnpj[7..10].CopyTo(caracteres[5..]);
            cnpj[11..15].CopyTo(caracteres[8..]);
            cnpj[16..].CopyTo(caracteres[12..]);
        }
        else if (cnpj.Length == TamanhoCnpj)
        {
            cnpj.CopyTo(caracteres);
        }
        else
        {
            return false;
        }

        return SoDigitosEMaiusculas(caracteres[..12])
            && SoDigitos(caracteres[12..])
            && !EhRepetido(caracteres)
            && DigitosVerificadores.Cnpj(caracteres[..12]) == DoisDigitos(caracteres[12..]);
    }

    /// <summary>
    /// Valida uma chave de acesso de DF-e de 44 posições, sem prefixo nem espaços: dígitos, com letras maiúsculas
    /// só no emitente (posições 7 a 18), e o cDV. Não confere cUF, AAMM, modelo, série nem o DV do emitente (que pode
    /// ser um CPF com <c>000</c> à esquerda).
    /// </summary>
    /// <param name="chave">A chave de acesso.</param>
    /// <returns><see langword="true"/> se a estrutura e o cDV estão corretos.</returns>
    public static bool ChaveDfe(string? chave) => chave is not null && ChaveDfe(chave.AsSpan());

    /// <inheritdoc cref="ChaveDfe(string?)"/>
    public static bool ChaveDfe(ReadOnlySpan<char> chave) =>
        chave.Length == TamanhoChaveDfe
        && SoDigitos(chave[..6])
        && SoDigitosEMaiusculas(chave[6..18])
        && SoDigitos(chave[18..])
        && DigitosVerificadores.ChaveDfe(chave[..43]) == chave[43] - '0';

    // Laços próprios em vez dos métodos genéricos de MemoryExtensions: ContainsAnyExceptInRange alocava 96 bytes por
    // chamada enquanto estava em tier-0 do JIT, o que o teste de alocação pega. Não trocar de volta.
    private static bool SoDigitos(ReadOnlySpan<char> texto)
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

    private static bool SoDigitosEMaiusculas(ReadOnlySpan<char> texto)
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

    private static bool EhRepetido(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (caractere != texto[0])
            {
                return false;
            }
        }

        return true;
    }

    private static int DoisDigitos(ReadOnlySpan<char> digitos) => ((digitos[0] - '0') * 10) + (digitos[1] - '0');
}
