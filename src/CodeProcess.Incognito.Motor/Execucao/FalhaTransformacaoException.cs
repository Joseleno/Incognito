using System.Globalization;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// Uma coluna tratada não pôde ser transformada numa linha (saída 1): o transformador lançou (<c>INC4007</c>), devolveu um
/// resultado fora da forma ou do contexto da coluna (<c>INC4005</c>), ou um valor lido não tem representação (<c>INC4006</c>:
/// na coluna, quando a política é falhar ou o transformador não a usa; na coluna de <c>derivarDe</c>, sempre). Traz a tabela, a coluna e o número da linha, nunca o valor: da exceção de um transformador de
/// fora sai só o tipo (e o código, quando tem a forma <c>INCnnnn</c>); a exceção original não vira interna e nada dela é
/// lido além disso.
/// </summary>
public sealed class FalhaTransformacaoException : IncognitoException
{
    internal FalhaTransformacaoException(string codigo, NomeTabela tabela, string coluna, long linha, string motivo)
        : base(codigo, Montar(tabela, coluna, linha, motivo))
    {
        Tabela = tabela;
        Coluna = coluna;
        Linha = linha;
    }

    /// <summary>A tabela, como o catálogo da origem a guarda.</summary>
    public NomeTabela Tabela { get; }

    /// <summary>A coluna, como o catálogo da origem a guarda (texto sem escape; para exibir, a mensagem).</summary>
    public string Coluna { get; }

    /// <summary>O número da linha na leitura da tabela, a partir de 1.</summary>
    public long Linha { get; }

    private static string Montar(NomeTabela tabela, string coluna, long linha, string motivo)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentException.ThrowIfNullOrEmpty(coluna);
        ArgumentOutOfRangeException.ThrowIfLessThan(linha, 1);
        ArgumentException.ThrowIfNullOrEmpty(motivo);
        return string.Create(CultureInfo.InvariantCulture, $"{new NomeColuna(tabela, coluna)}, linha {linha}: {motivo}");
    }
}
