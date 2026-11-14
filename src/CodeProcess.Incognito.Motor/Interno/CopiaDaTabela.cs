// A cópia lê e grava pelo contrato experimental de dialeto, sempre pelo envoltório; nada daqui é público.
#pragma warning disable INCX001

using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que a cópia de uma tabela contou: as linhas lidas e gravadas, a duração e os desfechos de cada coluna tratada.</summary>
/// <param name="Nome">A unidade, pelo nome da carga.</param>
/// <param name="Acao">A ação.</param>
/// <param name="Lidas">As linhas lidas da origem.</param>
/// <param name="Gravadas">As linhas que a gravação confirmou.</param>
/// <param name="Duracao">Quanto a tabela levou.</param>
/// <param name="Colunas">Os desfechos de cada coluna tratada, na ordem da unidade.</param>
internal sealed record ContagemDaTabela(NomeTabela Nome, AcaoTabela Acao, long Lidas, long Gravadas, TimeSpan Duracao, IReadOnlyList<ContagemDaColuna> Colunas)
{
    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ContagemDaTabela)} ({Lidas} lidas, {Gravadas} gravadas)";
}

/// <summary>Quantas vezes cada desfecho saiu numa coluna tratada (só os que saíram).</summary>
/// <param name="Coluna">A coluna, pelo nome no catálogo da origem.</param>
/// <param name="Desfechos">Cada desfecho e a contagem.</param>
internal sealed record ContagemDaColuna(string Coluna, IReadOnlyDictionary<Desfecho, long> Desfechos)
{
    /// <inheritdoc/>
    public override string ToString() => nameof(ContagemDaColuna);
}

/// <summary>
/// O contador interno do pipeline: os lotes vivos (lidos e ainda não descartados nem passados à gravação) e o pico deles,
/// para provar que a memória não cresce com a tabela (C1). Seguro entre threads.
/// </summary>
internal sealed class ContadorDoPipeline
{
    private long _vivos;
    private long _pico;

    /// <summary>Os lotes vivos agora.</summary>
    internal long Vivos => Interlocked.Read(ref _vivos);

    /// <summary>O maior número de lotes vivos ao mesmo tempo.</summary>
    internal long Pico => Interlocked.Read(ref _pico);

    internal void Entrou()
    {
        var vivos = Interlocked.Increment(ref _vivos);
        var pico = Interlocked.Read(ref _pico);
        while (vivos > pico)
        {
            var anterior = Interlocked.CompareExchange(ref _pico, vivos, pico);
            if (anterior == pico)
            {
                return;
            }

            pico = anterior;
        }
    }

    internal void Saiu() => Interlocked.Decrement(ref _vivos);

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ContadorDoPipeline)} ({Vivos} vivos, pico {Pico})";
}

/// <summary>
/// A cópia de uma unidade (seção 4.4 da proposta): o esvaziamento puro, ou a leitura (um lote por vez) → canal limitado a 2
/// → K tratamentos → canal limitado a 2 → gravação; sem tratada, a leitura vai direto à gravação. Cada etapa conclui o seu
/// canal no fim, o segundo só quando os K terminam; toda tarefa é aguardada e observada; a primeira falha que não é
/// cancelamento vence e cancela a tabela; os lotes que sobram nos canais são descartados. A leitura termina (com as
/// conferências da origem) antes de a gravação concluir, e o bloqueio é conferido antes de abrir a gravação, antes do
/// esvaziamento e logo antes de concluir, nunca por lote. Na falha, a gravação é descartada sem concluir (desfaz).
/// </summary>
internal static class CopiaDaTabela
{
    /// <summary>A capacidade de cada canal, em lotes.</summary>
    internal const int Capacidade = 2;

