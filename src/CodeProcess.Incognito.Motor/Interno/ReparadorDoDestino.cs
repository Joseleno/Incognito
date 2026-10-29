// O reparo chama o diário pelo envoltório e lê a identidade, tipos do contrato experimental de dialeto que o motor
// declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>Como reparar.</summary>
/// <param name="Validar">
/// Se valida as chaves que voltam sem validação: o <c>reparar</c> valida; o reparo no começo do <c>executar</c> não (a
/// tabela vai ser esvaziada, e a chave pendente é adotada pela execução).
/// </param>
/// <param name="Descartar">
/// O <c>reparar --descartar</c>: a confirmação de apagar do diário, sem restaurar, cada entrada que não se restaurou; nula
/// nunca apaga.
/// </param>
internal sealed record OpcoesDoReparo(bool Validar, Func<EntradaNaoRestaurada, CancellationToken, Task<bool>>? Descartar = null)
{
    /// <inheritdoc/>
    public override string ToString() => $"{nameof(OpcoesDoReparo)} (validar: {Validar}, descartar: {Descartar is not null})";
}

/// <summary>O que o reparo fez.</summary>
/// <param name="Restauracao">As chaves que continuam no diário, as entradas que não se restauraram e as triggers puladas.</param>
/// <param name="Descartadas">As entradas apagadas do diário sem restaurar, depois da confirmação.</param>
/// <param name="Interrompidas">Quantas execuções em andamento foram marcadas como interrompidas.</param>
internal sealed record ResultadoDoReparo(ResultadoDaRestauracao Restauracao, IReadOnlyList<EntradaDiario> Descartadas, int Interrompidas)
{
    /// <summary>Nada no diário.</summary>
    internal static readonly ResultadoDoReparo Vazio = new(ResultadoDaRestauracao.Vazio, [], 0);

    /// <summary>As entradas descartadas, copiadas.</summary>
    public IReadOnlyList<EntradaDiario> Descartadas { get; } = [.. Descartadas];

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ResultadoDoReparo)} ({Restauracao}, {Descartadas.Count} descartadas, {Interrompidas} interrompidas)";
}

/// <summary>
/// O <c>reparar</c>, e o primeiro passo do <c>executar</c>: devolve ao destino o que o diário guarda de uma execução
/// interrompida (as triggers no estado exato, depois as chaves recriadas e validadas) e marca como interrompida toda execução
/// que ficou em andamento. Só trabalha com a concessão viva e com o diário deste destino: uma execução de outro marcador
/// recusa o reparo inteiro (<c>INC3608</c>). Idempotente: rodar de novo depois de uma queda não muda o resultado.
/// </summary>
internal static class ReparadorDoDestino
{
    /// <summary>Repara o destino com a concessão que o chamador já tem (o <c>executar</c>, depois da proteção).</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="marcador">O identificador do marcador do destino.</param>
    /// <param name="opcoes">Se valida e se descarta.</param>
    /// <param name="relogio">O relógio da data do encerramento.</param>
    /// <param name="cancelamento">Cancela o reparo.</param>
    /// <returns>O que o reparo fez.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>): nada mais foi escrito.</exception>
    /// <exception cref="PlanoRecusadoException">O diário tem execução de outro marcador (<c>INC3608</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao ler o diário ou ao encerrar uma execução.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDoReparo> RepararAsync(
        DialetoProtegido destino,
        DialetoProtegido.ConcessaoProtegida concessao,
        Guid marcador,
        OpcoesDoReparo opcoes,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(relogio);

