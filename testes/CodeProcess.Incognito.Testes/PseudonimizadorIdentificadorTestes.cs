using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Identificador (<c>id.permutar</c>): bijeção exaustiva sobre [1, N) em N = 10⁴ e 10⁵, saída sempre em [1, N), zero e
/// negativos como pontos fixos, identificador a partir de N pela política de entrada malformada e recálculo
/// independente em todo N aceito. Os identificadores são sorteados na hora; a chave é a pública do vetor de derivação,
/// para uma falha ser reproduzível; as asserções não imprimem valores.
/// </summary>
public sealed class PseudonimizadorIdentificadorTestes : IDisposable
{
    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<int> DigitosAceitos => new(Enumerable.Range(4, 15));

    // N, a = 10^⌈d/2⌉, b = 10^⌊d/2⌋ e o ajuste em decimal ASCII, escritos por extenso.
    public static TheoryData<long, long, long, string> LadosEAjustes => new()
    {
        { 10_000, 100, 100, "10000" },
        { 100_000, 1_000, 100, "100000" },
        { 1_000_000, 1_000, 1_000, "1000000" },
        { 10_000_000, 10_000, 1_000, "10000000" },
        { 100_000_000, 10_000, 10_000, "100000000" },
        { 1_000_000_000, 100_000, 10_000, "1000000000" },
        { 10_000_000_000, 100_000, 100_000, "10000000000" },
        { 100_000_000_000, 1_000_000, 100_000, "100000000000" },
        { 1_000_000_000_000, 1_000_000, 1_000_000, "1000000000000" },
        { 10_000_000_000_000, 10_000_000, 1_000_000, "10000000000000" },
        { 100_000_000_000_000, 10_000_000, 10_000_000, "100000000000000" },
        { 1_000_000_000_000_000, 100_000_000, 10_000_000, "1000000000000000" },
        { 10_000_000_000_000_000, 100_000_000, 100_000_000, "10000000000000000" },
        { 100_000_000_000_000_000, 1_000_000_000, 100_000_000, "100000000000000000" },
        { 1_000_000_000_000_000_000, 1_000_000_000, 1_000_000_000, "1000000000000000000" },
    };

    public static TheoryData<long> TamanhosRecusados =>
    [
        long.MinValue,
        -10_000,
        0,
        1,
        10,
        1_000,
        9_999,
        10_001,
        20_000,
        99_999,
        123_456,
        999_999_999_999_999_999,
        1_000_000_000_000_000_001,
        long.MaxValue,
    ];

    // As políticas são internas e entram como número: o método de teste é público.
    public static TheoryData<int> PoliticasDeMalformado => new(Enum.GetValues<PoliticaEntradaMalformada>().Select(politica => (int)politica));

    public void Dispose() => _chave.Dispose();

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Bijecao_exaustiva_sobre_1_ate_N(int digitos)
    {
        // Sob a mutação, os dois casos ficam em 10⁴, o menor N aceito.
        var tamanho = Potencia(Mutacao.Tamanho(digitos, 4));
        using var id = Criar(tamanho);
        var saidas = new HashSet<long>();
        for (long entrada = 1; entrada < tamanho; entrada++)
        {
            Assert.Equal(Desfecho.Pseudonimizado, id.Pseudonimizar(entrada, out var saida));
            Assert.True(saida is >= 1 && saida < tamanho, "Saída fora de [1, N).");
            saidas.Add(saida!.Value);
        }

        Assert.Equal(tamanho - 1, saidas.Count);
    }

    [Theory]
    [MemberData(nameof(LadosEAjustes))]
    public void Pseudonimo_e_a_permutacao_id_com_N_em_ASCII_como_ajuste_e_sem_o_zero(long tamanho, long a, long b, string ajusteEsperado)
    {
        // Recalcula de forma independente, com a, b e o ajuste escritos na tabela: chave de domínio pedido-id@1 na
        // variante id; o zero fica fora da saída.
        using var id = Criar(tamanho);
        using var feistel = Feistel(a, b);
        var ajuste = Encoding.ASCII.GetBytes(ajusteEsperado);
        var zero = ConjuntoExcluido.DeValores(0);
        var sorteio = new Random(25_000 + ajusteEsperado.Length);
        foreach (var entrada in Amostra(sorteio, tamanho, 2_000))
        {
            var esperado = (long)feistel.Permutar((ulong)entrada, ajuste, zero);

            Assert.Equal(Desfecho.Pseudonimizado, id.Pseudonimizar(entrada, out var saida));
            Assert.True(saida == esperado, "O pseudônimo não é a permutação id com N como ajuste.");
            Assert.True(saida is >= 1 && saida < tamanho, "Saída fora de [1, N).");
        }
    }

