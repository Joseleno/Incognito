using System.Globalization;
using Npgsql;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// O banco (<c>current_database()</c>), o servidor (<c>inet_server_addr()</c> e a porta), a linhagem (o
/// <c>system_identifier</c> do cluster e o oid do banco) e o transporte (o modo de TLS da conexão, rebaixado se o
/// <c>pg_stat_ssl</c> diz que a sessão está em claro). O <c>pg_control_system()</c> é executável por qualquer usuário no
/// PostgreSQL; um serviço gerenciado que o restrinja deixa a linhagem só com o oid. O marcador sai do comentário do banco
/// (<see cref="FormatoDoMarcador"/>), sem que o texto de um comentário alheio chegue ao processo.
/// </summary>
internal sealed class IdentidadePostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : IdentidadeBanco
{
    private const string Consulta = $"""
        SELECT pg_catalog.current_database(),
               pg_catalog.host(pg_catalog.inet_server_addr()),
               pg_catalog.inet_server_port(),
               d.oid,
               pg_catalog.has_function_privilege('pg_catalog.pg_control_system()', 'EXECUTE'),
               (SELECT s.ssl FROM pg_catalog.pg_stat_ssl s WHERE s.pid = pg_catalog.pg_backend_pid()),
               {FormatoDoMarcador.Colunas}
        FROM pg_catalog.pg_database d
        WHERE d.datname = pg_catalog.current_database()
        """;

    private const string ConsultaSistema = "SELECT system_identifier FROM pg_catalog.pg_control_system()";

    public override async Task<IdentidadeLida> LerAsync(CancellationToken cancelamento)
    {
        lancarSeDescartado();
        var transacao = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            string banco;
            string servidor;
            bool podeLerSistema;
            bool? cifrada;
            MarcadorLido marcador;
            var linhagem = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var comando = new NpgsqlCommand(Consulta, conexao.Conexao))
            {
                await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
                await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
                banco = leitor.GetString(0);

                // Por socket Unix não há endereço nem porta; o IPv6 vai entre colchetes, como numa URL.
                var endereco = leitor.IsDBNull(1) ? null : leitor.GetString(1);
                servidor = endereco is null
                    ? ""
                    : $"{(endereco.Contains(':', StringComparison.Ordinal) ? $"[{endereco}]" : endereco)}:{leitor.GetInt32(2).ToString(CultureInfo.InvariantCulture)}";
                linhagem["oid_do_banco"] = leitor.GetFieldValue<uint>(3).ToString(CultureInfo.InvariantCulture);
                podeLerSistema = leitor.GetBoolean(4);
                cifrada = leitor.IsDBNull(5) ? null : leitor.GetBoolean(5);
                marcador = FormatoDoMarcador.Ler(leitor, 6);
            }

            // Conferido antes: uma recusa abortaria a transação.
            if (podeLerSistema)
            {
                await using var comando = new NpgsqlCommand(ConsultaSistema, conexao.Conexao);
                if (await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is long sistema)
                {
                    linhagem["system_identifier"] = sistema.ToString(CultureInfo.InvariantCulture);
                }
            }

            return new IdentidadeLida { Banco = banco, Servidor = servidor, Linhagem = linhagem, Transporte = conexao.Transporte(cifrada), Marcador = marcador };
        }
    }
}
