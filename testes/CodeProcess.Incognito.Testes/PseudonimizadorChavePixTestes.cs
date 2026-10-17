using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Chave Pix: cada tipo sai igual ao pseudônimo da coluna correspondente (instâncias criadas à parte com os mesmos
/// parâmetros), a classificação pela forma, a chave aleatória recalculada de forma independente, as políticas e o
/// descarte. Chaves, documentos e telefones são sorteados na hora; a chave é a pública do vetor de derivação.
/// </summary>
public sealed class PseudonimizadorChavePixTestes : IDisposable
{
    private const string Hexadecimal = "0123456789abcdef";

    private readonly ChaveIncognito _chave = ChaveDoVetor();
    private readonly Dictionary<PoliticaEntradaMalformada, Colunas> _colunas = [];

    public static TheoryData<string> SemTipo =>
    [
        "cpf:",
        "abc",
        "xyz",
        "+ abc def",
        "+",
        "１２３４５６７８９０１",
        "٠١٢٣٤٥٦٧٨٩٠",
        "nome＠dominio.test",
        "0123456789abcdef0123456789abcdef",
        "01234567089ab0cdef001230456789abcdef",
        "{01234567-89ab-cdef-0123-456789abcdef}",
        "01234567‐89ab-cdef-0123-456789abcdef",
        "01234567-89ab-cdef-0123-456789abcdeg",
        "0123456-789ab-cdef-0123-456789abcdef",
        "01234567-89abc-def-0123-456789abcdef",
        "------------------------------------",
        " 01234567-89ab-cdef-0123-456789abcdef",
        "01234567-89ab-cdef-0123-456789abcdef\t",
        "01234567-89ab-cdef-0123-456789abcdef ",
        " 0123456789",
        "-0123456789",
        // Só zeros: tomados por telefone, sairiam como Zerado, e não pela política.
        "-0000-0000",
        "0000+0000",
        "000.000.000/00",
        "123.456|789-01",
        "123|456.789-01",
        "0123456789ABCD",
        "ÀBCDEFGHIJKL00",
        "12.ABC.DEF|0001-00",
        "12|ABC.DEF/0001-00",
        "12.ABC|DEF/0001-00",
        "12.ABC.DEF/0001|00",
        "12.ABC.DEF/0001-0A",
        "( ) -",
        "0123456789\r",
    ];

    public static TheoryData<int> TiposDeChave => [0, 1, 2, 3];

    public void Dispose()
    {
        foreach (var colunas in _colunas.Values)
        {
            colunas.Dispose();
        }

        _chave.Dispose();
    }

    [Fact]
    public void Chave_do_tipo_CPF_sai_igual_a_coluna_de_CPF()
    {
        using var pix = Criar();
        using var coluna = NovoCpf();
        var sorteio = new Random(1800);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var cpf = CpfValido(sorteio);
            foreach (var entrada in new[] { cpf, MascararCpf(cpf) })
            {
                Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada, out var saida));
                coluna.Pseudonimizar(entrada, out var esperado);
                Assert.True(saida == esperado, "A chave do tipo CPF deu outro pseudônimo que a coluna.");
            }
        }
    }

    [Fact]
    public void Chave_do_tipo_CNPJ_sai_igual_a_coluna_de_CNPJ_inclusive_alfanumerico_com_mascara_e_ordem_permutada()
    {
        using var pix = Criar();
        using var coluna = NovoCnpj();
        using var comOrdem = NovoCnpj(permutarOrdem: true);
        using var pixComOrdem = Criar(cnpj: comOrdem);
        using var colunaComOrdem = NovoCnpj(permutarOrdem: true);
        var sorteio = new Random(1801);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: vez % 2 == 0);
            foreach (var entrada in new[] { cnpj, MascararCnpj(cnpj) })
            {
                Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada, out var saida));
                coluna.Pseudonimizar(entrada, out var esperado);
                pixComOrdem.Pseudonimizar(entrada, out var saidaComOrdem);
                colunaComOrdem.Pseudonimizar(entrada, out var esperadoComOrdem);
                Assert.True(saida == esperado && saidaComOrdem == esperadoComOrdem, "A chave do tipo CNPJ deu outro pseudônimo que a coluna.");
            }
        }
    }

    [Fact]
    public void Chave_do_tipo_email_sai_igual_a_coluna_de_email()
    {
        using var pix = Criar();
        using var coluna = NovoEmail();
        var sorteio = new Random(1802);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var entrada = Endereco(sorteio);
            if (vez % 3 == 0)
            {
                entrada = "+" + entrada.ToUpperInvariant();
            }

            Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada, out var saida));
            coluna.Pseudonimizar(entrada, out var esperado);
            Assert.True(saida == esperado, "A chave do tipo e-mail deu outro pseudônimo que a coluna.");
        }
    }

    [Fact]
    public void Chave_do_tipo_telefone_sai_igual_a_coluna_de_telefone()
    {
        // Forma do DICT (+55, DDD e celular) e as formas que aparecem em bases de aplicação, menos os 11 dígitos colados.
        using var pix = Criar();
        using var coluna = NovoTelefone();
        var sorteio = new Random(1803);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var ddd = Ddd(sorteio);
            var celular = "9" + Digitos(sorteio, 8);
            var formas = new[]
            {
                "+55" + ddd + celular,
                "55" + ddd + celular,
                "(" + ddd + ") " + celular[..5] + "-" + celular[5..],
                "+55 " + ddd + " " + celular,
                ddd + "." + celular[..5] + "." + celular[5..],
            };
            foreach (var entrada in formas)
            {
                Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada, out var saida));
                coluna.Pseudonimizar(entrada, out var esperado);
                Assert.True(saida == esperado, "A chave do tipo telefone deu outro pseudônimo que a coluna.");
            }
        }
    }

    [Fact]
    public void Outro_dominio_de_CPF_da_outro_pseudonimo()
    {
        using var outroDominio = NovoCpf(dominio: "cpf-cliente");
        using var pix = Criar();
        using var pixOutroDominio = Criar(cpf: outroDominio);
        var cpf = CpfValido(new Random(1804));

        pix.Pseudonimizar(cpf, out var a);
        pixOutroDominio.Pseudonimizar(cpf, out var b);

        Assert.True(a != b, "Domínios de CPF diferentes deram o mesmo pseudônimo.");
    }

    [Fact]
    public void Onze_digitos_sao_sempre_CPF_mesmo_com_forma_de_celular()
    {
        // Um telefone sem o +55 com 11 dígitos segue o CPF: assim nenhum pseudônimo de telefone cai entre os de CPF.
        using var preservarDv = NovoCpf(invalida: PoliticaEntradaInvalida.Preservar);
        using var anularDv = NovoCpf(invalida: PoliticaEntradaInvalida.Anular);
        using var coluna = NovoCpf(invalida: PoliticaEntradaInvalida.Preservar);
        using var pixPreservar = Criar(cpf: preservarDv);
        using var pixAnular = Criar(cpf: anularDv);
        var sorteio = new Random(1805);
        for (var vez = 0; vez < 3_000; vez++)
        {
            var entrada = (vez % 3) switch
            {
                0 => CpfValido(sorteio),
                1 => Ddd(sorteio) + "9" + Digitos(sorteio, 8),
                _ => Digitos(sorteio, 11),
            };
            var dvCerto = DvCpfClassico(entrada[..9]) == int.Parse(entrada[9..], CultureInfo.InvariantCulture);
            var esperadoDesfecho = coluna.Pseudonimizar(entrada, out var esperado);

            Assert.Equal(esperadoDesfecho, pixPreservar.Pseudonimizar(entrada, out var saida));
            Assert.True(saida == esperado, "Onze dígitos não seguiram o CPF.");
            if (!dvCerto)
            {
                Assert.Equal(Desfecho.AnuladoDvInvalido, pixAnular.Pseudonimizar(entrada, out var anulado));
                Assert.Null(anulado);
            }
        }
    }

    [Fact]
    public void Nove_dez_doze_e_treze_digitos_vao_para_o_telefone_sem_completar_zeros()
    {
        // O CPF completaria 9 e 10 dígitos e o CNPJ, 12 e 13; na chave Pix, as formas de telefone desses tamanhos são
        // telefone e saem com o mesmo tamanho.
        using var pix = Criar();
        using var coluna = NovoTelefone();
        var sorteio = new Random(1806);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var celular = "9" + Digitos(sorteio, 8);
            var entrada = (vez % 4) switch
            {
                0 => celular,
                1 => Ddd(sorteio) + (char)('2' + sorteio.Next(4)) + "1" + Digitos(sorteio, 6),
                2 => "0" + Ddd(sorteio) + celular,
                _ => "55" + Ddd(sorteio) + celular,
            };

            Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada, out var saida));
            coluna.Pseudonimizar(entrada, out var esperado);
            Assert.True(saida == esperado && saida!.Length == entrada.Length, "Dígitos que o CPF ou o CNPJ completariam não foram para o telefone.");
        }
    }

    [Fact]
    public void Espaco_a_esquerda_so_passa_no_email()
    {
        // O e-mail tira os espaços das pontas, como a coluna; nos outros tipos, espaço à esquerda é sem tipo.
        var anular = Instancias(PoliticaEntradaMalformada.Anular);
        using var pix = Criar(PoliticaEntradaMalformada.Anular);
        var sorteio = new Random(1807);
        for (var vez = 0; vez < 300; vez++)
        {
            var email = " " + Endereco(sorteio);
            Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(email, out var saida));
            anular.Email.Pseudonimizar(email, out var esperado);
            Assert.True(saida == esperado, "O e-mail com espaço à esquerda deu outro pseudônimo que a coluna.");

            var outros = new[] { " " + CpfValido(sorteio), " " + CnpjValido(sorteio, alfanumerico: true), " +55" + Ddd(sorteio) + "9" + Digitos(sorteio, 8), " " + Uuid(sorteio) };
            foreach (var entrada in outros)
            {
                Assert.Equal(Desfecho.AnuladoMalformado, pix.Pseudonimizar(entrada, out var anulado));
                Assert.Null(anulado);
            }
        }
    }

    [Fact]
    public void CNPJ_alfanumerico_em_minusculas_sai_igual_ao_em_maiusculas()
    {
        using var pix = Criar();
        using var coluna = NovoCnpj();
        var sorteio = new Random(1808);
        var vistos = 0;
        while (vistos < 500)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            if (!cnpj.Any(char.IsAsciiLetter))
            {
                continue;
            }

            vistos++;
            foreach (var entrada in new[] { cnpj, MascararCnpj(cnpj) })
            {
                Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(entrada.ToLowerInvariant(), out var saida));
                coluna.Pseudonimizar(entrada, out var esperado);
                Assert.True(saida == esperado, "O CNPJ em minúsculas deu outro pseudônimo que o em maiúsculas.");
            }
        }
    }

    [Fact]
    public void CNPJ_em_minusculas_com_DV_errado_fica_sem_tipo()
    {
        // Sem tipo segue a política de malformado da chave Pix, não a de DV do CNPJ (que aqui é falhar).
        var preservar = Instancias(PoliticaEntradaMalformada.Preservar);
        using var pix = Criar(PoliticaEntradaMalformada.Preservar);
        var sorteio = new Random(1809);
        var vistos = 0;
        while (vistos < 300)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            if (!cnpj.Any(char.IsAsciiLetter))
            {
                continue;
            }

            vistos++;
            var dvErrado = cnpj[..12] + ((int.Parse(cnpj[12..], CultureInfo.InvariantCulture) + 1 + sorteio.Next(98)) % 100).ToString("00", CultureInfo.InvariantCulture);
            foreach (var entrada in new[] { dvErrado.ToLowerInvariant(), MascararCnpj(dvErrado).ToLowerInvariant() })
            {
                Assert.Equal(Desfecho.CopiadoEmClaro, pix.Pseudonimizar(entrada, out var copiado));
                Assert.True(copiado == entrada, "O CNPJ em minúsculas com DV errado não ficou sem tipo.");
            }

            Assert.Throws<IncognitoException>(() => preservar.Cnpj.Pseudonimizar(dvErrado, out _));
        }
    }

    [Fact]
    public void CNPJ_em_maiusculas_com_DV_errado_segue_a_politica_de_DV_do_CNPJ()
    {
        using var preservarDv = NovoCnpj(invalida: PoliticaEntradaInvalida.Preservar);
        using var coluna = NovoCnpj(invalida: PoliticaEntradaInvalida.Preservar);
        using var pix = Criar(cnpj: preservarDv);
        var sorteio = new Random(1810);
        for (var vez = 0; vez < 500; vez++)
        {
            var cnpj = CnpjValido(sorteio, alfanumerico: vez % 2 == 0);
            var entrada = cnpj[..12] + ((int.Parse(cnpj[12..], CultureInfo.InvariantCulture) + 1 + sorteio.Next(98)) % 100).ToString("00", CultureInfo.InvariantCulture);

            Assert.Equal(Desfecho.DvInvalidoPreservado, pix.Pseudonimizar(entrada, out var saida));
            coluna.Pseudonimizar(entrada, out var esperado);
            Assert.True(saida == esperado, "O CNPJ com DV errado não seguiu a coluna.");
        }
    }

    [Fact]
    public void CNPJ_em_minusculas_em_ponto_fixo_volta_como_veio()
    {
        // Raiz zero com DV certo é ponto fixo: o valor volta como veio, com as minúsculas, não convertido.
        using var pix = Criar();
        var sorteio = new Random(1811);
        for (var vez = 0; vez < 100; vez++)
        {
            string ordem;
            do
            {
                ordem = new string(Enumerable.Range(0, 4).Select(_ => Alfanumerico[sorteio.Next(Alfanumerico.Length)]).ToArray());
            }
            while (!ordem.Any(char.IsAsciiLetter));

            var baseCnpj = "00000000" + ordem;
            var entrada = (baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", CultureInfo.InvariantCulture)).ToLowerInvariant();

            Assert.Equal(Desfecho.PontoFixo, pix.Pseudonimizar(entrada, out var saida));
            Assert.True(saida == entrada, "O ponto fixo não voltou com as minúsculas da entrada.");
        }
    }

    [Fact]
    public void Chave_aleatoria_e_o_HMAC_dos_16_bytes_com_versao_4()
    {
        // Recalcula de forma independente: K_semente de br.chave-pix em chave-pix@1, HMAC-SHA256 de "evp" ‖ 0x00 ‖ os 16
        // bytes do UUID na ordem do texto, os 16 primeiros bytes com versão 4 e variante da RFC, em minúsculas.
        using var pix = Criar();
        var chaveDeSemente = new byte[32];
        Subchaves.DerivarSemente(_chave, "br.chave-pix", "chave-pix", 1, "homologacao", chaveDeSemente);
        var sorteio = new Random(1812);
        for (var vez = 0; vez < 3_000; vez++)
        {
            var uuid = vez == 0 ? "00010203-0405-0607-0809-0a0b0c0d0e0f" : Uuid(sorteio);
            var bytes = Convert.FromHexString(uuid.Replace("-", string.Empty, StringComparison.Ordinal));
            var resultado = HMACSHA256.HashData(chaveDeSemente, "evp\0"u8.ToArray().Concat(bytes).ToArray()).AsSpan(0, 16).ToArray();
            resultado[6] = (byte)((resultado[6] & 0x0F) | 0x40);
            resultado[8] = (byte)((resultado[8] & 0x3F) | 0x80);
            var hex = Convert.ToHexStringLower(resultado);
            var esperado = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";

            Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(uuid, out var saida));
            Assert.True(saida == esperado, "A chave aleatória não é o HMAC dos 16 bytes.");
            Assert.True(saida![14] == '4' && saida[19] is '8' or '9' or 'a' or 'b', "A chave aleatória não saiu na versão 4.");
        }
    }

    [Fact]
    public void Chave_aleatoria_ignora_a_caixa_e_preserva_o_preenchimento()
    {
        using var pix = Criar();
        var sorteio = new Random(1813);
        for (var vez = 0; vez < 1_000; vez++)
        {
            var uuid = Uuid(sorteio);
            pix.Pseudonimizar(uuid, out var minusculas);

            pix.Pseudonimizar(uuid.ToUpperInvariant(), out var maiusculas);
            pix.Pseudonimizar(uuid + "    ", out var comPreenchimento);

            Assert.True(maiusculas == minusculas, "A caixa do UUID mudou a chave aleatória.");
            Assert.True(comPreenchimento == minusculas + "    ", "O preenchimento não foi preservado.");
        }
    }

    [Fact]
    public void Chaves_aleatorias_diferentes_dao_pseudonimos_diferentes()
    {
        using var pix = Criar();
        var sorteio = new Random(1814);
        var entradas = new HashSet<string>();
        while (entradas.Count < Mutacao.Tamanho(100_000, 5_000))
        {
            entradas.Add(Uuid(sorteio));
        }

        var saidas = entradas.Select(entrada => Saida(pix, entrada)).ToHashSet();

        Assert.Equal(entradas.Count, saidas.Count);
    }

    [Fact]
    public void Mesma_configuracao_da_a_mesma_chave_aleatoria_e_ambiente_dominio_ou_versao_diferente_nao()
    {
        var uuid = Uuid(new Random(1815));
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "chave-pix-destino");
        using var outraVersao = Criar(versao: 2);

        primeiro.Pseudonimizar(uuid, out var a);
        segundo.Pseudonimizar(uuid, out var b);
        outroAmbiente.Pseudonimizar(uuid, out var c);
        outroDominio.Pseudonimizar(uuid, out var d);
        outraVersao.Pseudonimizar(uuid, out var e);

        Assert.True(a == b, "A mesma configuração deu chaves diferentes.");
        Assert.True(a != c && a != d && a != e, "Ambiente, domínio ou versão diferente deu a mesma chave.");
    }

    [Fact]
    public void Saida_na_forma_do_DICT_continua_no_mesmo_tipo()
    {
        // As saídas dos tipos não se cruzam: o pseudônimo de cada tipo, pseudonimizado de novo, segue o mesmo tipo.
        using var pix = Criar();
        var colunas = Instancias(PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(1817);
        for (var vez = 0; vez < 500; vez++)
        {
            var cpf = CpfValido(sorteio);
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            var email = Endereco(sorteio);
            var telefone = "+55" + Ddd(sorteio) + "9" + Digitos(sorteio, 8);

            Assert.True(Saida(pix, Saida(pix, cpf)!) == Saida(colunas.Cpf, Saida(colunas.Cpf, cpf)!), "O pseudônimo de CPF mudou de tipo.");
            Assert.True(Saida(pix, Saida(pix, cnpj)!) == Saida(colunas.Cnpj, Saida(colunas.Cnpj, cnpj)!), "O pseudônimo de CNPJ mudou de tipo.");
            Assert.True(Saida(pix, Saida(pix, email)!) == Saida(colunas.Email, Saida(colunas.Email, email)!), "O pseudônimo de e-mail mudou de tipo.");
            Assert.True(Saida(pix, Saida(pix, telefone)!) == Saida(colunas.Telefone, Saida(colunas.Telefone, telefone)!), "O pseudônimo de telefone mudou de tipo.");
            Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(Saida(pix, Uuid(sorteio))!, out _));
        }
    }

    [Fact]
    public void Preenchimento_de_char_n_e_tratado_como_na_coluna_em_todo_tipo()
    {
        using var pix = Criar();
        var colunas = Instancias(PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(1818);
        for (var vez = 0; vez < 300; vez++)
        {
            var cpf = CpfValido(sorteio);
            var cnpj = CnpjValido(sorteio, alfanumerico: true);
            var email = Endereco(sorteio);
            var telefone = "+55" + Ddd(sorteio) + "9" + Digitos(sorteio, 8);
            var espacos = new string(' ', 1 + sorteio.Next(40));

            Assert.True(Saida(pix, cpf + espacos) == Saida(colunas.Cpf, cpf + espacos), "O preenchimento do CPF mudou.");
            Assert.True(Saida(pix, cnpj + espacos) == Saida(colunas.Cnpj, cnpj + espacos), "O preenchimento do CNPJ mudou.");
            Assert.True(Saida(pix, email + espacos) == Saida(colunas.Email, email + espacos), "O preenchimento do e-mail mudou.");
            Assert.True(Saida(pix, telefone + espacos) == Saida(colunas.Telefone, telefone + espacos), "O preenchimento do telefone mudou.");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("          ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            using var pix = Criar(malformada);

            Assert.Equal(Desfecho.Vazio, pix.Pseudonimizar(entrada, out var saida));
            Assert.Equal(entrada, saida);
        }
    }

    [Theory]
    [MemberData(nameof(SemTipo))]
    public void Valor_sem_tipo_segue_a_politica_da_chave_Pix(string entrada)
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

    [Fact]
    public void Malformado_dentro_de_um_tipo_segue_a_mesma_politica()
    {
        // Telefone com + de outro país e e-mail sem ponto no domínio têm tipo, mas não a forma do tipo.
        using var pix = Criar(PoliticaEntradaMalformada.Anular);
        var sorteio = new Random(1819);

        Assert.Equal(Desfecho.AnuladoMalformado, pix.Pseudonimizar("+1" + Digitos(sorteio, 10), out var telefone));
        Assert.Equal(Desfecho.AnuladoMalformado, pix.Pseudonimizar("local@semponto", out var email));
        Assert.Equal(Desfecho.AnuladoMalformado, pix.Pseudonimizar(Digitos(sorteio, 5), out var curto));
        Assert.True(telefone is null && email is null && curto is null, "O malformado dentro do tipo não foi anulado.");
    }

    [Fact]
    public void CNPJ_em_minusculas_com_o_primeiro_separador_trocado_nao_tem_tipo_e_volta_com_a_caixa_da_entrada()
    {
        // DV certo e só o primeiro separador trocado: sem a máscara do CNPJ, o valor não tem tipo e, com Preservar, volta
        // como veio. Tomado por CNPJ, passaria a maiúsculas antes da coluna de CNPJ e voltaria em maiúsculas.
        using var pix = Criar(PoliticaEntradaMalformada.Preservar);
        var cnpj = new GeradorBr(1840).Cnpj(alfanumerico: true, formatado: true);
        var entrada = (cnpj[..2] + "-" + cnpj[3..]).ToLowerInvariant();

        Assert.Equal(Desfecho.CopiadoEmClaro, pix.Pseudonimizar(entrada, out var saida));
        Assert.True(saida == entrada, "O valor sem tipo não voltou como veio.");
    }

    [Theory]
    [MemberData(nameof(TiposDeChave))]
    public void Pseudonimizador_recebido_com_outra_politica_de_malformado_e_recusado(int tipo)
    {
        var falhar = Instancias(PoliticaEntradaMalformada.Falhar);
        var anular = Instancias(PoliticaEntradaMalformada.Anular);
        var (cpf, cnpj, email, telefone, parametro) = tipo switch
        {
            0 => (anular.Cpf, falhar.Cnpj, falhar.Email, falhar.Telefone, "cpf"),
            1 => (falhar.Cpf, anular.Cnpj, falhar.Email, falhar.Telefone, "cnpj"),
            2 => (falhar.Cpf, falhar.Cnpj, anular.Email, falhar.Telefone, "email"),
            _ => (falhar.Cpf, falhar.Cnpj, falhar.Email, anular.Telefone, "telefone"),
        };

        Assert.Throws<ArgumentException>(parametro, () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", cpf, cnpj, email, telefone, PoliticaEntradaMalformada.Falhar));
    }

    [Fact]
    public void Desfechos_dos_tipos_sobem_sem_mudanca()
    {
        using var pix = Criar();
        var sorteio = new Random(1820);

        var repdigito = new string((char)('0' + sorteio.Next(10)), 9);
        var cpfRepdigito = repdigito + DvCpfClassico(repdigito).ToString("00", CultureInfo.InvariantCulture);
        Assert.Equal(Desfecho.PontoFixo, pix.Pseudonimizar(cpfRepdigito, out var pontoFixo));
        Assert.True(pontoFixo == cpfRepdigito, "O ponto fixo mudou.");

        var servico = "+550800" + Digitos(sorteio, 7);
        Assert.Equal(Desfecho.ServicoPreservado, pix.Pseudonimizar(servico, out var preservado));
        Assert.True(preservado == servico, "O número de serviço mudou.");
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_que_o_sequencial()
    {
        using var pix = Criar();
        var sorteio = new Random(1821);
        var entradas = Enumerable.Range(0, 50_000).Select(i => (i % 5) switch
        {
            0 => CpfValido(sorteio),
            1 => CnpjValido(sorteio, alfanumerico: true),
            2 => Endereco(sorteio),
            3 => "+55" + Ddd(sorteio) + "9" + Digitos(sorteio, 8),
            _ => Uuid(sorteio),
        }).ToArray();
        var sequencial = entradas.Select(e => Saida(pix, e)).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => pix.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou alguma chave.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada_e_as_instancias_recebidas_continuam()
    {
        var pix = Criar(PoliticaEntradaMalformada.Preservar);
        pix.Dispose();
        pix.Dispose();

        var sorteio = new Random(1822);
        var cpf = CpfValido(sorteio);
        foreach (var entrada in new[] { cpf, Uuid(sorteio), "sem tipo", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => pix.Pseudonimizar(entrada, out _));
        }

        Assert.Equal(Desfecho.Pseudonimizado, Instancias(PoliticaEntradaMalformada.Preservar).Cpf.Pseudonimizar(cpf, out _));
    }

    [Theory]
    [MemberData(nameof(TiposDeChave))]
    public void Instancia_recebida_descartada_recusa_so_o_seu_tipo(int tipo)
    {
        using var colunas = new Colunas(_chave, PoliticaEntradaMalformada.Falhar);
        using var pix = new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", colunas.Cpf, colunas.Cnpj, colunas.Email, colunas.Telefone, PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(1823);
        var entrada = tipo switch
        {
            0 => CpfValido(sorteio),
            1 => CnpjValido(sorteio, alfanumerico: true),
            2 => Endereco(sorteio),
            _ => "+55" + Ddd(sorteio) + "9" + Digitos(sorteio, 8),
        };
        IDisposable descartada = tipo switch { 0 => colunas.Cpf, 1 => colunas.Cnpj, 2 => colunas.Email, _ => colunas.Telefone };
        descartada.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pix.Pseudonimizar(entrada, out _));
        Assert.Equal(Desfecho.Pseudonimizado, pix.Pseudonimizar(Uuid(sorteio), out _));
    }

    [Fact]
    public void Descarte_zera_a_chave_de_semente()
    {
        var pix = Criar();
        var hmac = typeof(PseudonimizadorChavePix).GetField("_hmac", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pix)!;
        var chaveDeSemente = (byte[])typeof(HmacDeSemente).GetField("_chave", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hmac)!;

        pix.Dispose();

        Assert.True(Array.TrueForAll(chaveDeSemente, b => b == 0), "O descarte não zerou a chave de semente.");
    }

    [Fact]
    public void Politica_fora_das_definidas_instancia_nula_e_entrada_nula_sao_recusadas()
    {
        var falhar = Instancias(PoliticaEntradaMalformada.Falhar);
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", falhar.Cpf, falhar.Cnpj, falhar.Email, falhar.Telefone, (PoliticaEntradaMalformada)7));
        Assert.Throws<ArgumentNullException>("cpf", () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", null!, falhar.Cnpj, falhar.Email, falhar.Telefone, PoliticaEntradaMalformada.Falhar));
        Assert.Throws<ArgumentNullException>("cnpj", () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", falhar.Cpf, null!, falhar.Email, falhar.Telefone, PoliticaEntradaMalformada.Falhar));
        Assert.Throws<ArgumentNullException>("email", () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", falhar.Cpf, falhar.Cnpj, null!, falhar.Telefone, PoliticaEntradaMalformada.Falhar));
        Assert.Throws<ArgumentNullException>("telefone", () => new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", falhar.Cpf, falhar.Cnpj, falhar.Email, null!, PoliticaEntradaMalformada.Falhar));
        using var pix = Criar();
        Assert.Throws<ArgumentNullException>("entrada", () => pix.Pseudonimizar(null!, out _));
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    // Os quatro pseudonimizadores das colunas com a política de malformado da chave Pix, criados uma vez por política.
    private Colunas Instancias(PoliticaEntradaMalformada malformada)
    {
        if (!_colunas.TryGetValue(malformada, out var colunas))
        {
            colunas = new Colunas(_chave, malformada);
            _colunas[malformada] = colunas;
        }

        return colunas;
    }

    private PseudonimizadorChavePix Criar(
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "chave-pix",
        int versao = 1,
        PseudonimizadorCpf? cpf = null,
        PseudonimizadorCnpj? cnpj = null)
    {
        var colunas = Instancias(malformada);
        return new(_chave, dominio, versao, ambiente, cpf ?? colunas.Cpf, cnpj ?? colunas.Cnpj, colunas.Email, colunas.Telefone, malformada);
    }

    private PseudonimizadorCpf NovoCpf(string dominio = "cpf", PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Falhar) =>
        new(_chave, dominio, 1, "homologacao", invalida, PoliticaEntradaMalformada.Falhar);

    private PseudonimizadorCnpj NovoCnpj(bool permutarOrdem = false, PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Falhar) =>
        new(_chave, "cnpj", 1, "homologacao", permutarOrdem, invalida, PoliticaEntradaMalformada.Falhar);

    private PseudonimizadorEmail NovoEmail() => new(_chave, "email", 1, "homologacao", PoliticaEntradaMalformada.Falhar);

    private PseudonimizadorTelefone NovoTelefone() => new(_chave, "telefone", 1, "homologacao", PoliticaEntradaMalformada.Falhar);

    private static string? Saida(PseudonimizadorChavePix pix, string entrada)
    {
        pix.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorCpf cpf, string entrada)
    {
        cpf.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorCnpj cnpj, string entrada)
    {
        cnpj.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorEmail email, string entrada)
    {
        email.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    private static string? Saida(PseudonimizadorTelefone telefone, string entrada)
    {
        telefone.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    private static string Ddd(Random sorteio) => new([(char)('1' + sorteio.Next(9)), (char)('1' + sorteio.Next(9))]);

    private static string Endereco(Random sorteio) =>
        Texto(sorteio, 3 + sorteio.Next(12)) + "@" + Texto(sorteio, 3 + sorteio.Next(8)) + ".test";

    private static string Texto(Random sorteio, int tamanho) =>
        new(Enumerable.Range(0, tamanho).Select(_ => "abcdefghijklmnopqrstuvwxyz0123456789"[sorteio.Next(36)]).ToArray());

    private static string Uuid(Random sorteio)
    {
        var hex = new string(Enumerable.Range(0, 32).Select(_ => Hexadecimal[sorteio.Next(16)]).ToArray());
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private sealed class Colunas : IDisposable
    {
        internal Colunas(ChaveIncognito chave, PoliticaEntradaMalformada malformada)
        {
            Cpf = new PseudonimizadorCpf(chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Falhar, malformada);
            Cnpj = new PseudonimizadorCnpj(chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Falhar, malformada);
            Email = new PseudonimizadorEmail(chave, "email", 1, "homologacao", malformada);
            Telefone = new PseudonimizadorTelefone(chave, "telefone", 1, "homologacao", malformada);
        }

        internal PseudonimizadorCpf Cpf { get; }

        internal PseudonimizadorCnpj Cnpj { get; }

        internal PseudonimizadorEmail Email { get; }

        internal PseudonimizadorTelefone Telefone { get; }

        public void Dispose()
        {
            Cpf.Dispose();
            Cnpj.Dispose();
            Email.Dispose();
            Telefone.Dispose();
        }
    }
}
