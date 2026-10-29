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

    /// <summary>
    /// Como administrador, sem passar pelo dialeto e confirmado na hora: aumenta o maior valor da coluna comum
    /// (<see cref="MaioresValoresEsperados.Comum"/>) num banco do par.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="destino">Se é o banco do destino; senão, o da origem.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>O novo maior valor.</returns>
    public abstract Task<long> AumentarMaiorValorAsync(ParDeBancos par, bool destino, CancellationToken cancelamento);

    /// <summary>Como administrador, confirmado na hora: cria uma tabela nova na origem do par.</summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>O nome da tabela criada, como o catálogo o guarda.</returns>
    public abstract Task<NomeObjeto> CriarTabelaAsync(ParDeBancos par, CancellationToken cancelamento);

    /// <summary>
    /// Como administrador: tenta esvaziar uma tabela da origem do par por um meio que não respeita o instante da leitura (no
    /// PostgreSQL, <c>TRUNCATE</c>), desistindo em cerca de um segundo se ela estiver presa.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="tabela">A tabela.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>Se esvaziou.</returns>
    public abstract Task<bool> TentarEsvaziarAsync(ParDeBancos par, NomeObjeto tabela, CancellationToken cancelamento);

    /// <summary>
    /// Como administrador, confirmado na hora: anexa à particionada da origem do par
    /// (<see cref="MaioresValoresEsperados.Particionada"/>) uma partição nova com uma linha de valor maior que o declarado.
    /// </summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>O valor da linha da partição anexada.</returns>
    public abstract Task<long> AnexarParticaoAsync(ParDeBancos par, CancellationToken cancelamento);

    /// <summary>Quanto esperar, no máximo, que as conexões fechadas saiam do servidor (a saída é assíncrona). Padrão: 10 s.</summary>
    public virtual TimeSpan PrazoDasConexoes => TimeSpan.FromSeconds(10);

    /// <summary>Quantas conexões das sessões de leitura estão abertas num banco do par, lido como administrador.</summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="destino">Se é o banco do destino; senão, o da origem.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O número de conexões.</returns>
    public abstract Task<int> ContarConexoesDeLeituraAsync(ParDeBancos par, bool destino, CancellationToken cancelamento);

    /// <summary>Derruba, como administrador, as conexões das sessões de leitura num banco do par.</summary>
    /// <param name="par">O par do teste.</param>
    /// <param name="destino">Se é o banco do destino; senão, o da origem.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task DerrubarSessoesAsync(ParDeBancos par, bool destino, CancellationToken cancelamento);

    /// <summary>
    /// Cria um banco do diário novo (os casos de <see cref="CasosDoDiario"/>, sem o esquema do diário), de outro dono, com os
    /// objetos e o direito de criar esquema dados ao login do destino, e o conecta com esse login. Nome único.
    /// </summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O banco, que o teste descarta.</returns>
    public abstract ValueTask<BancoDoDiario> CriarBancoDoDiarioAsync(CancellationToken cancelamento);

    /// <summary>Conecta outra instância do destino ao banco do diário, com o mesmo login.</summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>O destino conectado, que o teste descarta.</returns>
    public abstract Task<DialetoDestino> ConectarOutroAsync(BancoDoDiario banco, CancellationToken cancelamento);

    /// <summary>
    /// O estado de uma chave estrangeira, lido como administrador e sem passar pelo dialeto: um texto opaco que muda quando a
    /// definição, a validação ou a imposição mudam.
    /// </summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="tabela">A tabela da chave.</param>
    /// <param name="chave">O nome.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O estado; nulo quando a chave não existe.</returns>
    public abstract Task<string?> LerChaveAsync(BancoDoDiario banco, NomeObjeto tabela, string chave, CancellationToken cancelamento);

    /// <summary>
    /// O estado de todas as triggers de usuário de uma tabela e das partições dela, lido como administrador: um texto opaco
    /// que muda quando o estado de qualquer uma muda.
    /// </summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="tabela">A tabela (numa particionada, a raiz).</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O estado.</returns>
    public abstract Task<string> LerTriggersAsync(BancoDoDiario banco, NomeObjeto tabela, CancellationToken cancelamento);

    /// <summary>
    /// Cria, como administrador e sem a conferência da chave, uma linha órfã em <see cref="CasosDoDiario.ComOrfao"/>, com o
    /// sentinela no valor da chave.
    /// </summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="sentinela">O texto que nenhuma saída pode trazer.</param>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task CriarOrfaoAsync(BancoDoDiario banco, string sentinela, CancellationToken cancelamento);

    /// <summary>Segura a tabela em outra sessão, sem deixar ninguém mais tocá-la, até o descarte.</summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="tabela">A tabela.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>O que solta a tabela.</returns>
    public abstract Task<IAsyncDisposable> SegurarAsync(BancoDoDiario banco, NomeObjeto tabela, CancellationToken cancelamento);

    /// <summary>
    /// Faz falhar, até o descarte, toda alteração de tabela no banco depois de feita e antes do fim da transação (no
    /// PostgreSQL, um gatilho de evento no fim do comando): a prova de que o registro e a alteração andam juntos.
    /// </summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>O que tira a falha.</returns>
    public abstract Task<IAsyncDisposable> FalharNoFimDoDdlAsync(BancoDoDiario banco, CancellationToken cancelamento);

    /// <summary>Cria, como administrador, uma chave com o nome dado e outra definição (a mesma tabela, outras colunas ou ações).</summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="tabela">A tabela.</param>
    /// <param name="chave">O nome.</param>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task RecriarComOutraDefinicaoAsync(BancoDoDiario banco, NomeObjeto tabela, string chave, CancellationToken cancelamento);

    /// <summary>Adultera o diário criado, como administrador e sem passar pelo dialeto.</summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="adulteracao">O que fazer.</param>
    /// <param name="sentinela">O texto que nenhuma saída pode trazer.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task AdulterarAsync(BancoDoDiario banco, Adulteracao adulteracao, string sentinela, CancellationToken cancelamento);

    /// <summary>Derruba, como administrador, a conexão do bloqueio de execução no banco do diário.</summary>
    /// <param name="banco">O banco do diário.</param>
    /// <param name="cancelamento">Cancela a operação.</param>
    /// <returns>A tarefa.</returns>
    public abstract Task DerrubarBloqueioAsync(BancoDoDiario banco, CancellationToken cancelamento);
}

