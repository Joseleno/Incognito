using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>A consistência que a configuração pede à leitura (<c>politica.consistencia</c>).</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum ModoConsistencia
{
    /// <summary>O instante único quando o banco oferece; senão, a leitura por tabela (padrão).</summary>
    MelhorEsforco = 0,

    /// <summary>Só o instante único: o banco que não o oferece recusa a sessão.</summary>
    Exigir = 1,
}

/// <summary>A consistência que a sessão de leitura obteve.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum ConsistenciaObtida
{
    /// <summary>Não informada: o motor recusa a sessão.</summary>
    Indefinida = 0,

    /// <summary>Todas as leituras da sessão veem o mesmo instante do banco.</summary>
    Instantaneo = 1,

    /// <summary>Cada tabela é lida no seu instante (o SQL Server sem o isolamento por snapshot).</summary>
    PorTabela = 2,
}

/// <summary>O que a sessão de leitura precisa: quantos leitores, a consistência pedida e as tabelas protegidas.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PedidoLeitura
{
    /// <summary>O maior número de leitores de uma sessão (o paralelismo máximo da configuração).</summary>
    public const int MaximoDeLeitores = 64;

    private readonly int _leitores;
    private readonly ModoConsistencia _modo;
    private readonly IReadOnlyList<NomeObjeto> _travadas = [];

    /// <summary>Quantos leitores, cada um numa conexão: de 1 a <see cref="MaximoDeLeitores"/>.</summary>
    public required int Leitores
    {
        get => _leitores;
        init => _leitores = value is >= 1 and <= MaximoDeLeitores
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Leitores), $"Uma sessão de leitura tem de 1 a {MaximoDeLeitores} leitores.");
    }

    /// <summary>A consistência pedida.</summary>
    public required ModoConsistencia Modo
    {
        get => _modo;
        init => _modo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Modo), "Modo de consistência desconhecido.");
    }

    /// <summary>
    /// As tabelas protegidas, da abertura ao descarte, contra o que não respeita o instante (esvaziar, reescrever, apagar),
    /// como o catálogo as guarda; sem repetidas. Só na origem: no destino, vazia.
    /// </summary>
    public IReadOnlyList<NomeObjeto> Travadas
    {
        get => _travadas;
        init
        {
            var copia = Lista.Copiar(value, nameof(Travadas));
            if (copia.Distinct().Count() != copia.Count)
            {
                throw new ArgumentException("A lista das tabelas travadas tem tabela repetida.", nameof(Travadas));
            }

            _travadas = copia;
        }
    }

    /// <summary>O número de leitores, o modo e quantas tabelas travadas, sem os nomes.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => $"{nameof(PedidoLeitura)} ({Leitores} leitores, {Modo}, {Travadas.Count} travadas)";
}

