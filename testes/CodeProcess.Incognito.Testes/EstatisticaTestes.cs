using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Testes estatísticos de fumaça das permutações e dos transformadores, com semente fixa: pontos fixos e ciclos em
/// domínios pequenos (os de uma permutação aleatória), entradas estruturadas como as dos bancos (valores consecutivos:
/// dígitos de saída uniformes por posição e nenhuma correlação de postos), avalanche da entrada e de cada byte do ajuste e
/// concordância entre chaves de domínios, variantes, ambientes e versões diferentes e entre chaves que diferem num byte.
/// Cada família de conferência roda também contra controles ruins (identidade, deslocamento, involução, Feistel com a
/// mesma função em toda rodada, multiplicação, chave repetida, ajuste ignorado, Feistel com função de rodada de um bit),
/// que precisam reprovar cada conferência nomeada: um teste que não reprova o controle não prova nada. Pegam defeitos
/// grosseiros de desenho (2 rodadas, função de rodada repetida ou de um bit, ajuste ou parte da chave ignorados), não 4
/// ou 6 rodadas nem uma função de rodada de 16 bits, e não provam segurança; os vetores fixos e a referência em Python
/// prendem a implementação. Os CPFs e celulares consecutivos saem de uma faixa sorteada com semente registrada e nunca
/// aparecem numa mensagem. Nunca trocar a semente para passar.
/// </summary>
public sealed class EstatisticaTestes
{
    private const string Lento = "Lento";

    public static TheoryData<uint, ulong, ulong, int> DominiosPequenos => new()
    {
        { 10, 10, 10, 1_000 },
        { 36, 36, 36, 200 },
        { 10, 40, 100, 60 },
        { 10, 100, 40, 60 },
    };

    [Fact]
    public void A_gama_incompleta_confere_com_quantis_tabelados()
    {
        // Quantis do qui-quadrado (tabelas usuais): P(χ² > q) para os graus de liberdade dados.
        (double Quantil, int Graus, double Cauda)[] tabelados =
        [
            (3.841, 1, 0.05), (16.919, 9, 0.05), (21.666, 9, 0.01), (27.877, 9, 0.001), (49.802, 35, 0.05), (57.342, 35, 0.01),
            (66.619, 35, 0.001), (124.342, 100, 0.05), (149.449, 100, 0.001),
        ];
        foreach (var (quantil, graus, cauda) in tabelados)
        {
            Assert.InRange(Estatistica.PValorQuiQuadrado(quantil, graus), cauda * 0.995, cauda * 1.005);
        }

        // Na região do alfa, em forma fechada: χ²₁ = Z² (P(|Z| ≥ 5) = 5,733031·10⁻⁷) e, com 2 graus, P(χ² > q) = e^(−q/2).
        Assert.InRange(Estatistica.PValorQuiQuadrado(25, 1), 5.733031e-7 * 0.9999, 5.733031e-7 * 1.0001);
        Assert.InRange(Estatistica.PValorQuiQuadrado(60, 2), Math.Exp(-30) * 0.9999, Math.Exp(-30) * 1.0001);
        Assert.InRange(Estatistica.PValorQuiQuadrado(0, 9), 0.9999, 1.0);
        Assert.True(Estatistica.PValorQuiQuadrado(400, 9) < 1e-50, "Cauda distante não ficou pequena.");
    }

    [Theory]
    [MemberData(nameof(DominiosPequenos))]
    public void Pontos_fixos_e_ciclos_seguem_os_de_uma_permutacao_aleatoria(uint radix, ulong a, ulong b, int chaves)
    {
        var dominio = Intervalo(a * b);
        var falhas = PontosFixosECiclos(chaves, dominio, k =>
        {
            using var permutacao = new PermutacaoFeistel(Chave("pontos-fixos", k), radix, a, b);
            return [.. dominio.Select(x => permutacao.Permutar(x, []))];
        });

        Assert.Empty(falhas);
    }

