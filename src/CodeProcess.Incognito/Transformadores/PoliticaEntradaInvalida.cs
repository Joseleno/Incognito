namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O que fazer com um documento de estrutura correta e DV errado (<c>entradaInvalida</c> na configuração). Só vale para
/// os transformadores com DV; os outros a ignoram.
/// </summary>
public enum PoliticaEntradaInvalida
{
    /// <summary>Pseudonimizar a base e manter o deslocamento do DV: inválido continua inválido (padrão).</summary>
    Preservar,

    /// <summary>Lançar <see cref="IncognitoException"/> (<c>INC1004</c>) sem o valor.</summary>
    Falhar,

    /// <summary>Gravar nulo.</summary>
    Anular,
}
