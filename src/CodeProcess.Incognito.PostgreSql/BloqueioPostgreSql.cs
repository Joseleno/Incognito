using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// O bloqueio de execução do PostgreSQL: <c>pg_try_advisory_lock(int4, int4)</c> de sessão, numa conexão dedicada aberta
/// pela mesma fonte da conexão de controle (sem pool) e conferida como ela, com a chave do Incognito
/// (<see cref="Classe"/>, <see cref="Execucao"/>). O lock consultivo é por banco: um bloqueio por destino.
/// </summary>
/// <remarks>
/// <para>
/// Na sessão dedicada, os keepalives TCP do servidor ficam em minutos (<c>tcp_keepalives_*</c>): sem eles, uma máquina que
/// cai ou uma rede partida deixaria o lock preso no servidor por horas (a próxima execução sairia com código 1). Pelo
/// socket Unix o servidor os ignora.
/// </para>
/// <para>
/// A chave divide espaço com os locks consultivos de qualquer aplicação do mesmo banco; o <c>classid</c> positivo aparece
/// assim no <c>pg_locks</c>, para quem administra conferir. A conexão a mais conta em <c>max_connections</c>. Um PgBouncer
/// em modo transação ou instrução quebra o lock de sessão; em modo sessão funciona.
/// </para>
/// </remarks>
internal sealed class BloqueioPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : BloqueioExecucao
{
    /// <summary>A primeira metade da chave: "INCG" em ASCII, positiva.</summary>
    internal const int Classe = 0x494E4347;

    /// <summary>A segunda metade da chave: a execução.</summary>
    internal const int Execucao = 1;

    private const string Keepalives = "SET tcp_keepalives_idle = 60; SET tcp_keepalives_interval = 10; SET tcp_keepalives_count = 6";

    private ConcessaoPostgreSql? _viva;
    private int _ocupado;

    /// <summary>De quanto em quanto tempo a concessão confere o bloqueio sozinha, para perceber a queda.</summary>
    internal TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(15);

    public override async Task<ConcessaoBloqueio?> TentarAdquirirAsync(CancellationToken cancelamento)
    {
        lancarSeDescartado();

        // Os locks de sessão são reentrantes: um segundo pedido nesta instância passaria, e seria preciso liberar duas vezes.
        if (Interlocked.CompareExchange(ref _ocupado, 1, 0) != 0)
        {
            throw new InvalidOperationException("Esta instância do destino já tem o bloqueio de execução (ou o pede agora).");
        }

        NpgsqlConnection? dedicada = null;
        try
        {
            dedicada = await conexao.AbrirOutraAsync(cancelamento).ConfigureAwait(false);
            await using (var ajustar = new NpgsqlCommand(Keepalives, dedicada))
            {
                await ajustar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
            }

            await using (var pedir = Comando("SELECT pg_catalog.pg_try_advisory_lock($1, $2)", dedicada))
            {
                if (await pedir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
                {
                    await dedicada.DisposeAsync().ConfigureAwait(false);
                    Volatile.Write(ref _ocupado, 0);
                    return null;
                }
            }

            var concessao = new ConcessaoPostgreSql(dedicada, Intervalo, () =>
            {
                Volatile.Write(ref _viva, null);
                Volatile.Write(ref _ocupado, 0);
            });
            Volatile.Write(ref _viva, concessao);
            return concessao;
        }
        catch
        {
            if (dedicada is not null)
            {
                await dedicada.DisposeAsync().ConfigureAwait(false);
            }

            Volatile.Write(ref _ocupado, 0);
            throw;
        }
    }

    /// <summary>Libera a concessão viva, no descarte do destino.</summary>
    /// <returns>A tarefa.</returns>
    internal ValueTask LiberarAsync() => Volatile.Read(ref _viva)?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>Um comando com a chave do Incognito nos parâmetros <c>$1</c> e <c>$2</c>.</summary>
    internal static NpgsqlCommand Comando(string sql, NpgsqlConnection conexao)
    {
        var comando = new NpgsqlCommand(sql, conexao) { CommandTimeout = 5 };
        comando.Parameters.Add(new NpgsqlParameter { Value = Classe, NpgsqlDbType = NpgsqlDbType.Integer });
        comando.Parameters.Add(new NpgsqlParameter { Value = Execucao, NpgsqlDbType = NpgsqlDbType.Integer });
        return comando;
    }
}

/// <summary>
/// Uma concessão do bloqueio do PostgreSQL, dona da conexão dedicada. O batimento confere no <c>pg_locks</c> que esta
/// sessão tem o lock (a forma de dois <c>int4</c> é a de <c>objsubid = 2</c>), com tempo-limite curto: numa conexão meio
/// aberta, a consulta não trava. Um temporizador repete o batimento sozinho, e a mudança da conexão para fechada ou
/// quebrada também marca a perda.
/// </summary>
internal sealed class ConcessaoPostgreSql : ConcessaoBloqueio
{
    private const string Batimento = """
        SELECT EXISTS (
            SELECT FROM pg_catalog.pg_locks l
            WHERE l.locktype = 'advisory' AND l.pid = pg_catalog.pg_backend_pid() AND l.granted
              AND l.database = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database())
              AND l.classid = $1::pg_catalog.oid AND l.objid = $2::pg_catalog.oid AND l.objsubid = 2)
        """;