    /// <summary>Copia ou esvazia uma unidade.</summary>
    /// <param name="unidade">A unidade da carga refeita, copiada ou esvaziada.</param>
    /// <param name="leitor">O leitor da sessão do trabalhador (a unidade só esvaziada não lê).</param>
    /// <param name="destino">O destino conectado.</param>
    /// <param name="transformadores">Os transformadores da execução.</param>
    /// <param name="tratamentos">Quantos tratamentos em paralelo (K), ao menos 1.</param>
    /// <param name="conferirBloqueio">Confere que o bloqueio do destino segue vivo.</param>
    /// <param name="contador">O contador de lotes vivos.</param>
    /// <param name="relogio">O relógio, para a duração.</param>
    /// <param name="cancelamento">O token da execução.</param>
    /// <returns>O que a tabela contou.</returns>
    /// <exception cref="FalhaTransformacaoException">Uma tratada não pôde ser transformada.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="BloqueioDestinoException">O bloqueio do destino se perdeu.</exception>
    /// <exception cref="OperationCanceledException">A execução foi cancelada.</exception>
    internal static async Task<ContagemDaTabela> CopiarAsync(
        UnidadeDeCopia unidade,
        DialetoProtegido.LeitorProtegido? leitor,
        DialetoProtegido destino,
        TransformadoresDaExecucao transformadores,
        int tratamentos,
        Func<CancellationToken, Task> conferirBloqueio,
        ContadorDoPipeline contador,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(unidade);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(conferirBloqueio);
        ArgumentNullException.ThrowIfNull(contador);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentOutOfRangeException.ThrowIfLessThan(tratamentos, 1);
        var inicio = relogio.GetTimestamp();
        var noDestino = unidade.NoDestino ?? throw new InvalidOperationException("Unidade tocada sem a tabela no destino.");
        if (unidade.Acao == AcaoTabela.Esvaziar)
        {
            await conferirBloqueio(cancelamento).ConfigureAwait(false);
            await destino.EsvaziarAsync(Objeto(noDestino), cancelamento).ConfigureAwait(false);
            return new ContagemDaTabela(unidade.Nome, unidade.Acao, 0, 0, relogio.GetElapsedTime(inicio), []);
        }

        if (unidade.Acao != AcaoTabela.Copiar || leitor is null)
        {
            throw new InvalidOperationException("Só a unidade copiada, com um leitor, ou a esvaziada passam pela cópia.");
        }

        ArgumentNullException.ThrowIfNull(transformadores);
        using var daTabela = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        var falha = new PrimeiraFalha(daTabela);
        var tratamento = unidade.Tratadas.Count > 0 ? new TratamentoDoLote(unidade, transformadores, daTabela.Token) : null;
        var pedidoDaLeitura = new PedidoLeituraTabela { Tabela = Objeto(unidade.Nome), Colunas = unidade.ColunasLidas, Convertidas = tratamento?.Convertidas ?? [] };
        var pedidoDaGravacao = new PedidoGravacao { Tabela = Objeto(noDestino), Colunas = unidade.ColunasNoDestino, Tratadas = tratamento?.Tratadas ?? [] };

        await conferirBloqueio(cancelamento).ConfigureAwait(false);
        await using var leitura = await leitor.AbrirTabelaAsync(pedidoDaLeitura, cancelamento).ConfigureAwait(false);
        await using var gravacao = await destino.AbrirGravacaoAsync(pedidoDaGravacao, cancelamento).ConfigureAwait(false);

        var lidos = Channel.CreateBounded<DialetoProtegido.LoteProtegido>(new BoundedChannelOptions(Capacidade) { SingleWriter = true, SingleReader = tratamento is null });
        var tratados = tratamento is null ? lidos : Channel.CreateBounded<DialetoProtegido.LoteProtegido>(new BoundedChannelOptions(Capacidade) { SingleReader = true });
        var contagens = tratamento is null ? [] : Enumerable.Range(0, tratamentos).Select(_ => tratamento.NovaContagem()).ToArray();
        long linhasLidas = 0;

        var lendo = Task.Run(async () => linhasLidas = await LerAsync(leitura, lidos.Writer, contador, falha, daTabela.Token).ConfigureAwait(false), CancellationToken.None);
        var tratando = tratamento is null
            ? Task.CompletedTask
            : TratarTodosAsync(tratamento, contagens, lidos.Reader, tratados.Writer, contador, falha, daTabela.Token);
        await GravarAsync(gravacao, tratados.Reader, contador, falha, daTabela.Token).ConfigureAwait(false);

        // Toda tarefa aguardada (nenhuma lança: cada uma registra a falha), e o que sobrou nos canais, descartado.
        await Task.WhenAll(lendo, tratando).ConfigureAwait(false);
        Esvaziar(lidos.Reader, contador);
        Esvaziar(tratados.Reader, contador);
        falha.LancarSeHouver();
        cancelamento.ThrowIfCancellationRequested();

        // A leitura já terminou, com as conferências da origem: só então o destino confirma.
        await conferirBloqueio(cancelamento).ConfigureAwait(false);
        var gravadas = await gravacao.ConcluirAsync(cancelamento).ConfigureAwait(false);
        return new ContagemDaTabela(unidade.Nome, unidade.Acao, linhasLidas, gravadas, relogio.GetElapsedTime(inicio), Colunas(unidade, contagens));
    }

