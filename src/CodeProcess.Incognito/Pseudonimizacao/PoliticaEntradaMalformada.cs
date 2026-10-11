namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>O que fazer com um valor que não tem a forma do documento (tamanho ou caracteres errados).</summary>
internal enum PoliticaEntradaMalformada
{
    /// <summary>Lançar <see cref="IncognitoException"/> sem o valor (padrão: falha segura).</summary>
    Falhar,

    /// <summary>Gravar nulo.</summary>
    Anular,

    /// <summary>Copiar o valor como está, em claro.</summary>
    Preservar,
}
