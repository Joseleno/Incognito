namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>O descarte do que já foi aberto quando uma abertura falha.</summary>
internal static class Descarte
{
    /// <summary>
    /// Descarta cada um, em ordem, depois de uma falha: a exceção de um descarte não toma o lugar da falha que o causou
    /// (quem chama relança a dela) nem impede o descarte dos seguintes.
    /// </summary>
    /// <param name="descartaveis">O que descartar, na ordem; nulos são pulados.</param>
    /// <returns>A tarefa do descarte.</returns>
    internal static async ValueTask DepoisDaFalhaAsync(params IAsyncDisposable?[] descartaveis)
    {
        foreach (var descartavel in descartaveis)
        {
            if (descartavel is null)
            {
                continue;
            }

            try
            {
                await descartavel.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A falha que levou ao descarte é a que sai.
            }
        }
    }
}
