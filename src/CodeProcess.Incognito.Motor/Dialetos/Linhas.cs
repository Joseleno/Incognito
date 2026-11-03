using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>Uma coluna lida na forma canônica: a posição dela no pedido e o tipo.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ColunaConvertida
{
    private readonly int _indice;
    private readonly TipoColuna _tipo;

    /// <summary>A posição da coluna em <see cref="PedidoLeituraTabela.Colunas"/>, a partir de zero.</summary>
    public required int Indice
    {
        get => _indice;
        init => _indice = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(Indice), "A posição da coluna convertida não pode ser negativa.");
    }

    /// <summary>O tipo canônico em que o valor é entregue (spec, tabela da representação canônica).</summary>
    public required TipoColuna Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de coluna desconhecido.");
    }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ColunaConvertida)} ({Indice}, {Tipo})";
}

/// <summary>
/// O que ler de uma tabela: as colunas, na ordem em que saem, e as que se leem na forma canônica (as tratadas e as
/// referências de <c>derivarDe</c>); as outras passam opacas, do jeito do dialeto.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PedidoLeituraTabela
{
    private readonly NomeObjeto _tabela = null!;
    private readonly IReadOnlyList<string> _colunas = [];
    private readonly IReadOnlyList<ColunaConvertida> _convertidas = [];

    /// <summary>A tabela, como o catálogo a guarda; numa particionada, a raiz.</summary>
    public required NomeObjeto Tabela { get => _tabela; init => _tabela = value ?? throw new ArgumentNullException(nameof(Tabela)); }

    /// <summary>As colunas, como o catálogo as guarda, na ordem em que saem; ao menos uma, sem repetidas.</summary>
    public required IReadOnlyList<string> Colunas
    {
        get => _colunas;
        init
        {
            var copia = Lista.CopiarNomes(value, nameof(Colunas));
            if (copia.Count == 0)
            {
                throw new ArgumentException("A leitura de uma tabela tem ao menos uma coluna.", nameof(Colunas));
            }

            if (copia.Distinct(StringComparer.Ordinal).Count() != copia.Count)
            {
                throw new ArgumentException("A lista das colunas tem coluna repetida.", nameof(Colunas));
            }

            _colunas = copia;
        }
    }

    /// <summary>As colunas lidas na forma canônica, pela posição em <see cref="Colunas"/>; sem posição repetida.</summary>
    public IReadOnlyList<ColunaConvertida> Convertidas
    {
        get => _convertidas;
        init
        {
            var copia = Lista.Copiar(value, nameof(Convertidas));
            if (copia.Select(convertida => convertida.Indice).Distinct().Count() != copia.Count)
            {
                throw new ArgumentException("A lista das convertidas tem posição repetida.", nameof(Convertidas));
            }

            _convertidas = copia;
        }
    }

    /// <summary>O número de colunas e de convertidas, sem os nomes.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => $"{nameof(PedidoLeituraTabela)} ({Colunas.Count} colunas, {Convertidas.Count} convertidas)";

    /// <summary>Confere o que só se confere com as duas listas: toda convertida aponta uma coluna do pedido.</summary>
    /// <exception cref="ArgumentException">Convertida fora das colunas.</exception>
    internal void Conferir()
    {
        if (Convertidas.Any(convertida => convertida.Indice >= Colunas.Count))
        {
            throw new ArgumentException("Uma coluna convertida aponta uma posição fora das colunas do pedido.", "pedido");
        }
    }
}

