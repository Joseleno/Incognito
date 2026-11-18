using System.Text;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O <c>executar</c> ponta a ponta contra o servidor, num esquema próprio e pequeno com dados de borda: os opacos chegam
/// byte a byte (C2), as tratadas mudam e continuam válidas, a trigger <c>AFTER TRUNCATE</c> não dispara, a chave volta
/// validada; a inserção, a atualização e a remoção na origem durante a cópia não chegam ao destino (C7, com o controle
/// positivo); a segunda execução simultânea sai com 1 sem tocar o destino (C3 do INC-035); a senha, a chave canário, os
/// valores tratados e o identificador do instante nunca saem.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed partial class ExecucaoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    // As colunas que nenhuma regra trata, comparadas byte a byte pelo COPY TO dos dois lados.
    private const string Opacas = "id, humor, notas, saldo, foto, intervalo, nascimento, medida, dados, criado, bruto, preco";

    private const string ClientesOpacos = $"SELECT {Opacas} FROM loja.clientes ORDER BY id";

    private const string PedidosOpacos = "SELECT id, cliente, total FROM loja.pedidos ORDER BY id";

    private const string CuponsOpacos = "SELECT valor FROM loja.cupons ORDER BY valor";

    private const string Esquema = """
        CREATE SCHEMA loja;
        CREATE TYPE loja.humor AS ENUM ('feliz', 'triste');
        CREATE TABLE loja.clientes (
            id bigint PRIMARY KEY,
            cpf varchar(14),
            nome text NOT NULL,
            humor loja.humor,
            notas integer[],
            saldo numeric(12, 2),
            foto bytea,
            intervalo interval,
            nascimento date,
            medida double precision,
            dados jsonb,
            criado timestamptz,
            bruto text,
            preco money);
        CREATE TABLE loja.pedidos (id bigint PRIMARY KEY, cliente bigint CONSTRAINT fk_pedidos_clientes REFERENCES loja.clientes (id), total numeric(10, 2));
        CREATE TABLE loja.cupons (numero bigint PRIMARY KEY, valor numeric(8, 2) NOT NULL UNIQUE);
        CREATE TABLE loja.disparos (tabela text);
        CREATE FUNCTION loja.registrar() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN INSERT INTO loja.disparos (tabela) VALUES (TG_TABLE_NAME); RETURN NULL; END$$;
        CREATE TRIGGER tg_esvaziada AFTER TRUNCATE ON loja.clientes FOR EACH STATEMENT EXECUTE FUNCTION loja.registrar();

        """;

    // Os opacos de cada linha: texto com tab, quebra e barra, enum, array com NULL, bytea, interval, date infinita, NaN,
    // jsonb, timestamptz com microssegundos, money negativo e com centavos, e NULLs.
    private static readonly string[] _opacos =
    [
        "'feliz', '{1,NULL,3}', 1234.56, '\\x00ff10'::bytea, '1 day 02:03:04', 'infinity', 'NaN', '{\"a\": [1, null], \"b\": \"x\\ty\"}', '2026-11-12 10:00:00.123456+00', E'linha1\\nlinha2\\\\N', '-1234.56'",
        "'triste', '{}', -0.01, ''::bytea, '-3 mons', '1990-02-28', 'Infinity', 'null', 'infinity', '\\N', '0.01'",
        "NULL, NULL, NULL, NULL, NULL, '-infinity', 1.5e-300, NULL, NULL, NULL, NULL",
        "'feliz', '{NULL}', 0, '\\x5c'::bytea, '00:00:00.000001', '2000-01-01', -0.0, '[]', '1970-01-01 00:00:00+00', E'\\t\\\\', '1000000.99'",
        "NULL, '{7}', 99999.99, NULL, NULL, NULL, NULL, '{\"€\": \"ção\"}', NULL, '', '0'",
        "'triste', NULL, NULL, '\\x'::bytea, '1 year', '2024-02-29', 3.14, '1', '2026-01-01 23:59:59.999999+00', 'zqSENTINELAqz', '-0.10'",
    ];

    // Os nomes, com o que o COPY em texto escapa.
    private static readonly string[] _nomes = ["Ana\tSilva", "Bruno\nCosta", "Caio \\ Dias", "Débora Conceição", "Eva € Lima", "Fábio"];

    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Execucao_completa_com_opacos_byte_a_byte_tratadas_validas_e_nada_vaza()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var (origem, destino, cpfs) = await BancosAsync(cancelamento);
        try
        {
            var canario = new Canario();
            var resultado = await ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), canario.Resolver, cancelamento);

            Assert.Null(resultado.Falha);
            Assert.Equal(0, resultado.Saida);
            Assert.Equal(ConsistenciaObtida.Instantaneo, resultado.Consistencia);

            // C2: os opacos e a tabela sem tratada, idênticos pelo COPY TO com os mesmos parâmetros e ORDER BY pela PK.
            Igual(await CopiaAsync(origem, ClientesOpacos), await CopiaAsync(destino, ClientesOpacos), "os opacos de loja.clientes");
            Igual(await CopiaAsync(origem, PedidosOpacos), await CopiaAsync(destino, PedidosOpacos), "loja.pedidos");

            // As tratadas mudaram, o CPF continua válido e o NULL continua NULL.
            var tratadas = await LinhasAsync(destino, "SELECT id, cpf, nome FROM loja.clientes ORDER BY id");
            Assert.Equal(_nomes.Length, tratadas.Count);
            foreach (var (id, cpf, nome) in tratadas)
            {
                Assert.False(string.Equals(_nomes[id - 1], nome, StringComparison.Ordinal), $"o nome da linha {id} não mudou");
                if (cpfs[id - 1] is null)
                {
                    Assert.Null(cpf);
                    continue;
                }

                Assert.False(string.Equals(cpfs[id - 1], cpf, StringComparison.Ordinal), $"o CPF da linha {id} não mudou");
                Assert.True(ValidadorBr.Cpf(cpf), $"linha {id}");
            }

            // O número do cupom, inteiro e chave primária, sai permutado (distinto, e não todo igual ao da origem); o valor vai junto.
            var numeros = await CopiaAsync(destino, "SELECT numero FROM loja.cupons ORDER BY valor");
            Assert.False(string.Equals(await CopiaAsync(origem, "SELECT numero FROM loja.cupons ORDER BY valor"), numeros, StringComparison.Ordinal), "o número do cupom não mudou");
            Igual(await CopiaAsync(origem, CuponsOpacos), await CopiaAsync(destino, CuponsOpacos), "loja.cupons.valor");
            Assert.Equal(4, numeros.Split((char)10, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Count());

            // A trigger AFTER TRUNCATE ficou desligada no esvaziamento; a chave voltou validada e o diário ficou vazio.
            Assert.Equal(0L, await servidor.ConsultarAsync(destino, "SELECT count(*) FROM loja.disparos", cancelamento));
            Assert.Equal("O", await servidor.ConsultarAsync(destino, "SELECT tgenabled::text FROM pg_catalog.pg_trigger WHERE tgname = 'tg_esvaziada'", cancelamento));
            Assert.Equal(true, await servidor.ConsultarAsync(destino, "SELECT convalidated FROM pg_catalog.pg_constraint WHERE conname = 'fk_pedidos_clientes'", cancelamento));
            Assert.Empty(resultado.Restauracao!.Pendentes);

            // A mesma chave dá o mesmo destino (reproduzível).
            var segunda = await ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), canario.Resolver, cancelamento);
            Assert.Equal(0, segunda.Saida);
            Assert.True(tratadas.SequenceEqual(await LinhasAsync(destino, "SELECT id, cpf, nome FROM loja.clientes ORDER BY id")), "a mesma chave deu outro destino");
            Assert.Equal(2, canario.Chaves.Count);
            Assert.All(canario.Chaves, chave => Assert.Throws<ObjectDisposedException>(() => new SessaoTransformacao(chave, "homologacao")));

            string[] proibidos =
            [
                servidor.SenhaLeitor, servidor.SenhaDono, ServidorPostgreSql.Sentinela, .. canario.Formas,
                .. cpfs.OfType<string>(), .. _nomes, .. tratadas.Select(linha => linha.Cpf).OfType<string>(), .. tratadas.Select(linha => linha.Nome),
            ];
            var textos = Textos(resultado).Concat(Textos(segunda)).ToList();
            SemProibidos(textos, proibidos);
            Assert.All(textos, texto => Assert.False(IdentificadorDoInstante().IsMatch(texto), "o identificador do instante aparece"));
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task C7_insercao_atualizacao_e_remocao_na_origem_durante_a_copia_nao_chegam_ao_destino()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var (origem, destino, _) = await BancosAsync(cancelamento);
        try
        {
            var antes = await RetratoDaOrigemAsync(origem);

            // Com um trabalhador e um tratamento, as tabelas vão na ordem do plano (clientes, cupons, pedidos): o gancho, no
            // primeiro valor de clientes, confirma por outra conexão inserção, atualização e remoção em cupons e pedidos, que
            // ainda não começaram a ser lidos (e em clientes, já no lote lido).
            var chamadas = 0;
            var registro = new RegistroTransformadores();
            registro.Adicionar(new FabricaDoGancho(() =>
            {
                if (Interlocked.Increment(ref chamadas) == 1)
                {
                    using var conexao = new NpgsqlConnection(servidor.StringDeConexaoDoAdministrador(origem));
                    conexao.Open();
                    using var comando = new NpgsqlCommand(
                        "INSERT INTO loja.clientes (id, nome) VALUES (7, 'inserido'); UPDATE loja.clientes SET bruto = 'mudou', saldo = 1 WHERE id = 2; DELETE FROM loja.clientes WHERE id = 6; "
                        + "INSERT INTO loja.cupons VALUES (105, 5.5); UPDATE loja.cupons SET valor = 9.5 WHERE numero = 101; DELETE FROM loja.cupons WHERE numero = 104; "
                        + "INSERT INTO loja.pedidos VALUES (5, 1, 1); UPDATE loja.pedidos SET total = 0 WHERE id = 1; DELETE FROM loja.pedidos WHERE id = 4",
                        conexao);
                    comando.ExecuteNonQuery();
                }
            }));

            var resultado = await ExecutarAsync(servidor, origem, destino, Configuracao(destino, nome: "exemplo.gancho@1"), registro, new Canario().Resolver, paralelismo: 1, tratamentos: 1, cancelamento);

            Assert.Equal(0, resultado.Saida);
            Assert.Equal(6, chamadas);
            Assert.Equal(["clientes", "cupons", "pedidos"], resultado.Tabelas.Select(tabela => tabela.Nome.Tabela));
            var depois = await RetratoDaOrigemAsync(origem);
            Igual(antes, await RetratoDaOrigemAsync(destino), "o destino e a origem antes das mudanças");

            // Controle positivo: as mudanças foram confirmadas, e a execução seguinte as leva.
            Assert.False(string.Equals(antes, depois, StringComparison.Ordinal), "as mudanças na origem não foram confirmadas");
            Assert.Equal(0, (await ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), new Canario().Resolver, cancelamento)).Saida);
            Igual(depois, await RetratoDaOrigemAsync(destino), "o destino e a origem depois das mudanças");
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("23514")]
    [InlineData("22023")]
    [InlineData("INC3132")]
    public async Task Erro_do_destino_na_carga_sai_com_1_sem_nenhum_valor(string caso)
    {
        // Só no destino: um CHECK que recusa a linha com o sentinela (o servidor põe a linha inteira no DETAIL), uma função
        // que lança com o nome tratado na mensagem, ou uma que troca o DateStyle no meio do COPY.
        var noDestino = caso switch
        {
            "23514" => "ALTER TABLE loja.clientes ADD CONSTRAINT ck_bruto CHECK (bruto IS DISTINCT FROM 'zqSENTINELAqz');",
            "22023" => "CREATE FUNCTION loja.recusar(t text, i bigint) RETURNS boolean LANGUAGE plpgsql AS $$BEGIN IF i = 2 THEN RAISE EXCEPTION 'recusado: %', t USING ERRCODE = '22023'; END IF; RETURN true; END$$; "
                + "ALTER TABLE loja.clientes ADD CONSTRAINT ck_nome CHECK (loja.recusar(nome, id));",
            _ => "CREATE FUNCTION loja.trocar(i bigint) RETURNS boolean LANGUAGE plpgsql AS $$BEGIN IF i = 2 THEN PERFORM pg_catalog.set_config('DateStyle', 'SQL, DMY', false); END IF; RETURN true; END$$; "
                + "ALTER TABLE loja.clientes ADD CONSTRAINT ck_troca CHECK (loja.trocar(id));",
        };
        var cancelamento = TestContext.Current.CancellationToken;
        var (origem, destino, cpfs) = await BancosAsync(cancelamento, noDestino);
        try
        {
            var resultado = await ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), new Canario().Resolver, cancelamento);

            Assert.Equal(1, resultado.Saida);
            var falha = Assert.IsType<FalhaDialetoException>(resultado.Falha);
            if (caso == "INC3132")
            {
                Assert.Equal("INC3132", falha.Codigo);
            }

            Assert.Empty(await LinhasAsync(destino, "SELECT id, cpf, nome FROM loja.clientes ORDER BY id"));
            string[] proibidos = [ServidorPostgreSql.Sentinela, servidor.SenhaLeitor, servidor.SenhaDono, "recusado", "SQL, DMY", .. cpfs.OfType<string>(), .. _nomes];
            SemProibidos(Textos(resultado), proibidos);
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task C3_a_segunda_execucao_simultanea_sai_com_1_sem_tocar_o_destino()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var (origem, destino, _) = await BancosAsync(cancelamento);
        try
        {
            Assert.Equal(0, (await ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), new Canario().Resolver, cancelamento)).Saida);
            var retrato = await RetratoAsync(destino);

            // A primeira execução segura o bloqueio do destino.
            await using var primeira = await DialetoProtegido.ConectarAsync(
                _provedor, "postgresql", LadoBanco.Destino, new SegredoConexao(servidor.StringDeConexao(destino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
            await using var concessao = await primeira.TentarAdquirirBloqueioAsync(cancelamento);
            Assert.NotNull(concessao);

            var ocupado = await Assert.ThrowsAsync<BloqueioDestinoException>(() => ExecutarAsync(origem, destino, Configuracao(destino), new RegistroTransformadores(), new Canario().Resolver, cancelamento));

            Assert.Equal(("INC3004", 1), (ocupado.Codigo, ClassificadorDeSaida.De(ocupado)));
            Igual(retrato, await RetratoAsync(destino), "o destino com a segunda execução recusada");
            await concessao.ConferirAsync(cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    // A comparação que nunca ecoa os valores na mensagem da falha (a regra das saídas vale também para o registro dos testes).
    private static void Igual(string esperado, string lido, string oQue) =>
        Assert.True(string.Equals(esperado, lido, StringComparison.Ordinal), $"{oQue}: diferentes ({esperado.Length} e {lido.Length} caracteres)");

    // Sem eco: a mensagem diz só qual proibido e qual texto, nunca o conteúdo (senhas, canário, CPFs).
    private static void SemProibidos(List<string> textos, string[] proibidos)
    {
        for (var texto = 0; texto < textos.Count; texto++)
        {
            for (var proibido = 0; proibido < proibidos.Length; proibido++)
            {
                Assert.False(textos[texto].Contains(proibidos[proibido], StringComparison.OrdinalIgnoreCase), $"o proibido {proibido} aparece no texto {texto}");
            }
        }
    }

    private async Task<string> RetratoDaOrigemAsync(string banco) =>
        await CopiaAsync(banco, ClientesOpacos) + await CopiaAsync(banco, PedidosOpacos) + await CopiaAsync(banco, CuponsOpacos);

    // O identificador que o pg_export_snapshot devolve (por exemplo, 00000003-0000001B-1).
    [GeneratedRegex("[0-9A-F]{8}-[0-9A-F]{8}-[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex IdentificadorDoInstante();

    // Tudo o que o resultado mostra: o texto dele, o de cada aviso, tabela e restauração, e a falha com as internas.
    internal static List<string> Textos(ResultadoDaExecucao resultado)
    {
        var textos = new List<string> { resultado.ToString() };
        textos.AddRange(resultado.Avisos.SelectMany(aviso => new[] { aviso.ToString(), aviso.Mensagem }));
        textos.AddRange(resultado.Tabelas.Select(tabela => tabela.ToString()));
        textos.AddRange(resultado.Tabelas.SelectMany(tabela => tabela.Colunas.Select(coluna => coluna.ToString())));
        textos.Add(resultado.Reparo?.ToString() ?? "");
        textos.Add(resultado.Restauracao?.ToString() ?? "");
        for (var atual = resultado.Falha; atual is not null; atual = atual.InnerException)
        {
            textos.Add(atual.ToString());
            textos.Add(atual.Message);
            textos.AddRange(atual.Data.Values.Cast<object?>().Select(valor => valor?.ToString() ?? ""));
            if (atual is IncognitoException incognito)
            {
                textos.Add(incognito.Codigo);
            }
        }

        return textos;
    }

    private static string Configuracao(string destino, string nome = "texto.substituir@1") => $"""
        versao: 1
        origem:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_ORIGEM
        destino:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_DESTINO
          bancoEsperado: {destino}
        modo: reproduzivel
        chave: ambiente:INCOGNITO_CHAVE
        ambiente: homologacao
        tabelas:
          loja.clientes:
            colunas:
              cpf: {"{"} transformador: br.cpf@1 {"}"}
              nome: {"{"} transformador: {nome} {"}"}
          loja.cupons:
            colunas:
              numero: {"{"} transformador: id.permutar@1, dominio: cupom {"}"}
          loja.disparos: {"{"} acao: ignorar {"}"}

        """;

    // A origem com o esquema e as linhas, e o destino do dono com o mesmo esquema, preparado (marcador e diário).
    private async Task<(string Origem, string Destino, string?[] Cpfs)> BancosAsync(CancellationToken cancelamento, string noDestino = "")
    {
        var origem = await servidor.CriarVazioAsync("origem", null, cancelamento);
        var destino = await servidor.CriarVazioAsync("destino", ServidorPostgreSql.Dono, cancelamento);
        var gerador = new GeradorBr(38_003);
        var cpfs = Enumerable.Range(1, _nomes.Length).Select(id => id == 3 ? null : gerador.Cpf(formatado: id % 2 == 0)).ToArray();

        await servidor.ExecutarAsync(origem, Esquema + $"GRANT USAGE ON SCHEMA loja TO {ServidorPostgreSql.Leitor}; GRANT SELECT ON ALL TABLES IN SCHEMA loja TO {ServidorPostgreSql.Leitor};", cancelamento);
        await using (var conexao = await servidor.AbrirComoAdministradorAsync(origem, cancelamento))
        {
            for (var id = 1; id <= _nomes.Length; id++)
            {
                await using var comando = new NpgsqlCommand($"INSERT INTO loja.clientes VALUES ({id}, @cpf, @nome, {_opacos[id - 1]})", conexao);
                comando.Parameters.AddWithValue("cpf", (object?)cpfs[id - 1] ?? DBNull.Value);
                comando.Parameters.AddWithValue("nome", _nomes[id - 1]);
                await comando.ExecuteNonQueryAsync(cancelamento);
            }

            await using var pedidos = new NpgsqlCommand("INSERT INTO loja.pedidos VALUES (1, 1, 10.00), (2, 1, 20.50), (3, 2, NULL), (4, NULL, 5); INSERT INTO loja.cupons VALUES (101, 1.5), (102, 2.5), (103, 3.5), (104, 4.5)", conexao);
            await pedidos.ExecuteNonQueryAsync(cancelamento);
        }

        await servidor.ExecutarAsync(destino, Esquema + noDestino, cancelamento);
        await PrepararDestinoAsync(servidor, origem, destino, "loja", cancelamento);
        return (origem, destino, cpfs);
    }

    // Tudo do esquema passa ao dono do destino: o esquema, as tabelas, as funções e os tipos.
    private static string Posse(string esquema) => $"""
        DO $$DECLARE o record; BEGIN
            EXECUTE 'ALTER SCHEMA {esquema} OWNER TO {ServidorPostgreSql.Dono}';
            FOR o IN SELECT c.oid::regclass::text AS nome FROM pg_catalog.pg_class c WHERE c.relnamespace = '{esquema}'::regnamespace AND c.relkind IN ('r', 'p') LOOP
                EXECUTE 'ALTER TABLE ' || o.nome || ' OWNER TO {ServidorPostgreSql.Dono}';
            END LOOP;
            FOR o IN SELECT p.oid::regprocedure::text AS nome FROM pg_catalog.pg_proc p WHERE p.pronamespace = '{esquema}'::regnamespace LOOP
                EXECUTE 'ALTER FUNCTION ' || o.nome || ' OWNER TO {ServidorPostgreSql.Dono}';
            END LOOP;
            FOR o IN SELECT t.oid::regtype::text AS nome FROM pg_catalog.pg_type t WHERE t.typnamespace = '{esquema}'::regnamespace AND t.typtype = 'e' LOOP
                EXECUTE 'ALTER TYPE ' || o.nome || ' OWNER TO {ServidorPostgreSql.Dono}';
            END LOOP;
        END$$;
        """;

    private Task<ResultadoDaExecucao> ExecutarAsync(
        string origem, string destino, string yaml, RegistroTransformadores registro, Func<ReferenciaEscrita, ChaveIncognito?> resolverChave, CancellationToken cancelamento) =>
        ExecutarAsync(servidor, origem, destino, yaml, registro, resolverChave, paralelismo: null, tratamentos: null, cancelamento);

    /// <summary>Planeja e executa com o dialeto PostgreSQL, o leitor na origem e o dono no destino.</summary>
    internal static async Task<ResultadoDaExecucao> ExecutarAsync(
        ServidorPostgreSql servidor,
        string origem,
        string destino,
        string yaml,
        RegistroTransformadores registro,
        Func<ReferenciaEscrita, ChaveIncognito?> resolverChave,
        int? paralelismo,
        int? tratamentos,
        CancellationToken cancelamento)
    {
        var configuracao = ConfiguracaoIncognito.LerYaml(yaml);
        var dialetos = new Dictionary<ProvedorBanco, ProvedorDialeto> { [ProvedorBanco.PostgreSql] = new ProvedorPostgreSql() };
        SegredoConexao Resolver(ConexaoBanco conexao) => new(ReferenceEquals(conexao, configuracao.Origem)
            ? servidor.StringDeConexao(origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)
            : servidor.StringDeConexao(destino, ServidorPostgreSql.Dono, servidor.SenhaDono));
        var plano = await PlanejadorInterno.PlanejarAsync(configuracao, registro, dialetos, Resolver, cancelamento);
        return await ExecutorInterno.ExecutarAsync(
            plano,
            registro,
            dialetos,
            Resolver,
            resolverChave,
            new OpcoesDaExecucao((_, _) => Task.FromResult(true), AceitarDadosExistentes: false, cancelamento, paralelismo, tratamentos),
            TimeProvider.System,
            cancelamento);
    }

    /// <summary>Passa ao dono tudo do esquema dado do destino e prepara o destino (marcador e diário).</summary>
    internal static async Task PrepararDestinoAsync(ServidorPostgreSql servidor, string origem, string destino, string esquema, CancellationToken cancelamento)
    {
        await servidor.ExecutarAsync(destino, Posse(esquema), cancelamento);
        var provedor = new ProvedorPostgreSql();
        await using var daOrigem = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Origem, new SegredoConexao(servidor.StringDeConexao(origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
        await using var doDestino = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Destino, new SegredoConexao(servidor.StringDeConexao(destino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
        var preparo = await PreparadorDoDestino.PrepararAsync(doDestino, daOrigem, destino, (_, _) => Task.FromResult(true), TimeProvider.System, cancelamento);
        Assert.Equal(SituacaoDoPreparo.Gravado, preparo.Situacao);
    }

    // O COPY TO em texto pela conexão do administrador, com os parâmetros da sessão iguais nos dois lados.
    private async Task<string> CopiaAsync(string banco, string consulta)
    {
        await using var conexao = await servidor.AbrirComoAdministradorAsync(banco, TestContext.Current.CancellationToken);
        using var leitor = await conexao.BeginTextExportAsync($"COPY ({consulta}) TO STDOUT", TestContext.Current.CancellationToken);
        return await leitor.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<(int Id, string? Cpf, string Nome)>> LinhasAsync(string banco, string consulta)
    {
        var linhas = new List<(int, string?, string)>();
        await using var conexao = await servidor.AbrirComoAdministradorAsync(banco, TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(consulta, conexao);
        await using var leitor = await comando.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await leitor.ReadAsync(TestContext.Current.CancellationToken))
        {
            linhas.Add(((int)leitor.GetInt64(0), leitor.IsDBNull(1) ? null : leitor.GetString(1), leitor.GetString(2)));
        }

        return linhas;
    }

    // Todo o conteúdo do destino: cada tabela dos esquemas loja e incognito, e o estado das chaves e triggers.
    private async Task<string> RetratoAsync(string banco)
    {
        var tabelas = new List<string>();
        await using (var conexao = await servidor.AbrirComoAdministradorAsync(banco, TestContext.Current.CancellationToken))
        {
            await using var comando = new NpgsqlCommand(
                "SELECT c.oid::regclass::text FROM pg_catalog.pg_class c WHERE c.relnamespace IN ('loja'::regnamespace, 'incognito'::regnamespace) AND c.relkind = 'r' ORDER BY 1",
                conexao);
            await using var leitor = await comando.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await leitor.ReadAsync(TestContext.Current.CancellationToken))
            {
                tabelas.Add(leitor.GetString(0));
            }
        }

        var retrato = new StringBuilder();
        foreach (var tabela in tabelas)
        {
            retrato.Append(tabela).Append('\n').Append(await CopiaAsync(banco, $"SELECT * FROM {tabela} ORDER BY 1"));
        }

        retrato.Append(await CopiaAsync(banco, "SELECT conname, convalidated FROM pg_catalog.pg_constraint WHERE connamespace = 'loja'::regnamespace ORDER BY 1"));
        retrato.Append(await CopiaAsync(banco, "SELECT tgname, tgenabled FROM pg_catalog.pg_trigger WHERE NOT tgisinternal ORDER BY 1"));
        return retrato.ToString();
    }

    // A chave canário do resolvedor: nova a cada chamada, guardada para conferir o descarte; o texto e os bytes nunca saem.
    private sealed class Canario
    {
        private static readonly byte[] _bytes = [.. Enumerable.Range(1, 32).Select(numero => (byte)numero)];
        private static readonly string _texto = "inc1_" + Convert.ToBase64String(_bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        internal List<ChaveIncognito> Chaves { get; } = [];

        internal string[] Formas { get; } = [_texto, _texto[5..], Convert.ToHexString(_bytes), Convert.ToBase64String(_bytes), string.Join(",", _bytes)];

        internal ChaveIncognito Resolver(ReferenciaEscrita referencia)
        {
            var chave = ChaveIncognito.Ler(_texto);
            Chaves.Add(chave);
            return chave;
        }
    }

    // Um transformador de fora que chama o gancho a cada valor e devolve o texto fixo.
    private sealed class FabricaDoGancho(Action gancho) : FabricaTransformador
    {
        public override string Nome => "exemplo.gancho";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Criado(gancho);

        private sealed class Criado(Action gancho) : Transformador
        {
            public override string Nome => "exemplo.gancho";

            public override int Versao => 1;

            public override Injetividade Injetividade => Injetividade.Nenhuma;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto)
            {
                gancho();
                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            }
        }
    }
}
