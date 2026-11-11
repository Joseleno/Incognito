using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// O que gravar numa tabela do destino: as colunas, as mesmas da leitura e na mesma ordem, e as tratadas, que toda linha de
/// todo lote precisa ter escritas ou mantidas.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PedidoGravacao
{
    private readonly NomeObjeto _tabela = null!;
    private readonly IReadOnlyList<string> _colunas = [];
    private readonly IReadOnlyList<ColunaConvertida> _tratadas = [];

    /// <summary>A tabela do destino, como o catálogo a guarda; numa particionada, a raiz.</summary>
    public required NomeObjeto Tabela { get => _tabela; init => _tabela = value ?? throw new ArgumentNullException(nameof(Tabela)); }

    /// <summary>
    /// As colunas do destino, como o catálogo as guarda: as mesmas do pedido da leitura que produziu os lotes, na mesma ordem;
    /// ao menos uma, sem repetidas.
    /// </summary>
    public required IReadOnlyList<string> Colunas
    {
        get => _colunas;
        init
        {
            var copia = Lista.CopiarNomes(value, nameof(Colunas));
            if (copia.Count == 0)
            {
                throw new ArgumentException("A gravação de uma tabela tem ao menos uma coluna.", nameof(Colunas));
            }

            if (copia.Distinct(StringComparer.Ordinal).Count() != copia.Count)
            {
                throw new ArgumentException("A lista das colunas tem coluna repetida.", nameof(Colunas));
            }

            _colunas = copia;
        }
    }

    /// <summary>
    /// As colunas tratadas, pela posição em <see cref="Colunas"/> e com o tipo em que foram lidas; sem posição repetida.
    /// Obrigatória: a tabela sem tratada passa uma lista vazia (esquecê-la desligaria a conferência da escrita).
    /// </summary>
    public required IReadOnlyList<ColunaConvertida> Tratadas
    {
        get => _tratadas;
        init
        {
            var copia = Lista.Copiar(value, nameof(Tratadas));
            if (copia.Select(tratada => tratada.Indice).Distinct().Count() != copia.Count)
            {
                throw new ArgumentException("A lista das tratadas tem posição repetida.", nameof(Tratadas));
            }

            _tratadas = copia;
        }
    }

    /// <summary>O número de colunas e de tratadas, sem os nomes.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => $"{nameof(PedidoGravacao)} ({Colunas.Count} colunas, {Tratadas.Count} tratadas)";

    /// <summary>Confere o que só se confere com as duas listas: toda tratada aponta uma coluna do pedido.</summary>
    /// <exception cref="ArgumentException">Tratada fora das colunas.</exception>
    internal void Conferir()
    {
        if (Tratadas.Any(tratada => tratada.Indice >= Colunas.Count))
        {
            throw new ArgumentException("Uma coluna tratada aponta uma posição fora das colunas do pedido.", "pedido");
        }
    }
}

