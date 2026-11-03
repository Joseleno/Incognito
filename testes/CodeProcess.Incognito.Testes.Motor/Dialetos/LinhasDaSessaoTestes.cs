using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// As regras da leitura das linhas que a base do contrato garante, sem depender do dialeto: o pedido conferido, o leitor
/// ocupado até o descarte da leitura, uma chamada por vez, a numeração dos lotes, o valor lido e o escrito na forma canônica
/// exata, a célula escrita uma vez e não lida depois, e o descarte único que nunca lança.
/// </summary>
public sealed class LinhasDaSessaoTestes
{
    private static readonly NomeObjeto _tabela = new("loja", "tipos");
    private static readonly ReferenciaColuna _coluna = new(_tabela, "id");

    [Fact]
    public void O_pedido_confere_as_colunas_e_as_convertidas()
    {
        Assert.Throws<ArgumentNullException>(() => new PedidoLeituraTabela { Tabela = null!, Colunas = ["id"] });
        Assert.Throws<ArgumentNullException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = null! });
        Assert.Throws<ArgumentException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = [] });
        Assert.Throws<ArgumentException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = [""] });
        Assert.Throws<ArgumentException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "id"] });
        Assert.Throws<ArgumentNullException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id"], Convertidas = [null!] });
        Assert.Throws<ArgumentException>(() => new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id"], Convertidas = [Convertida(0, TipoColuna.Inteiro), Convertida(0, TipoColuna.Texto)] });
        Assert.Throws<ArgumentOutOfRangeException>(() => Convertida(-1, TipoColuna.Inteiro));
        Assert.Throws<ArgumentOutOfRangeException>(() => Convertida(0, (TipoColuna)99));

        var colunas = new List<string> { "id", "texto" };
        var pedido = new PedidoLeituraTabela { Tabela = _tabela, Colunas = colunas, Convertidas = [Convertida(1, TipoColuna.Texto)] };
        colunas.Add("outra");
        Assert.Equal(2, pedido.Colunas.Count);
        Assert.DoesNotContain("tipos", pedido.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("texto", pedido.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_convertida_fora_das_colunas_e_recusada_antes_do_dialeto()
    {
        var leitor = new LeitorDeLinhas();
        await using var sessao = new SessaoSimples([leitor]);
        var pedido = new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id"], Convertidas = [Convertida(1, TipoColuna.Inteiro)] };

        await Assert.ThrowsAsync<ArgumentException>(() => leitor.AbrirTabelaAsync(pedido, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => leitor.AbrirTabelaAsync(null!, TestContext.Current.CancellationToken));
        Assert.Equal(0, leitor.Aberturas);
    }

    [Fact]
    public async Task O_leitor_fica_ocupado_ate_o_descarte_da_leitura()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas();
        await using var sessao = new SessaoSimples([leitor]);
        var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);

        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.LerMaiorValorAsync(_coluna, cancelamento));
        await leitura.DisposeAsync();
        await leitura.DisposeAsync();

        Assert.Equal(1, leitor.UltimaLeitura!.Descartes);
        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
        await (await leitor.AbrirTabelaAsync(Pedido(), cancelamento)).DisposeAsync();
    }

    [Fact]
    public async Task A_abertura_que_falha_ou_devolve_leitura_invalida_libera_o_leitor()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Falhar = true };
        var outro = new LeitorDeLinhas();
        await using var sessao = new SessaoSimples([leitor, outro]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        leitor.Falhar = false;
        leitor.DevolverNula = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        leitor.DevolverNula = false;

        // A leitura de outro leitor não volta, nem é descartada (liberaria o outro).
        var deOutro = await outro.AbrirTabelaAsync(Pedido(), cancelamento);
        leitor.Reaproveitar = deOutro;
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        Assert.Equal(0, outro.UltimaLeitura!.Descartes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => outro.LerMaiorValorAsync(_coluna, cancelamento));
        leitor.Reaproveitar = null;

        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
        await deOutro.DisposeAsync();
    }

    [Fact]
    public async Task Os_lotes_sao_numerados_contados_e_lidos_um_por_vez()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [2, 3] };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);

        using var primeiro = await leitura.LerLoteAsync(cancelamento);
        Assert.Equal((1L, 2, 2L), (primeiro!.PrimeiraLinha, primeiro.Quantidade, leitura.Linhas));
        leitor.UltimaLeitura!.Segurar = true;
        var segurado = leitura.LerLoteAsync(cancelamento);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
        leitor.UltimaLeitura.Soltar();
        using var segundo = await segurado;
        Assert.Equal((3L, 5L), (segundo!.PrimeiraLinha, leitura.Linhas));

        Assert.Null(await leitura.LerLoteAsync(cancelamento));
        Assert.Null(await leitura.LerLoteAsync(cancelamento));
        Assert.Equal(3, leitor.UltimaLeitura.Chamadas);
    }

    [Fact]
    public async Task O_lote_pedido_com_o_token_cancelado_e_recusado_antes_do_dialeto()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1] };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leitura.LerLoteAsync(cancelado.Token));
        Assert.Equal(0, leitor.UltimaLeitura!.Chamadas);
        using var lote = await leitura.LerLoteAsync(cancelamento);
        Assert.Equal(1L, lote!.PrimeiraLinha);
    }

    [Fact]
    public async Task O_lote_de_outro_pedido_e_recusado_e_descartado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1], LoteDeOutroPedido = true };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);

        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
        Assert.True(leitor.UltimaLeitura!.Devolvidos.Single().Descartado);
    }

    [Fact]
    public async Task A_leitura_recusa_uso_depois_do_descarte_dela_e_da_sessao_e_o_descarte_nunca_lanca()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1] };
        var sessao = new SessaoSimples([leitor]);
        var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        leitor.UltimaLeitura!.FalharNoDescarte = true;

        await sessao.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => leitura.LerLoteAsync(cancelamento));
        await leitura.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => leitura.LerLoteAsync(cancelamento));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        Assert.Equal(1, leitor.UltimaLeitura.Descartes);
    }

    [Fact]
    public async Task A_leitura_que_falhou_fica_quebrada_e_so_se_descarta()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1, 1] };
        await using var sessao = new SessaoSimples([leitor]);
        var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        leitor.UltimaLeitura!.FalharNaLeitura = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
        leitor.UltimaLeitura.FalharNaLeitura = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
        Assert.Equal(1, leitor.UltimaLeitura.Chamadas);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.LerMaiorValorAsync(_coluna, cancelamento));

        await leitura.DisposeAsync();
        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task O_cancelamento_no_meio_da_chamada_quebra_a_leitura()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1, 1] };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        leitor.UltimaLeitura!.Segurar = true;
        using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);

        var chamada = leitura.LerLoteAsync(parar.Token);
        await parar.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chamada);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
    }

    [Fact]
    public async Task O_mesmo_lote_devolvido_de_novo_e_recusado_sem_ser_descartado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1, 1] };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        using var primeiro = await leitura.LerLoteAsync(cancelamento);
        leitor.UltimaLeitura!.RepetirLote = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));

        Assert.False(primeiro!.Descartado);
        Assert.Equal(1L, primeiro.PrimeiraLinha);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitura.LerLoteAsync(cancelamento));
    }

    [Fact]
    public async Task O_descarte_espera_a_chamada_em_curso_e_so_entao_libera_o_leitor()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1] };
        await using var sessao = new SessaoSimples([leitor]);
        var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        var deTeste = leitor.UltimaLeitura!;
        deTeste.Segurar = true;

        var chamada = leitura.LerLoteAsync(cancelamento);
        var descarte = leitura.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => leitura.LerLoteAsync(cancelamento));
        Assert.False(descarte.IsCompleted);
        Assert.Equal(0, deTeste.Descartes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.LerMaiorValorAsync(_coluna, cancelamento));

        deTeste.Soltar();
        (await chamada)?.Dispose();
        await descarte;

        Assert.Equal(["leitura", "descarte"], deTeste.Ordem);
        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task O_leitor_e_liberado_mesmo_com_o_descarte_do_dialeto_falhando()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var leitor = new LeitorDeLinhas { Lotes = [1] };
        await using var sessao = new SessaoSimples([leitor]);
        var leitura = await leitor.AbrirTabelaAsync(Pedido(), cancelamento);
        leitor.UltimaLeitura!.FalharNoDescarte = true;

        await leitura.DisposeAsync();

        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task O_leitor_sem_sessao_e_a_leitura_ja_descartada_sao_recusados()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LeitorDeLinhas().AbrirTabelaAsync(Pedido(), cancelamento));

        var leitor = new LeitorDeLinhas { DevolverDescartada = true };
        await using var sessao = new SessaoSimples([leitor]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => leitor.AbrirTabelaAsync(Pedido(), cancelamento));
        Assert.Equal(31L, await leitor.LerMaiorValorAsync(_coluna, cancelamento));
    }

    [Fact]
    public async Task O_texto_do_lote_da_leitura_e_do_pedido_nao_traz_valor_nem_nome()
    {
        const string sentinela = "zqSENTINELAqz";
        var cancelamento = TestContext.Current.CancellationToken;
        var pedido = new PedidoLeituraTabela { Tabela = BancoFalso.Nome(sentinela), Colunas = [sentinela], Convertidas = [Convertida(0, TipoColuna.Texto)] };
        var lote = Lote(pedido, new Celula(0, 0, sentinela + "-valor"));
        lote.Escrever(0, 0, sentinela + "-escrito");
        var leitor = new LeitorDeLinhas { Lotes = [1] };
        await using var sessao = new SessaoSimples([leitor]);
        await using var leitura = await leitor.AbrirTabelaAsync(pedido, cancelamento);
        using var lido = await leitura.LerLoteAsync(cancelamento);

        Assert.DoesNotContain(sentinela, lote.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinela, lido!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinela, leitura.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinela, pedido.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinela, pedido.Convertidas[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_lote_le_so_as_convertidas_na_forma_canonica()
    {
        var lote = Lote(Pedido(), new Celula(0, 1, new byte[] { 1, 2 }), new Celula(0, 2, "texto"));

        Assert.Throws<ArgumentOutOfRangeException>(() => lote.TentarLer(0, 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => lote.TentarLer(1, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => lote.TentarLer(-1, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => lote.TentarLer(0, 3, out _));
        Assert.True(lote.TentarLer(0, 1, out var binario));
        Assert.Equal(new byte[] { 1, 2 }, (byte[])binario!);
        Assert.True(lote.TentarLer(0, 2, out var texto));
        Assert.Equal("texto", texto);
        Assert.DoesNotContain("texto", lote.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ForaDaForma))]
    public void O_valor_fora_da_forma_canonica_e_recusado_na_leitura_e_na_escrita(TipoColuna tipo, object valor)
    {
        var pedido = new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["valor"], Convertidas = [Convertida(0, tipo)] };

        Assert.Throws<InvalidOperationException>(() => Lote(pedido, new Celula(0, 0, valor)).TentarLer(0, 0, out _));
        var lote = Lote(pedido);
        Assert.Throws<ArgumentException>(() => lote.Escrever(0, 0, valor));
        Assert.False(lote.EstaEscrita(0, 0));
    }

    public static TheoryData<TipoColuna, object> ForaDaForma => new()
    {
        { TipoColuna.Inteiro, 7 },
        { TipoColuna.Texto, 'a' },
        { TipoColuna.Decimal, 1.5 },
        { TipoColuna.Data, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) },
        { TipoColuna.DataHora, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        { TipoColuna.DataHora, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local) },
        { TipoColuna.DataHoraComFuso, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-3)) },
        { TipoColuna.Binario, new List<byte> { 1 } },
        { TipoColuna.Booleano, 1 },
        { TipoColuna.Uuid, "0b6f1a52-6a0d-4b6c-9a52-6a0d4b6c9a52" },
    };

    // Os textos são montados no teste: o xUnit troca o surrogate isolado de um argumento por U+FFFD.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void O_texto_mal_formado_do_dialeto_vale_como_sem_representacao_e_e_recusado_na_escrita(int caso)
    {
        var texto = caso switch
        {
            0 => "a\0b",
            1 => "a\uD800",
            _ => "\uDC00a",
        };
        var pedido = new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["valor"], Convertidas = [Convertida(0, TipoColuna.Texto)] };

        Assert.False(Lote(pedido, new Celula(0, 0, texto)).TentarLer(0, 0, out var valor));
        Assert.Null(valor);
        var lote = Lote(pedido);
        Assert.Throws<ArgumentException>(() => lote.Escrever(0, 0, texto));
        Assert.False(FormaCanonica.TextoBemFormado(texto));
    }

    [Fact]
    public void A_forma_canonica_cobre_todo_tipo()
    {
        foreach (var tipo in Enum.GetValues<TipoColuna>())
        {
            Assert.NotNull(FormaCanonica.Tipo(tipo));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => FormaCanonica.Tipo((TipoColuna)99));
        Assert.True(FormaCanonica.Aceita(TipoColuna.DataHoraComFuso, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(FormaCanonica.Aceita(TipoColuna.Texto, "ação 😀"));
    }

    [Fact]
    public void A_celula_se_escreve_uma_vez_e_nao_se_le_depois()
    {
        var lote = Lote(Pedido(), new Celula(0, 1, new byte[] { 1 }), new Celula(0, 2, "original"));
        var escrito = new byte[] { 7 };

        lote.Escrever(0, 1, escrito);
        escrito[0] = 8;
        Assert.True(lote.EstaEscrita(0, 1));
        Assert.Equal(new byte[] { 7 }, (byte[])lote.Escritos[(0, 1)]!);
        Assert.Throws<InvalidOperationException>(() => lote.TentarLer(0, 1, out _));
        Assert.Throws<InvalidOperationException>(() => lote.Escrever(0, 1, null));
        Assert.Throws<InvalidOperationException>(() => lote.Manter(0, 1));

        lote.Manter(0, 2);
        Assert.True(lote.EstaEscrita(0, 2));
        Assert.Throws<InvalidOperationException>(() => lote.TentarLer(0, 2, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => lote.Escrever(0, 0, 1L));

    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void O_dialeto_que_falha_ao_escrever_ou_manter_invalida_o_lote(bool escrevendo)
    {
        var lote = Lote(Pedido(), new Celula(0, 2, "original"));
        lote.FalharNaEscrita = true;

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (escrevendo)
            {
                lote.Escrever(0, 2, "novo");
            }
            else
            {
                lote.Manter(0, 2);
            }
        });

        Assert.Throws<InvalidOperationException>(() => lote.TentarLer(0, 2, out _));
        Assert.Throws<InvalidOperationException>(() => lote.TentarLer(0, 1, out _));
        Assert.Throws<InvalidOperationException>(() => lote.EstaEscrita(0, 2));
        Assert.Throws<InvalidOperationException>(() => lote.Manter(0, 1));
        lote.Dispose();
    }

    [Fact]
    public void A_celula_e_de_uma_linha_e_de_uma_coluna_so()
    {
        var lote = new LoteDeTeste(Pedido(), 3, [new Celula(0, 1, new byte[] { 1 }), new Celula(1, 2, "um"), new Celula(2, 1, new byte[] { 2 })]);

        lote.Escrever(1, 1, new byte[] { 9 });

        Assert.True(lote.EstaEscrita(1, 1));
        Assert.False(lote.EstaEscrita(0, 1));
        Assert.False(lote.EstaEscrita(1, 2));
        Assert.False(lote.EstaEscrita(2, 1));
        Assert.True(lote.TentarLer(0, 1, out var primeira) && ((byte[])primeira!)[0] == 1);
        Assert.True(lote.TentarLer(1, 2, out var texto) && (string)texto! == "um");
        Assert.True(lote.TentarLer(2, 1, out var terceira) && ((byte[])terceira!)[0] == 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => lote.TentarLer(3, 1, out _));
    }

    [Fact]
    public void O_valor_sem_forma_do_dialeto_sai_nulo()
    {
        var lote = Lote(Pedido(), new Celula(0, 2, "sem forma"));
        lote.SemForma = true;

        Assert.False(lote.TentarLer(0, 2, out var valor));
        Assert.Null(valor);
    }

    [Fact]
    public void O_lote_descarta_uma_vez_nunca_lanca_e_recusa_uso_depois()
    {
        var lote = Lote(Pedido(), new Celula(0, 2, "a"));
        lote.FalharNoDescarte = true;

        lote.Dispose();
        lote.Dispose();

        Assert.Equal(1, lote.Descartes);
        Assert.Throws<ObjectDisposedException>(() => lote.TentarLer(0, 2, out _));
        Assert.Throws<ObjectDisposedException>(() => lote.Escrever(0, 2, "b"));
        Assert.Throws<ObjectDisposedException>(() => lote.EstaEscrita(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoteDeTeste(Pedido(), 0, []));
        Assert.Throws<ArgumentNullException>(() => new LoteDeTeste(null!, 1, []));
    }

    private static ColunaConvertida Convertida(int indice, TipoColuna tipo) => new() { Indice = indice, Tipo = tipo };

    private static PedidoLeituraTabela Pedido() => new()
    {
        Tabela = _tabela,
        Colunas = ["id", "conteudo", "texto"],
        Convertidas = [Convertida(1, TipoColuna.Binario), Convertida(2, TipoColuna.Texto)],
    };

    private static LoteDeTeste Lote(PedidoLeituraTabela pedido, params Celula[] celulas) => new(pedido, 1, celulas);

    private sealed record Celula(int Linha, int Coluna, object? Valor);

    private sealed class SessaoSimples(IReadOnlyList<LeitorDaSessao> leitores) : SessaoLeitura(leitores)
    {
        public override ConsistenciaObtida Obtida => ConsistenciaObtida.Instantaneo;

        protected override Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento) => Task.FromResult(new EsquemaBanco { Tabelas = [] });

        protected override Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento) => Task.FromResult<long?>(0);

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class LeitorDeLinhas : LeitorDaSessao
    {
        internal int Aberturas { get; private set; }

        internal bool Falhar { get; set; }

        internal bool DevolverNula { get; set; }

        internal LeituraTabela? Reaproveitar { get; set; }

        internal int[] Lotes { get; init; } = [];

        internal bool LoteDeOutroPedido { get; init; }

        internal bool DevolverDescartada { get; init; }

        internal LeituraDeTeste? UltimaLeitura { get; private set; }

        protected override Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento) => Task.FromResult<long?>(31);

        protected override Task<LeituraTabela> AbrirTabelaAsyncCore(PedidoLeituraTabela pedido, CancellationToken cancelamento)
        {
            Aberturas++;
            if (Falhar)
            {
                throw new InvalidOperationException("A abertura falhou.");
            }

            if (DevolverNula)
            {
                return Task.FromResult<LeituraTabela>(null!);
            }

            if (Reaproveitar is { } outra)
            {
                return Task.FromResult(outra);
            }

            UltimaLeitura = new LeituraDeTeste(LoteDeOutroPedido ? Pedido() : pedido, Lotes);
            return DevolverDescartada ? DescartadaAsync(UltimaLeitura) : Task.FromResult<LeituraTabela>(UltimaLeitura);
        }

        private static async Task<LeituraTabela> DescartadaAsync(LeituraTabela leitura)
        {
            await leitura.DisposeAsync();
            return leitura;
        }
    }

    private sealed class LeituraDeTeste(PedidoLeituraTabela pedidoDosLotes, int[] lotes) : LeituraTabela
    {
        private TaskCompletionSource _portao = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _proximo;

        internal int Descartes { get; private set; }

        internal int Chamadas { get; private set; }

        internal bool Segurar { get; set; }

        internal bool FalharNoDescarte { get; set; }

        internal List<LoteDeTeste> Devolvidos { get; } = [];

        internal List<string> Ordem { get; } = [];

        internal bool FalharNaLeitura { get; set; }

        internal bool RepetirLote { get; set; }

        internal void Soltar() => _portao.TrySetResult();

        protected override async Task<LoteLinhas?> LerLoteAsyncCore(CancellationToken cancelamento)
        {
            Chamadas++;
            if (Segurar)
            {
                await _portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
                Segurar = false;
                _portao = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (FalharNaLeitura)
            {
                throw new InvalidOperationException("A leitura do lote falhou.");
            }

            if (RepetirLote && Devolvidos.Count > 0)
            {
                return Devolvidos[^1];
            }

            Ordem.Add("leitura");
            if (_proximo == lotes.Length)
            {
                return null;
            }

            var lote = new LoteDeTeste(pedidoDosLotes, lotes[_proximo++], []);
            Devolvidos.Add(lote);
            return lote;
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Descartes++;
            Ordem.Add("descarte");
            return FalharNoDescarte ? ValueTask.FromException(new InvalidOperationException("O descarte falhou.")) : ValueTask.CompletedTask;
        }
    }

    private sealed class LoteDeTeste(PedidoLeituraTabela pedido, int quantidade, Celula[] celulas) : LoteLinhas(pedido, quantidade)
    {
        internal Dictionary<(int Linha, int Coluna), object?> Escritos { get; } = [];

        internal int Descartes { get; private set; }

        internal bool FalharNoDescarte { get; set; }

        internal bool FalharNaEscrita { get; set; }

        internal bool SemForma { get; set; }

        protected override bool TentarLerCore(int linha, int coluna, out object? valor)
        {
            valor = celulas.FirstOrDefault(celula => celula.Linha == linha && celula.Coluna == coluna)?.Valor;
            return !SemForma;
        }

        protected override void EscreverCore(int linha, int coluna, object? valor)
        {
            if (FalharNaEscrita)
            {
                throw new InvalidOperationException("A escrita falhou.");
            }

            Escritos[(linha, coluna)] = valor;
        }

        protected override void ManterCore(int linha, int coluna)
        {
            if (FalharNaEscrita)
            {
                throw new InvalidOperationException("A manutenção falhou.");
            }

            Escritos[(linha, coluna)] = "mantido";
        }

        protected override void DisposeCore()
        {
            Descartes++;
            if (FalharNoDescarte)
            {
                throw new InvalidOperationException("O descarte falhou.");
            }
        }
    }
}
