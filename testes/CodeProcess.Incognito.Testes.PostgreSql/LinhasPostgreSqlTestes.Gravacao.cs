using System.Data;
using System.Text;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a gravação do PostgreSQL faz além do que a suíte confere: o texto de cada tipo canônico para o <c>COPY</c> e os
/// buffers limpos, os valores nas bordas que voltam iguais, o <c>FREEZE</c>, a troca de um parâmetro do texto no meio do
/// <c>COPY</c> ou antes da conclusão que desfaz a tabela, a restrição adiada, o descarte com prazo no fluxo, a conexão que
/// volta depois do cancelamento, e cada falha com o próprio código, só com nomes.
/// </summary>
public sealed partial class LinhasPostgreSqlTestes
{
    private static readonly NomeObjeto _bordas = new("loja", "bordas");

    // As colunas canônicas da tabela das bordas, na ordem; a chave vem antes.
    private static readonly (string Nome, TipoColuna Tipo)[] _colunasDasBordas =
    [
        ("inteiro", TipoColuna.Inteiro), ("valor", TipoColuna.Decimal), ("texto", TipoColuna.Texto), ("ativo", TipoColuna.Booleano),
        ("dia", TipoColuna.Data), ("momento", TipoColuna.DataHora), ("instante", TipoColuna.DataHoraComFuso),
        ("identificador", TipoColuna.Uuid), ("conteudo", TipoColuna.Binario),
    ];

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(0, @"\N")]
    [InlineData(1, @"a\tb\nc\rd\\e € ç")]
    [InlineData(2, "-9223372036854775808")]
    [InlineData(3, "12.30")]
    [InlineData(4, "t")]
    [InlineData(5, "0001-01-01")]
    [InlineData(6, "2026-10-29 12:34:56")]
    [InlineData(7, "9999-12-31 23:59:59.999999")]
    [InlineData(8, "2026-10-29 12:34:56.000001+00")]
    [InlineData(9, "0b6f1a52-6a0d-4b6c-9a52-6a0d4b6c9a52")]
    [InlineData(10, @"\\x00ff5c0a")]
    [InlineData(11, @"\\N")]
    public void O_texto_para_o_copy_formata_cada_tipo_canonico(int caso, string esperado)
    {
        // Os valores montados aqui: o xUnit serializa os dados do teste, e o texto com controles chegaria trocado.
        object? valor = caso switch
        {
            0 => null,
            1 => "a\tb\nc\rd\\e € ç",
            2 => long.MinValue,
            3 => 12.30m,
            4 => true,
            5 => DateOnly.MinValue,
            6 => new DateTime(2026, 10, 29, 12, 34, 56),
            7 => new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9_999_999),
            8 => new DateTimeOffset(2026, 10, 29, 12, 34, 56, TimeSpan.Zero).AddTicks(10),
            9 => Guid.Parse("0b6f1a52-6a0d-4b6c-9a52-6a0d4b6c9a52"),
            10 => new byte[] { 0x00, 0xff, 0x5c, 0x0a },
            _ => "\\N",
        };
        var pool = new PoolQueConfere();
        var texto = new TextoParaOCopy(pool);

        texto.AcrescentarValor(valor);

        Assert.Equal(esperado, Encoding.UTF8.GetString(texto.Escrito.Span));
        texto.Devolver();
        Assert.Equal(0, pool.DevolvidosSujos);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void O_texto_para_o_copy_cresce_e_devolve_os_buffers_limpos()
    {
        var pool = new PoolQueConfere();
        var texto = new TextoParaOCopy(pool);

        // Com valor no buffer antes de crescer: o buffer velho volta ao pool limpo.
        texto.AcrescentarValor("valor");
        texto.AcrescentarValor(new string('x', (3 * TextoParaOCopy.Bloco) + 7));
        texto.AcrescentarValor(new byte[TextoParaOCopy.Bloco]);
        Assert.Equal(5 + (3 * TextoParaOCopy.Bloco) + 7 + 3 + (2 * TextoParaOCopy.Bloco), texto.Usados);
        texto.Esvaziar();
        texto.AcrescentarValor("valor");
        texto.Devolver();
        texto.Devolver();

        Assert.Equal(0, pool.DevolvidosSujos);
        Assert.True(pool.DevolvidosLimpos >= 2, $"{pool.DevolvidosLimpos} buffers devolvidos limpos, esperados ao menos 2 (o que cresceu e o último).");
        Assert.Throws<InvalidOperationException>(() => new TextoParaOCopy(pool).AcrescentarValor(1.5d));
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_valores_escritos_nas_bordas_voltam_iguais()
    {
        object?[][] linhas =
        [
            [long.MinValue, 0.0000000000000000000000000001m, "tab\tquebra\nretorno\rbarra\\ ponto \\.", true, DateOnly.MinValue, DateTime.MinValue,
                new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), Guid.Empty, Enumerable.Range(0, 256).Select(valor => (byte)valor).ToArray()],
            [long.MaxValue, 79228162514264337593543950335m, "\u0001\u001b\u007f controles", false, DateOnly.MaxValue, new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9_999_990),
                new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_990), Guid.Parse("0b6f1a52-6a0d-4b6c-9a52-6a0d4b6c9a52"), Array.Empty<byte>()],
            [0L, -1234.500m, "\U0001F600 € ção", true, new DateOnly(2024, 2, 29), new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(10),
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), new byte[] { 0x5c, 0x0a, 0x09, 0x0d }],
            [-1L, 0m, "\\N", false, new DateOnly(2026, 10, 29), new DateTime(2026, 10, 29), new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), Guid.Parse("00000000-0000-0000-0000-000000000001"), new byte[] { 0 }],
            [null, null, "", null, null, null, null, null, null],
        ];
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, linhas.Length, cancelamento);
            await GravarBordasAsync(destino, (lote, linha, chave) => Escrever(lote, linha, linhas[chave - 1]), cancelamento);

            var lidas = await LerBordasAsync(destino, cancelamento);
            Assert.Equal(linhas.Length, lidas.Count);
            for (var chave = 1; chave <= linhas.Length; chave++)
            {
                for (var coluna = 0; coluna < _colunasDasBordas.Length; coluna++)
                {
                    Assert.True(
                        IgualComEscala(lidas[chave][coluna], linhas[chave - 1][coluna]),
                        $"loja.bordas.{_colunasDasBordas[coluna].Nome} na linha {chave} voltou diferente do escrito.");
                }
            }
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_congela_as_linhas()
    {
        // Com o FREEZE, as linhas nascem congeladas: um instante de antes da gravação, de outra sessão, já as vê.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 3, cancelamento);
            await using var antiga = await servidor.AbrirComoAdministradorAsync(banco, cancelamento);
            await using var transacao = await antiga.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancelamento);
            await using (var instante = new NpgsqlCommand("SELECT 1", antiga, transacao))
            {
                await instante.ExecuteScalarAsync(cancelamento);
            }

            await GravarBordasAsync(destino, (lote, linha, _) => lote.Manter(linha, 1), cancelamento);

            await using var contar = new NpgsqlCommand("SELECT pg_catalog.count(*) FROM loja.bordas", antiga, transacao);
            Assert.Equal(3L, await contar.ExecuteScalarAsync(cancelamento));
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("DateStyle", "SQL, DMY")]
    [InlineData("IntervalStyle", "sql_standard")]
    [InlineData("TimeZone", "America/Sao_Paulo")]
    [InlineData("extra_float_digits", "0")]
    [InlineData("bytea_output", "escape")]
    [InlineData("array_nulls", "off")]
    [InlineData("xmloption", "document")]
    [InlineData("lc_monetary", "C.UTF-8")]
    [InlineData("search_path", "public")]
    [InlineData("standard_conforming_strings", "off")]
    [InlineData("client_encoding", "LATIN1")]
    public async Task O_parametro_trocado_no_meio_do_copy_desfaz_a_gravacao(string parametro, string valor)
    {
        // Um CHECK que troca o parâmetro a cada linha, como o faria código do banco: a conferência depois do COPY pega a troca,
        // e nada da tabela fica.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 2, cancelamento);
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE FUNCTION loja.trocar() RETURNS boolean LANGUAGE plpgsql AS $$BEGIN PERFORM pg_catalog.set_config('{parametro}', '{valor}', false); RETURN true; END$$;
                ALTER FUNCTION loja.trocar() OWNER TO {ServidorPostgreSql.Dono};
                ALTER TABLE loja.bordas ADD CONSTRAINT ck_trocar CHECK (loja.trocar());
                """,
                cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, chave) => lote.Escrever(linha, 3, $"{ServidorPostgreSql.Sentinela}-{chave}"), cancelamento));

            Assert.Equal("INC3132", falha.Codigo);
            ConferirSigilo(falha);
            var depois = await LerBordasAsync(destino, cancelamento);
            Assert.Equal(2, depois.Count);
            Assert.All(depois.Values, linha => Assert.Null(linha[2]));
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(0, "uq_bordas_texto", null)]
    [InlineData(1, null, "obrigatorio")]
    [InlineData(2, "ck_bordas_texto", null)]
    [InlineData(3, null, null)]
    public async Task A_restricao_violada_no_destino_sai_com_os_nomes_e_sem_o_valor(int caso, string? restricao, string? coluna)
    {
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 2, cancelamento);
            await servidor.ExecutarAsync(
                banco,
                $"ALTER TABLE loja.bordas ADD CONSTRAINT uq_bordas_texto UNIQUE (texto), ADD CONSTRAINT ck_bordas_texto CHECK (texto <> '{ServidorPostgreSql.Sentinela}-check')",
                cancelamento);

            // A unicidade, o NOT NULL, o CHECK e o tamanho, sempre com o sentinela no valor.
            var falha = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(
                destino,
                (lote, linha, chave) =>
                {
                    switch (caso)
                    {
                        case 0:
                            lote.Escrever(linha, 3, ServidorPostgreSql.Sentinela + "-unico");
                            break;
                        case 1:
                            lote.Escrever(linha, 3, $"{ServidorPostgreSql.Sentinela}-{chave}");
                            lote.Escrever(linha, 10, null);
                            break;
                        case 2:
                            lote.Escrever(linha, 3, ServidorPostgreSql.Sentinela + "-check");
                            break;
                        default:
                            lote.Escrever(linha, 11, ServidorPostgreSql.Sentinela);
                            break;
                    }
                },
                cancelamento));

            Assert.Equal("INC3138", falha.Codigo);
            ConferirSigilo(falha);
            Assert.True(restricao is null || falha.Message.Contains($"\"{restricao}\"", StringComparison.Ordinal), "A mensagem não cita a restrição violada.");
            Assert.True(coluna is null || falha.Message.Contains($"\"{coluna}\"", StringComparison.Ordinal), "A mensagem não cita a coluna.");
            Assert.Equal(2, (await LerBordasAsync(destino, cancelamento)).Count);
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("SELECT 1 FROM bordas")]
    [InlineData("SELECT loja_um() = 1")]
    public async Task A_funcao_que_depende_do_caminho_de_busca_sai_com_o_codigo_proprio(string corpo)
    {
        // A função sem o caminho fixado acha a tabela ou a função pelo caminho de quem chama; na gravação, ele é pg_catalog.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE FUNCTION loja.loja_um() RETURNS integer LANGUAGE sql AS 'SELECT 1';
                CREATE FUNCTION loja.sem_caminho() RETURNS boolean LANGUAGE plpgsql AS $$BEGIN RETURN EXISTS ({corpo}); END$$;
                ALTER TABLE loja.bordas ADD CONSTRAINT ck_caminho CHECK (loja.sem_caminho()) NOT VALID;
                """,
                cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, _) => lote.Escrever(linha, 3, ServidorPostgreSql.Sentinela), cancelamento));

            Assert.Equal("INC3140", falha.Codigo);
            ConferirSigilo(falha);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_login_sem_privilegio_nao_esvazia_nem_grava()
    {
        await NoDestinoAsync(async (banco, _, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            await using var leitor = await _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);

            var esvaziar = await Assert.ThrowsAsync<IncognitoException>(() => leitor.Gravador.EsvaziarAsync(_bordas, cancelamento));
            var gravar = await Assert.ThrowsAsync<IncognitoException>(() => leitor.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento));

            Assert.Equal("INC3139", esvaziar.Codigo);
            Assert.Equal("INC3139", gravar.Codigo);
            ConferirSigilo(esvaziar);
            ConferirSigilo(gravar);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_em_uso_e_a_inexistente_saem_com_os_codigos_proprios()
    {
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            var inexistente = new NomeObjeto("loja", "nao_existe");
            var semTabela = await Assert.ThrowsAsync<DialetoRecusouException>(() => destino.Gravador.EsvaziarAsync(inexistente, cancelamento));
            var semTabelaNaGravacao = await Assert.ThrowsAsync<DialetoRecusouException>(
                () => destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = inexistente, Colunas = ["id"], Tratadas = [] }, cancelamento));
            Assert.Equal("INC3134", semTabela.Codigo);
            Assert.Equal("INC3134", semTabelaNaGravacao.Codigo);
            await using (var segura = await servidor.AbrirComoAdministradorAsync(banco, cancelamento))
            {
                await using var transacao = await segura.BeginTransactionAsync(cancelamento);
                await using (var travar = new NpgsqlCommand("LOCK TABLE loja.bordas IN ACCESS SHARE MODE", segura, transacao))
                {
                    await travar.ExecuteNonQueryAsync(cancelamento);
                }

                var esvaziar = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.EsvaziarAsync(_bordas, cancelamento));
                var gravar = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento));

                Assert.Equal("INC3137", esvaziar.Codigo);
                Assert.Equal("INC3137", gravar.Codigo);
                ConferirSigilo(esvaziar);
                ConferirSigilo(gravar);
            }

            Assert.Single(await LerBordasAsync(destino, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_que_nao_aceita_a_gravacao_sai_com_o_codigo_proprio()
    {
        // A referenciada por uma chave estrangeira (o motor remove as chaves antes; o dialeto não sabe), a particionada com uma
        // partição estrangeira, a segurança por linha forçada para o dono, e a coluna gerada na lista.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            await servidor.ExecutarAsync(banco, "ALTER TABLE loja.bordas ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY", cancelamento);

            var referenciada = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.EsvaziarAsync(new NomeObjeto("loja", "pedidos"), cancelamento));
            var estrangeira = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.EsvaziarAsync(new NomeObjeto("loja", "leituras"), cancelamento));
            var gerada = await Assert.ThrowsAsync<IncognitoException>(
                () => destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = new NomeObjeto("loja", "calculos"), Colunas = ["id", "valor", "dobro"], Tratadas = [] }, cancelamento));
            var porLinha = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento));

            Assert.Equal("INC3142", referenciada.Codigo);
            Assert.Equal("INC3142", estrangeira.Codigo);
            Assert.Equal("INC3142", gerada.Codigo);
            Assert.Equal("INC3139", porLinha.Codigo);
            ConferirSigilo(referenciada);
            ConferirSigilo(gerada);
            Assert.Equal(2L, await servidor.ConsultarAsync(banco, "SELECT pg_catalog.count(*) FROM loja.pedidos", cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particao_estrangeira_que_aceita_o_esvaziamento_e_recusada_sem_tocar_o_outro_banco()
    {
        // Uma partição do postgres_fdw (em laço para o próprio banco) aceita o TRUNCATE, que chegaria à tabela remota: a
        // recusa vem depois dele, na mesma transação, e nada fica.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE EXTENSION IF NOT EXISTS postgres_fdw;
                CREATE SERVER laco FOREIGN DATA WRAPPER postgres_fdw OPTIONS (host '/var/run/postgresql', dbname '{banco}');
                CREATE USER MAPPING FOR {ServidorPostgreSql.Dono} SERVER laco OPTIONS (user 'postgres', password_required 'false');
                GRANT USAGE ON FOREIGN SERVER laco TO {ServidorPostgreSql.Dono};
                CREATE TABLE loja.remota (ano integer NOT NULL, mes integer NOT NULL);
                INSERT INTO loja.remota VALUES (2027, 1);
                CREATE TABLE loja.fora (ano integer NOT NULL, mes integer NOT NULL) PARTITION BY LIST (ano);
                CREATE FOREIGN TABLE loja.fora_2027 PARTITION OF loja.fora FOR VALUES IN (2027) SERVER laco OPTIONS (schema_name 'loja', table_name 'remota');
                ALTER TABLE loja.fora OWNER TO {ServidorPostgreSql.Dono};
                ALTER FOREIGN TABLE loja.fora_2027 OWNER TO {ServidorPostgreSql.Dono};
                """,
                cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => destino.Gravador.EsvaziarAsync(new NomeObjeto("loja", "fora"), cancelamento));

            Assert.Equal("INC3142", falha.Codigo);
            ConferirSigilo(falha);
            Assert.Equal(1L, await servidor.ConsultarAsync(banco, "SELECT pg_catalog.count(*) FROM loja.remota", cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_valor_que_o_tipo_recusa_e_a_conexao_que_cai_saem_sem_o_valor()
    {
        // O 22P02 traz o valor no texto do servidor; a conexão derrubada no meio sai pelo caso padrão.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 2, cancelamento);
            await servidor.ExecutarAsync(banco, "ALTER TABLE loja.bordas ALTER COLUMN curto TYPE integer USING NULL", cancelamento);
            var tipo = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, _) => lote.Escrever(linha, 11, ServidorPostgreSql.Sentinela), cancelamento));
            Assert.Equal("INC3138", tipo.Codigo);
            ConferirSigilo(tipo);

            var lotes = await LotesDasBordasAsync(destino, cancelamento);
            await using var gravacao = await destino.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento);
            foreach (var lote in lotes)
            {
                for (var linha = 0; linha < lote.Quantidade; linha++)
                {
                    lote.Escrever(linha, 3, ServidorPostgreSql.Sentinela);
                    for (var coluna = 1; coluna <= _colunasDasBordas.Length; coluna++)
                    {
                        if (!lote.EstaEscrita(linha, coluna))
                        {
                            lote.Manter(linha, coluna);
                        }
                    }
                }

                await gravacao.GravarAsync(lote, cancelamento);
            }

            await servidor.ExecutarAsync(
                banco,
                $"SELECT pg_catalog.pg_terminate_backend(a.pid, 5000) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.application_name = '{ConexaoPostgreSql.NomeDaGravacao}'",
                cancelamento);
            var caiu = await Assert.ThrowsAsync<IncognitoException>(() => gravacao.ConcluirAsync(cancelamento));
            Assert.Equal("INC3141", caiu.Codigo);
            ConferirSigilo(caiu);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_login_sem_mais_conexoes_sai_com_o_codigo_proprio()
    {
        // O servidor recusa a conexão da gravação já no pacote de início (o limite do papel, com a conexão de controle aberta).
        await NoDestinoAsync(async (banco, _, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            var papel = "incognito_limitado_" + banco[^8..];
            var senha = Guid.NewGuid().ToString("N");
            await servidor.ExecutarAsync(banco, $"CREATE ROLE {papel} LOGIN PASSWORD '{senha}' CONNECTION LIMIT 1; GRANT USAGE ON SCHEMA loja TO {papel}; GRANT SELECT, INSERT, TRUNCATE ON loja.bordas TO {papel}", cancelamento);
            try
            {
                await using (var limitado = await _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, papel, senha)), cancelamento))
                {
                    var falha = await Assert.ThrowsAsync<IncognitoException>(() => limitado.Gravador.EsvaziarAsync(_bordas, cancelamento));
                    Assert.Equal("INC3126", falha.Codigo);
                    ConferirSigilo(falha);
                    Assert.DoesNotContain(senha, falha.ToString(), StringComparison.Ordinal);
                }
            }
            finally
            {
                await servidor.ExecutarAsync(banco, $"DROP OWNED BY {papel}; DROP ROLE {papel}", CancellationToken.None);
            }
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_restricao_adiada_e_o_texto_trocado_antes_da_conclusao_desfazem_a_gravacao()
    {
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 2, cancelamento);
            await servidor.ExecutarAsync(banco, "ALTER TABLE loja.bordas ADD CONSTRAINT uq_adiada UNIQUE (texto) DEFERRABLE INITIALLY DEFERRED", cancelamento);
            var adiada = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, _) => lote.Escrever(linha, 3, ServidorPostgreSql.Sentinela), cancelamento));
            Assert.Equal("INC3138", adiada.Codigo);
            Assert.Contains("\"uq_adiada\"", adiada.Message, StringComparison.Ordinal);
            ConferirSigilo(adiada);

            ((DestinoPostgreSql)destino).Gravacoes.AntesDaConclusao = (conexao, ct) => SessaoLeituraPostgreSql.ExecutarAsync(conexao, "SET DateStyle = 'SQL, DMY'", ct);
            var trocado = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, chave) => lote.Escrever(linha, 3, $"{ServidorPostgreSql.Sentinela}-{chave}"), cancelamento));
            Assert.Equal("INC3132", trocado.Codigo);
            ConferirSigilo(trocado);
            Assert.All((await LerBordasAsync(destino, cancelamento)).Values, linha => Assert.Null(linha[2]));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_gatilho_adiado_que_troca_o_texto_e_pego_antes_do_commit()
    {
        // Um gatilho de restrição adiado também é código do banco: antecipado antes da conferência, a troca dele é pega.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 2, cancelamento);
            await servidor.ExecutarAsync(
                banco,
                $"CREATE FUNCTION loja.trocar_no_fim() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN PERFORM pg_catalog.set_config('DateStyle', 'SQL, DMY', false); RETURN NULL; END$$; "
                + "CREATE CONSTRAINT TRIGGER tg_no_fim AFTER INSERT ON loja.bordas DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION loja.trocar_no_fim()",
                cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => GravarBordasAsync(destino, (lote, linha, chave) => lote.Escrever(linha, 3, $"{ServidorPostgreSql.Sentinela}-{chave}"), cancelamento));

            Assert.Equal("INC3132", falha.Codigo);
            ConferirSigilo(falha);
            Assert.All((await LerBordasAsync(destino, cancelamento)).Values, linha => Assert.Null(linha[2]));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_lotes_maiores_que_um_bloco_gravam_cada_linha_uma_vez()
    {
        // Lotes de até 256 KiB saem em blocos de 64 KiB: cada bloco enviado sai do buffer.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await servidor.ExecutarAsync(
                banco,
                $"CREATE TABLE loja.larga (id bigint NOT NULL, texto text NOT NULL); "
                + "INSERT INTO loja.larga SELECT g, pg_catalog.repeat('y', 200) FROM pg_catalog.generate_series(1, 5000) g; "
                + $"ALTER TABLE loja.larga OWNER TO {ServidorPostgreSql.Dono}",
                cancelamento);
            var larga = new NomeObjeto("loja", "larga");
            var lotes = new List<LoteLinhas>();
            await using (var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento))
            {
                await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = larga, Colunas = ["id", "texto"] }, cancelamento);
                while (await leitura.LerLoteAsync(cancelamento) is { } lote)
                {
                    lotes.Add(lote);
                }
            }

            await using (var gravacao = await destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = larga, Colunas = ["id", "texto"], Tratadas = [] }, cancelamento))
            {
                foreach (var lote in lotes)
                {
                    await gravacao.GravarAsync(lote, cancelamento);
                }

                Assert.Equal(5000L, await gravacao.ConcluirAsync(cancelamento));
            }

            Assert.Equal(
                "5000|12502500|5000",
                await servidor.ConsultarAsync(banco, "SELECT pg_catalog.count(*) || '|' || pg_catalog.sum(id) || '|' || pg_catalog.count(DISTINCT id) FROM loja.larga", cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_nao_usa_a_conexao_de_controle()
    {
        // Outra parte do dialeto ocupada na conexão de controle: o esvaziamento abre e confere a conexão própria sem ela.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            await using var ocupar = new NpgsqlCommand("SELECT pg_catalog.pg_sleep(3)", ((DestinoPostgreSql)destino).Controle.Conexao);
            var ocupada = ocupar.ExecuteNonQueryAsync(cancelamento);
            await Task.Delay(300, cancelamento);

            await destino.Gravador.EsvaziarAsync(_bordas, cancelamento);

            Assert.False(ocupada.IsCompleted);
            await ocupada;
            Assert.Empty(await LerBordasAsync(destino, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_descarte_poe_prazo_no_fluxo_antes_de_cancelar()
    {
        // Com o tempo-limite de comando desligado, o cancelamento sem prazo no fluxo esperaria para sempre um servidor parado.
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await CriarBordasAsync(banco, 1, cancelamento);
            (int Leitura, int Escrita)? prazos = null;
            ((DestinoPostgreSql)destino).Gravacoes.AntesDoCancelamento = fluxo => prazos = (fluxo.ReadTimeout, fluxo.WriteTimeout);

            var gravacao = await destino.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento);
            await gravacao.DisposeAsync();

            var esperado = (int)LeituraTabelaPostgreSql.PrazoDoCancelamento.TotalMilliseconds;
            Assert.Equal((esperado, esperado), prazos);
            Assert.Single(await LerBordasAsync(destino, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_cancelada_no_meio_solta_a_conexao_e_deixa_a_tabela()
    {
        await NoDestinoAsync(async (banco, destino, cancelamento) =>
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE TABLE loja.larga (id bigint NOT NULL, texto text NOT NULL);
                INSERT INTO loja.larga SELECT g, pg_catalog.repeat('y', 200) FROM pg_catalog.generate_series(1, 20000) g;
                ALTER TABLE loja.larga OWNER TO {ServidorPostgreSql.Dono};
                """,
                cancelamento);
            var larga = new NomeObjeto("loja", "larga");
            var lotes = new List<LoteLinhas>();
            await using (var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento))
            {
                await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = larga, Colunas = ["id", "texto"] }, cancelamento);
                while (await leitura.LerLoteAsync(cancelamento) is { } lote)
                {
                    lotes.Add(lote);
                }
            }

            using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            var gravacao = await destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = larga, Colunas = ["id", "texto"], Tratadas = [] }, cancelamento);
            Exception? erro = null;
            try
            {
                await gravacao.GravarAsync(lotes[0], parar.Token);
                var gravar = gravacao.GravarAsync(lotes[1], parar.Token);
                await parar.CancelAsync();
                erro = await Record.ExceptionAsync(async () =>
                {
                    await gravar;
                    await gravacao.GravarAsync(lotes[2], parar.Token);
                });
            }
            finally
            {
                await gravacao.DisposeAsync();
                foreach (var lote in lotes)
                {
                    lote.Dispose();
                }
            }

            Assert.IsAssignableFrom<OperationCanceledException>(erro);
            Assert.Equal(20000L, await servidor.ConsultarAsync(banco, "SELECT pg_catalog.count(*) FROM loja.larga", cancelamento));
            var conexoes = await servidor.ConsultarAsync(
                banco,
                $"SELECT pg_catalog.count(*) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.application_name = '{ConexaoPostgreSql.NomeDaGravacao}'",
                cancelamento);
            Assert.True(conexoes is 0L || await EsperarSemGravacaoAsync(banco, cancelamento), "A conexão da gravação cancelada ficou aberta.");
        });
    }

    // A saída das conexões fechadas é assíncrona no servidor: espera até um prazo.
    private async Task<bool> EsperarSemGravacaoAsync(string banco, CancellationToken cancelamento)
    {
        var prazo = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < prazo)
        {
            if (await servidor.ConsultarAsync(
                banco,
                $"SELECT pg_catalog.count(*) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.application_name = '{ConexaoPostgreSql.NomeDaGravacao}'",
                cancelamento) is 0L)
            {
                return true;
            }

            await Task.Delay(100, cancelamento);
        }

        return false;
    }

    // Os lotes das bordas, lidos pela sessão do próprio destino, que é solta antes de gravar.
    private static async Task<List<LoteLinhas>> LotesDasBordasAsync(DialetoDestino destino, CancellationToken cancelamento)
    {
        var lotes = new List<LoteLinhas>();
        await using var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
        await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(LeituraDasBordas(), cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            lotes.Add(lote);
        }

        return lotes;
    }

    private static PedidoGravacao PedidoDasBordas() => new()
    {
        Tabela = _bordas,
        Colunas = ["id", .. _colunasDasBordas.Select(coluna => coluna.Nome), "obrigatorio", "curto"],
        Tratadas = [.. _colunasDasBordas.Select((coluna, i) => new ColunaConvertida { Indice = i + 1, Tipo = coluna.Tipo })],
    };

    private static PedidoLeituraTabela LeituraDasBordas() => new()
    {
        Tabela = _bordas,
        Colunas = ["id", .. _colunasDasBordas.Select(coluna => coluna.Nome), "obrigatorio", "curto"],
        Convertidas =
        [
            new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro },
            .. _colunasDasBordas.Select((coluna, i) => new ColunaConvertida { Indice = i + 1, Tipo = coluna.Tipo }),
            new ColunaConvertida { Indice = _colunasDasBordas.Length + 1, Tipo = TipoColuna.Texto },
            new ColunaConvertida { Indice = _colunasDasBordas.Length + 2, Tipo = TipoColuna.Texto },
        ],
    };

    // Escreve os valores da linha nas colunas canônicas (de 1 em diante).
    private static void Escrever(LoteLinhas lote, int linha, object?[] valores)
    {
        for (var coluna = 0; coluna < valores.Length; coluna++)
        {
            lote.Escrever(linha, coluna + 1, valores[coluna]);
        }
    }

    // O decimal compara também a escala.
    private static bool IgualComEscala(object? lido, object? esperado) => (lido, esperado) switch
    {
        (null, null) => true,
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (decimal a, decimal b) => a == b && a.Scale == b.Scale,
        (not null, not null) => lido.GetType() == esperado.GetType() && lido.Equals(esperado),
        _ => false,
    };

    // A tabela das bordas no destino, do dono, com as chaves de 1 a n e o resto nulo (fora a coluna obrigatória).
    private Task CriarBordasAsync(string banco, int linhas, CancellationToken cancelamento) =>
        servidor.ExecutarAsync(
            banco,
            $"""
            CREATE TABLE loja.bordas (
                id bigint PRIMARY KEY, inteiro bigint, valor numeric, texto text, ativo boolean, dia date, momento timestamp,
                instante timestamptz, identificador uuid, conteudo bytea, obrigatorio text NOT NULL DEFAULT 'x', curto varchar(3));
            INSERT INTO loja.bordas (id) SELECT g FROM pg_catalog.generate_series(1, {linhas}) g;
            ALTER TABLE loja.bordas OWNER TO {ServidorPostgreSql.Dono};
            """,
            cancelamento);

    // Lê as bordas pela sessão do próprio destino, solta a sessão (a gravação esvazia a tabela) e grava os lotes com o
    // tratamento dado a cada linha; as tratadas não tocadas ficam mantidas.
    private static async Task GravarBordasAsync(DialetoDestino destino, Action<LoteLinhas, int, long> tratar, CancellationToken cancelamento)
    {
        var lotes = new List<LoteLinhas>();
        await using (var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento))
        {
            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(LeituraDasBordas(), cancelamento);
            while (await leitura.LerLoteAsync(cancelamento) is { } lote)
            {
                lotes.Add(lote);
            }
        }

        await using var gravacao = await destino.Gravador.AbrirAsync(PedidoDasBordas(), cancelamento);
        foreach (var lote in lotes)
        {
            for (var linha = 0; linha < lote.Quantidade; linha++)
            {
                lote.TentarLer(linha, 0, out var chave);
                tratar(lote, linha, (long)chave!);
                for (var coluna = 1; coluna <= _colunasDasBordas.Length; coluna++)
                {
                    if (!lote.EstaEscrita(linha, coluna))
                    {
                        lote.Manter(linha, coluna);
                    }
                }
            }

            await gravacao.GravarAsync(lote, cancelamento);
        }

        await gravacao.ConcluirAsync(cancelamento);
    }

    // As colunas canônicas das bordas pela chave, lidas pela sessão do destino.
    private static async Task<Dictionary<long, object?[]>> LerBordasAsync(DialetoDestino destino, CancellationToken cancelamento)
    {
        var lidas = new Dictionary<long, object?[]>();
        await using var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
        await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(LeituraDasBordas(), cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            using (lote)
            {
                for (var linha = 0; linha < lote.Quantidade; linha++)
                {
                    lote.TentarLer(linha, 0, out var chave);
                    var valores = new object?[_colunasDasBordas.Length];
                    for (var coluna = 0; coluna < valores.Length; coluna++)
                    {
                        Assert.True(lote.TentarLer(linha, coluna + 1, out valores[coluna]));
                    }

                    lidas[(long)chave!] = valores;
                }
            }
        }

        return lidas;
    }

    private async Task NoDestinoAsync(Func<string, DialetoDestino, CancellationToken, Task> teste)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var destino = await _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
            await teste(banco, destino, cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
