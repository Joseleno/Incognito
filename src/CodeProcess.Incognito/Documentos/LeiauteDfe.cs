namespace CodeProcess.Incognito.Documentos;

/// <summary>O que muda de um modelo de DF-e para outro dentro da mesma chave de 44 posições.</summary>
/// <param name="modelo">O modelo.</param>
/// <param name="regra">Como o emitente é classificado.</param>
/// <param name="digitosDoCodigo">Dígitos do código numérico: 8 nas posições 36 a 43, ou 7 nas posições 37 a 43.</param>
/// <param name="nff">Partes do número na Nota Fiscal Fácil, ou nulo se o modelo não a tem.</param>
internal sealed class LeiauteDfe(ModeloDfe modelo, RegraEmitente regra, int digitosDoCodigo, LeiauteNff? nff)
{
    /// <summary>O modelo.</summary>
    internal ModeloDfe Modelo { get; } = modelo;

    /// <summary>Como o emitente é classificado.</summary>
    internal RegraEmitente Regra { get; } = regra;

    /// <summary>Dígitos do código numérico: 8 nas posições 36 a 43, ou 7 nas posições 37 a 43.</summary>
    internal int DigitosDoCodigo { get; } = digitosDoCodigo;

    /// <summary>Partes do número na Nota Fiscal Fácil, ou nulo se o modelo não a tem.</summary>
    internal LeiauteNff? Nff { get; } = nff;

    /// <summary>Se a posição 36 é o <c>nSiteAutoriz</c>, preservado (NF3e e NFCom, que têm código de 7 dígitos).</summary>
    internal bool PreservaPosicao36 => DigitosDoCodigo == 7;

    /// <summary>
    /// Se a saída evita os códigos de <see cref="RegrasChaveDfe.CodigosNumericosProibidos"/>: em todo código de 8
    /// dígitos (a regra é da NF-e; estendê-la aos outros modelos é decisão do projeto, porque só restringe a saída).
    /// </summary>
    internal bool ExcluiCodigosProibidos => DigitosDoCodigo == 8;
}
