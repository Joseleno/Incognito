namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Como o modelo decide o documento do emitente a partir dos campos da chave. O zero não é valor definido: um leiaute
/// sem regra não cai em regra nenhuma.
/// </summary>
internal enum RegraEmitente : byte
{
    /// <summary>Datas, tpEmis e séries da NFC-e, sem as séries do PAA.</summary>
    Series = 1,

    /// <summary>Datas, tpEmis e séries da NF-e, com as séries do PAA (970 a 979 CPF, 980 a 989 CNPJ).</summary>
    SeriesComPaa,

    /// <summary>
    /// Regra dos zeros (CT-e e MDF-e): letra nas posições 7 a 18 dá CNPJ; senão, <c>000</c> nas posições 7 a 9 dá CPF;
    /// senão, CNPJ com as raízes <c>000xxxxx</c> excluídas da saída.
    /// </summary>
    Zeros,

    /// <summary>O emitente só pode ser CNPJ.</summary>
    SempreCnpj,
}
