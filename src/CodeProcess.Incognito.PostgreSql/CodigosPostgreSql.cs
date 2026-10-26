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

    /// <summary>A medida do comportamento de uma colação falhou, ou a colação mudou no catálogo durante a leitura.</summary>
    internal const string MedidaDeColacao = "INC3106";

    /// <summary>O catálogo tem um valor que o dialeto não conhece (de uma versão do PostgreSQL mais nova).</summary>
    internal const string CatalogoDesconhecido = "INC3107";

    /// <summary>O maior valor foi pedido de uma tabela ou coluna que não existe, ou de uma coluna que não é inteira.</summary>
    internal const string ColunaNaoInteira = "INC3108";

    /// <summary>O login do destino não pode gravar o marcador: no PostgreSQL, só o dono do banco comenta nele.</summary>
    internal const string MarcadorSemPrivilegio = "INC3109";

    /// <summary>O bloqueio de execução do destino se perdeu, ou não pôde ser liberado.</summary>
    internal const string BloqueioPerdido = "INC3110";

    /// <summary>A existência de linhas foi pedida de uma tabela que não existe, ou que não é tabela comum nem particionada.</summary>
    internal const string TabelaDoDestinoInvalida = "INC3111";

    /// <summary>O banco do destino já tem um comentário ou um marcador, que o Incognito nunca sobrescreve.</summary>
    internal const string BancoJaComentado = "INC3112";

    /// <summary>O marcador relido depois de gravado não confere com o gravado.</summary>
    internal const string MarcadorNaoConfere = "INC3113";

    /// <summary>A conexão do bloqueio chegou a outro servidor ou banco que a de controle, ou a uma réplica.</summary>
    internal const string BloqueioEmOutroBanco = "INC3114";

    /// <summary>A existência de linhas não pôde ser lida: o login não lê a tabela, ou a segurança por linha vale para ele.</summary>
    internal const string LeituraDoDestinoRecusada = "INC3115";
}
