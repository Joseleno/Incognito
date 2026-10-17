namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O que fazer com um valor que não tem a forma do documento, como tamanho ou caracteres errados
/// (<c>entradaMalformada</c> na configuração).
/// </summary>
public enum PoliticaEntradaMalformada
{
    /// <summary>Lançar <see cref="IncognitoException"/> (<c>INC1003</c>) sem o valor (padrão: falha segura).</summary>
    Falhar,

    /// <summary>Gravar nulo.</summary>
    Anular,

    /// <summary>Copiar o valor como está, em claro; só vale escrito, e o relatório avisa.</summary>
    Preservar,
}