    [Theory]
    [MemberData(nameof(LadosEAjustes))]
    public void Identificador_que_a_permutacao_levaria_ao_zero_segue_para_o_proximo_do_ciclo(long tamanho, long a, long b, string ajusteEsperado)
    {
        // Só um identificador por N cai no zero: sem a exclusão, ele sairia 0. A amostra sorteada quase nunca o pega.
        using var id = Criar(tamanho);
        using var feistel = Feistel(a, b);
        var ajuste = Encoding.ASCII.GetBytes(ajusteEsperado);
        var levadoAoZero = (long)feistel.Inverter(0, ajuste);

        Assert.Equal(Desfecho.Pseudonimizado, id.Pseudonimizar(levadoAoZero, out var saida));
        Assert.True(saida == (long)feistel.Permutar(0, ajuste), "O identificador levado ao zero não seguiu para o próximo do ciclo.");
        Assert.True(saida is >= 1 && saida < tamanho, "Saída fora de [1, N).");
    }

    [Theory]
    [MemberData(nameof(DigitosAceitos))]
    public void Tamanho_e_o_N_recebido(int digitos)
    {
        using var id = Criar(Potencia(digitos));

        Assert.Equal(Potencia(digitos), id.Tamanho);
    }

    [Theory]
    [MemberData(nameof(PoliticasDeMalformado))]
    public void Zero_e_negativos_voltam_como_vieram_em_toda_politica(int politica)
    {
        using var id = Criar(10_000, (PoliticaEntradaMalformada)politica);
        var sorteio = new Random(25_300);
        var entradas = new List<long> { 0, -1, -9_999, -10_000, long.MinValue, long.MinValue + 1 };
        entradas.AddRange(Enumerable.Range(0, 100).Select(_ => -1 - sorteio.NextInt64(long.MaxValue)));
        foreach (var entrada in entradas)
        {
            Assert.Equal(Desfecho.PontoFixo, id.Pseudonimizar(entrada, out var saida));
            Assert.True(saida == entrada, "Zero ou negativo mudou.");
        }
    }

