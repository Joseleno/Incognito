using System.Text;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Procura segredo por extenso no arquivo inteiro: a chave do Incognito, uma senha de string de conexão e uma URL com
/// usuário e senha. Em dois níveis: linha a linha no texto bruto, que cobre os comentários, e em cada escalar já
/// decodificado (chaves de mapa inclusive), que cobre os escapes (<c>\x5f</c>, <c>\u0069</c>), a quebra de linha escapada
/// entre aspas e o escalar dobrado em várias linhas. Cada texto é conferido também sem espaços nem caracteres
/// invisíveis e na forma de compatibilidade (NFKC), que junta a chave partida e desfaz as letras de largura total.
/// </summary>
/// <remarks>
/// As expressões não voltam atrás (<see cref="RegexOptions.NonBacktracking"/>) e têm partes limitadas: o tempo é linear
/// no tamanho do arquivo. A chave é procurada pelo prefixo, sem caixa, seguido de 20 caracteres de base64
/// (padrão ou URL): pega a chave inteira, com sufixo, em base64 padrão ou um pedaço dela. Não dá para separar de um hash
/// uma chave sem o prefixo ou em hexadecimal, nem juntar uma chave dividida em dois campos: fica como limite.
/// </remarks>
internal static class VarreduraDeSegredos
{
    private static readonly (Regex Padrao, string Mensagem)[] _padroes =
    [
        (new Regex("inc1_[A-Za-z0-9+/=_-]{20}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking),
            "Chave do Incognito por extenso no arquivo. Use 'chave: arquivo:caminho' ou 'chave: ambiente:NOME' e troque a chave: ela já pode ter vazado."),
        (new Regex(@"\b(?:password|pwd|psw|senha)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking),
            "Senha por extenso no arquivo (parece uma string de conexão). Use 'conexao: ambiente:NOME' ou 'conexao: arquivo:caminho' e troque a senha."),
        (new Regex(@"[A-Za-z][A-Za-z0-9+.-]{0,31}://[^/\s:@]{0,1024}:[^@\s]{1,1024}@", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking),
            "URL com usuário e senha por extenso no arquivo. Use 'conexao: ambiente:NOME' ou 'conexao: arquivo:caminho' e troque a senha."),
    ];

    /// <summary>Se o texto contém um segredo por extenso, em qualquer das formas conferidas.</summary>
    internal static bool Contem(string texto) =>
        _padroes.Any(padrao => padrao.Padrao.IsMatch(texto) || padrao.Padrao.IsMatch(Variante(texto)));

    internal static void Varrer(string texto, NoYaml? raiz, ColetorDeErros erros)
    {
        var achados = new HashSet<(int Linha, int Padrao)>();
        var numero = 0;
        foreach (var linha in texto.Split('\n'))
        {
            numero++;
            var semQuebra = linha.TrimEnd('\r');
            var variante = (string?)null;
            for (var indice = 0; indice < _padroes.Length && !erros.SegredosCheios; indice++)
            {
                var (padrao, mensagem) = _padroes[indice];
                var coluna = 0;
                foreach (var achado in padrao.EnumerateMatches(semQuebra))
                {
                    coluna = achado.Index + 1;
                    break;
                }

                if (coluna == 0 && padrao.IsMatch(variante ??= Variante(semQuebra)))
                {
                    coluna = 1;
                }

                if (coluna > 0 && achados.Add((numero, indice)))
                {
                    erros.Adicionar(CodigosConfiguracao.SegredoPorExtenso, new Posicao(numero, coluna), "", mensagem);
                }
            }
        }

        if (raiz is not null)
        {
            VarrerEscalares(raiz, achados, erros);
        }
    }

    private static void VarrerEscalares(NoYaml no, HashSet<(int, int)> achados, ColetorDeErros erros)
    {
        switch (no)
        {
            case EscalarYaml escalar:
                ConferirEscalar(escalar, achados, erros);
                break;
            case MapaYaml mapa:
                foreach (var (chave, valor) in mapa.Entradas)
                {
                    ConferirEscalar(chave, achados, erros);
                    VarrerEscalares(valor, achados, erros);
                }

                break;
            case ListaYaml lista:
                foreach (var item in lista.Itens)
                {
                    VarrerEscalares(item, achados, erros);
                }

                break;
        }
    }

    // O local do escalar não entra: ele pode conter o próprio segredo (uma chave de mapa acima dele).
    private static void ConferirEscalar(EscalarYaml escalar, HashSet<(int, int)> achados, ColetorDeErros erros)
    {
        var variante = (string?)null;
        for (var indice = 0; indice < _padroes.Length && !erros.SegredosCheios; indice++)
        {
            var (padrao, mensagem) = _padroes[indice];
            if ((padrao.IsMatch(escalar.Valor) || padrao.IsMatch(variante ??= Variante(escalar.Valor)))
                && achados.Add((escalar.Posicao.Linha, indice)))
            {
                erros.Adicionar(CodigosConfiguracao.SegredoPorExtenso, escalar.Posicao, "", mensagem);
            }
        }
    }

    // Sem espaços, quebras e os caracteres que a passada recusa (invisíveis, de formato, de uso privado), em qualquer
    // plano, na forma de compatibilidade NFKC.
    private static string Variante(string texto)
    {
        var limpo = new StringBuilder(texto.Length);
        foreach (var caractere in texto.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(caractere) && PassadaDeCaracteres.Motivo(caractere) is null)
            {
                limpo.Append(caractere);
            }
        }

        var resultado = limpo.ToString();
        try
        {
            return resultado.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return resultado;
        }
    }
}
