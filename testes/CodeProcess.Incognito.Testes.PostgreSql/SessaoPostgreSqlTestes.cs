using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A sessão que o dialeto impõe, vista de dentro do servidor por um gatilho de login (PostgreSQL 17 em diante): os
/// parâmetros chegam impostos mesmo com o banco pedindo outros, uma mudança feita no login é recusada, e o detalhe de um
/// erro do servidor não sai na exceção.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class SessaoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const int DezessetEmDiante = 170000;

    // No lugar do papel criado pelo teste.
    private const string Papel = "{papel}";

    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_parametros_impostos_ja_valem_no_login_mesmo_com_o_banco_pedindo_outros()
    {
        await ComGatilhoDeLoginAsync(
            """
            IF pg_catalog.current_setting('search_path') <> 'pg_catalog'
               OR pg_catalog.current_setting('default_transaction_read_only') <> 'on'
               OR pg_catalog.current_setting('standard_conforming_strings') <> 'on'
               OR pg_catalog.current_setting('client_encoding') <> 'UTF8'
               OR pg_catalog.current_setting('statement_timeout') <> '0'
               OR pg_catalog.current_setting('idle_session_timeout') <> '0'
               OR pg_catalog.current_setting('idle_in_transaction_session_timeout') <> '0' THEN
                RAISE EXCEPTION 'sessão sem os parâmetros impostos';
            END IF;
            """,
            async (banco, conexao, cancelamento) =>
            {
                await servidor.ExecutarAsync(
                    "postgres",
                    $"ALTER DATABASE {banco} SET search_path = public; ALTER DATABASE {banco} SET default_transaction_read_only = off; "
                    + $"ALTER DATABASE {banco} SET statement_timeout = '5s'; ALTER DATABASE {banco} SET idle_session_timeout = '5s';",
                    cancelamento);
                await using var origem = await _provedor.ConectarOrigemAsync(conexao, cancelamento);
                Assert.NotEmpty((await origem.Esquema.LerAsync(cancelamento)).Tabelas);
            });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("search_path", "public")]
    [InlineData("default_transaction_read_only", "off")]
    [InlineData("standard_conforming_strings", "off")]
    [InlineData("role", Papel)]
    public async Task Parametro_mudado_no_login_e_recusado(string parametro, string valor)
    {
        // Para trocar de papel, o leitor ganha um papel próprio do teste, sem privilégio nenhum.
        var papel = $"inc_papel_{Guid.NewGuid():N}";
        await servidor.ExecutarAsync("postgres", $"CREATE ROLE {papel} NOLOGIN; GRANT {papel} TO {ServidorPostgreSql.Leitor};", TestContext.Current.CancellationToken);
        try
        {
            await ComGatilhoDeLoginAsync(
                $"PERFORM pg_catalog.set_config('{parametro}', '{valor.Replace(Papel, papel, StringComparison.Ordinal)}', false);",
                async (_, conexao, cancelamento) =>
                {
                    var erro = await Assert.ThrowsAsync<IncognitoException>(() => _provedor.ConectarOrigemAsync(conexao, cancelamento));
                    Assert.Equal("INC3104", erro.Codigo);
                });
        }
        finally
        {
            await servidor.ExecutarAsync("postgres", $"DROP ROLE {papel}", CancellationToken.None);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_detalhe_de_um_erro_do_servidor_nao_sai_na_excecao_mesmo_se_a_string_o_pedir()
    {
        await ComGatilhoDeLoginAsync(
            $"RAISE EXCEPTION 'login recusado' USING DETAIL = '{ServidorPostgreSql.Sentinela}-detalhe';",
            async (_, conexao, cancelamento) =>
            {
                var comDetalhe = new NpgsqlConnectionStringBuilder(conexao.Revelar()) { IncludeErrorDetail = true };
                var erro = await Assert.ThrowsAsync<PostgresException>(() => _provedor.ConectarOrigemAsync(new SegredoConexao(comDetalhe.ConnectionString), cancelamento));

                Assert.Equal("login recusado", erro.MessageText);
                Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.Detail ?? "", StringComparison.Ordinal);
            });
    }

    // Clona o modelo, cria um gatilho de login que roda o corpo só para o leitor e entrega o banco e a string do leitor.
    private async Task ComGatilhoDeLoginAsync(string corpo, Func<string, SegredoConexao, CancellationToken, Task> teste)
    {
        Assert.SkipWhen(servidor.Versao < DezessetEmDiante, "Gatilho de login só existe do PostgreSQL 17 em diante.");
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE FUNCTION public.ao_entrar() RETURNS event_trigger LANGUAGE plpgsql AS $corpo$
                BEGIN
                    IF session_user = '{ServidorPostgreSql.Leitor}' THEN
                        {corpo}
                    END IF;
                END
                $corpo$;
                CREATE EVENT TRIGGER ao_entrar ON login EXECUTE FUNCTION public.ao_entrar();
                """,
                cancelamento);
            await teste(banco, new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
