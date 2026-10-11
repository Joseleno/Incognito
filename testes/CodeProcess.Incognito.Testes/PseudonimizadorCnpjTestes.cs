using System.Globalization;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio CNPJ: raiz permutada pela variante da própria raiz (numérica ou alfanumérica), ordem preservada, DV válido
/// continua válido e inválido continua inválido, raiz zero como ponto fixo, as formas de entrada, as políticas e a
/// opção de permutar a ordem. Todo CNPJ é montado na hora, com o DV calculado: raiz e ordem sorteadas, ou raízes fixas
/// exigidas pelos critérios com ordem sorteada ou <c>0000</c> (que a Receita não atribui); o de raiz zero, que é
/// cadastrado, nunca aparece como literal. A chave é a pública do vetor de derivação; as asserções não imprimem valores.
/// </summary>
public sealed class PseudonimizadorCnpjTestes : IDisposable
{
    private const string SoDigitosTexto = "0123456789";

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> Malformados =>
    [
        "1A2B3C4D5E6f78",
        "1A2B3C4D5E6F7G",
        "1A2B3C4D5E6F7",
        "1A2B3C4D5E6F789",
        "1A2B3C4D5E6F",
        "1A2B3C4D5E67",
        "1A2B3C4D5E678",
        "1234567890123A",
        "12345678901",
        "123456789012345",
        "1A.2B3.C4D/5E6F.78",
        "1A-2B3.C4D/5E6F-78",
        "1A.2B3-C4D/5E6F-78",
        "1A.2B3.C4D-5E6F-78",
        "1A2B3C4D5E6F-78",
        " 1A2B3C4D5E6F78",
        "1A2B 3C4D5E6F78",
        "1A2B3C4D5E6F78\t",
        "١٢٣٤٥٦٧٨٩٠١٢٣٤",
        "1A2B3C4D5E6FÇ8",
        "1A2B3CÇD5E6F78",
        "1A2B3CＡD5E6F78",
        "1A2B3C4D5E6F٧٨",
        "1A.2B3.C4D/5E6F-789",
        "1A.2B3.C4D/5E6F-7",
    ];

    // As políticas são internas e entram como número: o método de teste é público.
    public static TheoryData<int, int> DigitosEPoliticas =>
        new(from digitos in Enumerable.Range(12, 2)
            from politica in Enum.GetValues<PoliticaEntradaMalformada>()
            select (digitos, (int)politica));

