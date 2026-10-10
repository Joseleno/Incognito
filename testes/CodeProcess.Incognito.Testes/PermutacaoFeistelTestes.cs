using System.Reflection;
using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Feistel modular sobre Z_a × Z_b: bijeção exaustiva em domínios reduzidos, inversa, cycle-walking, validação e uso
/// concorrente. O vetor fixo em <c>testes/vetores/</c> foi calculado por uma implementação independente e é contrato.
/// </summary>
public sealed class PermutacaoFeistelTestes
{
    private const string Pasta = "vetores";
    private const string Vetor = "feistel@1.json";

    // Casos e quantidade de pares do vetor: tirar um caso ou um par do arquivo derruba o teste.
    private static readonly Dictionary<string, int> _paresPorCaso = new()
    {
        ["desbalanceado 10^4 x 10^5"] = 15,
        ["desbalanceado 4000 x 10^4 com ajuste"] = 15,
        ["base 36 36^4 x 36^4"] = 15,
        ["desbalanceado 40 x 100 com ajuste binario"] = 15,
        ["a maior que b 100 x 40"] = 15,
        ["a minimo 2 x 20"] = 40,
        ["extremos 2^32-1 x 2^32-1"] = 16,
        ["ajuste de 300 bytes"] = 15,
        ["10^4 com exclusoes"] = 25,
        ["6 x 6 com metade excluida"] = 36,
    };

    public static TheoryData<string> CasosDoVetor => ArquivosDeTeste.NomesDosCasos(Pasta, Vetor, "casos");

    public static TheoryData<uint, ulong, ulong> DominiosReduzidos => new()
    {
        { 10, 100, 100 },
        { 10, 40, 100 },
        { 10, 100, 40 },
        { 36, 36, 1296 },
    };

    public static TheoryData<uint, ulong, ulong> DominiosGrandes => new()
    {
        { 10, 10_000, 100_000 },
        { 10, 4_000, 10_000 },
        { 36, 1_679_616, 1_679_616 },
        { 10, 1_000_000_000, 1_000_000_000 },
    };

