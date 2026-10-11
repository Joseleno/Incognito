namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>O que fazer com um documento de estrutura correta e DV errado.</summary>
internal enum PoliticaEntradaInvalida
{
    /// <summary>Pseudonimizar a base e manter o deslocamento do DV: inválido continua inválido.</summary>
    Preservar,

    /// <summary>Lançar <see cref="IncognitoException"/> sem o valor.</summary>
    Falhar,

    /// <summary>Gravar nulo.</summary>
    Anular,
}
