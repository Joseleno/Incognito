using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Caminho até um nó, em JSON Pointer (RFC 6901), montado só quando um erro o pede: cada nó guarda o pai e o próprio
/// trecho, e nunca a cadeia inteira (uma chave longa copiada em cada descendente esgotaria a memória com um arquivo
/// pequeno).
/// </summary>
/// <remarks>
/// O texto de uma chave só entra no caminho onde ele é estrutura: as chaves do esquema, o nome de uma tabela sob
/// <c>tabelas</c>, de uma coluna sob <c>colunas</c>, de um domínio sob <c>dominios</c> e o nome de um parâmetro numa
/// regra, desde que seja um nome aceito, com até 128 caracteres e sem forma de segredo. Em qualquer outro lugar, e com
/// qualquer outro texto, o trecho é <c>?</c>: o caminho de um erro nunca devolve o que foi escrito no arquivo.
/// </remarks>
internal sealed partial class Caminho
{
    internal static readonly Caminho Raiz = new(null, "", Contexto.Raiz);

    private const int TamanhoMaximoDoTrecho = 128;

    private static readonly HashSet<string> _chavesDoEsquema = new(StringComparer.Ordinal)
    {
        "versao", "origem", "destino", "modo", "chave", "ambiente", "politica", "dominios", "tabelas", "provedor",
        "conexao", "bancoEsperado", "semClassificacao", "suspeitaSemRegra", "entradaInvalida", "entradaMalformada",
        "consistencia", "paralelismo", "acao", "colunas", "transformador", "dominio", "derivarDe", "classe",
        "justificativa",
    };

    private readonly Caminho? _pai;
    private readonly string _trecho;
    private readonly Contexto _contexto;

    private Caminho(Caminho? pai, string trecho, Contexto contexto)
    {
        _pai = pai;
        _trecho = trecho;
        _contexto = contexto;
    }

    private enum Contexto
    {
        Raiz,
        Fixo,
        Tabelas,
        Tabela,
        Colunas,
        Regra,
        Dominios,
        Livre,
    }

    /// <summary>O caminho do valor de uma chave deste mapa.</summary>
    internal Caminho Filho(string chave) => _contexto switch
    {
        Contexto.Raiz when chave == "tabelas" => new(this, chave, Contexto.Tabelas),
        Contexto.Raiz when chave == "dominios" => new(this, chave, Contexto.Dominios),
        Contexto.Raiz or Contexto.Fixo when _chavesDoEsquema.Contains(chave) => new(this, chave, Contexto.Fixo),
        Contexto.Tabelas when Identificadores.Partes(chave) is { Count: 2 } && Seguro(chave) => new(this, Escapar(chave), Contexto.Tabela),
        Contexto.Tabela when chave == "colunas" => new(this, chave, Contexto.Colunas),
        Contexto.Tabela when chave == "acao" => new(this, chave, Contexto.Fixo),
        Contexto.Colunas when Identificadores.Partes(chave) is { Count: 1 } && Seguro(chave) => new(this, Escapar(chave), Contexto.Regra),
        Contexto.Dominios when RegraDosNomes().IsMatch(chave) && chave.Length <= 64 => new(this, chave, Contexto.Regra),
        Contexto.Regra when _chavesDoEsquema.Contains(chave) || (NomeDeParametro().IsMatch(chave) && chave.Length <= 64 && Seguro(chave)) =>
            new(this, chave, Contexto.Fixo),
        _ => new(this, "?", Contexto.Livre),
    };

    /// <summary>O caminho de um item de lista.</summary>
    internal Caminho Item(int indice) =>
        new(this, indice.ToString(CultureInfo.InvariantCulture), _contexto == Contexto.Fixo ? Contexto.Fixo : Contexto.Livre);

    public override string ToString()
    {
        if (_pai is null)
        {
            return "";
        }

        var trechos = new Stack<string>();
        for (var atual = this; atual._pai is not null; atual = atual._pai)
        {
            trechos.Push(atual._trecho);
        }

        return "/" + string.Join('/', trechos);
    }

    private static bool Seguro(string chave) => chave.Length <= TamanhoMaximoDoTrecho && !VarreduraDeSegredos.Contem(chave);

    private static string Escapar(string chave) =>
        chave.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    [GeneratedRegex(@"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RegraDosNomes();

    [GeneratedRegex(@"\A[a-z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NomeDeParametro();
}

/// <summary>Nó da árvore montada a partir dos eventos do YAML, com a posição de início e o caminho.</summary>
internal abstract class NoYaml(Posicao posicao, Caminho caminho)
{
    internal Posicao Posicao { get; } = posicao;

    internal Caminho Caminho { get; } = caminho;

    /// <summary>O caminho em JSON Pointer; trechos que não são estrutura aceita aparecem como <c>?</c>.</summary>
    internal string Local => Caminho.ToString();

    /// <summary>O caminho, em texto, do valor de uma chave deste nó.</summary>
    internal string LocalDoFilho(string chave) => Caminho.Filho(chave).ToString();
}

/// <summary>Escalar: o texto já decodificado e se foi escrito sem aspas (só então tem tipo implícito).</summary>
internal sealed class EscalarYaml(Posicao posicao, Caminho caminho, string valor, bool simples) : NoYaml(posicao, caminho)
{
    internal string Valor { get; } = valor;

    /// <summary>Escrito sem aspas nem bloco (<c>plain</c>): só esse tem tipo implícito pelo core schema.</summary>
    internal bool Simples { get; } = simples;

    internal TipoEscalar Tipo => Simples ? TiposDoYaml.Classificar(Valor) : TipoEscalar.Texto;
}

/// <summary>Mapa, na ordem do arquivo; as chaves são escalares e não se repetem.</summary>
internal sealed class MapaYaml(Posicao posicao, Caminho caminho, IReadOnlyList<KeyValuePair<EscalarYaml, NoYaml>> entradas)
    : NoYaml(posicao, caminho)
{
    internal IReadOnlyList<KeyValuePair<EscalarYaml, NoYaml>> Entradas { get; } = entradas;
}

/// <summary>Lista, na ordem do arquivo.</summary>
internal sealed class ListaYaml(Posicao posicao, Caminho caminho, IReadOnlyList<NoYaml> itens) : NoYaml(posicao, caminho)
{
    internal IReadOnlyList<NoYaml> Itens { get; } = itens;
}