    private readonly NpgsqlConnection _conexao;
    private readonly Action _aoLiberar;
    private readonly SemaphoreSlim _uso = new(1, 1);
    private readonly CancellationTokenSource _parar = new();
    private readonly Task _temporizador;

    internal ConcessaoPostgreSql(NpgsqlConnection conexao, TimeSpan intervalo, Action aoLiberar)
    {
        _conexao = conexao;
        _aoLiberar = aoLiberar;
        _conexao.StateChange += AoMudar;
        _temporizador = BaterAsync(intervalo);
    }

    /// <summary>A conexão dedicada, para os testes conferirem a sessão dela.</summary>
    internal NpgsqlConnection Conexao => _conexao;

    protected override async Task ConferirAsyncCore(CancellationToken cancelamento)
    {
        await _uso.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            await using var comando = BloqueioPostgreSql.Comando(Batimento, _conexao);
            if (await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                throw new IncognitoException(CodigosPostgreSql.BloqueioPerdido, "O bloqueio de execução do destino se perdeu.");
            }
        }
        finally
        {
            _uso.Release();
        }
    }

    protected override async ValueTask LiberarAsyncCore()
    {
        await _parar.CancelAsync().ConfigureAwait(false);
        _conexao.StateChange -= AoMudar;
        try
        {
            await _temporizador.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await _uso.WaitAsync().ConfigureAwait(false);
            await using var comando = BloqueioPostgreSql.Comando("SELECT pg_catalog.pg_advisory_unlock($1, $2)", _conexao);
            if (await comando.ExecuteScalarAsync().ConfigureAwait(false) is not true)
            {
                throw new IncognitoException(CodigosPostgreSql.BloqueioPerdido, "O bloqueio de execução do destino não estava mais com esta sessão ao ser liberado.");
            }
        }
        finally
        {
            try
            {
                await _conexao.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _parar.Dispose();
                _aoLiberar();
            }
        }
    }

    // A queda da conexão dedicada, percebida pelo driver numa operação.
    private void AoMudar(object? origem, StateChangeEventArgs mudanca)
    {
        if (mudanca.CurrentState is ConnectionState.Closed or ConnectionState.Broken && !_parar.IsCancellationRequested)
        {
            SinalizarPerda();
        }
    }

    // O batimento sozinho, até a liberação ou a primeira falha.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Qualquer falha do batimento é a perda do bloqueio, que fica marcada.")]
    private async Task BaterAsync(TimeSpan intervalo)
    {
        using var temporizador = new PeriodicTimer(intervalo);
        try
        {
            while (await temporizador.WaitForNextTickAsync(_parar.Token).ConfigureAwait(false))
            {
                await ConferirAsyncCore(_parar.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_parar.IsCancellationRequested)
        {
        }
        catch
        {
            SinalizarPerda();
        }
    }
}
