using System.Buffers;
using System.Text;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>O corte do fluxo do <c>COPY</c> em lotes (<see cref="CortadorDeLinhas"/>), sem banco: o fluxo é um vetor de bytes.</summary>
public sealed partial class LinhasPostgreSqlTestes
{
    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(65_536)]
    public async Task O_cortador_monta_as_linhas_partidas_entre_leituras_em_lotes_de_mil(int porLeitura)
    {
        var pool = new PoolQueConfere();
        var esperadas = Enumerable.Range(1, 2500).Select(i => $"{i}\tlinha {i}").ToList();
        var cortador = new CortadorDeLinhas(pool);

        var (lotes, lidas) = await CortarAsync(cortador, pool, Fluxo(esperadas), porLeitura);

        Assert.Equal([1000, 1000, 500], lotes);
        Assert.Equal(esperadas, lidas);
        Assert.True(cortador.FimDoFluxo);
        Assert.True(pool.DevolvidosLimpos > 0 && pool.DevolvidosSujos == 0);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_cortador_fecha_o_lote_pelos_bytes_antes_de_crescer()
    {
        // Linhas de mil bytes: o buffer enche no meio de uma linha, o lote fecha com as inteiras e a parcial vai ao seguinte.
        var pool = new PoolQueConfere();
        var esperadas = Enumerable.Range(1, 600).Select(i => i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 993)).ToList();
        var cortador = new CortadorDeLinhas(pool);

        var (lotes, lidas) = await CortarAsync(cortador, pool, Fluxo(esperadas), 65_536);

        Assert.Equal(esperadas, lidas);
        Assert.Equal([262, 262, 76], lotes);
        Assert.Equal(CortadorDeLinhas.BytesPorLote, pool.MaiorPedido);
        Assert.Equal(0, pool.DevolvidosSujos);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_linha_maior_que_o_lote_vai_sozinha_e_os_buffers_voltam_limpos()
    {
        var pool = new PoolQueConfere();
        var larga = "2\t" + new string('y', 3 * 1024 * 1024);
        string[] esperadas = ["1\tcurta", larga, "3\tcurta"];
        var cortador = new CortadorDeLinhas(pool);

        var (lotes, lidas) = await CortarAsync(cortador, pool, Fluxo(esperadas), 65_536);

        Assert.Equal([1, 1, 1], lotes);
        Assert.Equal(esperadas, lidas);
        Assert.True(pool.MaiorPedido > larga.Length, $"O maior buffer pedido tem {pool.MaiorPedido} bytes.");
        Assert.True(pool.DevolvidosLimpos >= 3 && pool.DevolvidosSujos == 0);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_fluxo_que_termina_no_meio_de_uma_linha_falha_e_devolve_os_buffers_limpos()
    {
        var pool = new PoolQueConfere();
        var cortador = new CortadorDeLinhas(pool);
        var ler = Leitor("1\tinteira\n2\tpela metade"u8.ToArray(), 4);

        var falha = await Assert.ThrowsAsync<IncognitoException>(() => cortador.ProximoAsync(ler));

        Assert.Equal("INC3133", falha.Codigo);
        cortador.Limpar();
        Assert.True(pool.DevolvidosLimpos > 0 && pool.DevolvidosSujos == 0);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_resto_de_quem_para_no_meio_volta_limpo_uma_vez()
    {
        var pool = new PoolQueConfere();
        var cortador = new CortadorDeLinhas(pool);
        var ler = Leitor(Fluxo(Enumerable.Range(1, 2500).Select(i => $"{i}\tlinha {i}")), 65_536);
        var primeiro = await cortador.ProximoAsync(ler);
        Assert.NotNull(primeiro);
        Buffers.Devolver(pool, primeiro.Value.Dados, primeiro.Value.Inicios[primeiro.Value.Linhas]);
        ArrayPool<int>.Shared.Return(primeiro.Value.Inicios);
        var antes = pool.DevolvidosLimpos;

        cortador.Limpar();
        cortador.Limpar();

        Assert.Equal(antes + 1, pool.DevolvidosLimpos);
        Assert.Equal(0, pool.DevolvidosSujos);
    }

    // Corta o fluxo inteiro, devolvendo cada lote como o lote do PostgreSQL faria: o número de linhas de cada lote e as linhas.
    private static async Task<(List<int> Lotes, List<string> Linhas)> CortarAsync(CortadorDeLinhas cortador, ArrayPool<byte> pool, byte[] fluxo, int porLeitura)
    {
        var ler = Leitor(fluxo, porLeitura);
        var lotes = new List<int>();
        var linhas = new List<string>();
        while (await cortador.ProximoAsync(ler) is ({ } dados, { } inicios, var quantas))
        {
            lotes.Add(quantas);
            for (var i = 0; i < quantas; i++)
            {
                linhas.Add(Encoding.UTF8.GetString(dados, inicios[i], inicios[i + 1] - inicios[i] - 1));
            }

            Buffers.Devolver(pool, dados, inicios[quantas]);
            ArrayPool<int>.Shared.Return(inicios);
        }

        cortador.Limpar();
        return (lotes, linhas);
    }

    private static byte[] Fluxo(IEnumerable<string> linhas) => Encoding.UTF8.GetBytes(string.Concat(linhas.Select(linha => linha + "\n")));

    // Lê o fluxo em pedaços de até porLeitura bytes; zero no fim.
    private static Func<Memory<byte>, ValueTask<int>> Leitor(byte[] fluxo, int porLeitura)
    {
        var posicao = 0;
        return destino =>
        {
            var quantos = Math.Min(Math.Min(porLeitura, destino.Length), fluxo.Length - posicao);
            fluxo.AsSpan(posicao, quantos).CopyTo(destino.Span);
            posicao += quantos;
            return ValueTask.FromResult(quantos);
        };
    }
}
