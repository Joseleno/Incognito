namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Códigos do <c>planejar</c>, dentro de <c>INC3xxx</c> (planejar e destino): <c>INC300x</c> para a infraestrutura,
/// <c>INC301x</c> a <c>INC309x</c> para os erros por área e <c>INC351x</c> a <c>INC359x</c> para os avisos. A saída sai do tipo da exceção, nunca do código. Um código nunca é reutilizado nem renumerado; cada um entra
/// aqui quando a verificação que o usa é implementada.
/// </summary>
internal static class CodigosPlanejar
{
    /// <summary>O plano foi recusado; cada erro está em <c>Erros</c> (saída 2).</summary>
    internal const string Agregado = "INC3000";

    /// <summary>
    /// O dialeto falhou com uma exceção que não é dele (do driver, do sistema) ou com um código fora da faixa dos dialetos:
    /// só o tipo, a operação e o lado saem (saída 1).
    /// </summary>
    internal const string FalhaDoDialeto = "INC3001";

    /// <summary>O esquema que o dialeto leu se contradiz (uma regra de coerência do modelo), defeito do dialeto (saída 1).</summary>
    internal const string EsquemaIncoerente = "INC3002";

    /// <summary>Não há dialeto para o provedor da configuração, ou o dialeto tem outro nome (saída 2).</summary>
    internal const string DialetoAusente = "INC3003";

    /// <summary>
    /// Se o código é da faixa dos dialetos (<c>INC31xx</c> a <c>INC34xx</c>): exatamente <c>INC3</c>, um dígito de 1 a 4 e
    /// dois dígitos ASCII. Conferido caractere a caractere: um prefixo deixaria passar texto depois dele, e um dígito que
    /// não é ASCII passaria por número.
    /// </summary>
    /// <param name="codigo">O código lido da exceção do dialeto.</param>
    /// <returns>Se é da faixa.</returns>
    internal static bool DaFaixaDosDialetos(string? codigo) =>
        codigo is { Length: 7 }
        && codigo[0] == 'I' && codigo[1] == 'N' && codigo[2] == 'C' && codigo[3] == '3'
        && codigo[4] is >= '1' and <= '4'
        && char.IsAsciiDigit(codigo[5]) && char.IsAsciiDigit(codigo[6]);
}
