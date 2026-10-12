namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// O que ficou como veio no número e no código da chave de DF-e. Uma chave pode ter mais de uma ocorrência; o relatório
/// conta cada uma à parte.
/// </summary>
[Flags]
internal enum OcorrenciasNumeroDfe
{
    /// <summary>Número e código permutados.</summary>
    Nenhuma = 0,

    /// <summary>Número 0 (fora da NFF): não existe e fica como veio.</summary>
    NumeroZero = 1,

    /// <summary>Sequencial 0 da NFF: fica como veio.</summary>
    SequencialZero = 2,

    /// <summary>
    /// Dia da NFF que não existe no mês do AAMM e no do número, ou um dos meses fora de 01 a 12: o dia fica como veio.
    /// </summary>
    DataNffInvalida = 4,

    /// <summary>Código numérico de 8 dígitos entre os 20 proibidos (chave anterior à regra): fica como veio.</summary>
    CodigoProibido = 8,

    /// <summary>
    /// Código numérico de 8 dígitos igual ao número pseudonimizado (comparação numérica): fica como veio e sai igual ao
    /// número, o que a regra 897 recusa. Chance da ordem de 10⁻⁸ por chave.
    /// </summary>
    CodigoIgualAoNumero = 16,
}
