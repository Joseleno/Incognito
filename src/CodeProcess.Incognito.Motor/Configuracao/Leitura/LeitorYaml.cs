using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Monta a árvore do YAML a partir dos eventos do <see cref="Parser"/>, nunca pelo <c>YamlStream</c> nem pelo
/// desserializador. O <c>YamlStream</c> é recursivo (estoura a pilha, sem chance de captura, com uns 10 KB de
/// colchetes) e resolve aliases e o hash de chaves complexas antes de qualquer validação (a bomba de aliases trava em
/// menos de 600 bytes). Aqui a profundidade é contada em cada início de mapa ou lista, e âncora, alias, tag, diretiva e
/// segundo documento são recusados no próprio evento, antes de montar qualquer coisa.
/// </summary>
/// <remarks>
/// As mensagens do YamlDotNet nunca são repassadas, nem como exceção interna: algumas ecoam o texto do arquivo (a chave
/// repetida, o nome do alias). Cada caso vira uma mensagem própria, com a posição do evento.
/// </remarks>
internal sealed class LeitorYaml
{
    /// <summary>Profundidade máxima de mapas e listas aninhados; o <c>incognito.yaml</c> usa no máximo 5.</summary>
    internal const int ProfundidadeMaxima = 16;

    /// <summary>Máximo de nós (escalares, mapas e listas); o rascunho de uma base grande fica bem abaixo.</summary>
    internal const int MaximoDeNos = 1_000_000;

    private readonly Parser _parser;
    private readonly ColetorDeErros _erros;
    private int _nos;

    private LeitorYaml(string texto, ColetorDeErros erros)
    {
        _parser = new Parser(new StringReader(texto));
        _erros = erros;
    }

    /// <summary>A raiz do documento, ou nulo se o YAML não pôde ser lido (os erros ficam no coletor).</summary>
    internal static NoYaml? Ler(string texto, ColetorDeErros erros)
    {
        if (Diretiva(texto) is { } linha)
        {
            erros.Adicionar(CodigosConfiguracao.ConstrucaoNaoAceita, new Posicao(linha, 1), "", "Diretiva do YAML (linha começando com '%') não é aceita.");
            return null;
        }

        try
        {
            return new LeitorYaml(texto, erros).Documento();
        }
        catch (LeituraInterrompida)
        {
            return null;
        }
    }

    private NoYaml? Documento()
    {
        Avancar();
        Esperar<StreamStart>();
        Avancar();
        if (_parser.Current is StreamEnd)
        {
            _erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O arquivo está vazio.");
            return null;
        }

        var inicio = Esperar<DocumentStart>();
        if (inicio.Version is not null)
        {
            Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, inicio, "Diretiva do YAML não é aceita.");
        }

