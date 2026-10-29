// A restauração chama o diário pelo envoltório e lê as entradas, tipos do contrato experimental de dialeto que o motor
// declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>Uma chave estrangeira que continua no diário depois da restauração.</summary>
/// <param name="Tabela">A tabela da chave, como o catálogo do destino a guarda.</param>
/// <param name="Nome">O nome da chave.</param>
/// <param name="Situacao">
/// Fora do destino (<see cref="SituacaoEntrada.Removida"/>: a validação direta não passou) ou de volta sem validação
/// (<see cref="SituacaoEntrada.SemValidacao"/>).
/// </param>
internal sealed record ChaveNoDiario(NomeObjeto Tabela, string Nome, SituacaoEntrada Situacao)
{
    /// <inheritdoc/>
    public override string ToString() => nameof(ChaveNoDiario);
}

/// <summary>Uma entrada do diário que não se restaurou, com o código da falha do dialeto.</summary>
/// <param name="Entrada">A entrada, como o diário a guarda.</param>
/// <param name="Codigo">O código da falha que o dialeto deu.</param>
internal sealed record EntradaNaoRestaurada(EntradaDiario Entrada, string Codigo)
{
    /// <inheritdoc/>
    public override string ToString() => $"{nameof(EntradaNaoRestaurada)} ({Codigo})";
}

/// <summary>O que a restauração deixou: as chaves que continuam no diário, as entradas que falharam e os avisos.</summary>
/// <param name="Pendentes">As chaves que continuam no diário (saída 4).</param>
/// <param name="Falhas">As entradas que não se restauraram (saída 1).</param>
/// <param name="Avisos">As triggers puladas (<c>INC4004</c>).</param>
internal sealed record ResultadoDaRestauracao(IReadOnlyList<ChaveNoDiario> Pendentes, IReadOnlyList<EntradaNaoRestaurada> Falhas, IReadOnlyList<Aviso> Avisos)
{
    /// <summary>Nada restaurado, nada pendente.</summary>
    internal static readonly ResultadoDaRestauracao Vazio = new([], [], []);

    /// <summary>As chaves pendentes, copiadas.</summary>
    public IReadOnlyList<ChaveNoDiario> Pendentes { get; } = [.. Pendentes];

    /// <summary>As falhas, copiadas.</summary>
    public IReadOnlyList<EntradaNaoRestaurada> Falhas { get; } = [.. Falhas];

    /// <summary>Os avisos, copiados.</summary>
    public IReadOnlyList<Aviso> Avisos { get; } = [.. Avisos];

    /// <summary>
    /// As ocorrências para quem mostra o resultado: cada chave pendente (<c>INC4002</c>), cada entrada que não se restaurou
    /// (<c>INC4003</c>, com o código do dialeto) e as triggers puladas. Só nomes de catálogo, escapados.
    /// </summary>
    /// <returns>As ocorrências, nessa ordem.</returns>
    internal IReadOnlyList<Aviso> Ocorrencias() =>
    [
        .. Pendentes.Select(chave => new Aviso(
            CodigosExecucao.ChavePendente,
            $"A chave estrangeira {TextoSeguro.Nome(chave.Nome)} não validou (há linha sem o par na tabela referenciada) e ficou no diário, {(chave.Situacao == SituacaoEntrada.Removida ? "fora do destino" : "de volta sem validação")}: corrija os dados e rode o reparar, ou a próxima execução tenta de novo.",
            RestauracaoDoDiario.Tabela(chave.Tabela),
            objeto: TextoSeguro.Nome(chave.Nome))),
        .. Falhas.Select(falha => new Aviso(
            CodigosExecucao.EntradaNaoRestaurada,
            $"{(falha.Entrada.Tipo == TipoEntrada.ChaveEstrangeira ? $"A chave estrangeira {TextoSeguro.Nome(falha.Entrada.Nome!)}" : "O estado das triggers")} guardado no diário não voltou: o dialeto falhou com o código {falha.Codigo}. Confira o destino; se não pode voltar (outra chave tomou o nome, ou a tabela sumiu), rode o reparar com --descartar.",
            RestauracaoDoDiario.Tabela(falha.Entrada.Tabela),
            objeto: falha.Entrada.Nome is { } nome ? TextoSeguro.Nome(nome) : null)),
        .. Avisos,
    ];

    /// <summary>Este resultado seguido de outro.</summary>
    /// <param name="outro">O outro.</param>
    /// <returns>Os dois juntos.</returns>
    internal ResultadoDaRestauracao Mais(ResultadoDaRestauracao outro) =>
        new([.. Pendentes, .. outro.Pendentes], [.. Falhas, .. outro.Falhas], [.. Avisos, .. outro.Avisos]);

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ResultadoDaRestauracao)} ({Pendentes.Count} pendentes, {Falhas.Count} falhas, {Avisos.Count} avisos)";
}

