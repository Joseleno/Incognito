using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio CEP: saída sempre nas faixas da UF de entrada, escolhida pela entidade (não pelo próprio CEP), formas e
/// máscaras, bordas das faixas e a política de malformado. As faixas dos testes são escritas à parte da implementação;
/// CEPs e entidades são sorteados na hora; a chave é a pública do vetor de derivação.
/// </summary>
public sealed class PseudonimizadorCepTestes : IDisposable
{
    // Faixas de CEP por UF dos Correios, escritas à parte da implementação.
    private static readonly Dictionary<string, (int Inicio, int Fim)[]> _faixasPorUf = new()
    {
        ["SP"] = [(1_000_000, 19_999_999)],
        ["RJ"] = [(20_000_000, 28_999_999)],
        ["ES"] = [(29_000_000, 29_999_999)],
        ["MG"] = [(30_000_000, 39_999_999)],
        ["BA"] = [(40_000_000, 48_999_999)],
        ["SE"] = [(49_000_000, 49_999_999)],
        ["PE"] = [(50_000_000, 56_999_999)],
        ["AL"] = [(57_000_000, 57_999_999)],
        ["PB"] = [(58_000_000, 58_999_999)],
        ["RN"] = [(59_000_000, 59_999_999)],
        ["CE"] = [(60_000_000, 63_999_999)],
        ["PI"] = [(64_000_000, 64_999_999)],
        ["MA"] = [(65_000_000, 65_999_999)],
        ["PA"] = [(66_000_000, 68_899_999)],
        ["AP"] = [(68_900_000, 68_999_999)],
        ["AM"] = [(69_000_000, 69_299_999), (69_400_000, 69_899_999)],
        ["RR"] = [(69_300_000, 69_399_999)],
        ["AC"] = [(69_900_000, 69_999_999)],
        ["DF"] = [(70_000_000, 72_799_999), (73_000_000, 73_699_999)],
        ["GO"] = [(72_800_000, 72_999_999), (73_700_000, 76_799_999)],
        ["RO"] = [(76_800_000, 76_999_999)],
        ["TO"] = [(77_000_000, 77_999_999)],
        ["MT"] = [(78_000_000, 78_899_999)],
        ["MS"] = [(79_000_000, 79_999_999)],
        ["PR"] = [(80_000_000, 87_999_999)],
        ["SC"] = [(88_000_000, 89_999_999)],
        ["RS"] = [(90_000_000, 99_999_999)],
    };

    // Faixa antiga de RO, fora da tabela atual dos Correios: aceita como entrada, mas nunca escolhida para a saída.
    private static readonly (int Inicio, int Fim) _faixaAntigaDeRo = (78_900_000, 78_999_999);

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> Malformados =>
    [
        "123456789",
        "1234-678",
        "12345 678",
        "1234567a",
        "12345-67",
        "12.3456-78",
        "12345_678",
        "12-345-678",
        "12/345-678",
        " 12345678",
        "12345678\t",
        "١٢٣٤٥٦٧٨",
    ];

