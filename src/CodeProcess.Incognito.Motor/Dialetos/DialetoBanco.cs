using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// Um banco conectado, origem ou destino: as partes comuns aos dois (esquema, identidade e SQL). Dono da conexão de
/// controle, aberta na conexão e fechada no descarte.
/// </summary>
/// <remarks>
/// <para>
/// As partes de metadados (<see cref="Esquema"/>, <see cref="Identidade"/>) usam a conexão de controle e não são
/// chamadas em paralelo; as operações sobre dados, quando entrarem no contrato, abrem as próprias conexões.
/// </para>
/// <para>
/// Uma exceção de dialeto que não seja <see cref="IncognitoException"/> é tratada pelo motor como falha sem mensagem
/// (só o tipo): as do driver podem trazer a string de conexão ou valores.
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
/// A origem: lida, nunca alterada. A leitura consistente dos dados entra no contrato com o INC da leitura.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoOrigem : DialetoBanco
{
}

/// <summary>
/// O destino: esvaziado e gravado. Bloqueio, marcador, diário, restrições e gravação entram no contrato com o INC que
/// os implementa.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoDestino : DialetoBanco
{
}
