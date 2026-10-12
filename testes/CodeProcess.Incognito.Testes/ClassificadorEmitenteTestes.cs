using System.Globalization;
using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Classificação do emitente da chave de DF-e e tabela de leiautes: cada passo da NF-e e da NFC-e, a regra dos zeros do
/// CT-e e do MDF-e, os modelos só com CNPJ, cada motivo de anomalia, as bordas de data e de série, o DV fora da decisão,
/// a estrutura conferida e a invariância sob os campos que a pseudonimização muda. Emitentes e números são sorteados na
/// hora; nenhuma chave é literal.
/// </summary>
public sealed class ClassificadorEmitenteTestes
{
    private const string Cnpj = "CNPJ";
    private const string CnpjSemZeros = "CNPJ sem zeros";
    private const string CnpjComLetra = "CNPJ com letra";
    private const string CnpjComLetraSoNa18 = "CNPJ com letra só na 18";
    private const string Cpf = "CPF";

    // Caso: modelo, AAMM, série, tpEmis, posição 30, emitente; esperado: tipo ou motivo, NFF e exclusão das raízes 000.
    public static TheoryData<string, string, string, char, char, string, string, bool, bool> Casos => new()
    {
        // NF-e e NFC-e: letra e AAMM anterior a 1808 decidem antes de tudo, inclusive do tpEmis 3.
        { "55", "2601", "920", '1', '5', CnpjComLetra, Cnpj, false, false },
        { "65", "2601", "920", '1', '5', CnpjComLetra, Cnpj, false, false },
        { "55", "2601", "920", '1', '5', CnpjComLetraSoNa18, Cnpj, false, false },
        { "55", "1808", "001", '3', '1', CnpjComLetra, Cnpj, false, false },
        { "65", "2103", "001", '3', '2', CnpjComLetra, Cnpj, false, false },
        { "55", "1807", "920", '1', '5', Cpf, Cnpj, false, false },
        { "65", "1807", "920", '1', '5', Cpf, Cnpj, false, false },
        { "55", "1808", "920", '1', '5', Cpf, Cpf, false, false },
        { "55", "1712", "999", '3', '9', CnpjSemZeros, Cnpj, false, false },

        // tpEmis 3: antes da NFF é anomalia; a partir de 2104, a posição 30 decide e vence a série e o 000.
        { "55", "1808", "001", '3', '1', CnpjSemZeros, nameof(MotivoAnomaliaDfe.TipoEmissao3ForaDoPeriodo), false, false },
        { "65", "2103", "001", '3', '1', CnpjSemZeros, nameof(MotivoAnomaliaDfe.TipoEmissao3ForaDoPeriodo), false, false },
        { "55", "2104", "026", '3', '1', CnpjSemZeros, Cnpj, true, false },
        { "65", "2104", "126", '3', '1', CnpjSemZeros, Cnpj, true, false },
        { "55", "2601", "026", '3', '1', Cpf, Cnpj, true, false },
        { "65", "2601", "126", '3', '1', Cpf, Cnpj, true, false },
        { "65", "2104", "126", '3', '2', Cpf, Cpf, true, false },
        { "55", "2601", "026", '3', '2', CnpjSemZeros, nameof(MotivoAnomaliaDfe.CpfSemZerosAEsquerda), false, false },
        { "55", "2601", "026", '3', '0', Cpf, nameof(MotivoAnomaliaDfe.NffSemTipoDeEmissor), false, false },
        { "65", "2601", "026", '3', '9', Cpf, nameof(MotivoAnomaliaDfe.NffSemTipoDeEmissor), false, false },
        { "55", "2601", "026", '3', '2', CnpjComLetra, Cnpj, true, false },
        { "55", "2601", "985", '3', '1', CnpjSemZeros, Cnpj, true, false },
        { "65", "2601", "975", '3', '2', Cpf, Cpf, true, false },
        { "65", "2601", "999", '3', '1', CnpjSemZeros, Cnpj, true, false },

        // Séries, com os outros tpEmis; fora da NFF, a posição 30 é do número e não decide.
        { "55", "2601", "000", '1', '5', CnpjSemZeros, Cnpj, false, false },
        { "55", "2601", "001", '1', '2', Cpf, Cnpj, false, false },
        { "65", "2601", "001", '9', '1', Cpf, Cnpj, false, false },
        { "55", "2601", "909", '6', '5', Cpf, Cnpj, false, false },
        { "55", "2601", "910", '1', '5', Cpf, Cpf, false, false },
        { "65", "2601", "969", '9', '5', Cpf, Cpf, false, false },
        { "55", "2601", "920", '7', '1', CnpjSemZeros, nameof(MotivoAnomaliaDfe.CpfSemZerosAEsquerda), false, false },
        { "55", "2601", "970", '1', '5', Cpf, Cpf, false, false },
        { "55", "2601", "979", '1', '5', Cpf, Cpf, false, false },
        { "55", "2601", "975", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.CpfSemZerosAEsquerda), false, false },
        { "55", "2601", "980", '1', '5', CnpjSemZeros, Cnpj, false, false },
        { "55", "2601", "989", '4', '5', Cpf, Cnpj, false, false },
        { "55", "2601", "990", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.SerieSemEmissao), false, false },
        { "55", "2601", "999", '2', '5', Cpf, nameof(MotivoAnomaliaDfe.SerieSemEmissao), false, false },
        { "65", "2601", "970", '1', '5', Cpf, nameof(MotivoAnomaliaDfe.SerieSemEmissao), false, false },
        { "65", "2601", "985", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.SerieSemEmissao), false, false },
        { "65", "2601", "999", '1', '5', Cpf, nameof(MotivoAnomaliaDfe.SerieSemEmissao), false, false },

        // CT-e e MDF-e, regra dos zeros: série, data e posição 30 não decidem.
        { "57", "2601", "001", '1', '5', Cpf, Cpf, false, false },
        { "58", "1501", "001", '1', '5', Cpf, Cpf, false, false },
        { "57", "2601", "001", '1', '5', CnpjSemZeros, Cnpj, false, true },
        { "58", "2601", "920", '1', '5', CnpjSemZeros, Cnpj, false, true },
        { "57", "2601", "975", '1', '5', Cpf, Cpf, false, false },
        { "58", "2601", "985", '1', '2', CnpjSemZeros, Cnpj, false, true },
        { "57", "2601", "126", '3', '5', Cpf, Cpf, true, false },
        { "57", "1501", "126", '3', '5', Cpf, Cpf, true, false },
        { "58", "2601", "126", '3', '1', CnpjSemZeros, Cnpj, true, true },
        { "57", "2601", "126", '3', '5', CnpjComLetra, Cnpj, true, false },
        { "58", "2601", "001", '1', '5', CnpjComLetra, Cnpj, false, false },
        { "57", "2601", "001", '1', '5', CnpjComLetraSoNa18, Cnpj, false, false },
        { "57", "2601", "969", '3', '5', Cpf, Cpf, true, false },
        { "57", "2601", "970", '3', '5', Cpf, Cpf, false, false },
        { "58", "2601", "989", '3', '5', CnpjSemZeros, Cnpj, false, true },
        { "58", "2601", "990", '3', '5', Cpf, Cpf, true, false },

        // Só CNPJ: o emitente com forma de CPF continua CNPJ, e tpEmis 3 não é NFF.
        { "62", "2601", "920", '3', '2', Cpf, Cnpj, false, false },
        { "63", "2601", "001", '1', '5', Cpf, Cnpj, false, false },
        { "64", "2601", "001", '2', '5', CnpjSemZeros, Cnpj, false, false },
        { "66", "2601", "975", '1', '5', CnpjComLetra, Cnpj, false, false },
        { "67", "2601", "001", '3', '1', Cpf, Cnpj, false, false },

        // Fora da tabela.
        { "59", "2601", "001", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.ModeloForaDaTabela), false, false },
        { "00", "2601", "001", '1', '5', Cpf, nameof(MotivoAnomaliaDfe.ModeloForaDaTabela), false, false },
        { "56", "2601", "001", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.ModeloForaDaTabela), false, false },
        { "60", "2601", "001", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.ModeloForaDaTabela), false, false },
        { "99", "2601", "001", '1', '5', CnpjSemZeros, nameof(MotivoAnomaliaDfe.ModeloForaDaTabela), false, false },
    };

    public static TheoryData<int> Modelos => new(Enum.GetValues<ModeloDfe>().Select(modelo => (int)modelo));

    public static TheoryData<int> PosicoesDoEmitenteComLetra => new(Enumerable.Range(7, 12));

    // Cada caso parte de uma chave de CPF na NF-e, série 920, e sai da estrutura num ponto.
    public static TheoryData<string> ForaDaEstrutura =>
    [
        "vazia",
        "35 posicoes",
        "43 posicoes",
        "45 posicoes",
        "50 posicoes",
        "minuscula no emitente",
        "letra na posicao 19",
        "letra na posicao 6",
        "letra no AAMM",
        "espaco no emitente",
        "espaco no AAMM",
        "barra na serie",
        "NUL no emitente",
        "digito de largura total no modelo",
        "digito arabe no emitente",
        "digito arabe no AAMM",
    ];

    [Theory]
    [MemberData(nameof(Casos))]
    public void Classificacao_segue_o_passo_do_modelo(
        string modelo, string aamm, string serie, char tipoEmissao, char posicao30, string emitente, string esperado, bool nff, bool excluiRaizComZeros)
    {
        var sorteio = new Random(2601);
        for (var vez = 0; vez < 50; vez++)
        {
            var chave = Montar(sorteio, modelo, aamm, serie, tipoEmissao, posicao30, Emitente(sorteio, emitente));

            var classificacao = ClassificadorEmitente.Classificar(chave);

            Assert.Equal(esperado, Descrever(classificacao));
            Assert.Equal(esperado is not (Cnpj or Cpf), classificacao.EhAnomalia);
            Assert.Equal(nff, classificacao.EhNff);
            Assert.Equal(excluiRaizComZeros, classificacao.ExcluiRaizComZeros);
            Assert.Same(LeiautesDfe.TentarObter(modelo, out var leiaute) ? leiaute : null, classificacao.Leiaute);
        }
    }

    [Theory]
    [MemberData(nameof(PosicoesDoEmitenteComLetra))]
    public void Uma_unica_letra_em_qualquer_posicao_de_7_a_18_da_CNPJ(int posicao)
    {
        // 000 nas posições 7 a 9 (salvo onde está a letra) e dígitos no resto: só a letra separa o CNPJ de um CPF.
        var sorteio = new Random(2606 + posicao);
        for (var vez = 0; vez < 50; vez++)
        {
            var baseCnpj = ("000" + Digitos(sorteio, 9)).ToCharArray();
            baseCnpj[posicao - 7] = (char)('A' + sorteio.Next(26));
            var emitente = new string(baseCnpj) + DvCnpjClassico(new string(baseCnpj)).ToString("00", CultureInfo.InvariantCulture);

            var nfe = ClassificadorEmitente.Classificar(Montar(sorteio, "55", "2601", "920", '1', '5', emitente));
            var cte = ClassificadorEmitente.Classificar(Montar(sorteio, "57", "2601", "001", '1', '5', emitente));

            Assert.Equal(TipoEmitente.Cnpj, nfe.Tipo);
            Assert.True(cte.Tipo == TipoEmitente.Cnpj && !cte.ExcluiRaizComZeros, "No CT-e, CNPJ com letra saiu como CPF ou com exclusão.");
        }
    }

    [Theory]
    [InlineData("55", "001", '1', '5', Cnpj)]
    [InlineData("55", "920", '1', '5', Cpf)]
    [InlineData("55", "975", '1', '5', Cpf)]
    [InlineData("55", "985", '1', '5', Cnpj)]
    [InlineData("65", "001", '1', '5', Cnpj)]
    [InlineData("65", "910", '1', '5', Cpf)]
    [InlineData("55", "026", '3', '1', Cnpj)]
    [InlineData("65", "126", '3', '2', Cpf)]
    [InlineData("57", "001", '1', '5', Cpf)]
    [InlineData("58", "920", '1', '5', Cpf)]
    [InlineData("57", "126", '3', '5', Cpf)]
    public void CPF_iniciado_por_00_que_forma_CNPJ_valido_segue_a_regra_e_nao_o_DV(
        string modelo, string serie, char tipoEmissao, char posicao30, string esperado)
    {
        // Todo CPF que começa com 00 forma, com o prefixo 000, um CNPJ de DV válido: o DV não pode decidir.
        var sorteio = new Random(2602);
        for (var vez = 0; vez < 200; vez++)
        {
            var cpf = CpfIniciadoPorZeroZero(sorteio);
            var emitente = "000" + cpf;
            Assert.True(ValidadorBr.Cpf(cpf) && ValidadorBr.Cnpj(emitente), "O emitente sorteado não é CPF e CNPJ válidos ao mesmo tempo.");

            var classificacao = ClassificadorEmitente.Classificar(Montar(sorteio, modelo, "2601", serie, tipoEmissao, posicao30, emitente));

            Assert.Equal(esperado, Descrever(classificacao));
        }
    }

    [Theory]
    [MemberData(nameof(ForaDaEstrutura))]
    public void Chave_fora_da_estrutura_e_anomalia_e_nunca_CNPJ(string caso)
    {
        var sorteio = new Random(2607);
        var chave = Montar(sorteio, "55", "2601", "920", '1', '5', Emitente(sorteio, Cpf));
        var fora = caso switch
        {
            "vazia" => string.Empty,
            "35 posicoes" => chave[..35],
            "43 posicoes" => chave[..43],
            "45 posicoes" => chave + "0",
            "50 posicoes" => chave + Digitos(sorteio, 6),
            "minuscula no emitente" => Trocar(chave, 9, 'a'),
            "letra na posicao 19" => Trocar(chave, 18, 'A'),
            "letra na posicao 6" => Trocar(chave, 5, 'A'),
            "letra no AAMM" => Trocar(chave, 2, 'A'),
            "espaco no emitente" => Trocar(chave, 9, ' '),
            "espaco no AAMM" => Trocar(chave, 2, ' '),
            "barra na serie" => Trocar(chave, 22, '/'),
            "NUL no emitente" => Trocar(chave, 12, '\u0000'),
            "digito de largura total no modelo" => Trocar(chave, 21, '５'),
            "digito arabe no emitente" => Trocar(chave, 10, '٣'),
            "digito arabe no AAMM" => Trocar(chave, 3, '٦'),
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };

        var classificacao = ClassificadorEmitente.Classificar(fora);

        Assert.True(classificacao.EhAnomalia, "Chave fora da estrutura foi classificada.");
        Assert.Equal(MotivoAnomaliaDfe.EstruturaInvalida, classificacao.Anomalia);
        Assert.Null(classificacao.Leiaute);
        Assert.False(ValidadorBr.ChaveDfe(fora), "O validador aceitou uma chave fora da estrutura.");
    }

    [Fact]
    public void Chave_zerada_tem_motivo_proprio_e_nenhuma_outra_chave_com_zeros_o_tem()
    {
        var zerada = new string('0', 44);
        Assert.True(ValidadorBr.ChaveDfe(zerada), "A chave zerada deveria ter cDV válido.");

        var classificacao = ClassificadorEmitente.Classificar(zerada);
        Assert.True(classificacao.EhAnomalia && classificacao.EhChaveZerada);
        Assert.Equal(MotivoAnomaliaDfe.ChaveZerada, classificacao.Anomalia);
        Assert.Null(classificacao.Leiaute);

        for (var posicao = 0; posicao < 44; posicao++)
        {
            var quase = ClassificadorEmitente.Classificar(Trocar(zerada, posicao, '1'));
            Assert.False(quase.EhChaveZerada, "Uma chave com um dígito diferente de zero foi tratada como zerada.");
        }
    }

    [Fact]
    public void Classificacao_nao_inicializada_e_anomalia_sem_motivo_e_nunca_CNPJ()
    {
        var naoInicializada = default(ClassificacaoEmitente);

        Assert.True(naoInicializada.EhAnomalia);
        Assert.False(naoInicializada.EhChaveZerada);
        Assert.Equal(MotivoAnomaliaDfe.NaoClassificada, naoInicializada.Anomalia);
        Assert.False(Enum.IsDefined((TipoEmitente)0), "O zero de TipoEmitente não pode ser um tipo.");
        Assert.False(Enum.IsDefined((RegraEmitente)0), "O zero de RegraEmitente não pode ser uma regra.");
    }

    [Fact]
    public void Classificacao_e_invariante_nos_campos_que_a_pseudonimizacao_muda()
    {
        // Muda o que a pseudonimização muda e mantém só o que ela preserva: AAMM, modelo, série, tpEmis, a posição 30
        // na NFF da NF-e e da NFC-e, a presença de letra no emitente e o 000 nas posições 7 a 9 onde a classe o lê. O
        // cUF, o número, o código e o cDV também mudam.
        var sorteio = new Random(2603);
        var classes = new HashSet<string>();
        for (var vez = 0; vez < 20_000; vez++)
        {
            var chave = ChaveQualquer(sorteio);
            var original = ClassificadorEmitente.Classificar(chave);
            if (original.EhChaveZerada)
            {
                continue;
            }

            var variada = ClassificadorEmitente.Classificar(Variar(sorteio, chave, original));

            Assert.True(Descrever(original) == Descrever(variada), "A classificação mudou com um campo que a pseudonimização muda.");
            Assert.True(original.EhNff == variada.EhNff, "A NFF mudou com um campo que a pseudonimização muda.");
            Assert.True(original.ExcluiRaizComZeros == variada.ExcluiRaizComZeros, "A exclusão das raízes 000 mudou.");
            Assert.True(ReferenceEquals(original.Leiaute, variada.Leiaute), "O leiaute mudou.");
            classes.Add(Descrever(original) + original.EhNff + original.ExcluiRaizComZeros);
        }

        // A amostra passa por todas as 11 classes: CNPJ com e sem NFF e com e sem exclusão, CPF com e sem NFF e os cinco
        // motivos de anomalia de uma chave com estrutura (a zerada fica de fora).
        Assert.Equal(11, classes.Count);
    }

    [Fact]
    public void Tirar_o_000_de_um_CPF_da_anomalia_na_NFe_e_CNPJ_com_exclusao_no_CTe()
    {
        var sorteio = new Random(2604);
        for (var vez = 0; vez < 500; vez++)
        {
            var semZeros = (1 + sorteio.Next(999)).ToString("000", CultureInfo.InvariantCulture);
            var cpf = CpfValido(sorteio);

            var nfe = ClassificadorEmitente.Classificar(Montar(sorteio, "55", "2601", "920", '1', '5', semZeros + cpf));
            var cte = ClassificadorEmitente.Classificar(Montar(sorteio, "57", "2601", "001", '1', '5', semZeros + cpf));

            Assert.Equal(MotivoAnomaliaDfe.CpfSemZerosAEsquerda, nfe.Anomalia);
            Assert.True(cte.Tipo == TipoEmitente.Cnpj && cte.ExcluiRaizComZeros, "No CT-e, emitente sem 000 não é CNPJ com exclusão.");
        }
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Cada_modelo_tem_a_regra_o_codigo_e_a_NFF_do_seu_leiaute(int codigo)
    {
        var modelo = (ModeloDfe)codigo;
        var leiaute = LeiautesDfe.Obter(modelo);
        var (regra, digitos, nff) = modelo switch
        {
            ModeloDfe.Nfe => (RegraEmitente.SeriesComPaa, 8, "dia 25, mês 27, tipo 29, sequencial 30 com 4"),
            ModeloDfe.Nfce => (RegraEmitente.Series, 8, "dia 25, mês 27, tipo 29, sequencial 30 com 4"),
            ModeloDfe.Cte or ModeloDfe.Mdfe => (RegraEmitente.Zeros, 8, "dia 27, mês 25, tipo -, sequencial 29 com 5"),
            ModeloDfe.Nf3e or ModeloDfe.Nfcom => (RegraEmitente.SempreCnpj, 7, "sem NFF"),
            _ => (RegraEmitente.SempreCnpj, 8, "sem NFF"),
        };

        Assert.Equal(modelo, leiaute.Modelo);
        Assert.Equal(regra, leiaute.Regra);
        Assert.Equal(digitos, leiaute.DigitosDoCodigo);
        Assert.Equal(modelo is ModeloDfe.Nf3e or ModeloDfe.Nfcom, leiaute.PreservaPosicao36);
        Assert.Equal(digitos == 8, leiaute.ExcluiCodigosProibidos);
        Assert.Equal(nff, leiaute.Nff is { } partes
            ? $"dia {partes.IndiceDia}, mês {partes.IndiceMes}, tipo {(partes.IndiceTipo?.ToString(CultureInfo.InvariantCulture) ?? "-")}, sequencial {partes.IndiceSequencial} com {partes.TamanhoSequencial}"
            : "sem NFF");
        Assert.True(LeiautesDfe.TentarObter(codigo.ToString("00", CultureInfo.InvariantCulture), out var pelaChave) && ReferenceEquals(leiaute, pelaChave));
    }

    [Fact]
    public void So_os_nove_codigos_de_modelo_tem_leiaute()
    {
        var comLeiaute = Enumerable.Range(0, 100)
            .Where(codigo => LeiautesDfe.TentarObter(codigo.ToString("00", CultureInfo.InvariantCulture), out _))
            .ToArray();

        Assert.Equal(Enum.GetValues<ModeloDfe>().Select(modelo => (int)modelo).Order(), comLeiaute);
        foreach (var codigo in new[] { "5", "555", "", "5A", " 5", "５５", "٥٥", "+5" })
        {
            Assert.False(LeiautesDfe.TentarObter(codigo, out var leiaute));
            Assert.Null(leiaute);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => LeiautesDfe.Obter((ModeloDfe)59));
        Assert.Throws<ArgumentOutOfRangeException>(() => LeiautesDfe.Obter((ModeloDfe)200));
    }

    [Fact]
    public void Classificar_nao_aloca()
    {
        var sorteio = new Random(2605);
        var chaves = Enumerable.Range(0, 64).Select(_ => ChaveQualquer(sorteio).ToCharArray()).ToArray();
        var cpfs = Classificar(chaves, 10);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        cpfs += Classificar(chaves, 1_000);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
        Assert.True(cpfs > 0, "Nenhuma chave da amostra foi classificada como CPF.");
    }

    private static int Classificar(char[][] chaves, int vezes)
    {
        var cpfs = 0;
        for (var vez = 0; vez < vezes; vez++)
        {
            foreach (var chave in chaves)
            {
                cpfs += ClassificadorEmitente.Classificar(chave).Tipo == TipoEmitente.Cpf ? 1 : 0;
            }
        }

        return cpfs;
    }

    private static string Descrever(ClassificacaoEmitente classificacao) => classificacao.Tipo switch
    {
        TipoEmitente.Cnpj => Cnpj,
        TipoEmitente.Cpf => Cpf,
        _ => classificacao.Anomalia.ToString(),
    };

    // A chave com os campos dados e o resto sorteado; cDV calculado.
    private static string Montar(Random sorteio, string modelo, string aamm, string serie, char tipoEmissao, char posicao30, string emitente)
    {
        var numero = Digitos(sorteio, 4) + posicao30 + Digitos(sorteio, 4);
        var primeiros43 = Digitos(sorteio, 2) + aamm + emitente + modelo + serie + numero + tipoEmissao + Digitos(sorteio, 8);
        return primeiros43 + (char)('0' + DvChaveClassico(primeiros43));
    }

    private static string Trocar(string chave, int indice, char caractere)
    {
        var caracteres = chave.ToCharArray();
        caracteres[indice] = caractere;
        return new string(caracteres);
    }

    private static string Emitente(Random sorteio, string tipo) => tipo switch
    {
        Cpf => "000" + CpfValido(sorteio),
        CnpjSemZeros => CnpjSemZerosNoInicio(sorteio),
        CnpjComLetra => CnpjAlfanumerico(sorteio),
        CnpjComLetraSoNa18 => CnpjComLetraSoNaUltimaDaOrdem(sorteio),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    private static string CnpjSemZerosNoInicio(Random sorteio)
    {
        string cnpj;
        do
        {
            cnpj = CnpjValido(sorteio, alfanumerico: false);
        }
        while (cnpj.StartsWith("000", StringComparison.Ordinal));

        return cnpj;
    }

    private static string CnpjAlfanumerico(Random sorteio)
    {
        string cnpj;
        do
        {
            cnpj = CnpjValido(sorteio, alfanumerico: true);
        }
        while (cnpj[..12].All(char.IsAsciiDigit));

        return cnpj;
    }

    // 000 nas posições 7 a 9 e a única letra na posição 18: só a letra separa o CNPJ de um CPF.
    private static string CnpjComLetraSoNaUltimaDaOrdem(Random sorteio)
    {
        var baseCnpj = "000" + Digitos(sorteio, 8) + (char)('A' + sorteio.Next(26));
        return baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string CpfIniciadoPorZeroZero(Random sorteio)
    {
        string cpf;
        do
        {
            cpf = "00" + Digitos(sorteio, 7);
        }
        while (cpf.Distinct().Count() == 1);

        return cpf + DvCpfClassico(cpf).ToString("00", CultureInfo.InvariantCulture);
    }

    // Chave de estrutura válida com campos sorteados, concentrada nas bordas que decidem.
    private static string ChaveQualquer(Random sorteio)
    {
        string[] modelos = ["55", "65", "57", "58", "62", "63", "64", "66", "67", "59", "00"];
        string[] aamms = ["1712", "1807", "1808", "2103", "2104", "2601", "0000", "9913"];
        string[] series = ["000", "909", "910", "969", "970", "979", "980", "989", "990", "999"];
        var modelo = modelos[sorteio.Next(modelos.Length)];
        var aamm = sorteio.Next(4) == 0 ? Digitos(sorteio, 4) : aamms[sorteio.Next(aamms.Length)];
        var serie = sorteio.Next(4) == 0 ? Digitos(sorteio, 3) : series[sorteio.Next(series.Length)];
        var tipoEmissao = sorteio.Next(2) == 0 ? '3' : (char)('1' + sorteio.Next(9));
        var posicao30 = "0123"[sorteio.Next(4)];
        var emitente = Emitente(sorteio, new[] { Cpf, CnpjSemZeros, CnpjComLetra }[sorteio.Next(3)]);
        if (sorteio.Next(10) == 0)
        {
            // Emitente numérico com 000 que não é CPF válido, e sem 000 que não é CNPJ válido: o DV não importa.
            emitente = (sorteio.Next(2) == 0 ? "000" : "123") + Digitos(sorteio, 11);
        }

        return Montar(sorteio, modelo, aamm, serie, tipoEmissao, posicao30, emitente);
    }

    // Sorteia de novo o que a pseudonimização muda, mantendo só o que ela preserva.
    private static string Variar(Random sorteio, string chave, ClassificacaoEmitente original)
    {
        string emitente;
        if (!chave[6..18].All(char.IsAsciiDigit))
        {
            emitente = CnpjAlfanumerico(sorteio);
        }
        else if (LeOsZeros(original))
        {
            emitente = chave[6..9] == "000"
                ? "000" + Digitos(sorteio, 11)
                : (sorteio.Next(9) + 1).ToString(CultureInfo.InvariantCulture) + Digitos(sorteio, 13);
        }
        else
        {
            // A classe não lê as posições 7 a 9: o CNPJ pseudonimizado pode ganhar ou perder o 000.
            emitente = (sorteio.Next(2) == 0 ? "000" : (sorteio.Next(9) + 1).ToString(CultureInfo.InvariantCulture) + "00") + Digitos(sorteio, 11);
        }

        var numero = Digitos(sorteio, 9).ToCharArray();
        if (chave[20..22] is "55" or "65" && chave[34] == '3' && int.Parse(chave[2..6], CultureInfo.InvariantCulture) >= 2104)
        {
            // Na NFF da NF-e e da NFC-e, a posição 30 é o tipo do emissor, preservado; fora dela, é dígito do número.
            numero[29 - 25] = chave[29];
        }

        var primeiros43 = Digitos(sorteio, 2) + chave[2..6] + emitente + chave[20..25] + new string(numero) + chave[34] + Digitos(sorteio, 8);
        return primeiros43 + (char)('0' + sorteio.Next(10));
    }

    // Onde a saída preserva o 000 das posições 7 a 9: no ramo CPF, na regra dos zeros e no CPF sem os zeros, que o
    // classificador reconhece por eles.
    private static bool LeOsZeros(ClassificacaoEmitente classificacao) =>
        classificacao.Tipo == TipoEmitente.Cpf
        || classificacao.ExcluiRaizComZeros
        || classificacao.Anomalia == MotivoAnomaliaDfe.CpfSemZerosAEsquerda;
}