        Avancar();
        var raiz = No(1, Caminho.Raiz);
        Avancar();
        Esperar<DocumentEnd>();
        Avancar();
        if (_parser.Current is DocumentStart outro)
        {
            Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, outro, "O arquivo tem mais de um documento YAML; use um só.");
        }

        Esperar<StreamEnd>();
        if (raiz is EscalarYaml { Tipo: TipoEscalar.Nulo })
        {
            _erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O arquivo está vazio.");
            return null;
        }

        return raiz;
    }

    // O evento atual é o início do nó; ao voltar, o evento atual é o fim dele.
    private NoYaml No(int profundidade, Caminho caminho)
    {
        var evento = _parser.Current;
        if (++_nos > MaximoDeNos)
        {
            Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, evento, "O arquivo tem nós demais (mais de 1 milhão de valores, mapas e listas).");
        }

        switch (evento)
        {
            case AnchorAlias:
                Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, evento, "Alias do YAML ('*nome') não é aceito; escreva o valor por extenso.");
                break;
            case NodeEvent { Anchor.IsEmpty: false }:
                Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, evento, "Âncora do YAML ('&nome') não é aceita.");
                break;
            case NodeEvent { Tag.IsEmpty: false }:
                Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, evento, "Tag do YAML ('!' ou '!!') não é aceita.");
                break;
        }

        switch (evento)
        {
            case Scalar escalar:
                return new EscalarYaml(Inicio(escalar), caminho, escalar.Value, escalar.Style == ScalarStyle.Plain);
            case MappingStart:
                ConferirProfundidade(profundidade, evento);
                return Mapa(profundidade, caminho, Inicio(evento));
            case SequenceStart:
                ConferirProfundidade(profundidade, evento);
                return Lista(profundidade, caminho, Inicio(evento));
            default:
                Interromper(CodigosConfiguracao.Sintaxe, evento, "Estrutura do YAML inesperada.");
                return null!;
        }
    }

    private MapaYaml Mapa(int profundidade, Caminho caminho, Posicao posicao)
    {
        var entradas = new List<KeyValuePair<EscalarYaml, NoYaml>>();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            Avancar();
            if (_parser.Current is MappingEnd)
            {
                return new MapaYaml(posicao, caminho, entradas);
            }

            if (_parser.Current is not Scalar)
            {
                Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, _parser.Current, "Chave de mapa que não é texto não é aceita.");
            }

            var chave = (EscalarYaml)No(profundidade + 1, caminho);
            Avancar();
            var valor = No(profundidade + 1, caminho.Filho(chave.Valor));
            if (vistas.Add(chave.Valor))
            {
                entradas.Add(new(chave, valor));
            }
            else
            {
                // O local é o do mapa: a chave repetida não volta na mensagem.
                _erros.Adicionar(CodigosConfiguracao.ChaveRepetida, chave.Posicao, caminho.ToString(), "Chave repetida no mesmo mapa.");
            }
        }
    }

    private ListaYaml Lista(int profundidade, Caminho caminho, Posicao posicao)
    {
        var itens = new List<NoYaml>();
        while (true)
        {
            Avancar();
            if (_parser.Current is SequenceEnd)
            {
                return new ListaYaml(posicao, caminho, itens);
            }

            itens.Add(No(profundidade + 1, caminho.Item(itens.Count)));
        }
    }

    private void ConferirProfundidade(int profundidade, ParsingEvent evento)
    {
        if (profundidade > ProfundidadeMaxima)
        {
            Interromper(CodigosConfiguracao.ConstrucaoNaoAceita, evento, "Mapas e listas aninhados demais (mais de 16 níveis).");
        }
    }

    private void Avancar()
    {
        try
        {
            if (!_parser.MoveNext())
            {
                _erros.Adicionar(CodigosConfiguracao.Sintaxe, null, "", "O YAML terminou antes do esperado.");
                throw new LeituraInterrompida();
            }
        }
        catch (YamlException excecao)
        {
            // Nunca a mensagem do YamlDotNet: só a posição.
            _erros.Adicionar(CodigosConfiguracao.Sintaxe, Converter(excecao.Start), "", MensagemDeSintaxe);
            throw new LeituraInterrompida();
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException or FormatException or OverflowException)
        {
            // O scanner do YamlDotNet lança exceções fora da hierarquia YamlException em entradas malformadas (achadas
            // pelo teste de propriedade). Sem posição confiável: a do último evento lido.
            _erros.Adicionar(CodigosConfiguracao.Sintaxe, _parser.Current is { } ultimo ? Inicio(ultimo) : null, "", MensagemDeSintaxe);
            throw new LeituraInterrompida();
        }
    }

    private const string MensagemDeSintaxe =
        "YAML com erro de sintaxe nesta posição (um valor com ': ', '#' ou terminado em ':' precisa de aspas).";

    private T Esperar<T>()
        where T : ParsingEvent
    {
        if (_parser.Current is T evento)
        {
            return evento;
        }

        Interromper(CodigosConfiguracao.Sintaxe, _parser.Current, "Estrutura do YAML inesperada.");
        return null!;
    }

    private void Interromper(string codigo, ParsingEvent? evento, string mensagem)
    {
        _erros.Adicionar(codigo, evento is null ? null : Inicio(evento), "", mensagem);
        throw new LeituraInterrompida();
    }

    private static Posicao Inicio(ParsingEvent evento) => new((int)evento.Start.Line, (int)evento.Start.Column);

    private static Posicao? Converter(Mark marca) =>
        marca.Line < 1 ? null : new Posicao((int)marca.Line, (int)marca.Column);

    // Diretiva ('%' no começo de uma linha antes do conteúdo): o YamlDotNet ignora as desconhecidas em silêncio. Só
    // linhas em branco e comentários podem vir antes dela; depois do conteúdo, um '%' no começo da linha é parte de um
    // escalar em várias linhas (ou erro de sintaxe, que o Parser acusa).
    private static int? Diretiva(string texto)
    {
        var linha = 0;
        foreach (var conteudo in texto.Split('\n'))
        {
            linha++;
            var semEspacos = conteudo.TrimStart(' ', '\t', '\r');
            if (conteudo.StartsWith('%'))
            {
                return linha;
            }

            if (semEspacos.Length > 0 && !semEspacos.StartsWith('#'))
            {
                return null;
            }
        }

        return null;
    }

    private sealed class LeituraInterrompida : Exception;
}
