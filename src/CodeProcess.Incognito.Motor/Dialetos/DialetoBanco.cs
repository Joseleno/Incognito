using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// Um banco conectado, origem ou destino: as partes comuns aos dois (esquema, identidade e SQL). Dono da conexão de
/// controle, aberta na conexão e fechada no descarte.
/// </summary>
/// <remarks>
/// <para>
/// As partes de metadados (<see cref="Esquema"/>, <see cref="Identidade"/>), a leitura agregada da origem
/// (<see cref="DialetoOrigem.Dados"/>), a gravação do marcador e a leitura de existência do destino
/// (<see cref="DialetoDestino.Marcador"/>, <see cref="DialetoDestino.Dados"/>) usam a conexão de controle e não são
/// chamadas em paralelo; o bloqueio de execução (<see cref="DialetoDestino.Bloqueio"/>) vive numa conexão dedicada; as
/// operações que leem ou gravam linhas, quando entrarem no contrato, abrem as próprias conexões.
/// </para>
/// <para>
/// O motor traduz toda exceção do dialeto: uma <see cref="DialetoRecusouException"/> vira erro do plano (saída 2); uma
/// <see cref="IncognitoException"/> de tipo exato, falha com o código e a mensagem (o texto do autor do dialeto, nunca
/// dado lido nem a string de conexão); uma subclasse dela, falha só com o código; e qualquer outra, falha sem mensagem,
/// só com o tipo, porque as do driver podem trazer a string de conexão ou valores. O código precisa estar na faixa dos
/// dialetos (<c>INC31xx</c> a <c>INC34xx</c>), e a interna nunca sai. O cancelamento pedido pelo chamador sai como
/// cancelamento novo, sem a interna; o que o chamador não pediu é falha. Ao fechar, qualquer falha sai só com o tipo.
/// </para>
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoBanco : IAsyncDisposable
{
    private int _descartado;

    /// <summary>A leitura do esquema do banco.</summary>
    public abstract LeitorEsquema Esquema { get; }

    /// <summary>O banco, o servidor e a linhagem, para conferir o destino e avisar de origem e destino iguais.</summary>
    public abstract IdentidadeBanco Identidade { get; }

    /// <summary>A sintaxe do banco: nomes citados.</summary>
    public abstract DialetoSql Sql { get; }

    /// <summary>
    /// Fecha a conexão de controle e o que mais o dialeto abriu. Só a primeira chamada descarta; uma chamada
    /// concorrente volta sem esperar a primeira terminar.
    /// </summary>
    /// <returns>A tarefa do descarte.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartado, 1) == 0)
        {
            await DisposeAsyncCore().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>O descarte do dialeto, chamado uma vez só. Padrão: nada.</summary>
    /// <returns>A tarefa do descarte.</returns>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    /// <summary>Lança se o dialeto já foi descartado: para as partes recusarem uso depois do descarte.</summary>
    /// <exception cref="ObjectDisposedException">Dialeto descartado.</exception>
    protected void LancarSeDescartado() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
}

/// <summary>
/// A origem: lida, nunca alterada. Além das partes comuns, a leitura agregada que o <c>planejar</c> precisa
/// (<see cref="Dados"/>); a leitura consistente das linhas entra no contrato com o INC da leitura.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoOrigem : DialetoBanco
{
    /// <summary>A leitura agregada dos dados da origem (o maior valor de uma coluna).</summary>
    public abstract LeitorDadosOrigem Dados { get; }
}

/// <summary>
/// O destino: esvaziado e gravado. Além das partes comuns, o bloqueio de execução, a gravação do marcador e a leitura de
/// existência de linhas; o diário, as restrições e a gravação entram no contrato com o INC que os implementa.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoDestino : DialetoBanco
{
    /// <summary>O bloqueio exclusivo de execução, em conexão própria.</summary>
    public abstract BloqueioExecucao Bloqueio { get; }

    /// <summary>A gravação do marcador do Incognito.</summary>
    public abstract MarcadorDestino Marcador { get; }

    /// <summary>Se uma tabela do destino tem linhas, sem ler valor.</summary>
    public abstract LeitorDadosDestino Dados { get; }
}
