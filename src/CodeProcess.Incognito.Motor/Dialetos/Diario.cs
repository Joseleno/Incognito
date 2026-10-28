using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// O diário de execução no destino (esquema <c>incognito</c>): toda alteração do Incognito no esquema do destino passa por
/// aqui e é registrada na mesma transação em que acontece, para que uma execução interrompida em qualquer ponto seja
/// reparada sem perder uma chave estrangeira nem o estado de uma trigger.
/// </summary>
/// <remarks>
/// <para>
/// Regras do contrato, cada uma com fato na suíte: toda operação exige a concessão viva do bloqueio desta instância do
/// destino (<see cref="BloqueioExecucao"/>) e a confere na própria transação; sem ela, lança
/// <see cref="InvalidOperationException"/>. Cada operação é uma transação só: falha no meio, nada fica (nem o registro sem a
/// alteração, nem a alteração sem o registro). A definição registrada recria a chave igual, com o mesmo estado de
/// imposição e de validação; as triggers voltam no estado exato de cada tabela física. Chamar de novo uma operação de
/// restauração depois de uma queda não muda o resultado. O diário é lido igual por outra instância.
/// </para>
/// <para>
/// O diário guarda só chaves estrangeiras e o estado das triggers de usuário: nunca uma restrição com literal (<c>CHECK</c>,
/// <c>NOT NULL</c>, exclusão), um texto de exceção do banco, a chave do Incognito, a string de conexão nem um valor de
/// linha. Nenhuma exceção, mensagem ou registro do dialeto traz a definição guardada, o texto lido do diário ou a mensagem
/// do banco (a da validação traz o valor da chave órfã): só o código. O diário guardado no destino é conferido antes de
/// cada uso (dono, privilégios de terceiros, estrutura sem gatilho que rode código), porque o dialeto executa a definição
/// guardada nele.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DiarioExecucao
{
    /// <summary>
    /// O que o diário pode deixar no esquema reservado do destino, por nome e tipo: o <c>planejar</c> aceita um
    /// subconjunto disto no destino, e nada mais.
    /// </summary>
    public abstract IReadOnlyList<ObjetoReservado> ObjetosDoDiario { get; }

    /// <summary>
    /// Cria o esquema e as tabelas do diário que faltam (<c>preparar-destino</c>), tira as concessões a terceiros que o banco
    /// der às tabelas novas e confere o diário; idempotente.
    /// </summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">O login não pode criar o esquema, ou o que existe não é o diário.</exception>
    public abstract Task CriarAsync(CancellationToken cancelamento);

    /// <summary>
    /// Lê o diário, sem escrever: se existe, as execuções em andamento ou com entrada pendente, e as entradas pendentes na
    /// ordem de restauração (as triggers antes das chaves; dentro de cada tipo, por tabela e nome).
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O estado do diário.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">O que existe no lugar do diário não é o diário, ou foi adulterado.</exception>
    public abstract Task<EstadoDiario> LerAsync(CancellationToken cancelamento);

    /// <summary>Registra uma execução em andamento.</summary>
    /// <param name="execucao">O identificador da execução.</param>
    /// <param name="marcador">O identificador do marcador do destino, a que a execução fica amarrada.</param>
    /// <param name="iniciadaEm">Quando, em UTC.</param>
    /// <param name="cancelamento">Cancela o registro.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">
    /// O diário não existe, ou há execução em andamento (o motor repara antes de começar outra).
    /// </exception>
    public abstract Task IniciarAsync(Guid execucao, Guid marcador, DateTimeOffset iniciadaEm, CancellationToken cancelamento);

    /// <summary>
    /// Remove registrando uma chave estrangeira: grava a definição e o estado exatos e remove a chave, numa transação. A
    /// chave que já tem entrada pendente é adotada pela execução, mantendo a definição original: a que ficou sem validação
    /// é removida de novo, e a que já está fora só muda de execução.
    /// </summary>
    /// <param name="execucao">A execução em andamento.</param>
    /// <param name="tabela">A tabela da chave, como o catálogo a guarda (numa particionada, a raiz).</param>
    /// <param name="chave">O nome da chave.</param>
    /// <param name="cancelamento">Cancela a remoção.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">
    /// A chave não existe e não está no diário, é a cópia de uma chave da tabela mãe, ou a execução não está em andamento.
    /// </exception>
    public abstract Task RemoverChaveAsync(Guid execucao, NomeObjeto tabela, string chave, CancellationToken cancelamento);

    /// <summary>
    /// Desliga registrando as triggers de usuário de uma tabela e das partições dela: grava o estado de cada uma, por tabela
    /// física, e as desliga, numa transação. Na herança clássica, só a própria tabela.
    /// </summary>
    /// <param name="execucao">A execução em andamento.</param>
    /// <param name="tabela">A tabela (numa particionada, a raiz).</param>
    /// <param name="cancelamento">Cancela o desligamento.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">A tabela já tem as triggers no diário, ou a execução não está em andamento.</exception>
    public abstract Task DesligarTriggersAsync(Guid execucao, NomeObjeto tabela, CancellationToken cancelamento);

    /// <summary>
    /// Religa as triggers de uma entrada no estado original, cada uma na própria tabela física, e apaga a entrada, numa
    /// transação. A trigger que não existe mais é pulada.
    /// </summary>
    /// <param name="entrada">A entrada das triggers.</param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As triggers puladas; vazia se nenhuma.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    public abstract Task<IReadOnlyList<TriggerDoDiario>> ReligarTriggersAsync(EntradaDiario entrada, CancellationToken cancelamento);

    /// <summary>
    /// Recria uma chave removida: sem validar quando o banco aceita, para validar depois em <see cref="ValidarChaveAsync"/>;
    /// validando direto quando não aceita. A chave que já existe igual só atualiza a entrada.
    /// </summary>
    /// <param name="entrada">A entrada da chave.</param>
    /// <param name="cancelamento">Cancela a recriação.</param>
    /// <returns>
    /// A situação da entrada depois: nula quando o estado original voltou e a entrada saiu;
    /// <see cref="SituacaoEntrada.SemValidacao"/> quando a chave voltou sem validação;
    /// <see cref="SituacaoEntrada.Removida"/> quando a validação direta não passou (há linha órfã) e a chave continua fora.
    /// </returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="DialetoRecusouException">Há uma chave com o mesmo nome e outra definição, ou a entrada não confere.</exception>
    public abstract Task<SituacaoEntrada?> RecriarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento);

    /// <summary>Valida uma chave recriada sem validação, na conexão de controle, e apaga a entrada quando passa.</summary>
    /// <param name="entrada">A entrada da chave, sem validação.</param>
    /// <param name="cancelamento">Cancela a validação.</param>
    /// <returns>
    /// Nula quando validou e a entrada saiu; <see cref="SituacaoEntrada.SemValidacao"/> quando há linha órfã;
    /// <see cref="SituacaoEntrada.Removida"/> quando a chave não existe mais.
    /// </returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    public abstract Task<SituacaoEntrada?> ValidarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento);

    /// <summary>
    /// Apaga uma entrada sem restaurar (<c>reparar --descartar</c>, depois da confirmação): para a chave que não se recria
    /// mais, porque a tabela referenciada sumiu ou outra definição tomou o nome.
    /// </summary>
    /// <param name="entrada">A entrada.</param>
    /// <param name="cancelamento">Cancela o descarte.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    public abstract Task DescartarAsync(EntradaDiario entrada, CancellationToken cancelamento);

    /// <summary>Grava a situação final de uma execução em andamento.</summary>
    /// <param name="execucao">A execução.</param>
    /// <param name="situacao">Concluída, falhou ou interrompida.</param>
    /// <param name="encerradaEm">Quando, em UTC.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Esta instância não tem a concessão viva do bloqueio.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A situação é <see cref="SituacaoExecucao.EmAndamento"/>.</exception>
    /// <exception cref="DialetoRecusouException">A execução não está em andamento.</exception>
    public abstract Task EncerrarAsync(Guid execucao, SituacaoExecucao situacao, DateTimeOffset encerradaEm, CancellationToken cancelamento);
}

