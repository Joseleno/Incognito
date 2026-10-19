using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// Um servidor PostgreSQL falso, para uma conexão só: recusa o TLS (e a criptografia GSS), recebe o pacote de início,
/// pede o método de autenticação escolhido e anota o que o cliente mandou. Prova o que o dialeto nunca manda a um servidor
/// hostil: senha em claro, credencial do sistema, sessão em claro fora da própria máquina.
/// </summary>
internal sealed class ServidorFalsoPostgreSql : IAsyncDisposable
{
    /// <summary>Senha em claro (AuthenticationCleartextPassword).</summary>
    internal const int SenhaEmClaro = 3;

    /// <summary>SSPI do Windows (AuthenticationSSPI).</summary>
    internal const int Sspi = 9;

    private const int PedidoDeTls = 80877103;
    private const int PedidoDeGss = 80877104;
    private const int VersaoDoProtocolo = 196608;

    private readonly TcpListener _ouvinte;
    private readonly CancellationTokenSource _prazo = new(TimeSpan.FromSeconds(20));

    /// <summary>Começa a ouvir no endereço e atende a primeira conexão.</summary>
    /// <param name="endereco">O endereço.</param>
    /// <param name="autenticacao">O código do pedido de autenticação.</param>
    internal ServidorFalsoPostgreSql(IPAddress endereco, int autenticacao)
    {
        _ouvinte = new TcpListener(endereco, 0);
        _ouvinte.Start();
        // Fora do contexto de sincronização do teste: o cliente espera a resposta enquanto o servidor a escreve.
        Recebido = Task.Run(() => AtenderAsync(autenticacao));
    }

    /// <summary>A porta.</summary>
    internal int Porta => ((IPEndPoint)_ouvinte.LocalEndpoint).Port;

    /// <summary>
    /// O que o cliente mandou, em ordem: <c>tls</c> e <c>gss</c> (pedidos de criptografia), <c>inicio</c> (o pacote de
    /// início, que traz usuário e banco) e o tipo da mensagem depois do pedido de autenticação (<c>p</c> é senha ou token).
    /// </summary>
    internal Task<List<string>> Recebido { get; }

    /// <summary>Para de ouvir.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask DisposeAsync()
    {
        await _prazo.CancelAsync();
        _ouvinte.Stop();
        _ouvinte.Dispose();
        await Recebido;
        _prazo.Dispose();
    }

    // Atende conexões até uma conversar: o Npgsql pode abrir antes uma conexão que fecha sem mandar nada.
    private async Task<List<string>> AtenderAsync(int autenticacao)
    {
        var recebido = new List<string>();
        try
        {
            while (recebido.Count == 0)
            {
                using var cliente = await _ouvinte.AcceptTcpClientAsync(_prazo.Token).ConfigureAwait(false);
                await ConversarAsync(cliente.GetStream(), autenticacao, recebido).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Acabou o prazo sem uma conexão que conversasse.
        }

        return recebido;
    }

    private async Task ConversarAsync(NetworkStream fluxo, int autenticacao, List<string> recebido)
    {
        try
        {
            while (true)
            {
                var tamanho = await LerInteiroAsync(fluxo).ConfigureAwait(false);
                var codigo = await LerInteiroAsync(fluxo).ConfigureAwait(false);
                if (codigo is PedidoDeTls or PedidoDeGss)
                {
                    recebido.Add(codigo == PedidoDeTls ? "tls" : "gss");
                    await fluxo.WriteAsync("N"u8.ToArray(), _prazo.Token).ConfigureAwait(false);
                    continue;
                }

                if (codigo != VersaoDoProtocolo || tamanho is < 8 or > 10_000)
                {
                    // Outra coisa (por exemplo, um TLS direto): anota e encerra.
                    recebido.Add($"outro:{tamanho:X8}:{codigo:X8}");
                    return;
                }

                await fluxo.ReadExactlyAsync(new byte[tamanho - 8], _prazo.Token).ConfigureAwait(false);
                recebido.Add("inicio");

                var pedido = new byte[9];
                pedido[0] = (byte)'R';
                BinaryPrimitives.WriteInt32BigEndian(pedido.AsSpan(1), 8);
                BinaryPrimitives.WriteInt32BigEndian(pedido.AsSpan(5), autenticacao);
                await fluxo.WriteAsync(pedido, _prazo.Token).ConfigureAwait(false);

                var resposta = new byte[1];
                if (await fluxo.ReadAsync(resposta, _prazo.Token).ConfigureAwait(false) == 1)
                {
                    recebido.Add(((char)resposta[0]).ToString());
                }

                return;
            }
        }
        catch (Exception erro) when (erro is IOException or EndOfStreamException)
        {
            // O cliente desistiu e fechou a conexão: o que ele mandou até aí é a resposta.
        }
    }

    private async Task<int> LerInteiroAsync(NetworkStream fluxo)
    {
        var bytes = new byte[4];
        await fluxo.ReadExactlyAsync(bytes, _prazo.Token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }
}
