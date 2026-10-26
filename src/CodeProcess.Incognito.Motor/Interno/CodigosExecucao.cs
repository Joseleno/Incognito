namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Códigos da execução, dentro de <c>INC4xxx</c> (execução e validação). A saída sai do tipo da exceção, nunca do código.
/// Um código nunca é reutilizado nem renumerado; cada um entra aqui quando a verificação que o usa é implementada.
/// </summary>
internal static class CodigosExecucao
{
    /// <summary>O bloqueio do destino se perdeu no meio (saída 1, <c>BloqueioDestinoException</c>).</summary>
    internal const string BloqueioPerdido = "INC4001";
}
