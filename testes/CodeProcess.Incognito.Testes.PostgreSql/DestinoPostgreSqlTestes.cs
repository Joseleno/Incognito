using System.Diagnostics;
using System.Globalization;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que o bloqueio, o marcador e a existência de linhas do destino PostgreSQL fazem além do que a suíte confere: a chave de
/// dois inteiros, a conexão dedicada sem pool e com os keepalives do servidor, a queda percebida pelo temporizador, nada
/// rastreado pelo Npgsql, o marcador gravado no formato exato, a recusa sem a mensagem do servidor.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class DestinoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_bloqueio_usa_a_chave_de_dois_inteiros_numa_conexao_dedicada_sem_pool()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);
            var controle = await Consultar<int>(banco, "SELECT count(*)::int FROM pg_catalog.pg_stat_activity WHERE datname = current_database() AND usename = 'incognito_dono'");

            var concessao = await destino.Bloqueio.TentarAdquirirAsync(cancelamento);
            Assert.NotNull(concessao);
            int pid;
            await using (concessao)
            {
                pid = await Consultar<int>(banco, Chave("SELECT l.pid FROM pg_catalog.pg_locks l"));
                Assert.Equal(controle + 1, await Consultar<int>(banco, "SELECT count(*)::int FROM pg_catalog.pg_stat_activity WHERE datname = current_database() AND usename = 'incognito_dono'"));
                // Os keepalives do servidor, na sessão dedicada (em segundos, como o pg_settings os guarda).
                await using (var mostrar = new NpgsqlCommand(
                    "SELECT string_agg(setting, '|' ORDER BY name) FROM pg_catalog.pg_settings WHERE name IN ('tcp_keepalives_count', 'tcp_keepalives_idle', 'tcp_keepalives_interval')",
                    ((ConcessaoPostgreSql)concessao).Conexao))
                {
                    Assert.Equal("6|60|10", await mostrar.ExecuteScalarAsync(cancelamento));
                }

                await concessao.ConferirAsync(cancelamento);
            }

            // Liberado de forma explícita e a conexão física fechada: o lock e a sessão somem.
            Assert.Equal(0, await Consultar<int>(banco, Chave("SELECT count(*)::int FROM pg_catalog.pg_locks l")));
            Assert.Equal(0, await Consultar<int>(banco, string.Create(CultureInfo.InvariantCulture, $"SELECT count(*)::int FROM pg_catalog.pg_stat_activity WHERE pid = {pid}")));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_queda_da_conexao_do_bloqueio_e_percebida_sem_batimento_pedido()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);
            ((BloqueioPostgreSql)destino.Bloqueio).Intervalo = TimeSpan.FromMilliseconds(200);
            await using var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!;
            var perdido = new TaskCompletionSource();
            await using var registro = concessao.Perdido.Register(() => perdido.TrySetResult());

            await servidor.ExecutarAsync(banco, Chave("SELECT pg_catalog.pg_terminate_backend(l.pid, 5000) FROM pg_catalog.pg_locks l"), cancelamento);

            await perdido.Task.WaitAsync(TimeSpan.FromSeconds(15), cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_batimento_confere_o_lock_e_nao_so_a_conexao()
    {
        // O lock solto pela própria sessão, com a conexão viva: o batimento acusa a perda, e o descarte não lança (o unlock
        // devolve falso).
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);
            var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!;
            await using (var soltar = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_unlock_all()", ((ConcessaoPostgreSql)concessao).Conexao))
            {
                await soltar.ExecuteNonQueryAsync(cancelamento);
            }

            var perda = await Assert.ThrowsAsync<IncognitoException>(() => concessao.ConferirAsync(cancelamento));
            Assert.Equal("INC3110", perda.Codigo);
            Assert.True(concessao.Perdido.IsCancellationRequested);
            await concessao.DisposeAsync();
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_bloqueio_nao_e_rastreado_pelo_Npgsql()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        // A fonte é global: só contam as atividades deste banco (os outros testes rodam em paralelo, com o administrador).
        var atividades = new List<string>();
        using var ouvinte = new ActivityListener
        {
            ShouldListenTo = fonte => fonte.Name.StartsWith("Npgsql", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = atividade =>
            {
                if (atividade.TagObjects.Any(etiqueta => string.Equals(etiqueta.Value?.ToString(), banco, StringComparison.Ordinal)))
                {
                    lock (atividades)
                    {
                        atividades.Add(atividade.DisplayName);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(ouvinte);
        try
        {
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);
            await using (var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!)
            {
                await concessao.ConferirAsync(cancelamento);
            }

            Assert.Empty(atividades);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_marcador_e_gravado_no_formato_exato_e_so_pelo_dono_do_banco()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            var identificador = Guid.NewGuid();
            var gravadoEm = new DateTimeOffset(2026, 10, 24, 12, 34, 56, TimeSpan.Zero);

            // O leitor conecta no destino e não é o dono: a recusa sai com o código, sem a mensagem do servidor.
            await using (var alheio = await ConectarAsync(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor))
            {
                var recusa = await Assert.ThrowsAsync<DialetoRecusouException>(() => alheio.Marcador.GravarAsync(identificador, gravadoEm, cancelamento));
                Assert.Equal("INC3109", recusa.Codigo);
                Assert.DoesNotContain("must be owner", recusa.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Null(recusa.InnerException);
            }

            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);
            await Assert.ThrowsAsync<ArgumentException>(() => destino.Marcador.GravarAsync(identificador, gravadoEm.AddMilliseconds(1), cancelamento));
            await Assert.ThrowsAsync<ArgumentException>(() => destino.Marcador.GravarAsync(identificador, gravadoEm.ToOffset(TimeSpan.FromHours(-3)), cancelamento));
            await destino.Marcador.GravarAsync(identificador, gravadoEm, cancelamento);

            var gravado = await Consultar<string>(banco, "SELECT pg_catalog.shobj_description(d.oid, 'pg_database') FROM pg_catalog.pg_database d WHERE d.datname = current_database()");
            Assert.Equal($"incognito-destino:1:{identificador:D}:2026-10-24T12:34:56Z", gravado);
            Assert.Equal(FormatoDoMarcador.Tamanho, gravado.Length);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Theory]
    [InlineData("incognito-destino:1:{0}:2026-10-24T12:34:56Z", SituacaoMarcador.Valido)]
    [InlineData("incognito-destino:1:{0}:2026-10-24T12:34:5Z", SituacaoMarcador.Invalido)]
    [InlineData("incognito-destino:2:{0}:2026-10-24T12:34:56Z", SituacaoMarcador.Invalido)]
    [InlineData("incognito-destino:1:{1}:2026-10-24T12:34:56Z", SituacaoMarcador.Invalido)]
    [InlineData("incognito-destino:1:00000000-0000-0000-0000-000000000000:2026-10-24T12:34:56Z", SituacaoMarcador.Invalido)]
    [InlineData("incognito-destino:1:{0}:2026-13-24T12:34:56Z", SituacaoMarcador.Invalido)]
    [InlineData("incognito-destino:1:{0}:2026-10-24T12:34:56+00", SituacaoMarcador.Invalido)]
    [InlineData("Incognito-destino:1:{0}:2026-10-24T12:34:56Z", SituacaoMarcador.Alheio)]
    [InlineData(" incognito-destino:1:{0}:2026-10-24T12:34:56Z", SituacaoMarcador.Alheio)]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task So_o_texto_canonico_e_marcador_valido(string formato, SituacaoMarcador esperada)
    {
        // {0} é o identificador em minúsculas; {1}, em maiúsculas (não é o que o Incognito grava).
        var cancelamento = TestContext.Current.CancellationToken;
        var identificador = Guid.NewGuid();
        var texto = string.Format(CultureInfo.InvariantCulture, formato, identificador.ToString("D"), identificador.ToString("D").ToUpperInvariant());
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, $"COMMENT ON DATABASE \"{banco}\" IS '{texto}'", cancelamento);
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);

            var marcador = (await destino.Identidade.LerAsync(cancelamento)).Marcador!;

            Assert.Equal(esperada, marcador.Situacao);
            Assert.Equal(esperada == SituacaoMarcador.Valido ? identificador : null, marcador.Identificador);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_que_o_dono_nao_le_e_recusada_sem_a_mensagem_do_servidor()
    {
        // O esquema alheio é de um terceiro papel, sem USAGE para o dono.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await ConectarAsync(banco, ServidorPostgreSql.Dono, servidor.SenhaDono);

            var semLeitura = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Dados.TemLinhasAsync(new NomeObjeto("alheio", "do_dono"), cancelamento));
            var inexistente = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Dados.TemLinhasAsync(new NomeObjeto("loja", "nao_existe"), cancelamento));
            var visao = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Dados.TemLinhasAsync(new NomeObjeto("loja", "mv_clientes"), cancelamento));

            Assert.Equal(("INC3115", "INC3111", "INC3111"), (semLeitura.Codigo, inexistente.Codigo, visao.Codigo));
            Assert.DoesNotContain("permission denied", semLeitura.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    // A consulta sobre o lock do Incognito neste banco, pela chave de dois inteiros (objsubid = 2).
    private static string Chave(string inicio) => string.Create(
        CultureInfo.InvariantCulture,
        $"{inicio} WHERE l.locktype = 'advisory' AND l.classid = {BloqueioPostgreSql.Classe} AND l.objid = {BloqueioPostgreSql.Execucao} AND l.objsubid = 2 AND l.database = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database())");

    private async Task<DialetoDestino> ConectarAsync(string banco, string usuario, string senha) =>
        await _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, usuario, senha)), TestContext.Current.CancellationToken);

    private async Task<T> Consultar<T>(string banco, string sql) => (T)(await servidor.ConsultarAsync(banco, sql, TestContext.Current.CancellationToken))!;
}
