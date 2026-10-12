namespace CodeProcess.Incognito.Documentos;

/// <summary>Por que a chave de DF-e não tem emitente classificado.</summary>
internal enum MotivoAnomaliaDfe : byte
{
    /// <summary>Valor padrão, de uma classificação não inicializada: conta como anomalia.</summary>
    NaoClassificada,

    /// <summary>Classificada: não há anomalia.</summary>
    Nenhum,

    /// <summary>
    /// As 44 posições em zero, valor de preenchimento do sistema: não segue a política de entrada malformada e volta como veio,
    /// porque não tem dado pessoal.
    /// </summary>
    ChaveZerada,

    /// <summary>
    /// Fora da estrutura da chave: tamanho diferente de 44, ou caractere que não é dígito ASCII (nem letra maiúscula ASCII
    /// nas posições 7 a 18).
    /// </summary>
    EstruturaInvalida,

    /// <summary>Modelo fora dos nove tratados (inclusive o 59, do CF-e SAT, que usa outro leiaute).</summary>
    ModeloForaDaTabela,

    /// <summary>NFF da NF-e ou da NFC-e com a posição 30 diferente de 1 (CNPJ) e de 2 (CPF).</summary>
    NffSemTipoDeEmissor,

    /// <summary>
    /// tpEmis 3 na NF-e ou na NFC-e, fora das séries do PAA, com AAMM de 1808 a 2103: depois do SCAN e antes da NFF.
    /// </summary>
    TipoEmissao3ForaDoPeriodo,

    /// <summary>Série que nenhum processo de emissão vigente usa (990 a 999 na NF-e, 970 a 999 na NFC-e).</summary>
    SerieSemEmissao,

    /// <summary>NF-e ou NFC-e classificada como CPF sem <c>000</c> nas posições 7 a 9.</summary>
    CpfSemZerosAEsquerda,
}
