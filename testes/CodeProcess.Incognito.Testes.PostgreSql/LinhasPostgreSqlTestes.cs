using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a leitura das linhas do PostgreSQL faz além do que a suíte confere: o desescape e a conversão do texto do
/// <c>COPY</c> nas bordas, os lotes limitados e numerados, a linha larga, o cancelamento que deixa o leitor no instante, os
/// parâmetros do texto impostos contra os do banco, o texto trocado antes do COPY e durante ele, a falha do servidor no meio,
/// a falha sem o nome nem o valor, o lote que apaga o original escrito e devolve os buffers limpos, e o corte das linhas.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed partial class LinhasPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private static readonly ReferenciaColuna _lojas = new(new NomeObjeto("loja", "lojas"), "id");
    private static readonly byte[] _conteudo = [0x00, 0xff, 0x5c, 0x0a];

    // Valores do banco para os parâmetros que mudam o texto do COPY, diferentes dos que o dialeto impõe.
    private static readonly string[] _parametrosDoBanco =
    [
        "DateStyle = 'SQL, DMY'", "IntervalStyle = 'sql_standard'", "TimeZone = 'America/Sao_Paulo'", "extra_float_digits = 0",
        "bytea_output = 'escape'", "array_nulls = off", "xmloption = 'document'", "lc_monetary = 'C.UTF-8'",
    ];

    private readonly ProvedorPostgreSql _provedor = new();

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"\b\f\n\r\t\v\\", "\b\f\n\r\t\v\\")]
    [InlineData(@"\101\7\x41\x4", "A\u0007A\u0004")]
    [InlineData(@"\q\", "q\\")]
    [InlineData(@"\N", "N")]
    public void O_desescape_cobre_as_sequencias_do_copy(string campo, string esperado)
    {
        var bytes = Encoding.UTF8.GetBytes(campo);
        var destino = new byte[bytes.Length];

        var escritos = TextoDoCopy.Desescapar(bytes, destino);

        Assert.Equal(esperado, Encoding.Latin1.GetString(destino, 0, escritos));
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(TipoColuna.Inteiro, "-9223372036854775808", true)]
    [InlineData(TipoColuna.Inteiro, "9223372036854775808", false)]
    [InlineData(TipoColuna.Inteiro, "1.5", false)]
    [InlineData(TipoColuna.Decimal, "12.30", true)]
    [InlineData(TipoColuna.Decimal, "-0.01", true)]
    [InlineData(TipoColuna.Decimal, "NaN", false)]
    [InlineData(TipoColuna.Decimal, "Infinity", false)]
    [InlineData(TipoColuna.Decimal, "0.1234567890123456789012345678901", false)]
    [InlineData(TipoColuna.Decimal, "0.000000000000000000000000000001", false)]
    [InlineData(TipoColuna.Decimal, "1.00000000000000000000000000001", false)]
    [InlineData(TipoColuna.Decimal, "1000000000000000000000000000000", false)]
    [InlineData(TipoColuna.Booleano, "t", true)]
    [InlineData(TipoColuna.Booleano, "true", false)]
    [InlineData(TipoColuna.Data, "2026-10-29", true)]
    [InlineData(TipoColuna.Data, "0044-03-15 BC", false)]
    [InlineData(TipoColuna.Data, "10000-01-01", false)]
    [InlineData(TipoColuna.Data, "infinity", false)]
    [InlineData(TipoColuna.DataHora, "2026-10-29 12:34:56", true)]
    [InlineData(TipoColuna.DataHora, "2026-10-29 12:34:56.000001", true)]
    [InlineData(TipoColuna.DataHora, "2026-10-29 12:34:56 BC", false)]
    [InlineData(TipoColuna.DataHora, "-infinity", false)]
    [InlineData(TipoColuna.DataHoraComFuso, "2026-10-29 12:34:56.5+00", true)]
    [InlineData(TipoColuna.DataHoraComFuso, "2026-10-29 12:34:56+01", false)]
    [InlineData(TipoColuna.DataHoraComFuso, "0001-01-01 00:00:00+00 BC", false)]
    [InlineData(TipoColuna.Uuid, "0b6f1a52-6a0d-4b6c-9a52-6a0d4b6c9a52", true)]
    [InlineData(TipoColuna.Uuid, "0b6f1a526a0d4b6c9a526a0d4b6c9a52", false)]
    [InlineData(TipoColuna.Binario, @"\x00ff", true)]
    [InlineData(TipoColuna.Binario, @"\x0", false)]
    [InlineData(TipoColuna.Binario, @"\377", false)]
    public void A_conversao_aceita_so_o_que_tem_forma_canonica(TipoColuna tipo, string texto, bool converte)
    {
        Assert.Equal(converte, TextoDoCopy.TentarConverter(tipo, Encoding.UTF8.GetBytes(texto), out var valor));
        Assert.True(converte ? valor is not null && valor.GetType() == FormaCanonica.Tipo(tipo) : valor is null);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void A_conversao_guarda_a_escala_a_fracao_e_o_fuso_e_recusa_texto_que_nao_e_utf8()
    {
        Assert.True(TextoDoCopy.TentarConverter(TipoColuna.Decimal, "12.30"u8, out var numero));
        Assert.Equal("12.30", ((decimal)numero!).ToString(CultureInfo.InvariantCulture));
        Assert.True(TextoDoCopy.TentarConverter(TipoColuna.DataHora, "2026-10-29 12:34:56.000001"u8, out var dataHora));
        Assert.Equal((10L, DateTimeKind.Unspecified), (((DateTime)dataHora!).Ticks % TimeSpan.TicksPerSecond, ((DateTime)dataHora).Kind));
        Assert.True(TextoDoCopy.TentarConverter(TipoColuna.DataHoraComFuso, "2026-10-29 12:34:56+00"u8, out var comFuso));
        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)comFuso!).Offset);
        Assert.True(TextoDoCopy.TentarConverter(TipoColuna.Binario, @"\x00FF"u8, out var binario));
        Assert.Equal(new byte[] { 0, 255 }, (byte[])binario!);
        Assert.True(TextoDoCopy.TentarConverter(TipoColuna.Texto, "ação €"u8, out var texto));
        Assert.Equal("ação €", texto);
        Assert.False(TextoDoCopy.TentarConverter(TipoColuna.Texto, [0x61, 0xC3], out _));
        Assert.True(TextoDoCopy.Nulo(@"\N"u8));
        Assert.False(TextoDoCopy.Nulo(@"\\N"u8));
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public void A_conexao_impoe_o_texto_do_copy_e_o_fuso_pelo_parametro_de_inicio(bool origem, string soLeitura)
    {
        var construtor = new Npgsql.NpgsqlConnectionStringBuilder("Host=servidor;Database=banco;Username=u;Password=p;Timezone=America/Sao_Paulo");

        ConexaoPostgreSql.Impor(construtor, origem);

        Assert.Equal("UTC", construtor.Timezone);
        Assert.Contains(ConexaoPostgreSql.OpcoesDoTexto, construtor.Options, StringComparison.Ordinal);
        Assert.Contains("-c default_transaction_read_only=" + soLeitura, construtor.Options, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void O_lote_apaga_o_original_escrito_e_devolve_os_buffers_limpos()
    {
        var pool = new PoolQueConfere();
        var pedido = new PedidoLeituraTabela
        {
            Tabela = _lojas.Tabela,
            Colunas = ["id", "texto"],
            Convertidas = [new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
        };
        var linhas = "1\tzqSENTINELAqz\n2\tmantido\n"u8.ToArray();
        var dados = pool.Rent(linhas.Length);
        linhas.CopyTo(dados, 0);
        var inicios = ArrayPool<int>.Shared.Rent(3);
        (inicios[0], inicios[1], inicios[2]) = (0, 16, linhas.Length);

        var lote = new LoteLinhasPostgreSql(pedido, dados, inicios, 2, pool);
        Assert.True(lote.TentarLer(0, 1, out var original));
        Assert.Equal(ServidorPostgreSql.Sentinela, original);
        lote.Escrever(0, 1, "trocado");
        lote.Manter(1, 1);

        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, Encoding.UTF8.GetString(dados), StringComparison.Ordinal);
        Assert.Equal("2\tmantido", Encoding.UTF8.GetString(lote.Linha(1)));
        lote.Dispose();
        Assert.True(pool.DevolvidosLimpos > 0 && pool.DevolvidosSujos == 0);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_grande_sai_em_lotes_limitados_e_numerados_e_a_linha_larga_volta_inteira()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE TABLE loja.muitas (id bigint, texto text);
                INSERT INTO loja.muitas SELECT g, 'linha ' || g FROM pg_catalog.generate_series(1, 2500) g;
                INSERT INTO loja.muitas VALUES (2501, pg_catalog.repeat('larga ', 60000));
                GRANT SELECT ON loja.muitas TO {ServidorPostgreSql.Leitor};
                """,
                cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            var pedido = new PedidoLeituraTabela
            {
                Tabela = new NomeObjeto("loja", "muitas"),
                Colunas = ["id", "texto"],
                Convertidas = [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
            };

            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(pedido, cancelamento);
            var (lotes, linhas, larga) = (0, 0L, (string?)null);
            while (await leitura.LerLoteAsync(cancelamento) is { } lote)
            {
                using (lote)
                {
                    Assert.True(lote.Quantidade <= CortadorDeLinhas.LinhasPorLote, $"Um lote com {lote.Quantidade} linhas.");
                    Assert.Equal(linhas + 1, lote.PrimeiraLinha);
                    for (var i = 0; i < lote.Quantidade; i++)
                    {
                        Assert.True(lote.TentarLer(i, 0, out var id));
                        if ((long)id! == 2501)
                        {
                            Assert.True(lote.TentarLer(i, 1, out var texto));
                            larga = (string)texto!;
                        }
                    }

                    lotes++;
                    linhas += lote.Quantidade;
                }
            }

            Assert.Equal(2501L, linhas);
            Assert.True(lotes >= 3, $"{lotes} lotes para 2.501 linhas.");
            Assert.Equal(string.Concat(Enumerable.Repeat("larga ", 60000)), larga);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_controles_e_as_barras_voltam_iguais()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE TABLE loja.controles (id bigint, texto text);
                INSERT INTO loja.controles VALUES (1, E'a\x01b\x07c\x1bd\x7fe\bf\fg\x0bh\ri\nj\tk\\l'), (2, E'\\N'), (3, '');
                GRANT SELECT ON loja.controles TO {ServidorPostgreSql.Leitor};
                """,
                cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);

            var lidos = await LerTextosAsync(sessao.Leitores[0], new NomeObjeto("loja", "controles"), cancelamento);

            Assert.Equal("a\u0001b\u0007c\u001bd\u007fe\bf\fg\vh\ri\nj\tk\\l", lidos[1]);
            Assert.Equal("\\N", lidos[2]);
            Assert.Equal("", lidos[3]);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_cancelamento_no_meio_da_leitura_deixa_o_leitor_no_instante()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            var longa = await CriarLongaAsync(banco, 300_000, cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco, Travadas = [_lojas.Tabela] }, cancelamento);
            var leitor = sessao.Leitores[0];
            using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);

            // O cancelamento chega por outra thread depois de três lotes, no meio do COPY (a tabela tem dezenas de megabytes):
            // a leitura para com o cancelamento dentro do prazo, e o descarte volta o leitor ao instante.
            var leitura = await leitor.AbrirTabelaAsync(PedidoDeTexto(longa), parar.Token);
            var lotes = 0;
            var relogio = new System.Diagnostics.Stopwatch();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                while (await leitura.LerLoteAsync(parar.Token) is { } lote)
                {
                    lote.Dispose();
                    if (++lotes == 3)
                    {
                        relogio.Start();
                        _ = Task.Run(parar.Cancel, cancelamento);
                    }
                }
            });
            await leitura.DisposeAsync();
            Assert.True(relogio.Elapsed < LeituraTabelaPostgreSql.PrazoDoCancelamento, $"O cancelamento e o descarte levaram {relogio.Elapsed.TotalSeconds:0.0} s.");
            await servidor.ExecutarAsync(banco, "INSERT INTO loja.lojas (id) VALUES (777)", cancelamento);

            Assert.Equal(91L, await leitor.LerMaiorValorAsync(_lojas, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_lote_que_sai_do_buffer_depois_do_cancelamento_nunca_chega_a_quem_chamou()
    {
        // A base recusa o token já cancelado antes do dialeto; aqui o núcleo é chamado direto, como se o pedido chegasse
        // no meio da leitura: o que o Npgsql ainda tem no buffer não sai como lote.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            var longa = await CriarLongaAsync(banco, 100_000, cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(PedidoDeTexto(longa), cancelamento);
            (await leitura.LerLoteAsync(cancelamento))!.Dispose();
            using var cancelado = new CancellationTokenSource();
            await cancelado.CancelAsync();
            var nucleo = typeof(LeituraTabelaPostgreSql).GetMethod("LerLoteAsyncCore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => (Task<LoteLinhas?>)nucleo.Invoke(leitura, [cancelado.Token])!);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_parametros_do_texto_valem_mesmo_com_outros_no_banco()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            foreach (var parametro in _parametrosDoBanco)
            {
                await servidor.ExecutarAsync(banco, $"ALTER DATABASE \"{banco}\" SET {parametro}", cancelamento);
            }

            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            var pedido = new PedidoLeituraTabela
            {
                Tabela = new NomeObjeto("loja", "tipos"),
                Colunas = ["id", "dia", "instante", "conteudo"],
                Convertidas =
                [
                    new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Data },
                    new ColunaConvertida { Indice = 2, Tipo = TipoColuna.DataHoraComFuso }, new ColunaConvertida { Indice = 3, Tipo = TipoColuna.Binario },
                ],
            };

            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(pedido, cancelamento);
            var achou = false;
            while (await leitura.LerLoteAsync(cancelamento) is { } lote)
            {
                using (lote)
                {
                    for (var i = 0; i < lote.Quantidade; i++)
                    {
                        if (lote.TentarLer(i, 0, out var id) && (long)id! == 103)
                        {
                            Assert.True(lote.TentarLer(i, 1, out var dia) && (DateOnly)dia! == new DateOnly(2026, 10, 29));
                            Assert.True(lote.TentarLer(i, 2, out var instante) && ((DateTimeOffset)instante!).Offset == TimeSpan.Zero);
                            Assert.True(lote.TentarLer(i, 3, out var conteudo) && ((byte[])conteudo!).AsSpan().SequenceEqual(_conteudo));
                            achou = true;
                        }
                    }
                }
            }

            Assert.True(achou, "A linha 103 da loja.tipos não foi lida.");
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [MemberData(nameof(Trocas))]
    public async Task A_sessao_com_o_texto_trocado_e_recusada_antes_de_ler(string troca)
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            var leitor = (LeitorDaSessaoPostgreSql)sessao.Leitores[0];
            await SessaoLeituraPostgreSql.ExecutarAsync(leitor.Conexao, troca, cancelamento);

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => leitor.AbrirTabelaAsync(PedidoDeTexto(_lojas.Tabela), cancelamento));

            Assert.Equal("INC3132", falha.Codigo);
            ConferirSigilo(falha);
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [MemberData(nameof(Trocas))]
    public async Task O_texto_trocado_durante_o_copy_e_pego_na_conclusao_e_desfeito(string troca)
    {
        // A troca entre o fim do COPY e a conferência, como a faria código do banco no meio da leitura: a leitura falha, e a
        // volta ao ponto desfaz a troca, com o leitor no instante.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            ((OrigemPostgreSql)origem).Sessoes.AntesDaConclusao = (conexao, ct) => SessaoLeituraPostgreSql.ExecutarAsync(conexao, troca, ct);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco, Travadas = [_lojas.Tabela] }, cancelamento);
            var leitor = sessao.Leitores[0];

            var falha = await Assert.ThrowsAsync<IncognitoException>(async () =>
            {
                await using var leitura = await leitor.AbrirTabelaAsync(PedidoDeTexto(_lojas.Tabela), cancelamento);
                while (await leitura.LerLoteAsync(cancelamento) is { } lote)
                {
                    lote.Dispose();
                }
            });

            Assert.Equal("INC3132", falha.Codigo);
            ConferirSigilo(falha);
            await servidor.ExecutarAsync(banco, "INSERT INTO loja.lojas (id) VALUES (777)", cancelamento);
            Assert.Equal(91L, await leitor.LerMaiorValorAsync(_lojas, cancelamento));
        });
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_falha_do_servidor_no_meio_do_copy_sai_como_falha_e_nunca_como_fim(bool derrubar)
    {
        // O comando cancelado pelo servidor desfaz só o ponto, e o leitor segue no instante; a conexão derrubada leva o leitor.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            var longa = await CriarLongaAsync(banco, 100_000, cancelamento);
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco, Travadas = [_lojas.Tabela] }, cancelamento);
            var leitor = (LeitorDaSessaoPostgreSql)sessao.Leitores[0];
            var processo = leitor.Conexao.ProcessID;
            var linhas = 0L;

            var falha = await Assert.ThrowsAsync<IncognitoException>(async () =>
            {
                await using var leitura = await leitor.AbrirTabelaAsync(PedidoDeTexto(longa), cancelamento);
                using (var primeiro = await leitura.LerLoteAsync(cancelamento))
                {
                    linhas += primeiro!.Quantidade;
                }

                var funcao = derrubar ? "pg_terminate_backend" : "pg_cancel_backend";
                await servidor.ExecutarAsync(banco, $"SELECT pg_catalog.{funcao}({processo})", cancelamento);
                while (await leitura.LerLoteAsync(cancelamento) is { } lote)
                {
                    using (lote)
                    {
                        linhas += lote.Quantidade;
                    }
                }
            });

            Assert.True(linhas < 100_000, $"{linhas} linhas lidas antes da falha.");
            Assert.Equal("INC3133", falha.Codigo);
            ConferirSigilo(falha);
            if (derrubar)
            {
                var depois = await Assert.ThrowsAsync<IncognitoException>(() => leitor.LerMaiorValorAsync(_lojas, cancelamento));
                Assert.Equal("INC3135", depois.Codigo);
                ConferirSigilo(depois);
            }
            else
            {
                await servidor.ExecutarAsync(banco, "INSERT INTO loja.lojas (id) VALUES (777)", cancelamento);
                Assert.Equal(91L, await leitor.LerMaiorValorAsync(_lojas, cancelamento));
            }
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_leitor_com_a_conexao_derrubada_parado_falha_com_o_codigo_proprio()
    {
        // A queda só aparece na primeira conversa com o servidor: o maior valor e a leitura das linhas saem do dialeto.
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            var leitor = (LeitorDaSessaoPostgreSql)sessao.Leitores[0];
            await servidor.ExecutarAsync(banco, $"SELECT pg_catalog.pg_terminate_backend({leitor.Conexao.ProcessID}, 5000)", cancelamento);

            var maior = await Assert.ThrowsAsync<IncognitoException>(() => leitor.LerMaiorValorAsync(_lojas, cancelamento));
            var linhas = await Assert.ThrowsAsync<IncognitoException>(() => leitor.AbrirTabelaAsync(PedidoDeTexto(_lojas.Tabela), cancelamento));

            Assert.Equal("INC3135", maior.Codigo);
            Assert.Equal("INC3135", linhas.Codigo);
            ConferirSigilo(maior);
            ConferirSigilo(linhas);
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_falha_da_leitura_nao_traz_o_nome_da_coluna_nem_o_texto_do_servidor()
    {
        await NoBancoAsync(async (banco, cancelamento) =>
        {
            await using var origem = await ConectarAsync(banco, cancelamento);
            await using var sessao = await origem.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
            var leitor = sessao.Leitores[0];
            var pedido = new PedidoLeituraTabela { Tabela = _lojas.Tabela, Colunas = [ServidorPostgreSql.Sentinela] };

            var falha = await Assert.ThrowsAsync<IncognitoException>(() => leitor.AbrirTabelaAsync(pedido, cancelamento));

            Assert.Equal("INC3133", falha.Codigo);
            Assert.Null(falha.InnerException);
            ConferirSigilo(falha);
            Assert.Equal(91L, await leitor.LerMaiorValorAsync(_lojas, cancelamento));
        });
    }

    // Cada troca do que a conferência guarda, como código do banco a faria no meio da transação.
    public static TheoryData<string> Trocas =>
    [
        "SET DateStyle = 'SQL, DMY'", "SET IntervalStyle = 'sql_standard'", "SET TimeZone = 'America/Sao_Paulo'", "SET extra_float_digits = 0",
        "SET bytea_output = 'escape'", "SET array_nulls = off", "SET xmloption = 'document'", "SET lc_monetary = 'C.UTF-8'",
        "SET search_path = loja, pg_catalog", "SET row_security = on",
    ];

    private static PedidoLeituraTabela PedidoDeTexto(NomeObjeto tabela) => new()
    {
        Tabela = tabela,
        Colunas = ["id"],
        Convertidas = [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }],
    };

    // Os textos de uma tabela (id, texto), pela chave.
    private static async Task<Dictionary<long, string?>> LerTextosAsync(LeitorDaSessao leitor, NomeObjeto tabela, CancellationToken cancelamento)
    {
        var pedido = new PedidoLeituraTabela
        {
            Tabela = tabela,
            Colunas = ["id", "texto"],
            Convertidas = [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
        };
        var lidos = new Dictionary<long, string?>();
        await using var leitura = await leitor.AbrirTabelaAsync(pedido, cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            using (lote)
            {
                for (var i = 0; i < lote.Quantidade; i++)
                {
                    Assert.True(lote.TentarLer(i, 0, out var id));
                    Assert.True(lote.TentarLer(i, 1, out var texto));
                    lidos[(long)id!] = (string?)texto;
                }
            }
        }

        return lidos;
    }

    // Nenhuma falha traz o sentinela, a senha do leitor ou um identificador de instante.
    private void ConferirSigilo(Exception falha)
    {
        var texto = falha.ToString();
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(ServidorPostgreSql.SentinelaCurto, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(servidor.SenhaLeitor, texto, StringComparison.Ordinal);
        Assert.DoesNotMatch(Instante(), texto);
    }

    // Uma tabela de linhas largas (200 bytes de texto), que o login de leitura lê.
    private async Task<NomeObjeto> CriarLongaAsync(string banco, int linhas, CancellationToken cancelamento)
    {
        await servidor.ExecutarAsync(
            banco,
            $"""
            CREATE TABLE loja.longa (id bigint, texto text);
            INSERT INTO loja.longa SELECT g, pg_catalog.repeat('x', 200) FROM pg_catalog.generate_series(1, {linhas}) g;
            GRANT SELECT ON loja.longa TO {ServidorPostgreSql.Leitor};
            """,
            cancelamento);
        return new NomeObjeto("loja", "longa");
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

    [GeneratedRegex("[0-9A-F]{8}-[0-9A-F]{8}-[0-9]{1,10}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Instante();

    // Um pool que confere, a cada devolução, se o array volta limpo.
    private sealed class PoolQueConfere : ArrayPool<byte>
    {
        internal int DevolvidosLimpos { get; private set; }

        internal int DevolvidosSujos { get; private set; }

        internal int MaiorPedido { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            MaiorPedido = Math.Max(MaiorPedido, minimumLength);
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            if (clearArray)
            {
                Array.Clear(array);
            }

            if (Array.TrueForAll(array, valor => valor == 0))
            {
                DevolvidosLimpos++;
            }
            else
            {
                DevolvidosSujos++;
            }
        }
    }
}
