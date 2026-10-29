using System.Diagnostics;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A leitura numa réplica em espera de verdade (lento, além de banco: fora do pull request, no push da develop e da main). A réplica é
/// criada dentro do contêiner por <c>pg_basebackup</c>, com <c>max_standby_streaming_delay</c> curto: a sessão abre e lê
/// no instante exportado na réplica; um <c>TRUNCATE</c> na primária, reproduzido contra as travas, derruba as sessões que
/// travaram a tabela, e a leitura seguinte sai com o código da reprodução (a forma real em que o Npgsql entrega o FATAL da
/// conexão parada); o leitor que ainda não tinha lido a tabela acha o arquivo trocado pela guarda, nunca a tabela vazia.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly), só pela versão pedida.</param>
public sealed class ReplicaPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const string Lento = "Lento";
    private const string Leitor = "leitor_replica";

    private static readonly ReferenciaColuna _lojas = new(new NomeObjeto("loja", "lojas"), "id");

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [Trait("Categoria", Lento)]
    public async Task A_leitura_na_replica_le_o_instante_e_a_reproducao_a_derruba_com_o_codigo()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var senha = ServidorPostgreSql.NovaSenha();
        await using var conteiner = ServidorPostgreSql.ConteinerComReplica(servidor.Versao / 10000, senha);
        await conteiner.StartAsync(cancelamento);
        var primaria = new NpgsqlConnectionStringBuilder(conteiner.GetConnectionString()) { Pooling = false }.ConnectionString;
        var senhaDoLeitor = ServidorPostgreSql.NovaSenha();
        await ExecutarAsync(
            primaria,
            $"""
            CREATE ROLE {Leitor} LOGIN PASSWORD '{senhaDoLeitor}';
            CREATE SCHEMA loja;
            CREATE TABLE loja.lojas (id integer PRIMARY KEY);
            INSERT INTO loja.lojas SELECT pg_catalog.generate_series(1, 91);
            GRANT USAGE ON SCHEMA loja TO {Leitor};
            GRANT SELECT ON loja.lojas TO {Leitor};
            """,
            cancelamento);

        var criada = await conteiner.ExecAsync(
            [
                "gosu", "postgres", "sh", "-c",
                "pg_basebackup -h /var/run/postgresql -U postgres -D /tmp/espelho -R -X stream -c fast"
                    + $" && printf 'port = {ServidorPostgreSql.PortaDaReplica}\\nmax_standby_streaming_delay = 2s\\nhot_standby_feedback = off\\n' >> /tmp/espelho/postgresql.auto.conf"
                    + " && pg_ctl -D /tmp/espelho -l /tmp/espelho.log -w start",
            ],
            cancelamento);
        Assert.True(criada.ExitCode == 0, "A réplica não subiu.");

        var porta = conteiner.GetMappedPublicPort(ServidorPostgreSql.PortaDaReplica);
        var naReplica = new NpgsqlConnectionStringBuilder(primaria) { Port = porta }.ConnectionString;
        await EsperarAsync(async () => await ContarAsync(naReplica, cancelamento) == 91, cancelamento);

        var doLeitor = new NpgsqlConnectionStringBuilder(naReplica) { Username = Leitor, Password = senhaDoLeitor }.ConnectionString;
        await using var origem = await new ProvedorPostgreSql().ConectarOrigemAsync(new SegredoConexao(doLeitor), cancelamento);
        var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 2, Modo = ModoConsistencia.Exigir, Travadas = [_lojas.Tabela] }, cancelamento);
        try
        {
            Assert.True(((SessaoLeituraPostgreSql)sessao).EmReplica);
            Assert.Equal(91L, await sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));
            Assert.NotNull(await sessao.LerIdadeAsync(cancelamento));

            // O TRUNCATE na primária espera, na reprodução, as travas da sessão; passado o atraso, a réplica derruba quem as
            // segura (o coordenador e o primeiro leitor) e o reproduz.
            await ExecutarAsync(primaria, "TRUNCATE loja.lojas", cancelamento);
            await EsperarAsync(async () => await ContarAsync(naReplica, cancelamento) == 0, cancelamento);

            var derrubado = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));
            var trocada = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[1].LerMaiorValorAsync(_lojas, cancelamento));

            Assert.Equal(("INC3123", "INC3129"), (derrubado.Codigo, trocada.Codigo));
            Assert.DoesNotContain(senhaDoLeitor, derrubado.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await sessao.DisposeAsync();
        }
    }

    private static async Task ExecutarAsync(string conexao, string sql, CancellationToken cancelamento)
    {
        await using var aberta = new NpgsqlConnection(conexao);
        await aberta.OpenAsync(cancelamento);
        await using var comando = new NpgsqlCommand(sql, aberta);
        await comando.ExecuteNonQueryAsync(cancelamento);
    }

    // Na réplica, como administrador; a tabela ainda não reproduzida conta como não pronta.
    private static async Task<long> ContarAsync(string conexao, CancellationToken cancelamento)
    {
        try
        {
            await using var aberta = new NpgsqlConnection(conexao);
            await aberta.OpenAsync(cancelamento);
            await using var comando = new NpgsqlCommand("SELECT pg_catalog.count(*) FROM loja.lojas", aberta);
            return (long)(await comando.ExecuteScalarAsync(cancelamento))!;
        }
        catch (PostgresException erro) when (erro.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            return -1;
        }
    }

    // A reprodução e a subida da réplica são assíncronas: espera até um prazo.
    private static async Task EsperarAsync(Func<Task<bool>> condicao, CancellationToken cancelamento)
    {
        var prazo = Stopwatch.StartNew();
        while (!await condicao())
        {
            Assert.True(prazo.Elapsed < TimeSpan.FromSeconds(60), "A réplica não chegou ao estado esperado em 60 s.");
            await Task.Delay(250, cancelamento);
        }
    }
}