/// <summary>
/// A leitura em curso das linhas de uma tabela, num leitor da sessão: lotes, um por vez, até o fim. O ciclo de vida é da
/// base: o leitor fica ocupado até o descarte da leitura; uma chamada por vez; a sessão descartada recusa; o descarte é
/// único, nunca lança e libera o leitor.
/// </summary>
/// <remarks>
/// <para>
/// No fim (o lote nulo), o dialeto já conferiu que a sessão seguiu intacta durante a leitura (com a consistência por tabela,
/// o instante é o da tabela). O descarte no meio interrompe a leitura e deixa o leitor no instante da sessão, pronto para
/// outra leitura; ele espera a chamada em curso terminar. Por isso o núcleo responde ao cancelamento em tempo limitado (o
/// dialeto desiste da conexão quando o servidor não responde) e nunca descarta a própria leitura.
/// </para>
/// <para>
/// Uma leitura cuja chamada lançou depois de começar (inclusive pelo cancelamento) está quebrada: linhas podem ter sido
/// consumidas, e toda chamada seguinte é recusada; só resta descartá-la. O cancelamento já pedido antes da chamada não a
/// quebra. O lote entregue é independente da leitura: vale depois da chamada seguinte e dos descartes da leitura e da
/// sessão.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeituraTabela : IAsyncDisposable
{
    private readonly SemaphoreSlim _chamada = new(1, 1);
    private LeitorDaSessao? _leitor;
    private SessaoLeitura? _sessao;
    private PedidoLeituraTabela? _pedido;
    private long _linhas;
    private int _terminada;
    private int _quebrada;
    private int _descartada;

    /// <summary>Quantas linhas os lotes devolvidos até agora trouxeram.</summary>
    public long Linhas => Interlocked.Read(ref _linhas);

    /// <summary>O pedido desta leitura; só existe depois da abertura (no construtor do dialeto, lança).</summary>
    /// <exception cref="InvalidOperationException">A leitura ainda não foi ligada ao leitor.</exception>
    protected PedidoLeituraTabela Pedido => _pedido ?? throw new InvalidOperationException("A leitura não é de um leitor da sessão.");

    /// <summary>O próximo lote, ou nulo no fim (e de novo nulo depois dele).</summary>
    /// <param name="cancelamento">Cancela a leitura do lote; depois de começar, quebra a leitura.</param>
    /// <returns>O lote, que quem chama passa adiante ou descarta; nulo no fim.</returns>
    /// <exception cref="ObjectDisposedException">A leitura ou a sessão foram descartadas.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    /// <exception cref="InvalidOperationException">
    /// Outra chamada em curso, a leitura quebrada por uma chamada anterior, a leitura não é de um leitor, ou o dialeto
    /// devolveu um lote de outra leitura ou já entregue.
    /// </exception>
    public async Task<LoteLinhas?> LerLoteAsync(CancellationToken cancelamento)
    {
        var sessao = _sessao ?? throw new InvalidOperationException("A leitura não é de um leitor da sessão.");
        ObjectDisposedException.ThrowIf(Descartada || sessao.Descartada, this);
        if (!_chamada.Wait(0, CancellationToken.None))
        {
            // O descarte pode ter tomado a vez: o que sai é a leitura descartada.
            ObjectDisposedException.ThrowIf(Descartada, this);
            throw new InvalidOperationException("A leitura já está numa chamada: os lotes são lidos um por vez.");
        }

        try
        {
            ObjectDisposedException.ThrowIf(Descartada, this);
            if (Volatile.Read(ref _quebrada) != 0)
            {
                throw new InvalidOperationException("A leitura falhou numa chamada anterior: linhas podem ter sido consumidas, e ela só se descarta.");
            }

            cancelamento.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _terminada) != 0)
            {
                return null;
            }

            LoteLinhas? lote;
            try
            {
                lote = await LerLoteAsyncCore(cancelamento).ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _quebrada, 1);
                throw;
            }

            if (lote is null)
            {
                Volatile.Write(ref _terminada, 1);
                return null;
            }

            if (!ReferenceEquals(lote.Pedido, _pedido) || !lote.Numerar(Linhas + 1))
            {
                // O lote já entregue é de quem o segura: só o que nunca saiu é descartado aqui.
                Volatile.Write(ref _quebrada, 1);
                if (lote.PrimeiraLinha == 0)
                {
                    lote.Dispose();
                }

                throw new InvalidOperationException("O dialeto devolveu um lote que não é desta leitura, ou que já foi entregue.");
            }

            Interlocked.Add(ref _linhas, lote.Quantidade);
            return lote;
        }
        finally
        {
            _chamada.Release();
        }
    }

    /// <summary>
    /// Termina a leitura: no meio, interrompe e volta o leitor ao instante da sessão. Uma vez só; espera a chamada em curso;
    /// nunca lança; libera o leitor. Uma chamada concorrente volta sem esperar a primeira, antes de o leitor estar livre.
    /// </summary>
    /// <returns>A tarefa do descarte.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar da exceção de quem o chama; o leitor que falhou aqui falha na próxima leitura.")]
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
                _leitor?.LiberarDaLeitura();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>O número de linhas lidas, sem valores.</summary>
    /// <returns>O texto.</returns>
    public sealed override string ToString() => $"{nameof(LeituraTabela)} ({Linhas} linhas)";

    /// <summary>
    /// A leitura do próximo lote; nunca em paralelo. Nulo no fim, depois de conferir a sessão. Responde ao cancelamento em
    /// tempo limitado e nunca descarta a própria leitura.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>Um lote novo, criado com o <see cref="Pedido"/> desta leitura, ou nulo.</returns>
    protected abstract Task<LoteLinhas?> LerLoteAsyncCore(CancellationToken cancelamento);

    /// <summary>
    /// O descarte: no meio, interrompe e volta o leitor ao instante da sessão, em tempo limitado; chamado uma vez só, nunca
    /// junto com uma leitura do lote.
    /// </summary>
    /// <returns>A tarefa.</returns>
    protected abstract ValueTask DisposeAsyncCore();

    /// <summary>Se a leitura já foi descartada.</summary>
    internal bool Descartada => Volatile.Read(ref _descartada) != 0;

    /// <summary>Liga a leitura ao leitor que a abriu.</summary>
    /// <param name="leitor">O leitor.</param>
    /// <param name="sessao">A sessão dele.</param>
    /// <param name="pedido">O pedido.</param>
    /// <returns>Falso quando a leitura já é de outro leitor.</returns>
    internal bool Ligar(LeitorDaSessao leitor, SessaoLeitura sessao, PedidoLeituraTabela pedido)
    {
        if (Interlocked.CompareExchange(ref _leitor, leitor, null) is not null)
        {
            return false;
        }

        _sessao = sessao;
        _pedido = pedido;
        return true;
    }
}

