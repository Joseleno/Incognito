using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// O que cada dialeto fornece à suíte: um par de bancos por teste, os dois com o esquema de conformidade, e a declaração
/// do que a suíte deve ler dele. Uma por classe de teste (<c>IClassFixture</c>); o servidor (contêiner) fica numa
/// fixture de assembly do projeto do dialeto.
/// </summary>
/// <remarks>
/// A origem é aberta com um usuário só de leitura e o destino com o dono do banco (dono também dos objetos do esquema,
/// fora os de um terceiro papel), nunca com o administrador do servidor, que passaria em tudo sem provar segurança por
/// linha, máscara nem dono.
/// </remarks>
public abstract class FixtureDialeto
{
    /// <summary>O que o esquema de conformidade deste dialeto e versão tem.</summary>
    public abstract EsquemaDeConformidade Esperado { get; }

    /// <summary>
    /// Cria um par de bancos novo (origem e destino com o esquema de conformidade; a origem lida por um usuário só de
    /// leitura, o destino pelo dono) e os conecta. Nomes únicos, que nunca vêm do nome do teste.
    /// </summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O par, que o teste descarta.</returns>
    public abstract ValueTask<ParDeBancos> CriarParAsync(CancellationToken cancelamento);

    /// <summary>
    /// Cria um par como o de <see cref="CriarParAsync"/>, mas lido nos dois lados pelo administrador do servidor, que vê
    /// canais do catálogo que o leitor e o dono não veem. Só o fato do sentinela o usa.
    /// </summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O par, que o teste descarta.</returns>
    public abstract ValueTask<ParDeBancos> CriarParPrivilegiadoAsync(CancellationToken cancelamento);

    /// <summary>O dialeto, para a suíte conectar com uma string que ele recusa.</summary>
    public abstract ProvedorDialeto Provedor { get; }

    /// <summary>
    /// Uma string de conexão que o dialeto recusa ao conectar, nos dois lados, com <see cref="DialetoRecusouException"/>
    /// (uma opção que o Incognito não aceita, por exemplo), com o sentinela dentro: a suíte confere que a recusa não o traz.
    /// </summary>
    /// <param name="sentinela">O texto que a recusa nunca pode trazer.</param>
    /// <returns>A string de conexão.</returns>
    public abstract SegredoConexao ConexaoRecusada(string sentinela);

    /// <summary>
    /// Conecta outra instância do destino do par, com o mesmo login: a exclusividade e a persistência do bloqueio valem
    /// entre instâncias, nunca só dentro de uma.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>O destino conectado, que o teste descarta.</returns>
    public abstract Task<DialetoDestino> ConectarOutroDestinoAsync(ParDeBancos par, CancellationToken cancelamento);

    /// <summary>
    /// Grava no lugar do marcador de um banco do par, como administrador e sem passar pelo dialeto: um comentário alheio ou
    /// um inválido (o prefixo do marcador do dialeto) com o sentinela, ou um marcador válido com o identificador e a data.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="destino">Se é o banco do destino; senão, o da origem.</param>
    /// <param name="comentario">O que gravar.</param>
    /// <param name="sentinela">O texto que nenhuma saída pode trazer (no alheio e no inválido).</param>
    /// <param name="identificador">O identificador do marcador válido.</param>
    /// <param name="gravadoEm">A data do marcador válido, em UTC e sem fração de segundo.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task ComentarAsync(
        ParDeBancos par, bool destino, ComentarioDoBanco comentario, string sentinela, Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento);

    /// <summary>
    /// Derruba, como administrador, a conexão do bloqueio de execução do destino do par: a queda que a concessão precisa
    /// perceber.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task DerrubarBloqueioAsync(ParDeBancos par, CancellationToken cancelamento);
}

/// <summary>Os dois bancos de um teste, conectados.</summary>
public sealed class ParDeBancos : IAsyncDisposable
{
    private readonly Func<ValueTask> _aoDescartar;
    private int _descartado;

    /// <summary>Monta o par.</summary>
    /// <param name="origem">A origem conectada.</param>
    /// <param name="destino">O destino conectado.</param>
    /// <param name="bancoOrigem">O nome do banco de origem.</param>
    /// <param name="bancoDestino">O nome do banco de destino.</param>
    /// <param name="aoDescartar">O que a fixture faz no fim (apagar os bancos), depois de descartar os dialetos.</param>
    public ParDeBancos(DialetoOrigem origem, DialetoDestino destino, string bancoOrigem, string bancoDestino, Func<ValueTask> aoDescartar)
    {
        Origem = origem ?? throw new ArgumentNullException(nameof(origem));
        Destino = destino ?? throw new ArgumentNullException(nameof(destino));
        ArgumentException.ThrowIfNullOrEmpty(bancoOrigem);
        ArgumentException.ThrowIfNullOrEmpty(bancoDestino);
        BancoOrigem = bancoOrigem;
        BancoDestino = bancoDestino;
        _aoDescartar = aoDescartar ?? throw new ArgumentNullException(nameof(aoDescartar));
    }

    /// <summary>A origem.</summary>
    public DialetoOrigem Origem { get; }

    /// <summary>O destino.</summary>
    public DialetoDestino Destino { get; }

    /// <summary>O nome do banco de origem.</summary>
    public string BancoOrigem { get; }

    /// <summary>O nome do banco de destino.</summary>
    public string BancoDestino { get; }

    /// <summary>Descarta os dialetos e apaga os bancos; idempotente.</summary>
    /// <returns>A tarefa do descarte.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartado, 1) != 0)
        {
            return;
        }

        // Um descarte que falhe não impede os outros: o banco com conexão aberta não seria apagado.
        try
        {
            await Origem.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Destino.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _aoDescartar().ConfigureAwait(false);
            }
        }
    }
}