/// <summary>
/// O caminho de volta do diário, comum ao reparo e ao fim da execução, em fases: primeiro as triggers, depois as chaves
/// (todas as recriações antes de qualquer validação, em série, na conexão de controle). Cada passo confere o bloqueio antes;
/// a perda e o cancelamento sobem na hora, sem escrever mais nada. A falha do dialeto numa entrada fica no resultado com o
/// código, e o resto segue: uma entrada que não se recria não trava as outras.
/// </summary>
internal static class RestauracaoDoDiario
{
    /// <summary>Religa as triggers das entradas, cada uma no estado original.</summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="entradas">As entradas pendentes; só as de triggers são tratadas.</param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As falhas e os avisos das triggers puladas.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaRestauracao> ReligarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, IEnumerable<EntradaDiario> entradas, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        ArgumentNullException.ThrowIfNull(entradas);
        var falhas = new List<EntradaNaoRestaurada>();
        var avisos = new List<Aviso>();
        foreach (var entrada in entradas.Where(entrada => entrada.Tipo == TipoEntrada.Triggers).ToList())
        {
            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            try
            {
                foreach (var pulada in await destino.ReligarTriggersAsync(concessao, entrada, cancelamento).ConfigureAwait(false))
                {
                    avisos.Add(new Aviso(
                        CodigosExecucao.TriggerPulada,
                        $"A trigger {TextoSeguro.Nome(pulada.Nome)} estava desligada pelo Incognito e não existe mais no destino: não foi religada.",
                        Tabela(pulada.Tabela),
                        objeto: TextoSeguro.Nome(pulada.Nome)));
                }
            }
            catch (FalhaDialetoException falha)
            {
                falhas.Add(new EntradaNaoRestaurada(entrada, falha.Codigo));
            }
        }

        return new ResultadoDaRestauracao([], falhas, avisos);
    }

    /// <summary>
    /// Recria as chaves removidas e, quando pedido, valida as que voltaram sem validação; a que não valida continua no
    /// diário.
    /// </summary>
    /// <param name="destino">O destino.</param>
    /// <param name="concessao">A concessão viva do bloqueio.</param>
    /// <param name="entradas">As entradas pendentes; só as de chave são tratadas.</param>
    /// <param name="validar">
    /// Se valida as chaves que voltaram sem validação; sem validar, elas continuam no diário (o reparo no começo do
    /// <c>executar</c>, que vai esvaziar as tabelas).
    /// </param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As chaves que continuam no diário e as falhas.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<ResultadoDaRestauracao> RecriarEValidarAsync(
        DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, IEnumerable<EntradaDiario> entradas, bool validar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(concessao);
        ArgumentNullException.ThrowIfNull(entradas);
        var falhas = new List<EntradaNaoRestaurada>();

        // Todas as recriações antes de qualquer validação: dois VALIDATE do mesmo filho se bloqueiam, e a recriação de uma
        // chave não espera a validação de outra.
        var depois = new List<EntradaDiario>();
        foreach (var entrada in entradas.Where(entrada => entrada.Tipo == TipoEntrada.ChaveEstrangeira).ToList())
        {
            if (entrada.Situacao != SituacaoEntrada.Removida)
            {
                depois.Add(entrada);
                continue;
            }

            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            try
            {
                if (await destino.RecriarChaveAsync(concessao, entrada, cancelamento).ConfigureAwait(false) is { } situacao)
                {
                    depois.Add(Com(entrada, situacao));
                }
            }
            catch (FalhaDialetoException falha)
            {
                falhas.Add(new EntradaNaoRestaurada(entrada, falha.Codigo));
            }
        }

        var pendentes = new List<ChaveNoDiario>();
        foreach (var entrada in depois)
        {
            if (!validar || entrada.Situacao != SituacaoEntrada.SemValidacao)
            {
                pendentes.Add(new ChaveNoDiario(entrada.Tabela, entrada.Nome!, entrada.Situacao));
                continue;
            }

            await concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            try
            {
                if (await destino.ValidarChaveAsync(concessao, entrada, cancelamento).ConfigureAwait(false) is { } situacao)
                {
                    pendentes.Add(new ChaveNoDiario(entrada.Tabela, entrada.Nome!, situacao));
                }
            }
            catch (FalhaDialetoException falha)
            {
                falhas.Add(new EntradaNaoRestaurada(entrada, falha.Codigo));
            }
        }

        return new ResultadoDaRestauracao(pendentes, falhas, []);
    }

    /// <summary>A tabela de um nome do catálogo, para uma ocorrência.</summary>
    /// <param name="nome">O nome.</param>
    /// <returns>A tabela.</returns>
    internal static NomeTabela Tabela(NomeObjeto nome) => new(nome.Esquema, nome.Nome);

    // A entrada com a situação que o dialeto devolveu.
    private static EntradaDiario Com(EntradaDiario entrada, SituacaoEntrada situacao) => new()
    {
        Execucao = entrada.Execucao,
        Tipo = entrada.Tipo,
        Tabela = entrada.Tabela,
        Nome = entrada.Nome,
        Situacao = situacao,
    };
}