/// <summary>
/// A gravação das tabelas do destino: o esvaziamento e a gravação dos lotes lidos, cada chamada numa conexão própria, fechada
/// no fim. Seguro entre threads: gravações de tabelas diferentes correm em paralelo. O descarte do dialeto fecha as
/// gravações vivas.
/// </summary>
/// <remarks>
/// <para>
/// O conteúdo anterior sai da tabela (não do disco nem das cópias do banco): a gravação esvazia e grava. Numa particionada,
/// com as partições; nas outras, só a própria tabela, sem as herdeiras de herança clássica (como a leitura das linhas). Uma
/// tabela que outra sessão segura falha em tempo limitado, sem esperar. O descarte do dialeto espera as chamadas em curso:
/// quem chama cancela antes.
/// </para>
/// <para>
/// O valor gravado é de coluna: nenhuma exceção, mensagem ou registro do dialeto o traz, nem a violação de uma restrição do
/// destino, que sai só com nomes. O motor traduz qualquer exceção do gravador só com o código.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class GravadorEmLote
{
    /// <summary>Esvazia a tabela numa conexão própria e confirma (numa particionada, com as partições; nas outras, só ela).</summary>
    /// <param name="tabela">A tabela do destino, como o catálogo a guarda.</param>
    /// <param name="cancelamento">Cancela o esvaziamento.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="ArgumentNullException">Tabela nula.</exception>
    /// <exception cref="ObjectDisposedException">Dialeto descartado.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    public Task EsvaziarAsync(NomeObjeto tabela, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        return EsvaziarAsyncCore(tabela, cancelamento);
    }

    /// <summary>
    /// Abre a gravação de uma tabela numa conexão própria: esvazia e começa a gravar. Só a conclusão confirma; sem ela, o
    /// descarte desfaz o que o banco deixa desfazer (<see cref="GravacaoTabela.DisposeAsync"/>).
    /// </summary>
    /// <param name="pedido">A tabela, as colunas e as tratadas.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A gravação, que quem chama conclui e descarta.</returns>
    /// <exception cref="ArgumentNullException">Pedido nulo.</exception>
    /// <exception cref="ArgumentException">Tratada fora das colunas do pedido.</exception>
    /// <exception cref="ObjectDisposedException">Dialeto descartado.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    /// <exception cref="InvalidOperationException">O dialeto devolveu uma gravação inválida.</exception>
    public async Task<GravacaoTabela> AbrirAsync(PedidoGravacao pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        pedido.Conferir();
        var gravacao = await AbrirAsyncCore(pedido, cancelamento).ConfigureAwait(false);

        // A gravação nula, já descartada ou de outro pedido é defeito do dialeto; a de outro pedido não se descarta daqui.
        if (gravacao is null || gravacao.Descartada || !gravacao.Ligar(pedido))
        {
            throw new InvalidOperationException("O dialeto não devolveu uma gravação nova para este pedido.");
        }

        return gravacao;
    }

    /// <summary>O esvaziamento, com a tabela conferida.</summary>
    /// <param name="tabela">A tabela.</param>
    /// <param name="cancelamento">Cancela o esvaziamento.</param>
    /// <returns>A tarefa.</returns>
    protected abstract Task EsvaziarAsyncCore(NomeObjeto tabela, CancellationToken cancelamento);

    /// <summary>A abertura da gravação, com o pedido conferido.</summary>
    /// <param name="pedido">O pedido.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>Uma gravação nova, que a base liga ao pedido.</returns>
    protected abstract Task<GravacaoTabela> AbrirAsyncCore(PedidoGravacao pedido, CancellationToken cancelamento);
}

