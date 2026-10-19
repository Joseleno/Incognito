namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// Códigos dos erros do dialeto PostgreSQL, na faixa <c>INC31xx</c> (dentro de <c>INC3xxx</c>, planejar e destino). Um
/// código nunca é reutilizado nem renumerado.
/// </summary>
internal static class CodigosPostgreSql
{
    /// <summary>A string de conexão não pôde ser lida, ou uma opção dela não pôde ser usada na abertura.</summary>
    internal const string StringInvalida = "INC3101";

    /// <summary>
    /// A string de conexão pede algo que o Incognito recusa: vários servidores, sem servidor ou banco, opção obsoleta, só
    /// senha em claro, ou sem senha com a do ambiente definida.
    /// </summary>
    internal const string OpcaoRecusada = "INC3102";

    /// <summary>Servidor numa versão abaixo da mínima suportada.</summary>
    internal const string VersaoNaoSuportada = "INC3103";

    /// <summary>Um parâmetro de sessão imposto pelo Incognito não ficou com o valor pedido.</summary>
    internal const string SessaoDivergente = "INC3104";

    /// <summary>Uma partição com <c>DETACH ... CONCURRENTLY</c> interrompido, que a leitura pelo pai não enxerga.</summary>
    internal const string ParticaoPendente = "INC3105";
}
