using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Configuracao.Leitura;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O escape de todo texto que o motor não escreveu (nomes do catálogo, mensagem de dialeto) antes de ir para uma mensagem:
/// o que a passada de caracteres do YAML recusa (controles, quebras Unicode, controles bidirecionais, invisíveis, uso
/// privado), o tab e o surrogate sem par viram <c>\uXXXX</c>, e a barra invertida vira <c>\\</c>, para o escape ser
/// injetivo (um nome com o texto <c>\u000A</c> não fica igual a uma quebra escapada).
/// </summary>
/// <remarks>
/// No YAML esses caracteres já são recusados; no catálogo, nunca: um nome de tabela com ESC ou com U+202E mudaria o que um
/// terminal ou um editor mostram.
/// </remarks>
internal static class TextoSeguro
{
    /// <summary>Até quantas unidades UTF-16 uma mensagem vinda de fora do motor sai inteira; acima, é cortada e ganha <c>…</c>.</summary>
    internal const int MaximoDaMensagem = 500;

    /// <summary>O texto com o escape, inteiro.</summary>
    /// <param name="texto">O texto do catálogo ou do dialeto.</param>
    /// <returns>O texto escapado.</returns>
    internal static string Nome(string texto) => Escapar(texto, int.MaxValue);

    /// <summary>
    /// O texto com o escape, com no máximo <see cref="MaximoDaMensagem"/> unidades mais as reticências: o corte vem depois do
    /// escape, sem partir um par surrogate nem uma sequência de escape, e termina em <c>…</c>.
    /// </summary>
    /// <param name="texto">A mensagem de fora do motor.</param>
    /// <returns>A mensagem escapada e cortada.</returns>
    internal static string Mensagem(string texto) => Escapar(texto, MaximoDaMensagem);

    private static string Escapar(string texto, int maximo)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var saida = new StringBuilder(Math.Min(texto.Length, maximo));
        for (var indice = 0; indice < texto.Length;)
        {
            var (pedaco, largura) = Pedaco(texto, indice);
            if (saida.Length + pedaco.Length > maximo)
            {
                return saida.Append('…').ToString();
            }

            saida.Append(pedaco);
            indice += largura;
        }

        return saida.ToString();
    }

    // O que sai no lugar do caractere que começa no índice, e quantas unidades UTF-16 ele ocupa.
    private static (string Pedaco, int Largura) Pedaco(string texto, int indice)
    {
        var caractere = texto[indice];
        if (caractere == '\\')
        {
            return ("\\\\", 1);
        }

        if (char.IsHighSurrogate(caractere) && indice + 1 < texto.Length && char.IsLowSurrogate(texto[indice + 1]))
        {
            var par = texto.Substring(indice, 2);
            return (PassadaDeCaracteres.Motivo(new Rune(caractere, texto[indice + 1])) is null ? par : Unidades(par), 2);
        }

        if (char.IsSurrogate(caractere) || caractere == '\t' || PassadaDeCaracteres.Motivo(new Rune(caractere)) is not null)
        {
            return (Unidades(caractere.ToString()), 1);
        }

        return (caractere.ToString(), 1);
    }

    private static string Unidades(string unidades) =>
        string.Concat(unidades.Select(unidade => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)unidade:X4}")));
}
