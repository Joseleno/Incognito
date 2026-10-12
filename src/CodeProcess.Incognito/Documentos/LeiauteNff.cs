namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Partes do número da chave na Nota Fiscal Fácil, que embute a data de emissão. Os índices contam a partir de zero
/// sobre as 44 posições (a posição 26 é o índice 25).
/// </summary>
/// <param name="indiceDia">Índice do dia, com 2 dígitos.</param>
/// <param name="indiceMes">Índice do mês, com 2 dígitos.</param>
/// <param name="indiceTipo">Índice do tipo do emissor (1 CNPJ, 2 CPF), ou nulo se o modelo não o tem.</param>
/// <param name="indiceSequencial">Índice do sequencial diário.</param>
/// <param name="tamanhoSequencial">Dígitos do sequencial.</param>
internal sealed class LeiauteNff(int indiceDia, int indiceMes, int? indiceTipo, int indiceSequencial, int tamanhoSequencial)
{
    /// <summary>Índice do dia, com 2 dígitos.</summary>
    internal int IndiceDia { get; } = indiceDia;

    /// <summary>Índice do mês, com 2 dígitos.</summary>
    internal int IndiceMes { get; } = indiceMes;

    /// <summary>Índice do tipo do emissor (1 CNPJ, 2 CPF), ou nulo se o modelo não o tem.</summary>
    internal int? IndiceTipo { get; } = indiceTipo;

    /// <summary>Índice do sequencial diário.</summary>
    internal int IndiceSequencial { get; } = indiceSequencial;

    /// <summary>Dígitos do sequencial.</summary>
    internal int TamanhoSequencial { get; } = tamanhoSequencial;
}
