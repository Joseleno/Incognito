using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O código de saída de uma exceção do motor, pelo tipo e nunca pela faixa do código (spec 11.2): configuração inválida ou
/// plano recusado dão 2, cancelamento dá 130, e o resto, inclusive a falha do dialeto, dá 1. O enum público entra com a
/// CLI.
/// </summary>
internal static class ClassificadorDeSaida
{
    /// <summary>Execução com erro (conexão, banco, entrada e saída).</summary>
    internal const int Execucao = 1;

    /// <summary>Configuração inválida ou destino não autorizado.</summary>
    internal const int Configuracao = 2;

    /// <summary>Cancelado pelo usuário.</summary>
    internal const int Cancelado = 130;

    /// <summary>O código de saída da exceção.</summary>
    /// <param name="excecao">A exceção que interrompeu o comando.</param>
    /// <returns>O código de saída.</returns>
    internal static int De(Exception excecao)
    {
        ArgumentNullException.ThrowIfNull(excecao);
        return excecao switch
        {
            ConfiguracaoInvalidaException or PlanoRecusadoException => Configuracao,
            OperationCanceledException => Cancelado,
            _ => Execucao,
        };
    }
}
