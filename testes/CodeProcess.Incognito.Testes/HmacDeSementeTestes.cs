using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// HMAC da chave de semente: igual ao HMAC-SHA256 da biblioteca, sobre bytes ou sobre prefixo e texto em UTF-8, recusa
/// tamanhos errados, zera a chave no descarte e nunca entrega resultado calculado com chave zerada.
/// </summary>
public sealed class HmacDeSementeTestes
{
    private static readonly byte[] _chave = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();

    [Fact]
    public void Resultado_e_o_HMAC_SHA256_da_chave()
    {
        using var hmac = new HmacDeSemente(_chave);
        var sorteio = new Random(1701);
        for (var vez = 0; vez < 200; vez++)
        {
            var mensagem = new byte[sorteio.Next(0, 300)];
            sorteio.NextBytes(mensagem);
            var resultado = new byte[32];

            hmac.Calcular(mensagem, resultado);

            Assert.Equal(HMACSHA256.HashData(_chave, mensagem), resultado);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(400)]
    public void Resultado_sobre_texto_e_o_HMAC_do_prefixo_e_do_texto_em_UTF8(int tamanho)
    {
        // 400 pedaços (de 1 a 4 bytes em UTF-8) passam dos 512 bytes da pilha e usam o buffer emprestado.
        using var hmac = new HmacDeSemente(_chave);
        string[] pedacos = ["a", "é", "€", "😀"];
        var texto = string.Concat(Enumerable.Range(0, tamanho).Select(i => pedacos[i % 4]));
        byte[] prefixo = [(byte)'S', (byte)'P', 0];
        var resultado = new byte[32];

        Assert.True(hmac.Calcular(prefixo, texto, resultado, recusarInvalido: true));

        Assert.Equal(HMACSHA256.HashData(_chave, prefixo.Concat(Encoding.UTF8.GetBytes(texto)).ToArray()), resultado);
    }

    [Fact]
    public void Surrogate_sem_par_e_recusado_ou_trocado_por_U_FFFD()
    {
        using var hmac = new HmacDeSemente(_chave);
        var resultado = new byte[32];

        Assert.False(hmac.Calcular([], "a\uD800b", resultado, recusarInvalido: true));
        Assert.False(hmac.Calcular([], "a\uDC00b", resultado, recusarInvalido: true));
        Assert.True(hmac.Calcular([], "a\uD800b", resultado, recusarInvalido: false));
        Assert.Equal(HMACSHA256.HashData(_chave, "a�b"u8), resultado);
    }

    [Fact]
    public void Chave_ou_destino_de_tamanho_errado_sao_recusados()
    {
        Assert.Throws<ArgumentException>("chaveDeSemente", () => new HmacDeSemente(new byte[31]));
        using var hmac = new HmacDeSemente(_chave);
        Assert.Throws<ArgumentException>("destino", () => hmac.Calcular([1], new byte[31]));
        Assert.Throws<ArgumentException>("destino", () => hmac.Calcular([1], new byte[33]));
    }

    [Fact]
    public void Descarte_zera_a_copia_da_chave()
    {
        var hmac = new HmacDeSemente(_chave);
        var copia = (byte[])typeof(HmacDeSemente).GetField("_chave", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hmac)!;
        Assert.Equal(_chave, copia);

        hmac.Dispose();

        Assert.True(Array.TrueForAll(copia, b => b == 0), "A cópia da chave não foi zerada no descarte.");
    }

    [Fact]
    public void Depois_do_descarte_o_calculo_e_recusado_e_o_destino_fica_zerado()
    {
        var hmac = new HmacDeSemente(_chave);
        hmac.Dispose();
        hmac.Dispose();
        var destino = Enumerable.Repeat((byte)0xEE, 32).ToArray();

        Assert.Throws<ObjectDisposedException>(() => hmac.Calcular([1], destino));
        Assert.True(Array.TrueForAll(destino, b => b == 0), "O destino ficou com o resultado de chave zerada.");
        Assert.Throws<ObjectDisposedException>(() => hmac.Calcular([], "texto", new byte[32], recusarInvalido: true));
    }

    [Fact]
    public void Descarte_concorrente_nunca_entrega_resultado_de_chave_zerada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        byte[] mensagem = [1, 2, 3];
        var esperado = HMACSHA256.HashData(_chave, mensagem);
        var errados = 0;
        for (var rodada = 0; rodada < 2_000; rodada++)
        {
            var hmac = new HmacDeSemente(_chave);
            using var largada = new Barrier(2);
            var calculo = new Thread(() =>
            {
                var resultado = new byte[32];
                largada.SignalAndWait(cancelamento);
                try
                {
                    while (true)
                    {
                        hmac.Calcular(mensagem, resultado);
                        if (!resultado.AsSpan().SequenceEqual(esperado))
                        {
                            Interlocked.Increment(ref errados);
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            });
            calculo.Start();
            largada.SignalAndWait(cancelamento);
            Thread.SpinWait(50);
            hmac.Dispose();
            Assert.True(calculo.Join(TimeSpan.FromSeconds(10)), "O cálculo não terminou depois do descarte.");
        }

        Assert.Equal(0, errados);
    }
}
