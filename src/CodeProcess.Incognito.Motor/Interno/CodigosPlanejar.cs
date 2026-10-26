namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Códigos do <c>planejar</c>, dentro de <c>INC3xxx</c> (planejar e destino): <c>INC300x</c> para a infraestrutura,
/// <c>INC301x</c> a <c>INC309x</c> para os erros por área (<c>INC309x</c>, o <c>id.permutar</c>), <c>INC360x</c> para a
/// proteção do destino e <c>INC351x</c> a <c>INC359x</c> para os avisos. A saída sai do tipo da exceção, nunca do código. Um
/// código nunca é reutilizado nem renumerado; cada um entra aqui quando a verificação que o usa é implementada.
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

    /// <summary>Outra execução tem o bloqueio do destino (saída 1, <c>BloqueioDestinoException</c>).</summary>
    internal const string DestinoOcupado = "INC3004";

    /// <summary>Tabela escrita na configuração que não existe em nenhum dos bancos (saída 2).</summary>
    internal const string TabelaInexistente = "INC3010";

    /// <summary>Coluna escrita na configuração que não existe na tabela da origem (saída 2).</summary>
    internal const string ColunaInexistente = "INC3011";

    /// <summary>Nome que designa mais de um objeto, ou que o dialeto não sabe comparar sem adivinhar (saída 2).</summary>
    internal const string NomeAmbiguo = "INC3012";

    /// <summary>Duas entradas da configuração que designam o mesmo objeto (saída 2).</summary>
    internal const string EntradaRepetida = "INC3013";

    /// <summary>Entrada que designa uma partição: a regra se escreve na raiz (saída 2).</summary>
    internal const string EntradaEmParticao = "INC3014";

    /// <summary>Entrada num esquema de sistema ou no reservado ao Incognito (saída 2).</summary>
    internal const string EsquemaReservado = "INC3015";

    /// <summary>A coluna de <c>derivarDe</c> não existe na tabela da origem (saída 2).</summary>
    internal const string DerivarDeInexistente = "INC3016";

    /// <summary><c>derivarDe</c> em ciclo depois de resolvidos os nomes (saída 2).</summary>
    internal const string DerivarDeEmCiclo = "INC3017";

    /// <summary>Unidade de cópia que só existe na origem e não é ignorada (saída 2).</summary>
    internal const string SoNaOrigem = "INC3020";

    /// <summary>Unidade de cópia que só existe no destino sem <c>esvaziar</c> ou <c>ignorar</c> escrito (saída 2).</summary>
    internal const string SoNoDestino = "INC3021";

    /// <summary>Coluna da origem que não existe no destino (saída 2).</summary>
    internal const string ColunaAusenteNoDestino = "INC3022";

    /// <summary>Coluna tratada com tipo ou situação diferentes entre os lados (saída 2).</summary>
    internal const string TratadaComTipoDiferente = "INC3023";

    /// <summary>Colunas ligadas por chave estrangeira ou herança com regras diferentes (saída 2).</summary>
    internal const string RegrasDiferentesNoComponente = "INC3030";

    /// <summary>Colunas ligadas que diferem só nos domínios citados (<c>dominioCpf</c> e afins) (saída 2).</summary>
    internal const string DominiosCitadosDiferentes = "INC3031";

    /// <summary>Coluna mantida ligada a uma coluna transformada: o valor real ficaria nela (saída 2).</summary>
    internal const string MantidaNoComponente = "INC3032";

    /// <summary>Regra com <c>derivarDe</c> ligada por chave estrangeira a outras colunas (saída 2).</summary>
    internal const string DerivarDeNaChave = "INC3033";

    /// <summary>A coluna de <c>derivarDe</c> não tem forma canônica na origem (saída 2).</summary>
    internal const string DerivarDeSemFormaCanonica = "INC3034";

    /// <summary>Chave estrangeira do destino de uma tabela copiada para uma esvaziada: as linhas ficariam órfãs (saída 2).</summary>
    internal const string CopiadaParaEsvaziada = "INC3035";

    /// <summary>Chave estrangeira do destino entre uma tabela copiada e uma ignorada, com coluna tratada do lado copiado (saída 2).</summary>
    internal const string TratadaNaFronteira = "INC3036";

    /// <summary>Coluna gerada ligada por chave estrangeira a uma coluna tratada (saída 2).</summary>
    internal const string GeradaNoComponente = "INC3037";

    /// <summary>O tipo da coluna tratada no destino não está entre os que o transformador aceita (saída 2).</summary>
    internal const string TipoNaoAceito = "INC3040";

    /// <summary>
    /// Coluna tratada sem forma canônica (tipo nulo ou situação diferente de suportado), com transformador que não seja
    /// <c>nulo</c> nem <c>fixo</c> (saída 2).
    /// </summary>
    internal const string SemFormaCanonica = "INC3041";

    /// <summary>Regra escrita numa coluna gerada: o banco recalcula o valor (saída 2).</summary>
    internal const string RegraEmGerada = "INC3042";

    /// <summary>O limite do destino, em bytes, não comporta um caractere da codificação dele (saída 2).</summary>
    internal const string LimiteSemCaractere = "INC3043";

    /// <summary>Coluna de texto ou binária tratada cujo comprimento o dialeto não mediu (saída 2).</summary>
    internal const string ComprimentoNaoMedido = "INC3044";

    /// <summary>A fábrica recusou a coluna ao criar o transformador, com a mensagem dela escapada (saída 2).</summary>
    internal const string CriacaoRecusada = "INC3045";

    /// <summary>A fábrica de fora falhou ao criar, ler ou descartar o transformador: só o tipo da exceção sai (saída 2).</summary>
    internal const string FalhaDaFabrica = "INC3046";

    /// <summary>
    /// Coluna inteira tratada sem a faixa, ou decimal tratada sem a precisão ou com mais de 28 dígitos (saída 2).
    /// </summary>
    internal const string FaixaOuPrecisaoNaoMedida = "INC3047";

    /// <summary>Política que produz NULL numa coluna do destino que não aceita NULL ou é da chave primária (saída 2).</summary>
    internal const string NuloEmObrigatoria = "INC3048";

    /// <summary>
    /// Coluna tratada com transformador sem injetividade em chave primária, índice único (pela coluna, por expressão ou por
    /// gerada) ou chave estrangeira (saída 2).
    /// </summary>
    internal const string SemInjetividadeNaUnicidade = "INC3050";

    /// <summary>
    /// Índice único que compara a coluna tratada por uma igualdade indeterminada, ou não determinística sem prova na tabela
    /// de formato fixo (saída 2).
    /// </summary>
    internal const string IgualdadeIndeterminada = "INC3051";

    /// <summary>Regra que produz NULL num índice único que trata NULLs como iguais (saída 2).</summary>
    internal const string NuloNoIndice = "INC3052";

    /// <summary>Exclusão que cita coluna tratada com transformador sem injetividade (saída 2).</summary>
    internal const string SemInjetividadeNaExclusao = "INC3053";

    /// <summary>A segurança por linha vale para o login na leitura da origem copiada, ou o dialeto não informa (saída 2).</summary>
    internal const string LeituraFiltradaPorLinha = "INC3060";

    /// <summary>A segurança por linha vale para o login na gravação do destino copiado, ou o dialeto não informa (saída 2).</summary>
    internal const string EscritaRestritaPorLinha = "INC3061";

    /// <summary>O dialeto não informa as permissões do login de um lado (saída 2).</summary>
    internal const string AcessoNaoInformado = "INC3062";

    /// <summary>O login da origem não lê todas as colunas de uma tabela copiada (saída 2).</summary>
    internal const string SemLeitura = "INC3063";

    /// <summary>O login do destino não esvazia, não insere ou não grava a identidade numa tabela tocada (saída 2).</summary>
    internal const string SemGravacao = "INC3064";

    /// <summary>O login do destino não desliga as triggers ligadas da tabela tocada ou de uma partição dela (saída 2).</summary>
    internal const string SemDesligarTriggers = "INC3065";

    /// <summary>O login do destino não remove e recria uma chave estrangeira que o diário remove (saída 2).</summary>
    internal const string SemRecriarChave = "INC3066";

    /// <summary>O login do destino não ajusta uma sequence usada por tabela copiada (saída 2).</summary>
    internal const string SemAjustarSequence = "INC3067";

    /// <summary>O login do destino não limpa um objeto residual que guarda dado (saída 2).</summary>
    internal const string ResidualSemLimpeza = "INC3070";

    /// <summary>
    /// Tabela estrangeira no lado que a ação escrita toca (<c>copiar</c>: qualquer lado; <c>esvaziar</c>: o destino), ou com
    /// regra de coluna escrita (saída 2).
    /// </summary>
    internal const string EstrangeiraTocada = "INC3071";

    /// <summary>Particionada com partição estrangeira e ação diferente de <c>ignorar</c> (saída 2).</summary>
    internal const string ParticaoEstrangeira = "INC3072";

    /// <summary>Objeto no esquema reservado ao Incognito, ou o dialeto não informa o que há nele (saída 2).</summary>
    internal const string ObjetoNoEsquemaReservado = "INC3073";

    /// <summary>Tabela tocada do destino, ou uma partição dela, numa publicação de replicação lógica, ou o dialeto não informa (saída 2).</summary>
    internal const string TabelaPublicada = "INC3074";

    /// <summary>Tabela estrangeira num lado e local no outro, sem ação nem regra de coluna escritas (saída 2).</summary>
    internal const string EstrangeiraNumLado = "INC3075";

    /// <summary>Tabela tocada do destino, ou uma partição dela, numa assinatura de replicação, ou o dialeto não informa (saída 2).</summary>
    internal const string TabelaAssinada = "INC3076";

    /// <summary>A origem guarda bytes sem codificação declarada, e o destino tem codificação (saída 2).</summary>
    internal const string OrigemSemCodificacao = "INC3080";

    /// <summary>O destino não representa todos os caracteres da codificação da origem (saída 2).</summary>
    internal const string CodificacaoSemRepertorio = "INC3081";

    /// <summary>O dialeto não informa a codificação e a versão do banco de um lado (saída 2).</summary>
    internal const string PropriedadesNaoInformadas = "INC3082";

    /// <summary>
    /// O N do domínio do <c>id.permutar</c> passaria de 10¹⁸, ou uma coluna do domínio não comporta N − 1 no destino
    /// (saída 2).
    /// </summary>
    internal const string TamanhoNaoCabe = "INC3090";

    /// <summary>Domínio do <c>id.permutar</c> sem coluna na chave primária de uma tabela copiada da origem (saída 2).</summary>
    internal const string DominioSemFonte = "INC3091";

    /// <summary>
    /// Coluna do domínio do <c>id.permutar</c> fora da chave primária e sem chave estrangeira validada até ela, com valor a
    /// partir de N e <c>entradaMalformada: falhar</c> (saída 2).
    /// </summary>
    internal const string ForaDoDominio = "INC3092";

    /// <summary>O destino não tem o marcador do Incognito, ou o dialeto não informa (saída 2).</summary>
    internal const string DestinoSemMarcador = "INC3600";

    /// <summary>O marcador do Incognito não se lê num dos lados (saída 2).</summary>
    internal const string MarcadorIlegivel = "INC3601";

    /// <summary>A origem tem o marcador do Incognito, ou o dialeto não informa (saída 2).</summary>
    internal const string OrigemMarcada = "INC3602";

    /// <summary>O banco conectado no destino não é o <c>bancoEsperado</c> (saída 2).</summary>
    internal const string BancoInesperado = "INC3603";

    /// <summary>Tabela <c>ignorar</c> com linhas no destino, sem aceitar os dados existentes (saída 2).</summary>
    internal const string IgnoradaComLinhas = "INC3604";

    /// <summary>
    /// O <c>preparar-destino</c> num banco com um comentário que não é marcador válido do Incognito, ou cujo marcador o
    /// dialeto não informa (saída 2).
    /// </summary>
    internal const string ComentarioAlheio = "INC3605";

    /// <summary>O <c>preparar-destino</c> num destino que é o próprio banco da origem (saída 2).</summary>
    internal const string DestinoIgualAOrigem = "INC3606";

    /// <summary>Aviso: outro objeto difere de um nome resolvido só pela caixa.</summary>
    internal const string NomeQuaseIgual = "INC3510";

    /// <summary>Aviso: coluna só no destino que não aceita nulo e não tem identidade nem geração.</summary>
    internal const string ObrigatoriaSoNoDestino = "INC3520";

    /// <summary>Aviso: coluna copiada mais estreita no destino, ou com tipo diferente sem ser tratada.</summary>
    internal const string MaisEstreitaNoDestino = "INC3521";

    /// <summary>Aviso: colação com comportamento diferente entre os lados.</summary>
    internal const string ColacaoDiferente = "INC3522";

    /// <summary>Aviso: chave primária, chave estrangeira, índice único ou CHECK presente só num lado.</summary>
    internal const string RestricaoSoNumLado = "INC3523";

    /// <summary>Aviso: chave estrangeira da origem que o banco não impõe ou não validou.</summary>
    internal const string ChaveNaoValidada = "INC3530";

    /// <summary>Aviso: chave estrangeira do destino entre uma tabela ignorada e uma tocada, sem coluna tratada.</summary>
    internal const string FronteiraComIgnorada = "INC3531";

    /// <summary>Aviso: transformador de fora numa coluna ligada por chave com igualdade que não é exata.</summary>
    internal const string TerceiroLigadoSemIgualdadeExata = "INC3532";

    /// <summary>Aviso: herdeira ignorada de mãe copiada com coluna tratada.</summary>
    internal const string HerdeiraIgnorada = "INC3533";

    /// <summary>Aviso: o tipo da coluna tratada tem restrição própria no destino, ou o dialeto não informa.</summary>
    internal const string RestritaPeloTipo = "INC3540";

    /// <summary>Aviso: CHECK imposto do destino que cita coluna tratada.</summary>
    internal const string CheckComTratada = "INC3541";

    /// <summary>Aviso: chave de partição do destino com coluna tratada, ou incompleta.</summary>
    internal const string ParticaoComTratada = "INC3542";

    /// <summary>Aviso: predicado de índice parcial que cita coluna tratada.</summary>
    internal const string PredicadoComTratada = "INC3550";

    /// <summary>Aviso: coluna tratada injetiva num elemento de expressão ou numa gerada indexada.</summary>
    internal const string TratadaEmExpressao = "INC3551";

    /// <summary>Aviso: o transformador normaliza a entrada, e entradas distintas podem dar a mesma saída no índice.</summary>
    internal const string ColisaoPelaNormalizacao = "INC3552";

    /// <summary>Aviso: exclusão que cita coluna tratada com transformador injetivo.</summary>
    internal const string ExclusaoComTratada = "INC3553";

    /// <summary>Aviso: transformador de fora em igualdade normalizada conhecida (sem a caixa ou sem os espaços finais).</summary>
    internal const string TerceiroEmNormalizada = "INC3554";

    /// <summary>Aviso: entrada preservada em claro no mesmo índice único que os pseudônimos.</summary>
    internal const string PreservadaComPseudonimos = "INC3555";

    /// <summary>Aviso: conexão fora da própria máquina sem cifra com o servidor verificado, ou o dialeto não informa.</summary>
    internal const string TransporteEmClaro = "INC3560";

    /// <summary>Aviso: sequence do destino não usada por tabela copiada que o login não ajusta.</summary>
    internal const string SequenceSemAjuste = "INC3561";

    /// <summary>Aviso: o dialeto não sabe dizer tudo o que uma materialized view do destino lê.</summary>
    internal const string DependenciasIncompletas = "INC3570";

    /// <summary>Aviso: coluna de tipo não suportado copiada como veio.</summary>
    internal const string NaoSuportadaCopiada = "INC3571";

    /// <summary>Aviso: tabela estrangeira nos dois lados e fora da configuração, ignorada.</summary>
    internal const string EstrangeiraIgnorada = "INC3572";

    /// <summary>Aviso: a versão principal do servidor difere entre a origem e o destino.</summary>
    internal const string VersaoDiferente = "INC3580";

    /// <summary>Aviso: o destino guarda bytes sem codificação declarada, e a origem tem codificação.</summary>
    internal const string DestinoSemCodificacao = "INC3581";

    /// <summary>Aviso: a origem e o destino têm a mesma linhagem, ou estão no mesmo endereço.</summary>
    internal const string MesmaLinhagem = "INC3582";

    /// <summary>Aviso: o destino ainda não tem o marcador do Incognito, no <c>planejar</c>.</summary>
    internal const string DestinoAindaSemMarcador = "INC3583";

    /// <summary>Aviso: há versão mais nova do transformador registrada.</summary>
    internal const string VersaoMaisNova = "INC3590";

    /// <summary>Aviso: <c>entradaMalformada: preservar</c> copia em claro a entrada fora do formato.</summary>
    internal const string MalformadaPreservada = "INC3591";

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
