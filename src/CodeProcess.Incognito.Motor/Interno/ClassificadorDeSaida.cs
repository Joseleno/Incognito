using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O código de saída de uma exceção do motor, pelo tipo e nunca pela faixa do código (spec 11.2): configuração inválida ou
/// plano recusado dão 2, cancelamento dá 130, e o resto, inclusive a falha do dialeto, dá 1. A execução que termina sai
/// pelo resultado: uma entrada que não se restaurou dá 1, e uma chave que ficou no diário sem validar dá 4. A precedência
/// entre os desfechos é 130, 1 e 4. O enum público entra com a CLI.
/// </summary>
internal static class ClassificadorDeSaida
{
    /// <summary>Execução sem erro.</summary>
    internal const int Sucesso = 0;

    /// <summary>Execução com erro (conexão, banco, entrada e saída).</summary>
    internal const int Execucao = 1;

    /// <summary>Configuração inválida ou destino não autorizado.</summary>
    internal const int Configuracao = 2;

    /// <summary>A execução terminou, mas uma chave estrangeira não validou e ficou no diário.</summary>
    internal const int Validacao = 4;

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

    /// <summary>O código de saída de uma restauração que terminou: a falha antes da chave pendente.</summary>
    /// <param name="resultado">O que a restauração deixou.</param>
    /// <returns>O código de saída.</returns>
    internal static int De(ResultadoDaRestauracao resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return resultado.Falhas.Count > 0 ? Execucao
            : resultado.Pendentes.Count > 0 ? Validacao
            : Sucesso;
    }

    /// <summary>O código de saída de um reparo que terminou.</summary>
    /// <param name="resultado">O que o reparo fez.</param>
    /// <returns>O código de saída.</returns>
    internal static int De(ResultadoDoReparo resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return De(resultado.Restauracao);
    }
}
