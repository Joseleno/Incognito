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

    /// <summary>
    /// Tabela estrangeira no lado que a ação escrita toca (<c>copiar</c>: qualquer lado; <c>esvaziar</c>: o destino), ou com
    /// regra de coluna escrita (saída 2).
    /// </summary>
    internal const string EstrangeiraTocada = "INC3071";

    /// <summary>Particionada com partição estrangeira e ação diferente de <c>ignorar</c> (saída 2).</summary>
    internal const string ParticaoEstrangeira = "INC3072";

    /// <summary>Objeto no esquema reservado ao Incognito, ou o dialeto não informa o que há nele (saída 2).</summary>
    internal const string ObjetoNoEsquemaReservado = "INC3073";

    /// <summary>Tabela estrangeira num lado e local no outro, sem ação nem regra de coluna escritas (saída 2).</summary>
    internal const string EstrangeiraNumLado = "INC3075";

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

    /// <summary>Aviso: tabela estrangeira nos dois lados e fora da configuração, ignorada.</summary>
    internal const string EstrangeiraIgnorada = "INC3572";

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