    public static TheoryData<int> ForaDasFaixas => [0, 1, 999_999];

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void CEP_sai_sempre_na_faixa_da_UF_de_entrada()
    {
        using var cep = Criar();
        var sorteio = new Random(1720);
        foreach (var (uf, faixas) in _faixasPorUf)
        {
            for (var vez = 0; vez < 300; vez++)
            {
                var entrada = Sortear(sorteio, faixas);
                var entidade = Digitos(sorteio, 11);

                Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(Oito(entrada), entidade, out var saida));
                Assert.True(UfDe(int.Parse(saida!, CultureInfo.InvariantCulture)) == uf, "O CEP saiu de outra UF.");

                cep.Pseudonimizar(Mascarar(Oito(entrada)), entidade, out var comMascara);
                cep.Pseudonimizar((long)entrada, entidade, out var numero);
                Assert.True(comMascara == Mascarar(saida!) && numero == int.Parse(saida!, CultureInfo.InvariantCulture), "Máscara ou coluna numérica mudou o CEP.");
            }
        }
    }

    [Fact]
    public void A_mesma_entidade_na_mesma_UF_da_o_mesmo_CEP_e_o_CEP_de_entrada_so_decide_a_UF()
    {
        using var cep = Criar();
        var sorteio = new Random(1721);
        for (var vez = 0; vez < 1_000; vez++)
        {
            var entidade = Digitos(sorteio, 11);
            var (uf, faixas) = _faixasPorUf.ElementAt(sorteio.Next(_faixasPorUf.Count));

            cep.Pseudonimizar(Oito(Sortear(sorteio, faixas)), entidade, out var primeiro);
            cep.Pseudonimizar(Oito(Sortear(sorteio, faixas)), entidade, out var segundo);

            Assert.True(primeiro == segundo, "Dois CEPs da mesma UF e da mesma entidade deram saídas diferentes.");
            Assert.True(UfDe(int.Parse(primeiro!, CultureInfo.InvariantCulture)) == uf, "O CEP saiu de outra UF.");
        }
    }

    [Fact]
    public void Entidades_diferentes_se_espalham_pela_UF()
    {
        using var cep = Criar();
        var sorteio = new Random(1722);
        var saidas = new HashSet<string>();
        for (var vez = 0; vez < 10_000; vez++)
        {
            cep.Pseudonimizar(Oito(Sortear(sorteio, _faixasPorUf["SP"])), Digitos(sorteio, 11), out var saida);
            saidas.Add(saida!);
        }

        // 10 mil escolhas em 19 milhões de CEPs: colisões esperadas por volta de 3.
        Assert.True(saidas.Count > 9_980, "As entidades não estão se espalhando pela faixa.");
    }

    [Fact]
    public void CEP_e_o_HMAC_da_entidade_nas_faixas_da_UF()
    {
        // Recalcula de forma independente: K_semente de br.cep em cep@1, HMAC-SHA256 de UF ‖ 0x00 ‖ entidade sem os espaços
        // das pontas em UTF-8, 8 primeiros bytes big-endian, módulo a quantidade de CEPs da UF, contados na ordem das faixas.
        using var cep = Criar();
        var chaveDeSemente = new byte[32];
        Subchaves.DerivarSemente(_chave, "br.cep", "cep", 1, "homologacao", chaveDeSemente);
        var sorteio = new Random(1723);
        for (var vez = 0; vez < 3_000; vez++)
        {
            var (uf, faixas) = _faixasPorUf.ElementAt(sorteio.Next(_faixasPorUf.Count));
            var entidade = vez % 5 == 0 ? "Entidade " + Digitos(sorteio, 6) + " ç  " : Digitos(sorteio, 11);
            var mensagem = Encoding.ASCII.GetBytes(uf + "\0").Concat(Encoding.UTF8.GetBytes(entidade.Trim())).ToArray();
            var semente = BinaryPrimitives.ReadUInt64BigEndian(HMACSHA256.HashData(chaveDeSemente, mensagem));
            var indice = semente % (ulong)faixas.Sum(f => (long)f.Fim - f.Inicio + 1);
            var esperado = 0;
            foreach (var (inicio, fim) in faixas)
            {
                var tamanho = (ulong)(fim - inicio + 1);
                if (indice < tamanho)
                {
                    esperado = inicio + (int)indice;
                    break;
                }

                indice -= tamanho;
            }

            cep.Pseudonimizar(Oito(Sortear(sorteio, faixas)), entidade, out var saida);

            Assert.True(saida == Oito(esperado), "O CEP não é a escolha da entidade nas faixas da UF.");
        }
    }

    [Fact]
    public void Bordas_das_faixas_ficam_na_UF_certa()
    {
        using var cep = Criar();
        var entidade = Digitos(new Random(1724), 11);
        foreach (var (uf, faixas) in _faixasPorUf)
        {
            foreach (var (inicio, fim) in faixas)
            {
                foreach (var borda in new[] { inicio, fim })
                {
                    Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(Oito(borda), entidade, out var saida));
                    Assert.True(UfDe(int.Parse(saida!, CultureInfo.InvariantCulture)) == uf, "A borda de uma faixa caiu em outra UF.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ForaDasFaixas))]
    public void CEP_fora_das_faixas_segue_a_politica(int valor)
    {
        // Abaixo de 01000-000 não há UF.
        using var falhar = Criar();
        using var anular = Criar(PoliticaEntradaMalformada.Anular);
        var entidade = Digitos(new Random(1725), 11);

        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(Oito(valor), entidade, out _)).Codigo);
        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar((long)valor, entidade, out _)).Codigo);
        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(Oito(valor), entidade, out var anulado));
        Assert.Null(anulado);
    }

    [Fact]
    public void Faixa_antiga_de_RO_e_entrada_de_RO_e_sai_so_nas_faixas_atuais()
    {
        // A faixa antiga só decide a UF: o CEP sai nas faixas atuais de RO e é o mesmo que um CEP atual de RO daria
        // para a mesma entidade. As bordas e os vizinhos de MT e MS ficam nas UFs deles.
        using var cep = Criar();
        var sorteio = new Random(1738);
        var (inicio, fim) = _faixaAntigaDeRo;
        for (var vez = 0; vez < 1_000; vez++)
        {
            var entrada = vez switch { 0 => inicio, 1 => fim, _ => Sortear(sorteio, [_faixaAntigaDeRo]) };
            var entidade = Digitos(sorteio, 11);

            Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(Oito(entrada), entidade, out var antiga));
            cep.Pseudonimizar(Oito(Sortear(sorteio, _faixasPorUf["RO"])), entidade, out var atual);
            cep.Pseudonimizar(Mascarar(Oito(entrada)), entidade, out var comMascara);
            cep.Pseudonimizar((long)entrada, entidade, out var numero);

            Assert.True(antiga == atual, "A faixa antiga de RO deu outro CEP que a faixa atual.");
            Assert.True(UfDe(int.Parse(antiga!, CultureInfo.InvariantCulture)) == "RO", "A faixa antiga de RO saiu de outra UF.");
            Assert.True(comMascara == Mascarar(antiga!) && numero == int.Parse(antiga!, CultureInfo.InvariantCulture), "Máscara ou coluna numérica mudou o CEP.");
        }

        var vizinha = Digitos(sorteio, 11);
        cep.Pseudonimizar(Oito(inicio - 1), vizinha, out var mt);
        cep.Pseudonimizar(Oito(fim + 1), vizinha, out var ms);
        Assert.True(UfDe(int.Parse(mt!, CultureInfo.InvariantCulture)) == "MT" && UfDe(int.Parse(ms!, CultureInfo.InvariantCulture)) == "MS", "Um vizinho da faixa antiga de RO mudou de UF.");
    }

    [Fact]
    public void Texto_de_7_digitos_e_completado_e_o_zero_sai_do_preenchimento()
    {
        using var cep = Criar();
        var sorteio = new Random(1726);
        var entidade = Digitos(sorteio, 11);
        var entrada = Sortear(sorteio, [(1_000_000, 9_999_999)]);
        cep.Pseudonimizar(Oito(entrada), entidade, out var completo);

        Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(Oito(entrada)[1..], entidade, out var semZero));
        cep.Pseudonimizar(Oito(entrada)[1..] + "   ", entidade, out var comPreenchimento);
        cep.Pseudonimizar(Oito(entrada) + "   ", entidade, out var completoComPreenchimento);

        Assert.True(semZero == completo, "O CEP de 7 dígitos não deu o mesmo do completo.");
        Assert.True(comPreenchimento == completo + "  " && completoComPreenchimento == completo + "   ", "O preenchimento não foi tratado.");
    }

    [Fact]
    public void Mascara_com_ponto_e_preservada()
    {
        using var cep = Criar();
        var sorteio = new Random(1727);
        var entidade = Digitos(sorteio, 11);
        var entrada = Oito(Sortear(sorteio, _faixasPorUf["MG"]));
        cep.Pseudonimizar(entrada, entidade, out var saida);

        Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(entrada[..2] + "." + entrada[2..5] + "-" + entrada[5..], entidade, out var comPonto));

        Assert.True(comPonto == saida![..2] + "." + saida[2..5] + "-" + saida[5..], "A máscara com ponto não foi preservada.");
    }

    [Theory]
    [MemberData(nameof(Malformados))]
    public void Entrada_malformada_segue_a_politica(string entrada)
    {
        using var falhar = Criar();
        using var anular = Criar(PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(PoliticaEntradaMalformada.Preservar);
        var entidade = Digitos(new Random(1728), 11);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, entidade, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(entrada.Trim(), excecao.Message, StringComparison.Ordinal);

        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, entidade, out var anulado));
        Assert.Null(anulado);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(entrada, entidade, out var copiado));
        Assert.Equal(entrada, copiado);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(100_000_000L)]
    [InlineData(4_296_277_396L)]
    [InlineData(-4_293_657_196L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void CEP_numerico_fora_de_8_digitos_segue_a_politica(long numero)
    {
        using var falhar = Criar();
        using var preservar = Criar(PoliticaEntradaMalformada.Preservar);
        var entidade = Digitos(new Random(1729), 11);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(numero, entidade, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(numero.ToString(CultureInfo.InvariantCulture), excecao.Message, StringComparison.Ordinal);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(numero, entidade, out var copiado));
        Assert.Equal(numero, copiado);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("          ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            using var cep = Criar(malformada);

            Assert.Equal(Desfecho.Vazio, cep.Pseudonimizar(entrada, "entidade", out var saida));
            Assert.Equal(entrada, saida);
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_CEP_e_ambiente_dominio_ou_versao_diferente_nao()
    {
        var sorteio = new Random(1730);
        var entrada = Oito(Sortear(sorteio, _faixasPorUf["SP"]));
        var entidade = Digitos(sorteio, 11);
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "cep-entrega");
        using var outraVersao = Criar(versao: 2);

        primeiro.Pseudonimizar(entrada, entidade, out var a);
        segundo.Pseudonimizar(entrada, entidade, out var b);
        outroAmbiente.Pseudonimizar(entrada, entidade, out var c);
        outroDominio.Pseudonimizar(entrada, entidade, out var d);
        outraVersao.Pseudonimizar(entrada, entidade, out var e);

        Assert.True(a == b, "A mesma configuração deu CEPs diferentes.");
        Assert.True(a != c && a != d && a != e, "Ambiente, domínio ou versão diferente deu o mesmo CEP.");
    }

    [Fact]
    public void Entidade_longa_e_usada_inteira()
    {
        // Mais de 256 bytes em UTF-8: a escolha não pode depender só do começo.
        using var cep = Criar();
        var sorteio = new Random(1731);
        var comeco = new string('x', 300);
        var entrada = Oito(Sortear(sorteio, _faixasPorUf["RS"]));

        cep.Pseudonimizar(entrada, comeco + "a", out var umaEntidade);
        cep.Pseudonimizar(entrada, comeco + "b", out var outraEntidade);

        Assert.True(umaEntidade != outraEntidade, "Entidades que diferem no fim deram o mesmo CEP.");
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var cep = Criar();
        var sorteio = new Random(1732);
        var entradas = Enumerable.Range(0, 100_000)
            .Select(_ => (Cep: Oito(Sortear(sorteio, _faixasPorUf.ElementAt(sorteio.Next(_faixasPorUf.Count)).Value)), Entidade: Digitos(sorteio, 11)))
            .ToArray();
        var sequencial = entradas.Select(e => { cep.Pseudonimizar(e.Cep, e.Entidade, out var saida); return saida; }).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => cep.Pseudonimizar(entradas[i].Cep, entradas[i].Entidade, out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum CEP.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var cep = Criar(PoliticaEntradaMalformada.Preservar);
        cep.Dispose();
        cep.Dispose();

        var valido = Sortear(new Random(1733), _faixasPorUf["SP"]);
        foreach (var entrada in new[] { Oito(valido), "malformado", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => cep.Pseudonimizar(entrada, "entidade", out _));
        }

        Assert.Throws<ObjectDisposedException>(() => cep.Pseudonimizar((long)valido, "entidade", out _));
    }

    [Fact]
    public void Descarte_zera_a_chave_de_semente()
    {
        var cep = Criar();
        var hmac = typeof(PseudonimizadorCep).GetField("_hmac", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cep)!;
        var chaveDeSemente = (byte[])typeof(HmacDeSemente).GetField("_chave", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hmac)!;

        cep.Dispose();

        Assert.True(Array.TrueForAll(chaveDeSemente, b => b == 0), "O descarte não zerou a chave de semente.");
    }

    [Fact]
    public void Politica_fora_das_definidas_e_entrada_ou_entidade_nula_sao_recusadas()
    {
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar((PoliticaEntradaMalformada)7));
        using var cep = Criar();
        var valido = Sortear(new Random(1734), _faixasPorUf["SP"]);
        Assert.Throws<ArgumentNullException>("entrada", () => cep.Pseudonimizar(null!, "entidade", out _));
        Assert.Throws<ArgumentNullException>("entidade", () => cep.Pseudonimizar(Oito(valido), null!, out _));
        Assert.Throws<ArgumentNullException>("entidade", () => cep.Pseudonimizar((long)valido, null!, out _));
    }

    [Theory]
    [InlineData("entidade-270710", 69_000_000, 69_400_000)]
    [InlineData("entidade-1361366", 70_000_000, 73_000_000)]
    [InlineData("entidade-7765325", 72_800_000, 73_700_000)]
    public void Escolha_na_borda_entre_as_faixas_de_uma_UF_fica_na_UF(string entidade, int primeiraFaixa, int esperado)
    {
        // Entidades sintéticas que, com a chave do vetor, caem exatamente no primeiro CEP da segunda faixa de AM, DF e GO
        // (índice igual ao tamanho da primeira faixa): um erro de uma posição sairia no CEP seguinte, de outra UF.
        using var cep = Criar();

        cep.Pseudonimizar(Oito(primeiraFaixa), entidade, out var saida);

        Assert.True(saida == Oito(esperado), "A escolha na borda entre as faixas saiu da UF.");
    }

    [Fact]
    public void Espacos_das_pontas_da_entidade_nao_mudam_o_CEP_e_entidade_vazia_da_um_CEP_fixo_por_UF()
    {
        using var cep = Criar();
        var sorteio = new Random(1735);
        var entidade = Digitos(sorteio, 11);
        var entrada = Oito(Sortear(sorteio, _faixasPorUf["PR"]));
        cep.Pseudonimizar(entrada, entidade, out var semEspacos);

        cep.Pseudonimizar(entrada, entidade + "   ", out var comPreenchimento);
        cep.Pseudonimizar(entrada, "  " + entidade, out var comEspacoAEsquerda);
        Assert.True(comPreenchimento == semEspacos && comEspacoAEsquerda == semEspacos, "Espaços na entidade mudaram o CEP.");

        cep.Pseudonimizar(entrada, string.Empty, out var vazia);
        cep.Pseudonimizar(Oito(Sortear(sorteio, _faixasPorUf["PR"])), "   ", out var emBranco);
        Assert.True(vazia == emBranco && UfDe(int.Parse(vazia!, CultureInfo.InvariantCulture)) == "PR", "Entidade vazia não deu um CEP fixo na UF.");
    }

    [Fact]
    public void A_mesma_entidade_em_UFs_de_mesmo_tamanho_nao_repete_os_digitos_finais()
    {
        // MG e RS têm 10⁷ CEPs cada: com a mesma semente nas duas, os 7 últimos dígitos seriam sempre iguais.
        using var cep = Criar();
        var sorteio = new Random(1736);
        var iguais = 0;
        for (var vez = 0; vez < 1_000; vez++)
        {
            var entidade = Digitos(sorteio, 11);
            cep.Pseudonimizar(Oito(Sortear(sorteio, _faixasPorUf["MG"])), entidade, out var mg);
            cep.Pseudonimizar(Oito(Sortear(sorteio, _faixasPorUf["RS"])), entidade, out var rs);
            iguais += mg![1..] == rs![1..] ? 1 : 0;
        }

        Assert.True(iguais < 5, "A mesma entidade repete os dígitos finais entre UFs.");
    }

    [Fact]
    public void Mascara_e_preenchimento_juntos_sao_preservados()
    {
        using var cep = Criar();
        var sorteio = new Random(1737);
        var entidade = Digitos(sorteio, 11);
        var entrada = Mascarar(Oito(Sortear(sorteio, _faixasPorUf["BA"])));
        cep.Pseudonimizar(entrada, entidade, out var semEspacos);

        Assert.Equal(Desfecho.Pseudonimizado, cep.Pseudonimizar(entrada + "   ", entidade, out var comEspacos));

        Assert.True(comEspacos == semEspacos + "   ", "Máscara com preenchimento perdeu os espaços.");
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorCep Criar(
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "cep",
        int versao = 1) =>
        new(_chave, dominio, versao, ambiente, malformada);

    private static int Sortear(Random sorteio, (int Inicio, int Fim)[] faixas)
    {
        var (inicio, fim) = faixas[sorteio.Next(faixas.Length)];
        return inicio + sorteio.Next(fim - inicio + 1);
    }

    private static string? UfDe(int cep) =>
        _faixasPorUf.FirstOrDefault(par => par.Value.Any(f => cep >= f.Inicio && cep <= f.Fim)).Key;

    private static string Oito(int cep) => cep.ToString("00000000", CultureInfo.InvariantCulture);

    private static string Mascarar(string cep) => cep[..5] + "-" + cep[5..];
}