    public static TheoryData<int, bool> PoliticasEModos =>
        new(from politica in Enum.GetValues<PoliticaEntradaInvalida>()
            from modo in Enumerable.Range(0, 2)
            select ((int)politica, modo == 1));

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Raizes_diferentes_dao_raizes_diferentes_nas_duas_variantes()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1502);
        var ordem = Ordem(sorteio, alfanumerica: false);
        foreach (var alfanumerica in new[] { false, true })
        {
            var raizes = new HashSet<string>();
            while (raizes.Count < 100_000)
            {
                raizes.Add(Raiz(sorteio, alfanumerica));
            }

            var saidas = new HashSet<string>();
            foreach (var raiz in raizes)
            {
                Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(Montar(raiz, ordem), out var saida));
                saidas.Add(saida![..8]);
            }

            Assert.Equal(raizes.Count, saidas.Count);
        }
    }

    [Fact]
    public void CNPJ_valido_continua_valido_com_a_mesma_ordem_a_mesma_mascara_e_a_presenca_de_letra_na_raiz()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1503);
        for (var vez = 0; vez < 6_000; vez++)
        {
            var raizAlfanumerica = vez % 3 == 2;
            var entrada = Montar(Raiz(sorteio, raizAlfanumerica), Ordem(sorteio, alfanumerica: vez % 2 == 0));

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(entrada, out var saida));
            Assert.True(saida!.Length == 14 && ValidadorBr.Cnpj(saida), "CNPJ válido saiu inválido ou fora do formato.");
            Assert.True(saida[8..12] == entrada[8..12], "A ordem não foi preservada.");
            Assert.True(saida[..8] != entrada[..8], "A raiz foi copiada em claro.");
            Assert.True(TemLetra(saida[..8]) == raizAlfanumerica, "A presença de letra na raiz mudou.");

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(MascararCnpj(entrada), out var comMascara));
            Assert.True(comMascara == MascararCnpj(saida), "A máscara não foi preservada ou o pseudônimo mudou com ela.");
        }
    }

    [Fact]
    public void Matriz_e_filiais_continuam_com_a_mesma_raiz_inclusive_filial_alfanumerica_de_raiz_numerica()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1504);
        foreach (var raizAlfanumerica in new[] { false, true })
        {
            var raiz = Raiz(sorteio, raizAlfanumerica);
            var raizesDeSaida = new HashSet<string>();
            for (var filial = 0; filial < 40; filial++)
            {
                var ordem = Ordem(sorteio, alfanumerica: filial % 2 == 1);
                cnpj.Pseudonimizar(Montar(raiz, ordem), out var saida);

                Assert.True(saida![8..12] == ordem, "A ordem da filial não foi preservada.");
                raizesDeSaida.Add(saida[..8]);
            }

            Assert.Single(raizesDeSaida);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CNPJ_com_DV_invalido_continua_invalido_com_o_mesmo_deslocamento_em_texto_mascara_e_numero(bool permutarOrdem)
    {
        using var cnpj = Criar(permutarOrdem: permutarOrdem);
        var sorteio = new Random(1505);
        for (var vez = 0; vez < 5_000; vez++)
        {
            var valido = Montar(Raiz(sorteio, alfanumerica: vez % 2 == 1), Ordem(sorteio, alfanumerica: vez % 4 >= 2));
            var deslocamento = 1 + sorteio.Next(99);
            var entrada = DeslocarDv(valido, deslocamento);

            Assert.Equal(Desfecho.DvInvalidoPreservado, cnpj.Pseudonimizar(entrada, out var saida));
            cnpj.Pseudonimizar(valido, out var saidaDoValido);

            Assert.False(ValidadorBr.Cnpj(saida), "CNPJ com DV inválido saiu válido.");
            Assert.True(saida![..12] == saidaDoValido![..12], "Raiz ou ordem do pseudônimo depende do DV da entrada.");
            var dvSaida = int.Parse(saida[12..], CultureInfo.InvariantCulture);
            var dvCorretoDaSaida = int.Parse(saidaDoValido[12..], CultureInfo.InvariantCulture);
            Assert.True((dvSaida - dvCorretoDaSaida + 100) % 100 == deslocamento, "O deslocamento do DV não foi preservado.");

            Assert.Equal(Desfecho.DvInvalidoPreservado, cnpj.Pseudonimizar(MascararCnpj(entrada), out var comMascara));
            Assert.True(comMascara == MascararCnpj(saida), "Com máscara, o DV inválido não teve o mesmo tratamento.");

            if (!TemLetra(entrada))
            {
                Assert.Equal(Desfecho.DvInvalidoPreservado, cnpj.Pseudonimizar(long.Parse(entrada, CultureInfo.InvariantCulture), out var numero));
                Assert.True(Quatorze(numero!.Value) == saida, "Em coluna numérica, o DV inválido não teve o mesmo tratamento.");
            }
        }
    }

    [Fact]
    public void Os_100_DVs_de_uma_base_dao_100_saidas_diferentes()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1506);
        var baseCnpj = Raiz(sorteio, alfanumerica: true) + Ordem(sorteio, alfanumerica: false);

        var saidas = Enumerable.Range(0, 100)
            .Select(dv => { cnpj.Pseudonimizar(baseCnpj + dv.ToString("00", CultureInfo.InvariantCulture), out var saida); return saida; })
            .ToHashSet();

        Assert.Equal(100, saidas.Count);
    }

    [Theory]
    [MemberData(nameof(PoliticasEModos))]
    public void CNPJ_de_raiz_zero_continua_igual_e_e_ponto_fixo_e_a_politica_de_DV_vem_antes(int politica, bool permutarOrdem)
    {
        // Raiz 00000000 com ordem e DV montados na hora: é a raiz de um CNPJ cadastrado e nunca aparece como literal.
        // Fica igual com ordem numérica ou alfanumérica, com ou sem permutarOrdem.
        var invalida = (PoliticaEntradaInvalida)politica;
        using var cnpj = Criar(invalida, permutarOrdem: permutarOrdem);
        var sorteio = new Random(1507);
        foreach (var ordemAlfanumerica in new[] { false, true })
        {
            var comDvCerto = Montar(new string('0', 8), Ordem(sorteio, ordemAlfanumerica));

            Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(comDvCerto, out var saida));
            Assert.True(saida == comDvCerto, "CNPJ de raiz zero não ficou igual.");
            Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(MascararCnpj(comDvCerto), out var comMascara));
            Assert.True(comMascara == MascararCnpj(comDvCerto), "CNPJ de raiz zero com máscara não ficou igual.");

            var comDvErrado = DeslocarDv(comDvCerto, 41);
            foreach (var forma in new[] { comDvErrado, MascararCnpj(comDvErrado) })
            {
                switch (invalida)
                {
                    case PoliticaEntradaInvalida.Preservar:
                        Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(forma, out var mantido));
                        Assert.True(mantido == forma, "CNPJ de raiz zero com DV errado não ficou como veio.");
                        break;
                    case PoliticaEntradaInvalida.Falhar:
                        Assert.Equal("INC1004", Assert.Throws<IncognitoException>(() => cnpj.Pseudonimizar(forma, out _)).Codigo);
                        break;
                    default:
                        Assert.Equal(Desfecho.AnuladoDvInvalido, cnpj.Pseudonimizar(forma, out var anulado));
                        Assert.Null(anulado);
                        break;
                }
            }
        }

        var numerico = Montar(new string('0', 8), Ordem(sorteio, alfanumerica: false));
        Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(long.Parse(numerico, CultureInfo.InvariantCulture), out var numero));
        Assert.True(Quatorze(numero!.Value) == numerico, "CNPJ numérico de raiz zero não ficou igual.");
    }

    [Fact]
    public void As_outras_raizes_repdigito_sao_permutadas_e_nenhuma_saida_tem_raiz_zero()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1508);
        foreach (var simbolo in Alfanumerico.Where(simbolo => simbolo != '0'))
        {
            var raiz = new string(simbolo, 8);
            var entrada = Montar(raiz, Ordem(sorteio, alfanumerica: false));

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(entrada, out var saida));
            Assert.True(saida![..8] != raiz, "Raiz repdígito ficou em claro.");
            Assert.True(saida[..8] != new string('0', 8), "Uma saída caiu na raiz zero.");
        }
    }

    [Fact]
    public void Raiz_com_Z_e_raiz_com_16_produzem_saidas_diferentes()
    {
        // Regressão da v2: com ASCII − 48 as duas raízes valeriam 42 na codificação.
        using var cnpj = Criar();
        const string ordem = "0000";

        cnpj.Pseudonimizar(Montar("0000000Z", ordem), out var comLetra);
        cnpj.Pseudonimizar(Montar("00000016", ordem), out var soDigitos);

        Assert.True(comLetra![..8] != soDigitos![..8], "As duas raízes deram a mesma saída.");
        Assert.True(TemLetra(comLetra[..8]) && !TemLetra(soDigitos[..8]), "A variante não saiu da raiz.");
    }

    [Theory]
    [MemberData(nameof(DigitosEPoliticas))]
    public void CNPJ_em_texto_sem_os_zeros_a_esquerda_e_pseudonimizado_com_14_digitos_em_toda_politica(int digitos, int politica)
    {
        using var cnpj = Criar(malformada: (PoliticaEntradaMalformada)politica);
        var sorteio = new Random(1510 + digitos);
        for (var vez = 0; vez < 1_000; vez++)
        {
            // Raiz com 14 − dígitos zeros à esquerda e o dígito seguinte diferente de zero.
            var raiz = new string('0', 14 - digitos) + (char)('1' + sorteio.Next(9)) + Digitos(sorteio, digitos - 7);
            var completo = Montar(raiz, Ordem(sorteio, alfanumerica: false));

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(completo[(14 - digitos)..], out var saida));
            cnpj.Pseudonimizar(completo, out var saidaDoCompleto);

            Assert.True(saida == saidaDoCompleto, "O CNPJ sem zeros não deu o mesmo pseudônimo do completo.");
            Assert.True(saida!.Length == 14 && ValidadorBr.Cnpj(saida), "O CNPJ sem zeros não saiu com 14 dígitos válidos.");

            var invalido = DeslocarDv(completo, 1 + sorteio.Next(99));
            Assert.Equal(Desfecho.DvInvalidoPreservado, cnpj.Pseudonimizar(invalido[(14 - digitos)..], out var saidaInvalida));
            cnpj.Pseudonimizar(invalido, out var saidaDoInvalidoCompleto);
            Assert.True(saidaInvalida == saidaDoInvalidoCompleto, "Sem zeros, o DV inválido não teve o mesmo tratamento.");
        }
    }

    [Theory]
    [InlineData(12, 2, 0)]
    [InlineData(12, 5, 3)]
    [InlineData(12, 1, 0)]
    [InlineData(13, 1, 0)]
    [InlineData(13, 4, 3)]
    [InlineData(13, 0, 0)]
    public void Zeros_acrescentados_saem_do_preenchimento_sem_alargar_o_valor(int digitos, int espacos, int espacosNaSaida)
    {
        using var cnpj = Criar();
        var sorteio = new Random(1512 + digitos);
        var raiz = new string('0', 14 - digitos) + '7' + Digitos(sorteio, digitos - 7);
        var completo = Montar(raiz, Ordem(sorteio, alfanumerica: false));
        cnpj.Pseudonimizar(completo, out var saidaDoCompleto);

        Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(completo[(14 - digitos)..] + new string(' ', espacos), out var saida));

        Assert.True(saida == saidaDoCompleto + new string(' ', espacosNaSaida), "Os zeros acrescentados não saíram do preenchimento.");
    }

    [Fact]
    public void Espacos_a_direita_sao_preservados()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1514);
        var entrada = Montar(Raiz(sorteio, alfanumerica: true), Ordem(sorteio, alfanumerica: true));
        cnpj.Pseudonimizar(entrada, out var semEspacos);

        Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(entrada + "   ", out var comEspacos));
        Assert.True(comEspacos == semEspacos + "   ", "O preenchimento à direita não foi preservado.");

        Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(MascararCnpj(entrada) + " ", out var mascaraComEspaco));
        Assert.True(mascaraComEspaco == MascararCnpj(semEspacos!) + " ", "O preenchimento da máscara não foi preservado.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CNPJ_numerico_e_completado_com_zeros_e_bate_com_o_texto(bool permutarOrdem)
    {
        using var cnpj = Criar(permutarOrdem: permutarOrdem);
        var sorteio = new Random(1515);
        for (var vez = 0; vez < 5_000; vez++)
        {
            // Raízes pequenas de propósito: CNPJs que começam com zeros, que a coluna numérica guarda com menos dígitos.
            var raiz = (vez % 2 == 0 ? 1 + sorteio.Next(9_999) : 1 + sorteio.Next(99_999_999)).ToString("00000000", CultureInfo.InvariantCulture);
            var texto = Montar(raiz, Ordem(sorteio, alfanumerica: false));

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(long.Parse(texto, CultureInfo.InvariantCulture), out var saida));
            cnpj.Pseudonimizar(texto, out var saidaTexto);

            Assert.True(Quatorze(saida!.Value) == saidaTexto, "Numérico e texto deram pseudônimos diferentes.");
        }
    }

    [Fact]
    public void Os_extremos_da_faixa_numerica_sao_CNPJ()
    {
        // Zero é a raiz zero com ordem 0000 e DV 00, montado na hora; o maior número tem DV errado (o certo começa com 6).
        using var cnpj = Criar();
        var zero = long.Parse(Montar(new string('0', 8), "0000"), CultureInfo.InvariantCulture);

        Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(zero, out var saidaDoZero));
        Assert.Equal(zero, saidaDoZero);
        Assert.Equal(Desfecho.DvInvalidoPreservado, cnpj.Pseudonimizar(99_999_999_999_999L, out var saidaDoMaior));
        Assert.InRange(saidaDoMaior!.Value, 0L, 99_999_999_999_999L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void So_a_raiz_zero_fica_igual_na_fronteira_do_prefixo(bool permutarOrdem)
    {
        // Âncoras com ordem 0000 (que a Receita não atribui) ou com letra, DV montado na hora: o último valor de raiz
        // zero (ordem 9999 ou ZZZZ) fica igual; o primeiro de fora (raiz 00000001, ordem 0000 ou 000A) e as raízes
        // 0000000d são permutados.
        using var cnpj = Criar(permutarOrdem: permutarOrdem);
        foreach (var ordem in new[] { "9999", "ZZZZ" })
        {
            var fixo = Montar(new string('0', 8), ordem);
            Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(fixo, out var saida));
            Assert.True(saida == fixo, "O último valor de raiz zero não ficou igual.");
        }

        foreach (var raiz in Enumerable.Range(1, 9).Select(d => "0000000" + (char)('0' + d)))
        {
            foreach (var ordem in new[] { "0000", "000A" })
            {
                var entrada = Montar(raiz, ordem);
                Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(entrada, out var saida));
                Assert.True(saida![..8] != raiz && saida[..8] != new string('0', 8), "Raiz fora do prefixo ficou fixa ou caiu na raiz zero.");
            }
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(100_000_000_000_000L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void CNPJ_numerico_fora_de_14_digitos_segue_a_politica_de_malformado(long numero)
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

    [Fact]
    public void Minuscula_valida_em_maiusculas_e_malformada()
    {
        // O mesmo CNPJ com uma letra em minúscula não é tratado como o maiúsculo.
        using var cnpj = Criar();
        var sorteio = new Random(1516);
        var valido = Montar(Raiz(sorteio, alfanumerica: true), Ordem(sorteio, alfanumerica: false));

        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => cnpj.Pseudonimizar(valido.ToLowerInvariant(), out _)).Codigo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("              ")]
    [InlineData("                  ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            foreach (var invalida in Enum.GetValues<PoliticaEntradaInvalida>())
            {
                using var cnpj = Criar(invalida, malformada);

                Assert.Equal(Desfecho.Vazio, cnpj.Pseudonimizar(entrada, out var saida));
                Assert.Equal(entrada, saida);
            }
        }
    }

    [Fact]
    public void DV_invalido_segue_a_politica()
    {
        var sorteio = new Random(1517);
        // Raiz com um zero à esquerda, para a forma de 13 dígitos também passar pela política de DV.
        var invalido = DeslocarDv(Montar("0" + (char)('1' + sorteio.Next(9)) + Digitos(sorteio, 6), Ordem(sorteio, alfanumerica: false)), 1);
        var alfanumerico = DeslocarDv(Montar(Raiz(sorteio, alfanumerica: true), Ordem(sorteio, alfanumerica: true)), 1);
        using var falhar = Criar(invalida: PoliticaEntradaInvalida.Falhar);
        using var anular = Criar(invalida: PoliticaEntradaInvalida.Anular);

        foreach (var forma in new[] { invalido, MascararCnpj(invalido), invalido[1..], alfanumerico, MascararCnpj(alfanumerico) })
        {
            var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(forma, out _));
            Assert.Equal("INC1004", excecao.Codigo);
            Assert.DoesNotContain(forma.TrimStart('0')[..8], excecao.Message, StringComparison.Ordinal);
            Assert.Equal(Desfecho.AnuladoDvInvalido, anular.Pseudonimizar(forma, out var anulado));
            Assert.Null(anulado);
        }

        var numero = long.Parse(invalido, CultureInfo.InvariantCulture);
        Assert.Equal("INC1004", Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(numero, out _)).Codigo);
        Assert.Equal(Desfecho.AnuladoDvInvalido, anular.Pseudonimizar(numero, out var numeroAnulado));
        Assert.Null(numeroAnulado);
    }

    [Fact]
    public void Pseudonimo_e_a_permutacao_da_raiz_na_variante_da_raiz_com_o_DV_recalculado()
    {
        // Recalcula de forma independente: chaves de domínio cnpj@1 nas variantes num e alfa, 10⁴ × 10⁴ em base 10 sem a
        // raiz zero e 36⁴ × 36⁴ em base 36 sem as raízes só com dígitos, sem ajuste, DV pela referência clássica.
        using var cnpj = Criar();
        using var numerica = Feistel(Variantes.Numerico, 10, 10_000);
        using var alfanumerica = Feistel(Variantes.Alfanumerico, 36, 36 * 36 * 36 * 36);
        var raizZero = ConjuntoExcluido.PorRegra(valor => valor == 0, 1);
        var soDigitos = ConjuntoExcluido.PorRegra(valor => !TemLetra(DeBase36(valor, 8)), 100_000_000);
        var sorteio = new Random(1518);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var raizAlfanumerica = vez % 2 == 1;
            var raiz = Raiz(sorteio, raizAlfanumerica);
            var ordem = Ordem(sorteio, alfanumerica: vez % 4 < 2);
            var raizEsperada = raizAlfanumerica
                ? DeBase36(alfanumerica.Permutar(ParaBase36(raiz), [], soDigitos), 8)
                : numerica.Permutar(ulong.Parse(raiz, CultureInfo.InvariantCulture), [], raizZero).ToString("00000000", CultureInfo.InvariantCulture);

            cnpj.Pseudonimizar(Montar(raiz, ordem), out var saida);

            Assert.True(saida == Montar(raizEsperada, ordem), "O pseudônimo não é a permutação da raiz.");
        }
    }

    [Fact]
    public void A_raiz_que_a_permutacao_levaria_a_um_excluido_caminha_ate_a_proxima_da_orbita()
    {
        // As pré-imagens cruas da raiz zero e de raízes só com dígitos precisam seguir a órbita até sair dos
        // excluídos: amostras aleatórias quase nunca caem nelas.
        using var cnpj = Criar();
        using var numerica = Feistel(Variantes.Numerico, 10, 10_000);
        using var alfanumerica = Feistel(Variantes.Alfanumerico, 36, 36 * 36 * 36 * 36);
        var ordem = Ordem(new Random(1519), alfanumerica: false);

        var preImagemDoZero = numerica.Inverter(0, []);
        Assert.NotEqual(0UL, preImagemDoZero);
        var esperadaNumerica = numerica.Permutar(0, []);
        while (esperadaNumerica == 0)
        {
            esperadaNumerica = numerica.Permutar(esperadaNumerica, []);
        }

        cnpj.Pseudonimizar(Montar(preImagemDoZero.ToString("00000000", CultureInfo.InvariantCulture), ordem), out var saidaNumerica);
        Assert.True(saidaNumerica![..8] == esperadaNumerica.ToString("00000000", CultureInfo.InvariantCulture), "A pré-imagem da raiz zero não caminhou pela órbita.");

        var caminhadas = 0;
        var sorteio = new Random(1520);
        while (caminhadas < 20)
        {
            var soDigitos = Digitos(sorteio, 8);
            var preImagem = DeBase36(alfanumerica.Inverter(ParaBase36(soDigitos), []), 8);
            if (!TemLetra(preImagem))
            {
                continue;
            }

            var esperada = ParaBase36(soDigitos);
            while (!TemLetra(DeBase36(esperada, 8)))
            {
                esperada = alfanumerica.Permutar(esperada, []);
            }

            cnpj.Pseudonimizar(Montar(preImagem, ordem), out var saida);

            Assert.True(saida![..8] == DeBase36(esperada, 8), "A pré-imagem de uma raiz só com dígitos não caminhou pela órbita.");
            caminhadas++;
        }
    }

    [Fact]
    public void Com_permutarOrdem_os_12_sao_permutados_sem_misturar_numerico_e_alfanumerico()
    {
        using var cnpj = Criar(permutarOrdem: true);
        using var semOrdem = Criar();
        var sorteio = new Random(1521);
        var saidas = new HashSet<string>();
        var ordensIguais = 0;
        for (var vez = 0; vez < 20_000; vez++)
        {
            var entrada = Montar(Raiz(sorteio, alfanumerica: vez % 3 == 0), Ordem(sorteio, alfanumerica: vez % 2 == 0));

            Assert.Equal(Desfecho.Pseudonimizado, cnpj.Pseudonimizar(entrada, out var saida));
            semOrdem.Pseudonimizar(entrada, out var saidaSemOrdem);

            Assert.True(ValidadorBr.Cnpj(saida), "CNPJ válido saiu inválido.");
            Assert.True(TemLetra(saida![..12]) == TemLetra(entrada[..12]), "A presença de letra nos 12 mudou.");
            Assert.True(saida[..8] != new string('0', 8), "Uma saída caiu na raiz zero.");
            Assert.True(saida != saidaSemOrdem, "permutarOrdem deu o mesmo pseudônimo da raiz.");
            ordensIguais += saida[8..12] == entrada[8..12] ? 1 : 0;
            Assert.True(saidas.Add(saida), "Dois CNPJs deram o mesmo pseudônimo.");
        }

        Assert.True(ordensIguais < 100, "A ordem quase nunca mudou com permutarOrdem.");
    }

    [Fact]
    public void Com_permutarOrdem_a_raiz_zero_com_qualquer_ordem_continua_igual()
    {
        using var cnpj = Criar(permutarOrdem: true);
        var sorteio = new Random(1522);
        for (var vez = 0; vez < 200; vez++)
        {
            var entrada = Montar(new string('0', 8), Ordem(sorteio, alfanumerica: vez % 2 == 1));

            Assert.Equal(Desfecho.PontoFixo, cnpj.Pseudonimizar(entrada, out var saida));
            Assert.True(saida == entrada, "CNPJ de raiz zero mudou com permutarOrdem.");
        }
    }

    [Fact]
    public void Com_permutarOrdem_o_pseudonimo_e_a_permutacao_dos_12_na_variante_ordem()
    {
        // Recalcula de forma independente: chave de domínio cnpj@1 na variante ordem nas duas partes, 10⁶ × 10⁶ sem as
        // raízes zero e 36⁶ × 36⁶ sem as cadeias só com dígitos nem as de raiz zero.
        using var cnpj = Criar(permutarOrdem: true);
        using var numerica = Feistel(Variantes.Ordem, 10, 1_000_000);
        using var alfanumerica = Feistel(Variantes.Ordem, 36, 36UL * 36 * 36 * 36 * 36 * 36);
        // A raiz zero, com qualquer ordem, fica fora das duas partes: valores abaixo de 10⁴ e de 36⁴.
        var raizZero = ConjuntoExcluido.PorRegra(valor => valor < 10_000, 10_000);
        var soDigitosOuRaizZero = ConjuntoExcluido.PorRegra(
            valor => valor < 1_679_616 || !TemLetra(DeBase36(valor, 12)), 1_000_000_000_000 + 1_679_616 - 10_000);
        var sorteio = new Random(1523);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var baseCnpj = Raiz(sorteio, alfanumerica: vez % 3 == 0) + Ordem(sorteio, alfanumerica: vez % 2 == 0);
            var baseEsperada = TemLetra(baseCnpj)
                ? DeBase36(alfanumerica.Permutar(ParaBase36(baseCnpj), [], soDigitosOuRaizZero), 12)
                : numerica.Permutar(ulong.Parse(baseCnpj, CultureInfo.InvariantCulture), [], raizZero).ToString("000000000000", CultureInfo.InvariantCulture);

            cnpj.Pseudonimizar(Montar(baseCnpj[..8], baseCnpj[8..]), out var saida);

            Assert.True(saida == Montar(baseEsperada[..8], baseEsperada[8..]), "O pseudônimo não é a permutação dos 12.");
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_versao_ou_permutarOrdem_diferente_nao()
    {
        var sorteio = new Random(1524);
        var entrada = Montar(Raiz(sorteio, alfanumerica: true), Ordem(sorteio, alfanumerica: false));
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "cnpj-fornecedor");
        using var outraVersao = Criar(versao: 2);
        using var comOrdem = Criar(permutarOrdem: true);

        primeiro.Pseudonimizar(entrada, out var a);
        segundo.Pseudonimizar(entrada, out var b);
        outroAmbiente.Pseudonimizar(entrada, out var c);
        outroDominio.Pseudonimizar(entrada, out var d);
        outraVersao.Pseudonimizar(entrada, out var e);
        comOrdem.Pseudonimizar(entrada, out var f);

        Assert.True(a == b, "A mesma configuração deu pseudônimos diferentes.");
        Assert.True(a != c && a != d && a != e && a != f, "Ambiente, domínio, versão ou permutarOrdem diferente deu o mesmo pseudônimo.");
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var cnpj = Criar();
        var sorteio = new Random(1525);
        var entradas = Enumerable.Range(0, 100_000)
            .Select(i => Montar(Raiz(sorteio, alfanumerica: i % 2 == 0), Ordem(sorteio, alfanumerica: false)))
            .ToArray();
        var sequencial = entradas.Select(entrada => { cnpj.Pseudonimizar(entrada, out var saida); return saida; }).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => cnpj.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum pseudônimo.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var cnpj = Criar(malformada: PoliticaEntradaMalformada.Preservar);
        var sorteio = new Random(1526);
        var valido = Montar(Raiz(sorteio, alfanumerica: false), Ordem(sorteio, alfanumerica: false));
        var raizZero = Montar(new string('0', 8), Ordem(sorteio, alfanumerica: false));
        cnpj.Dispose();
        cnpj.Dispose();

        foreach (var entrada in new[] { valido, raizZero, "malformado", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => cnpj.Pseudonimizar(entrada, out _));
        }

        Assert.Throws<ObjectDisposedException>(() => cnpj.Pseudonimizar(long.Parse(valido, CultureInfo.InvariantCulture), out _));
        Assert.Throws<ObjectDisposedException>(() => cnpj.Pseudonimizar(-1L, out _));
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
        using var cnpj = Criar();

        Assert.Throws<ArgumentNullException>(() => cnpj.Pseudonimizar((string)null!, out _));
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorCnpj Criar(
        PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Preservar,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "cnpj",
        int versao = 1,
        bool permutarOrdem = false) =>
        new(_chave, dominio, versao, ambiente, permutarOrdem, invalida, malformada);

    private PermutacaoFeistel Feistel(string variante, uint radix, ulong lado)
    {
        var chaveDeDominio = new byte[32];
        Subchaves.DerivarChaveDeDominio(_chave, "cnpj", 1, variante, "homologacao", chaveDeDominio);
        return new PermutacaoFeistel(chaveDeDominio, radix, lado, lado);
    }

    // Raiz de 8 caracteres: só com dígitos e diferente de zero, ou com ao menos uma letra.
    private static string Raiz(Random sorteio, bool alfanumerica)
    {
        string raiz;
        do
        {
            var alfabeto = alfanumerica ? Alfanumerico : SoDigitosTexto;
            raiz = new string(Enumerable.Range(0, 8).Select(_ => alfabeto[sorteio.Next(alfabeto.Length)]).ToArray());
        }
        while (alfanumerica ? !TemLetra(raiz) : raiz == new string('0', 8));

        return raiz;
    }

    // Ordem de 4 caracteres; a alfanumérica tem ao menos uma letra.
    private static string Ordem(Random sorteio, bool alfanumerica)
    {
        string ordem;
        do
        {
            var alfabeto = alfanumerica ? Alfanumerico : SoDigitosTexto;
            ordem = new string(Enumerable.Range(0, 4).Select(_ => alfabeto[sorteio.Next(alfabeto.Length)]).ToArray());
        }
        while (alfanumerica && !TemLetra(ordem));

        return ordem;
    }

    private static string Montar(string raiz, string ordem) =>
        raiz + ordem + DvCnpjClassico(raiz + ordem).ToString("00", CultureInfo.InvariantCulture);

    private static string DeslocarDv(string cnpj, int deslocamento) =>
        cnpj[..12] + ((int.Parse(cnpj[12..], CultureInfo.InvariantCulture) + deslocamento) % 100).ToString("00", CultureInfo.InvariantCulture);

    private static string Quatorze(long numero) => numero.ToString("00000000000000", CultureInfo.InvariantCulture);

    private static bool TemLetra(string texto) => texto.Any(char.IsAsciiLetterUpper);

    // Base 36 escrita à parte da implementação: o valor de cada símbolo é a posição no alfabeto 0-9A-Z.
    private static ulong ParaBase36(string texto) =>
        texto.Aggregate(0UL, (valor, simbolo) => (valor * 36) + (ulong)Alfanumerico.IndexOf(simbolo, StringComparison.Ordinal));

    private static string DeBase36(ulong valor, int tamanho)
    {
        var simbolos = new char[tamanho];
        for (var i = tamanho - 1; i >= 0; i--)
        {
            simbolos[i] = Alfanumerico[(int)(valor % 36)];
            valor /= 36;
        }

        return new string(simbolos);
    }
}
