using System.Globalization;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio CPF: bijeção sobre as bases, DV válido continua válido, DV inválido continua inválido com o mesmo
/// deslocamento, máscara e preenchimento preservados, e as políticas de entrada inválida e malformada. Todo CPF é
/// sorteado na hora; a chave é a pública do vetor de derivação, para uma falha ser reproduzível; as asserções não
/// imprimem valores.
/// </summary>
public sealed class PseudonimizadorCpfTestes : IDisposable
{
    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> Malformados =>
    [
        "12345678",
        "123456789012",
        "123.456.789.01",
        "123-456.789-01",
        "123.456-789-01",
        "123.456.789/01",
        "123456.789-01",
        "1234567890a",
        " 12345678901",
        "123 45678901",
        "١٢٣٤٥٦٧٨٩٠١",
        "12345678901\t",
    ];

    // As políticas são internas e entram como número: o método de teste é público.
    public static TheoryData<int, int> DigitosEPoliticas =>
        new(from digitos in Enumerable.Range(9, 2)
            from politica in Enum.GetValues<PoliticaEntradaMalformada>()
            select (digitos, (int)politica));

    public static TheoryData<int, int> RepdigitosEPoliticas =>
        new(from digito in Enumerable.Range(0, 10)
            from politica in Enum.GetValues<PoliticaEntradaInvalida>()
            select (digito, (int)politica));

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Um_milhao_de_CPFs_diferentes_dao_um_milhao_de_pseudonimos_diferentes()
    {
        using var cpf = Criar();
        var sorteio = new Random(1401);
        var bases = new HashSet<long>();
        while (bases.Count < 1_000_000)
        {
            var baseCpf = sorteio.NextInt64(1_000_000_000);
            if (!EhRepdigito(baseCpf))
            {
                bases.Add(baseCpf);
            }
        }

        var saidas = new HashSet<long>(bases.Count);
        foreach (var baseCpf in bases)
        {
            cpf.Pseudonimizar(ComDvCorreto(baseCpf), out var saida);
            saidas.Add(saida!.Value);
        }

        Assert.Equal(bases.Count, saidas.Count);
    }

