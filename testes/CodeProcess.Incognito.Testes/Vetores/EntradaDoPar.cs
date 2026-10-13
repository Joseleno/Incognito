using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>A entrada de um par: texto ou inteiro, e a entidade no CEP.</summary>
internal readonly record struct EntradaDoPar(string? Texto, long Numero, bool EhNumero, string? Entidade)
{
    private static readonly SearchValues<char> _hexadecimais = SearchValues.Create("0123456789ABCDEF");

    internal static EntradaDoPar DeTexto(string texto, string? entidade = null) => new(texto, 0, false, entidade);

    internal static EntradaDoPar DeNumero(long numero, string? entidade = null) => new(null, numero, true, entidade);

    /// <summary>Lê a entrada (e a entidade) de um par já conferido pelo esquema.</summary>
    internal static EntradaDoPar Ler(JsonElement par)
    {
        string? entidade = par.TryGetProperty("entidade", out var e) ? e.GetString()
            : par.TryGetProperty("entidadeUtf16", out var eu) ? DeUtf16(eu.GetString()!)
            : null;
        if (par.TryGetProperty("entradaUtf16", out var utf16))
        {
            return DeTexto(DeUtf16(utf16.GetString()!), entidade);
        }

        var entrada = par.GetProperty("entrada");
        return entrada.ValueKind == JsonValueKind.Number ? DeNumero(entrada.GetInt64(), entidade) : DeTexto(entrada.GetString()!, entidade);
    }

    /// <summary>
    /// Texto com UTF-16 inválido (surrogate sem par), que o JSON não carrega como texto: as unidades em hexadecimal, 4
    /// dígitos maiúsculos cada.
    /// </summary>
    internal static string DeUtf16(string hexadecimais)
    {
        if (hexadecimais.Length == 0 || hexadecimais.Length % 4 != 0)
        {
            throw new InvalidDataException("Texto em UTF-16 com tamanho que não é múltiplo de 4.");
        }

        var texto = new StringBuilder(hexadecimais.Length / 4);
        for (var i = 0; i < hexadecimais.Length; i += 4)
        {
            if (hexadecimais.AsSpan(i, 4).ContainsAnyExcept(_hexadecimais)
                || !ushort.TryParse(hexadecimais.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unidade))
            {
                throw new InvalidDataException("Texto em UTF-16 com hexadecimal inválido.");
            }

            texto.Append((char)unidade);
        }

        return texto.ToString();
    }

    internal static string ParaUtf16(string texto)
    {
        var hexadecimais = new StringBuilder(texto.Length * 4);
        foreach (var unidade in texto)
        {
            hexadecimais.Append(((int)unidade).ToString("X4", CultureInfo.InvariantCulture));
        }

        return hexadecimais.ToString();
    }

    /// <summary>Verdadeiro se o texto tem surrogate sem par (não cabe como texto no JSON).</summary>
    internal static bool TemUtf16Invalido(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            if (char.IsHighSurrogate(texto[i]) && i + 1 < texto.Length && char.IsLowSurrogate(texto[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(texto[i]))
            {
                return true;
            }
        }

        return false;
    }
}
