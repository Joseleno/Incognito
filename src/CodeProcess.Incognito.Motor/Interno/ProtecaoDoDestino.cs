// A proteção chama o dialeto pelo envoltório e lê a identidade, tipos do contrato experimental de dialeto que o motor
// declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que a confirmação mostra antes de escrever no destino.</summary>
/// <param name="Servidor">O servidor do destino, escapado; vazio numa conexão local sem endereço.</param>
/// <param name="Banco">O banco do destino, escapado.</param>
/// <param name="Avisos">Os avisos da conferência (a linhagem ou o endereço iguais aos da origem).</param>
internal sealed record ResumoDoDestino(string Servidor, string Banco, IReadOnlyList<Aviso> Avisos)
{
    /// <summary>Os avisos, copiados.</summary>
    public IReadOnlyList<Aviso> Avisos { get; } = [.. Avisos];
}

/// <summary>O desfecho da proteção do destino.</summary>
/// <param name="Concessao">
/// O bloqueio, que o executor segura até o fim e confere antes de cada passo crítico; nulo quando a confirmação foi negada
/// (o bloqueio já foi liberado e nada foi escrito).
/// </param>
/// <param name="Avisos">Os avisos da conferência, para o relatório.</param>
/// <param name="Marcador">O identificador do marcador do destino, a que a execução fica amarrada no diário.</param>
internal sealed record ResultadoDaProtecao(DialetoProtegido.ConcessaoProtegida? Concessao, IReadOnlyList<Aviso> Avisos, Guid Marcador)
{
    /// <summary>Os avisos, copiados.</summary>
    public IReadOnlyList<Aviso> Avisos { get; } = [.. Avisos];

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ResultadoDaProtecao)} ({(Concessao is null ? "negada" : "confirmada")}, {Avisos.Count} avisos)";
}

/// <summary>
/// A proteção do destino antes da primeira escrita (o passo 3 da execução): adquire o bloqueio, lê a identidade dos dois
/// lados pelo próprio dialeto (sem depender do plano), confere o marcador, o <c>bancoEsperado</c>, o diário de execução e
/// as tabelas ignoradas com linhas, avisa a linhagem e pede a confirmação. Em qualquer falha, e na confirmação negada, o bloqueio é liberado
/// antes de sair; nada é escrito no destino.
/// </summary>
internal static class ProtecaoDoDestino
{
    /// <summary>Confere o destino e devolve o bloqueio.</summary>
    /// <param name="origem">A origem, conectada.</param>
    /// <param name="destino">O destino, conectado.</param>
    /// <param name="bancoEsperado">O <c>destino.bancoEsperado</c> da configuração; nulo quando não escrito.</param>
    /// <param name="ignoradas">
    /// As tabelas do destino com a ação <c>ignorar</c>, como o catálogo do destino as guarda, inclusive as que só existem
    /// nele.
    /// </param>
    /// <param name="aceitarDadosExistentes">Se as tabelas ignoradas podem ter linhas.</param>
    /// <param name="confirmar">A confirmação, com o servidor, o banco e os avisos; falso não escreve nada.</param>
    /// <param name="cancelamento">Cancela a conferência.</param>
    /// <returns>O bloqueio e os avisos, ou só os avisos com a confirmação negada.</returns>
    /// <exception cref="BloqueioDestinoException">Outra execução tem o bloqueio (<c>INC3004</c>).</exception>
    /// <exception cref="PlanoRecusadoException">
    /// O destino não está protegido (<c>INC360x</c>; sem o diário, <c>INC3607</c>), ou o dialeto recusou uma leitura.
    /// </exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaProtecao> ConferirAsync(
        DialetoProtegido origem,
        DialetoProtegido destino,
        string? bancoEsperado,
        IReadOnlyList<NomeObjeto> ignoradas,
        bool aceitarDadosExistentes,
        Func<ResumoDoDestino, CancellationToken, Task<bool>> confirmar,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(ignoradas);
        ArgumentNullException.ThrowIfNull(confirmar);

        // O bloqueio vem antes de tudo: com outra execução no destino, nada mais é pedido a ele.
        var concessao = await destino.TentarAdquirirBloqueioAsync(cancelamento).ConfigureAwait(false) ?? throw Ocupado();
        try
        {
            var daOrigem = await origem.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            var doDestino = await destino.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            var coletor = new ColetorDoPlano();
            ConferenciaDoDestino.Conferir(daOrigem, doDestino, bancoEsperado, exigirMarcador: true, coletor);

            // O diário só se lê no destino marcado e com o nome esperado; sem ele, nada que a execução altera se repara.
            if (!coletor.TemErros && !(await destino.LerDiarioAsync(concessao, cancelamento).ConfigureAwait(false)).Existe)
            {
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.DestinoSemDiario,
                    "O destino não tem o diário de execução do Incognito, onde a execução registra o que altera para repará-lo depois de uma queda: rode o preparar-destino antes de executar."));
            }

            // As linhas só se leem no destino marcado e com o nome esperado: num banco que não é o destino, nem isso.
            if (!coletor.TemErros && !aceitarDadosExistentes)
            {
                foreach (var tabela in ignoradas)
                {
                    if (await destino.TemLinhasAsync(tabela, cancelamento).ConfigureAwait(false))
                    {
                        coletor.Erro(new ErroPlano(
                            CodigosPlanejar.IgnoradaComLinhas,
                            "A tabela ignorada tem linhas no destino, que podem ter vindo de um restore com dados reais: esvazie-a, escreva a ação esvaziar, ou aceite os dados existentes.",
                            tabela: new NomeTabela(tabela.Esquema, tabela.Nome)));
                    }
                }
            }

            coletor.LancarSeHouverErros();
            var avisos = coletor.Avisos();
            if (!await confirmar(new ResumoDoDestino(TextoSeguro.Nome(doDestino.Servidor), TextoSeguro.Nome(doDestino.Banco), avisos), cancelamento).ConfigureAwait(false))
            {
                await concessao.DisposeAsync().ConfigureAwait(false);
                return new ResultadoDaProtecao(null, avisos, doDestino.Marcador!.Identificador!.Value);
            }

            return new ResultadoDaProtecao(concessao, avisos, doDestino.Marcador!.Identificador!.Value);
        }
        catch
        {
            // O descarte nunca lança: o erro que sai é o da conferência.
            await concessao.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>A exceção do bloqueio com outra execução, sem dizer quem o tem.</summary>
    /// <returns>A exceção.</returns>
    internal static BloqueioDestinoException Ocupado() => new(
        CodigosPlanejar.DestinoOcupado,
        "Outra execução do Incognito tem o bloqueio do destino, e nada foi escrito nele: espere ela terminar, ou confira se ela ainda roda.");
}
