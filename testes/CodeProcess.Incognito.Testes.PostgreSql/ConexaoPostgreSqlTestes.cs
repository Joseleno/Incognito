using System.Collections;
using System.Reflection;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que o dialeto PostgreSQL faz além da suíte: o que recusa na string de conexão, o que impõe por cima dela, a versão
/// mínima e o sentinela, que nenhuma saída traz.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class ConexaoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    // No lugar da senha nos casos: o sentinela entra só na execução, nunca no nome do teste (que vai para o log do CI).
    private const string Senha = "{senha}";

    private readonly ProvedorPostgreSql _provedor = new();

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("Host=a,b;Database=loja;Username=u;Password=" + Senha)]
    [InlineData("Host=a;Database=loja;Load Balance Hosts=true;Username=u;Password=" + Senha)]
    [InlineData("Database=loja;Username=u;Password=" + Senha)]
    [InlineData("Host=a;Username=u;Password=" + Senha)]
    [InlineData("Host=a;Database=loja;Server Compatibility Mode=NoTypeLoading;Password=" + Senha)]
    [InlineData("Host=a;Database=loja;Load Table Composites=true;Password=" + Senha)]
    [InlineData("Host=a;Database=loja;Require Auth=Password;Password=" + Senha)]
    [InlineData("Host=a;Database=loja;Require Auth=!MD5;Password=" + Senha)]
    public async Task String_com_varios_servidores_sem_servidor_sem_banco_opcao_obsoleta_ou_senha_em_claro_e_recusada(string conexao)
    {
        var erro = await Assert.ThrowsAsync<IncognitoException>(() => _provedor.ConectarOrigemAsync(ComSentinela(conexao), TestContext.Current.CancellationToken));

        Assert.Equal("INC3102", erro.Codigo);
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("Host=a;Database=loja;Password=" + Senha + ";resto")]
    [InlineData("Host=a;Database=loja;Port=" + Senha)]
    [InlineData("Host=a;Database=loja;Password=x;" + Senha + "=1")]
    [InlineData("Host=localhost;Database=loja;SSL Negotiation=Direct;SSL Mode=Prefer;Password=" + Senha)]
    public async Task String_malformada_e_recusada_sem_trazer_nenhum_pedaco_dela(string conexao)
    {
        var erro = await Assert.ThrowsAsync<IncognitoException>(() => _provedor.ConectarDestinoAsync(ComSentinela(conexao), TestContext.Current.CancellationToken));

        Assert.Equal("INC3101", erro.Codigo);
        Assert.Null(erro.InnerException);
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Senha_errada_nao_aparece_no_erro()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            var conexao = servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, ServidorPostgreSql.NovaSenha());
            var erro = await Assert.ThrowsAsync<PostgresException>(() => _provedor.ConectarDestinoAsync(new SegredoConexao(conexao), cancelamento));

            Assert.Equal(PostgresErrorCodes.InvalidPassword, erro.SqlState);
            Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_caminho_de_busca_a_sessao_so_de_leitura_e_o_nome_da_aplicacao_sao_impostos_por_cima_da_string()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            // A string pede o contrário de tudo o que o dialeto impõe; a conexão confere a sessão e recusaria se não valesse.
            var construtor = new NpgsqlConnectionStringBuilder(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor))
            {
                SearchPath = "public",
                Options = "-c search_path=public -c default_transaction_read_only=off",
                ApplicationName = "outra",
                IncludeErrorDetail = true,
                Pooling = true,
            };
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(construtor.ConnectionString), cancelamento);
            var esquema = await origem.Esquema.LerAsync(cancelamento);

            Assert.NotEmpty(esquema.Tabelas);
            Assert.Equal(
                "Incognito",
                await servidor.ConsultarAsync("postgres", $"SELECT application_name FROM pg_stat_activity WHERE datname = '{banco}' AND usename = '{ServidorPostgreSql.Leitor}'", cancelamento));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_codificacao_e_imposta_por_cima_da_string_e_os_nomes_com_acento_saem_certos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            var construtor = new NpgsqlConnectionStringBuilder(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor))
            {
                ClientEncoding = "LATIN1",
                Encoding = "LATIN1",
            };
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(construtor.ConnectionString), cancelamento);
            var tabela = (await origem.Esquema.LerAsync(cancelamento)).Tabelas.Single(lida => lida.Nome.Equals(new NomeObjeto("loja", "select")));

            // O euro não existe no LATIN1: com a codificação da string, o nome nem chegaria.
            Assert.Contains("coluna com espaço", tabela.Colunas.Select(coluna => coluna.Nome));
            Assert.Contains("valor €", tabela.Colunas.Select(coluna => coluna.Nome));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_identidade_traz_servidor_com_porta_e_a_linhagem_inteira_e_distingue_os_bancos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var bancoOrigem = await servidor.ClonarModeloAsync(cancelamento);
        var bancoDestino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(bancoOrigem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            await using var destino = await _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(bancoDestino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
            var deOrigem = await origem.Identidade.LerAsync(cancelamento);
            var deDestino = await destino.Identidade.LerAsync(cancelamento);

            Assert.Matches(@":\d+$", deOrigem.Servidor);
            Assert.Equal(["oid_do_banco", "system_identifier"], deOrigem.Linhagem.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(deOrigem.Linhagem["system_identifier"], deDestino.Linhagem["system_identifier"]);
            Assert.NotEqual(deOrigem.Linhagem["oid_do_banco"], deDestino.Linhagem["oid_do_banco"]);
        }
        finally
        {
            await servidor.ApagarAsync(bancoOrigem);
            await servidor.ApagarAsync(bancoDestino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_provedor_se_chama_postgresql_e_nao_cita_identificador_vazio()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);

            Assert.Equal("postgresql", _provedor.Nome);
            Assert.Throws<ArgumentException>(() => destino.Sql.Citar(""));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Nada_lido_do_esquema_nem_da_identidade_traz_o_sentinela()
    {
        // O sentinela está em todo texto opaco e nos dados do esquema de conformidade (a lista no começo do .sql).
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            var textos = new List<string>();
            Coletar(await origem.Esquema.LerAsync(cancelamento), textos);
            Coletar(await origem.Identidade.LerAsync(cancelamento), textos);
            textos.Add(origem.ToString()!);

            Assert.NotEmpty(textos);
            Assert.DoesNotContain(textos, texto => texto.Contains(ServidorPostgreSql.Sentinela, StringComparison.Ordinal));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Servidor_abaixo_do_15_e_recusado_pela_versao()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var senha = ServidorPostgreSql.NovaSenha();
        await using var antigo = ServidorPostgreSql.Conteiner(14, senha);
        await antigo.StartAsync(cancelamento);

        var erro = await Assert.ThrowsAsync<IncognitoException>(() => _provedor.ConectarOrigemAsync(new SegredoConexao(antigo.GetConnectionString()), cancelamento));

        Assert.Equal("INC3103", erro.Codigo);
        Assert.Contains("versão 14", erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
    }

    private static SegredoConexao ComSentinela(string conexao) => new(conexao.Replace(Senha, ServidorPostgreSql.NovaSenha(), StringComparison.Ordinal));

    // Todo texto alcançável pelas propriedades públicas, inclusive o ToString de cada objeto.
    private static void Coletar(object? valor, List<string> textos)
    {
        switch (valor)
        {
            case null:
                return;
            case string texto:
                textos.Add(texto);
                return;
            case IDictionary dicionario:
                foreach (DictionaryEntry par in dicionario)
                {
                    Coletar(par.Key, textos);
                    Coletar(par.Value, textos);
                }

                return;
            case IEnumerable itens:
                foreach (var item in itens)
                {
                    Coletar(item, textos);
                }

                return;
            case Enum or ValueType:
                textos.Add(valor.ToString()!);
                return;
        }

        textos.Add(valor.ToString()!);
        foreach (var propriedade in valor.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(propriedade => propriedade.GetIndexParameters().Length == 0))
        {
            Coletar(propriedade.GetValue(valor), textos);
        }
    }
}
