using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>A leitura do esquema de um banco, sem alterá-lo.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorEsquema
{
    /// <summary>
    /// Lê o esquema: tabelas, colunas, chaves, sequences e objetos residuais, as tabelas, as sequences e os objetos
    /// residuais em ordem ordinal de esquema e nome (o plano e o relatório saem iguais entre leituras), as colunas na
    /// ordem do catálogo, e as permissões do login que lê. As tabelas, as sequences e os objetos residuais do esquema
    /// <c>incognito</c> (diário do Incognito) ficam fora das listas, e os objetos dele vão para
    /// <see cref="EsquemaBanco.ObjetosNoEsquemaReservado"/>. Não escreve no banco.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema lido.</returns>
    /// <remarks>
    /// Um dialeto que não vê o catálogo inteiro (por falta de privilégio do login) lança: nunca devolve um esquema parcial,
    /// que deixaria tabelas, restrições ou objetos residuais sem conferência.
    /// </remarks>
    public abstract Task<EsquemaBanco> LerAsync(CancellationToken cancelamento);
}

/// <summary>
/// A leitura de dados da origem que o <c>planejar</c> precisa: só agregados de uma coluna, nunca uma linha. O valor lido é
/// de coluna tratada: nenhuma exceção, mensagem ou registro do dialeto o traz, e o motor traduz qualquer exceção desta parte
/// só com o código.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorDadosOrigem
{
    /// <summary>
    /// O maior valor não nulo de uma coluna <c>Inteiro</c> do esquema lido, nas linhas que a cópia lê da tabela: numa
    /// particionada, as de todas as partições; nas outras, só as da própria tabela, sem as herdeiras de herança clássica.
    /// Lê na conexão de controle, numa transação só de leitura, e não escreve no banco. A segurança por linha nunca filtra
    /// a leitura em silêncio: o dialeto falha se ela valer para o login.
    /// </summary>
    /// <param name="coluna">A tabela e a coluna, como o catálogo as guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O maior valor; nulo quando nenhuma linha tem valor na coluna.</returns>
    public abstract Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento);
}

/// <summary>
/// A leitura do destino que a proteção dele precisa: só se uma tabela tem linhas, nunca um valor (o destino restaurado de
/// produção guarda dado real). O motor traduz qualquer exceção desta parte só com o código.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorDadosDestino
{
    /// <summary>
    /// Se a tabela tem ao menos uma linha, como a cópia a trataria: numa particionada, as de todas as partições; nas
    /// outras, só as da própria tabela, sem as herdeiras de herança clássica. Lê na conexão de controle, numa transação só
    /// de leitura, e não escreve no banco. A segurança por linha nunca esconde linhas em silêncio: o dialeto falha se ela
    /// valer para o login.
    /// </summary>
    /// <param name="tabela">A tabela, como o catálogo a guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>Se há linha.</returns>
    public abstract Task<bool> TemLinhasAsync(NomeObjeto tabela, CancellationToken cancelamento);
}

/// <summary>
/// A gravação do marcador do Incognito no destino (<c>preparar-destino</c>); a leitura, nos dois lados, vem pela
/// identidade (<see cref="IdentidadeLida.Marcador"/>).
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class MarcadorDestino
{
    /// <summary>
    /// Grava o marcador no banco do destino, só onde não há nada (<see cref="SituacaoMarcador.Ausente"/>): um comentário
    /// alheio, um marcador ilegível ou um já gravado nunca são sobrescritos. Confirma e relê numa transação nova; o
    /// relido diferente do gravado é falha.
    /// </summary>
    /// <param name="identificador">O identificador aleatório do marcador.</param>
    /// <param name="gravadoEm">Quando, em UTC (deslocamento zero) e sem fração de segundo.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="ArgumentException">Data fora de UTC ou com fração de segundo, ou identificador vazio.</exception>
    /// <exception cref="DialetoRecusouException">
    /// O banco já tem algo no lugar do marcador, ou o login não pode gravá-lo (no PostgreSQL, precisa ser o dono do banco);
    /// a mensagem nunca traz o texto que o banco tinha.
    /// </exception>
    public abstract Task GravarAsync(Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento);
}

/// <summary>
/// O bloqueio exclusivo de execução no destino: uma execução do Incognito por banco de destino, de sessão, em
/// conexão dedicada.
/// </summary>
/// <remarks>
/// Regras do contrato, cada uma com fato na suíte: a concessão vive numa conexão própria, sem pool, separada da conexão de
/// controle e do mesmo servidor e banco dela; a liberação é explícita antes de fechar; um segundo pedido na mesma instância
/// com uma concessão viva lança (os bloqueios de sessão são reentrantes, e o segundo passaria); a exclusividade e a
/// persistência valem entre instâncias novas do dialeto; a queda da conexão do bloqueio é percebida
/// (<see cref="ConcessaoBloqueio.Perdido"/>). Descartar o <see cref="DialetoDestino"/> com uma concessão viva a libera.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class BloqueioExecucao
{
    /// <summary>Tenta adquirir o bloqueio, sem esperar.</summary>
    /// <param name="cancelamento">Cancela a tentativa.</param>
    /// <returns>A concessão; nula quando outra sessão tem o bloqueio.</returns>
    /// <exception cref="InvalidOperationException">Esta instância já tem uma concessão viva.</exception>
    public abstract Task<ConcessaoBloqueio?> TentarAdquirirAsync(CancellationToken cancelamento);
}