/// <summary>O tipo de uma entrada do diário.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum TipoEntrada
{
    /// <summary>Uma chave estrangeira removida.</summary>
    ChaveEstrangeira = 0,

    /// <summary>As triggers de usuário de uma tabela (e das partições dela), desligadas.</summary>
    Triggers = 1,
}

/// <summary>A situação de uma entrada pendente do diário.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum SituacaoEntrada
{
    /// <summary>A chave está fora, ou as triggers estão desligadas.</summary>
    Removida = 0,

    /// <summary>A chave voltou sem validação, e o estado original era validado: falta validar.</summary>
    SemValidacao = 1,
}

/// <summary>A situação de uma execução registrada no diário.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum SituacaoExecucao
{
    /// <summary>Em andamento: o destino não deve ser usado.</summary>
    EmAndamento = 0,

    /// <summary>Concluída.</summary>
    Concluida = 1,

    /// <summary>Falhou, e o que havia a restaurar foi restaurado ou ficou no diário.</summary>
    Falhou = 2,

    /// <summary>Encontrada em andamento pelo reparo: a cópia ficou pela metade, e o destino não deve ser usado.</summary>
    Interrompida = 3,
}

/// <summary>O tipo de um objeto no esquema reservado ao Incognito.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum TipoObjetoReservado
{
    /// <summary>Uma tabela comum.</summary>
    Tabela = 0,

    /// <summary>Um índice de tabela comum.</summary>
    Indice = 1,

    /// <summary>Uma sequence.</summary>
    Sequence = 2,

    /// <summary>Uma restrição que o banco guarda como objeto (no SQL Server, a chave primária).</summary>
    Restricao = 3,

    /// <summary>Qualquer outro (particionada, tabela estrangeira, view, materialized view, índice de particionada).</summary>
    Outro = 4,
}

