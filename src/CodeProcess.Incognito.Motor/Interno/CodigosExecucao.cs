namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Códigos da execução, dentro de <c>INC4xxx</c> (execução e validação). A saída sai do tipo da exceção, nunca do código.
/// Um código nunca é reutilizado nem renumerado; cada um entra aqui quando a verificação que o usa é implementada.
/// </summary>
internal static class CodigosExecucao
{
    /// <summary>O bloqueio do destino se perdeu no meio (saída 1, <c>BloqueioDestinoException</c>).</summary>
    internal const string BloqueioPerdido = "INC4001";

    /// <summary>
    /// Uma chave estrangeira não validou (há linha órfã) e ficou no diário: resultado, nunca exceção (saída 4, pela
    /// <see cref="ClassificadorDeSaida.De(ResultadoDoReparo)"/>).
    /// </summary>
    internal const string ChavePendente = "INC4002";

    /// <summary>
    /// Uma entrada do diário não se restaurou (outra chave tomou o nome, ou a tabela sumiu): resultado, saída 1; o
    /// <c>reparar --descartar</c> a apaga.
    /// </summary>
    internal const string EntradaNaoRestaurada = "INC4003";

    /// <summary>Aviso: uma trigger guardada no diário não existe mais e foi pulada.</summary>
    internal const string TriggerPulada = "INC4004";
}