/// <summary>
/// Uma concessão do bloqueio de execução. O ciclo de vida é da base: o descarte libera uma vez e nunca lança; a perda
/// (queda da conexão ou batimento que falha) cancela <see cref="Perdido"/>, e a liberação voluntária nunca o cancela.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class ConcessaoBloqueio : IAsyncDisposable
{
    private readonly CancellationTokenSource _perdido = new();
    private int _liberada;

    /// <summary>Cancelado quando o bloqueio se perde; nunca lança, nem depois do descarte.</summary>
    public CancellationToken Perdido => _perdido.Token;

    /// <summary>
    /// O batimento: confere no servidor que a sessão ainda tem o bloqueio, antes de cada passo crítico. Se não tem, ou a
    /// conferência falha, cancela <see cref="Perdido"/> e lança.
    /// </summary>
    /// <param name="cancelamento">Cancela a conferência (não é perda).</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="ObjectDisposedException">A concessão já foi liberada.</exception>
    public async Task ConferirAsync(CancellationToken cancelamento)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _liberada) != 0, this);
        try
        {
            await ConferirAsyncCore(cancelamento).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            SinalizarPerda();
            throw;
        }
    }

    /// <summary>Libera o bloqueio e fecha a conexão dedicada, uma vez só; nunca lança (a falha da liberação marca a perda).</summary>
    /// <returns>A tarefa do descarte.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar da exceção de quem o chama; a falha marca a perda.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _liberada, 1) == 0)
        {
            try
            {
                await LiberarAsyncCore().ConfigureAwait(false);
            }
            catch
            {
                SinalizarPerda();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A conferência no servidor; lança se a sessão não tem mais o bloqueio.</summary>
    /// <param name="cancelamento">Cancela a conferência.</param>
    /// <returns>A tarefa.</returns>
    protected abstract Task ConferirAsyncCore(CancellationToken cancelamento);

    /// <summary>A liberação explícita do bloqueio e o fechamento da conexão dedicada; chamada uma vez só.</summary>
    /// <returns>A tarefa.</returns>
    protected abstract ValueTask LiberarAsyncCore();

    /// <summary>Marca a perda do bloqueio (a queda da conexão, percebida pelo dialeto); nunca lança.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Quem registrou no token não derruba quem sinaliza a perda.")]
    protected void SinalizarPerda()
    {
        try
        {
            _perdido.Cancel();
        }
        catch
        {
        }
    }
}

/// <summary>O banco, o servidor, a linhagem e o transporte da conexão.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class IdentidadeBanco
{
    /// <summary>Lê a identidade, sem escrever no banco.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A identidade lida.</returns>
    public abstract Task<IdentidadeLida> LerAsync(CancellationToken cancelamento);
}

/// <summary>Se um nome escrito designa um nome do catálogo (<see cref="DialetoSql.CompararNome"/>).</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum CorrespondenciaNome
{
    /// <summary>Não designa.</summary>
    NaoCorresponde = 0,

    /// <summary>Designa.</summary>
    Corresponde = 1,

    /// <summary>O dialeto não sabe decidir sem adivinhar (por exemplo, a dobra de caixa depende do locale do servidor).</summary>
    Indeterminada = 2,
}

/// <summary>A sintaxe do banco.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoSql
{
    /// <summary>
    /// Cita um identificador como o catálogo o guarda (<c>"Cliente"</c> no PostgreSQL, <c>[Cliente]</c> no SQL
    /// Server), escapando o delimitador.
    /// </summary>
    /// <param name="identificador">O identificador, como o catálogo o guarda.</param>
    /// <returns>O identificador citado.</returns>
    public abstract string Citar(string identificador);

    /// <summary>
    /// Se um nome escrito na configuração designa um nome do catálogo, pela regra do banco desta conexão: a dobra de caixa
    /// do nome sem aspas e a igualdade dos identificadores dependem do banco (a codificação no PostgreSQL, a colação do
    /// catálogo no SQL Server). Vale só para o banco da conexão de onde veio este <see cref="DialetoSql"/>. O motor também a
    /// usa com o nome do catálogo do outro lado, entre aspas, para parear a origem com o destino, e com nomes fixos que podem
    /// não existir no catálogo (os esquemas reservados): <paramref name="doCatalogo"/> não precisa existir.
    /// </summary>
    /// <param name="escrito">O nome como foi escrito, sem as aspas.</param>
    /// <param name="entreAspas">Se foi escrito entre aspas (delimitado, na sintaxe do banco).</param>
    /// <param name="doCatalogo">O nome como o catálogo o guarda.</param>
    /// <returns>
    /// Se corresponde, se não corresponde, ou <see cref="CorrespondenciaNome.Indeterminada"/> quando o dialeto não sabe
    /// decidir sem adivinhar (o motor recusa o plano, nunca escolhe).
    /// </returns>
    public abstract CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo);

    /// <summary>Cita o esquema e o nome, separados por ponto.</summary>
    /// <param name="nome">O nome do objeto.</param>
    /// <returns>O nome citado.</returns>
    /// <exception cref="ArgumentNullException">Nome nulo.</exception>
    public string Citar(NomeObjeto nome)
    {
        ArgumentNullException.ThrowIfNull(nome);
        return Citar(nome.Esquema) + "." + Citar(nome.Nome);
    }
}