    [Fact]
    [Trait("Categoria", Lento)]
    public void Pontos_fixos_e_ciclos_em_dominio_de_dez_mil()
    {
        var dominio = Intervalo(10_000);
        var falhas = PontosFixosECiclos(300, dominio, k =>
        {
            using var permutacao = new PermutacaoFeistel(Chave("pontos-fixos-lento", k), 10, 100, 100);
            return [.. dominio.Select(x => permutacao.Permutar(x, "ajuste"u8))];
        });

        Assert.Empty(falhas);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void Dia_da_NFF_segue_uma_permutacao_aleatoria_dos_dias_do_mes(int dias)
    {
        // Como no dfe-numero: 6 × 6 com 24 rodadas e {0} ∪ os dias depois do último excluídos, que ficam fora da contagem.
        var excluidos = ConjuntoExcluido.DeValores([0, .. Enumerable.Range(dias + 1, 35 - dias).Select(d => (ulong)d)]);
        var dominio = Enumerable.Range(1, dias).Select(d => (ulong)d).ToArray();
        var falhas = PontosFixosECiclos(500, dominio, k =>
        {
            using var permutacao = new PermutacaoFeistel(Chave("nff-dia", k), 10, 6, 6, 24);
            return [.. dominio.Select(d => permutacao.Permutar(d, "emitente-aamm-serie"u8, excluidos))];
        });

        Assert.Empty(falhas);
    }

    [Fact]
    public void Pontos_fixos_e_ciclos_reprovam_os_controles()
    {
        var dominio = Intervalo(100);
        string[] todas = ["pontos fixos", "chaves sem ponto fixo", "2-ciclos", "ciclos"];
        Reprova(PontosFixosECiclos(1_000, dominio, _ => [.. dominio]), todas);
        Reprova(PontosFixosECiclos(1_000, dominio, _ => [.. dominio.Select(x => (x + 1) % 100)]), todas);

        // Uma involução sorteada (só 2-ciclos, sem ponto fixo, com 100 valores) e uma Feistel com a mesma função em toda
        // rodada, o defeito plausível de desenho.
        var fonte = SequenciaXoshiro.DaSemente(2101);
        Reprova(PontosFixosECiclos(1_000, dominio, _ => Involucao(dominio.Length, fonte)), todas);
        Reprova(PontosFixosECiclos(1_000, dominio, k => [.. dominio.Select(x => FeistelSemRodada(x, 10, 12, (ulong)k))]), todas);
    }

    [Fact]
    public void Chaves_de_dominios_variantes_ambientes_e_versoes_diferentes_concordam_como_permutacoes_independentes()
    {
        // As concordâncias entre P_A e P_B (pontos fixos de P_B⁻¹ ∘ P_A) seguem Poisson(1) por par.
        using var chave = ChaveDeTeste();
        var dominio = Intervalo(100);
        var pares = new List<(byte[] A, byte[] B)>();
        for (var i = 0; i < 100; i++)
        {
            var dominioDoPar = $"dominio-{i}";
            pares.Add((Subchave(chave, dominioDoPar, 1, "num", "homologacao"), Subchave(chave, dominioDoPar + "-outro", 1, "num", "homologacao")));
            pares.Add((Subchave(chave, dominioDoPar, 1, "num", "homologacao"), Subchave(chave, dominioDoPar, 1, "alfa", "homologacao")));
            pares.Add((Subchave(chave, dominioDoPar, 1, "num", "homologacao"), Subchave(chave, dominioDoPar, 1, "num", "producao")));
            pares.Add((Subchave(chave, dominioDoPar, 1, "num", "homologacao"), Subchave(chave, dominioDoPar, 2, "num", "homologacao")));
        }

        // As 100 chaves A e as 400 chaves B são todas diferentes (variante, ambiente ou versão esquecidos na derivação
        // dariam chaves iguais). Somam-se pares que diferem num só byte da chave, 4 por posição: uma permutação que lesse só
        // parte da chave daria permutações iguais. Todas concordam como as de chaves independentes.
        Assert.Equal(500, pares.SelectMany(par => new[] { par.A, par.B }).Select(Convert.ToHexString).Distinct().Count());
        for (var posicao = 0; posicao < Subchaves.Tamanho; posicao++)
        {
            for (var j = 0; j < 4; j++)
            {
                var a = Chave("concordancia-byte", (posicao * 4) + j);
                var b = (byte[])a.Clone();
                b[posicao] ^= (byte)(1 << (j * 2));
                pares.Add((a, b));
            }
        }

        try
        {
            Assert.Empty(Concordancias(pares, dominio));
            Reprova(Concordancias([.. pares.Select(par => (par.A, par.A))], dominio), "concordâncias", "pares sem concordância");

            // Uma permutação que ignorasse o último byte da chave: os pares que diferem só nele concordam em tudo.
            Reprova(Concordancias([.. pares.Select(par => (SemUltimoByte(par.A), SemUltimoByte(par.B)))], dominio), "concordâncias");
        }
        finally
        {
            pares.ForEach(par =>
            {
                CryptographicOperations.ZeroMemory(par.A);
                CryptographicOperations.ZeroMemory(par.B);
            });
        }
    }

    [Fact]
    public void Avalanche_da_entrada_e_de_cada_byte_do_ajuste()
    {
        var fonte = SequenciaXoshiro.DaSemente(2102);
        using var decimal8 = new PermutacaoFeistel(Chave("avalanche", 0), 10, 10_000, 10_000);
        using var base36 = new PermutacaoFeistel(Chave("avalanche", 1), 36, 46_656, 46_656);

        Assert.Empty(Avalanche((x, ajuste) => decimal8.Permutar(x, ajuste), 10, 8, 2_000, 23, fonte));
        Assert.Empty(Avalanche((x, ajuste) => base36.Permutar(x, ajuste), 36, 6, 2_000, 14, fonte));
    }

    [Fact]
    public void Avalanche_reprova_os_controles()
    {
        var fonte = SequenciaXoshiro.DaSemente(2103);
        using var permutacao = new PermutacaoFeistel(Chave("avalanche-controle", 0), 10, 10_000, 10_000);

        Reprova(Avalanche((x, _) => x, 10, 8, 500, 0, fonte), [.. Enumerable.Range(0, 8).Select(posicao => $"avalanche da entrada na posição {posicao},")]);
        Reprova(Avalanche((x, _) => permutacao.Permutar(x, []), 10, 8, 500, 23, fonte), [.. Enumerable.Range(0, 23).Select(indice => $"avalanche do byte {indice} do ajuste,")]);
        Reprova(Avalanche((x, _) => FeistelDeUmBit(x, 10_000, 12), 10, 8, 500, 0, fonte), [.. Enumerable.Range(0, 8).Select(posicao => $"avalanche da entrada na posição {posicao},")]);
    }

    [Fact]
    public void Entradas_consecutivas_nas_permutacoes_saem_sem_padrao()
    {
        // Como nos bancos: números de nota, códigos e sequenciais pequenos e consecutivos, com o mesmo ajuste.
        var fonte = SequenciaXoshiro.DaSemente(2104);
        var ajuste = Encoding.ASCII.GetBytes(new GeradorBr(2104).Cnpj() + "\0" + "55001");
        using var numero = new PermutacaoFeistel(Chave("nnf", 0), 10, 10_000, 100_000);
        using var codigo = new PermutacaoFeistel(Chave("cnf8", 0), 10, 10_000, 10_000);
        using var sequencial = new PermutacaoFeistel(Chave("nff-seq", 0), 10, 100, 100);

        Assert.Empty(Consecutivas(Sequencia(1, 20_000), x => numero.Permutar(x, ajuste), 10, 9));
        Assert.Empty(Consecutivas(Sequencia(1 + fonte.Uniforme(1_000_000), 20_000), x => codigo.Permutar(x, ajuste), 10, 8));
        Assert.Empty(Consecutivas(Sequencia(1, 2_000), x => sequencial.Permutar(x, ajuste), 10, 4));
    }

    [Fact]
    public void Entradas_consecutivas_reprovam_os_controles()
    {
        var entradas = Sequencia(1, 20_000);
        Reprova(Consecutivas(entradas, x => x, 10, 9), "dígitos da posição 8", "postos com a entrada", "postos entre saídas vizinhas");
        // Multiplicação que dá a volta no módulo: sem correlação com a entrada, mas saídas vizinhas andam juntas.
        Reprova(Consecutivas(entradas, x => (x * 618_033_989) % 1_000_000_000, 10, 9), "postos entre saídas vizinhas");
    }

    [Fact]
    public void CPFs_ids_e_celulares_consecutivos_saem_sem_padrao()
    {
        using var chave = ChaveDeTeste();
        using var cpf = new PseudonimizadorCpf(chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var identificador = new PseudonimizadorIdentificador(chave, "pedido-id", 1, "homologacao", 1_000_000_000, PoliticaEntradaMalformada.Falhar);
        using var telefone = new PseudonimizadorTelefone(chave, "telefone", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        var fonte = SequenciaXoshiro.DaSemente(2105);

        Assert.Empty(Consecutivas(Sequencia(100_000_000 + fonte.Uniforme(800_000_000), 20_000), baseCpf =>
        {
            var base9 = baseCpf.ToString("D9", CultureInfo.InvariantCulture);
            Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(base9 + DigitosVerificadores.Cpf(base9).ToString("00", CultureInfo.InvariantCulture), out var saida));
            return ulong.Parse(saida.AsSpan(0, 9), CultureInfo.InvariantCulture);
        }, 10, 9));
        Assert.Empty(Consecutivas(Sequencia(1, 20_000), id =>
        {
            Assert.Equal(Desfecho.Pseudonimizado, identificador.Pseudonimizar((long)id, out var saida));
            return (ulong)saida!.Value;
        }, 10, 9));
        Assert.Empty(Consecutivas(Sequencia(60_000_000 + fonte.Uniforme(30_000_000), 20_000), numero =>
        {
            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar("119" + numero.ToString("D8", CultureInfo.InvariantCulture), out var saida));
            return ulong.Parse(saida.AsSpan(3), CultureInfo.InvariantCulture);
        }, 10, 8));
    }

    // Pontos fixos (soma ~ Poisson(K) e chaves sem nenhum ~ binomial), 2-ciclos (média e variância 1/2 por chave) e
    // número de ciclos (média Hₘ, variância Hₘ − Hₘ⁽²⁾) de K permutações do domínio dado.
    private static List<string> PontosFixosECiclos(int chaves, ulong[] dominio, Func<int, ulong[]> imagens)
    {
        var m = dominio.Length;
        long fixos = 0, semFixos = 0, doisCiclos = 0, ciclos = 0;
        for (var k = 0; k < chaves; k++)
        {
            var imagem = imagens(k);
            var visitado = new bool[m];
            var fixosDaChave = 0;
            for (var i = 0; i < m; i++)
            {
                if (visitado[i])
                {
                    continue;
                }

                var tamanho = 0;
                for (var j = i; !visitado[j]; j = Array.BinarySearch(dominio, imagem[j]))
                {
                    Assert.True(Array.BinarySearch(dominio, imagem[j]) >= 0, "Uma imagem saiu do domínio: não é permutação.");
                    visitado[j] = true;
                    tamanho++;
                }

                ciclos++;
                fixosDaChave += tamanho == 1 ? 1 : 0;
                doisCiclos += tamanho == 2 ? 1 : 0;
            }

            fixos += fixosDaChave;
            semFixos += fixosDaChave == 0 ? 1 : 0;
        }

        double harmonico = 0, harmonico2 = 0, semFixosEsperado = 0, fatorial = 1;
        for (var i = 1; i <= m; i++)
        {
            harmonico += 1.0 / i;
            harmonico2 += 1.0 / ((double)i * i);
        }

        for (var j = 0; j <= Math.Min(m, 30); j++)
        {
            fatorial *= Math.Max(j, 1);
            semFixosEsperado += (j % 2 == 0 ? 1 : -1) / fatorial;
        }

        var falhas = new List<string>();
        Conferir(falhas, "pontos fixos", fixos, chaves, chaves);
        Conferir(falhas, "chaves sem ponto fixo", semFixos, chaves * semFixosEsperado, chaves * semFixosEsperado * (1 - semFixosEsperado));
        Conferir(falhas, "2-ciclos", doisCiclos, chaves / 2.0, chaves / 2.0);
        Conferir(falhas, "ciclos", ciclos, chaves * harmonico, chaves * (harmonico - harmonico2));
        return falhas;
    }

    private static List<string> Concordancias(List<(byte[] A, byte[] B)> pares, ulong[] dominio)
    {
        long concordancias = 0, semNenhuma = 0;
        foreach (var (chaveA, chaveB) in pares)
        {
            using var a = new PermutacaoFeistel(chaveA, 10, 10, 10);
            using var b = new PermutacaoFeistel(chaveB, 10, 10, 10);
            var doPar = dominio.Count(x => a.Permutar(x, []) == b.Permutar(x, []));
            concordancias += doPar;
            semNenhuma += doPar == 0 ? 1 : 0;
        }

        var falhas = new List<string>();
        Conferir(falhas, "concordâncias", concordancias, pares.Count, pares.Count);
        Conferir(falhas, "pares sem concordância", semNenhuma, pares.Count * Math.Exp(-1), pares.Count * Math.Exp(-1) * (1 - Math.Exp(-1)));
        return falhas;
    }

    // Trocar um dígito da entrada muda cada posição da saída com probabilidade 1 − (N/r − 1)/(N − 1) (a saída nova é
    // uniforme entre as outras N − 1); trocar um byte do ajuste, com 1 − 1/r (outra permutação, independente).
    private static List<string> Avalanche(Func<ulong, byte[], ulong> permutar, uint radix, int digitos, int amostras, int bytesDeAjuste, FonteDeSorteio fonte)
    {
        var n = Math.Pow(radix, digitos);
        var pEntrada = 1 - (((n / radix) - 1) / (n - 1));
        var pAjuste = 1 - (1.0 / radix);
        var falhas = new List<string>();
        var ajuste = new byte[bytesDeAjuste];
        for (var posicao = 0; posicao < digitos; posicao++)
        {
            var mudancas = new long[digitos];
            for (var amostra = 0; amostra < amostras; amostra++)
            {
                Sortear(fonte, ajuste);
                var x = fonte.Uniforme((ulong)n);
                var peso = (ulong)Math.Pow(radix, posicao);
                var digito = x / peso % radix;
                var outro = (digito + 1 + fonte.Uniforme(radix - 1)) % radix;
                Contar(mudancas, permutar(x, ajuste), permutar(x - (digito * peso) + (outro * peso), ajuste), radix);
            }

            ConferirPosicoes(falhas, $"avalanche da entrada na posição {posicao}", mudancas, amostras, pEntrada);
        }

        for (var indice = 0; indice < bytesDeAjuste; indice++)
        {
            var mudancas = new long[digitos];
            for (var amostra = 0; amostra < amostras; amostra++)
            {
                Sortear(fonte, ajuste);
                var x = fonte.Uniforme((ulong)n);
                var antes = permutar(x, ajuste);
                ajuste[indice] ^= (byte)(1 + fonte.Uniforme(255));
                Contar(mudancas, antes, permutar(x, ajuste), radix);
            }

            ConferirPosicoes(falhas, $"avalanche do byte {indice} do ajuste", mudancas, amostras, pAjuste);
        }

        return falhas;
    }

    // Saídas de entradas consecutivas: cada posição com os dígitos uniformes (qui-quadrado) e nenhuma correlação de postos
    // com a entrada nem entre saídas vizinhas.
    private static List<string> Consecutivas(ulong[] entradas, Func<ulong, ulong> permutar, uint radix, int digitos)
    {
        var saidas = entradas.Select(permutar).ToArray();
        var falhas = new List<string>();
        if (saidas.Distinct().Count() != saidas.Length)
        {
            falhas.Add("saídas repetidas: não é permutação");
        }

        var esperados = Enumerable.Repeat(saidas.Length / (double)radix, (int)radix).ToArray();
        for (var posicao = 0; posicao < digitos; posicao++)
        {
            var peso = (ulong)Math.Pow(radix, posicao);
            var contagem = new long[radix];
            foreach (var saida in saidas)
            {
                contagem[saida / peso % radix]++;
            }

            var p = Estatistica.PValorQuiQuadrado(Estatistica.QuiQuadrado(contagem, esperados), (int)radix - 1);
            if (!(p >= Estatistica.AlfaQuiQuadrado))
            {
                falhas.Add($"dígitos da posição {posicao}: p = {p:E2}");
            }
        }

        var escala = Math.Sqrt(saidas.Length - 1);
        var comAEntrada = Estatistica.Spearman(entradas, saidas) * escala;
        var entreVizinhas = Estatistica.Spearman(saidas[..^1], saidas[1..]) * Math.Sqrt(saidas.Length - 2);
        if (!(Math.Abs(comAEntrada) < Estatistica.LimiteZ))
        {
            falhas.Add($"postos com a entrada: z = {comAEntrada:F1}");
        }

        if (!(Math.Abs(entreVizinhas) < Estatistica.LimiteZ))
        {
            falhas.Add($"postos entre saídas vizinhas: z = {entreVizinhas:F1}");
        }

        return falhas;
    }

    private static void Conferir(List<string> falhas, string nome, double observado, double media, double variancia)
    {
        var z = Estatistica.Z(observado, media, variancia);
        if (!(Math.Abs(z) < Estatistica.LimiteZ))
        {
            falhas.Add($"{nome}: z = {z:F1}");
        }
    }

    // Um controle ruim precisa reprovar cada conferência nomeada, não só alguma delas.
    private static void Reprova(List<string> falhas, params string[] prefixos)
    {
        foreach (var prefixo in prefixos)
        {
            Assert.Contains(falhas, falha => falha.StartsWith(prefixo, StringComparison.Ordinal));
        }
    }

    private static void ConferirPosicoes(List<string> falhas, string nome, long[] mudancas, int amostras, double p)
    {
        for (var posicao = 0; posicao < mudancas.Length; posicao++)
        {
            Conferir(falhas, $"{nome}, saída {posicao}", mudancas[posicao], amostras * p, amostras * p * (1 - p));
        }
    }

    private static void Contar(long[] mudancas, ulong antes, ulong depois, uint radix)
    {
        for (var posicao = 0; posicao < mudancas.Length; posicao++)
        {
            mudancas[posicao] += antes % radix != depois % radix ? 1 : 0;
            antes /= radix;
            depois /= radix;
        }
    }

    // Feistel balanceada com a mesma função em toda rodada (sem o número da rodada): a permutação vira a 12ª potência de
    // uma rodada só, e todo ciclo da rodada com comprimento que divide 12 se desfaz em pontos fixos e ciclos curtos. É o
    // controle que pontos fixos e ciclos precisam reprovar.
    private static ulong FeistelSemRodada(ulong x, ulong lado, int rodadas, ulong chave)
    {
        var (esquerda, direita) = Math.DivRem(x, lado);
        for (var rodada = 0; rodada < rodadas; rodada++)
        {
            var f = Misturar(direita ^ (chave * 0x9E37_79B9_7F4A_7C15UL)) % lado;
            (esquerda, direita) = (direita, (esquerda + f) % lado);
        }

        return (esquerda * lado) + direita;
    }

    // O finalizador do SplitMix64.
    private static ulong Misturar(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }

    // Feistel balanceada de 12 rodadas sobre 10⁴ × 10⁴ com função de rodada de um bit: uma bijeção sem difusão, o
    // controle que a avalanche precisa reprovar.
    private static ulong FeistelDeUmBit(ulong x, ulong lado, int rodadas)
    {
        var (esquerda, direita) = Math.DivRem(x, lado);
        for (var rodada = 0; rodada < rodadas; rodada++)
        {
            var f = ((direita * 0x9E37_79B9_7F4A_7C15UL) + (ulong)rodada) >> 63;
            (esquerda, direita) = (direita, (esquerda + f) % lado);
        }

        return (esquerda * lado) + direita;
    }

    private static byte[] SemUltimoByte(byte[] chave)
    {
        var copia = (byte[])chave.Clone();
        copia[^1] = 0;
        return copia;
    }

    private static ulong[] Involucao(int tamanho, FonteDeSorteio fonte)
    {
        var imagem = Enumerable.Range(0, tamanho).Select(i => (ulong)i).ToArray();
        var ordem = Enumerable.Range(0, tamanho).OrderBy(_ => fonte.Uniforme(1UL << 32)).ToArray();
        for (var i = 0; i + 1 < tamanho; i += 2)
        {
            (imagem[ordem[i]], imagem[ordem[i + 1]]) = ((ulong)ordem[i + 1], (ulong)ordem[i]);
        }

        return imagem;
    }

    private static ulong[] Intervalo(ulong tamanho) => [.. Enumerable.Range(0, (int)tamanho).Select(i => (ulong)i)];

    private static ulong[] Sequencia(ulong inicio, int quantidade) => [.. Enumerable.Range(0, quantidade).Select(i => inicio + (ulong)i)];

    private static void Sortear(FonteDeSorteio fonte, byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)fonte.Uniforme(256);
        }
    }

    // Chave de domínio repetível por rótulo e índice, distinta para cada par, sem passar pela derivação.
    private static byte[] Chave(string rotulo, int indice) => SHA256.HashData(Encoding.UTF8.GetBytes($"{rotulo}\0{indice}"));

    private static byte[] Subchave(ChaveIncognito chave, string dominio, int versao, string variante, string ambiente)
    {
        var subchave = new byte[Subchaves.Tamanho];
        Subchaves.DerivarChaveDeDominio(chave, dominio, versao, variante, ambiente, subchave);
        return subchave;
    }

    // A chave pública de teste dos vetores.
    private static ChaveIncognito ChaveDeTeste()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }
}
