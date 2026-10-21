using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// Por onde um texto da string de conexão ou do banco chega à exceção crua do driver: o usuário, o banco, um gatilho de
/// login e o servidor. O PostgreSQL nunca ecoa a senha, e um teste só com o sentinela na senha passaria por acaso; estes
/// mostram os canais que o envoltório do motor fecha (ele nunca lê a mensagem, os dados nem o texto da exceção do driver).
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class CanaisDoDriverPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const int DezesseteEmDiante = 170000;

    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_usuario_e_o_banco_da_string_voltam_na_excecao_do_driver()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var sentinela = Sentinela();
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            // O usuário inexistente: 28P01 com senha, ou 28000 sem ela, conforme o pg_hba; os dois trazem o nome.
            var usuario = await Assert.ThrowsAsync<PostgresException>(() => _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, sentinela, ServidorPostgreSql.NovaSenha())), cancelamento));
            Assert.Contains(usuario.SqlState, new[] { PostgresErrorCodes.InvalidPassword, PostgresErrorCodes.InvalidAuthorizationSpecification });
            Assert.Contains(sentinela, usuario.ToString(), StringComparison.OrdinalIgnoreCase);

            var inexistente = await Assert.ThrowsAsync<PostgresException>(() => _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(sentinela, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento));
            Assert.Equal(PostgresErrorCodes.InvalidCatalogName, inexistente.SqlState);
            Assert.Contains(sentinela, inexistente.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_gatilho_de_login_poe_texto_do_banco_na_mensagem_na_dica_e_nos_dados()
    {
        // O Include Error Detail desligado redige só o detalhe: a mensagem, a dica e o contexto passam, também nos dados.
        Assert.SkipWhen(servidor.Versao < DezesseteEmDiante, "Gatilho de login só existe do PostgreSQL 17 em diante.");
        var cancelamento = TestContext.Current.CancellationToken;
        var sentinela = Sentinela();
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE FUNCTION public.ao_entrar() RETURNS event_trigger LANGUAGE plpgsql AS $corpo$
                BEGIN
                    IF session_user = '{ServidorPostgreSql.Leitor}' THEN
                        RAISE EXCEPTION 'recusado %', '{sentinela}' USING HINT = '{sentinela}-dica';
                    END IF;
                END
                $corpo$;
                CREATE EVENT TRIGGER ao_entrar ON login EXECUTE FUNCTION public.ao_entrar();
                """,
                cancelamento);

            var erro = await Assert.ThrowsAsync<PostgresException>(() => _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento));

            Assert.Contains(sentinela, erro.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(sentinela, erro.Hint ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Contains(erro.Data.Values.Cast<object?>(), valor => valor?.ToString()?.Contains(sentinela, StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_servidor_que_nao_responde_volta_com_o_endereco_na_excecao_do_driver()
    {
        // Sem texto do usuário: o driver põe o endereço resolvido, e um nome que não resolve nem aparece.
        var erro = await Assert.ThrowsAnyAsync<NpgsqlException>(() => _provedor.ConectarOrigemAsync(
            new SegredoConexao($"Host=127.0.0.1;Port=1;Database=loja;Username=u;Password={ServidorPostgreSql.NovaSenha()};Timeout=3"),
            TestContext.Current.CancellationToken));

        Assert.Contains("127.0.0.1:1", erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Um nome válido de papel e de banco, sem forma de CPF.
    private static string Sentinela() => "canal_" + Guid.NewGuid().ToString("N")[..12] + "_q";
}