    [Theory]
    [MemberData(nameof(CasosDoVetor))]
    public void Permutacao_confere_com_o_vetor_fixo(string nome)
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, Vetor);
        var caso = ArquivosDeTeste.Caso(documento, "casos", nome);
        Assert.Equal(12, documento.RootElement.GetProperty("rodadas").GetInt32());
        using var permutacao = new PermutacaoFeistel(
            ArquivosDeTeste.Hex(documento.RootElement, "chaveDeDominio"),
            caso.GetProperty("radix").GetUInt32(),
            caso.GetProperty("a").GetUInt64(),
            caso.GetProperty("b").GetUInt64());
        var ajuste = ArquivosDeTeste.Hex(caso, "ajuste");
        var excluidos = ConjuntoExcluido.DeValores(
            [.. caso.GetProperty("excluidos").EnumerateArray().Select(valor => valor.GetUInt64())]);

        var conferidos = 0;
        foreach (var par in caso.GetProperty("pares").EnumerateArray())
        {
            var entrada = par.GetProperty("entrada").GetUInt64();
            var saida = par.GetProperty("saida").GetUInt64();
            Assert.Equal(saida, permutacao.Permutar(entrada, ajuste, excluidos));
            Assert.Equal(entrada, permutacao.Inverter(saida, ajuste, excluidos));
            conferidos++;
        }

        Assert.Equal(_paresPorCaso[nome], conferidos);
    }

    [Fact]
    public void Vetor_fixo_tem_exatamente_os_casos_gerados()
    {
        var casos = ArquivosDeTeste.NomesDosCasos(Pasta, Vetor, "casos").Select(linha => linha.Data).Order();

        Assert.Equal(_paresPorCaso.Keys.Order(), casos);
    }

    [Theory]
    [MemberData(nameof(DominiosReduzidos))]
    public void Permutacao_e_bijecao_exaustiva(uint radix, ulong a, ulong b)
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), radix, a, b);
        var tamanho = a * b;
        var vistos = new bool[tamanho];

        for (ulong x = 0; x < tamanho; x++)
        {
            var y = permutacao.Permutar(x, "ajuste"u8);
            Assert.True(y < tamanho, "A saída caiu fora do domínio.");
            Assert.False(vistos[y], "Duas entradas deram a mesma saída.");
            vistos[y] = true;
            Assert.Equal(x, permutacao.Inverter(y, "ajuste"u8));
        }
    }

    [Theory]
    [MemberData(nameof(DominiosReduzidos))]
    public void Permutacao_com_50_por_cento_de_exclusoes_e_bijecao_exaustiva(uint radix, ulong a, ulong b)
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), radix, a, b);
        var tamanho = a * b;
        var excluidos = ConjuntoExcluido.PorRegra(valor => valor % 2 == 0, tamanho / 2);
        var vistos = new bool[tamanho];

        for (ulong x = 0; x < tamanho; x++)
        {
            var y = permutacao.Permutar(x, [], excluidos);
            Assert.True(y < tamanho, "A saída caiu fora do domínio.");
            Assert.False(vistos[y], "Duas entradas deram a mesma saída.");
            vistos[y] = true;

            // Fora de E a saída fica fora de E; dentro de E a entrada é ponto fixo.
            Assert.Equal(x % 2 == 0, y % 2 == 0);
            Assert.True(x % 2 != 0 || y == x, "Valor excluído não ficou como ponto fixo.");
            Assert.Equal(x, permutacao.Inverter(y, [], excluidos));
        }
    }

    [Theory]
    [MemberData(nameof(DominiosGrandes))]
    public void Inversa_desfaz_a_permutacao(uint radix, ulong a, ulong b)
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), radix, a, b);
        var sorteio = new Random(20261010);
        var tamanho = a * b;
        var bytesDeAjuste = new byte[16];

        for (var amostra = 0; amostra < 2_000; amostra++)
        {
            var x = (ulong)sorteio.NextInt64((long)Math.Min(tamanho, long.MaxValue));
            var ajuste = bytesDeAjuste.AsSpan(0, sorteio.Next(17));
            sorteio.NextBytes(ajuste);

            var y = permutacao.Permutar(x, ajuste);

            Assert.True(y < tamanho, "A saída caiu fora do domínio.");
            Assert.Equal(x, permutacao.Inverter(y, ajuste));
        }
    }

    [Fact]
    public void Caminhada_longa_dentro_do_limite_nao_estoura()
    {
        // Com esta chave fixa e os pares excluídos, a entrada 6189 caminha 17 passos (conferido pela referência em
        // Python): um limite menor que 64 lançaria INC1002 aqui.
        using var permutacao = new PermutacaoFeistel(Enumerable.Repeat((byte)0x60, 32).ToArray(), 10, 100, 100);
        var excluidos = ConjuntoExcluido.PorRegra(valor => valor % 2 == 0, 5_000);

        var saida = permutacao.Permutar(6_189, [], excluidos);

        Assert.True(saida % 2 == 1, "A saída caiu nos excluídos.");
        Assert.Equal(6_189UL, permutacao.Inverter(saida, [], excluidos));
    }

    [Fact]
    public void Extremos_do_dominio_vao_e_voltam()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, uint.MaxValue, uint.MaxValue);
        var ultimo = (ulong)uint.MaxValue * uint.MaxValue - 1;

        foreach (var x in new[] { 0UL, 1UL, ultimo - 1, ultimo })
        {
            var y = permutacao.Permutar(x, []);
            Assert.True(y <= ultimo, "A saída caiu fora do domínio.");
            Assert.Equal(x, permutacao.Inverter(y, []));
        }
    }

    [Fact]
    public void Ajuste_chave_e_dimensoes_diferentes_dao_permutacoes_diferentes()
    {
        var chave = ChaveAleatoria();
        using var base10 = new PermutacaoFeistel(chave, 10, 100, 100);
        using var outraChave = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        using var outroRadix = new PermutacaoFeistel(chave, 36, 100, 100);
        using var invertida = new PermutacaoFeistel(chave, 10, 50, 200);

        var referencia = Imagem(base10, "11"u8);

        Assert.NotEqual(referencia, Imagem(base10, "21"u8));
        Assert.NotEqual(referencia, Imagem(base10, []));
        Assert.NotEqual(referencia, Imagem(outraChave, "11"u8));
        Assert.NotEqual(referencia, Imagem(outroRadix, "11"u8));
        Assert.NotEqual(referencia, Imagem(invertida, "11"u8));
        Assert.Equal(referencia, Imagem(base10, "11"u8));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(1)]
    public void Numero_impar_de_rodadas_e_recusado(int rodadas)
    {
        Assert.Throws<ArgumentException>(nameof(rodadas), () => new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100, rodadas));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(256)]
    public void Rodadas_fora_de_10_a_254_sao_recusadas(int rodadas)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(rodadas), () => new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100, rodadas));
    }

    [Fact]
    public void Rodadas_pares_de_10_a_254_sao_aceitas()
    {
        using var minima = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100, 10);
        using var maxima = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100, 254);

        Assert.Equal(1234UL, minima.Inverter(minima.Permutar(1234, []), []));
        Assert.Equal(1234UL, maxima.Inverter(maxima.Permutar(1234, []), []));
    }

    [Fact]
    public void Dimensoes_radix_e_chave_invalidos_sao_recusados()
    {
        Assert.Throws<ArgumentOutOfRangeException>("a", () => new PermutacaoFeistel(ChaveAleatoria(), 10, 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>("b", () => new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 1));
        Assert.Throws<ArgumentOutOfRangeException>("a", () => new PermutacaoFeistel(ChaveAleatoria(), 10, (ulong)uint.MaxValue + 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>("b", () => new PermutacaoFeistel(ChaveAleatoria(), 10, 100, (ulong)uint.MaxValue + 1));
        Assert.Throws<ArgumentOutOfRangeException>("radix", () => new PermutacaoFeistel(ChaveAleatoria(), 1, 100, 100));
        Assert.Throws<ArgumentException>("chaveDeDominio", () => new PermutacaoFeistel(new byte[31], 10, 100, 100));
        Assert.Throws<ArgumentException>("chaveDeDominio", () => new PermutacaoFeistel(new byte[33], 10, 100, 100));
    }

    [Theory]
    [InlineData(10, 2, 21)]
    [InlineData(10, 1_000, 99)]
    [InlineData(36, 2, 73)]
    public void Lado_maior_que_radix_vezes_o_outro_e_recusado(uint radix, ulong a, ulong b)
    {
        Assert.Throws<ArgumentException>(nameof(b), () => new PermutacaoFeistel(ChaveAleatoria(), radix, a, b));
    }

    [Theory]
    [InlineData(10, 2, 20)]
    [InlineData(10, 1_000, 100)]
    [InlineData(36, 36, 1_296)]
    public void Lado_ate_radix_vezes_o_outro_e_aceito(uint radix, ulong a, ulong b)
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), radix, a, b);

        Assert.Equal(a * b, permutacao.Tamanho);
    }

    [Fact]
    public void Valor_fora_do_dominio_e_recusado_sem_aparecer_na_mensagem()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        const ulong ForaDoDominio = 987_654_321;

        var naPermutacao = Assert.Throws<ArgumentOutOfRangeException>(() => permutacao.Permutar(ForaDoDominio, []));
        var naInversa = Assert.Throws<ArgumentOutOfRangeException>(() => permutacao.Inverter(ForaDoDominio, []));
        var comExclusoes = Assert.Throws<ArgumentOutOfRangeException>(() => permutacao.Permutar(10_000, [], ConjuntoExcluido.Vazio));

        Assert.DoesNotContain("987654321", naPermutacao.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", naInversa.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("10000", comExclusoes.Message, StringComparison.Ordinal);
        Assert.Null(naPermutacao.ActualValue);
        Assert.Null(naInversa.ActualValue);
        Assert.Null(comExclusoes.ActualValue);
    }

    [Fact]
    public void Ajuste_maior_que_65535_bytes_e_recusado()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);

        Assert.Throws<ArgumentException>("ajuste", () => permutacao.Permutar(1, new byte[65_536]));
        Assert.Equal(1UL, permutacao.Inverter(permutacao.Permutar(1, new byte[65_535]), new byte[65_535]));
    }

    [Fact]
    public void Exclusoes_acima_de_50_por_cento_sao_recusadas()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        var excessivo = ConjuntoExcluido.PorRegra(valor => valor > 4_998, 5_001);

        Assert.Throws<ArgumentException>("excluidos", () => permutacao.Permutar(1, [], excessivo));
    }

    [Fact]
    public void Estouro_do_limite_lanca_excecao_sem_o_valor()
    {
        // Chave fixa: com uma aleatória, 4321 poderia ser ponto fixo da permutação (chance de 1 em 10⁴).
        using var permutacao = new PermutacaoFeistel(Enumerable.Repeat((byte)0x5A, 32).ToArray(), 10, 100, 100);

        // Um conjunto que declara 1 excluído mas exclui quase tudo: o limite de |E| + 1 = 2 passos estoura.
        var mentiroso = ConjuntoExcluido.PorRegra(valor => valor != 4_321, 1);
        var excecao = Assert.Throws<IncognitoException>(() => permutacao.Permutar(4_321, [], mentiroso));

        Assert.Equal("INC1002", excecao.Codigo);
        Assert.Equal(
            "A permutação excedeu o limite de passos ao evitar os valores excluídos: o conjunto exclui mais valores do que declara. Nenhum valor foi produzido.",
            excecao.Message);
        Assert.Null(excecao.InnerException);
    }

    [Fact]
    public void Entrada_excluida_e_ponto_fixo()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        var excluidos = ConjuntoExcluido.DeValores(0, 1111, 9999);

        Assert.Equal(1111UL, permutacao.Permutar(1111, [], excluidos));
        Assert.Equal(9999UL, permutacao.Inverter(9999, [], excluidos));
    }

    [Fact]
    public void Conjunto_de_valores_ignora_repeticoes_e_conta_os_distintos()
    {
        var excluidos = ConjuntoExcluido.DeValores(5, 5, 7);

        Assert.Equal(2UL, excluidos.Quantidade);
        Assert.True(excluidos.Contem(5) && excluidos.Contem(7) && !excluidos.Contem(6), "O conjunto não reconhece os valores.");
        Assert.Equal(0UL, ConjuntoExcluido.Vazio.Quantidade);
    }

    [Fact]
    public void Permutacao_descartada_zera_a_chave_e_nao_e_usada()
    {
        var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        permutacao.Permutar(1, []);
        var chave = (byte[])typeof(PermutacaoFeistel).GetField("_chave", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(permutacao)!;

        permutacao.Dispose();

        Assert.True(Array.TrueForAll(chave, octeto => octeto == 0), "A cópia da chave de domínio não foi zerada.");
        Assert.Throws<ObjectDisposedException>(() => permutacao.Permutar(1, []));
        Assert.Throws<ObjectDisposedException>(() => permutacao.Inverter(1, []));
        permutacao.Dispose();
    }

    [Fact]
    public void Uso_concorrente_da_os_mesmos_resultados_que_o_sequencial()
    {
        using var permutacao = new PermutacaoFeistel(ChaveAleatoria(), 10, 100, 100);
        var ajustes = Enumerable.Range(0, 8).Select(indice => new[] { (byte)indice }).ToArray();
        var esperados = ajustes.Select(ajuste => Imagem(permutacao, ajuste)).ToArray();
        var cancelamento = TestContext.Current.CancellationToken;
        var resultados = new ulong[ajustes.Length][];
        var falhas = new Exception?[ajustes.Length];
        using var largada = new Barrier(ajustes.Length);

        // Exceção capturada dentro da thread: uma regressão vira falha do teste, não queda do processo de testes.
        var threads = Enumerable.Range(0, ajustes.Length).Select(indice => new Thread(() =>
        {
            try
            {
                largada.SignalAndWait(cancelamento);
                var imagem = Imagem(permutacao, ajustes[indice]);
                for (ulong y = 0; y < (ulong)imagem.Length; y += 7)
                {
                    if (permutacao.Inverter(imagem[y], ajustes[indice]) != y)
                    {
                        throw new InvalidOperationException("A inversa concorrente não desfez a permutação.");
                    }
                }

                resultados[indice] = imagem;
            }
            catch (Exception excecao)
            {
                falhas[indice] = excecao;
            }
        })).ToList();
        threads.ForEach(thread => thread.Start());

        Assert.All(threads, thread => Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Uma thread não terminou."));
        Assert.All(falhas, falha => Assert.Null(falha));
        for (var indice = 0; indice < ajustes.Length; indice++)
        {
            Assert.Equal(esperados[indice], resultados[indice]);
        }
    }

    private static byte[] ChaveAleatoria() => RandomNumberGenerator.GetBytes(32);

    private static ulong[] Imagem(PermutacaoFeistel permutacao, ReadOnlySpan<byte> ajuste)
    {
        var imagem = new ulong[permutacao.Tamanho];
        for (ulong x = 0; x < permutacao.Tamanho; x++)
        {
            imagem[x] = permutacao.Permutar(x, ajuste);
        }

        return imagem;
    }
}
