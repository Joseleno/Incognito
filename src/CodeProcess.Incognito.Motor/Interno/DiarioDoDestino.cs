// O diário da execução chama o dialeto pelo envoltório, tipos do contrato experimental de dialeto que o motor declara;
// nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// As duas metades do diário que o executor chama com a concessão viva: antes do primeiro esvaziamento, registra a execução
/// e remove registrando as chaves e as triggers; depois da carga, devolve tudo em fases (as triggers, então as chaves
/// recriadas e validadas) e encerra. A perda do bloqueio interrompe na hora, sem escrever mais nada (nem o encerramento): o
/// diário fica para o <c>reparar</c>.
/// </summary>
internal static class DiarioDoDestino
{
    /// <summary>
    /// Registra a execução e remove registrando: as chaves da carga e as que o diário ainda guarda (a execução nova as
    /// adota), depois as triggers de cada raiz.
    /// </summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="marcador">O identificador do marcador do destino, a que a execução fica amarrada.</param>
    /// <param name="chaves">As chaves que a carga remove (<see cref="DadosDeExecucao.ChavesRemovidas"/>).</param>
    /// <param name="triggers">As raízes cujas triggers a carga desliga (<see cref="DadosDeExecucao.TriggersDesligadas"/>).</param>
    /// <param name="relogio">O relógio do início.</param>
    /// <param name="cancelamento">Cancela o preparo.</param>
    /// <returns>O identificador da execução.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>): nada mais foi escrito.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou; o que já foi removido está no diário.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<Guid> PrepararAsync(
        DialetoProtegido destino,
        DialetoProtegido.ConcessaoProtegida concessao,
        Guid marcador,
        IReadOnlyList<ChaveRemovida> chaves,
        IReadOnlyList<NomeObjeto> triggers,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        ArgumentNullException.ThrowIfNull(chaves);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(relogio);

        var execucao = Guid.NewGuid();
        await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
        await destino.IniciarExecucaoAsync(concessao, execucao, marcador, ReparadorDoDestino.Segundo(relogio), cancelamento).ConfigureAwait(false);

        // As da carga, na ordem dela, e depois as que ficaram no diário de uma execução anterior (a chave com órfão).
        var removidas = chaves.Select(chave => (Tabela: new NomeObjeto(chave.Tabela.Esquema, chave.Tabela.Tabela), Nome: chave.Chave)).ToList();
        var estado = await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false);
        removidas.AddRange(estado.Pendentes
            .Where(entrada => entrada.Tipo == TipoEntrada.ChaveEstrangeira)
            .Select(entrada => (entrada.Tabela, Nome: entrada.Nome!))
            .Where(adotada => !removidas.Contains(adotada)));

        foreach (var (tabela, nome) in removidas)
        {
            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            await destino.RemoverChaveAsync(concessao, execucao, tabela, nome, cancelamento).ConfigureAwait(false);
        }

        foreach (var raiz in triggers)
        {
            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            await destino.DesligarTriggersAsync(concessao, execucao, raiz, cancelamento).ConfigureAwait(false);
        }

        return execucao;
    }

    /// <summary>A primeira fase da volta: religa as triggers que a execução desligou.</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="cancelamento">Cancela a restauração (o executor passa um token próprio depois do Ctrl+C).</param>
    /// <returns>As falhas e as triggers puladas.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao ler o diário.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaRestauracao> ReligarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, Guid execucao, CancellationToken cancelamento) =>
        await RestauracaoDoDiario.ReligarAsync(destino, concessao, await DaExecucaoAsync(destino, concessao, execucao, cancelamento).ConfigureAwait(false), cancelamento).ConfigureAwait(false);

    /// <summary>A segunda fase da volta: recria e valida as chaves que a execução removeu.</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As chaves que continuam no diário e as falhas.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao ler o diário.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaRestauracao> RecriarEValidarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, Guid execucao, CancellationToken cancelamento) =>
        await RestauracaoDoDiario.RecriarEValidarAsync(destino, concessao, await DaExecucaoAsync(destino, concessao, execucao, cancelamento).ConfigureAwait(false), validar: true, cancelamento).ConfigureAwait(false);

    /// <summary>Grava a situação final da execução.</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="situacao">Concluída, falhou ou interrompida.</param>
    /// <param name="relogio">O relógio do encerramento.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task EncerrarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, Guid execucao, SituacaoExecucao situacao, TimeProvider relogio, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        ArgumentNullException.ThrowIfNull(relogio);
        await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
        await destino.EncerrarExecucaoAsync(concessao, execucao, situacao, ReparadorDoDestino.Segundo(relogio), cancelamento).ConfigureAwait(false);
    }

    /// <summary>As duas fases da volta e o encerramento como concluída (a chave pendente não muda a situação: dá a saída 4).</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="relogio">O relógio do encerramento.</param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As chaves que continuam no diário, as falhas e as triggers puladas.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao ler o diário ou ao encerrar.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaRestauracao> RestaurarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, Guid execucao, TimeProvider relogio, CancellationToken cancelamento)
    {
        var resultado = await ReligarAsync(destino, concessao, execucao, cancelamento).ConfigureAwait(false);
        resultado = resultado.Mais(await RecriarEValidarAsync(destino, concessao, execucao, cancelamento).ConfigureAwait(false));
        await EncerrarAsync(destino, concessao, execucao, SituacaoExecucao.Concluida, relogio, cancelamento).ConfigureAwait(false);
        return resultado;
    }

    // As entradas pendentes da execução, na ordem de restauração.
    private static async Task<IReadOnlyList<EntradaDiario>> DaExecucaoAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, Guid execucao, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
        var estado = await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false);
        return [.. estado.Pendentes.Where(entrada => entrada.Execucao == execucao)];
    }
}
