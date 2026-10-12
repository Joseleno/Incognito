using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// GeradorBr: o sorteio (SplitMix64, xoshiro256** e Lemire) contra valores calculados fora da implementação, a
/// sequência fixa por semente, a validade de tudo o que é gerado, as exclusões forçadas por uma fonte roteirizada, a
/// distribuição e o uso por várias threads. Nenhum documento fica escrito no código.
/// </summary>
public sealed class GeradorBrTestes
{
    // Os 67 DDDs em uso, escritos à parte da implementação.
    private static readonly HashSet<int> _ddds =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 24, 27, 28, 31, 32, 33, 34, 35, 37, 38, 41, 42, 43, 44, 45, 46, 47,
        48, 49, 51, 53, 54, 55, 61, 62, 63, 64, 65, 66, 67, 68, 69, 71, 73, 74, 75, 77, 79, 81, 82, 83, 84, 85, 86, 87,
        88, 89, 91, 92, 93, 94, 95, 96, 97, 98, 99,
    ];

    public static TheoryData<long, ulong[], ulong[]> EstadosDaSemente => new()
    {
        // Calculados por uma referência em Python escrita à parte: estado expandido pelo SplitMix64 e as 4 primeiras saídas.
        // A primeira palavra da semente 0 é a primeira saída conhecida do SplitMix64 a partir de zero (0xE220A8397B1DCDAF).
        { 0, [16294208416658607535, 7960286522194355700, 487617019471545679, 17909611376780542444], [11091344671253066420, 13793997310169335082, 1900383378846508768, 7684712102626143532] },
        { 42, [13679457532755275413, 2949826092126892291, 5139283748462763858, 6349198060258255764], [1546998764402558742, 6990951692964543102, 12544586762248559009, 17057574109182124193] },
        { -1, [16490336266968443936, 16834447057089888969, 4048727598324417001, 7862637804313477842], [10328197420357168392, 14156678507024973869, 9357971779955476126, 13791585006304312367] },
        { long.MinValue, [5196802822362493915, 14154714916085338130, 7036458801432265024, 6426116064599561977], [14995854929039038659, 8753989917356920162, 5005381104203694257, 16559243352527774060] },
        { long.MaxValue, [3055647633038352039, 17441316833444690247, 17011665146503905680, 2314904739866303483], [1016735219197722821, 1807766611157899291, 8889853145372989117, 944049668562640016] },
    };

    [Fact]
    public void SplitMix64_da_os_valores_publicados()
    {
        // Saídas do SplitMix64 com semente 1234567, publicadas no Rosetta Code ("Pseudo-random numbers/Splitmix64").
        ulong[] esperado = [6457827717110365317, 3203168211198807973, 9817491932198370423, 4593380528125082431, 16408922859458223821];
        var x = 1234567UL;

        var saidas = esperado.Select(_ => SequenciaXoshiro.SplitMix64(ref x)).ToArray();

        Assert.Equal(esperado, saidas);
    }

    [Fact]
    public void Xoshiro256_estrela_estrela_da_os_valores_do_codigo_de_referencia()
    {
        // Estado {1, 2, 3, 4}: as duas primeiras saídas saem à mão (11520 e 0); as outras, da referência em Python.
        var sequencia = new SequenciaXoshiro(1, 2, 3, 4);

        var saidas = Enumerable.Range(0, 6).Select(_ => sequencia.Proximo()).ToArray();

        ulong[] esperado = [11520, 0, 1509978240, 1215971899390074240, 1216172134540287360, 607988272756665600];
        Assert.Equal(esperado, saidas);
    }

    [Theory]
    [MemberData(nameof(EstadosDaSemente))]
    public void Semente_e_expandida_pelo_SplitMix64(long semente, ulong[] estado, ulong[] saidas)
    {
        var daSemente = SequenciaXoshiro.DaSemente(semente);
        var doEstado = new SequenciaXoshiro(estado[0], estado[1], estado[2], estado[3]);

        foreach (var esperado in saidas)
        {
            Assert.Equal(esperado, daSemente.Proximo());
            Assert.Equal(esperado, doEstado.Proximo());
        }
    }

    [Fact]
    public void Uniforme_da_os_valores_da_referencia_inclusive_com_rejeicao()
    {
        // Com n pequeno a rejeição é rara demais para aparecer (6 em 2⁶⁴ para n = 10); n = 2⁶³ + 1 rejeita quase metade.
        var pequeno = SequenciaXoshiro.DaSemente(42);
        var grande = SequenciaXoshiro.DaSemente(42);

        var digitos = Enumerable.Range(0, 20).Select(_ => pequeno.Uniforme(10)).ToArray();
        var grandes = Enumerable.Range(0, 6).Select(_ => grande.Uniforme((1UL << 63) + 1)).ToArray();

        ulong[] digitosEsperados = [0, 3, 6, 9, 9, 7, 7, 8, 7, 5, 6, 2, 8, 3, 7, 8, 6, 8, 7, 7];
        ulong[] grandesEsperados = [9147776489032658738, 7099593415032875292, 6633989454467100377, 7022439175346172479, 2681029139591840946, 7388145106668446555];
        Assert.Equal(digitosEsperados, digitos);
        Assert.Equal(grandesEsperados, grandes);
    }

    [Fact]
    public void Uniforme_rejeita_o_numero_cuja_parte_baixa_fica_abaixo_do_limite()
    {
        // n = 10: o limite é 2⁶⁴ mod 10 = 6. Zero dá parte baixa 0 e é rejeitado; o maior dá 9.
        var fonte = new FonteRoteirizada([0, ulong.MaxValue]);

        Assert.Equal(9UL, fonte.Uniforme(10));
        Assert.Equal(2, fonte.Consumidos);

        // Na borda, parte baixa igual ao limite (x·10 = 4·2⁶⁴ + 6), o número é aceito.
        var naBorda = new FonteRoteirizada([(ulong)((((UInt128)4 << 64) + 6) / 10)]);
        Assert.Equal(4UL, naBorda.Uniforme(10));
        Assert.Equal(1, naBorda.Consumidos);

        // n = 3: o limite é 2⁶⁴ mod 3 = 1, e só a parte baixa 0 (x = 0) é rejeitada.
        var impar = new FonteRoteirizada([0, ulong.MaxValue]);
        Assert.Equal(2UL, impar.Uniforme(3));
        Assert.Equal(2, impar.Consumidos);
        Assert.Throws<ArgumentOutOfRangeException>(() => fonte.Uniforme(0));
    }

    [Fact]
    public void Sequencia_fixa_da_semente_42()
    {
        // SHA-256 das primeiras 1000 trincas (CPF, CNPJ numérico, CNPJ alfanumérico) da semente 42, calculado pela
        // referência em Python escrita à parte. Mudar esta sequência é mudança de contrato: só numa versão maior, com
        // registro no CHANGELOG.
        var gerador = new GeradorBr(42);
        var linhas = new List<string>();
        for (var vez = 0; vez < 1_000; vez++)
        {
            linhas.Add(gerador.Cpf());
            linhas.Add(gerador.Cnpj());
            linhas.Add(gerador.Cnpj(alfanumerico: true));
        }

        var resumo = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', linhas))));

        Assert.Equal("46e397b086f1a338d56fcbc4e54fda7c8e2c63fdaa9b979cf2afcb2b707dc437", resumo);
    }

    [Fact]
    public void Sequencia_fixa_da_semente_42_nos_tipos_internos()
    {
        // SHA-256 de 200 rodadas de telefone (celular; fixo com +55 e máscara), CEP (sem UF e de RO), e-mail e chave Pix
        // de cada tipo, calculado pela referência em Python escrita à parte. Fixa também a ordem dos sorteios e as
        // tabelas de DDD e de faixas de CEP.
        var gerador = new GeradorBr(42);
        var linhas = new List<string>();
        for (var vez = 0; vez < 200; vez++)
        {
            linhas.Add(gerador.Telefone(celular: true));
            linhas.Add(gerador.Telefone(celular: false, comPais: true, formatado: true));
            linhas.Add(gerador.Cep());
            linhas.Add(gerador.Cep("RO"));
            linhas.Add(gerador.Email());
            linhas.AddRange(Enum.GetValues<TipoChavePix>().Select(gerador.ChavePix));
        }

        var resumo = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', linhas))));

        Assert.Equal("82bc379c27db22f1658518d60acb30425afea2468b81a2ec274e66ff148f0069", resumo);
    }

    [Fact]
    public void Estado_todo_zero_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => new SequenciaXoshiro(0, 0, 0, 0));
        Assert.Equal(11520UL, new SequenciaXoshiro(1, 2, 3, 4).Proximo());
    }

    [Fact]
    public void Mesma_semente_gera_a_mesma_sequencia_e_outra_semente_outra()
    {
        var primeiro = new GeradorBr(1901);
        var segundo = new GeradorBr(1901);
        var outro = new GeradorBr(1902);

        var a = Sequencia(primeiro);
        var b = Sequencia(segundo);
        var c = Sequencia(outro);

        Assert.True(a.SequenceEqual(b), "A mesma semente gerou sequências diferentes.");
        Assert.False(a.SequenceEqual(c), "Sementes diferentes geraram a mesma sequência.");
    }

    [Fact]
    public void Formatado_nao_consome_sorteio()
    {
        var semMascara = new GeradorBr(1903);
        var comMascara = new GeradorBr(1903);
        for (var vez = 0; vez < 500; vez++)
        {
            var cpf = semMascara.Cpf();
            var cnpj = semMascara.Cnpj(alfanumerico: vez % 2 == 0);
            var cep = semMascara.Cep();
            var telefone = semMascara.Telefone(celular: true);

            Assert.True(comMascara.Cpf(formatado: true) == MascararCpf(cpf), "O CPF formatado não é o mesmo.");
            Assert.True(comMascara.Cnpj(alfanumerico: vez % 2 == 0, formatado: true) == MascararCnpj(cnpj), "O CNPJ formatado não é o mesmo.");
            Assert.True(comMascara.Cep(formatado: true) == cep[..5] + "-" + cep[5..], "O CEP formatado não é o mesmo.");
            Assert.True(comMascara.Telefone(celular: true, comPais: true, formatado: true) == "+55 (" + telefone[..2] + ") " + telefone[2..7] + "-" + telefone[7..], "O telefone formatado não é o mesmo.");

            var fixo = semMascara.Telefone(celular: false);
            Assert.True(comMascara.Telefone(celular: false, formatado: true) == "(" + fixo[..2] + ") " + fixo[2..6] + "-" + fixo[6..], "O fixo formatado não é o mesmo.");
        }
    }

    [Fact]
    public void Todo_CPF_e_CNPJ_gerado_passa_no_ValidadorBr()
    {
        var gerador = new GeradorBr(1904);
        for (var vez = 0; vez < 20_000; vez++)
        {
            var cpf = gerador.Cpf(formatado: vez % 2 == 0);
            var cnpj = gerador.Cnpj(alfanumerico: false, formatado: vez % 2 == 0);
            var alfanumerico = gerador.Cnpj(alfanumerico: true, formatado: vez % 2 == 0);

            Assert.True(ValidadorBr.Cpf(cpf) && ValidadorBr.Cnpj(cnpj) && ValidadorBr.Cnpj(alfanumerico), "Um documento gerado não passou no ValidadorBr.");
        }

        Assert.True(ValidadorBr.Cpf(GeradorBr.Padrao.Cpf()) && ValidadorBr.Cnpj(GeradorBr.Padrao.Cnpj(alfanumerico: true)), "O Padrao gerou documento inválido.");
    }

    [Fact]
    public void CNPJ_sai_com_ordem_0001_e_o_alfanumerico_com_letra_na_raiz()
    {
        var gerador = new GeradorBr(1905);
        for (var vez = 0; vez < 5_000; vez++)
        {
            var numerico = gerador.Cnpj();
            var alfanumerico = gerador.Cnpj(alfanumerico: true);

            Assert.True(numerico[8..12] == "0001" && alfanumerico[8..12] == "0001", "O CNPJ não saiu com a ordem 0001.");
            Assert.True(numerico.All(char.IsAsciiDigit), "O CNPJ numérico saiu com letra.");
            Assert.True(alfanumerico[..8].Any(char.IsAsciiLetterUpper) && alfanumerico.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)), "A raiz alfanumérica saiu sem letra ou com minúscula.");
        }
    }

    [Fact]
    public void Base_quase_repdigito_e_aceita_na_primeira()
    {
        var cpf = Roteiro(("777777770".Select(c => c - '0'), 10));
        var cnpj = Roteiro(("00000001".Select(c => c - '0'), 10));

        Assert.True(new GeradorBr(cpf).Cpf()[..9] == "777777770" && cpf.Consumidos == 9, "A base quase repdígito do CPF foi recusada.");
        Assert.True(new GeradorBr(cnpj).Cnpj()[..8] == "00000001" && cnpj.Consumidos == 8, "A raiz quase zero do CNPJ foi recusada.");
    }

    [Fact]
    public void Base_de_CPF_repdigito_e_sorteada_inteira_de_novo()
    {
        // Nove sorteios iguais formam um repdígito, recusado; os nove seguintes formam a base.
        var base9 = "123456780";
        var fonte = Roteiro((Enumerable.Repeat(7, 9).Concat(base9.Select(c => c - '0')), 10));

        var cpf = new GeradorBr(fonte).Cpf();

        Assert.True(cpf == base9 + DvCpfClassico(base9).ToString("00", CultureInfo.InvariantCulture), "O repdígito não foi sorteado de novo.");
        Assert.Equal(18, fonte.Consumidos);
    }

    [Fact]
    public void Raiz_zero_do_CNPJ_numerico_e_sorteada_inteira_de_novo()
    {
        var raiz = "12345670";
        var fonte = Roteiro((Enumerable.Repeat(0, 8).Concat(raiz.Select(c => c - '0')), 10));

        var cnpj = new GeradorBr(fonte).Cnpj();

        Assert.True(cnpj == raiz + "0001" + DvCnpjClassico(raiz + "0001").ToString("00", CultureInfo.InvariantCulture), "A raiz zero não foi sorteada de novo.");
        Assert.Equal(16, fonte.Consumidos);
    }

    [Fact]
    public void Raiz_alfanumerica_so_com_digitos_e_sorteada_inteira_de_novo()
    {
        // Índices no alfabeto 0-9A-Z: oito dígitos são recusados; a raiz seguinte tem letra.
        var fonte = Roteiro(([1, 2, 3, 4, 5, 6, 7, 8, 10, 1, 2, 3, 4, 5, 6, 35], 36));

        var cnpj = new GeradorBr(fonte).Cnpj(alfanumerico: true);

        Assert.True(cnpj[..12] == "A123456Z0001" && cnpj[12..] == DvCnpjClassico("A123456Z0001").ToString("00", CultureInfo.InvariantCulture), "A raiz só com dígitos não foi sorteada de novo.");
        Assert.Equal(16, fonte.Consumidos);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void Fixo_com_prefixo_de_servico_e_sorteado_de_novo_sem_o_DDD(int primeiro, int segundo)
    {
        // DDD (índice 0) e fixo 3003 ou 4004 + 4 dígitos, recusado; depois 2 + 7 dígitos iguais. O DDD não é sorteado
        // de novo: 1 + 8 + 8 sorteios.
        var prefixo = (char)('2' + primeiro);
        var fonte = Roteiro(([0], 67), ([primeiro], 4), ([segundo, 0, prefixo - '0', 1, 2, 3, 4], 10), ([0], 4), ([5, 5, 5, 5, 5, 5, 5], 10));

        var telefone = new GeradorBr(fonte).Telefone(celular: false);

        Assert.True(telefone[..2] == "11" && telefone[2] == '2' && telefone[3..].All(c => c == '5'), "O fixo com prefixo de serviço não foi sorteado de novo.");
        Assert.Equal(17, fonte.Consumidos);
    }

    [Fact]
    public void Distribuicao_da_base_e_uniforme_por_posicao()
    {
        // Qui-quadrado com α = 0,001 e semente fixa: 27,88 para 9 graus de liberdade e 66,62 para 35. O DV não é
        // uniforme e fica de fora.
        var gerador = new GeradorBr(1906);
        const int Amostras = 50_000;
        var cpfs = Enumerable.Range(0, Amostras).Select(_ => gerador.Cpf()).ToArray();
        var alfanumericos = Enumerable.Range(0, Amostras).Select(_ => gerador.Cnpj(alfanumerico: true)).ToArray();

        for (var posicao = 0; posicao < 9; posicao++)
        {
            Assert.True(QuiQuadrado(cpfs.Select(c => c[posicao]), Alfabeto("0123456789")) < 27.88, "A base do CPF não é uniforme.");
        }

        for (var posicao = 0; posicao < 8; posicao++)
        {
            Assert.True(QuiQuadrado(alfanumericos.Select(c => c[posicao]), Alfabeto(Alfanumerico)) < 66.62, "A raiz alfanumérica não é uniforme.");
        }
    }

    [Fact]
    public void Telefone_gerado_e_pseudonimizado_sem_cair_em_malformado_nem_em_servico()
    {
        using var chave = ChaveIncognito.Gerar();
        using var pseudonimizador = new PseudonimizadorTelefone(chave, "telefone", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(1907);
        for (var vez = 0; vez < 5_000; vez++)
        {
            var celular = vez % 2 == 0;
            var telefone = gerador.Telefone(celular, comPais: vez % 3 == 0, formatado: vez % 5 == 0);
            var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
            var semPais = telefone.StartsWith('+') ? digitos[2..] : digitos;

            Assert.Equal(Desfecho.Pseudonimizado, pseudonimizador.Pseudonimizar(telefone, out _));
            Assert.True(_ddds.Contains(int.Parse(semPais[..2], CultureInfo.InvariantCulture)), "O DDD não está em uso.");
            Assert.True(celular ? semPais.Length == 11 && semPais[2] == '9' : semPais.Length == 10 && semPais[2] is >= '2' and <= '5', "O telefone saiu da classe pedida.");
        }
    }

    [Fact]
    public void CEP_gerado_fica_nas_faixas_de_saida_da_UF()
    {
        using var chave = ChaveIncognito.Gerar();
        using var pseudonimizador = new PseudonimizadorCep(chave, "cep", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(1908);
        foreach (var uf in FaixasDeCepPorUf.Keys)
        {
            for (var vez = 0; vez < 200; vez++)
            {
                var cep = int.Parse(gerador.Cep(uf), CultureInfo.InvariantCulture);

                Assert.True(FaixasDeCepPorUf[uf].Any(faixa => cep >= faixa.Inicio && cep <= faixa.Fim), "O CEP saiu das faixas da UF.");
            }
        }

        // Sem UF, cada uma das 27 com a mesma chance: qui-quadrado com α = 0,001 e 26 graus de liberdade (54,05).
        var porUf = FaixasDeCepPorUf.Keys.ToDictionary(uf => uf, _ => 0);
        for (var vez = 0; vez < 27_000; vez++)
        {
            var cep = gerador.Cep(formatado: vez % 2 == 0);
            Assert.Equal(Desfecho.Pseudonimizado, pseudonimizador.Pseudonimizar(cep, "entidade", out _));
            var numero = int.Parse(cep.Replace("-", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
            porUf[FaixasDeCepPorUf.Single(par => par.Value.Any(faixa => numero >= faixa.Inicio && numero <= faixa.Fim)).Key]++;
        }

        Assert.True(porUf.Values.Sum(observado => (observado - 1_000.0) * (observado - 1_000.0) / 1_000.0) < 54.05, "As UFs não saem com a mesma chance.");
        Assert.Throws<ArgumentException>("uf", () => gerador.Cep("XX"));
    }

    [Fact]
    public void Email_gerado_fica_em_example_org_e_e_pseudonimizado()
    {
        using var chave = ChaveIncognito.Gerar();
        using var pseudonimizador = new PseudonimizadorEmail(chave, "email", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(1909);
        var vistos = new HashSet<string>();
        for (var vez = 0; vez < 10_000; vez++)
        {
            var email = gerador.Email();

            Assert.True(email.EndsWith("@example.org", StringComparison.Ordinal) && email.Length == 24, "O e-mail saiu fora do domínio reservado.");
            Assert.True(email[..12].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)), "A parte local saiu fora de [a-z0-9].");
            Assert.Equal(Desfecho.Pseudonimizado, pseudonimizador.Pseudonimizar(email, out _));
            vistos.Add(email);
        }

        Assert.Equal(10_000, vistos.Count);
    }

    [Fact]
    public void Chave_Pix_gerada_e_pseudonimizada_em_todo_tipo()
    {
        using var chave = ChaveIncognito.Gerar();
        using var cpf = new PseudonimizadorCpf(chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        using var email = new PseudonimizadorEmail(chave, "email", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        using var telefone = new PseudonimizadorTelefone(chave, "telefone", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        using var pix = new PseudonimizadorChavePix(chave, "chave-pix", 1, "homologacao", cpf, cnpj, email, telefone, PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(1910);
        var cnpjsComLetra = 0;
        foreach (var tipo in Enum.GetValues<TipoChavePix>())
        {
            for (var vez = 0; vez < 1_000; vez++)
            {
                var chavePix = gerador.ChavePix(tipo);
                cnpjsComLetra += tipo == TipoChavePix.Cnpj && chavePix.Any(char.IsAsciiLetter) ? 1 : 0;

                Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(chavePix, out _));
                var formaCerta = tipo switch
                {
                    TipoChavePix.Cpf => chavePix.Length == 11 && ValidadorBr.Cpf(chavePix),
                    TipoChavePix.Cnpj => chavePix.Length == 14 && ValidadorBr.Cnpj(chavePix),
                    TipoChavePix.Email => chavePix.EndsWith("@example.org", StringComparison.Ordinal),
                    TipoChavePix.Telefone => chavePix.StartsWith("+55", StringComparison.Ordinal) && chavePix.Length == 14 && chavePix[5] == '9',
                    _ => EhUuidVersao4(chavePix),
                };
                Assert.True(formaCerta, "A chave Pix saiu fora da forma do DICT.");
            }
        }

        // CNPJ numérico e alfanumérico meio a meio.
        Assert.True(cnpjsComLetra is > 400 and < 600, "A chave Pix de CNPJ não alterna entre numérico e alfanumérico.");
        Assert.Throws<ArgumentOutOfRangeException>("tipo", () => gerador.ChavePix((TipoChavePix)9));
    }

    [Fact]
    public void Padrao_e_instancia_com_semente_servem_varias_threads()
    {
        var gerador = new GeradorBr(1911);
        var doPadrao = new string[20_000];
        var daSemente = new string[20_000];

        Parallel.For(0, doPadrao.Length, i =>
        {
            doPadrao[i] = GeradorBr.Padrao.Cpf();
            daSemente[i] = gerador.Cnpj(alfanumerico: true);
        });

        Assert.True(doPadrao.All(ValidadorBr.Cpf) && daSemente.All(ValidadorBr.Cnpj), "O uso concorrente gerou documento inválido.");
        Assert.True(doPadrao.Distinct().Count() > 19_990, "O Padrao repetiu documentos entre threads.");

        // Com várias threads, a instância com semente gera o mesmo conjunto que uma thread só, em outra ordem.
        var sequencial = new GeradorBr(1911);
        var esperado = Enumerable.Range(0, daSemente.Length).Select(_ => sequencial.Cnpj(alfanumerico: true)).Order(StringComparer.Ordinal);
        Assert.True(esperado.SequenceEqual(daSemente.Order(StringComparer.Ordinal)), "O lock deixou a sequência se perder.");
    }

    // 8-4-4-4-12 com hexadecimais minúsculos, versão 4 e variante da RFC.
    private static bool EhUuidVersao4(string uuid) =>
        uuid.Length == 36
        && Enumerable.Range(0, 36).All(i => i is 8 or 13 or 18 or 23 ? uuid[i] == '-' : char.IsAsciiDigit(uuid[i]) || uuid[i] is >= 'a' and <= 'f')
        && uuid[14] == '4'
        && uuid[19] is '8' or '9' or 'a' or 'b';

    private static string[] Sequencia(GeradorBr gerador) =>
        Enumerable.Range(0, 300).SelectMany(_ => new[]
        {
            gerador.Cpf(),
            gerador.Cnpj(),
            gerador.Cnpj(alfanumerico: true),
            gerador.Telefone(celular: true),
            gerador.Cep(),
            gerador.Email(),
            gerador.ChavePix(TipoChavePix.Aleatoria),
        }).ToArray();

    private static Dictionary<char, int> Alfabeto(string caracteres) => caracteres.ToDictionary(c => c, _ => 0);

    private static double QuiQuadrado(IEnumerable<char> amostra, Dictionary<char, int> contagem)
    {
        var total = 0;
        foreach (var caractere in amostra)
        {
            contagem[caractere]++;
            total++;
        }

        var esperado = (double)total / contagem.Count;
        return contagem.Values.Sum(observado => (observado - esperado) * (observado - esperado) / esperado);
    }

    // Números que fazem o Uniforme(n) de cada parte devolver exatamente os valores pedidos, em sequência: com
    // x = ⌊valor·2⁶⁴/n⌋ + 2, os bits altos de x·n dão o valor, e a parte baixa, 2n − (valor·2⁶⁴ mod n), fica acima do
    // limite de rejeição.
    private static FonteRoteirizada Roteiro(params (IEnumerable<int> Valores, ulong N)[] partes) =>
        new(partes.SelectMany(parte => parte.Valores.Select(valor => (ulong)(((UInt128)(ulong)valor << 64) / parte.N) + 2)).ToArray());

    private sealed class FonteRoteirizada(ulong[] numeros) : FonteDeSorteio
    {
        internal int Consumidos { get; private set; }

        internal override ulong Proximo() =>
            Consumidos < numeros.Length ? numeros[Consumidos++] : throw new InvalidOperationException("O roteiro acabou.");
    }
}
