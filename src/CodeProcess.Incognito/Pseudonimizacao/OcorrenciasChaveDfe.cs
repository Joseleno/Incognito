namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// O que a chave de DF-e teve de fora do comum: partes que ficaram como vieram, DV interno do emitente errado ou
/// emitente sem classificação. Uma chave pode ter mais de uma ocorrência; o relatório conta cada uma à parte. As do
/// número e do código saem do domínio <c>dfe-numero</c>; as do emitente e a anomalia, do <c>br.chave-dfe</c>.
/// </summary>
[Flags]
internal enum OcorrenciasChaveDfe
{
    /// <summary>Nada fora do comum.</summary>
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

    /// <summary>
    /// Emitente que a permutação da coluna deixa como veio (CPF de base repdígito, CNPJ de raiz zero): o número e o
    /// código seguem pseudonimizados.
    /// </summary>
    EmitentePontoFixo = 32,

    /// <summary>
    /// DV do CPF ou do CNPJ do emitente errado: o deslocamento é mantido (δ mod 100), sem a política de DV da coluna. Uma
    /// chave autorizada nunca tem; com o cDV certo, só esta contagem mostra o caso.
    /// </summary>
    DvDoEmitenteInvalido = 64,

    /// <summary>
    /// Chave sem emitente classificável (série sem emissão, tipo de emissor da NFF fora de 1 e 2, CPF sem os zeros à
    /// esquerda, tpEmis 3 fora do período), tratada pela política de entrada malformada; separa a anomalia da forma
    /// errada no relatório.
    /// </summary>
    Anomalia = 128,
}