/// <summary>
/// A gravação em curso de uma tabela do destino: os lotes, um por vez, e a conclusão, que confirma. O ciclo de vida é da
/// base: o lote passado pela primeira vez é da gravação, que o descarta sempre; uma chamada por vez; a gravação que falhou
/// depois de começar uma chamada (inclusive pelo cancelamento, ou pela recusa do lote de outro dialeto) está quebrada e só
/// se descarta; a concluída recusa outra chamada; o descarte é único, nunca lança e espera a chamada em curso.
/// </summary>
/// <remarks>
/// <para>
/// Antes de passar o lote ao dialeto, a base confere a forma dele: vivo e válido, as mesmas colunas do pedido na mesma
/// ordem, cada tratada convertida no lote com o mesmo tipo, e escrita ou mantida em toda linha. Nada tratado sai sem escrita:
/// um lote fora disso é recusado sem gravar nada dele. Essa recusa não quebra a gravação, nem o cancelamento pedido antes da
/// chamada, nem a recusa de uma chamada concorrente.
/// </para>
/// <para>
/// O dialeto recusa o lote de outro dialeto com <see cref="InvalidOperationException"/>. As células sem escrita (as não
/// convertidas, as mantidas e as convertidas não escritas, como a referência de <c>derivarDe</c>) vão como vieram; as
/// escritas, com o valor escrito, também numa convertida que não é tratada.
/// </para>
/// <para>
/// O descarte sem concluir desfaz a gravação, em tempo limitado: num banco que grava a tabela numa transação só (no
/// PostgreSQL, com o esvaziamento), a tabela fica como estava; nos outros, como estava, vazia ou parcial (a segurança vem do
/// diário). O núcleo responde ao cancelamento em tempo limitado e nunca descarta a própria gravação.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class GravacaoTabela : IAsyncDisposable
{
    private readonly SemaphoreSlim _chamada = new(1, 1);
    private PedidoGravacao? _pedido;
    private long _linhas;
    private int _concluida;
    private int _quebrada;
    private int _descartada;

    /// <summary>Quantas linhas os lotes gravados até agora trouxeram.</summary>
    public long Linhas => Interlocked.Read(ref _linhas);

    /// <summary>O pedido desta gravação; só existe depois da abertura (no construtor do dialeto, lança).</summary>
    /// <exception cref="InvalidOperationException">A gravação ainda não foi ligada ao pedido.</exception>
    protected PedidoGravacao Pedido => _pedido ?? throw new InvalidOperationException("A gravação não é de um gravador.");

    /// <summary>
    /// Grava um lote. O lote passa a ser da gravação, que o descarta, também quando o recusa ou falha; o lote já passado a uma
    /// gravação é recusado e fica com quem o tem.
    /// </summary>
    /// <param name="lote">O lote, lido com as colunas do pedido, na mesma ordem.</param>
    /// <param name="cancelamento">Cancela a gravação do lote; depois de começar, quebra a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="ArgumentNullException">Lote nulo.</exception>
    /// <exception cref="ObjectDisposedException">A gravação ou o lote foram descartados.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    /// <exception cref="InvalidOperationException">
    /// Outra chamada em curso, a gravação quebrada ou concluída, a gravação não é de um gravador, o lote já passado a uma
    /// gravação, o lote fora da forma do pedido (inválido, outras colunas, tratada não convertida ou de outro tipo, célula
    /// tratada sem escrita), ou o lote de outro dialeto (o que quebra a gravação).
    /// </exception>
    public async Task GravarAsync(LoteLinhas lote, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(lote);

        // O lote passado a outra chamada (desta ou de outra gravação) não é desta: recusado sem descartar, porque a outra
        // ainda o lê.
        if (!lote.Tomar())
        {
            throw new InvalidOperationException("O lote já foi passado a uma gravação.");
        }

        try
        {
            var pedido = _pedido ?? throw new InvalidOperationException("A gravação não é de um gravador.");
            await NaVezAsync(
                () => Conferir(lote, pedido),
                async () =>
                {
                    await GravarAsyncCore(lote, cancelamento).ConfigureAwait(false);
                    Interlocked.Add(ref _linhas, lote.Quantidade);
                },
                cancelamento).ConfigureAwait(false);
        }
        finally
        {
            lote.Dispose();
        }
    }

    /// <summary>Conclui a gravação: fecha, confere que a sessão seguiu intacta e confirma.</summary>
    /// <param name="cancelamento">Cancela a conclusão; depois de começar, quebra a gravação.</param>
    /// <returns>As linhas gravadas.</returns>
    /// <exception cref="ObjectDisposedException">A gravação foi descartada.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    /// <exception cref="InvalidOperationException">Outra chamada em curso, a gravação quebrada ou já concluída, ou não é de um gravador.</exception>
    public async Task<long> ConcluirAsync(CancellationToken cancelamento)
    {
        _ = _pedido ?? throw new InvalidOperationException("A gravação não é de um gravador.");
        await NaVezAsync(
            static () => { },
            async () =>
            {
                await ConcluirAsyncCore(cancelamento).ConfigureAwait(false);
                Volatile.Write(ref _concluida, 1);
            },
            cancelamento).ConfigureAwait(false);
        return Linhas;
    }

    /// <summary>
    /// Termina a gravação: sem a conclusão, desfaz. Uma vez só; espera a chamada em curso; nunca lança. Uma chamada
    /// concorrente volta sem esperar a primeira.
    /// </summary>
    /// <returns>A tarefa do descarte.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar da exceção de quem o chama.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartada, 1) == 0)
        {
            await _chamada.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await DisposeAsyncCore().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _chamada.Release();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>O número de linhas gravadas, sem nomes nem valores.</summary>
    /// <returns>O texto.</returns>
    public sealed override string ToString() => $"{nameof(GravacaoTabela)} ({Linhas} linhas)";

    /// <summary>
    /// A gravação de um lote já conferido; nunca em paralelo. Recusa o lote de outro dialeto. Responde ao cancelamento em
    /// tempo limitado e nunca descarta a própria gravação nem o lote.
    /// </summary>
    /// <param name="lote">O lote, conferido.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    protected abstract Task GravarAsyncCore(LoteLinhas lote, CancellationToken cancelamento);

    /// <summary>A conclusão: fecha, confere a sessão e confirma; nunca em paralelo.</summary>
    /// <param name="cancelamento">Cancela a conclusão.</param>
    /// <returns>A tarefa.</returns>
    protected abstract Task ConcluirAsyncCore(CancellationToken cancelamento);

    /// <summary>
    /// O descarte: sem a conclusão, desfaz; fecha a conexão; em tempo limitado. Chamado uma vez só, nunca junto com outra
    /// chamada.
    /// </summary>
    /// <returns>A tarefa.</returns>
    protected abstract ValueTask DisposeAsyncCore();

    /// <summary>Se a gravação já foi descartada.</summary>
    internal bool Descartada => Volatile.Read(ref _descartada) != 0;

    /// <summary>Liga a gravação ao pedido que a abriu.</summary>
    /// <param name="pedido">O pedido.</param>
    /// <returns>Falso quando a gravação já é de outro pedido.</returns>
    internal bool Ligar(PedidoGravacao pedido) => Interlocked.CompareExchange(ref _pedido, pedido, null) is null;

    // Nada tratado sai sem escrita: a forma do lote contra o pedido, antes de o dialeto ver o lote.
    private static void Conferir(LoteLinhas lote, PedidoGravacao pedido)
    {
        ObjectDisposedException.ThrowIf(lote.Descartado, lote);
        if (lote.Invalido)
        {
            throw new InvalidOperationException("O lote ficou inválido: o dialeto falhou ao escrever ou manter uma célula. Nada dele foi gravado.");
        }

        // Pelos nomes, na ordem: uma ordem trocada entre a leitura e a gravação trocaria os valores entre as colunas.
        var lido = lote.Pedido;
        if (!lido.Colunas.SequenceEqual(pedido.Colunas, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("As colunas do lote não são as do pedido da gravação, na mesma ordem. Nada dele foi gravado.");
        }

        foreach (var tratada in pedido.Tratadas)
        {
            if (!lido.Convertidas.Any(convertida => convertida.Indice == tratada.Indice && convertida.Tipo == tratada.Tipo))
            {
                throw new InvalidOperationException("Uma coluna tratada não foi lida no lote como convertida do mesmo tipo. Nada dele foi gravado.");
            }

            for (var linha = 0; linha < lote.Quantidade; linha++)
            {
                if (!lote.EstaEscrita(linha, tratada.Indice))
                {
                    throw new InvalidOperationException("Uma célula de coluna tratada não foi escrita nem mantida. Nada do lote foi gravado.");
                }
            }
        }
    }

    // Uma chamada por vez, na gravação viva, aberta e não concluída. A conferência que recusa antes do dialeto não a quebra
    // (nada foi gravado); a falha depois de começar, sim.
    private async Task NaVezAsync(Action conferir, Func<Task> chamada, CancellationToken cancelamento)
    {
        ObjectDisposedException.ThrowIf(Descartada, this);
        if (!_chamada.Wait(0, CancellationToken.None))
        {
            // O descarte pode ter tomado a vez: o que sai é a gravação descartada.
            ObjectDisposedException.ThrowIf(Descartada, this);
            throw new InvalidOperationException("A gravação já está numa chamada: uma chamada por vez.");
        }

        try
        {
            ObjectDisposedException.ThrowIf(Descartada, this);
            if (Volatile.Read(ref _quebrada) != 0)
            {
                throw new InvalidOperationException("A gravação falhou numa chamada anterior e só se descarta.");
            }

            if (Volatile.Read(ref _concluida) != 0)
            {
                throw new InvalidOperationException("A gravação já foi concluída.");
            }

            cancelamento.ThrowIfCancellationRequested();
            conferir();
            try
            {
                await chamada().ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _quebrada, 1);
                throw;
            }
        }
        finally
        {
            _chamada.Release();
        }
    }
}
