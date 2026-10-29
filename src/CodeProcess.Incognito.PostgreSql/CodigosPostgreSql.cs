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

    /// <summary>
    /// O diário do destino não é o esperado: outro dono, privilégio de escrita de outro papel, estrutura, versão ou conteúdo
    /// inesperados, ou uma definição guardada fora da forma de uma chave estrangeira.
    /// </summary>
    internal const string DiarioInesperado = "INC3116";

    /// <summary>
    /// O diário não permite a operação: a chave não existe nem está no diário (ou é a cópia de uma chave da mãe), a tabela já
    /// tem as triggers no diário, ou a execução não está em andamento (ou já há outra).
    /// </summary>
    internal const string EntradaIncoerente = "INC3117";

    /// <summary>Há uma chave com o nome da que o diário guarda, com outra definição.</summary>
    internal const string OutraDefinicao = "INC3118";

    /// <summary>O destino não liberou a tabela a tempo (outra sessão a segurava): nada da operação ficou.</summary>
    internal const string TempoDeBloqueio = "INC3119";

    /// <summary>A chave recriada não ficou igual à definição guardada: nada da operação ficou.</summary>
    internal const string RecriacaoNaoConfere = "INC3120";

    /// <summary>O diário não existe no destino (o <c>preparar-destino</c> o cria).</summary>
    internal const string SemDiario = "INC3121";

    /// <summary>O login do destino não pode criar o esquema do diário (no PostgreSQL, <c>CREATE</c> no banco).</summary>
    internal const string DiarioSemPrivilegio = "INC3122";

    /// <summary>
    /// A leitura na réplica foi interrompida pela reprodução (conflito com a recuperação) ou perdeu a conexão: uma réplica
    /// dedicada com <c>max_standby_streaming_delay = -1</c>, ou um clone, evita.
    /// </summary>
    internal const string ConflitoNaReplica = "INC3123";

    /// <summary>Uma tabela travada não foi liberada a tempo na abertura da leitura (outra sessão a segurava).</summary>
    internal const string TravaNaoObtida = "INC3124";

    /// <summary>A tabela de travas do servidor se esgotou (<c>max_locks_per_transaction</c>).</summary>
    internal const string TravasDemais = "INC3125";

    /// <summary>O servidor recusou mais conexões (<c>max_connections</c> ou o limite do login).</summary>
    internal const string ConexoesDemais = "INC3126";

    /// <summary>Uma conexão da sessão de leitura chegou a outro servidor ou banco que a de controle, ou não importou o instante.</summary>
    internal const string LeituraEmOutroBanco = "INC3127";

    /// <summary>A leitura falhou: o login não lê a tabela, ou a segurança por linha vale para ele (nunca filtra em silêncio).</summary>
    internal const string LeituraRecusada = "INC3128";

    /// <summary>
    /// A tabela mudou depois do início da leitura (esvaziada, reescrita, trocada ou com partição anexada ou destacada), ou tem
    /// uma alteração pendente que faria a leitura esperar.
    /// </summary>
    internal const string TabelaMudou = "INC3129";

    /// <summary>O instante da leitura ficou antigo demais para o servidor (<c>old_snapshot_threshold</c>, até o 16).</summary>
    internal const string InstanteAntigo = "INC3130";

    /// <summary>Uma tabela a travar não existe, não é tabela comum ou particionada, ou o login não tem <c>SELECT</c> nela.</summary>
    internal const string TravadaInvalida = "INC3131";
}