/// <summary>O que a fixture faz com o diário criado, para a suíte conferir que o dialeto o recusa sem executar nada.</summary>
public enum Adulteracao
{
    /// <summary>A definição guardada de uma chave removida ganha outra ação do <c>ALTER TABLE</c> depois dela, com o sentinela.</summary>
    DefinicaoComOutraAcao,

    /// <summary>Outro papel ganha o direito de mudar uma coluna do diário.</summary>
    EscritaDeOutroPapel,

    /// <summary>Uma tabela do diário ganha um gatilho, que rodaria código de outro papel a cada gravação.</summary>
    GatilhoNoDiario,

    /// <summary>Uma execução registrada passa a dizer uma versão do diário mais nova que a do dialeto.</summary>
    VersaoMaisNova,
}

/// <summary>Um banco do diário conectado com o login do destino.</summary>
public sealed class BancoDoDiario : IAsyncDisposable
{
    private readonly Func<ValueTask> _aoDescartar;
    private int _descartado;

    /// <summary>Monta o banco.</summary>
    /// <param name="destino">O destino conectado.</param>
    /// <param name="banco">O nome do banco.</param>
    /// <param name="aoDescartar">O que a fixture faz no fim (apagar o banco), depois de descartar o dialeto.</param>
    public BancoDoDiario(DialetoDestino destino, string banco, Func<ValueTask> aoDescartar)
    {
        Destino = destino ?? throw new ArgumentNullException(nameof(destino));
        ArgumentException.ThrowIfNullOrEmpty(banco);
        Banco = banco;
        _aoDescartar = aoDescartar ?? throw new ArgumentNullException(nameof(aoDescartar));
    }

    /// <summary>O destino.</summary>
    public DialetoDestino Destino { get; }

    /// <summary>O nome do banco.</summary>
    public string Banco { get; }

    /// <summary>Descarta o dialeto e apaga o banco; idempotente.</summary>
    /// <returns>A tarefa do descarte.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartado, 1) != 0)
        {
            return;
        }

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
