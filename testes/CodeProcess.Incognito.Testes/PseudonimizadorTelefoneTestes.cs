using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio telefone: DDD e classe preservados, bijeção dentro de cada classe com o DDD como ajuste, formatação
/// preservada e conferida, número sem DDD, código do país e 0 de tronco, celular antigo como classe própria, números
/// de serviço preservados e a política de malformado. Todo número de forma válida é sorteado na hora (os de serviço com
/// o prefixo fixo e o resto sorteado ou nas bordas da faixa); as teorias recebem só o nome do caso, para nenhum número
/// aparecer na saída; a chave é a pública do vetor de derivação.
/// </summary>
public sealed class PseudonimizadorTelefoneTestes : IDisposable
{
    private readonly ChaveIncognito _chave = ChaveDoVetor();

    // Sem forma de telefone e sem número que possa ser de alguém: podem ficar escritos.
    public static TheoryData<string> MalformadosEscritos =>
    [
        "1234567",
        "123456789012345",
        "()- .",
        "1 1 9 1 2 3 4 5 6 7 8 9 0 1 2 3 4",
        "08001234",
        "07001234567",
    ];

    // Montados na hora a partir de DDD e números sorteados; o nome descreve o defeito.
    public static TheoryData<string> MalformadosMontados =>
    [
        "mais sem o 55",
        "mais no meio",
        "mais com 0800",
        "mais com 3003",
        "13 digitos sem o 55",
        "12 digitos sem o 55 nem o 0",
        "DDD com zero no primeiro",
        "DDD com zero no segundo",
        "celular de 9 sem o 9",
        "fixo comecando com 1",
        "fixo comecando com 0",
        "letra no numero",
        "tab no meio",
        "tab no fim",
        "barra",
        "digitos nao ASCII",
        "espaco a esquerda",
        "separador dentro do DDD",
        "separador dentro do 55",
        "separador entre o 0 e o DDD",
        "parenteses em volta do numero",
        "parenteses em volta do 55",
        "parentese so de abrir",
        "dois pares de parenteses",
        "fixo com ramal",
        "fixo com DDD e ramal",
        "celular de 7 digitos com DDD entre parenteses",
        "fixo em grupos de 3 e 5",
        "celular em grupos de 1 e 8",
        "celular em grupos de 4 e 5",
        "celular com separador depois do segundo digito",
        "dois separadores dentro do numero",
        "hifen no fim",
        "33 caracteres",
        "parece 0800 sem o zero",
        "parentese abrindo antes do 55",
        "espaco antes de fechar o parentese",
        "parentese de abrir repetido",
        "separador dentro do DDD com o 0",
    ];

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void DDD_e_classe_sao_preservados_em_todas_as_formas()
    {
        using var telefone = Criar();
        var sorteio = new Random(1601);
        for (var vez = 0; vez < 9_000; vez++)
        {
            var ddd = Ddd(sorteio);
            var classe = vez % 3;
            var numero = Numero(sorteio, classe);
            var entrada = Formatar(sorteio, vez % 7, ddd, numero);

            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(entrada, out var saida));
            Assert.True(SoSeparadores(saida!) == SoSeparadores(entrada), "A formatação não foi preservada.");
            var digitos = SoDigitos(saida!);
            var numeroDeSaida = digitos[^numero.Length..];
            Assert.True(digitos[..^numero.Length] == SoDigitos(entrada)[..^numero.Length], "O DDD, o 55 ou o 0 mudou.");
            Assert.True(DaClasse(numeroDeSaida, classe), "A classe mudou.");
            Assert.True(numeroDeSaida != numero, "O número foi copiado em claro.");
        }
    }

    [Fact]
    public void Bijecao_dentro_de_cada_classe_com_o_mesmo_DDD()
    {
        using var telefone = Criar();
        var sorteio = new Random(1602);
        var ddd = Ddd(sorteio);
        for (var classe = 0; classe < 3; classe++)
        {
            var numeros = new HashSet<string>();
            while (numeros.Count < Mutacao.Tamanho(200_000, 10_000))
            {
                numeros.Add(Numero(sorteio, classe));
            }

            var saidas = new HashSet<string>();
            foreach (var numero in numeros)
            {
                telefone.Pseudonimizar(ddd + numero, out var saida);
                saidas.Add(saida!);
            }

            Assert.Equal(numeros.Count, saidas.Count);
        }
    }

    [Fact]
    public void Pseudonimo_e_a_permutacao_da_classe_com_o_DDD_como_ajuste()
    {
        // Recalcula de forma independente: chaves de domínio telefone@1 nas variantes cel e fixo; celular sobre os 8
        // dígitos depois do 9 (10⁴ × 10⁴); celular antigo sobre (primeiro − 6)·10⁷ + os outros 7 com a chave cel
        // (4000 × 10⁴); fixo sobre (primeiro − 2)·10⁷ + os outros 7 (4000 × 10⁴) sem os prefixos 3003 e 4004; ajuste =
        // os dois dígitos do DDD em ASCII, ou vazio sem DDD.
        using var telefone = Criar();
        using var celular = Feistel(Variantes.Celular, 10_000);
        using var celularAntigo = Feistel(Variantes.Celular, 4_000);
        using var fixo = Feistel(Variantes.Fixo, 4_000);
        var servicos = ConjuntoExcluido.PorRegra(valor => EhPrefixoDeServico(NumeroComBase(valor, '2')), 20_000);
        var sorteio = new Random(1603);
        for (var vez = 0; vez < 6_000; vez++)
        {
            var ddd = vez % 4 == 3 ? string.Empty : Ddd(sorteio);
            var ajuste = Encoding.ASCII.GetBytes(ddd);
            var numero = Numero(sorteio, vez % 3);
            var esperado = (vez % 3) switch
            {
                0 => "9" + celular.Permutar(ulong.Parse(numero[1..], CultureInfo.InvariantCulture), ajuste).ToString("00000000", CultureInfo.InvariantCulture),
                1 => NumeroComBase(fixo.Permutar(ValorComBase(numero, '2'), ajuste, servicos), '2'),
                _ => NumeroComBase(celularAntigo.Permutar(ValorComBase(numero, '6'), ajuste), '6'),
            };

            telefone.Pseudonimizar(ddd + numero, out var saida);

            Assert.True(saida == ddd + esperado, "O pseudônimo não é a permutação da classe.");
        }
    }

    [Fact]
    public void O_55_e_o_0_de_tronco_nao_mudam_o_pseudonimo_e_o_DDD_muda()
    {
        using var telefone = Criar();
        var sorteio = new Random(1604);
        var diferentes = 0;
        for (var vez = 0; vez < 1_500; vez++)
        {
            var numero = Numero(sorteio, vez % 3);
            var ddd = Ddd(sorteio);
            var outroDdd = ddd == "11" ? "21" : "11";

            telefone.Pseudonimizar(ddd + numero, out var comDdd);
            telefone.Pseudonimizar("55" + ddd + numero, out var comPais);
            telefone.Pseudonimizar("+55 (" + ddd + ") " + numero, out var comMais);
            telefone.Pseudonimizar("0" + ddd + numero, out var comTronco);
            telefone.Pseudonimizar("(0" + ddd + ") " + numero, out var comTroncoEntreParenteses);
            telefone.Pseudonimizar(outroDdd + numero, out var comOutroDdd);
            telefone.Pseudonimizar(numero, out var semDdd);

            var pseudonimo = comDdd![2..];
            Assert.True(comPais == "55" + comDdd && comMais == "+55 (" + ddd + ") " + pseudonimo, "O código do país mudou o pseudônimo.");
            Assert.True(comTronco == "0" + comDdd && comTroncoEntreParenteses == "(0" + ddd + ") " + pseudonimo, "O 0 de tronco mudou o pseudônimo.");
            diferentes += pseudonimo != comOutroDdd![2..] && pseudonimo != semDdd ? 1 : 0;
        }

        Assert.True(diferentes > 1_490, "O DDD não está mudando o número.");
    }

    [Fact]
    public void Numero_sem_DDD_e_pseudonimizado_na_mesma_classe()
    {
        using var telefone = Criar();
        var sorteio = new Random(1605);
        for (var vez = 0; vez < 6_000; vez++)
        {
            var classe = vez % 3;
            var numero = Numero(sorteio, classe);
            var entrada = vez % 2 == 0 ? numero : numero[..^4] + "-" + numero[^4..];

            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(entrada, out var saida));
            Assert.True(DaClasse(SoDigitos(saida!), classe), "Sem DDD, a classe mudou.");
            Assert.True(SoSeparadores(saida!) == SoSeparadores(entrada), "Sem DDD, a formatação mudou.");
        }
    }

    [Fact]
    public void Celular_antigo_fica_com_8_digitos_e_nao_colide_com_o_celular_atual()
    {
        // Uma linha antiga e a atual com os mesmos 8 dígitos podem ser de pessoas diferentes (número reatribuído):
        // as saídas ficam em classes diferentes, nunca coincidem, e o valor não fica mais largo.
        using var telefone = Criar();
        var sorteio = new Random(1606);
        for (var vez = 0; vez < 4_000; vez++)
        {
            var antigo = CelularAntigo(sorteio);
            var ddd = vez % 3 == 2 ? string.Empty : Ddd(sorteio);

            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(ddd + antigo, out var saidaAntiga));
            telefone.Pseudonimizar(ddd + "9" + antigo, out var saidaAtual);

            Assert.True(saidaAntiga!.Length == ddd.Length + 8 && EhCelularAntigo(saidaAntiga[ddd.Length..]), "O celular antigo mudou de classe ou de tamanho.");
            Assert.True(saidaAntiga != saidaAtual![..^9] + saidaAtual[^8..], "O celular antigo saiu igual ao atual sem o 9.");
        }
    }

    [Fact]
    public void Numeros_de_servico_voltam_como_vieram()
    {
        using var telefone = Criar();
        var sorteio = new Random(1608);
        foreach (var prefixo in new[] { "0800", "0300", "0303", "0500", "0900" })
        {
            var numero = prefixo + Digitos(sorteio, 7);
            foreach (var entrada in new[] { numero, prefixo + " " + numero[4..7] + " " + numero[7..], prefixo + "-" + numero[4..7] + "-" + numero[7..], "55" + numero, "+55 " + numero })
            {
                Assert.Equal(Desfecho.ServicoPreservado, telefone.Pseudonimizar(entrada, out var saida));
                Assert.Equal(entrada, saida);
            }
        }

        foreach (var prefixo in new[] { "3003", "4004" })
        {
            foreach (var final in new[] { "0000", "9999", Digitos(sorteio, 4) })
            {
                var numero = prefixo + final;
                var ddd = Ddd(sorteio);
                foreach (var entrada in new[] { numero, prefixo + "-" + final, ddd + numero, "0" + ddd + numero, "+55 (" + ddd + ") " + prefixo + "-" + final })
                {
                    Assert.Equal(Desfecho.ServicoPreservado, telefone.Pseudonimizar(entrada, out var saida));
                    Assert.Equal(entrada, saida);
                }
            }
        }
    }

    [Fact]
    public void Fixos_nas_bordas_dos_prefixos_de_servico_sao_permutados_e_nenhum_fixo_vira_servico()
    {
        using var telefone = Criar();
        using var fixo = Feistel(Variantes.Fixo, 4_000);
        var sorteio = new Random(1609);
        foreach (var numero in new[] { "30029999", "30040000", "40039999", "40050000", "2003" + Digitos(sorteio, 4), "5004" + Digitos(sorteio, 4) })
        {
            var ddd = Ddd(sorteio);
            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(ddd + numero, out var saida));
            Assert.False(EhPrefixoDeServico(saida![2..]), "Um fixo virou número de serviço.");
            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(long.Parse(ddd + numero, CultureInfo.InvariantCulture), out _));
        }

        // As pré-imagens cruas de números de serviço precisam caminhar até um fixo comum.
        var dddDaCaminhada = Ddd(sorteio);
        var ajuste = Encoding.ASCII.GetBytes(dddDaCaminhada);
        var caminhadas = 0;
        while (caminhadas < 20)
        {
            var servico = (sorteio.Next(2) == 0 ? "3003" : "4004") + Digitos(sorteio, 4);
            var preImagem = NumeroComBase(fixo.Inverter(ValorComBase(servico, '2'), ajuste), '2');
            if (EhPrefixoDeServico(preImagem))
            {
                continue;
            }

            var esperada = ValorComBase(servico, '2');
            while (EhPrefixoDeServico(NumeroComBase(esperada, '2')))
            {
                esperada = fixo.Permutar(esperada, ajuste);
            }

            telefone.Pseudonimizar(dddDaCaminhada + preImagem, out var saida);

            Assert.True(saida == dddDaCaminhada + NumeroComBase(esperada, '2'), "A pré-imagem de um número de serviço não caminhou pela órbita.");
            caminhadas++;
        }
    }

    [Fact]
    public void Telefone_numerico_bate_com_o_texto()
    {
        using var telefone = Criar();
        var sorteio = new Random(1610);
        for (var vez = 0; vez < 6_000; vez++)
        {
            var texto = (vez % 6) switch
            {
                0 => Ddd(sorteio) + Celular(sorteio),
                1 => "55" + Ddd(sorteio) + Fixo(sorteio),
                2 => "55" + Ddd(sorteio) + Celular(sorteio),
                3 => Ddd(sorteio) + CelularAntigo(sorteio),
                4 => "55" + Ddd(sorteio) + CelularAntigo(sorteio),
                _ => vez % 4 == 1 ? CelularAntigo(sorteio) : Fixo(sorteio),
            };

            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(long.Parse(texto, CultureInfo.InvariantCulture), out var saida));
            telefone.Pseudonimizar(texto, out var saidaTexto);

            Assert.True(saida!.Value.ToString(CultureInfo.InvariantCulture) == saidaTexto, "Numérico e texto deram pseudônimos diferentes.");
        }

        foreach (var servico in new[] { "3003" + Digitos(sorteio, 4), Ddd(sorteio) + "4004" + Digitos(sorteio, 4), "55" + "0800" + Digitos(sorteio, 7) })
        {
            var numero = long.Parse(servico, CultureInfo.InvariantCulture);
            Assert.Equal(Desfecho.ServicoPreservado, telefone.Pseudonimizar(numero, out var saidaDeServico));
            Assert.Equal(numero, saidaDeServico);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("0000000")]
    [InlineData("00000000")]
    [InlineData("0000000000000")]
    [InlineData("+55 (00) 0000-0000")]
    [InlineData("55 00 00000-0000")]
    [InlineData("(11) 0000-0000")]
    [InlineData("(21) 00000-0000")]
    [InlineData("0 11 0000-0000")]
    [InlineData("(011) 00000-0000")]
    [InlineData("+55 11 00000-0000")]
    [InlineData("00                     0000-0000")]
    [InlineData("(00) 0000-0000")]
    [InlineData("(00) 00000-0000")]
    [InlineData("0000-0000")]
    [InlineData("00.0000.0000")]
    [InlineData("000 000 000 000")]
    [InlineData("()0000+0000")]
    public void Telefone_so_com_zeros_volta_como_veio_em_toda_politica(string zerado)
    {
        foreach (var politica in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            using var telefone = Criar(politica);
            foreach (var entrada in new[] { zerado, zerado + "   " })
            {
                Assert.Equal(Desfecho.Zerado, telefone.Pseudonimizar(entrada, out var saida));
                Assert.Equal(entrada, saida);
            }

            Assert.Equal(Desfecho.Zerado, telefone.Pseudonimizar(0L, out var numero));
            Assert.Equal(0L, numero);
        }
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("1 0000-0000")]
    [InlineData("12 0000-000")]
    [InlineData("56 11 0000-0000")]
    [InlineData("56 11 00000-0000")]
    [InlineData("00                      0000-0000")]
    [InlineData("0000000x")]
    [InlineData("0000\t0000")]
    [InlineData("0000/0000")]
    [InlineData("000000001")]
    [InlineData("(00) 0000-0001")]
    public void Zeros_fora_do_tamanho_com_outro_caractere_ou_com_outro_digito_nao_sao_zerado(string entrada)
    {
        using var telefone = Criar();

        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => telefone.Pseudonimizar(entrada, out _)).Codigo);
    }

    [Theory]
    [InlineData("(11) 90000-0000")]
    [InlineData("(11) 3000-0000")]
    [InlineData("+55 11 90000-0000")]
    public void Numero_com_um_digito_diferente_de_zero_e_pseudonimizado(string entrada)
    {
        using var telefone = Criar();

        Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(entrada, out var saida));
        Assert.NotEqual(entrada, saida);
    }

    [Theory]
    [InlineData(1_100_000_000L)]
    [InlineData(11_000_000_000L)]
    [InlineData(5_511_000_000_000L)]
    [InlineData(5_500_000_000_000L)]
    public void Numero_com_o_numero_do_assinante_zerado_volta_como_veio(long numero)
    {
        using var telefone = Criar();

        Assert.Equal(Desfecho.Zerado, telefone.Pseudonimizar(numero, out var saida));
        Assert.Equal(numero, saida);
    }

    [Theory]
    [InlineData("0800 123456")]
    [InlineData("0800123456")]
    [InlineData("0300-123456")]
    [InlineData("+55 0500 123456")]
    [InlineData("550900123456")]
    public void Codigo_nao_geografico_antigo_de_10_digitos_volta_como_veio(string entrada)
    {
        using var telefone = Criar();

        Assert.Equal(Desfecho.ServicoPreservado, telefone.Pseudonimizar(entrada, out var saida));
        Assert.Equal(entrada, saida);
    }

    [Theory]
    [InlineData(800_123_456L, true)]
    [InlineData(300_123_456L, true)]
    [InlineData(303_123_456L, true)]
    [InlineData(500_123_456L, true)]
    [InlineData(900_123_456L, false)]
    [InlineData(55_0303_123_4567L, true)]
    public void Codigo_nao_geografico_numerico_antigo_e_com_55(long numero, bool servico)
    {
        // Com 9 dígitos, 900 seria um celular sem DDD.
        using var telefone = Criar();

        Assert.Equal(servico ? Desfecho.ServicoPreservado : Desfecho.Pseudonimizado, telefone.Pseudonimizar(numero, out var saida));
        Assert.Equal(servico, saida == numero);
    }

    [Theory]
    [InlineData("800")]
    [InlineData("300")]
    [InlineData("303")]
    [InlineData("500")]
    [InlineData("900")]
    public void Servico_numerico_que_perdeu_o_zero_volta_como_veio(string prefixo)
    {
        // 0800 e afins guardados como número: 10 dígitos que, como texto, seriam DDD com zero.
        using var telefone = Criar();
        var numero = long.Parse(prefixo + Digitos(new Random(1615), 7), CultureInfo.InvariantCulture);

        Assert.Equal(Desfecho.ServicoPreservado, telefone.Pseudonimizar(numero, out var saida));
        Assert.Equal(numero, saida);
        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => telefone.Pseudonimizar(numero.ToString(CultureInfo.InvariantCulture), out _)).Codigo);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1_234_567L)]
    [InlineData(12_345_678_901_234L)]
    [InlineData(7_001_234_567L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Telefone_numerico_fora_da_forma_segue_a_politica(long numero)
    {
        using var falhar = Criar();
        using var anular = Criar(PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(PoliticaEntradaMalformada.Preservar);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(numero, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(numero.ToString(CultureInfo.InvariantCulture), excecao.Message, StringComparison.Ordinal);

        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(numero, out var anulado));
        Assert.Null(anulado);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(numero, out var copiado));
        Assert.Equal(numero, copiado);
    }

    [Theory]
    [MemberData(nameof(MalformadosEscritos))]
    public void Entrada_escrita_malformada_segue_a_politica(string entrada) => ConferirMalformado(entrada);

    [Theory]
    [MemberData(nameof(MalformadosMontados))]
    public void Entrada_montada_malformada_segue_a_politica(string caso) => ConferirMalformado(Montar(caso, new Random(1616)));

    [Fact]
    public void Formatacao_de_32_caracteres_e_aceita()
    {
        using var telefone = Criar();
        var sorteio = new Random(1617);
        var entrada = "(" + Ddd(sorteio) + ")" + new string(' ', 19) + Celular(sorteio);

        Assert.Equal(32, entrada.Length);
        Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(entrada, out _));
    }

    [Fact]
    public void Espacos_a_direita_sao_preservados_mesmo_em_char_longo()
    {
        using var telefone = Criar();
        var sorteio = new Random(1611);
        var entrada = "(" + Ddd(sorteio) + ") " + Celular(sorteio);
        telefone.Pseudonimizar(entrada, out var semEspacos);

        foreach (var espacos in new[] { 3, 40 })
        {
            Assert.Equal(Desfecho.Pseudonimizado, telefone.Pseudonimizar(entrada + new string(' ', espacos), out var comEspacos));
            Assert.True(comEspacos == semEspacos + new string(' ', espacos), "O preenchimento à direita não foi preservado.");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("               ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            using var telefone = Criar(malformada);

            Assert.Equal(Desfecho.Vazio, telefone.Pseudonimizar(entrada, out var saida));
            Assert.Equal(entrada, saida);
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_ou_versao_diferente_nao_em_toda_classe()
    {
        var sorteio = new Random(1612);
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "telefone-contato");
        using var outraVersao = Criar(versao: 2);
        for (var classe = 0; classe < 3; classe++)
        {
            var entrada = Ddd(sorteio) + Numero(sorteio, classe);
            primeiro.Pseudonimizar(entrada, out var a);
            segundo.Pseudonimizar(entrada, out var b);
            outroAmbiente.Pseudonimizar(entrada, out var c);
            outroDominio.Pseudonimizar(entrada, out var d);
            outraVersao.Pseudonimizar(entrada, out var e);

            Assert.True(a == b, "A mesma configuração deu pseudônimos diferentes.");
            Assert.True(a != c && a != d && a != e, "Ambiente, domínio ou versão diferente deu o mesmo pseudônimo.");
        }
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var telefone = Criar();
        var sorteio = new Random(1613);
        var entradas = Enumerable.Range(0, Mutacao.Tamanho(100_000, 5_000)).Select(i => Ddd(sorteio) + Numero(sorteio, i % 3)).ToArray();
        var sequencial = entradas.Select(entrada => { telefone.Pseudonimizar(entrada, out var saida); return saida; }).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => telefone.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum pseudônimo.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var telefone = Criar(PoliticaEntradaMalformada.Preservar);
        var sorteio = new Random(1614);
        var valido = Ddd(sorteio) + Celular(sorteio);
        telefone.Dispose();
        telefone.Dispose();

        foreach (var entrada in new[] { valido, "0800" + Digitos(sorteio, 7), "malformado", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => telefone.Pseudonimizar(entrada, out _));
        }

        Assert.Throws<ObjectDisposedException>(() => telefone.Pseudonimizar(long.Parse(valido, CultureInfo.InvariantCulture), out _));
        Assert.Throws<ObjectDisposedException>(() => telefone.Pseudonimizar(-1L, out _));
    }

    [Fact]
    public void Politica_fora_das_definidas_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar((PoliticaEntradaMalformada)7));
    }

    [Fact]
    public void Entrada_nula_e_recusada()
    {
        using var telefone = Criar();

        Assert.Throws<ArgumentNullException>(() => telefone.Pseudonimizar((string)null!, out _));
    }

    private void ConferirMalformado(string entrada)
    {
        using var falhar = Criar();
        using var anular = Criar(PoliticaEntradaMalformada.Anular);
        using var preservar = Criar(PoliticaEntradaMalformada.Preservar);

        var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _));
        Assert.Equal("INC1003", excecao.Codigo);
        Assert.DoesNotContain(entrada.Trim(), excecao.Message, StringComparison.Ordinal);

        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out var anulado));
        Assert.Null(anulado);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(entrada, out var copiado));
        Assert.Equal(entrada, copiado);
    }

    // Cada caso parte de um DDD e de números sorteados; nenhum tem a forma aceita.
    private static string Montar(string caso, Random sorteio)
    {
        var ddd = Ddd(sorteio);
        var celular = Celular(sorteio);
        var fixo = Fixo(sorteio);
        return caso switch
        {
            "mais sem o 55" => "+" + ddd + celular,
            "mais no meio" => "55+" + ddd + celular,
            "mais com 0800" => "+0800" + Digitos(sorteio, 7),
            "mais com 3003" => "+3003-" + Digitos(sorteio, 4),
            "13 digitos sem o 55" => "54" + ddd + celular,
            "12 digitos sem o 55 nem o 0" => "54" + ddd + fixo,
            "DDD com zero no primeiro" => "0" + ddd[1] + fixo,
            "DDD com zero no segundo" => ddd[0] + "0" + celular,
            "celular de 9 sem o 9" => ddd + "8" + celular[1..],
            "fixo comecando com 1" => ddd + "1" + fixo[1..],
            "fixo comecando com 0" => ddd + " 0" + fixo[1..],
            "letra no numero" => ddd + " " + celular[..4] + "A-" + celular[5..],
            "tab no meio" => ddd + "\t" + celular,
            "tab no fim" => ddd + celular + "\t",
            "barra" => ddd + "/" + celular,
            "digitos nao ASCII" => new string((ddd + celular).Select(c => (char)('٠' + (c - '0'))).ToArray()),
            "espaco a esquerda" => " " + ddd + celular,
            "separador dentro do DDD" => ddd[0] + " " + ddd[1] + " " + celular,
            "separador dentro do 55" => "5 5 " + ddd + celular,
            "separador entre o 0 e o DDD" => "0 " + ddd + " " + fixo,
            "parenteses em volta do numero" => ddd + " (" + celular + ")",
            "parenteses em volta do 55" => "(55) " + ddd + celular,
            "parentese so de abrir" => "(" + ddd + " " + celular,
            "dois pares de parenteses" => "((" + ddd + ")) " + celular,
            "fixo com ramal" => fixo[..4] + "-" + fixo[4..] + " " + Digitos(sorteio, 2),
            "fixo com DDD e ramal" => "(" + ddd + ") " + fixo[..4] + "-" + fixo[4..] + " " + Digitos(sorteio, 2),
            "celular de 7 digitos com DDD entre parenteses" => "(9" + ddd[1] + ") " + fixo[1..4] + "-" + fixo[4..],
            "fixo em grupos de 3 e 5" => ddd + " " + fixo[..3] + "-" + fixo[3..],
            "celular em grupos de 1 e 8" => ddd + " 9 " + celular[1..],
            "celular em grupos de 4 e 5" => ddd + " " + celular[..4] + "-" + celular[4..],
            "celular com separador depois do segundo digito" => "(" + ddd + ") " + celular[..2] + " " + celular[2..5] + "-" + celular[5..],
            "dois separadores dentro do numero" => ddd + " " + celular[..5] + "--" + celular[5..],
            "hifen no fim" => ddd + celular + "-",
            "33 caracteres" => "(" + ddd + ")" + new string(' ', 20) + celular,
            "parece 0800 sem o zero" => ddd[0] + "800" + Digitos(sorteio, 7),
            "parentese abrindo antes do 55" => "(55 " + ddd + ") " + celular,
            "espaco antes de fechar o parentese" => "(" + ddd + " ) " + celular,
            "parentese de abrir repetido" => "(" + ddd + ") (" + celular,
            "separador dentro do DDD com o 0" => "0" + ddd[0] + " " + ddd[1] + " " + fixo,
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorTelefone Criar(
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "telefone",
        int versao = 1) =>
        new(_chave, dominio, versao, ambiente, malformada);

    private PermutacaoFeistel Feistel(string variante, ulong a)
    {
        var chaveDeDominio = new byte[32];
        Subchaves.DerivarChaveDeDominio(_chave, "telefone", 1, variante, "homologacao", chaveDeDominio);
        return new PermutacaoFeistel(chaveDeDominio, 10, a, 10_000);
    }

    private static string Ddd(Random sorteio) => new([(char)('1' + sorteio.Next(9)), (char)('1' + sorteio.Next(9))]);

    // Classe 0: celular; 1: fixo; 2: celular antigo.
    private static string Numero(Random sorteio, int classe) =>
        classe switch { 0 => Celular(sorteio), 1 => Fixo(sorteio), _ => CelularAntigo(sorteio) };

    private static bool DaClasse(string numero, int classe) =>
        classe switch { 0 => EhCelular(numero), 1 => EhFixo(numero), _ => EhCelularAntigo(numero) };

    private static string Celular(Random sorteio) => "9" + Digitos(sorteio, 8);

    private static string CelularAntigo(Random sorteio) => (char)('6' + sorteio.Next(4)) + Digitos(sorteio, 7);

    private static string Fixo(Random sorteio)
    {
        string fixo;
        do
        {
            fixo = (char)('2' + sorteio.Next(4)) + Digitos(sorteio, 7);
        }
        while (EhPrefixoDeServico(fixo));

        return fixo;
    }

    // Formas aceitas de escrever DDD e número: colado, com parênteses e hífen, com espaço, com o país, com pontos, com o
    // 0 de tronco e, no celular de 9, em 1, 4 e 4.
    private static string Formatar(Random sorteio, int forma, string ddd, string numero) => forma switch
    {
        0 => ddd + numero,
        1 => "(" + ddd + ") " + numero[..^4] + "-" + numero[^4..],
        2 => ddd + " " + numero,
        3 => (sorteio.Next(2) == 0 ? "+55 " : "55") + ddd + numero,
        4 => ddd + "." + numero[..^4] + "." + numero[^4..],
        5 => (sorteio.Next(2) == 0 ? "0" + ddd + " " : "(0" + ddd + ") ") + numero,
        _ => numero.Length == 9 ? ddd + " " + numero[..1] + " " + numero[1..5] + " " + numero[5..] : "+55 (" + ddd + ")" + numero,
    };

    private static bool EhCelular(string numero) => numero.Length == 9 && numero[0] == '9';

    private static bool EhCelularAntigo(string numero) => numero.Length == 8 && numero[0] is >= '6' and <= '9';

    private static bool EhFixo(string numero) => numero.Length == 8 && numero[0] is >= '2' and <= '5' && !EhPrefixoDeServico(numero);

    private static bool EhPrefixoDeServico(string numero) =>
        numero.StartsWith("3003", StringComparison.Ordinal) || numero.StartsWith("4004", StringComparison.Ordinal);

    private static string NumeroComBase(ulong valor, char primeiro) =>
        (char)(primeiro + (int)(valor / 10_000_000)) + (valor % 10_000_000).ToString("0000000", CultureInfo.InvariantCulture);

    private static ulong ValorComBase(string numero, char primeiro) =>
        ((ulong)(numero[0] - primeiro) * 10_000_000) + ulong.Parse(numero[1..], CultureInfo.InvariantCulture);

    private static string SoDigitos(string texto) => new(texto.Where(char.IsAsciiDigit).ToArray());

    private static string SoSeparadores(string texto) => new(texto.Select(c => char.IsAsciiDigit(c) ? '#' : c).ToArray());
}
