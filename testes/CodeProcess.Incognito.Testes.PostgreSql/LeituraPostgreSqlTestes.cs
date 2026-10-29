using System.Diagnostics;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a sessão de leitura do PostgreSQL faz além do que a suíte confere: os leitores importam o instante exportado (nunca
/// tiram o próprio), a guarda pega o <c>TRUNCATE</c> que passou depois da queda do coordenador, o DDL na fila derruba a
/// leitura na hora sem inutilizar o leitor, os erros da abertura saem com o código e fecham o que já abriu, e nenhuma
/// mensagem traz o sentinela, a senha ou o identificador do instante.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed partial class LeituraPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const int DezessetEmDiante = 170000;

    private static readonly ReferenciaColuna _lojas = new(new NomeObjeto("loja", "lojas"), "id");
    private static readonly ReferenciaColuna _pessoa = new(new NomeObjeto("loja", "pessoa"), "id");

    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_leitores_importam_o_instante_exportado_e_nunca_tiram_o_proprio()
    {
        // Uma loja confirmada entre o export e a primeira importação: um leitor que tirasse o próprio instante a veria.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            ((OrigemPostgreSql)origem).Sessoes.DepoisDoExport = ct => servidor.ExecutarAsync(banco, "INSERT INTO loja.lojas (id) VALUES (5555)", ct);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(3, _lojas.Tabela), cancelamento);

            foreach (var leitor in sessao.Leitores)
            {
                Assert.Equal(91L, await leitor.LerMaiorValorAsync(_lojas, cancelamento));
            }

            Assert.Equal(5555L, await origem.Dados.LerMaiorValorAsync(_lojas, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_queda_do_coordenador_nunca_deixa_a_leitura_vazia_em_silencio()
    {
        // Sem o coordenador, as travas somem e o TRUNCATE passa; o leitor, que ainda não tinha lido a tabela, a leria vazia
        // pelo instante. A guarda vê o arquivo trocado e falha.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(2, _lojas.Tabela), cancelamento);
            var coordenador = ((SessaoLeituraPostgreSql)sessao).Conexoes.First().ProcessID;
            await servidor.ExecutarAsync(banco, $"SELECT pg_catalog.pg_terminate_backend({coordenador}, 5000)", cancelamento);
            await servidor.ExecutarAsync(banco, "SET lock_timeout = '5s'; TRUNCATE loja.lojas CASCADE", cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));

            Assert.Equal("INC3129", falha.Codigo);
            await Assert.ThrowsAnyAsync<Exception>(() => sessao.LerIdadeAsync(cancelamento));
            ConferirSigilo(falha);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_ddl_na_fila_derruba_a_leitura_na_hora_e_o_leitor_segue()
    {
        // O TRUNCATE espera o coordenador; o leitor que pedisse a trava esperaria atrás dele, e ele espera a sessão.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            var sessao = await origem.AbrirLeituraAsync(Pedido(1, _lojas.Tabela), cancelamento);
            Task truncar;
            try
            {
                truncar = servidor.ExecutarAsync(banco, "SET lock_timeout = '30s'; TRUNCATE loja.lojas CASCADE", cancelamento);
                await EsperarAsync(
                    async () => (long)(await servidor.ConsultarAsync(banco, "SELECT pg_catalog.count(*) FROM pg_catalog.pg_locks l WHERE NOT l.granted AND l.relation = 'loja.lojas'::pg_catalog.regclass", cancelamento))! > 0,
                    cancelamento);

                var relogio = Stopwatch.StartNew();
                var falha = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));

                Assert.Equal("INC3129", falha.Codigo);
                Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(3), $"A leitura esperou {relogio.Elapsed} atrás do DDL.");
                Assert.Equal(12L, await sessao.Leitores[0].LerMaiorValorAsync(_pessoa, cancelamento));
                ConferirSigilo(falha);
            }
            finally
            {
                await sessao.DisposeAsync();
            }

            await truncar;
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_presa_na_abertura_falha_com_a_tabela_e_nao_deixa_conexao()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using (var administrador = await servidor.AbrirComoAdministradorAsync(banco, cancelamento))
            {
                await using var transacao = await administrador.BeginTransactionAsync(cancelamento);
                await using (var prender = new NpgsqlCommand("LOCK TABLE loja.lojas IN ACCESS EXCLUSIVE MODE", administrador, transacao))
                {
                    await prender.ExecuteNonQueryAsync(cancelamento);
                }

                var falha = await Assert.ThrowsAsync<IncognitoException>(() => origem.AbrirLeituraAsync(Pedido(2, _lojas.Tabela), cancelamento));

                Assert.Equal("INC3124", falha.Codigo);
                Assert.Contains("loja.lojas", falha.Message, StringComparison.Ordinal);
                ConferirSigilo(falha);
            }

            await EsperarAsync(async () => await ContarAsync(banco, cancelamento) == 0, cancelamento);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particionada_travada_prende_as_particoes()
    {
        // A particionada é travada com as partições: o TRUNCATE de uma delas espera a sessão.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(1, new NomeObjeto("loja", "pedidos")), cancelamento);

            var preso = await Assert.ThrowsAsync<PostgresException>(() => servidor.ExecutarAsync(banco, "SET lock_timeout = '1s'; TRUNCATE loja.pedidos_1", cancelamento));

            Assert.Equal(PostgresErrorCodes.LockNotAvailable, preso.SqlState);
            Assert.Equal(75L, await sessao.Leitores[0].LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "pedidos"), "id"), cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_religada_na_transacao_faz_a_leitura_falhar()
    {
        // Código do usuário (uma função de saída de tipo) pode religar a segurança por linha dentro da transação; a leitura
        // seguinte falharia filtrada em silêncio, e a reconferência a recusa.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(1), cancelamento);
            var leitor = ((SessaoLeituraPostgreSql)sessao).Conexoes.Last();
            await using (var religar = new NpgsqlCommand("SELECT pg_catalog.set_config('row_security', 'on', true)", leitor))
            {
                await religar.ExecuteScalarAsync(cancelamento);
            }

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));

            Assert.Equal("INC3104", falha.Codigo);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_travada_sem_select_e_recusada()
    {
        // loja.parcial: o leitor só tem SELECT numa coluna, e a trava pede o da tabela.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);

            var recusa = await Assert.ThrowsAsync<DialetoRecusouException>(() => origem.AbrirLeituraAsync(Pedido(1, new NomeObjeto("loja", "parcial")), cancelamento));

            Assert.Equal("INC3131", recusa.Codigo);
            ConferirSigilo(recusa);
            await EsperarAsync(async () => await ContarAsync(banco, cancelamento) == 0, cancelamento);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_leitor_que_nao_conecta_fecha_os_ja_abertos()
    {
        // O limite do banco deixa a conexão de controle, o coordenador e um leitor; o segundo leitor é recusado.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await servidor.ExecutarAsync("postgres", $"ALTER DATABASE \"{banco}\" CONNECTION LIMIT 3", cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => origem.AbrirLeituraAsync(Pedido(3), cancelamento));

            Assert.Equal("INC3126", falha.Codigo);
            ConferirSigilo(falha);
            await EsperarAsync(async () => await ContarAsync(banco, cancelamento) == 0, cancelamento);
            await servidor.ExecutarAsync("postgres", $"ALTER DATABASE \"{banco}\" CONNECTION LIMIT -1", cancelamento);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Toda_conexao_da_sessao_e_so_de_leitura_sem_seguranca_por_linha_e_nomeada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.ClonarModeloAsync(cancelamento);
        var destino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var daOrigem = await ConectarAsync(origem, cancelamento);
            await using var doDestino = await _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(destino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
            foreach (var dialeto in new DialetoBanco[] { daOrigem, doDestino })
            {
                await using var sessao = await dialeto.AbrirLeituraAsync(Pedido(2), cancelamento);
                foreach (var conexao in ((SessaoLeituraPostgreSql)sessao).Conexoes)
                {
                    await using var comando = new NpgsqlCommand(
                        "SELECT pg_catalog.current_setting('transaction_read_only') || '|' || pg_catalog.current_setting('row_security') || '|' || pg_catalog.current_setting('application_name') || '|' || pg_catalog.current_setting('transaction_isolation')",
                        conexao);
                    Assert.Equal($"on|off|{ConexaoPostgreSql.NomeDaLeitura}|repeatable read", await comando.ExecuteScalarAsync(cancelamento));
                }
            }
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_transaction_timeout_do_papel_nao_derruba_a_sessao()
    {
        if (servidor.Versao < DezessetEmDiante)
        {
            return;
        }

        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await servidor.ExecutarAsync("postgres", $"ALTER ROLE {ServidorPostgreSql.Leitor} IN DATABASE \"{banco}\" SET transaction_timeout = '1s'", cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(1, _lojas.Tabela), cancelamento);
            await Task.Delay(TimeSpan.FromSeconds(2), cancelamento);

            Assert.Equal(91L, await sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));
            Assert.NotNull(await sessao.LerIdadeAsync(cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_falha_sem_o_valor_escondido_e_o_leitor_segue()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await servidor.ExecutarAsync(banco, "INSERT INTO loja.segredos (id, conteudo) VALUES (5, 'visivel'), (7310598264, 'zqSENTINELAqz')", cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(Pedido(1), cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => sessao.Leitores[0].LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "segredos"), "id"), cancelamento));

            Assert.Equal("INC3128", falha.Codigo);
            Assert.DoesNotContain("7310598264", falha.ToString(), StringComparison.Ordinal);
            ConferirSigilo(falha);
            Assert.Equal(91L, await sessao.Leitores[0].LerMaiorValorAsync(_lojas, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void Os_erros_da_leitura_sao_traduzidos_pelo_codigo_e_a_reproducao_so_na_replica()
    {
        (string SqlState, bool Replica, bool Abertura, string? Codigo)[] casos =
        [
            ("53200", false, true, "INC3125"),
            ("53300", false, true, "INC3126"),
            ("72000", false, false, "INC3130"),
            ("42501", false, false, "INC3128"),
            ("42501", false, true, null),
            ("55P03", false, false, "INC3129"),
            ("55P03", false, true, null),
            ("40001", true, false, "INC3123"),
            ("40P01", true, false, "INC3123"),
            ("57P04", true, false, "INC3123"),
            ("40001", false, false, null),
            ("42P01", true, false, null),
        ];

        foreach (var (sqlState, replica, abertura, codigo) in casos)
        {
            var erro = new PostgresException(ServidorPostgreSql.Sentinela, "ERROR", "ERROR", sqlState);
            var traduzida = ErrosDaLeitura.Traduzir(erro, replica, abertura);

            Assert.True((traduzida as IncognitoException)?.Codigo == codigo, $"{sqlState} (réplica {replica}, abertura {abertura}) saiu como {traduzida?.GetType().Name ?? "nada"}.");
            Assert.True(traduzida is null || !traduzida.ToString().Contains(ServidorPostgreSql.Sentinela, StringComparison.Ordinal), $"{sqlState} traz a mensagem do servidor.");
        }

        // A conexão perdida (o FATAL que chega com a conexão parada) é da reprodução só na réplica.
        var perdida = new NpgsqlException("A conexão caiu.");
        Assert.Equal("INC3123", Assert.IsType<IncognitoException>(ErrosDaLeitura.Traduzir(perdida, emReplica: true, naAbertura: false)).Codigo);
        Assert.Null(ErrosDaLeitura.Traduzir(perdida, emReplica: false, naAbertura: false));
        Assert.Null(ErrosDaLeitura.Traduzir(new OperationCanceledException(), emReplica: true, naAbertura: false));
    }

    private static PedidoLeitura Pedido(int leitores, params NomeObjeto[] travadas) =>
        new() { Leitores = leitores, Modo = ModoConsistencia.MelhorEsforco, Travadas = travadas };

    // Nenhuma falha traz o sentinela, a senha do leitor ou um identificador de instante.
    private void ConferirSigilo(Exception falha)
    {
        var texto = falha.ToString();
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(ServidorPostgreSql.SentinelaCurto, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(servidor.SenhaLeitor, texto, StringComparison.Ordinal);
        Assert.DoesNotMatch(Instante(), texto);
    }

    private async Task NoBancoAsync(Func<string, CancellationToken, Task> teste)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await teste(banco, cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    private Task<DialetoOrigem> ConectarAsync(string banco, CancellationToken cancelamento) =>
        _provedor.ConectarOrigemAsync(new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);

    private async Task<long> ContarAsync(string banco, CancellationToken cancelamento) =>
        (long)(await servidor.ConsultarAsync(
            banco,
            $"SELECT pg_catalog.count(*) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.application_name = '{ConexaoPostgreSql.NomeDaLeitura}'",
            cancelamento))!;

    // Espera a condição até um prazo: a saída das conexões e a fila de travas são assíncronas no servidor.
    private static async Task EsperarAsync(Func<Task<bool>> condicao, CancellationToken cancelamento)
    {
        var prazo = Stopwatch.StartNew();
        while (!await condicao())
        {
            Assert.True(prazo.Elapsed < TimeSpan.FromSeconds(10), "A condição não chegou em 10 s.");
            await Task.Delay(100, cancelamento);
        }
    }

    [GeneratedRegex("[0-9A-F]{8}-[0-9A-F]{8}-[0-9]+")]
    private static partial Regex Instante();
}
