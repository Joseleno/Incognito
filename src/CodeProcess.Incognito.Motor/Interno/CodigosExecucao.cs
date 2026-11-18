namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Códigos da execução, dentro de <c>INC4xxx</c> (execução e validação). A saída sai do tipo da exceção, nunca do código.
/// Um código nunca é reutilizado nem renumerado; cada um entra aqui quando a verificação que o usa é implementada.
/// </summary>
internal static class CodigosExecucao
{
    /// <summary>O bloqueio do destino se perdeu no meio (saída 1, <c>BloqueioDestinoException</c>).</summary>
    internal const string BloqueioPerdido = "INC4001";

    /// <summary>
    /// Uma chave estrangeira não validou (há linha órfã) e ficou no diário: resultado, nunca exceção (saída 4, pela
    /// <see cref="ClassificadorDeSaida.De(ResultadoDoReparo)"/>).
    /// </summary>
    internal const string ChavePendente = "INC4002";

    /// <summary>
    /// Uma entrada do diário não se restaurou (outra chave tomou o nome, ou a tabela sumiu): resultado, saída 1; o
    /// <c>reparar --descartar</c> a apaga.
    /// </summary>
    internal const string EntradaNaoRestaurada = "INC4003";

    /// <summary>Aviso: uma trigger guardada no diário não existe mais e foi pulada.</summary>
    internal const string TriggerPulada = "INC4004";

    /// <summary>
    /// Um transformador devolveu um resultado inválido: desfecho indefinido ou desconhecido, nulidade que não confere com o
    /// desfecho, valor fora da forma canônica ou do contexto da coluna (saída 1, <c>FalhaTransformacaoException</c>).
    /// </summary>
    internal const string ResultadoInvalido = "INC4005";

    /// <summary>
    /// O valor lido não tem representação na forma canônica e a política é falhar, ou a coluna de <c>derivarDe</c> não tem
    /// representação (saída 1, <c>FalhaTransformacaoException</c>).
    /// </summary>
    internal const string SemRepresentacao = "INC4006";

    /// <summary>Um transformador lançou (saída 1, <c>FalhaTransformacaoException</c>, só com o tipo da exceção de fora).</summary>
    internal const string TransformadorFalhou = "INC4007";

    /// <summary>Aviso: o descarte de um transformador de fora falhou (só o tipo); a saída não muda.</summary>
    internal const string DescarteDoTransformador = "INC4008";

    /// <summary>Aviso fixo: a execução falhou ou foi interrompida depois do preparo, e o destino está incompleto (D12).</summary>
    internal const string DestinoIncompleto = "INC4009";

    /// <summary>
    /// Aviso: a restauração depois de uma falha também falhou (só o tipo), e o diário ficou pendente; a saída é a da falha
    /// original, e o <c>reparar</c> conclui.
    /// </summary>
    internal const string RestauracaoIncompleta = "INC4010";

    /// <summary>Aviso: a idade do instante da leitura não foi medida (só o tipo); a cópia não muda.</summary>
    internal const string IdadeNaoMedida = "INC4011";

    /// <summary>
    /// O reparo do começo da execução não restaurou alguma entrada do diário (saída 1): nada da cópia foi escrito, e o
    /// <c>reparar</c> mostra o quê.
    /// </summary>
    internal const string ReparoIncompleto = "INC4012";
}
