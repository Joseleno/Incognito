using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>O dialeto PostgreSQL (<c>provedor: postgresql</c>), do 15 em diante.</summary>
/// <remarks>
/// <para>
/// A string de conexão é lida pelo Npgsql, e o dialeto impõe por cima: uma conexão de controle sem pool, sem detalhe de
/// erro do servidor, sem registro de parâmetros, em UTF-8, sem senha em claro, sem a credencial do sistema quando há
/// senha, com TLS fora da própria máquina, só o <c>pg_catalog</c> no caminho de busca e, na origem, transações só de
/// leitura. Recusa string com mais de um servidor, sem servidor ou sem banco, com opção obsoleta ou sem senha com a do
/// ambiente definida, e a leitura de um banco com partição de <c>DETACH ... CONCURRENTLY</c> interrompido.
/// </para>
/// <para>Parte do contrato experimental de dialeto: muda até a 1.0.</para>
/// </remarks>
[Experimental("INCX001")]
public sealed class ProvedorPostgreSql : ProvedorDialeto
{
    /// <inheritdoc/>
    public override string Nome => "postgresql";

    /// <inheritdoc/>
    public override async Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        new OrigemPostgreSql(await ConexaoPostgreSql.AbrirAsync(conexao, origem: true, cancelamento).ConfigureAwait(false));

    /// <inheritdoc/>
    public override async Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        new DestinoPostgreSql(await ConexaoPostgreSql.AbrirAsync(conexao, origem: false, cancelamento).ConfigureAwait(false));
}

/// <summary>A origem PostgreSQL.</summary>
internal sealed class OrigemPostgreSql : DialetoOrigem
{
    private readonly ConexaoPostgreSql _conexao;

    internal OrigemPostgreSql(ConexaoPostgreSql conexao)
    {
        _conexao = conexao;
        Esquema = new LeitorEsquemaPostgreSql(conexao, LancarSeDescartado);
        Identidade = new IdentidadePostgreSql(conexao, LancarSeDescartado);
        Sql = new DialetoSqlPostgreSql(conexao.Nomes);
        Dados = new LeitorDadosOrigemPostgreSql(conexao, LancarSeDescartado);
    }

    public override LeitorEsquema Esquema { get; }

    public override IdentidadeBanco Identidade { get; }

    public override DialetoSql Sql { get; }

    public override LeitorDadosOrigem Dados { get; }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await _conexao.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }
    }
}

/// <summary>O destino PostgreSQL.</summary>
internal sealed class DestinoPostgreSql : DialetoDestino
{
    private readonly ConexaoPostgreSql _conexao;

    internal DestinoPostgreSql(ConexaoPostgreSql conexao)
    {
        _conexao = conexao;
        Esquema = new LeitorEsquemaPostgreSql(conexao, LancarSeDescartado);
        Identidade = new IdentidadePostgreSql(conexao, LancarSeDescartado);
        Sql = new DialetoSqlPostgreSql(conexao.Nomes);
        _bloqueio = new BloqueioPostgreSql(conexao, LancarSeDescartado);
        Marcador = new MarcadorDestinoPostgreSql(conexao, LancarSeDescartado);
        Dados = new LeitorDadosDestinoPostgreSql(conexao, LancarSeDescartado);
        Diario = new DiarioPostgreSql(conexao, _bloqueio, LancarSeDescartado);
    }

    private readonly BloqueioPostgreSql _bloqueio;

    public override LeitorEsquema Esquema { get; }

    public override IdentidadeBanco Identidade { get; }

    public override DialetoSql Sql { get; }

    public override BloqueioExecucao Bloqueio => _bloqueio;

    public override MarcadorDestino Marcador { get; }

    public override LeitorDadosDestino Dados { get; }

    public override DiarioExecucao Diario { get; }

    // A concessão viva é liberada antes de fechar a fonte de que a conexão dela saiu.
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await _bloqueio.LiberarAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _conexao.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// A citação de nomes do PostgreSQL (entre aspas duplas, com a aspa dobrada) e a comparação de nomes pela regra do banco
/// da conexão.
/// </summary>
internal sealed class DialetoSqlPostgreSql(RegraDosNomes nomes) : DialetoSql
{
    public override string Citar(string identificador) => CitacaoPostgreSql.Citar(identificador);

    public override CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo) => nomes.Comparar(escrito, entreAspas, doCatalogo);
}

/// <summary>A citação de nomes do PostgreSQL, para quem não tem a conexão (as mensagens do leitor).</summary>
internal static class CitacaoPostgreSql
{
    /// <summary>O identificador entre aspas duplas, com a aspa dobrada.</summary>
    internal static string Citar(string identificador)
    {
        ArgumentException.ThrowIfNullOrEmpty(identificador);
        return "\"" + identificador.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>O esquema e o nome citados, separados por ponto.</summary>
    internal static string Citar(NomeObjeto nome) => Citar(nome.Esquema) + "." + Citar(nome.Nome);
}