    [Theory]
    [MemberData(nameof(DigitosAceitos))]
    public void Identificador_a_partir_de_N_segue_a_politica(int digitos)
    {
        var tamanho = Potencia(digitos);
        using var falhar = Criar(tamanho);
        using var anular = Criar(tamanho, PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(tamanho, PoliticaEntradaMalformada.Preservar);
        var sorteio = new Random(25_100 + digitos);
        var entradas = new[] { tamanho, tamanho + 1, long.MaxValue, tamanho + sorteio.NextInt64(long.MaxValue - tamanho) };
        var mensagens = new HashSet<string>();
        foreach (var entrada in entradas)
        {
            var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _));
            Assert.Equal("INC1003", excecao.Codigo);
            Assert.Null(excecao.InnerException);
            Assert.DoesNotContain(entrada.ToString(CultureInfo.InvariantCulture), excecao.Message, StringComparison.Ordinal);
            mensagens.Add(excecao.Message);

            Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out var anulado));
            Assert.Null(anulado);
            Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(entrada, out var copiado));
            Assert.True(copiado == entrada, "Com preservar, o identificador a partir de N não foi copiado como veio.");
        }

        // A mensagem é a mesma para qualquer valor: não carrega nada dele, em formato nenhum.
        Assert.Single(mensagens);
    }

    [Fact]
    public void Com_preservar_a_copia_em_claro_nao_colide_com_nenhum_pseudonimo()
    {
        const long Tamanho = 10_000;
        using var preservar = Criar(Tamanho, PoliticaEntradaMalformada.Preservar);
        var sorteio = new Random(25_200);
        var entradas = Enumerable.Range(1, (int)Tamanho - 1).Select(valor => (long)valor)
            .Concat(Enumerable.Range(0, 10_000).Select(_ => Tamanho + sorteio.NextInt64(long.MaxValue - Tamanho)))
            .Concat([0L, -1L, long.MinValue])
            .ToHashSet();

        var saidas = entradas.Select(entrada => { preservar.Pseudonimizar(entrada, out var saida); return saida!.Value; }).ToHashSet();

        Assert.Equal(entradas.Count, saidas.Count);
    }

    [Theory]
    [MemberData(nameof(DigitosAceitos))]
    public void Extremos_do_dominio_sao_permutados(int digitos)
    {
        var tamanho = Potencia(digitos);
        using var id = Criar(tamanho);
        foreach (var entrada in new[] { 1L, 2L, tamanho / 2, tamanho - 2, tamanho - 1 })
        {
            Assert.Equal(Desfecho.Pseudonimizado, id.Pseudonimizar(entrada, out var saida));
            Assert.True(saida is >= 1 && saida < tamanho, "Saída fora de [1, N) num extremo do domínio.");
        }
    }

    [Theory]
    [MemberData(nameof(TamanhosRecusados))]
    public void N_que_nao_e_potencia_de_10_entre_10_4_e_10_18_e_recusado(long n)
    {
        Assert.Throws<ArgumentOutOfRangeException>("tamanho", () => Criar(n));
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_versao_ou_N_diferente_nao()
    {
        using var primeiro = Criar(10_000);
        using var segundo = Criar(10_000);
        using var outroAmbiente = Criar(10_000, ambiente: "desenvolvimento");
        using var outroDominio = Criar(10_000, dominio: "cliente-id");
        using var outraVersao = Criar(10_000, versao: 2);
        using var outroTamanho = Criar(100_000);
        var sorteio = new Random(25_400);
        var iguais = new int[4];
        foreach (var entrada in Amostra(sorteio, 10_000, 1_000))
        {
            primeiro.Pseudonimizar(entrada, out var a);
            segundo.Pseudonimizar(entrada, out var b);
            outroAmbiente.Pseudonimizar(entrada, out var c);
            outroDominio.Pseudonimizar(entrada, out var d);
            outraVersao.Pseudonimizar(entrada, out var e);
            outroTamanho.Pseudonimizar(entrada, out var f);

            Assert.True(a == b, "A mesma configuração deu pseudônimos diferentes.");
            iguais[0] += a == c ? 1 : 0;
            iguais[1] += a == d ? 1 : 0;
            iguais[2] += a == e ? 1 : 0;
            iguais[3] += a == f ? 1 : 0;
        }

        // Permutações independentes coincidem em cerca de 1 valor a cada 10⁴: em 1.000, quase sempre nenhum.
        Assert.All(iguais, quantidade => Assert.True(quantidade <= 3, "Ambiente, domínio, versão ou N diferente deu o mesmo pseudônimo."));
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var id = Criar(1_000_000_000_000);
        var sorteio = new Random(25_500);
        var entradas = Amostra(sorteio, 1_000_000_000_000, 100_000).ToArray();
        var sequencial = entradas.Select(entrada => { id.Pseudonimizar(entrada, out var saida); return saida; }).ToArray();
        var paralelo = new long?[entradas.Length];

        Parallel.For(0, entradas.Length, i => id.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum pseudônimo.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var id = Criar(10_000, PoliticaEntradaMalformada.Preservar);
        id.Dispose();
        id.Dispose();

        foreach (var entrada in new[] { 1L, 9_999L, 0L, -1L, 10_000L })
        {
            Assert.Throws<ObjectDisposedException>(() => id.Pseudonimizar(entrada, out _));
        }
    }

    [Fact]
    public void Politica_fora_das_definidas_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar(10_000, (PoliticaEntradaMalformada)7));
    }

    // Valores distintos em [1, N), sorteados.
    private static HashSet<long> Amostra(Random sorteio, long tamanho, int quantidade)
    {
        var valores = new HashSet<long>();
        while (valores.Count < Math.Min(quantidade, tamanho - 1))
        {
            valores.Add(1 + sorteio.NextInt64(tamanho - 1));
        }

        return valores;
    }

    private static long Potencia(int expoente)
    {
        long valor = 1;
        for (var i = 0; i < expoente; i++)
        {
            valor *= 10;
        }

        return valor;
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorIdentificador Criar(
        long tamanho,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "pedido-id",
        int versao = 1) =>
        new(_chave, dominio, versao, ambiente, tamanho, malformada);

    private PermutacaoFeistel Feistel(long a, long b)
    {
        var chaveDeDominio = new byte[32];
        Subchaves.DerivarChaveDeDominio(_chave, "pedido-id", 1, "id", "homologacao", chaveDeDominio);
        return new PermutacaoFeistel(chaveDeDominio, 10, (ulong)a, (ulong)b);
    }
}