        await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
        var estado = await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false);
        if (!estado.Existe)
        {
            return ResultadoDoReparo.Vazio;
        }

        // O diário de outro marcador não é deste destino (um restore de outro banco, ou um marcador refeito): nada dele é
        // executado aqui.
        if (estado.Execucoes.Any(execucao => execucao.Marcador != marcador))
        {
            throw new PlanoRecusadoException([new ErroPlano(
                CodigosPlanejar.DiarioDeOutroMarcador,
                "O diário do destino tem execução de outro marcador do Incognito: ele não é deste destino, e nada dele foi executado. Confira o destino; se o banco foi restaurado de outro, apague o esquema incognito à mão e rode o preparar-destino.")]);
        }

        var restauracao = await RestauracaoDoDiario.ReligarAsync(destino, concessao, estado.Pendentes, cancelamento).ConfigureAwait(false);
        restauracao = restauracao.Mais(await RestauracaoDoDiario.RecriarEValidarAsync(destino, concessao, estado.Pendentes, opcoes.Validar, cancelamento).ConfigureAwait(false));

        var descartadas = new List<EntradaDiario>();
        if (opcoes.Descartar is { } confirmar)
        {
            var falhas = new List<EntradaNaoRestaurada>();
            foreach (var falha in restauracao.Falhas)
            {
                if (!await confirmar(falha, cancelamento).ConfigureAwait(false))
                {
                    falhas.Add(falha);
                    continue;
                }

                await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
                await destino.DescartarEntradaAsync(concessao, falha.Entrada, cancelamento).ConfigureAwait(false);
                descartadas.Add(falha.Entrada);
            }

            restauracao = new ResultadoDaRestauracao(restauracao.Pendentes, falhas, restauracao.Avisos);
        }

        var interrompidas = 0;
        foreach (var execucao in estado.Execucoes.Where(execucao => execucao.Situacao == SituacaoExecucao.EmAndamento))
        {
            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            await destino.EncerrarExecucaoAsync(concessao, execucao.Id, SituacaoExecucao.Interrompida, Segundo(relogio), cancelamento).ConfigureAwait(false);
            interrompidas++;
        }

        return new ResultadoDoReparo(restauracao, descartadas, interrompidas);
    }

    /// <summary>
    /// O comando <c>reparar</c>: adquire o bloqueio, exige o marcador válido e o <c>bancoEsperado</c>, repara e libera o
    /// bloqueio, também na falha.
    /// </summary>
    /// <param name="destino">O destino, conectado.</param>
    /// <param name="bancoEsperado">O <c>destino.bancoEsperado</c> da configuração; nulo quando não escrito.</param>
    /// <param name="opcoes">Se descarta (o <c>reparar</c> sempre valida).</param>
    /// <param name="relogio">O relógio da data do encerramento.</param>
    /// <param name="cancelamento">Cancela o reparo.</param>
    /// <returns>O que o reparo fez.</returns>
    /// <exception cref="BloqueioDestinoException">
    /// Outra execução tem o bloqueio (<c>INC3004</c>), ou ele se perdeu no meio (<c>INC4001</c>).
    /// </exception>
    /// <exception cref="PlanoRecusadoException">
    /// O destino não tem o marcador válido (<c>INC3600</c>, <c>INC3601</c>), não é o banco esperado (<c>INC3603</c>), ou o
    /// diário é de outro marcador (<c>INC3608</c>).
    /// </exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDoReparo> RepararComBloqueioAsync(
        DialetoProtegido destino,
        string? bancoEsperado,
        OpcoesDoReparo opcoes,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(opcoes);

        var concessao = await destino.TentarAdquirirBloqueioAsync(cancelamento).ConfigureAwait(false) ?? throw ProtecaoDoDestino.Ocupado();
        try
        {
            var identidade = await destino.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            var coletor = new ColetorDoPlano();
            ConferenciaDoDestino.ConferirBanco(identidade, bancoEsperado, coletor);
            switch (identidade.Marcador?.Situacao)
            {
                case SituacaoMarcador.Valido:
                    break;
                case SituacaoMarcador.Invalido:
                    coletor.Erro(new ErroPlano(
                        CodigosPlanejar.MarcadorIlegivel,
                        "O destino tem um marcador do Incognito que não se lê: o reparar só trabalha num destino preparado, e nada foi executado."));
                    break;
                default:
                    coletor.Erro(new ErroPlano(
                        CodigosPlanejar.DestinoSemMarcador,
                        "O destino não tem o marcador do Incognito, ou o dialeto não informa: o reparar só trabalha num destino preparado, e nada foi executado."));
                    break;
            }

            coletor.LancarSeHouverErros();
            return await RepararAsync(destino, concessao, identidade.Marcador!.Identificador!.Value, opcoes with { Validar = true }, relogio, cancelamento).ConfigureAwait(false);
        }
        finally
        {
            // O descarte nunca lança: o erro que sai é o do reparo.
            await concessao.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>O instante, em UTC, cortado no segundo (como o marcador).</summary>
    /// <param name="relogio">O relógio.</param>
    /// <returns>O instante.</returns>
    internal static DateTimeOffset Segundo(TimeProvider relogio)
    {
        var agora = relogio.GetUtcNow();
        return new DateTimeOffset(agora.UtcTicks - (agora.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
