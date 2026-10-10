using CodeProcess.Incognito.Documentos;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// DV de CPF, CNPJ (numérico e alfanumérico) e chave de DF-e. Nenhum identificador fica escrito no código: os válidos
/// são sorteados na hora e calculados por uma implementação clássica independente; as únicas referências fixas são
/// as âncoras didáticas da Receita em <c>testes/ancoras/</c>. As asserções não imprimem os valores sorteados.
/// </summary>
public sealed class ValidadorBrTestes
{
    private const string Pasta = "ancoras";
    private const string AncorasCnpj = "cnpj-alfanumerico-rfb.json";
    private const string Alfanumerico = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static TheoryData<string> CasosDidaticos => ArquivosDeTeste.NomesDosCasos(Pasta, AncorasCnpj, "casos");

    public static TheoryData<string> CpfsMalformados =>
    [
        "",
        "123",
        "1234567890",
        "123456789012",
        "123.456.789.01",
        "123456.789-01",
        "123.456.78901",
        " 12345678901",
        "12345678901 ",
        "1234567890a",
        "١٢٣٤٥٦٧٨٩٠١",
        "123-456-789-01",
    ];

    public static TheoryData<string> CnpjsMalformados =>
    [
        "",
        "1234567890123",
        "123456789012345",
        "12.345.678/0001-9",
        "12.345.678.0001-90",
        "12345678/0001-90",
        "12.345.678/000190",
    ];

