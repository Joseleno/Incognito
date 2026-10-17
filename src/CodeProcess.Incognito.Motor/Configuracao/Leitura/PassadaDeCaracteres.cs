using System.Globalization;
using System.Text;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Confere o texto inteiro, comentários inclusive, antes do YAML, e depois cada valor já decodificado. Recusa o que o
/// YamlDotNet aceita em silêncio e um editor ou o diff mostram diferente ou nem mostram: controles C0 (exceto tab, LF e
/// CR seguido de LF) e C1, CR sozinho, as quebras de linha Unicode (NEL, LS e PS, que o YamlDotNet trata como quebra e
/// o diff não, escondendo uma linha numa revisão), os controles bidirecionais ("Trojan Source"), o BOM fora do início,
/// surrogate sem par, todo caractere de formato (em qualquer plano), os ignoráveis por padrão (seletores de variação,
/// preenchedores Hangul, tags) e os de uso privado. Cada caractere é classificado pelo code point, não pela metade
/// UTF-16.
/// </summary>
internal static class PassadaDeCaracteres
{
    /// <summary>Tira o BOM do início, se houver, e devolve o texto a ler; registra cada caractere recusado.</summary>
    internal static string Conferir(string texto, ColetorDeErros erros)
    {
        if (texto.Length > 0 && texto[0] == (char)0xFEFF)
        {
            texto = texto[1..];
        }

        var linha = 1;
        var coluna = 1;
        for (var indice = 0; indice < texto.Length; indice++)
        {
            var caractere = texto[indice];
            if (caractere == '\r')
            {
                if (indice + 1 < texto.Length && texto[indice + 1] == '\n')
                {
                    indice++;
                }
                else
                {
                    // CR sozinho: o YAML o lê como quebra, mas o terminal volta ao começo da linha e o que vem depois
                    // cobre o que veio antes.
                    erros.Adicionar(CodigosConfiguracao.Caractere, new Posicao(linha, coluna), "", "CR sem LF: use LF ou CRLF.");
                }

                linha++;
                coluna = 1;
                continue;
            }

            if (caractere == '\n')
            {
                linha++;
                coluna = 1;
                continue;
            }

            var (motivo, largura) = Ler(texto, indice);
            if (motivo is not null)
            {
                erros.Adicionar(CodigosConfiguracao.Caractere, new Posicao(linha, coluna), "", motivo);
            }

            indice += largura - 1;
            coluna += largura;
        }

        return texto;
    }

    /// <summary>
    /// Confere cada escalar já decodificado: um escape do YAML (<c>\e</c>, <c>\x00</c>, <c>\u202E</c>,
    /// <c>\U000E0041</c>) traria de volta o que a passada no texto recusa. Tab e LF continuam aceitos (escalares em
    /// bloco).
    /// </summary>
    internal static void ConferirEscalares(NoYaml no, ColetorDeErros erros)
    {
        switch (no)
        {
            case EscalarYaml escalar:
                ConferirEscalar(escalar, erros);
                break;
            case MapaYaml mapa:
                foreach (var (chave, valor) in mapa.Entradas)
                {
                    ConferirEscalar(chave, erros);
                    ConferirEscalares(valor, erros);
                }

                break;
            case ListaYaml lista:
                foreach (var item in lista.Itens)
                {
                    ConferirEscalares(item, erros);
                }

                break;
        }
    }

    /// <summary>
    /// Por que o caractere é recusado, ou nulo. Tab, LF e CR são tratados por quem chama; aqui o tab é aceito.
    /// </summary>
    internal static string? Motivo(Rune caractere)
    {
        var valor = caractere.Value;
        return valor switch
        {
            0x09 => null,
            0x85 or 0x2028 or 0x2029 =>
                "Quebra de linha Unicode (NEL, LS ou PS): o YAML a trata como quebra e editores e o diff não; use LF ou CRLF.",
            < 0x20 or 0x7F or (>= 0x80 and <= 0x9F) => "Caractere de controle; só tab, LF e CR são aceitos.",
            (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C =>
                "Controle bidirecional: muda a ordem em que o texto aparece num editor ou no diff.",
            0xFEFF => "BOM fora do início do arquivo.",
            _ when Rune.GetUnicodeCategory(caractere) == UnicodeCategory.Format || IgnoravelPorPadrao(valor) =>
                "Caractere invisível (largura zero, seletor de variação, preenchedor, tag e semelhantes): some no editor e no diff.",
            _ when Rune.GetUnicodeCategory(caractere) == UnicodeCategory.PrivateUse =>
                "Caractere de uso privado: não tem forma definida e pode não aparecer.",
            _ => null,
        };
    }

    // Os ignoráveis por padrão que não são de formato, e o espaço em branco do braile (que é letra e não aparece).
    private static bool IgnoravelPorPadrao(int valor) =>
        valor is 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or (>= 0x180B and <= 0x180F) or 0x3164
            or (>= 0xFE00 and <= 0xFE0F) or 0xFFA0 or (>= 0xE0000 and <= 0xE0FFF) or 0x2800;

    private static void ConferirEscalar(EscalarYaml escalar, ColetorDeErros erros)
    {
        var valor = escalar.Valor;
        for (var indice = 0; indice < valor.Length; indice++)
        {
            var caractere = valor[indice];
            if (caractere is '\n')
            {
                continue;
            }

            var (motivo, largura) = caractere == '\r' ? ("CR dentro de um valor.", 1) : Ler(valor, indice);
            if (motivo is not null)
            {
                erros.Adicionar(CodigosConfiguracao.Caractere, escalar.Posicao, "", "Valor com caractere proibido (escrito por escape do YAML): " + motivo);
                return;
            }

            indice += largura - 1;
        }
    }

    // O motivo da recusa do caractere que começa no índice e quantas unidades UTF-16 ele ocupa.
    private static (string? Motivo, int Largura) Ler(string texto, int indice)
    {
        var caractere = texto[indice];
        if (char.IsHighSurrogate(caractere) && indice + 1 < texto.Length && char.IsLowSurrogate(texto[indice + 1]))
        {
            return (Motivo(new Rune(caractere, texto[indice + 1])), 2);
        }

        return char.IsSurrogate(caractere) ? ("Surrogate UTF-16 sem par.", 1) : (Motivo(new Rune(caractere)), 1);
    }
}