/// <summary>
/// Um lote de linhas lidas, no formato do dialeto: só um gravador do mesmo dialeto o grava. O motor lê na forma canônica as
/// colunas convertidas do pedido e escreve nelas o valor tratado (ou as mantém como vieram); as outras colunas passam como
/// vieram. As regras são da base: o valor lido e o escrito estão na forma canônica exata do tipo; uma célula se escreve uma
/// vez, e depois de escrita não se lê (o dialeto pode apagar o original); o lote descartado recusa uso.
/// </summary>
/// <remarks>
/// <para>
/// O lote é independente da leitura que o produziu: vale depois da leitura seguinte e dos descartes da leitura e da sessão,
/// até o próprio descarte. É usado por uma thread de cada vez, inclusive no descarte. O dialeto guarda as linhas como quiser
/// (no PostgreSQL, os bytes do <c>COPY</c>) e limpa o que guardou no descarte. Nenhuma exceção, mensagem ou <c>ToString</c>
/// traz valor.
/// </para>
/// <para>
/// O motor lê todas as células de uma linha de que precisa antes de escrever qualquer uma delas. Se o dialeto falhar ao
/// escrever ou manter uma célula, o lote fica inválido e recusa todo uso, menos o descarte.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LoteLinhas : IDisposable
{
    private readonly PedidoLeituraTabela _pedido;
    private readonly int[] _convertida;
    private readonly bool[] _escritas;
    private long _primeira;
    private int _invalido;
    private int _descartado;

    /// <summary>Fixa o pedido e o número de linhas do lote.</summary>
    /// <param name="pedido">O pedido da leitura que produziu o lote.</param>
    /// <param name="quantidade">Quantas linhas, ao menos uma.</param>
    /// <exception cref="ArgumentNullException">Pedido nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Lote sem linha.</exception>
    /// <exception cref="ArgumentException">Convertida fora das colunas do pedido.</exception>
    /// <exception cref="OverflowException">Linhas e convertidas demais para um lote.</exception>
    protected LoteLinhas(PedidoLeituraTabela pedido, int quantidade)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantidade, 1);
        pedido.Conferir();
        _pedido = pedido;
        Quantidade = quantidade;
        _convertida = new int[pedido.Colunas.Count];
        Array.Fill(_convertida, -1);
        for (var i = 0; i < pedido.Convertidas.Count; i++)
        {
            _convertida[pedido.Convertidas[i].Indice] = i;
        }

        _escritas = new bool[checked(quantidade * pedido.Convertidas.Count)];
    }

    /// <summary>Quantas linhas o lote tem.</summary>
    public int Quantidade { get; }

    /// <summary>O número, na leitura (a partir de 1), da primeira linha do lote; zero antes de a leitura entregá-lo.</summary>
    public long PrimeiraLinha => Interlocked.Read(ref _primeira);

    /// <summary>O pedido da leitura que produziu o lote.</summary>
    internal PedidoLeituraTabela Pedido => _pedido;

    /// <summary>Se o lote já foi descartado.</summary>
    internal bool Descartado => Volatile.Read(ref _descartado) != 0;

    /// <summary>Lê, na forma canônica, o valor original de uma coluna convertida.</summary>
    /// <param name="linha">A linha no lote, a partir de zero.</param>
    /// <param name="coluna">A posição da coluna no pedido; precisa ser convertida.</param>
    /// <param name="valor">
    /// O valor, na forma canônica do tipo (um objeto novo a cada chamada; o <c>decimal</c> com a escala do banco); nulo para
    /// NULL.
    /// </param>
    /// <returns>
    /// Falso quando a coluna tem um valor sem representação na forma canônica (o valor sai nulo), inclusive o texto com NUL ou
    /// surrogate isolado.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Lote descartado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Linha ou coluna fora do lote, ou coluna não convertida.</exception>
    /// <exception cref="InvalidOperationException">
    /// A célula já foi escrita ou mantida, o lote ficou inválido, ou o dialeto devolveu um valor de outro tipo.
    /// </exception>
    public bool TentarLer(int linha, int coluna, out object? valor)
    {
        var celula = Celula(linha, coluna, out var tipo);
        if (_escritas[celula])
        {
            throw new InvalidOperationException("A célula já foi escrita: o valor original não se lê depois da escrita.");
        }

        if (!TentarLerCore(linha, coluna, out var lido) || lido is string { } texto && !FormaCanonica.TextoBemFormado(texto))
        {
            valor = null;
            return false;
        }

        if (lido is not null && !FormaCanonica.Aceita(tipo, lido))
        {
            throw new InvalidOperationException($"O dialeto devolveu um valor fora da forma canônica do tipo {tipo}.");
        }

        valor = lido;
        return true;
    }

    /// <summary>Escreve o valor tratado numa coluna convertida, uma vez por célula; o <c>byte[]</c> é copiado.</summary>
    /// <param name="linha">A linha no lote, a partir de zero.</param>
    /// <param name="coluna">A posição da coluna no pedido; precisa ser convertida.</param>
    /// <param name="valor">O valor, na forma canônica exata do tipo da coluna; nulo para NULL.</param>
    /// <exception cref="ObjectDisposedException">Lote descartado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Linha ou coluna fora do lote, ou coluna não convertida.</exception>
    /// <exception cref="ArgumentException">Valor fora da forma canônica do tipo da coluna.</exception>
    /// <exception cref="InvalidOperationException">A célula já foi escrita ou mantida, ou o lote ficou inválido.</exception>
    public void Escrever(int linha, int coluna, object? valor)
    {
        var celula = Celula(linha, coluna, out var tipo);
        if (valor is not null && !FormaCanonica.Aceita(tipo, valor))
        {
            throw new ArgumentException($"O valor não está na forma canônica do tipo {tipo}.", nameof(valor));
        }

        RecusarSeEscrita(celula);
        try
        {
            EscreverCore(linha, coluna, valor is byte[] bytes ? bytes.ToArray() : valor);
        }
        catch
        {
            Volatile.Write(ref _invalido, 1);
            throw;
        }

        _escritas[celula] = true;
    }

    /// <summary>Mantém o valor original de uma coluna convertida, como veio, uma vez por célula.</summary>
    /// <param name="linha">A linha no lote, a partir de zero.</param>
    /// <param name="coluna">A posição da coluna no pedido; precisa ser convertida.</param>
    /// <exception cref="ObjectDisposedException">Lote descartado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Linha ou coluna fora do lote, ou coluna não convertida.</exception>
    /// <exception cref="InvalidOperationException">A célula já foi escrita ou mantida, ou o lote ficou inválido.</exception>
    public void Manter(int linha, int coluna)
    {
        var celula = Celula(linha, coluna, out _);
        RecusarSeEscrita(celula);
        try
        {
            ManterCore(linha, coluna);
        }
        catch
        {
            Volatile.Write(ref _invalido, 1);
            throw;
        }

        _escritas[celula] = true;
    }

    /// <summary>Se a célula de uma coluna convertida já foi escrita ou mantida.</summary>
    /// <param name="linha">A linha no lote, a partir de zero.</param>
    /// <param name="coluna">A posição da coluna no pedido; precisa ser convertida.</param>
    /// <returns>Se foi escrita ou mantida.</returns>
    /// <exception cref="ObjectDisposedException">Lote descartado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Linha ou coluna fora do lote, ou coluna não convertida.</exception>
    /// <exception cref="InvalidOperationException">O lote ficou inválido.</exception>
    public bool EstaEscrita(int linha, int coluna) => _escritas[Celula(linha, coluna, out _)];

    /// <summary>Descarta o lote e o que o dialeto guardou dele, uma vez só; nunca lança.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar da exceção de quem o chama.")]
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _descartado, 1) == 0)
        {
            try
            {
                DisposeCore();
            }
            catch
            {
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>O número de linhas, as colunas e as convertidas, sem valores.</summary>
    /// <returns>O texto.</returns>
    public sealed override string ToString() => $"{nameof(LoteLinhas)} ({Quantidade} linhas, {_pedido.Colunas.Count} colunas, {_pedido.Convertidas.Count} convertidas)";

    /// <summary>
    /// A leitura do valor original, na forma canônica; nunca lança por causa do valor. Devolve sempre um objeto novo (um
    /// <c>byte[]</c> que o dialeto não guarda, porque a base não o copia).
    /// </summary>
    /// <param name="linha">A linha, conferida.</param>
    /// <param name="coluna">A coluna, conferida e convertida.</param>
    /// <param name="valor">O valor, ou nulo para NULL.</param>
    /// <returns>Falso quando o valor não tem representação na forma canônica.</returns>
    protected abstract bool TentarLerCore(int linha, int coluna, out object? valor);

    /// <summary>A escrita do valor tratado (o dialeto pode apagar o original da célula).</summary>
    /// <param name="linha">A linha, conferida.</param>
    /// <param name="coluna">A coluna, conferida e convertida.</param>
    /// <param name="valor">O valor, conferido e já copiado, ou nulo.</param>
    protected abstract void EscreverCore(int linha, int coluna, object? valor);

    /// <summary>A manutenção do valor original da célula.</summary>
    /// <param name="linha">A linha, conferida.</param>
    /// <param name="coluna">A coluna, conferida e convertida.</param>
    protected abstract void ManterCore(int linha, int coluna);

    /// <summary>O descarte do que o dialeto guardou (limpando os valores); chamado uma vez só.</summary>
    protected abstract void DisposeCore();

    /// <summary>Numera o lote quando a leitura o entrega, uma vez só.</summary>
    /// <param name="primeira">O número da primeira linha.</param>
    /// <returns>Falso quando o lote já foi numerado ou descartado.</returns>
    internal bool Numerar(long primeira) => !Descartado && Interlocked.CompareExchange(ref _primeira, primeira, 0) == 0;

    // A célula de uma linha e de uma coluna convertida, conferidas.
    private int Celula(int linha, int coluna, out TipoColuna tipo)
    {
        ObjectDisposedException.ThrowIf(Descartado, this);
        if (Volatile.Read(ref _invalido) != 0)
        {
            throw new InvalidOperationException("O lote ficou inválido: o dialeto falhou ao escrever ou manter uma célula.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(linha);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(linha, Quantidade);
        ArgumentOutOfRangeException.ThrowIfNegative(coluna);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(coluna, _convertida.Length);
        var convertida = _convertida[coluna];
        if (convertida < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coluna), "A coluna não é convertida: passa como veio.");
        }

        tipo = _pedido.Convertidas[convertida].Tipo;
        return (linha * _pedido.Convertidas.Count) + convertida;
    }

    private void RecusarSeEscrita(int celula)
    {
        if (_escritas[celula])
        {
            throw new InvalidOperationException("A célula já foi escrita ou mantida.");
        }
    }
}

/// <summary>A forma canônica de cada <see cref="TipoColuna"/> (spec, tabela da representação canônica).</summary>
internal static class FormaCanonica
{
    /// <summary>O tipo .NET do valor de uma coluna do tipo.</summary>
    /// <param name="tipo">O tipo da coluna.</param>
    /// <returns>O tipo .NET.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Tipo desconhecido.</exception>
    internal static Type Tipo(TipoColuna tipo) => tipo switch
    {
        TipoColuna.Texto => typeof(string),
        TipoColuna.Inteiro => typeof(long),
        TipoColuna.Decimal => typeof(decimal),
        TipoColuna.Data => typeof(DateOnly),
        TipoColuna.DataHora => typeof(DateTime),
        TipoColuna.DataHoraComFuso => typeof(DateTimeOffset),
        TipoColuna.Binario => typeof(byte[]),
        TipoColuna.Booleano => typeof(bool),
        TipoColuna.Uuid => typeof(Guid),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de coluna desconhecido."),
    };

    /// <summary>
    /// Se o valor está na forma canônica exata do tipo: o tipo .NET exato, a data e hora sem fuso (<see cref="DateTimeKind.Unspecified"/>),
    /// a com fuso no deslocamento zero, e o texto sem NUL nem surrogate isolado.
    /// </summary>
    /// <param name="tipo">O tipo da coluna.</param>
    /// <param name="valor">O valor, não nulo.</param>
    /// <returns>Se está na forma.</returns>
    internal static bool Aceita(TipoColuna tipo, object valor) =>
        valor.GetType() == Tipo(tipo) && valor switch
        {
            DateTime data => data.Kind == DateTimeKind.Unspecified,
            DateTimeOffset data => data.Offset == TimeSpan.Zero,
            string texto => TextoBemFormado(texto),
            _ => true,
        };

    /// <summary>Se o texto não tem NUL nem surrogate isolado; sem exceção (uma por valor seria cara e levaria o valor).</summary>
    /// <param name="texto">O texto.</param>
    /// <returns>Se está bem formado.</returns>
    internal static bool TextoBemFormado(string texto)
    {
        var resto = texto.AsSpan();
        while (!resto.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(resto, out var caractere, out var lidos) != OperationStatus.Done || caractere.Value == 0)
            {
                return false;
            }

            resto = resto[lidos..];
        }

        return true;
    }
}
