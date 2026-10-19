using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a leitura do esquema do PostgreSQL faz além do que a suíte confere: a recusa da partição com detach pendente,
/// que precisa de um esquema montado no meio do teste. O resto (esquemas lidos, colunas, chaves, sequences, partição e
/// herança) a suíte confere pelo inventário.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class LeitorEsquemaPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Particao_com_detach_concurrently_interrompido_e_recusada_com_os_nomes_citados()
    {
        // A leitura pelo pai já não enxerga as linhas da partição pendente: copiada pelo pai, elas sumiriam. O nome com
        // maiúscula mostra que o comando sugerido cita os nomes.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, """CREATE TABLE loja.p (id integer) PARTITION BY LIST (id); CREATE TABLE loja."P1" PARTITION OF loja.p FOR VALUES IN (1);""", cancelamento);

            // Um snapshot antigo segura o DETACH CONCURRENTLY, que é cancelado no meio e deixa a partição pendente.
            await using (var antiga = await servidor.AbrirComoAdministradorAsync(banco, cancelamento))
            {
                await using (var comando = new NpgsqlCommand("BEGIN ISOLATION LEVEL REPEATABLE READ; SELECT count(*) FROM loja.p;", antiga))
                {
                    await comando.ExecuteNonQueryAsync(cancelamento);
                }

                // Comandos separados: o DETACH CONCURRENTLY não roda dentro de um bloco de transação.
                await using var outra = await servidor.AbrirComoAdministradorAsync(banco, cancelamento);
                await using (var limite = new NpgsqlCommand("SET statement_timeout = '1s'", outra))
                {
                    await limite.ExecuteNonQueryAsync(cancelamento);
                }

                await using var detach = new NpgsqlCommand("""ALTER TABLE loja.p DETACH PARTITION loja."P1" CONCURRENTLY""", outra);
                var erro = await Assert.ThrowsAsync<PostgresException>(() => detach.ExecuteNonQueryAsync(cancelamento));
                Assert.Equal(PostgresErrorCodes.QueryCanceled, erro.SqlState);
            }

            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            var recusa = await Assert.ThrowsAsync<IncognitoException>(() => origem.Esquema.LerAsync(cancelamento));

            Assert.Equal("INC3105", recusa.Codigo);
            Assert.Contains("""ALTER TABLE "loja"."p" DETACH PARTITION "loja"."P1" FINALIZE""", recusa.Message, StringComparison.Ordinal);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