/// <summary>Um objeto no esquema reservado ao Incognito, pelo nome e pelo tipo.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ObjetoReservado : IEquatable<ObjetoReservado>
{
    private readonly NomeObjeto _nome = null!;
    private readonly TipoObjetoReservado _tipo;

    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>O tipo.</summary>
    public required TipoObjetoReservado Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de objeto reservado desconhecido.");
    }

    /// <summary>Igualdade pelo nome (ordinal) e pelo tipo.</summary>
    /// <param name="other">O outro objeto.</param>
    /// <returns>Verdadeiro se nome e tipo são iguais.</returns>
    public bool Equals(ObjetoReservado? other) => other is not null && Nome.Equals(other.Nome) && Tipo == other.Tipo;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ObjetoReservado);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Nome, Tipo);

    /// <summary>O nome e o tipo, para mensagens.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => $"{Nome} ({Tipo})";
}

/// <summary>O diário lido.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class EstadoDiario
{
    private readonly IReadOnlyList<ExecucaoLida> _execucoes = [];
    private readonly IReadOnlyList<EntradaDiario> _pendentes = [];

    /// <summary>Se o diário existe (o esquema e as tabelas).</summary>
    public required bool Existe { get; init; }

    /// <summary>As execuções em andamento, e as encerradas que ainda têm entrada pendente, em ordem de início.</summary>
    public IReadOnlyList<ExecucaoLida> Execucoes { get => _execucoes; init => _execucoes = Lista.Copiar(value, nameof(Execucoes)); }

    /// <summary>As entradas pendentes, na ordem de restauração: as triggers antes das chaves; em cada tipo, por tabela e nome.</summary>
    public IReadOnlyList<EntradaDiario> Pendentes { get => _pendentes; init => _pendentes = Lista.Copiar(value, nameof(Pendentes)); }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(EstadoDiario)} ({Execucoes.Count} execuções, {Pendentes.Count} pendentes)";
}

/// <summary>Uma execução registrada no diário.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ExecucaoLida
{
    private readonly SituacaoExecucao _situacao;

    /// <summary>O identificador.</summary>
    public required Guid Id { get; init; }

    /// <summary>O identificador do marcador do destino quando a execução começou.</summary>
    public required Guid Marcador { get; init; }

    /// <summary>Quando começou, em UTC.</summary>
    public required DateTimeOffset IniciadaEm { get; init; }

    /// <summary>A situação.</summary>
    public required SituacaoExecucao Situacao
    {
        get => _situacao;
        init => _situacao = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Situacao), "Situação de execução desconhecida.");
    }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ExecucaoLida)} ({Situacao})";
}

/// <summary>
/// Uma entrada pendente do diário: o que o motor vê dela (o tipo, a tabela, o nome e a situação), nunca a definição nem o
/// estado guardados, que ficam no dialeto.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class EntradaDiario
{
    private readonly NomeObjeto _tabela = null!;
    private readonly TipoEntrada _tipo;
    private readonly SituacaoEntrada _situacao;

    /// <summary>A execução que registrou (ou adotou) a entrada.</summary>
    public required Guid Execucao { get; init; }

    /// <summary>O tipo.</summary>
    public required TipoEntrada Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de entrada desconhecido.");
    }

    /// <summary>A tabela da chave, ou a tabela cujas triggers foram desligadas (numa particionada, a raiz).</summary>
    public required NomeObjeto Tabela { get => _tabela; init => _tabela = value ?? throw new ArgumentNullException(nameof(Tabela)); }

    /// <summary>O nome da chave; nulo nas triggers.</summary>
    public string? Nome { get; init; }

    /// <summary>A situação.</summary>
    public required SituacaoEntrada Situacao
    {
        get => _situacao;
        init => _situacao = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Situacao), "Situação de entrada desconhecida.");
    }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(EntradaDiario)} ({Tipo}, {Situacao})";
}

/// <summary>Uma trigger registrada no diário, na tabela física em que está.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class TriggerDoDiario
{
    private readonly NomeObjeto _tabela = null!;
    private readonly string _nome = null!;

    /// <summary>A tabela física (a raiz ou uma partição).</summary>
    public required NomeObjeto Tabela { get => _tabela; init => _tabela = value ?? throw new ArgumentNullException(nameof(Tabela)); }

    /// <summary>O nome da trigger.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <inheritdoc/>
    public override string ToString() => nameof(TriggerDoDiario);
}
