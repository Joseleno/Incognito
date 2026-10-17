using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>Tipo de um escalar escrito sem aspas, pelo core schema do YAML 1.2.</summary>
internal enum TipoEscalar
{
    Texto,
    Nulo,
    Booleano,
    Inteiro,
    Decimal,
}

/// <summary>
/// O tipo implícito de um escalar sem aspas pelo core schema do YAML 1.2 (nada dos booleanos do YAML 1.1, como
/// <c>yes</c> e <c>on</c>, que são texto). O validador e a conversão para JSON dos testes do esquema usam a mesma regra,
/// para concordarem.
/// </summary>
internal static partial class TiposDoYaml
{
    internal static TipoEscalar Classificar(string valor)
    {
        if (valor.Length == 0 || valor is "~" or "null" or "Null" or "NULL")
        {
            return TipoEscalar.Nulo;
        }

        if (valor is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
        {
            return TipoEscalar.Booleano;
        }

        if (Inteiro().IsMatch(valor))
        {
            return TipoEscalar.Inteiro;
        }

        return Decimal().IsMatch(valor) ? TipoEscalar.Decimal : TipoEscalar.Texto;
    }

    [GeneratedRegex(@"\A(?:[-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Inteiro();

    [GeneratedRegex(@"\A(?:[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))\z", RegexOptions.CultureInvariant)]
    private static partial Regex Decimal();
}
