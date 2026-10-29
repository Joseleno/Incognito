// O preparo chama o dialeto pelo envoltório e lê a identidade, tipos do contrato experimental de dialeto que o motor
// declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que o <c>preparar-destino</c> fez.</summary>
internal enum SituacaoDoPreparo
{
    /// <summary>Gravou um marcador novo e criou o diário.</summary>
    Gravado,

    /// <summary>O destino já tinha um marcador válido e o diário, e nada foi gravado.</summary>
    JaPreparado,

    /// <summary>O destino já tinha um marcador válido, sem o diário: só o diário foi criado.</summary>
    DiarioCriado,

    /// <summary>A confirmação foi negada, e nada foi gravado.</summary>
    Negado,
}

/// <summary>O desfecho do <c>preparar-destino</c>.</summary>
/// <param name="Situacao">O que foi feito.</param>
/// <param name="Identificador">
/// O identificador do marcador gravado ou que já estava; nulo na confirmação negada de um destino sem marcador.
/// </param>
/// <param name="Avisos">Os avisos (a linhagem ou o endereço iguais aos da origem).</param>
internal sealed record ResultadoDoPreparo(SituacaoDoPreparo Situacao, Guid? Identificador, IReadOnlyList<Aviso> Avisos)
{
    /// <summary>Os avisos, copiados.</summary>
    public IReadOnlyList<Aviso> Avisos { get; } = [.. Avisos];
}

/// <summary>
/// O <c>preparar-destino</c>: adquire o bloqueio, lê a identidade do destino (e a da origem, quando configurada), confere o
/// <c>bancoEsperado</c> e que o destino não é o próprio banco da origem, e, depois da confirmação, grava um marcador com
/// identificador aleatório e cria o diário de execução, nessa ordem (o esquema do diário nunca vai a um banco sem o
/// marcador). Nunca sobrescreve: com marcador válido só cria o diário que falta; com um comentário alheio, um marcador
/// ilegível ou sem saber o que o banco tem, recusa. O bloqueio é liberado antes de sair.
/// </summary>
internal static class PreparadorDoDestino
{
    /// <summary>Prepara o destino.</summary>
    /// <param name="destino">O destino, conectado.</param>
    /// <param name="origem">A origem, conectada; nula quando não configurada.</param>
    /// <param name="bancoEsperado">O <c>destino.bancoEsperado</c> da configuração; nulo quando não escrito.</param>
    /// <param name="confirmar">A confirmação, com o servidor, o banco e os avisos; falso não grava nada.</param>
    /// <param name="relogio">O relógio da data do marcador.</param>
    /// <param name="cancelamento">Cancela o preparo.</param>
    /// <returns>O que foi feito.</returns>
    /// <exception cref="BloqueioDestinoException">
    /// Outra execução tem o bloqueio (<c>INC3004</c>), ou ele se perdeu antes de gravar (<c>INC4001</c>).
    /// </exception>
    /// <exception cref="PlanoRecusadoException">O destino não pode ser preparado (<c>INC360x</c>), ou o dialeto recusou.</exception>
    /// <exception cref="FalhaDialetoException">
    /// O dialeto falhou ou recusou o diário, ou a releitura não confirma o marcador gravado ou o diário criado.
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDoPreparo> PrepararAsync(
        DialetoProtegido destino,
        DialetoProtegido? origem,
        string? bancoEsperado,
        Func<ResumoDoDestino, CancellationToken, Task<bool>> confirmar,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(confirmar);
        ArgumentNullException.ThrowIfNull(relogio);

        var concessao = await destino.TentarAdquirirBloqueioAsync(cancelamento).ConfigureAwait(false) ?? throw ProtecaoDoDestino.Ocupado();
        try
        {
            var doDestino = await destino.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            var daOrigem = origem is null ? null : await origem.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            var coletor = new ColetorDoPlano();
            ConferenciaDoDestino.ConferirBanco(doDestino, bancoEsperado, coletor);
            if (daOrigem is not null && ConferenciaDoDestino.MesmaLinhagem(daOrigem, doDestino) && ConferenciaDoDestino.MesmoEndereco(daOrigem, doDestino))
            {
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.DestinoIgualAOrigem,
                    "O destino é o próprio banco da origem (a mesma linhagem, no mesmo endereço): confira as conexões da origem e do destino."));
            }

            var marcador = doDestino.Marcador;
            switch (marcador?.Situacao)
            {
                case SituacaoMarcador.Valido or SituacaoMarcador.Ausente:
                    break;
                case null:
                    coletor.Erro(new ErroPlano(
                        CodigosPlanejar.ComentarioAlheio,
                        "O dialeto não informa o que o destino tem no lugar do marcador do Incognito: sem saber, o preparar-destino não grava."));
                    break;
                default:
                    coletor.Erro(new ErroPlano(
                        CodigosPlanejar.ComentarioAlheio,
                        "O destino tem um comentário que não é um marcador válido do Incognito, e o preparar-destino nunca o sobrescreve: apague o comentário do banco à mão e rode de novo."));
                    break;
            }

            coletor.LancarSeHouverErros();
            var preparado = marcador!.Situacao == SituacaoMarcador.Valido;
            if (preparado && (await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false)).Existe)
            {
                return new ResultadoDoPreparo(SituacaoDoPreparo.JaPreparado, marcador.Identificador, []);
            }

            if (daOrigem is not null)
            {
                ConferenciaDoDestino.AvisarLinhagem(daOrigem, doDestino, coletor);
            }

            var avisos = coletor.Avisos();
            if (!await confirmar(new ResumoDoDestino(TextoSeguro.Nome(doDestino.Servidor), TextoSeguro.Nome(doDestino.Banco), avisos), cancelamento).ConfigureAwait(false))
            {
                return new ResultadoDoPreparo(SituacaoDoPreparo.Negado, preparado ? marcador.Identificador : null, avisos);
            }

            // A confirmação pode ter esperado muito: o bloqueio precisa estar vivo antes de gravar.
            var identificador = marcador.Identificador;
            if (!preparado)
            {
                await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
                identificador = Guid.NewGuid();
                var gravadoEm = ReparadorDoDestino.Segundo(relogio);
                await destino.GravarMarcadorAsync(identificador.Value, gravadoEm, cancelamento).ConfigureAwait(false);

                var relido = (await destino.LerIdentidadeAsync(cancelamento).ConfigureAwait(false)).Marcador;
                if (relido is not { Situacao: SituacaoMarcador.Valido } || relido.Identificador != identificador || relido.GravadoEm != gravadoEm)
                {
                    throw new FalhaDialetoException(CodigosPlanejar.EsquemaIncoerente, "O dialeto gravou o marcador do Incognito no destino, mas a releitura não o confirma.");
                }
            }

            // Depois do marcador: uma queda entre os dois deixa o destino marcado sem o diário, e o preparar-destino seguinte
            // só cria o diário.
            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            await destino.CriarDiarioAsync(concessao, cancelamento).ConfigureAwait(false);
            return (await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false)).Existe
                ? new ResultadoDoPreparo(preparado ? SituacaoDoPreparo.DiarioCriado : SituacaoDoPreparo.Gravado, identificador, avisos)
                : throw new FalhaDialetoException(CodigosPlanejar.EsquemaIncoerente, "O dialeto criou o diário do Incognito no destino, mas a releitura não o confirma.");
        }
        finally
        {
            // O descarte nunca lança: o erro que sai é o do preparo.
            await concessao.DisposeAsync().ConfigureAwait(false);
        }
    }
}