    private static async Task<long> LerAsync(
        DialetoProtegido.LeituraProtegida leitura, ChannelWriter<DialetoProtegido.LoteProtegido> saida, ContadorDoPipeline contador, PrimeiraFalha falha, CancellationToken token)
    {
        long linhas = 0;
        try
        {
            while (await leitura.LerLoteAsync(token).ConfigureAwait(false) is { } lote)
            {
                contador.Entrou();
                linhas += lote.Quantidade;
                try
                {
                    await saida.WriteAsync(lote, token).ConfigureAwait(false);
                }
                catch
                {
                    Descartar(lote, contador);
                    throw;
                }
            }
        }
        catch (Exception excecao)
        {
            falha.Registrar(excecao);
        }
        finally
        {
            saida.TryComplete();
        }

        return linhas;
    }

    // Os K tratamentos; o segundo canal só se conclui quando todos terminam.
    private static async Task TratarTodosAsync(
        TratamentoDoLote tratamento,
        TratamentoDoLote.Contagem[] contagens,
        ChannelReader<DialetoProtegido.LoteProtegido> entrada,
        ChannelWriter<DialetoProtegido.LoteProtegido> saida,
        ContadorDoPipeline contador,
        PrimeiraFalha falha,
        CancellationToken token)
    {
        try
        {
            await Task.WhenAll(contagens.Select(contagem => Task.Run(() => TratarAsync(tratamento, contagem, entrada, saida, contador, falha, token), CancellationToken.None))).ConfigureAwait(false);
        }
        finally
        {
            saida.TryComplete();
        }
    }

    private static async Task TratarAsync(
        TratamentoDoLote tratamento,
        TratamentoDoLote.Contagem contagem,
        ChannelReader<DialetoProtegido.LoteProtegido> entrada,
        ChannelWriter<DialetoProtegido.LoteProtegido> saida,
        ContadorDoPipeline contador,
        PrimeiraFalha falha,
        CancellationToken token)
    {
        try
        {
            await foreach (var lote in entrada.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    tratamento.Tratar(lote, contagem);
                    await saida.WriteAsync(lote, token).ConfigureAwait(false);
                }
                catch
                {
                    Descartar(lote, contador);
                    throw;
                }
            }
        }
        catch (Exception excecao)
        {
            falha.Registrar(excecao);
        }
    }

    // A gravação é dona de cada lote que recebe: ela o descarta, também na falha.
    private static async Task GravarAsync(
        DialetoProtegido.GravacaoProtegida gravacao, ChannelReader<DialetoProtegido.LoteProtegido> entrada, ContadorDoPipeline contador, PrimeiraFalha falha, CancellationToken token)
    {
        try
        {
            await foreach (var lote in entrada.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await gravacao.GravarAsync(lote, token).ConfigureAwait(false);
                }
                finally
                {
                    contador.Saiu();
                }
            }
        }
        catch (Exception excecao)
        {
            falha.Registrar(excecao);
        }
    }

    private static void Esvaziar(ChannelReader<DialetoProtegido.LoteProtegido> canal, ContadorDoPipeline contador)
    {
        while (canal.TryRead(out var lote))
        {
            Descartar(lote, contador);
        }
    }

    private static void Descartar(DialetoProtegido.LoteProtegido lote, ContadorDoPipeline contador)
    {
        lote.Dispose();
        contador.Saiu();
    }

    private static ContagemDaColuna[] Colunas(UnidadeDeCopia unidade, TratamentoDoLote.Contagem[] contagens)
    {
        if (contagens.Length == 0)
        {
            return [];
        }

        var total = contagens[0];
        foreach (var outra in contagens.Skip(1))
        {
            total.Somar(outra);
        }

        return
        [
            .. unidade.Tratadas.Select((tratada, coluna) => new ContagemDaColuna(
                tratada.Coluna,
                total.PorColuna[coluna]
                    .Select((quantidade, desfecho) => (Desfecho: (Desfecho)desfecho, Quantidade: quantidade))
                    .Where(par => par.Quantidade > 0)
                    .ToDictionary(par => par.Desfecho, par => par.Quantidade))),
        ];
    }

    private static NomeObjeto Objeto(NomeTabela nome) => new(nome.Esquema, nome.Tabela);

    // A primeira falha que não é cancelamento vence e cancela a tabela; o cancelamento que ela causou não conta.
    private sealed class PrimeiraFalha(CancellationTokenSource daTabela)
    {
        private Exception? _primeira;

        internal void Registrar(Exception excecao)
        {
            if (excecao is OperationCanceledException && daTabela.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _primeira, excecao, null) is null)
            {
                daTabela.Cancel();
            }
        }

        internal void LancarSeHouver()
        {
            if (Volatile.Read(ref _primeira) is { } primeira)
            {
                ExceptionDispatchInfo.Throw(primeira);
            }
        }
    }
}
