using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// As regras da gravação que a base do contrato garante, sem depender do dialeto: o pedido conferido, a posse do lote (sempre
/// descartado pela gravação), a forma do lote conferida antes do dialeto (nada tratado sai sem escrita), uma chamada por vez,
/// a gravação quebrada depois de uma falha, a concluída que recusa outra chamada, e o descarte único que nunca lança e espera
/// a chamada em curso.
/// </summary>
public sealed class GravacaoNaBaseTestes
{
    private static readonly NomeObjeto _tabela = new("loja", "tipos");

    [Fact]
    public void O_pedido_confere_as_colunas_e_as_tratadas()
    {
        Assert.Throws<ArgumentNullException>(() => new PedidoGravacao { Tabela = null!, Colunas = ["id"], Tratadas = [] });
        Assert.Throws<ArgumentNullException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = null!, Tratadas = [] });
        Assert.Throws<ArgumentException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = [], Tratadas = [] });
        Assert.Throws<ArgumentException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = [""], Tratadas = [] });
        Assert.Throws<ArgumentException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = ["id", "id"], Tratadas = [] });
        Assert.Throws<ArgumentNullException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = ["id"], Tratadas = [null!] });
        Assert.Throws<ArgumentException>(() => new PedidoGravacao { Tabela = _tabela, Colunas = ["id"], Tratadas = [Convertida(0, TipoColuna.Inteiro), Convertida(0, TipoColuna.Texto)] });

        var colunas = new List<string> { "id", "texto" };
        var pedido = new PedidoGravacao { Tabela = _tabela, Colunas = colunas, Tratadas = [Convertida(1, TipoColuna.Texto)] };
        colunas.Add("outra");
        Assert.Equal(2, pedido.Colunas.Count);
        Assert.DoesNotContain("tipos", pedido.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("texto", pedido.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tratada_fora_das_colunas_e_recusada_antes_do_dialeto()
    {
        var gravador = new GravadorDeTeste();
        var pedido = new PedidoGravacao { Tabela = _tabela, Colunas = ["id"], Tratadas = [Convertida(1, TipoColuna.Inteiro)] };

        await Assert.ThrowsAsync<ArgumentException>(() => gravador.AbrirAsync(pedido, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => gravador.AbrirAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => gravador.EsvaziarAsync(null!, TestContext.Current.CancellationToken));
        Assert.Equal(0, gravador.Aberturas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_gravacao_invalida_do_dialeto_e_recusada(int caso)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravador = new GravadorDeTeste();
        var devolvida = caso switch
        {
            0 => null,
            1 => new GravacaoDeTeste(),
            _ => await gravador.AbrirAsync(PedidoDeGravacao(), cancelamento),
        };
        if (caso == 1)
        {
            await devolvida!.DisposeAsync();
        }

        gravador.Criar = () => devolvida;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gravador.AbrirAsync(PedidoDeGravacao(), cancelamento));
    }

    [Fact]
    public async Task O_lote_gravado_e_descartado_e_as_linhas_contadas()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var gravacao = await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var lotes = new[] { Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 2)), Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 3)) };

        foreach (var lote in lotes)
        {
            await gravacao.GravarAsync(lote, cancelamento);
        }

        Assert.All(lotes, lote => Assert.Equal(1, lote.Descartes));
        Assert.Equal(5, gravacao.Linhas);
        Assert.Equal(5, await gravacao.ConcluirAsync(cancelamento));
        Assert.Equal(2, ((GravacaoDeTeste)gravacao).Gravados);
        Assert.Equal("GravacaoTabela (5 linhas)", gravacao.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task O_lote_fora_da_forma_e_recusado_sem_chegar_ao_dialeto_e_a_gravacao_segue(int caso)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var lote = caso switch
        {
            // Uma linha com a tratada sem escrita.
            0 => SoAPrimeiraEscrita(LoteDeGravacao.Novo(PedidoDeLeitura(), 2)),

            // Outro número de colunas.
            1 => Escrito(LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto", "outra"], Convertidas = [Convertida(1, TipoColuna.Texto)] }, 1)),

            // A tratada lida como opaca.
            2 => LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto"] }, 1),

            // A tratada lida com outro tipo, mesmo escrita.
            3 => BinarioEscrito(),

            // O lote que ficou inválido.
            4 => Invalido(),

            // O lote já descartado.
            _ => Descartado(),
        };

        if (caso == 5)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => gravacao.GravarAsync(lote, cancelamento));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(lote, cancelamento));
        }

        Assert.Equal(1, lote.Descartes);
        Assert.Equal(0, gravacao.Gravados);

        // Nada foi gravado: a gravação segue.
        await gravacao.GravarAsync(Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1)), cancelamento);
        Assert.Equal(1, await gravacao.ConcluirAsync(cancelamento));
    }

    [Fact]
    public async Task A_tratada_mantida_e_a_convertida_que_nao_e_tratada_passam()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var lote = LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto"], Convertidas = [Convertida(0, TipoColuna.Inteiro), Convertida(1, TipoColuna.Texto)] }, 1);
        lote.Manter(0, 1);

        await gravacao.GravarAsync(lote, cancelamento);

        Assert.Equal(1, gravacao.Gravados);
    }

    [Fact]
    public async Task A_gravacao_que_falhou_ou_foi_cancelada_no_meio_fica_quebrada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        foreach (var cancelar in new[] { false, true })
        {
            await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
            using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            gravacao.AoGravar = () =>
            {
                if (cancelar)
                {
                    parar.Cancel();
                    parar.Token.ThrowIfCancellationRequested();
                }

                throw new InvalidOperationException("A gravação falhou no dialeto.");
            };

            var primeiro = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
            if (cancelar)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gravacao.GravarAsync(primeiro, parar.Token));
            }
            else
            {
                var falha = await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(primeiro, parar.Token));
                Assert.Equal("A gravação falhou no dialeto.", falha.Message);
            }

            gravacao.AoGravar = null;

            var segundo = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
            var recusa = await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(segundo, cancelamento));
            Assert.Contains("falhou numa chamada anterior", recusa.Message, StringComparison.Ordinal);
            await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.ConcluirAsync(cancelamento));
            Assert.Equal(1, primeiro.Descartes);
            Assert.Equal(1, segundo.Descartes);
            Assert.Equal(0, gravacao.Linhas);
        }
    }

    [Fact]
    public async Task O_cancelamento_pedido_antes_da_chamada_nao_quebra_a_gravacao()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var lote = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gravacao.GravarAsync(lote, new CancellationToken(canceled: true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gravacao.ConcluirAsync(new CancellationToken(canceled: true)));

        Assert.Equal(1, lote.Descartes);
        Assert.Equal(0, gravacao.Gravados);
        Assert.Equal(0, await gravacao.ConcluirAsync(cancelamento));
    }

    [Fact]
    public async Task A_gravacao_concluida_ou_descartada_recusa_outra_chamada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        await gravacao.ConcluirAsync(cancelamento);

        var depois = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(depois, cancelamento));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.ConcluirAsync(cancelamento));
        Assert.Equal(1, depois.Descartes);

        await gravacao.DisposeAsync();
        var descartada = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gravacao.GravarAsync(descartada, cancelamento));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gravacao.ConcluirAsync(cancelamento));
        Assert.Equal(1, descartada.Descartes);
        Assert.Equal(1, gravacao.Concluidas);
    }

    [Fact]
    public async Task Os_lotes_sao_gravados_um_por_vez_e_o_descarte_espera_a_chamada_em_curso()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var portao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gravacao.AoGravarAsync = async () =>
        {
            entrou.SetResult();
            await portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        };

        var emCurso = gravacao.GravarAsync(Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1)), cancelamento);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        var outro = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(outro, cancelamento));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.ConcluirAsync(cancelamento));
        Assert.Equal(1, outro.Descartes);

        var descarte = gravacao.DisposeAsync().AsTask();
        await Task.Delay(50, cancelamento);
        Assert.False(descarte.IsCompleted);
        Assert.Equal(0, gravacao.Descartes);

        portao.SetResult();
        await emCurso;
        await descarte;
        Assert.Equal(1, gravacao.Descartes);
    }

    [Fact]
    public async Task O_descarte_e_unico_e_nunca_lanca()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        gravacao.FalharNoDescarte = true;

        await gravacao.DisposeAsync();
        await gravacao.DisposeAsync();

        Assert.Equal(1, gravacao.Descartes);
    }

    [Fact]
    public async Task A_gravacao_fora_de_um_gravador_recusa_uso()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var solta = new GravacaoDeTeste();
        var lote = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => solta.GravarAsync(lote, cancelamento));
        await Assert.ThrowsAsync<InvalidOperationException>(() => solta.ConcluirAsync(cancelamento));
        await Assert.ThrowsAsync<ArgumentNullException>(() => solta.GravarAsync(null!, cancelamento));
        Assert.Equal(1, lote.Descartes);
    }

    [Fact]
    public async Task O_lote_passado_a_outra_chamada_e_recusado_sem_ser_descartado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravador = new GravadorDeTeste();
        var gravacao = (GravacaoDeTeste)await gravador.AbrirAsync(PedidoDeGravacao(), cancelamento);
        await using var outra = (GravacaoDeTeste)await gravador.AbrirAsync(PedidoDeGravacao(), cancelamento);
        var portao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gravacao.AoGravarAsync = async () =>
        {
            entrou.SetResult();
            await portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        };
        var lote = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));

        var emCurso = gravacao.GravarAsync(lote, cancelamento);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);

        // Na mesma gravação e em outra: recusado, e o lote segue vivo com quem o grava.
        var mesma = await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(lote, cancelamento));
        var naOutra = await Assert.ThrowsAsync<InvalidOperationException>(() => outra.GravarAsync(lote, cancelamento));
        Assert.Contains("já foi passado a uma gravação", mesma.Message, StringComparison.Ordinal);
        Assert.Contains("já foi passado a uma gravação", naOutra.Message, StringComparison.Ordinal);
        Assert.Equal(0, lote.Descartes);
        Assert.Equal(0, outra.Gravados);

        portao.SetResult();
        await emCurso;
        Assert.Equal(1, lote.Descartes);
        Assert.Equal(1, gravacao.Gravados);
        await gravacao.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task As_colunas_do_lote_sao_as_do_pedido_na_mesma_ordem(int caso)
    {
        // A ordem trocada entre a leitura e a gravação trocaria os valores entre as colunas; o nome diferente, a coluna.
        var cancelamento = TestContext.Current.CancellationToken;
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var lote = caso == 0
            ? LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["texto", "id"], Convertidas = [Convertida(1, TipoColuna.Texto)] }, 1)
            : LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "nome"], Convertidas = [Convertida(1, TipoColuna.Texto)] }, 1);
        lote.Escrever(0, 1, "tratado");

        var recusa = await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(lote, cancelamento));

        Assert.Contains("na mesma ordem", recusa.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("texto", recusa.Message, StringComparison.Ordinal);
        Assert.Equal(1, lote.Descartes);
        Assert.Equal(0, gravacao.Gravados);
    }

    [Fact]
    public async Task Cada_tratada_e_conferida_em_toda_linha()
    {
        // Duas tratadas: a segunda sem escrita na segunda linha recusa o lote.
        var cancelamento = TestContext.Current.CancellationToken;
        var pedido = new PedidoGravacao { Tabela = _tabela, Colunas = ["id", "texto", "nome"], Tratadas = [Convertida(1, TipoColuna.Texto), Convertida(2, TipoColuna.Texto)] };
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(pedido, cancelamento);
        var lote = LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto", "nome"], Convertidas = [Convertida(1, TipoColuna.Texto), Convertida(2, TipoColuna.Texto)] }, 2);
        lote.Escrever(0, 1, "tratado");
        lote.Escrever(0, 2, "tratado");
        lote.Escrever(1, 1, "tratado");

        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(lote, cancelamento));

        Assert.Equal(0, gravacao.Gravados);
    }

    [Fact]
    public async Task O_pedido_sem_tratadas_aceita_o_lote_sem_escrita_e_recusa_o_descartado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var pedido = new PedidoGravacao { Tabela = _tabela, Colunas = ["id", "texto"], Tratadas = [] };
        await using var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(pedido, cancelamento);
        var descartado = LoteDeGravacao.Novo(PedidoDeLeitura(), 1);
        descartado.Dispose();

        await gravacao.GravarAsync(LoteDeGravacao.Novo(PedidoDeLeitura(), 2), cancelamento);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gravacao.GravarAsync(descartado, cancelamento));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.GravarAsync(Invalido(), cancelamento));

        Assert.Equal(1, gravacao.Gravados);
        Assert.Same(pedido, gravacao.PedidoVisto);
    }

    [Fact]
    public async Task A_conclusao_que_falhou_quebra_a_gravacao_e_o_descarte_ainda_desfaz()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        gravacao.FalharNaConclusao = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.ConcluirAsync(cancelamento));
        gravacao.FalharNaConclusao = false;
        var recusa = await Assert.ThrowsAsync<InvalidOperationException>(() => gravacao.ConcluirAsync(cancelamento));
        Assert.Contains("falhou numa chamada anterior", recusa.Message, StringComparison.Ordinal);

        await gravacao.DisposeAsync();
        Assert.Equal(1, gravacao.Concluidas);
        Assert.Equal(1, gravacao.Descartes);
    }

    [Fact]
    public async Task O_descarte_espera_a_conclusao_em_curso_e_o_concorrente_volta_sem_esperar()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = (GravacaoDeTeste)await new GravadorDeTeste().AbrirAsync(PedidoDeGravacao(), cancelamento);
        var portao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gravacao.AoConcluirAsync = async () =>
        {
            entrou.SetResult();
            await portao.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        };

        var conclusao = gravacao.ConcluirAsync(cancelamento);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        var primeiro = gravacao.DisposeAsync().AsTask();
        var segundo = gravacao.DisposeAsync().AsTask();
        await segundo.WaitAsync(TimeSpan.FromSeconds(10), cancelamento);
        await Task.Delay(50, cancelamento);
        Assert.False(primeiro.IsCompleted);
        Assert.Equal(0, gravacao.Descartes);

        portao.SetResult();
        await conclusao;
        await primeiro;
        Assert.Equal(1, gravacao.Descartes);
    }

    private static ColunaConvertida Convertida(int indice, TipoColuna tipo) => new() { Indice = indice, Tipo = tipo };

    // Lido da origem: a chave opaca e o texto convertido.
    private static PedidoLeituraTabela PedidoDeLeitura() => new() { Tabela = _tabela, Colunas = ["id", "texto"], Convertidas = [Convertida(1, TipoColuna.Texto)] };

    // Gravado no destino: o texto tratado.
    private static PedidoGravacao PedidoDeGravacao() => new() { Tabela = _tabela, Colunas = ["id", "texto"], Tratadas = [Convertida(1, TipoColuna.Texto)] };

    private static LoteDeGravacao Escrito(LoteDeGravacao lote)
    {
        for (var linha = 0; linha < lote.Quantidade; linha++)
        {
            lote.Escrever(linha, 1, "tratado");
        }

        return lote;
    }

    private static LoteDeGravacao SoAPrimeiraEscrita(LoteDeGravacao lote)
    {
        lote.Escrever(0, 1, "tratado");
        return lote;
    }

    private static LoteDeGravacao BinarioEscrito()
    {
        var lote = LoteDeGravacao.Novo(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto"], Convertidas = [Convertida(1, TipoColuna.Binario)] }, 1);
        lote.Escrever(0, 1, new byte[] { 1 });
        return lote;
    }

    private static LoteDeGravacao Invalido()
    {
        var lote = LoteDeGravacao.Novo(PedidoDeLeitura(), 1);
        lote.FalharNaEscrita = true;
        Assert.Throws<InvalidOperationException>(() => lote.Escrever(0, 1, "tratado"));
        return lote;
    }

    private static LoteDeGravacao Descartado()
    {
        var lote = Escrito(LoteDeGravacao.Novo(PedidoDeLeitura(), 1));
        lote.Dispose();
        return lote;
    }

    private sealed class GravadorDeTeste : GravadorEmLote
    {
        internal int Aberturas { get; private set; }

        // O que o dialeto devolve a cada abertura: por padrão, uma gravação nova.
        internal Func<GravacaoTabela?> Criar { get; set; } = () => new GravacaoDeTeste();

        protected override Task EsvaziarAsyncCore(NomeObjeto tabela, CancellationToken cancelamento) => Task.CompletedTask;

        protected override Task<GravacaoTabela> AbrirAsyncCore(PedidoGravacao pedido, CancellationToken cancelamento)
        {
            Aberturas++;
            return Task.FromResult(Criar()!);
        }
    }

    private sealed class GravacaoDeTeste : GravacaoTabela
    {
        internal int Gravados { get; private set; }

        internal int Concluidas { get; private set; }

        internal int Descartes { get; private set; }

        internal bool FalharNoDescarte { get; set; }

        internal Action? AoGravar { get; set; }

        internal Func<Task>? AoGravarAsync { get; set; }

        internal Func<Task>? AoConcluirAsync { get; set; }

        internal bool FalharNaConclusao { get; set; }

        internal PedidoGravacao? PedidoVisto { get; private set; }

        protected override async Task GravarAsyncCore(LoteLinhas lote, CancellationToken cancelamento)
        {
            PedidoVisto = Pedido;
            AoGravar?.Invoke();
            if (AoGravarAsync is not null)
            {
                await AoGravarAsync();
            }

            Gravados++;
        }

        protected override async Task ConcluirAsyncCore(CancellationToken cancelamento)
        {
            Concluidas++;
            if (AoConcluirAsync is not null)
            {
                await AoConcluirAsync();
            }

            if (FalharNaConclusao)
            {
                throw new InvalidOperationException("A conclusão falhou no dialeto.");
            }
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Descartes++;
            return FalharNoDescarte ? throw new InvalidOperationException("O descarte falhou.") : ValueTask.CompletedTask;
        }
    }

    private sealed class LoteDeGravacao(PedidoLeituraTabela pedido, int quantidade) : LoteLinhas(pedido, quantidade)
    {
        internal int Descartes { get; private set; }

        internal bool FalharNaEscrita { get; set; }

        internal static LoteDeGravacao Novo(PedidoLeituraTabela pedido, int quantidade) => new(pedido, quantidade);

        protected override bool TentarLerCore(int linha, int coluna, out object? valor)
        {
            valor = null;
            return true;
        }

        protected override void EscreverCore(int linha, int coluna, object? valor)
        {
            if (FalharNaEscrita)
            {
                throw new InvalidOperationException("A escrita falhou.");
            }
        }

        protected override void ManterCore(int linha, int coluna)
        {
        }

        protected override void DisposeCore() => Descartes++;
    }
}
