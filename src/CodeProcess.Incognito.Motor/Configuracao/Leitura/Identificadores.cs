using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Nomes de tabela e coluna como escritos no arquivo: identificadores separados por ponto, sem aspas (letra ou
/// <c>_</c>, depois letras, dígitos, <c>_</c> e <c>$</c>) ou entre aspas duplas (com <c>""</c> para uma aspa). Sem marca
/// combinante, caractere invisível, espaço que não seja o comum, preenchedor Hangul (letra que não aparece) nem texto
/// fora da forma NFC: grafias que parecem iguais não podem virar regras para objetos diferentes.
/// </summary>
internal static partial class Identificadores
{
    /// <summary>As partes, ou nulo se o texto não é uma sequência de identificadores aceitos.</summary>
    internal static List<Identificador>? Partes(string texto)
    {
        var partes = new List<Identificador>();
        var indice = 0;
        while (true)
        {
            if (indice >= texto.Length)
            {
                return null;
            }

            if (texto[indice] == '"')
            {
                var nome = new StringBuilder();
                indice++;
                while (true)
                {
                    if (indice >= texto.Length)
                    {
                        return null;
                    }

                    if (texto[indice] == '"')
                    {
                        if (indice + 1 < texto.Length && texto[indice + 1] == '"')
                        {
                            nome.Append('"');
                            indice += 2;
                            continue;
                        }

                        indice++;
                        break;
                    }

                    nome.Append(texto[indice]);
                    indice++;
                }

                var entreAspas = nome.ToString();
                if (entreAspas.Length == 0 || !Aceito(entreAspas))
                {
                    return null;
                }

                partes.Add(new Identificador(entreAspas, EntreAspas: true));
            }
            else
            {
                var inicio = indice;
                while (indice < texto.Length && texto[indice] != '.')
                {
                    indice++;
                }

                var nome = texto[inicio..indice];
                if (!Simples().IsMatch(nome) || !Aceito(nome))
                {
                    return null;
                }

                partes.Add(new Identificador(nome, EntreAspas: false));
            }

            if (indice == texto.Length)
            {
                return partes;
            }

            if (texto[indice] != '.')
            {
                return null;
            }

            indice++;
        }
    }

    private static bool Aceito(string nome) =>
        nome.All(CaractereAceito) && Normalizado(nome);

    private static bool CaractereAceito(char caractere) =>
        caractere == ' '
        || (caractere is not ('\u115F' or '\u1160' or '\u3164' or '\uFFA0') && CharUnicodeInfo.GetUnicodeCategory(caractere) is not (
            UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned));

    // Só confere; nunca normaliza (um nome fora de NFC é recusado, não trocado em silêncio).
    private static bool Normalizado(string nome)
    {
        if (Ascii().IsMatch(nome))
        {
            return true;
        }

        try
        {
            return nome.IsNormalized(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"\A[\p{L}_][\p{L}\p{Nd}_$]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Simples();

    [GeneratedRegex(@"\A[\x20-\x7E]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Ascii();
}
