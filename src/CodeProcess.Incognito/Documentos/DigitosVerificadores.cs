using System.Diagnostics;

namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Cálculo dos dígitos verificadores por módulo 11. Cada caractere vale ASCII − 48, o que dá o próprio dígito de
/// '0' a '9' e 17 a 42 para as letras de 'A' a 'Z' (CNPJ alfanumérico e chave de DF-e). Esse valor serve só para o
/// DV: usá-lo como número para codificar a raiz geraria colisões ("0000000Z" e "00000016" valeriam ambos 42).
/// </summary>
/// <remarks>Quem chama já conferiu tamanho e caracteres.</remarks>
internal static class DigitosVerificadores
{
    /// <summary>Os dois DVs do CPF, como número de 0 a 99.</summary>
    /// <param name="base9">Os 9 dígitos da base.</param>
    /// <returns>Primeiro DV × 10 + segundo DV.</returns>
    internal static int Cpf(ReadOnlySpan<char> base9)
    {
        Debug.Assert(base9.Length == 9, "A base do CPF tem 9 dígitos.");
        int soma1 = 0, soma2 = 0;
        for (var i = 0; i < 9; i++)
        {
            var digito = base9[i] - '0';
            soma1 += digito * (10 - i);
            soma2 += digito * (11 - i);
        }

        var primeiro = Modulo11(soma1);
        var segundo = Modulo11(soma2 + (primeiro * 2));
        return (primeiro * 10) + segundo;
    }

    /// <summary>Os dois DVs do CNPJ, numérico ou alfanumérico, como número de 0 a 99.</summary>
    /// <param name="base12">Os 12 caracteres da raiz e da ordem, dígitos ou letras maiúsculas.</param>
    /// <returns>Primeiro DV × 10 + segundo DV.</returns>
    internal static int Cnpj(ReadOnlySpan<char> base12)
    {
        Debug.Assert(base12.Length == 12, "A base do CNPJ tem 12 caracteres.");

        // Pesos do segundo DV; os do primeiro são os mesmos a partir da segunda posição.
        ReadOnlySpan<byte> pesos = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int soma1 = 0, soma2 = 0;
        for (var i = 0; i < 12; i++)
        {
            var valor = base12[i] - '0';
            soma1 += valor * pesos[i + 1];
            soma2 += valor * pesos[i];
        }

        var primeiro = Modulo11(soma1);
        var segundo = Modulo11(soma2 + (primeiro * pesos[12]));
        return (primeiro * 10) + segundo;
    }

    /// <summary>
    /// O cDV da chave de acesso de DF-e, sobre os <b>43</b> primeiros caracteres, com pesos de 2 a 9 cíclicos a partir
    /// da direita (código do Anexo II da NT Conjunta 2025.001; o texto da NT fala em 44, o código usa 43).
    /// </summary>
    /// <param name="primeiros43">Os 43 caracteres antes do cDV.</param>
    /// <returns>O cDV, de 0 a 9.</returns>
    internal static int ChaveDfe(ReadOnlySpan<char> primeiros43)
    {
        Debug.Assert(primeiros43.Length == 43, "O cDV é calculado sobre 43 caracteres.");
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (primeiros43[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        return Modulo11(soma);
    }

    // Resto 0 ou 1 dá DV 0; os demais, 11 − resto.
    private static int Modulo11(int soma)
    {
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
