using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio <c>dfe-numero</c>: recálculo independente de cada variante com os ajustes documentados, bijeção exaustiva do dia
/// em cada mês e do sequencial em 10⁴ e 10⁵, código nunca proibido nem igual ao número pseudonimizado, pontos fixos
/// contados, posições preservadas, inversa e separação por configuração. Emitentes, números e códigos são sorteados na
/// hora; a chave é a pública do vetor de derivação; as asserções não imprimem valores.
/// </summary>
public sealed class PseudonimizadorNumeroDfeTestes : IDisposable
{
    // 2024 e 2028 bissextos, 2025 não.
    private static readonly string[] _anos = ["24", "25", "28"];

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> ModelosForaDaNff => ["55", "65", "57", "58", "63", "64", "67"];

    public static TheoryData<string, int> AnosEMeses =>
        new(from ano in _anos from mes in Enumerable.Range(1, 12) select (ano, mes));

    // Cada caso parte de uma NFF da NF-e válida e invalida a data num ponto.
    public static TheoryData<string> DatasInvalidas =>
    [
        "mes do AAMM 00",
        "mes do AAMM 13",
        "mes do numero 00",
        "mes do numero 13",
        "dia 00",
        "dia 30 em fevereiro",
        "dia 31 em abril",
        "dia 36",
        "dia 99",
    ];

    public void Dispose() => _chave.Dispose();

    [Theory]
    [MemberData(nameof(ModelosForaDaNff))]
    public void Numero_e_codigo_fora_da_NFF_sao_as_permutacoes_nnf_e_cnf8_com_os_ajustes_documentados(string modelo)
    {
        // Recalcula de forma independente: nnf 10⁴ × 10⁵ com E = {0} e ajuste emitente' ‖ 0x00 ‖ mod ‖ série; cnf8
        // 10⁴ × 10⁴ com E = os 20 proibidos ∪ {número' se < 10⁸} e ajuste emitente' ‖ número'.
        using var numeroDfe = Criar();
        using var nnf = Feistel(Variantes.NumeroDfe, 10_000, 100_000);
        using var cnf8 = Feistel(Variantes.CodigoNumerico8, 10_000, 10_000);
        var sorteio = new Random(2701);
        for (var vez = 0; vez < 500; vez++)
        {
            var emitente = Emitente(sorteio);
            var serie = Serie(sorteio);
            var numero = 1 + sorteio.NextInt64(999_999_999);
            var codigo = CodigoPermitido(sorteio, numero);
            var chave = Montar(sorteio, "2601", emitente, modelo, serie, Nove(numero), '1', Oito(codigo));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            var numeroEsperado = (long)nnf.Permutar((ulong)numero, Ascii(emitente + "\0" + modelo + serie), ConjuntoExcluido.DeValores(0));
            var adicional = numeroEsperado < 100_000_000 ? (ulong)numeroEsperado : (ulong?)null;
            var codigoEsperado = (long)cnf8.Permutar((ulong)codigo, Ascii(emitente + Nove(numeroEsperado)), Proibidos(), adicional);
            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            Assert.True(saida[25..34] == Nove(numeroEsperado), "O número não é a permutação nnf com o ajuste esperado.");
            Assert.True(saida[35..43] == Oito(codigoEsperado), "O código não é a permutação cnf8 com o ajuste esperado.");
            Assert.True(saida[..25] == chave[..25] && saida[34..35] == chave[34..35] && saida[43] == chave[43], "Uma posição fora do número e do código mudou.");
        }
    }

    [Theory]
    [InlineData("62")]
    [InlineData("66")]
    public void Codigo_de_7_digitos_e_a_permutacao_cnf7_com_a_posicao_36_preservada(string modelo)
    {
        using var numeroDfe = Criar();
        using var nnf = Feistel(Variantes.NumeroDfe, 10_000, 100_000);
        using var cnf7 = Feistel(Variantes.CodigoNumerico7, 1_000, 10_000);
        var sorteio = new Random(2702);
        for (var vez = 0; vez < 500; vez++)
        {
            var emitente = CnpjValido(sorteio, alfanumerico: vez % 2 == 0);
            var serie = Serie(sorteio);
            var numero = 1 + sorteio.NextInt64(999_999_999);
            var codigo = Digitos(sorteio, 8);
            var chave = Montar(sorteio, "2601", emitente, modelo, serie, Nove(numero), '1', codigo);

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            var numeroEsperado = (long)nnf.Permutar((ulong)numero, Ascii(emitente + "\0" + modelo + serie), ConjuntoExcluido.DeValores(0));
            var codigoEsperado = (long)cnf7.Permutar(ulong.Parse(codigo[1..], CultureInfo.InvariantCulture), Ascii(emitente + Nove(numeroEsperado)), ConjuntoExcluido.Vazio);
            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            Assert.True(saida[35] == chave[35], "A posição 36 (nSiteAutoriz) mudou.");
            Assert.True(saida[36..43] == codigoEsperado.ToString("0000000", CultureInfo.InvariantCulture), "O código não é a permutação cnf7.");
        }
    }