    [Fact]
    public void Ancoras_didaticas_sao_marcadas_como_origem_didatico_rfb()
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, AncorasCnpj);

        Assert.Equal("didatico-rfb", documento.RootElement.GetProperty("origem").GetString());
        Assert.Equal(12, documento.RootElement.GetProperty("casos").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(CasosDidaticos))]
    public void Cnpj_confere_com_as_ancoras_didaticas_da_Receita(string nome)
    {
        using var documento = ArquivosDeTeste.Ler(Pasta, AncorasCnpj);
        var caso = ArquivosDeTeste.Caso(documento, "casos", nome);
        var cnpj = ArquivosDeTeste.Texto(caso, "cnpj");
        var valido = caso.GetProperty("valido").GetBoolean();

        Assert.Equal(valido, ValidadorBr.Cnpj(cnpj));
        Assert.Equal(valido, ValidadorBr.Cnpj(cnpj.AsSpan()));
    }

    [Fact]
    public void Regressao_do_FAQ_AA3456780003_vale_com_DV_86_e_nao_com_29()
    {
        // Os casos ficam só no arquivo de âncoras; aqui são lidos pelo nome.
        using var documento = ArquivosDeTeste.Ler(Pasta, AncorasCnpj);
        var comDv29 = ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "casos", "regressao do FAQ: AA3456780003 com DV 29"), "cnpj");
        var comDv86 = ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "casos", "regressao do FAQ: AA3456780003 com DV 86"), "cnpj");

        Assert.False(ValidadorBr.Cnpj(comDv29));
        Assert.True(ValidadorBr.Cnpj(comDv86));
        Assert.Equal(86, DigitosVerificadores.Cnpj(comDv86.AsSpan(0, 12)));
    }

    [Fact]
    public void Cnpj_valido_alterado_na_forma_e_recusado()
    {
        var sorteio = new Random(1316);
        for (var vez = 0; vez < 200; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true);

            Assert.False(ValidadorBr.Cnpj(cnpj[..13] + "A"), "CNPJ com letra no DV foi aceito.");
            Assert.False(ValidadorBr.Cnpj(Trocar(cnpj, 11, '-')), "CNPJ com hífen na base foi aceito.");
            Assert.False(ValidadorBr.Cnpj(Trocar(cnpj, 11, 'É')), "CNPJ com letra acentuada foi aceito.");
            Assert.False(ValidadorBr.Cnpj(cnpj + " "), "CNPJ com espaço no fim foi aceito.");
            Assert.False(ValidadorBr.Cnpj(" " + cnpj), "CNPJ com espaço no início foi aceito.");
        }
    }

    [Fact]
    public void Cpf_valido_e_aceito_com_e_sem_mascara()
    {
        var sorteio = new Random(1301);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var cpf = CpfValido(sorteio);

            Assert.True(ValidadorBr.Cpf(cpf), "CPF válido recusado.");
            Assert.True(ValidadorBr.Cpf(MascararCpf(cpf)), "CPF válido com máscara recusado.");
            Assert.True(ValidadorBr.Cpf(MascararCpf(cpf).AsSpan()), "CPF válido com máscara recusado na sobrecarga com span.");
        }
    }

    [Fact]
    public void Cpf_com_um_digito_trocado_segue_a_referencia_classica()
    {
        var sorteio = new Random(1302);
        var recusados = 0;
        for (var vez = 0; vez < 500; vez++)
        {
            var cpf = CpfValido(sorteio).ToCharArray();
            var posicao = sorteio.Next(11);
            cpf[posicao] = (char)('0' + ((cpf[posicao] - '0' + 1 + sorteio.Next(9)) % 10));

            // Os restos 0 e 1 dão o mesmo DV, então uma troca rara passa: a referência clássica decide.
            var texto = new string(cpf);
            var esperado = DvCpfClassico(texto[..9]) == (((cpf[9] - '0') * 10) + (cpf[10] - '0')) && texto.Distinct().Count() > 1;
            Assert.Equal(esperado, ValidadorBr.Cpf(cpf));
            recusados += esperado ? 0 : 1;
        }

        Assert.True(recusados > 450, "Trocas de dígito deveriam ser quase sempre recusadas.");
    }

    [Fact]
    public void Cpf_repdigito_e_recusado_mesmo_com_DV_valido()
    {
        for (var digito = '0'; digito <= '9'; digito++)
        {
            var repdigito = new string(digito, 11);
            Assert.Equal(DvCpfClassico(repdigito[..9]), int.Parse(repdigito[9..], System.Globalization.CultureInfo.InvariantCulture));

            Assert.False(ValidadorBr.Cpf(repdigito), "CPF repdígito foi aceito.");
            Assert.False(ValidadorBr.Cpf(MascararCpf(repdigito)), "CPF repdígito com máscara foi aceito.");
        }
    }

    [Theory]
    [MemberData(nameof(CpfsMalformados))]
    public void Cpf_malformado_e_recusado(string cpf)
    {
        Assert.False(ValidadorBr.Cpf(cpf));
        Assert.False(ValidadorBr.Cpf(cpf.AsSpan()));
    }

    [Fact]
    public void Cpf_e_cnpj_nulos_sao_recusados()
    {
        Assert.False(ValidadorBr.Cpf((string?)null));
        Assert.False(ValidadorBr.Cnpj((string?)null));
        Assert.False(ValidadorBr.ChaveDfe((string?)null));
    }

    [Fact]
    public void Cnpj_numerico_e_alfanumerico_validos_sao_aceitos_com_e_sem_mascara()
    {
        var sorteio = new Random(1303);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: vez % 2 == 0);

            Assert.True(ValidadorBr.Cnpj(cnpj), "CNPJ válido recusado.");
            Assert.True(ValidadorBr.Cnpj(MascararCnpj(cnpj)), "CNPJ válido com máscara recusado.");
            Assert.True(ValidadorBr.Cnpj(MascararCnpj(cnpj).AsSpan()), "CNPJ válido com máscara recusado na sobrecarga com span.");
        }
    }

    [Fact]
    public void Cnpj_com_um_caractere_trocado_segue_a_referencia_classica()
    {
        var sorteio = new Random(1304);
        for (var vez = 0; vez < 500; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true).ToCharArray();
            var posicao = sorteio.Next(14);
            var alfabeto = posicao < 12 ? Alfanumerico : "0123456789";
            var original = cnpj[posicao];
            do
            {
                cnpj[posicao] = alfabeto[sorteio.Next(alfabeto.Length)];
            }
            while (cnpj[posicao] == original);

            // O módulo 11 com valores ASCII − 48 não pega toda troca de letra (Z e 9 diferem por 33 ≡ 0 mod 11):
            // a referência clássica decide se a troca continua válida.
            var esperado = DvCnpjClassico(new string(cnpj, 0, 12)) == (((cnpj[12] - '0') * 10) + (cnpj[13] - '0'))
                && cnpj.Distinct().Count() > 1;
            Assert.Equal(esperado, ValidadorBr.Cnpj(cnpj));
        }
    }

    [Fact]
    public void Cnpj_com_letra_minuscula_e_recusado()
    {
        var sorteio = new Random(1305);
        for (var vez = 0; vez < 200; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            var comMinuscula = string.Concat(cnpj[..12].ToLowerInvariant(), cnpj[12..]);
            if (comMinuscula == cnpj)
            {
                continue;
            }

            Assert.False(ValidadorBr.Cnpj(comMinuscula), "CNPJ com letra minúscula foi aceito.");
        }
    }

    [Fact]
    public void Cnpj_com_14_caracteres_iguais_e_recusado()
    {
        // Só os 14 zeros têm DV válido: é o único caso em que a recusa vem da regra, e não do DV.
        Assert.Equal(0, DvCnpjClassico(new string('0', 12)));
        Assert.False(ValidadorBr.Cnpj(new string('0', 14)), "CNPJ com 14 zeros foi aceito.");
        foreach (var caractere in Alfanumerico)
        {
            Assert.False(ValidadorBr.Cnpj(new string(caractere, 14)), "CNPJ com caracteres iguais foi aceito.");
        }
    }

    [Fact]
    public void Cnpj_com_base_repetida_e_DV_valido_e_aceito()
    {
        // A regra recusa só os 14 caracteres iguais; uma base repetida com o seu DV continua válida.
        foreach (var caractere in "ABZ")
        {
            var baseRepetida = new string(caractere, 12);
            var cnpj = baseRepetida + DvCnpjClassico(baseRepetida).ToString("00", System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(ValidadorBr.Cnpj(cnpj), "CNPJ de base repetida com DV válido foi recusado.");
        }
    }

    [Theory]
    [MemberData(nameof(CnpjsMalformados))]
    public void Cnpj_malformado_e_recusado(string cnpj)
    {
        Assert.False(ValidadorBr.Cnpj(cnpj));
        Assert.False(ValidadorBr.Cnpj(cnpj.AsSpan()));
    }

    [Fact]
    public void Em_digitos_o_DV_por_ASCII_menos_48_e_o_do_algoritmo_classico()
    {
        var sorteio = new Random(1306);
        for (var vez = 0; vez < 10_000; vez++)
        {
            var baseCnpj = Digitos(sorteio, 12);
            var primeiros43 = Digitos(sorteio, 43);

            Assert.True(DigitosVerificadores.Cnpj(baseCnpj) == DvCnpjClassico(baseCnpj), "DV do CNPJ difere do clássico.");
            Assert.True(DigitosVerificadores.ChaveDfe(primeiros43) == DvChaveClassico(primeiros43), "cDV difere do clássico.");
        }
    }

    [Fact]
    public void Cpf_bate_com_o_algoritmo_classico()
    {
        var sorteio = new Random(1307);
        for (var vez = 0; vez < 10_000; vez++)
        {
            var baseCpf = Digitos(sorteio, 9);

            Assert.True(DigitosVerificadores.Cpf(baseCpf) == DvCpfClassico(baseCpf), "DV do CPF difere do clássico.");
        }
    }

    [Fact]
    public void Chave_de_DFe_valida_e_aceita()
    {
        var sorteio = new Random(1308);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var chave = ChaveValida(sorteio, emitenteAlfanumerico: vez % 2 == 0);

            Assert.True(ValidadorBr.ChaveDfe(chave), "Chave de DF-e válida recusada.");
            Assert.True(ValidadorBr.ChaveDfe(chave.AsSpan()), "Chave de DF-e válida recusada na sobrecarga com span.");
        }
    }

    [Fact]
    public void Chave_de_DFe_com_CPF_no_emitente_e_aceita_sem_conferir_o_DV_de_CNPJ()
    {
        // Emitente pessoa física: 000 + CPF. Os 14 caracteres não formam CNPJ válido, e a chave continua válida:
        // o validador confere só a estrutura e o cDV.
        var sorteio = new Random(1317);
        var conferidas = 0;
        while (conferidas < 200)
        {
            var emitente = "000" + CpfValido(sorteio);
            if (ValidadorBr.Cnpj(emitente))
            {
                continue;
            }

            var primeiros43 = Digitos(sorteio, 6) + emitente + Digitos(sorteio, 23);
            var chave = primeiros43 + (char)('0' + DvChaveClassico(primeiros43));

            Assert.True(ValidadorBr.ChaveDfe(chave), "Chave de DF-e com CPF no emitente foi recusada.");
            conferidas++;
        }
    }

    [Fact]
    public void Chave_de_DFe_com_cDV_errado_e_recusada()
    {
        var sorteio = new Random(1309);
        for (var vez = 0; vez < 500; vez++)
        {
            var chave = ChaveValida(sorteio, emitenteAlfanumerico: true).ToCharArray();
            chave[43] = (char)('0' + ((chave[43] - '0' + 1 + sorteio.Next(9)) % 10));

            Assert.False(ValidadorBr.ChaveDfe(chave), "Chave de DF-e com cDV errado foi aceita.");
        }
    }

    [Fact]
    public void Chave_de_DFe_com_letra_fora_das_posicoes_7_a_18_ou_minuscula_e_recusada()
    {
        var sorteio = new Random(1310);
        for (var posicao = 0; posicao < 44; posicao++)
        {
            var chave = ChaveValida(sorteio, emitenteAlfanumerico: false).ToCharArray();
            chave[posicao] = posicao is >= 6 and < 18 ? 'a' : 'A';

            // Recalcula o cDV para a recusa vir da estrutura, não do dígito.
            if (posicao < 43)
            {
                chave[43] = (char)('0' + DvChaveClassico(new string(chave, 0, 43)));
            }

            Assert.False(ValidadorBr.ChaveDfe(chave), "Chave de DF-e com letra fora do lugar foi aceita.");
        }
    }

    [Theory]
    [InlineData(43)]
    [InlineData(45)]
    [InlineData(50)]
    [InlineData(0)]
    public void Chave_de_DFe_com_tamanho_diferente_de_44_e_recusada(int tamanho)
    {
        Assert.False(ValidadorBr.ChaveDfe(new string('1', tamanho)));
    }

    [Fact]
    public void Chave_de_DFe_com_prefixo_ou_espacos_e_recusada()
    {
        var chave = ChaveValida(new Random(1311), emitenteAlfanumerico: false);

        Assert.False(ValidadorBr.ChaveDfe("NFe" + chave));
        Assert.False(ValidadorBr.ChaveDfe(string.Join(' ', chave.Chunk(4).Select(grupo => new string(grupo)))));
    }

    [Fact]
    public void Mascara_com_separador_trocado_e_recusada()
    {
        var sorteio = new Random(1313);
        var cpf = MascararCpf(CpfValido(sorteio));
        var cnpj = MascararCnpj(CnpjValido(sorteio, alfanumerico: true));

        foreach (var posicao in new[] { 3, 7, 11 })
        {
            foreach (var trocado in new[] { '-', '.', '/', ' ', '0' })
            {
                if (trocado != cpf[posicao])
                {
                    Assert.False(ValidadorBr.Cpf(Trocar(cpf, posicao, trocado)), "CPF com separador trocado foi aceito.");
                }
            }
        }

        foreach (var posicao in new[] { 2, 6, 10, 15 })
        {
            foreach (var trocado in new[] { '-', '.', '/', ' ', '0' })
            {
                if (trocado != cnpj[posicao])
                {
                    Assert.False(ValidadorBr.Cnpj(Trocar(cnpj, posicao, trocado)), "CNPJ com separador trocado foi aceito.");
                }
            }
        }
    }

    [Fact]
    public void Letra_no_DV_do_CNPJ_e_recusada_mesmo_quando_o_valor_bate()
    {
        // Com ASCII − 48, "3A" valeria 3 × 10 + 17 = 47: um DV 47 escrito assim não pode passar.
        var sorteio = new Random(1314);
        var conferidos = 0;
        while (conferidos < 200)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            var dv = int.Parse(cnpj[12..], System.Globalization.CultureInfo.InvariantCulture);
            for (var valor = 17; valor <= 42 && valor <= dv; valor++)
            {
                if ((dv - valor) % 10 == 0)
                {
                    var comLetra = string.Concat(cnpj[..12], (char)('0' + ((dv - valor) / 10)), (char)('0' + valor));
                    Assert.False(ValidadorBr.Cnpj(comLetra), "CNPJ com letra no DV foi aceito.");
                    conferidos++;
                }
            }
        }
    }

    [Fact]
    public void Digito_de_outro_alfabeto_e_recusado_mesmo_quando_o_DV_bate()
    {
        // O dígito arábico-índico um (U+0661) vale 1585 em ASCII − 48; o DV é calculado com esse valor, para a
        // recusa vir da regra de só dígitos ASCII.
        var sorteio = new Random(1315);
        for (var vez = 0; vez < 50; vez++)
        {
            var baseCpf = "١" + Digitos(sorteio, 8);
            var cpf = baseCpf + DvCpfClassico(baseCpf).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(ValidadorBr.Cpf(cpf), "CPF com dígito de outro alfabeto foi aceito.");

            var primeiros43 = "١" + Digitos(sorteio, 42);
            var chave = primeiros43 + (char)('0' + DvChaveClassico(primeiros43));
            Assert.False(ValidadorBr.ChaveDfe(chave), "Chave de DF-e com dígito de outro alfabeto foi aceita.");

            var baseCnpj = "١" + Digitos(sorteio, 11);
            var cnpj = baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(ValidadorBr.Cnpj(cnpj), "CNPJ com dígito de outro alfabeto na base foi aceito.");
        }
    }

    [Fact]
    public void Lista_dos_codigos_numericos_proibidos_tem_os_20_da_regra_B03_10()
    {
        var esperados = Enumerable.Range(0, 10).Select(digito => digito * 11_111_111)
            .Concat([12_345_678, 23_456_789, 34_567_890, 45_678_901, 56_789_012, 67_890_123, 78_901_234, 89_012_345, 90_123_456, 1_234_567])
            .Order();

        Assert.Equal(esperados, RegrasChaveDfe.CodigosNumericosProibidos.ToArray().Order());
        Assert.True(RegrasChaveDfe.EhCodigoNumericoProibido(1_234_567), "01234567 não foi reconhecido como proibido.");
        Assert.False(RegrasChaveDfe.EhCodigoNumericoProibido(1_234_568), "01234568 foi reconhecido como proibido.");
    }

    [Fact]
    public void Validacao_com_span_nao_aloca()
    {
        var sorteio = new Random(1312);
        var cpf = MascararCpf(CpfValido(sorteio)).ToCharArray();
        var cnpj = MascararCnpj(CnpjValido(sorteio, alfanumerico: true)).ToCharArray();
        var chave = ChaveValida(sorteio, emitenteAlfanumerico: true).ToCharArray();
        var aceitos = Validar(cpf, cnpj, chave, 100);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        aceitos += Validar(cpf, cnpj, chave, 10_000);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
        Assert.Equal(3 * 10_100, aceitos);
    }

    private static int Validar(char[] cpf, char[] cnpj, char[] chave, int vezes)
    {
        var aceitos = 0;
        for (var vez = 0; vez < vezes; vez++)
        {
            aceitos += (ValidadorBr.Cpf(cpf.AsSpan()) ? 1 : 0) + (ValidadorBr.Cnpj(cnpj.AsSpan()) ? 1 : 0) + (ValidadorBr.ChaveDfe(chave.AsSpan()) ? 1 : 0);
        }

        return aceitos;
    }

    private static string CpfValido(Random sorteio)
    {
        string baseCpf;
        do
        {
            baseCpf = Digitos(sorteio, 9);
        }
        while (baseCpf.Distinct().Count() == 1);

        return baseCpf + DvCpfClassico(baseCpf).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CnpjValido(Random sorteio, bool alfanumerico)
    {
        var alfabeto = alfanumerico ? Alfanumerico : "0123456789";
        string baseCnpj;
        do
        {
            baseCnpj = new string(Enumerable.Range(0, 12).Select(_ => alfabeto[sorteio.Next(alfabeto.Length)]).ToArray());
        }
        while (baseCnpj.Distinct().Count() == 1);

        return baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ChaveValida(Random sorteio, bool emitenteAlfanumerico)
    {
        var primeiros43 = Digitos(sorteio, 6) + CnpjValido(sorteio, emitenteAlfanumerico) + Digitos(sorteio, 23);
        return primeiros43 + (char)('0' + DvChaveClassico(primeiros43));
    }

    private static string Digitos(Random sorteio, int quantidade) =>
        new(Enumerable.Range(0, quantidade).Select(_ => (char)('0' + sorteio.Next(10))).ToArray());

    private static string Trocar(string texto, int posicao, char caractere) =>
        string.Concat(texto.AsSpan(0, posicao), new string(caractere, 1), texto.AsSpan(posicao + 1));

    private static string MascararCpf(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    private static string MascararCnpj(string cnpj) => $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

    // Referências clássicas, escritas à parte da implementação: dígitos inteiros e, no CNPJ e na chave, o valor
    // ASCII − 48 só para as letras.
    private static int DvCpfClassico(string baseCpf)
    {
        var primeiro = Modulo11(baseCpf.Select((c, i) => (c - '0') * (10 - i)).Sum());
        var segundo = Modulo11(baseCpf.Select((c, i) => (c - '0') * (11 - i)).Sum() + (primeiro * 2));
        return (primeiro * 10) + segundo;
    }

    private static int DvCnpjClassico(string baseCnpj)
    {
        int[] pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var primeiro = Modulo11(baseCnpj.Select((c, i) => Valor(c) * pesos1[i]).Sum());
        var segundo = Modulo11(baseCnpj.Select((c, i) => Valor(c) * pesos2[i]).Sum() + (primeiro * pesos2[12]));
        return (primeiro * 10) + segundo;
    }

    private static int DvChaveClassico(string primeiros43)
    {
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += Valor(primeiros43[i]) * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        return Modulo11(soma);
    }

    private static int Valor(char caractere) => char.IsAsciiDigit(caractere) ? caractere - '0' : caractere - 48;

    private static int Modulo11(int soma) => soma % 11 < 2 ? 0 : 11 - (soma % 11);
}
