using System.Globalization;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O diário no PostgreSQL, além da suíte: a chave de particionada com órfão continua no diário (do 15 ao 17, a recriação
/// valida direto e a chave fica fora); o diário de outro dono, com escrita de um membro do login, com outro objeto no
/// esquema ou com concessão dada pelos privilégios padrão é recusado ou corrigido; a definição guardada só é executada na
/// forma de uma chave estrangeira; e o destino confere de segundo em segundo se o cliente ainda está lá.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class DiarioPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_de_particionada_com_orfao_continua_no_diario()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarBancoDoDiarioAsync(cancelamento);
        try
        {
            // A loja 2 some sem a conferência das chaves: a linha (2, 1) dos pedidos fica órfã.
            await servidor.ExecutarAsync(banco, "SET session_replication_role = replica; DELETE FROM loja.lojas WHERE id = 2; RESET session_replication_role", cancelamento);
            await using var destino = await _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
            await using var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!;
            var tabela = new NomeObjeto("loja", "pedidos");
            var execucao = Guid.NewGuid();
            await destino.Diario.CriarAsync(cancelamento);
            await destino.Diario.IniciarAsync(execucao, Guid.NewGuid(), DateTimeOffset.UnixEpoch, cancelamento);
            await destino.Diario.RemoverChaveAsync(execucao, tabela, "fk_pedidos_lojas", cancelamento);

            var recriada = await destino.Diario.RecriarChaveAsync(Assert.Single((await destino.Diario.LerAsync(cancelamento)).Pendentes), cancelamento);
            var depois = servidor.Versao >= 180000
                ? await destino.Diario.ValidarChaveAsync(Assert.Single((await destino.Diario.LerAsync(cancelamento)).Pendentes), cancelamento)
                : recriada;
            var pendente = Assert.Single((await destino.Diario.LerAsync(cancelamento)).Pendentes);

            // Do 15 ao 17, a chave não volta (a validação direta achou o órfão); no 18, volta sem validação. Nos dois, a
            // entrada fica, e nada foi exceção.
            var esperada = servidor.Versao >= 180000 ? SituacaoEntrada.SemValidacao : SituacaoEntrada.Removida;
            Assert.Equal((esperada, esperada, esperada), (recriada!.Value, depois!.Value, pendente.Situacao));
            Assert.Equal(servidor.Versao >= 180000, await ChaveExisteAsync(banco, "loja.pedidos", "fk_pedidos_lojas"));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_diario_com_escrita_de_um_membro_do_login_ou_com_outro_objeto_e_recusado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var dono = await DonoProprioAsync();
        await using (var destino = await dono.ConectarAsync(_provedor))
        await using (var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!)
        {
            await destino.Diario.CriarAsync(cancelamento);
            Assert.True((await destino.Diario.LerAsync(cancelamento)).Existe);

            // Um papel membro do login escreve como ele, sem aparecer em nenhuma concessão das tabelas.
            await servidor.ExecutarAsync("postgres", $"GRANT {dono.Login} TO {dono.Terceiro}", cancelamento);
            var membro = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Diario.LerAsync(cancelamento));
            Assert.Equal("INC3116", membro.Codigo);
            await servidor.ExecutarAsync("postgres", $"REVOKE {dono.Login} FROM {dono.Terceiro}", cancelamento);
            Assert.True((await destino.Diario.LerAsync(cancelamento)).Existe);

            // Uma função no esquema do diário, mesmo do login, não é do diário.
            await servidor.ExecutarAsync(
                dono.Banco,
                $"CREATE FUNCTION incognito.extra() RETURNS integer LANGUAGE sql AS 'SELECT 1'; ALTER FUNCTION incognito.extra() OWNER TO {dono.Login}",
                cancelamento);
            var objeto = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Diario.LerAsync(cancelamento));
            Assert.Equal("INC3116", objeto.Codigo);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_criacao_tira_as_concessoes_dos_privilegios_padrao_e_recusa_o_esquema_de_outro_dono()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var dono = await DonoProprioAsync();

        // Os privilégios padrão do login dão escrita ao terceiro em toda tabela nova: a criação tira, e só a leitura fica.
        await servidor.ExecutarAsync(
            dono.Banco,
            $"ALTER DEFAULT PRIVILEGES FOR ROLE {dono.Login} GRANT SELECT, INSERT, UPDATE ON TABLES TO {dono.Terceiro}; ALTER DEFAULT PRIVILEGES FOR ROLE {dono.Login} GRANT CREATE ON SCHEMAS TO {dono.Terceiro}",
            cancelamento);
        await using (var destino = await dono.ConectarAsync(_provedor))
        await using (var concessao = (await destino.Bloqueio.TentarAdquirirAsync(cancelamento))!)
        {
            await destino.Diario.CriarAsync(cancelamento);
        }

        var privilegios = await servidor.ConsultarAsync(
            dono.Banco,
            $"SELECT has_table_privilege('{dono.Terceiro}', 'incognito.restricao_removida', 'SELECT')::text || has_table_privilege('{dono.Terceiro}', 'incognito.restricao_removida', 'INSERT, UPDATE')::text || has_schema_privilege('{dono.Terceiro}', 'incognito', 'CREATE')::text",
            cancelamento);
        Assert.Equal("truefalsefalse", privilegios);

        // O esquema incognito de outro dono nunca é usado.
        await using var outro = await DonoProprioAsync();
        await servidor.ExecutarAsync(outro.Banco, $"CREATE SCHEMA incognito AUTHORIZATION {outro.Terceiro}", cancelamento);
        await using var noOutro = await outro.ConectarAsync(_provedor);
        await using var daOutra = (await noOutro.Bloqueio.TentarAdquirirAsync(cancelamento))!;
        Assert.Equal("INC3116", (await Assert.ThrowsAsync<DialetoRecusouException>(() => noOutro.Diario.CriarAsync(cancelamento))).Codigo);
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id)", true)]
    [InlineData("FOREIGN KEY (a, b) REFERENCES loja.lojas(a, b) MATCH FULL NOT VALID", true)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) ON UPDATE SET NULL ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED", true)]
    [InlineData("FOREIGN KEY (loja, \"Código\") REFERENCES \"Outro \"\"esquema\"\"\".\"lojas\"(id, codigo) ON DELETE SET NULL (loja) NOT ENFORCED", true)]
    [InlineData("FOREIGN KEY (loja, PERIOD vigencia) REFERENCES loja.lojas(id, PERIOD vigencia)", true)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id), ADD CONSTRAINT k CHECK (true)", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) -- NOT VALID", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id); DROP TABLE loja.lojas", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) /* x */", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES lojas(id)", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) ON DELETE SET DEFAULT ($1)", false)]
    [InlineData("CHECK ((loja > 0))", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) USING (f())", false)]
    [InlineData("FOREIGN KEY (\"loja) REFERENCES loja.lojas(id)", false)]
    [InlineData("FOREIGN KEY (loja) REFERENCES loja.lojas(id) NOT VALID NOT VALID", false)]
    [InlineData("FOREIGN KEY (loja codigo) REFERENCES loja.lojas(id, codigo)", false)]
    [InlineData("FOREIGN KEY (PERIOD vigencia, loja) REFERENCES loja.lojas(PERIOD vigencia, id)", false)]
    public void So_a_forma_de_uma_chave_estrangeira_e_executada(string definicao, bool valida) =>
        Assert.Equal(valida, FormatoDaDefinicao.Valida(definicao));

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_destino_confere_de_segundo_em_segundo_se_o_cliente_ainda_esta_la()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var conexao = await ConexaoPostgreSql.AbrirAsync(new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono)), origem: false, cancelamento);
            await using var outra = await conexao.AbrirOutraAsync(cancelamento);
            foreach (var sessao in new[] { conexao.Conexao, outra })
            {
                await using var comando = new NpgsqlCommand("SELECT current_setting('client_connection_check_interval')", sessao);
                Assert.Equal("1s", await comando.ExecuteScalarAsync(cancelamento));
            }
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    private async Task<bool> ChaveExisteAsync(string banco, string tabela, string chave) =>
        await servidor.ConsultarAsync(
            banco,
            $"SELECT EXISTS (SELECT FROM pg_catalog.pg_constraint WHERE conrelid = '{tabela}'::regclass AND conname = '{chave}')",
            TestContext.Current.CancellationToken) is true;

    // Um login dono só deste teste, num banco do administrador em que ele cria esquema, e um terceiro papel sem login: a
    // concessão de papel e os privilégios padrão do teste não alcançam os bancos dos outros testes, que rodam juntos.
    private async Task<DonoProprio> DonoProprioAsync()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var sufixo = Guid.NewGuid().ToString("N")[..12];
        var (login, terceiro, senha, banco) = ($"dono_{sufixo}", $"terceiro_{sufixo}", Guid.NewGuid().ToString("N"), $"diario_{sufixo}");
        await servidor.ExecutarAsync("postgres", $"CREATE ROLE {login} LOGIN PASSWORD '{senha}'; CREATE ROLE {terceiro}", cancelamento);
        await servidor.ExecutarAsync("postgres", $"CREATE DATABASE {banco}", cancelamento);
        await servidor.ExecutarAsync("postgres", $"GRANT CONNECT, CREATE ON DATABASE {banco} TO {login}", cancelamento);
        await servidor.ExecutarAsync(banco, $"SET ROLE {login}; CREATE SCHEMA loja; CREATE TABLE loja.t (id integer); RESET ROLE", cancelamento);
        return new DonoProprio(servidor, login, terceiro, senha, banco);
    }

    private sealed class DonoProprio(ServidorPostgreSql servidor, string login, string terceiro, string senha, string banco) : IAsyncDisposable
    {
        internal string Login => login;

        internal string Terceiro => terceiro;

        internal string Banco => banco;

        internal Task<DialetoDestino> ConectarAsync(ProvedorPostgreSql provedor) =>
            provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, login, senha)), TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await servidor.ApagarAsync(banco);
            await servidor.ExecutarAsync("postgres", string.Create(CultureInfo.InvariantCulture, $"DROP ROLE IF EXISTS {login}; DROP ROLE IF EXISTS {terceiro}"), CancellationToken.None);
        }
    }
}