/// <summary>
/// Uma sessão de leitura consistente: o coordenador (que segura o instante e as travas, relê o esquema e mede a idade) e
/// os leitores, cada um numa conexão própria. O ciclo de vida é da base: o descarte fecha tudo, uma vez só, e nunca lança;
/// a sessão descartada recusa uso, também pelos leitores.
/// </summary>
/// <remarks>
/// <para>
/// Regras do contrato. Na suíte de conformidade: todas as leituras de dados e as consultas do catálogo que seguem o
/// instante veem o mesmo instante (nada confirmado depois da abertura aparece, em nenhum leitor nem no esquema relido),
/// quando a consistência obtida é <see cref="ConsistenciaObtida.Instantaneo"/>; uma tabela travada nunca é lida vazia em
/// silêncio (o banco impede o esvaziamento, ou a leitura seguinte falha); a segurança por linha nunca filtra uma leitura
/// em silêncio; um leitor segue utilizável, no mesmo instante, depois de uma leitura que falhou; o descarte, e o descarte
/// do dialeto com a sessão viva, fecham as conexões da sessão. Na base, provadas pelo teste do contrato: o descarte único
/// que nunca lança, a recusa de uso depois do descarte, os leitores fixos e distintos, o mesmo leitor nunca em paralelo
/// consigo mesmo e o coordenador uma chamada por vez. No teste de cada dialeto: nenhuma conexão da sessão escreve.
/// </para>
/// <para>
/// Fora da garantia do instante: as permissões do login (<see cref="EsquemaBanco.Acesso"/>) e o que o servidor resolve
/// pelo catálogo atual, como os nomes dos tipos.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class SessaoLeitura : IAsyncDisposable
{
    private readonly IReadOnlyList<LeitorDaSessao> _leitores;
    private int _descartada;
    private int _coordenadorOcupado;

    /// <summary>Fixa os leitores da sessão, na ordem.</summary>
    /// <param name="leitores">Os leitores, distintos, nenhum de outra sessão.</param>
    /// <exception cref="ArgumentNullException">Lista nula ou com leitor nulo.</exception>
    /// <exception cref="ArgumentException">Lista vazia, leitor repetido ou já de outra sessão.</exception>
    protected SessaoLeitura(IReadOnlyList<LeitorDaSessao> leitores)
    {
        var copia = Lista.Copiar(leitores, nameof(leitores));
        if (copia.Count == 0)
        {
            throw new ArgumentException("Uma sessão de leitura tem ao menos um leitor.", nameof(leitores));
        }

        if (copia.Distinct(ReferenceEqualityComparer.Instance).Count() != copia.Count)
        {
            throw new ArgumentException("A sessão de leitura tem um leitor repetido.", nameof(leitores));
        }

        if (copia.Any(leitor => leitor.Ligado))
        {
            throw new ArgumentException("Um dos leitores já é de outra sessão de leitura.", nameof(leitores));
        }

        foreach (var leitor in copia)
        {
            leitor.Ligar(this);
        }

        _leitores = copia;
    }

    /// <summary>Os leitores, um por conexão, na ordem da abertura.</summary>
    public IReadOnlyList<LeitorDaSessao> Leitores => _leitores;

    /// <summary>A consistência obtida.</summary>
    public abstract ConsistenciaObtida Obtida { get; }

    /// <summary>Se a sessão já foi descartada.</summary>
    internal bool Descartada => Volatile.Read(ref _descartada) != 0;

    /// <summary>
    /// Relê o esquema pelo coordenador, no instante da sessão, com as mesmas regras de <see cref="LeitorEsquema.LerAsync"/>.
    /// Uma chamada do coordenador por vez (a base recusa a outra); em paralelo com os leitores.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema relido.</returns>
    /// <exception cref="ObjectDisposedException">Sessão descartada.</exception>
    /// <exception cref="InvalidOperationException">O coordenador está em outra chamada.</exception>
    public Task<EsquemaBanco> LerEsquemaAsync(CancellationToken cancelamento) => NoCoordenadorAsync(LerEsquemaAsyncCore, cancelamento);

    /// <summary>
    /// A idade do instante da sessão, em transações do banco (no PostgreSQL, a idade do xmin): cresce enquanto a sessão
    /// vive, e pode ser lida várias vezes. Pelo coordenador, uma chamada dele por vez (a base recusa a outra).
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A idade; nula quando o banco não a mede.</returns>
    /// <exception cref="ObjectDisposedException">Sessão descartada.</exception>
    /// <exception cref="InvalidOperationException">O coordenador está em outra chamada.</exception>
    public Task<long?> LerIdadeAsync(CancellationToken cancelamento) => NoCoordenadorAsync(LerIdadeAsyncCore, cancelamento);

    /// <summary>Fecha o coordenador e os leitores, uma vez só; nunca lança.</summary>
    /// <returns>A tarefa do descarte.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar da exceção de quem o chama; a sessão já está perdida.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartada, 1) == 0)
        {
            try
            {
                await DisposeAsyncCore().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A releitura do esquema pelo coordenador; chamada uma por vez.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema relido.</returns>
    protected abstract Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento);

    /// <summary>A idade do instante pelo coordenador; chamada uma por vez.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A idade, ou nula.</returns>
    protected abstract Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento);

    /// <summary>Fecha o coordenador e os leitores; chamado uma vez só.</summary>
    /// <returns>A tarefa.</returns>
    protected abstract ValueTask DisposeAsyncCore();

    /// <summary>Lança se a sessão já foi descartada.</summary>
    /// <exception cref="ObjectDisposedException">Sessão descartada.</exception>
    protected void LancarSeDescartado() => ObjectDisposedException.ThrowIf(Descartada, this);

    // Uma chamada do coordenador por vez: a conexão dele não aceita dois comandos juntos.
    private async Task<T> NoCoordenadorAsync<T>(Func<CancellationToken, Task<T>> chamada, CancellationToken cancelamento)
    {
        LancarSeDescartado();
        if (Interlocked.Exchange(ref _coordenadorOcupado, 1) != 0)
        {
            throw new InvalidOperationException("O coordenador da sessão já está numa chamada: o esquema e a idade são lidos um por vez.");
        }

        try
        {
            return await chamada(cancelamento).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _coordenadorOcupado, 0);
        }
    }
}

