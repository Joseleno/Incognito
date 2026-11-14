using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O fechamento do <c>COPY</c> com o servidor que para de responder, pelo fluxo de verdade: entre o dialeto e o servidor, um
/// proxy TCP que congela (não repassa nada, nos dois sentidos, nem às conexões novas, como a do pedido de cancelamento). A
/// conclusão cancelada e o descarte sem a conclusão voltam no prazo do cancelamento e nada é confirmado. Pausar o servidor
/// compartilhado derrubaria os outros testes; o proxy congela só as conexões deste.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class FechamentoDoCopyPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private static readonly NomeObjeto _tabela = new("loja", "fechamento");

    // O prazo do cancelamento (10 s) e uma folga larga para o runner.
    private static readonly TimeSpan _teto = LeituraTabelaPostgreSql.PrazoDoCancelamento + TimeSpan.FromSeconds(15);

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_conclusao_cancelada_com_o_servidor_parado_volta_no_prazo_sem_confirmar()
    {
        await NoDestinoAsync(async (banco, destino, proxy, cancelamento) =>
        {
            var antes = await ArquivoAsync(banco, cancelamento);
            var lotes = await LotesAsync(destino, cancelamento);
            var gravacao = await destino.Gravador.AbrirAsync(Pedido(), cancelamento);
            foreach (var lote in lotes)
            {
                await gravacao.GravarAsync(lote, cancelamento);
            }

            proxy.Congelar();
            using var parar = new CancellationTokenSource();
            var concluir = Task.Run(() => gravacao.ConcluirAsync(parar.Token), CancellationToken.None);
            await Task.Delay(1000, cancelamento);
            await parar.CancelAsync();
            var cronometro = Stopwatch.StartNew();

            Assert.True(await Task.WhenAny(concluir, Task.Delay(_teto, cancelamento)) == concluir, $"A conclusão não voltou em {_teto.TotalSeconds} s depois do cancelamento.");
            Assert.False(concluir.IsCompletedSuccessfully, "A conclusão abandonada devolveu as linhas como gravadas.");
            var descarte = Task.Run(() => gravacao.DisposeAsync().AsTask(), CancellationToken.None);
            Assert.True(await Task.WhenAny(descarte, Task.Delay(_teto, cancelamento)) == descarte, "O descarte depois da conclusão abandonada não voltou no prazo.");
            Assert.True(cronometro.Elapsed < 2 * _teto);

            await proxy.DisposeAsync();
            Assert.Equal(antes, await ArquivoAsync(banco, cancelamento));
        });
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_descarte_sem_a_conclusao_com_o_servidor_parado_volta_no_prazo_sem_confirmar()
    {
        await NoDestinoAsync(async (banco, destino, proxy, cancelamento) =>
        {
            var antes = await ArquivoAsync(banco, cancelamento);
            var lotes = await LotesAsync(destino, cancelamento);
            var gravacao = await destino.Gravador.AbrirAsync(Pedido(), cancelamento);
            await gravacao.GravarAsync(lotes[0], cancelamento);
            foreach (var lote in lotes.Skip(1))
            {
                lote.Dispose();
            }

            proxy.Congelar();
            var descarte = Task.Run(() => gravacao.DisposeAsync().AsTask(), CancellationToken.None);

            Assert.True(await Task.WhenAny(descarte, Task.Delay(_teto, cancelamento)) == descarte, $"O descarte não voltou em {_teto.TotalSeconds} s.");

            await proxy.DisposeAsync();
            Assert.Equal(antes, await ArquivoAsync(banco, cancelamento));
        });
    }

    private static PedidoGravacao Pedido() => new() { Tabela = _tabela, Colunas = ["id", "texto"], Tratadas = [] };

    // As linhas da própria tabela, lidas pelo dialeto: a gravação as escreve de volta depois de esvaziar.
    private static async Task<List<LoteLinhas>> LotesAsync(DialetoDestino destino, CancellationToken cancelamento)
    {
        var lotes = new List<LoteLinhas>();
        await using var sessao = await destino.AbrirLeituraAsync(new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.MelhorEsforco }, cancelamento);
        await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = _tabela, Colunas = ["id", "texto"] }, cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            lotes.Add(lote);
        }

        return lotes;
    }

    // O arquivo da tabela: o TRUNCATE confirmado o troca. Lido no catálogo, sem esperar a trava da gravação aberta, e só depois
    // de o proxy fechar as conexões (o servidor desfaz a transação dela).
    private async Task<object?> ArquivoAsync(string banco, CancellationToken cancelamento)
    {
        var prazo = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < prazo
            && await servidor.ConsultarAsync(banco, $"SELECT pg_catalog.count(*) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.application_name = '{ConexaoPostgreSql.NomeDaGravacao}'", cancelamento) is not 0L)
        {
            await Task.Delay(200, cancelamento);
        }

        return await servidor.ConsultarAsync(banco, "SELECT c.relfilenode FROM pg_catalog.pg_class c WHERE c.oid = 'loja.fechamento'::regclass", cancelamento);
    }

    private async Task NoDestinoAsync(Func<string, DialetoDestino, ProxyQueCongela, CancellationToken, Task> teste)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                banco,
                $"""
                CREATE TABLE loja.fechamento (id bigint NOT NULL, texto text NOT NULL);
                INSERT INTO loja.fechamento SELECT g, pg_catalog.repeat('v', 100) FROM pg_catalog.generate_series(1, 20000) g;
                ALTER TABLE loja.fechamento OWNER TO {ServidorPostgreSql.Dono};
                """,
                cancelamento);
            var construtor = new NpgsqlConnectionStringBuilder(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono));
            var proxy = new ProxyQueCongela(construtor.Host!, construtor.Port);
            DialetoDestino? destino = null;
            try
            {
                construtor.Host = IPAddress.Loopback.ToString();
                construtor.Port = proxy.Porta;
                destino = await new ProvedorPostgreSql().ConectarDestinoAsync(new SegredoConexao(construtor.ConnectionString), cancelamento);
                await teste(banco, destino, proxy, cancelamento);
            }
            finally
            {
                // O proxy fecha antes do dialeto: o descarte do dialeto espera a chamada em curso, que um proxy congelado
                // prenderia para sempre se a desistência do fechamento falhasse; assim o teste falha em vez de travar.
                await proxy.DisposeAsync();
                if (destino is not null)
                {
                    await destino.DisposeAsync();
                }
            }
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    // Repassa cada conexão ao servidor; congelado, para de repassar nos dois sentidos e segura as conexões novas sem ligá-las.
    // O descarte fecha tudo, e o servidor vê as conexões caírem.
    private sealed class ProxyQueCongela : IAsyncDisposable
    {
        private readonly TcpListener _escuta = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _fim = new();
        private readonly List<TcpClient> _abertos = [];
        private readonly string _host;
        private readonly int _porta;
        private readonly Task _aceitando;
        private TaskCompletionSource _liberado = Liberado();
        private int _descartado;

        internal ProxyQueCongela(string host, int porta)
        {
            _host = host;
            _porta = porta;
            _escuta.Start();
            _aceitando = AceitarAsync();
        }

        internal int Porta => ((IPEndPoint)_escuta.LocalEndpoint).Port;

        internal void Congelar() => Volatile.Write(ref _liberado, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _descartado, 1) != 0)
            {
                return;
            }

            await _fim.CancelAsync();
            _escuta.Stop();
            lock (_abertos)
            {
                foreach (var aberto in _abertos)
                {
                    aberto.Dispose();
                }
            }

            try
            {
                await _aceitando;
            }
            catch (Exception excecao) when (excecao is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }

            _fim.Dispose();
        }

        private static TaskCompletionSource Liberado()
        {
            var liberado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            liberado.SetResult();
            return liberado;
        }

        private async Task AceitarAsync()
        {
            while (!_fim.IsCancellationRequested)
            {
                var cliente = await _escuta.AcceptTcpClientAsync(_fim.Token);
                Guardar(cliente);
                _ = RepassarAsync(cliente);
            }
        }

        private void Guardar(TcpClient aberto)
        {
            lock (_abertos)
            {
                _abertos.Add(aberto);
            }
        }

        private async Task RepassarAsync(TcpClient cliente)
        {
            try
            {
                await Volatile.Read(ref _liberado).Task.WaitAsync(_fim.Token);
                var servidor = new TcpClient();
                Guardar(servidor);
                await servidor.ConnectAsync(_host, _porta, _fim.Token);
                await Task.WhenAll(BombearAsync(cliente.GetStream(), servidor.GetStream()), BombearAsync(servidor.GetStream(), cliente.GetStream()));
            }
            catch (Exception excecao) when (excecao is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
            }
        }

        // Lê e, antes de repassar, espera o proxy estar liberado: congelado, nada passa, e o remetente acaba sem espaço no envio.
        private async Task BombearAsync(NetworkStream de, NetworkStream para)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                int lidos;
                while ((lidos = await de.ReadAsync(buffer, _fim.Token)) > 0)
                {
                    await Volatile.Read(ref _liberado).Task.WaitAsync(_fim.Token);
                    await para.WriteAsync(buffer.AsMemory(0, lidos), _fim.Token);
                }
            }
            catch (Exception excecao) when (excecao is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
            }
        }
    }
}