    [Theory]
    [InlineData("55")]
    [InlineData("65")]
    [InlineData("57")]
    [InlineData("58")]
    public void Partes_da_NFF_sao_as_permutacoes_nff_dia_e_nff_seq_com_o_dia_permutado_no_ajuste_do_sequencial(string modelo)
    {
        // Dia: 6 × 6 com 24 rodadas, E = {0} ∪ os dias depois do último do mês; ajuste emitente' ‖ 0x00 ‖ AAMM ‖ mod ‖
        // série ‖ número com dia e sequencial em '0'. Sequencial: 10² × 10² (55/65) ou 10² × 10³ (57/58), E = {0}, mesmo
        // ajuste com o dia já permutado.
        var transporte = modelo is "57" or "58";
        using var numeroDfe = Criar();
        using var dia = Feistel(Variantes.DiaNff, 6, 6, rodadas: 24);
        using var sequencial = Feistel(Variantes.SequencialNff, 100, transporte ? 1_000UL : 100UL);
        var sorteio = new Random(2703);
        for (var vez = 0; vez < 500; vez++)
        {
            var emitente = EmitenteCpf(sorteio);
            var serie = "1" + "26";
            var diaEntrada = 1 + sorteio.Next(30);
            var sequencialEntrada = 1 + sorteio.Next(transporte ? 99_999 : 9_999);
            var numero = Numero(transporte, diaEntrada, 4, transporte ? null : '2', sequencialEntrada);
            var chave = Montar(sorteio, "2604", emitente, modelo, serie, numero, '3', Digitos(sorteio, 8));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            var indiceDoDia = transporte ? 2 : 0;
            var mascarado = numero.ToCharArray();
            Zerar(mascarado, transporte);
            mascarado[indiceDoDia] = mascarado[indiceDoDia + 1] = '0';
            var diaEsperado = (int)dia.Permutar((ulong)diaEntrada, Ascii(emitente + "\0" + "2604" + modelo + serie + new string(mascarado)), DiasExcluidos(30));
            var diaTexto = diaEsperado.ToString("00", CultureInfo.InvariantCulture);
            mascarado[indiceDoDia] = diaTexto[0];
            mascarado[indiceDoDia + 1] = diaTexto[1];
            var sequencialEsperado = (long)sequencial.Permutar((ulong)sequencialEntrada, Ascii(emitente + "\0" + "2604" + modelo + serie + new string(mascarado)), ConjuntoExcluido.DeValores(0));

            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            Assert.True(saida[25..34] == Numero(transporte, diaEsperado, 4, transporte ? null : '2', sequencialEsperado), "As partes da NFF não são as permutações esperadas.");
        }
    }

    [Theory]
    [MemberData(nameof(AnosEMeses))]
    public void Dia_da_NFF_e_bijecao_sobre_os_dias_do_mes_e_sempre_existe(string ano, int mes)
    {
        // Mesmo emitente, mesma série e mesmo sequencial: só o dia muda; a imagem dos dias válidos é exatamente eles.
        using var numeroDfe = Criar();
        var sorteio = new Random(2704 + mes);
        var emitente = EmitenteCpf(sorteio);
        var codigo = Digitos(sorteio, 8);
        var dias = DiasNoMes(mes, int.Parse(ano, CultureInfo.InvariantCulture));
        var aamm = ano + mes.ToString("00", CultureInfo.InvariantCulture);
        foreach (var (modelo, transporte) in new[] { ("55", false), ("58", true) })
        {
            var saidas = new HashSet<int>();
            for (var dia = 1; dia <= dias; dia++)
            {
                var chave = Montar(sorteio, aamm, emitente, modelo, "1" + ano, Numero(transporte, dia, mes, transporte ? null : '2', 7), '3', codigo);

                var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

                var diaSaida = int.Parse(transporte ? saida[27..29] : saida[25..27], CultureInfo.InvariantCulture);
                Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
                Assert.True(diaSaida >= 1 && diaSaida <= dias, "O dia permutado não existe no mês.");
                saidas.Add(diaSaida);
            }

            Assert.Equal(dias, saidas.Count);
        }
    }

    [Fact]
    public void Dia_vale_so_se_existe_nos_dois_meses()
    {
        // AAMM de março de 2024 e número de fevereiro: os dias 1 a 29 são permutados entre si; 30 e 31 ficam.
        using var numeroDfe = Criar();
        var sorteio = new Random(2705);
        var emitente = EmitenteCpf(sorteio);
        var saidas = new HashSet<int>();
        for (var dia = 1; dia <= 31; dia++)
        {
            var chave = Montar(sorteio, "2403", emitente, "55", "124", Numero(false, dia, 2, '2', 15), '3', Digitos(sorteio, 8));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            var diaSaida = int.Parse(saida[25..27], CultureInfo.InvariantCulture);
            if (dia <= 29)
            {
                Assert.True(diaSaida is >= 1 and <= 29, "O dia saiu fora dos que existem nos dois meses.");
                saidas.Add(diaSaida);
            }
            else
            {
                Assert.Equal(OcorrenciasChaveDfe.DataNffInvalida, ocorrencias);
                Assert.Equal(dia, diaSaida);
            }
        }

        Assert.Equal(29, saidas.Count);
    }

