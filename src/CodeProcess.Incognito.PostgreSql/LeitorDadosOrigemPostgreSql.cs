using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A leitura agregada da origem: o maior valor de uma coluna inteira, a única consulta do dialeto que lê linhas de uma
/// tabela do usuário (teste de arquitetura). Numa transação só de leitura em <c>REPEATABLE READ</c>, com
/// <c>row_security = off</c>: se a segurança por linha valer para o login, a consulta falha em vez de devolver o maior
/// valor das linhas visíveis.
/// </summary>
/// <remarks>
/// A tabela e a coluna são conferidas antes pelo catálogo (tabela comum ou particionada, coluna <c>smallint</c>,
/// <c>integer</c> ou <c>bigint</c>, também por domínio): nenhum cast de outro tipo chega a falhar com o valor na
/// mensagem do servidor. A particionada é lida pela raiz (com as partições); as outras com <c>ONLY</c>, sem as herdeiras,
/// como a cópia lê. O valor nunca entra em exceção nem mensagem do dialeto.
/// </remarks>
internal sealed class LeitorDadosOrigemPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : LeitorDadosOrigem
{
    // O tipo base pela cadeia de domínios, a forma da tabela e o oid; nada se lê se a tabela ou a coluna não existem.
    internal const string ConsultaColuna = """
        WITH RECURSIVE coluna AS (
            SELECT c.oid, c.relkind, a.atttypid
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid
            WHERE n.nspname = $1 AND c.relname = $2 AND a.attname = $3 AND a.attnum > 0 AND NOT a.attisdropped
              AND c.relkind IN ('r', 'p')),
        cadeia AS (
            SELECT t.oid, t.typbasetype
            FROM pg_catalog.pg_type t
            JOIN coluna ON t.oid = coluna.atttypid
            UNION ALL
            SELECT t.oid, t.typbasetype
            FROM pg_catalog.pg_type t
            JOIN cadeia ON t.oid = cadeia.typbasetype)
        SELECT coluna.relkind = 'p', (SELECT cadeia.oid FROM cadeia WHERE cadeia.typbasetype = 0) IN (20, 21, 23), coluna.oid
        FROM coluna
        """;

    public override async Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(coluna);
        lancarSeDescartado();
        var transacao = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            await using (var desligar = new NpgsqlCommand("SET LOCAL row_security = off", conexao.Conexao))
            {
                await desligar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
            }

            bool particionada;
            await using (var conferir = new NpgsqlCommand(ConsultaColuna, conexao.Conexao))
            {
                conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Tabela.Esquema, NpgsqlDbType = NpgsqlDbType.Text });
                conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Tabela.Nome, NpgsqlDbType = NpgsqlDbType.Text });
                conferir.Parameters.Add(new NpgsqlParameter { Value = coluna.Coluna, NpgsqlDbType = NpgsqlDbType.Text });
                await using var leitor = await conferir.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
                if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false) || leitor.IsDBNull(1) || !leitor.GetBoolean(1))
                {
                    throw new DialetoRecusouException(
                        CodigosPostgreSql.ColunaNaoInteira,
                        "O maior valor só se lê de uma coluna smallint, integer ou bigint de uma tabela da origem: a tabela ou a coluna não existe, ou é de outro tipo.");
                }

                particionada = leitor.GetBoolean(0);
            }

            var tabela = (particionada ? "" : "ONLY ") + CitacaoPostgreSql.Citar(coluna.Tabela);
            var citada = CitacaoPostgreSql.Citar(coluna.Coluna);
            await using var comando = new NpgsqlCommand($"SELECT pg_catalog.max({citada})::pg_catalog.int8 FROM {tabela}", conexao.Conexao);
            var maior = await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false);
            return maior is long valor ? valor : null;
        }
    }
}
