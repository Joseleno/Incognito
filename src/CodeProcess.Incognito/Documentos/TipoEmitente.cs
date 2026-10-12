namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Documento do emitente na chave de DF-e. O zero não é valor definido: uma classificação não inicializada nunca vira
/// CNPJ, que é o erro que vaza dígitos de um CPF.
/// </summary>
internal enum TipoEmitente : byte
{
    /// <summary>CNPJ, numérico ou alfanumérico, nas posições 7 a 20.</summary>
    Cnpj = 1,

    /// <summary>CPF nas posições 10 a 20, com <c>000</c> nas posições 7 a 9.</summary>
    Cpf = 2,
}