/// <summary>
/// Um leitor da sessão: uma conexão no instante da sessão. Leitores diferentes podem ser chamados em paralelo; o mesmo,
/// nunca (a base recusa). Uma leitura que falha não inutiliza o leitor: a seguinte lê no mesmo instante.
/// </summary>
/// <remarks>
/// Toda leitura vê a tabela como ela era no instante: o dialeto falha se a tabela foi esvaziada, reescrita ou trocada
/// depois dele, ou se há uma alteração dela pendente que faria a leitura esperar. A segurança por linha nunca filtra a
/// leitura em silêncio: o dialeto falha se ela valer para o login. O valor lido é de coluna: nenhuma exceção, mensagem ou
/// registro do dialeto o traz, e o motor traduz qualquer exceção do leitor só com o código.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorDaSessao
{
    private SessaoLeitura? _sessao;
    private int _ocupado;

    /// <summary>
    /// O maior valor não nulo de uma coluna <c>Inteiro</c>, com as regras de <see cref="LeitorDadosOrigem.LerMaiorValorAsync"/>
    /// (as linhas que a cópia lê da tabela), no instante da sessão; nos dois lados.
    /// </summary>
    /// <param name="coluna">A tabela e a coluna, como o catálogo as guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O maior valor; nulo quando nenhuma linha tem valor na coluna.</returns>
    /// <exception cref="ObjectDisposedException">Sessão descartada.</exception>
    /// <exception cref="InvalidOperationException">O leitor está em outra leitura, ou não é de uma sessão.</exception>
    public async Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(coluna);
        var sessao = Volatile.Read(ref _sessao) ?? throw new InvalidOperationException("O leitor não é de uma sessão de leitura.");
        ObjectDisposedException.ThrowIf(sessao.Descartada, sessao);
        if (Interlocked.Exchange(ref _ocupado, 1) != 0)
        {
            throw new InvalidOperationException("O leitor já está numa leitura: o mesmo leitor nunca é chamado em paralelo consigo mesmo.");
        }

        try
        {
            return await LerMaiorValorAsyncCore(coluna, cancelamento).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _ocupado, 0);
        }
    }

    /// <summary>A leitura do maior valor; nunca em paralelo neste leitor.</summary>
    /// <param name="coluna">A tabela e a coluna.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O maior valor, ou nulo.</returns>
    protected abstract Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento);

    /// <summary>Se o leitor já é de uma sessão.</summary>
    internal bool Ligado => Volatile.Read(ref _sessao) is not null;

    /// <summary>Liga o leitor à sessão que o fixou.</summary>
    /// <param name="sessao">A sessão.</param>
    /// <exception cref="ArgumentException">O leitor já é de outra sessão.</exception>
    internal void Ligar(SessaoLeitura sessao)
    {
        if (Interlocked.CompareExchange(ref _sessao, sessao, null) is not null)
        {
            throw new ArgumentException("O leitor já é de outra sessão de leitura.", nameof(sessao));
        }
    }
}
