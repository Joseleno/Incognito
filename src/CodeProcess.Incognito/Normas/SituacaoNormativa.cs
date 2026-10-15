namespace CodeProcess.Incognito.Normas;

/// <summary>A situação de uma referência na data da revisão.</summary>
internal enum SituacaoNormativa
{
    /// <summary>A versão conferida é a mais recente publicada (não quer dizer força de norma: estudo e acórdão também).</summary>
    Atual,

    /// <summary>O documento final ainda não saiu; a referência cita a versão preliminar.</summary>
    EmElaboracao,
}