    [Fact]
    public void CPF_valido_continua_valido_e_com_a_mesma_mascara()
    {
        using var cpf = Criar();
        var sorteio = new Random(1402);
        for (var vez = 0; vez < 5_000; vez++)
        {
            var entrada = CpfValido(sorteio);

            Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(entrada, out var saida));
            Assert.True(saida!.Length == 11 && ValidadorBr.Cpf(saida), "CPF válido saiu inválido ou fora do formato.");

            Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(MascararCpf(entrada), out var comMascara));
            Assert.True(comMascara == MascararCpf(saida), "A máscara não foi preservada ou o pseudônimo mudou com ela.");
        }
    }

    [Fact]
    public void CPF_com_DV_invalido_continua_invalido_com_o_mesmo_deslocamento_em_texto_mascara_e_numero()
    {
        using var cpf = Criar();
        var sorteio = new Random(1403);
        for (var vez = 0; vez < 5_000; vez++)
        {
            var valido = CpfValido(sorteio);
            var deslocamento = 1 + sorteio.Next(99);
            var entrada = DeslocarDv(valido, deslocamento);

            Assert.Equal(Desfecho.DvInvalidoPreservado, cpf.Pseudonimizar(entrada, out var saida));
            cpf.Pseudonimizar(valido, out var saidaDoValido);

            Assert.False(ValidadorBr.Cpf(saida), "CPF com DV inválido saiu válido.");
            Assert.True(saida![..9] == saidaDoValido![..9], "A base do pseudônimo depende do DV da entrada.");
            var dvSaida = int.Parse(saida[9..], CultureInfo.InvariantCulture);
            var dvCorretoDaSaida = int.Parse(saidaDoValido[9..], CultureInfo.InvariantCulture);
            Assert.True((dvSaida - dvCorretoDaSaida + 100) % 100 == deslocamento, "O deslocamento do DV não foi preservado.");

            Assert.Equal(Desfecho.DvInvalidoPreservado, cpf.Pseudonimizar(MascararCpf(entrada), out var comMascara));
            Assert.True(comMascara == MascararCpf(saida), "Com máscara, o DV inválido não teve o mesmo tratamento.");

            Assert.Equal(Desfecho.DvInvalidoPreservado, cpf.Pseudonimizar(long.Parse(entrada, CultureInfo.InvariantCulture), out var numero));
            Assert.True(Onze(numero!.Value) == saida, "Em coluna numérica, o DV inválido não teve o mesmo tratamento.");
        }
    }

    [Fact]
    public void Os_100_DVs_de_uma_base_dao_100_saidas_diferentes()
    {
        using var cpf = Criar();
        var baseCpf = CpfValido(new Random(1404))[..9];

        var saidas = Enumerable.Range(0, 100)
            .Select(dv => { cpf.Pseudonimizar(baseCpf + dv.ToString("00", CultureInfo.InvariantCulture), out var saida); return saida; })
            .ToHashSet();

        Assert.Equal(100, saidas.Count);
    }

    [Theory]
    [MemberData(nameof(DigitosEPoliticas))]
    public void CPF_em_texto_sem_os_zeros_a_esquerda_e_pseudonimizado_com_11_digitos_em_toda_politica(int digitos, int politica)
    {
        // A cópia em claro só é risco com preservar; as outras políticas provam que a forma não é tratada como malformada.
        using var cpf = Criar(malformada: (PoliticaEntradaMalformada)politica);
        var sorteio = new Random(1405 + digitos);
        for (var vez = 0; vez < 2_000; vez++)
        {
            // Base com 11 − dígitos zeros à esquerda e o primeiro dígito seguinte diferente de zero.
            var baseCpf = new string('0', 11 - digitos) + (char)('1' + sorteio.Next(9)) + Digitos(sorteio, digitos - 3);
            var completo = baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture);
            Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(completo[(11 - digitos)..], out var saida));
            cpf.Pseudonimizar(completo, out var saidaDoCompleto);

            Assert.True(saida == saidaDoCompleto, "O CPF sem zeros não deu o mesmo pseudônimo do completo.");
            Assert.True(saida!.Length == 11 && ValidadorBr.Cpf(saida), "O CPF sem zeros não saiu com 11 dígitos válidos.");
            Assert.True(saida != completo, "O CPF sem zeros foi copiado em claro.");

            var invalido = DeslocarDv(completo, 1 + sorteio.Next(99));
            Assert.Equal(Desfecho.DvInvalidoPreservado, cpf.Pseudonimizar(invalido[(11 - digitos)..], out var saidaInvalida));
            cpf.Pseudonimizar(invalido, out var saidaDoInvalidoCompleto);
            Assert.True(saidaInvalida == saidaDoInvalidoCompleto, "Sem zeros, o DV inválido não teve o mesmo tratamento.");
        }
    }

    [Fact]
    public void CPF_numerico_e_completado_com_zeros_e_bate_com_o_texto()
    {
        using var cpf = Criar();
        var sorteio = new Random(1407);
        for (var vez = 0; vez < 5_000; vez++)
        {
            // Bases pequenas de propósito: CPFs que começam com zeros, que a coluna numérica guarda com menos dígitos.
            var baseCpf = vez % 2 == 0 ? sorteio.NextInt64(1_000_000) : sorteio.NextInt64(1_000_000_000);
            if (EhRepdigito(baseCpf))
            {
                continue;
            }

            var numero = ComDvCorreto(baseCpf);
            Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(numero, out var saida));
            cpf.Pseudonimizar(Onze(numero), out var saidaTexto);

            Assert.True(Onze(saida!.Value) == saidaTexto, "Numérico e texto deram pseudônimos diferentes.");
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(100_000_000_000L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void CPF_numerico_fora_de_11_digitos_segue_a_politica_de_malformado(long numero)
    {
        using var falhar = Criar();
        using var anular = Criar(malformada: PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(malformada: PoliticaEntradaMalformada.Preservar);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(numero, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(numero.ToString(CultureInfo.InvariantCulture), excecao.Message, StringComparison.Ordinal);

        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(numero, out var anulado));
        Assert.Null(anulado);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(numero, out var copiado));
        Assert.Equal(numero, copiado);
    }

    [Fact]
    public void Espacos_a_direita_sao_preservados()
    {
        using var cpf = Criar();
        var entrada = CpfValido(new Random(1408));
        cpf.Pseudonimizar(entrada, out var semEspacos);

        Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(entrada + "   ", out var comEspacos));
        Assert.True(comEspacos == semEspacos + "   ", "O preenchimento à direita não foi preservado.");

        Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(MascararCpf(entrada) + " ", out var mascaraComEspaco));
        Assert.True(mascaraComEspaco == MascararCpf(semEspacos!) + " ", "O preenchimento da máscara não foi preservado.");
    }

    [Theory]
    [InlineData(9, 2, 0)]
    [InlineData(9, 5, 3)]
    [InlineData(9, 1, 0)]
    [InlineData(10, 1, 0)]
    [InlineData(10, 4, 3)]
    [InlineData(10, 0, 0)]
    public void Zeros_acrescentados_saem_do_preenchimento_sem_alargar_o_valor(int digitos, int espacos, int espacosNaSaida)
    {
        // char(11) com CPF de 9 dígitos chega com 2 espaços: os 2 zeros ocupam o lugar deles.
        using var cpf = Criar();
        var baseCpf = new string('0', 11 - digitos) + '7' + Digitos(new Random(1413 + digitos), digitos - 3);
        var completo = baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture);
        cpf.Pseudonimizar(completo, out var saidaDoCompleto);

        Assert.Equal(Desfecho.Pseudonimizado, cpf.Pseudonimizar(completo[(11 - digitos)..] + new string(' ', espacos), out var saida));

        Assert.True(saida == saidaDoCompleto + new string(' ', espacosNaSaida), "Os zeros acrescentados não saíram do preenchimento.");
    }

    [Theory]
    [MemberData(nameof(Malformados))]
    public void Entrada_malformada_segue_a_politica(string entrada)
    {
        using var falhar = Criar();
        using var anular = Criar(malformada: PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(malformada: PoliticaEntradaMalformada.Preservar);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(entrada.Trim(), excecao.Message, StringComparison.Ordinal);

        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out var anulado));
        Assert.Null(anulado);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(entrada, out var copiado));
        Assert.Equal(entrada, copiado);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("           ")]
    [InlineData("              ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            foreach (var invalida in Enum.GetValues<PoliticaEntradaInvalida>())
            {
                using var cpf = Criar(invalida, malformada);

                Assert.Equal(Desfecho.Vazio, cpf.Pseudonimizar(entrada, out var saida));
                Assert.Equal(entrada, saida);
            }
        }
    }

    [Fact]
    public void DV_invalido_segue_a_politica()
    {
        // Base com um zero à esquerda, para a forma de 10 dígitos também passar pela política de DV.
        var baseCpf = "0" + (char)('1' + new Random(1409).Next(9)) + Digitos(new Random(1409), 7);
        var invalido = DeslocarDv(baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture), 1);
        var numero = long.Parse(invalido, CultureInfo.InvariantCulture);
        using var falhar = Criar(invalida: PoliticaEntradaInvalida.Falhar);
        using var anular = Criar(invalida: PoliticaEntradaInvalida.Anular);

        foreach (var forma in new[] { invalido, MascararCpf(invalido), invalido[1..] })
        {
            var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(forma, out _));
            Assert.Equal("INC1004", excecao.Codigo);
            Assert.DoesNotContain(invalido[..9], excecao.Message, StringComparison.Ordinal);
            Assert.Equal(Desfecho.AnuladoDvInvalido, anular.Pseudonimizar(forma, out var anulado));
            Assert.Null(anulado);
        }

        var excecaoNumerica = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(numero, out _));
        Assert.Equal("INC1004", excecaoNumerica.Codigo);
        Assert.DoesNotContain(invalido[1..9], excecaoNumerica.Message, StringComparison.Ordinal);
        Assert.Equal(Desfecho.AnuladoDvInvalido, anular.Pseudonimizar(numero, out var numeroAnulado));
        Assert.Null(numeroAnulado);
    }

    [Theory]
    [MemberData(nameof(RepdigitosEPoliticas))]
    public void Base_repdigito_e_ponto_fixo_e_a_politica_de_DV_vem_antes(int digito, int politica)
    {
        var invalida = (PoliticaEntradaInvalida)politica;
        // DV certo pelo módulo 11: ponto fixo em toda política. DV errado: segue a política, e preservar o mantém.
        using var cpf = Criar(invalida);
        var baseCpf = new string((char)('0' + digito), 9);
        var comDvCerto = baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture);
        var comDvErrado = DeslocarDv(comDvCerto, 37);

        Assert.Equal(Desfecho.PontoFixo, cpf.Pseudonimizar(comDvCerto, out var saida));
        Assert.True(saida == comDvCerto, "Base repdígito com DV certo não ficou como ponto fixo.");
        Assert.Equal(Desfecho.PontoFixo, cpf.Pseudonimizar(long.Parse(comDvCerto, CultureInfo.InvariantCulture), out var numero));
        Assert.True(Onze(numero!.Value) == comDvCerto, "Base repdígito numérica com DV certo não ficou como ponto fixo.");

        foreach (var forma in new[] { comDvErrado, MascararCpf(comDvErrado) })
        {
            switch (invalida)
            {
                case PoliticaEntradaInvalida.Preservar:
                    Assert.Equal(Desfecho.PontoFixo, cpf.Pseudonimizar(forma, out var mantido));
                    Assert.True(mantido == forma, "Base repdígito com DV errado não ficou como veio.");
                    break;
                case PoliticaEntradaInvalida.Falhar:
                    Assert.Equal("INC1004", Assert.Throws<IncognitoException>(() => cpf.Pseudonimizar(forma, out _)).Codigo);
                    break;
                default:
                    Assert.Equal(Desfecho.AnuladoDvInvalido, cpf.Pseudonimizar(forma, out var anulado));
                    Assert.Null(anulado);
                    break;
            }
        }
    }

    [Fact]
    public void Pseudonimo_e_a_permutacao_do_dominio_cpf_na_variante_num_com_o_DV_recalculado()
    {
        // Recalcula de forma independente: chave de domínio cpf@1, variante num, 10⁴ × 10⁵ em base 10, sem ajuste,
        // repdígitos excluídos, DV pela referência clássica. Prende os parâmetros até os vetores fixos do domínio.
        using var cpf = Criar();
        using var crua = PermutacaoDoDominio();
        var repdigitos = ConjuntoExcluido.PorRegra(valor => valor % 111_111_111 == 0, 10);
        var sorteio = new Random(1412);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var entrada = CpfValido(sorteio);
            var baseEsperada = crua.Permutar(ulong.Parse(entrada[..9], CultureInfo.InvariantCulture), [], repdigitos)
                .ToString("000000000", CultureInfo.InvariantCulture);

            cpf.Pseudonimizar(entrada, out var saida);

            Assert.True(saida == baseEsperada + DvCpfClassico(baseEsperada).ToString("00", CultureInfo.InvariantCulture), "O pseudônimo não é a permutação do domínio.");
        }
    }

    [Fact]
    public void A_base_que_a_permutacao_levaria_a_um_repdigito_caminha_ate_a_proxima_base_da_orbita()
    {
        // A pré-imagem crua de cada repdígito precisa seguir a órbita da permutação sem exclusões até sair dos
        // repdígitos: amostras aleatórias quase nunca caem nesses 10 valores.
        using var cpf = Criar();
        using var crua = PermutacaoDoDominio();

        for (var digito = 0; digito <= 9; digito++)
        {
            var repdigito = (ulong)digito * 111_111_111;
            var preImagem = crua.Inverter(repdigito, []);
            if (preImagem % 111_111_111 == 0)
            {
                continue;
            }

            var esperada = repdigito;
            while (esperada % 111_111_111 == 0)
            {
                esperada = crua.Permutar(esperada, []);
            }

            cpf.Pseudonimizar(ComDvCorreto((long)preImagem), out var saida);

            Assert.True((ulong)(saida!.Value / 100) == esperada, "A pré-imagem de um repdígito não caminhou pela órbita.");
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_ou_versao_diferente_nao()
    {
        var entrada = CpfValido(new Random(1411));
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "cpf-cliente");
        using var outraVersao = Criar(versao: 2);

        primeiro.Pseudonimizar(entrada, out var a);
        segundo.Pseudonimizar(entrada, out var b);
        outroAmbiente.Pseudonimizar(entrada, out var c);
        outroDominio.Pseudonimizar(entrada, out var d);
        outraVersao.Pseudonimizar(entrada, out var e);

        Assert.True(a == b, "A mesma configuração deu pseudônimos diferentes.");
        Assert.True(a != c && a != d && a != e, "Ambiente, domínio ou versão diferente deu o mesmo pseudônimo.");
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var cpf = Criar();
        var sorteio = new Random(1414);
        var entradas = Enumerable.Range(0, 100_000).Select(_ => CpfValido(sorteio)).ToArray();
        var sequencial = entradas.Select(entrada => { cpf.Pseudonimizar(entrada, out var saida); return saida; }).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => cpf.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum pseudônimo.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var cpf = Criar(malformada: PoliticaEntradaMalformada.Preservar);
        var valido = CpfValido(new Random(1415));
        cpf.Dispose();
        cpf.Dispose();

        foreach (var entrada in new[] { valido, new string('1', 11), "malformado", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => cpf.Pseudonimizar(entrada, out _));
        }

        Assert.Throws<ObjectDisposedException>(() => cpf.Pseudonimizar(long.Parse(valido, CultureInfo.InvariantCulture), out _));
        Assert.Throws<ObjectDisposedException>(() => cpf.Pseudonimizar(-1L, out _));
    }

    [Fact]
    public void Politica_fora_das_definidas_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>("invalida", () => Criar(invalida: (PoliticaEntradaInvalida)7));
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar(malformada: (PoliticaEntradaMalformada)7));
    }

    [Fact]
    public void Entrada_nula_e_recusada()
    {
        using var cpf = Criar();

        Assert.Throws<ArgumentNullException>(() => cpf.Pseudonimizar((string)null!, out _));
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorCpf Criar(
        PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Preservar,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "cpf",
        int versao = 1) =>
        new(_chave, dominio, versao, ambiente, invalida, malformada);

    private PermutacaoFeistel PermutacaoDoDominio()
    {
        var chaveDeDominio = new byte[32];
        Subchaves.DerivarChaveDeDominio(_chave, "cpf", 1, Variantes.Numerico, "homologacao", chaveDeDominio);
        return new PermutacaoFeistel(chaveDeDominio, 10, 10_000, 100_000);
    }

    private static bool EhRepdigito(long baseCpf) => baseCpf % 111_111_111 == 0;

    private static long ComDvCorreto(long baseCpf)
    {
        var texto = baseCpf.ToString("000000000", CultureInfo.InvariantCulture);
        return (baseCpf * 100) + DvCpfClassico(texto);
    }

    private static string DeslocarDv(string cpf, int deslocamento) =>
        cpf[..9] + ((int.Parse(cpf[9..], CultureInfo.InvariantCulture) + deslocamento) % 100).ToString("00", CultureInfo.InvariantCulture);

    private static string Onze(long numero) => numero.ToString("00000000000", CultureInfo.InvariantCulture);
}
