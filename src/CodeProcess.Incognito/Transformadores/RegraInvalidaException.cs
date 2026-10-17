namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Regra de transformação recusada pela fábrica (<c>INC1005</c>): parâmetro desconhecido ou fora da forma,
/// <c>derivarDe</c> que falta ou sobra, tipo ou comprimento da coluna que o transformador não atende. O motor a mostra
/// como erro de configuração (<c>INC2022</c>) apontando a regra ou o parâmetro.
/// </summary>
/// <remarks>A mensagem fala da regra, nunca de valores de coluna.</remarks>
public sealed class RegraInvalidaException : IncognitoException
{
    /// <summary>O código do erro.</summary>
    public const string CodigoDoErro = "INC1005";

    /// <summary>Cria a exceção.</summary>
    /// <param name="mensagem">O que está errado na regra, sem valores de coluna.</param>
    /// <param name="parametro">O nome do parâmetro recusado; nulo quando o problema é da regra ou da coluna.</param>
    public RegraInvalidaException(string mensagem, string? parametro = null)
        : base(CodigoDoErro, mensagem)
    {
        Parametro = parametro;
    }

    /// <summary>O nome do parâmetro recusado; nulo quando o problema é da regra ou da coluna.</summary>
    public string? Parametro { get; }
}