    [Theory]
    [MemberData(nameof(DatasInvalidas))]
    public void Data_invalida_da_NFF_deixa_o_dia_e_pseudonimiza_o_resto(string caso)
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2706);
        var (aamm, dia, mes) = caso switch
        {
            "mes do AAMM 00" => ("2600", 15, 6),
            "mes do AAMM 13" => ("2613", 15, 6),
            "mes do numero 00" => ("2606", 15, 0),
            "mes do numero 13" => ("2606", 15, 13),
            "dia 00" => ("2606", 0, 6),
            "dia 30 em fevereiro" => ("2402", 30, 2),
            "dia 31 em abril" => ("2604", 31, 4),
            "dia 36" => ("2606", 36, 6),
            "dia 99" => ("2606", 99, 6),
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
        for (var vez = 0; vez < 50; vez++)
        {
            var sequencial = 1 + sorteio.Next(9_999);
            var chave = Montar(sorteio, aamm, EmitenteCpf(sorteio), "55", "126", Numero(false, dia, mes, '2', sequencial), '3', Digitos(sorteio, 8));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.DataNffInvalida), "A data inválida não foi contada.");
            Assert.True(saida[25..30] == chave[25..30], "O dia, o mês ou o tipo mudou com a data inválida.");
            Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez a chave com data inválida.");
        }
    }

    [Theory]
    [InlineData("55", false)]
    [InlineData("57", true)]
    public void Sequencial_da_NFF_e_bijecao_exaustiva_sem_o_zero(string modelo, bool transporte)
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2707);
        var emitente = EmitenteCpf(sorteio);
        var codigo = Digitos(sorteio, 8);
        var tamanho = transporte ? 100_000 : 10_000;
        var vistos = new bool[tamanho];
        for (var sequencial = 1; sequencial < tamanho; sequencial++)
        {
            var chave = Montar(sorteio, "2606", emitente, modelo, "126", Numero(transporte, 0, 6, transporte ? null : '2', sequencial), '3', codigo);

            var saida = Pseudonimizar(numeroDfe, chave, out _);

            var sequencialSaida = int.Parse(transporte ? saida[29..34] : saida[30..34], CultureInfo.InvariantCulture);
            Assert.True(sequencialSaida != 0, "O sequencial saiu 0.");
            Assert.False(vistos[sequencialSaida], "Dois sequenciais deram a mesma saída.");
            vistos[sequencialSaida] = true;
        }

        var zero = Montar(sorteio, "2606", emitente, modelo, "126", Numero(transporte, 0, 6, transporte ? null : '2', 0), '3', codigo);
        var saidaDoZero = Pseudonimizar(numeroDfe, zero, out var ocorrencias);
        Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.SequencialZero), "O sequencial 0 não foi contado.");
        Assert.True(saidaDoZero[25..34] == zero[25..34], "O número com sequencial 0 e data inválida mudou.");
    }

    [Fact]
    public void O_mesmo_sequencial_sai_diferente_em_dias_diferentes()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2708);
        var emitente = EmitenteCpf(sorteio);
        var saidasDoSequencial1 = new HashSet<string>();
        for (var dia = 1; dia <= 30; dia++)
        {
            var chave = Montar(sorteio, "2606", emitente, "55", "126", Numero(false, dia, 6, '2', 1), '3', Digitos(sorteio, 8));
            saidasDoSequencial1.Add(Pseudonimizar(numeroDfe, chave, out _)[30..34]);
        }

        Assert.True(saidasDoSequencial1.Count >= 25, "O sequencial 1 saiu quase igual em todos os dias.");
    }

    [Fact]
    public void Numero_fora_da_NFF_e_injetivo_em_100_mil_consecutivos_e_o_zero_fica()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2709);
        var emitente = Emitente(sorteio);
        var codigo = Oito(CodigoPermitido(sorteio, 0));
        var inicio = sorteio.NextInt64(999_000_000);
        var saidas = new HashSet<string>();
        for (var numero = inicio; numero < inicio + 100_000; numero++)
        {
            var saida = Pseudonimizar(numeroDfe, Montar(sorteio, "2601", emitente, "57", "001", Nove(numero), '1', codigo), out _);
            Assert.True(saida[25..34] != "000000000", "Um número saiu 0.");
            saidas.Add(saida[25..34]);
        }

        Assert.Equal(100_000, saidas.Count);
        var zero = Montar(sorteio, "2601", emitente, "57", "001", "000000000", '1', codigo);
        var saidaDoZero = Pseudonimizar(numeroDfe, zero, out var ocorrencias);
        Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.NumeroZero), "O número 0 não foi contado.");
        Assert.True(saidaDoZero[25..34] == "000000000", "O número 0 mudou.");
    }

    [Fact]
    public void Codigo_nunca_sai_proibido_nem_igual_ao_numero_pseudonimizado()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2710);
        for (var vez = 0; vez < 20_000; vez++)
        {
            var chave = ChaveQualquer(sorteio, codigoDe8: true);
            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);
            if ((ocorrencias & (OcorrenciasChaveDfe.CodigoProibido | OcorrenciasChaveDfe.CodigoIgualAoNumero)) != 0)
            {
                continue;
            }

            var codigo = int.Parse(saida[35..43], CultureInfo.InvariantCulture);
            Assert.False(RegrasChaveDfe.EhCodigoNumericoProibido(codigo), "O código saiu proibido.");
            Assert.True(codigo != long.Parse(saida[25..34], CultureInfo.InvariantCulture), "O código saiu igual ao número.");
        }
    }

    [Fact]
    public void Os_20_codigos_proibidos_ficam_e_sao_contados()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2711);
        foreach (var proibido in RegrasChaveDfe.CodigosNumericosProibidos.ToArray())
        {
            var chave = Montar(sorteio, "2601", Emitente(sorteio), "55", "001", Nove(1 + sorteio.NextInt64(999_999_999)), '1', Oito(proibido));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.CodigoProibido), "O código proibido não foi contado.");
            Assert.True(saida[35..43] == chave[35..43], "O código proibido mudou.");
            Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez a chave com código proibido.");
        }
    }

    [Fact]
    public void Codigo_igual_ao_numero_pseudonimizado_fica_e_e_contado_e_a_comparacao_e_numerica()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2712);
        var achados = 0;
        var maiores = 0;
        while (achados < 20 || maiores < 20)
        {
            // Calcula o número' com um código qualquer e monta a chave com o código igual a ele (quando cabe em 8 dígitos)
            // ou igual aos seus 8 últimos dígitos (quando não cabe, e então não é igual).
            var emitente = Emitente(sorteio);
            var numero = Nove(1 + sorteio.NextInt64(999_999_999));
            var numeroSaida = Pseudonimizar(numeroDfe, Montar(sorteio, "2601", emitente, "55", "001", numero, '1', "50000001"), out _)[25..34];
            var codigo = numeroSaida[1..];
            if (RegrasChaveDfe.EhCodigoNumericoProibido(int.Parse(codigo, CultureInfo.InvariantCulture)))
            {
                continue;
            }

            var chave = Montar(sorteio, "2601", emitente, "55", "001", numero, '1', codigo);
            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);
            if (numeroSaida[0] == '0')
            {
                Assert.Equal(OcorrenciasChaveDfe.CodigoIgualAoNumero, ocorrencias);
                Assert.True(saida[35..43] == codigo, "O código igual ao número' mudou.");
                Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez a chave com código igual ao número'.");
                achados++;
            }
            else
            {
                Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
                maiores++;
            }
        }
    }

    [Fact]
    public void Codigo_que_a_permutacao_levaria_ao_numero_pseudonimizado_segue_para_o_proximo_do_ciclo()
    {
        // Monta pela inversa o código que, sem a exclusão do número', sairia igual a ele: a amostra quase nunca o pega.
        using var numeroDfe = Criar();
        using var cnf8 = Feistel(Variantes.CodigoNumerico8, 10_000, 10_000);
        var sorteio = new Random(2720);
        var montados = 0;
        while (montados < 20)
        {
            var emitente = Emitente(sorteio);
            var numero = Nove(1 + sorteio.NextInt64(999_999_999));
            var numeroSaida = Pseudonimizar(numeroDfe, Montar(sorteio, "2601", emitente, "55", "001", numero, '1', "50000001"), out _)[25..34];
            var numeroPseudonimizado = ulong.Parse(numeroSaida, CultureInfo.InvariantCulture);
            if (numeroPseudonimizado >= 100_000_000 || RegrasChaveDfe.EhCodigoNumericoProibido((int)numeroPseudonimizado))
            {
                continue;
            }

            var ajuste = Ascii(emitente + numeroSaida);
            var codigo = cnf8.Inverter(numeroPseudonimizado, ajuste, Proibidos(), adicional: null);
            if (RegrasChaveDfe.EhCodigoNumericoProibido((int)codigo) || codigo == numeroPseudonimizado)
            {
                continue;
            }

            var chave = Montar(sorteio, "2601", emitente, "55", "001", numero, '1', Oito((long)codigo));
            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            Assert.True(saida[35..43] != numeroSaida[1..], "O código saiu igual ao número pseudonimizado.");
            Assert.True(saida[35..43] == Oito((long)cnf8.Permutar(codigo, ajuste, Proibidos(), numeroPseudonimizado)), "O código não seguiu para o próximo do ciclo.");
            Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez o código que seguiu para o próximo do ciclo.");
            montados++;
        }
    }

    [Fact]
    public void Numero_que_a_permutacao_levaria_ao_zero_segue_para_o_proximo_do_ciclo()
    {
        // Só um número por ajuste cai no 0; sem a exclusão ele sairia 000000000.
        using var numeroDfe = Criar();
        using var nnf = Feistel(Variantes.NumeroDfe, 10_000, 100_000);
        var sorteio = new Random(2721);
        for (var vez = 0; vez < 50; vez++)
        {
            var emitente = Emitente(sorteio);
            var serie = Serie(sorteio);
            var ajuste = Ascii(emitente + "\0" + "57" + serie);
            var levadoAoZero = nnf.Inverter(0, ajuste, ConjuntoExcluido.Vazio);
            if (levadoAoZero == 0)
            {
                continue;
            }

            var chave = Montar(sorteio, "2601", emitente, "57", serie, Nove((long)levadoAoZero), '1', Oito(CodigoPermitido(sorteio, 0)));
            var saida = Pseudonimizar(numeroDfe, chave, out _);

            Assert.True(saida[25..34] != "000000000", "O número saiu 0.");
            Assert.True(saida[25..34] == Nove((long)nnf.Permutar(levadoAoZero, ajuste, ConjuntoExcluido.DeValores(0))), "O número não seguiu para o próximo do ciclo.");
            Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez o número levado ao zero.");
        }
    }

    [Fact]
    public void Codigo_que_a_permutacao_levaria_a_um_proibido_segue_para_o_proximo_do_ciclo()
    {
        // Para cada um dos 20 proibidos, o código que sem a exclusão sairia nele: a amostra quase nunca o pega.
        using var numeroDfe = Criar();
        using var cnf8 = Feistel(Variantes.CodigoNumerico8, 10_000, 10_000);
        var sorteio = new Random(2722);
        foreach (var proibido in RegrasChaveDfe.CodigosNumericosProibidos.ToArray())
        {
            var emitente = Emitente(sorteio);
            var numero = Nove(1 + sorteio.NextInt64(999_999_999));
            var numeroSaida = Pseudonimizar(numeroDfe, Montar(sorteio, "2601", emitente, "55", "001", numero, '1', "50000001"), out _)[25..34];
            var numeroPseudonimizado = ulong.Parse(numeroSaida, CultureInfo.InvariantCulture);
            ulong? adicional = numeroPseudonimizado < 100_000_000 ? numeroPseudonimizado : null;
            var ajuste = Ascii(emitente + numeroSaida);
            var codigo = cnf8.Inverter((ulong)proibido, ajuste, ConjuntoExcluido.Vazio);
            if (RegrasChaveDfe.EhCodigoNumericoProibido((int)codigo) || codigo == numeroPseudonimizado)
            {
                continue;
            }

            var chave = Montar(sorteio, "2601", emitente, "55", "001", numero, '1', Oito((long)codigo));
            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            Assert.False(RegrasChaveDfe.EhCodigoNumericoProibido(int.Parse(saida[35..43], CultureInfo.InvariantCulture)), "O código saiu proibido.");
            Assert.True(saida[35..43] == Oito((long)cnf8.Permutar(codigo, ajuste, Proibidos(), adicional)), "O código não seguiu para o próximo do ciclo.");
            Assert.True(Inverter(numeroDfe, saida) == chave, "A inversa não desfez o código levado a um proibido.");
        }
    }

    [Fact]
    public void Codigo_proibido_igual_ao_numero_pseudonimizado_conta_como_proibido()
    {
        // Monta o número cujo pseudônimo é um proibido p e põe p como código: vale a ordem proibido antes de igual.
        using var numeroDfe = Criar();
        using var nnf = Feistel(Variantes.NumeroDfe, 10_000, 100_000);
        var sorteio = new Random(2723);
        foreach (var proibido in RegrasChaveDfe.CodigosNumericosProibidos.ToArray().Where(codigo => codigo != 0))
        {
            var emitente = Emitente(sorteio);
            var serie = Serie(sorteio);
            var numero = nnf.Inverter((ulong)proibido, Ascii(emitente + "\0" + "55" + serie), ConjuntoExcluido.DeValores(0));
            var chave = Montar(sorteio, "2601", emitente, "55", serie, Nove((long)numero), '1', Oito(proibido));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.True(saida[25..34] == Nove(proibido), "O número pseudonimizado não é o proibido montado.");
            Assert.Equal(OcorrenciasChaveDfe.CodigoProibido, ocorrencias);
            Assert.True(saida[35..43] == chave[35..43], "O código proibido mudou.");
        }
    }

    [Theory]
    [InlineData("2402", 3, 29)]
    [InlineData("2502", 3, 28)]
    [InlineData("2604", 5, 30)]
    public void Dia_vale_so_se_existe_tambem_no_mes_do_AAMM(string aamm, int mesDoNumero, int dias)
    {
        // O mês do número tem mais dias que o do AAMM: valem só os dias do AAMM.
        using var numeroDfe = Criar();
        var sorteio = new Random(2724);
        var emitente = EmitenteCpf(sorteio);
        var saidas = new HashSet<int>();
        for (var dia = 1; dia <= 31; dia++)
        {
            var chave = Montar(sorteio, aamm, emitente, "55", "124", Numero(false, dia, mesDoNumero, '2', 15), '3', Digitos(sorteio, 8));

            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            var diaSaida = int.Parse(saida[25..27], CultureInfo.InvariantCulture);
            if (dia <= dias)
            {
                Assert.True(diaSaida >= 1 && diaSaida <= dias, "O dia saiu fora dos que existem nos dois meses.");
                saidas.Add(diaSaida);
            }
            else
            {
                Assert.Equal(OcorrenciasChaveDfe.DataNffInvalida, ocorrencias);
                Assert.Equal(dia, diaSaida);
            }
        }

        Assert.Equal(dias, saidas.Count);
    }

    [Theory]
    [InlineData("55", false)]
    [InlineData("65", false)]
    [InlineData("57", true)]
    [InlineData("58", true)]
    public void Na_NFF_mes_e_tipo_ficam_e_dia_e_sequencial_mudam_nas_posicoes_do_modelo(string modelo, bool transporte)
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2713);
        var diasMudaram = 0;
        for (var vez = 0; vez < 300; vez++)
        {
            var dia = 1 + sorteio.Next(28);
            var chave = Montar(sorteio, "2602", EmitenteCpf(sorteio), modelo, "126", Numero(transporte, dia, 2, transporte ? null : '2', 1 + sorteio.Next(9_999)), '3', Digitos(sorteio, 8));

            var saida = Pseudonimizar(numeroDfe, chave, out _);

            if (transporte)
            {
                Assert.True(saida[25..27] == chave[25..27], "O mês do CT-e ou do MDF-e mudou.");
                diasMudaram += saida[27..29] != chave[27..29] ? 1 : 0;
            }
            else
            {
                Assert.True(saida[27..30] == chave[27..30], "O mês ou o tipo da NF-e mudou.");
                diasMudaram += saida[25..27] != chave[25..27] ? 1 : 0;
            }
        }

        // Um dia permutado entre 28 sai igual por acaso em cerca de 1/28 das vezes.
        Assert.True(diasMudaram >= 250, "O dia quase não mudou: as posições do dia e do mês podem estar trocadas.");
    }

    [Fact]
    public void Inversa_desfaz_a_chave_em_toda_classe()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2714);
        for (var vez = 0; vez < 20_000; vez++)
        {
            var chave = ChaveQualquer(sorteio, codigoDe8: sorteio.Next(4) != 0);
            var saida = Pseudonimizar(numeroDfe, chave, out var ocorrencias);

            Assert.True(Inverter(numeroDfe, saida, out var ocorrenciasDaInversa) == chave, "A inversa não desfez a chave.");
            Assert.Equal(ocorrencias, ocorrenciasDaInversa);
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_resultado_e_dominio_versao_ou_ambiente_diferente_nao()
    {
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroDominio = Criar(dominio: "dfe-numero-teste");
        using var outraVersao = Criar(versao: 2);
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        var sorteio = new Random(2715);
        for (var vez = 0; vez < 100; vez++)
        {
            var chave = ChaveQualquer(sorteio, codigoDe8: true);
            var a = Pseudonimizar(primeiro, chave, out _);

            Assert.True(a == Pseudonimizar(segundo, chave, out _), "A mesma configuração deu resultados diferentes.");
            Assert.True(a != Pseudonimizar(outroDominio, chave, out _), "Outro domínio deu o mesmo resultado.");
            Assert.True(a != Pseudonimizar(outraVersao, chave, out _), "Outra versão deu o mesmo resultado.");
            Assert.True(a != Pseudonimizar(outroAmbiente, chave, out _), "Outro ambiente deu o mesmo resultado.");
        }
    }

    [Fact]
    public void Chave_sem_emitente_classificado_ou_fora_da_estrutura_e_recusada_sem_o_valor()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2716);
        var numero = Nove(1 + sorteio.NextInt64(999_999_999));
        var valida = Montar(sorteio, "2601", Emitente(sorteio), "55", "001", numero, '1', "50000001");
        var anomala = Montar(sorteio, "2601", Emitente(sorteio), "59", "001", numero, '1', "50000001");
        var anomalaComLeiaute = Montar(sorteio, "2601", "123" + CpfValido(sorteio), "55", "920", numero, '1', "50000001");
        Assert.True(ClassificadorEmitente.Classificar(anomalaComLeiaute) is { EhAnomalia: true, Leiaute: not null }, "O caso de anomalia com leiaute não foi montado.");
        Assert.Throws<ArgumentException>("chave", () => numeroDfe.Pseudonimizar(anomalaComLeiaute.ToCharArray()));
        Assert.Throws<ArgumentException>("chave", () => numeroDfe.Inverter(anomalaComLeiaute.ToCharArray()));
        Assert.Throws<ArgumentException>("chave", () => numeroDfe.Pseudonimizar(new string('0', 44).ToCharArray()));

        var semEmitente = Assert.Throws<ArgumentException>("chave", () => numeroDfe.Pseudonimizar(anomala.ToCharArray()));
        var foraDaEstrutura = Assert.Throws<ArgumentException>("chave", () => numeroDfe.Pseudonimizar(valida[..43].ToCharArray()));
        Assert.Throws<ArgumentException>("chave", () => numeroDfe.Pseudonimizar(("a" + valida[1..]).ToCharArray()));
        Assert.True(Pseudonimizar(numeroDfe, valida, out _) != valida, "A chave válida não foi pseudonimizada.");
        Assert.DoesNotContain(numero, semEmitente.Message + foraDaEstrutura.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(numero[1..], semEmitente.Message + foraDaEstrutura.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Depois_do_descarte_toda_chave_e_recusada()
    {
        var numeroDfe = Criar();
        var sorteio = new Random(2717);
        var chave = Montar(sorteio, "2601", Emitente(sorteio), "55", "001", Nove(1), '1', "50000001");
        numeroDfe.Dispose();
        numeroDfe.Dispose();

        Assert.Throws<ObjectDisposedException>(() => numeroDfe.Pseudonimizar(chave.ToCharArray()));
        Assert.Throws<ObjectDisposedException>(() => numeroDfe.Inverter(chave.ToCharArray()));
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2718);
        var chaves = Enumerable.Range(0, 20_000).Select(_ => ChaveQualquer(sorteio, codigoDe8: true)).ToArray();
        var sequencial = chaves.Select(chave => Pseudonimizar(numeroDfe, chave, out _)).ToArray();
        var paralelo = new string[chaves.Length];

        Parallel.For(0, chaves.Length, i => paralelo[i] = Pseudonimizar(numeroDfe, chaves[i], out _));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum resultado.");
    }

    [Fact]
    public void Pseudonimizar_nao_aloca()
    {
        using var numeroDfe = Criar();
        var sorteio = new Random(2719);
        var chaves = Enumerable.Range(0, 64).Select(i => ChaveQualquer(sorteio, codigoDe8: i % 4 != 0).ToCharArray()).ToArray();
        var copia = new char[44];
        Pseudonimizar(numeroDfe, chaves, copia, 2);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        Pseudonimizar(numeroDfe, chaves, copia, 100);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
    }

    private static void Pseudonimizar(PseudonimizadorNumeroDfe numeroDfe, char[][] chaves, char[] copia, int vezes)
    {
        for (var vez = 0; vez < vezes; vez++)
        {
            for (var i = 0; i < chaves.Length; i++)
            {
                chaves[i].CopyTo(copia, 0);
                numeroDfe.Pseudonimizar(copia);
            }
        }
    }

    private static string Pseudonimizar(PseudonimizadorNumeroDfe numeroDfe, string chave, out OcorrenciasChaveDfe ocorrencias)
    {
        var classificacao = ClassificadorEmitente.Classificar(chave);
        var caracteres = chave.ToCharArray();
        ocorrencias = numeroDfe.Pseudonimizar(caracteres);
        Assert.True(ClassificadorEmitente.Classificar(caracteres).Tipo == classificacao.Tipo, "A classificação da saída mudou.");
        return new string(caracteres);
    }

    private static string Inverter(PseudonimizadorNumeroDfe numeroDfe, string saida) => Inverter(numeroDfe, saida, out _);

    private static string Inverter(PseudonimizadorNumeroDfe numeroDfe, string saida, out OcorrenciasChaveDfe ocorrencias)
    {
        var caracteres = saida.ToCharArray();
        ocorrencias = numeroDfe.Inverter(caracteres);
        return new string(caracteres);
    }

    // Chave de estrutura válida e com emitente classificado, em qualquer modelo, com e sem NFF.
    private static string ChaveQualquer(Random sorteio, bool codigoDe8)
    {
        string modelo;
        if (codigoDe8)
        {
            string[] modelos = ["55", "65", "57", "58", "63", "64", "67"];
            modelo = modelos[sorteio.Next(modelos.Length)];
        }
        else
        {
            modelo = sorteio.Next(2) == 0 ? "62" : "66";
        }

        var transporte = modelo is "57" or "58";
        var nff = modelo is "55" or "65" or "57" or "58" && sorteio.Next(2) == 0;
        var ano = (sorteio.Next(6) + 22).ToString(CultureInfo.InvariantCulture);
        var aamm = ano + (1 + sorteio.Next(12)).ToString("00", CultureInfo.InvariantCulture);
        string numero;
        string serie;
        var tipoEmissaoSemNff = '1';
        if (nff)
        {
            // Dia e mês às vezes fora, sequencial às vezes 0.
            var mes = sorteio.Next(10) == 0 ? sorteio.Next(100) : int.Parse(aamm[2..], CultureInfo.InvariantCulture);
            var dia = sorteio.Next(10) == 0 ? sorteio.Next(100) : 1 + sorteio.Next(31);
            var sequencial = sorteio.Next(20) == 0 ? 0 : 1 + sorteio.Next(transporte ? 99_999 : 9_999);
            numero = Numero(transporte, dia, mes, transporte ? null : (char)('1' + sorteio.Next(2)), sequencial);
            serie = sorteio.Next(2).ToString(CultureInfo.InvariantCulture) + ano;
        }
        else
        {
            numero = sorteio.Next(50) == 0 ? "000000000" : Nove(1 + sorteio.NextInt64(999_999_999));
            serie = sorteio.Next(3).ToString("000", CultureInfo.InvariantCulture);
            if (sorteio.Next(4) == 0 && modelo is "55" or "57" or "58")
            {
                // tpEmis 3 sem NFF: série do PAA (NF-e: 980 a 989, CNPJ) ou CT-e e MDF-e antes de 2006.
                serie = modelo == "55" ? (980 + sorteio.Next(10)).ToString(CultureInfo.InvariantCulture) : serie;
                aamm = modelo == "55" ? aamm : "19" + aamm[2..];
                tipoEmissaoSemNff = '3';
            }
        }

        var codigo = sorteio.Next(20) == 0 ? Oito(RegrasChaveDfe.CodigosNumericosProibidos[sorteio.Next(20)]) : Digitos(sorteio, 8);
        var emitente = modelo is "55" or "65" && nff ? (numero[4] == '1' ? CnpjValido(sorteio, alfanumerico: false) : "000" + CpfValido(sorteio)) : Emitente(sorteio);
        if (modelo == "55" && tipoEmissaoSemNff == '3')
        {
            emitente = CnpjValido(sorteio, alfanumerico: false);
        }

        return Montar(sorteio, aamm, emitente, modelo, serie, numero, nff ? '3' : tipoEmissaoSemNff, codigo);
    }

    // O número da NFF: dia, mês, tipo e sequencial de 4 (NF-e e NFC-e) ou mês, dia e sequencial de 5 (CT-e e MDF-e).
    private static string Numero(bool transporte, int dia, int mes, char? tipo, long sequencial) => transporte
        ? mes.ToString("00", CultureInfo.InvariantCulture) + dia.ToString("00", CultureInfo.InvariantCulture) + sequencial.ToString("00000", CultureInfo.InvariantCulture)
        : dia.ToString("00", CultureInfo.InvariantCulture) + mes.ToString("00", CultureInfo.InvariantCulture) + tipo + sequencial.ToString("0000", CultureInfo.InvariantCulture);

    private static void Zerar(char[] numero, bool transporte)
    {
        for (var i = transporte ? 4 : 5; i < 9; i++)
        {
            numero[i] = '0';
        }
    }

    private static string Montar(Random sorteio, string aamm, string emitente, string modelo, string serie, string numero, char tipoEmissao, string codigo)
    {
        var primeiros43 = Digitos(sorteio, 2) + aamm + emitente + modelo + serie + numero + tipoEmissao + codigo;
        return primeiros43 + (char)('0' + DvChaveClassico(primeiros43));
    }

    private static string Emitente(Random sorteio) => sorteio.Next(3) switch
    {
        0 => "000" + CpfValido(sorteio),
        1 => CnpjValido(sorteio, alfanumerico: false),
        _ => CnpjValido(sorteio, alfanumerico: true),
    };

    private static string EmitenteCpf(Random sorteio) => "000" + CpfValido(sorteio);

    private static string Serie(Random sorteio) => sorteio.Next(910).ToString("000", CultureInfo.InvariantCulture);

    private static long CodigoPermitido(Random sorteio, long numero)
    {
        long codigo;
        do
        {
            codigo = sorteio.NextInt64(100_000_000);
        }
        while (RegrasChaveDfe.EhCodigoNumericoProibido((int)codigo) || codigo == numero);

        return codigo;
    }

    private static int DiasNoMes(int mes, int ano) => DateTime.DaysInMonth(2000 + ano, mes);

    private static ConjuntoExcluido DiasExcluidos(int dias) =>
        ConjuntoExcluido.DeValores([0, .. Enumerable.Range(dias + 1, 35 - dias).Select(dia => (ulong)dia)]);

    private static ConjuntoExcluido Proibidos() =>
        ConjuntoExcluido.DeValores([.. RegrasChaveDfe.CodigosNumericosProibidos.ToArray().Select(codigo => (ulong)codigo)]);

    private static string Nove(long valor) => valor.ToString("000000000", CultureInfo.InvariantCulture);

    private static string Oito(long valor) => valor.ToString("00000000", CultureInfo.InvariantCulture);

    private static byte[] Ascii(string texto) => Encoding.ASCII.GetBytes(texto);

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorNumeroDfe Criar(string dominio = PseudonimizadorNumeroDfe.NomeDoDominio, int versao = 1, string ambiente = "homologacao") =>
        new(_chave, dominio, versao, ambiente);

    private PermutacaoFeistel Feistel(string variante, ulong a, ulong b, int rodadas = 12)
    {
        var chaveDeDominio = new byte[32];
        Subchaves.DerivarChaveDeDominio(_chave, PseudonimizadorNumeroDfe.NomeDoDominio, 1, variante, "homologacao", chaveDeDominio);
        return new PermutacaoFeistel(chaveDeDominio, 10, a, b, rodadas);
    }
}
