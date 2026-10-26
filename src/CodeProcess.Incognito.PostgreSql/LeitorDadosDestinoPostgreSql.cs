using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A leitura de existência de linhas no destino, a segunda consulta do dialeto sobre uma tabela do usuário (teste de
/// arquitetura): <c>SELECT EXISTS (SELECT FROM [ONLY] tabela)</c>, que nunca traz um valor. Numa transação só de leitura em
/// <c>REPEATABLE READ</c>, com <c>row_security = off</c> (a segurança por linha faz a consulta falhar, nunca esconder
/// linhas) e <c>lock_timeout</c> de 5 s (um <c>TRUNCATE</c> alheio não a prende).
/// </summary>
/// <remarks>
/// A tabela é conferida antes pelo catálogo (comum ou particionada). A particionada é lida pela raiz (com as partições);
/// as outras com <c>ONLY</c>, sem as herdeiras, como a cópia lê.
/// </remarks>
internal sealed class LeitorDadosDestinoPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : LeitorDadosDestino
{
    private const string ConsultaTabela = """
        SELECT c.relkind = 'p'
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relname = $2 AND c.relkind IN ('r', 'p')
        """;

    public override async Task<bool> TemLinhasAsync(NomeObjeto tabela, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        lancarSeDescartado();
        var transacao = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            await using (var ajustar = new NpgsqlCommand("SET LOCAL row_security = off; SET LOCAL lock_timeout = '5s'", conexao.Conexao))
            {
                await ajustar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
            }

            bool particionada;
            await using (var conferir = new NpgsqlCommand(ConsultaTabela, conexao.Conexao))
            {
                conferir.Parameters.Add(new NpgsqlParameter { Value = tabela.Esquema, NpgsqlDbType = NpgsqlDbType.Text });
                conferir.Parameters.Add(new NpgsqlParameter { Value = tabela.Nome, NpgsqlDbType = NpgsqlDbType.Text });
                await using var leitor = await conferir.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
                if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
                {
                    throw new DialetoRecusouException(
                        CodigosPostgreSql.TabelaDoDestinoInvalida,
                        "A existência de linhas só se lê de uma tabela comum ou particionada do destino: a tabela não existe, ou é de outra forma.");
                }

                particionada = leitor.GetBoolean(0);
            }

            var citada = (particionada ? "" : "ONLY ") + CitacaoPostgreSql.Citar(tabela);
            await using var comando = new NpgsqlCommand($"SELECT EXISTS (SELECT FROM {citada})", conexao.Conexao);
            try
            {
                return await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is true;
            }
            catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new DialetoRecusouException(
                    CodigosPostgreSql.LeituraDoDestinoRecusada,
                    "O login do destino não lê a tabela, ou a segurança por linha vale para ele: a existência de linhas não pôde ser conferida.");
            }
        }
    }
}
