using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Domínio e-mail: saída sempre em domínio reservado, parte local de 100 bits do HMAC em base32, normalização por
/// minúsculas e espaços das pontas, a política de malformado. Todo endereço é sorteado na hora; a chave é a pública do
/// vetor de derivação; as asserções não imprimem valores.
/// </summary>
public sealed class PseudonimizadorEmailTestes : IDisposable
{
    private const string Letras = "abcdefghijklmnopqrstuvwxyz0123456789";

    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public static TheoryData<string> Malformados =>
    [
        "semarroba",
        "@dominio.test",
        "local@",
        "local@semponto",
        "local@.inicio.test",
        "local@fim.test.",
        "local@dois..pontos.test",
        "dois@arrobas@dominio.test",
        "com espaco@dominio.test",
        "tab\t@dominio.test",
        "controle\u0001@dominio.test",
        "nbsp @dominio.test",
        "local@com espaco.test",
        "local@controle\u0001.test",
        "com.ponto@semponto",
    ];

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Nenhum_email_sai_fora_do_dominio_reservado()
    {
        using var email = Criar();
        var sorteio = new Random(1702);
        for (var vez = 0; vez < 10_000; vez++)
        {
            Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(Endereco(sorteio), out var saida));

            Assert.EndsWith("@example.com", saida, StringComparison.Ordinal);
            var local = saida![..^"@example.com".Length];
            Assert.True(local.Length == 20 && local.All(c => c is (>= 'a' and <= 'z') or (>= '2' and <= '7')), "Parte local fora do base32 de 20 caracteres.");
        }
    }

    [Fact]
    public void Enderecos_diferentes_dao_pseudonimos_diferentes()
    {
        using var email = Criar();
        var sorteio = new Random(1703);
        var enderecos = new HashSet<string>();
        while (enderecos.Count < 100_000)
        {
            enderecos.Add(Endereco(sorteio));
        }

        var saidas = enderecos.Select(endereco => { email.Pseudonimizar(endereco, out var saida); return saida; }).ToHashSet();

        Assert.Equal(enderecos.Count, saidas.Count);
    }

    [Fact]
    public void Maiusculas_e_espacos_das_pontas_nao_mudam_o_pseudonimo()
    {
        using var email = Criar();
        var sorteio = new Random(1704);
        for (var vez = 0; vez < 1_000; vez++)
        {
            var endereco = Endereco(sorteio);
            email.Pseudonimizar(endereco, out var base1);

            email.Pseudonimizar(endereco.ToUpperInvariant(), out var maiusculas);
            email.Pseudonimizar("  " + endereco, out var espacoAEsquerda);
            email.Pseudonimizar("\t" + endereco + "\t", out var tabs);

            Assert.True(maiusculas == base1 && espacoAEsquerda == base1 && tabs == base1, "A normalização mudou o pseudônimo.");
        }
    }

    [Fact]
    public void Pontos_e_mais_da_parte_local_ficam_como_vieram()
    {
        // Nenhuma regra de provedor: com e sem ponto, com e sem +, são endereços diferentes.
        using var email = Criar();
        var sorteio = new Random(1705);
        var local = Texto(sorteio, 8);
        var dominio = Texto(sorteio, 6) + ".test";

        email.Pseudonimizar(local + "@" + dominio, out var simples);
        email.Pseudonimizar(local[..4] + "." + local[4..] + "@" + dominio, out var comPonto);
        email.Pseudonimizar(local + "+" + Texto(sorteio, 3) + "@" + dominio, out var comMais);

        Assert.True(simples != comPonto && simples != comMais, "Uma regra de provedor foi aplicada.");
    }

    [Fact]
    public void Pseudonimo_e_o_HMAC_da_semente_em_base32()
    {
        // Recalcula de forma independente: K_semente de email@1, HMAC-SHA256 do endereço com a regra congelada de
        // minúsculas (A-Z e À-Þ menos ×) e em UTF-8, 13 primeiros bytes em base32 da RFC 4648 (minúsculas), 20 caracteres.
        using var email = Criar();
        var chaveDeSemente = new byte[32];
        Subchaves.DerivarSemente(_chave, "email", "email", 1, "homologacao", chaveDeSemente);
        var sorteio = new Random(1706);
        for (var vez = 0; vez < 2_000; vez++)
        {
            var endereco = (vez % 4) switch
            {
                0 => Endereco(sorteio),
                1 => Endereco(sorteio).ToUpperInvariant(),
                2 => "ÀÉÇÃÕÞ" + Endereco(sorteio),
                _ => "ΣİŸ" + Endereco(sorteio),
            };
            var hmac = HMACSHA256.HashData(chaveDeSemente, Encoding.UTF8.GetBytes(MinusculasCongeladas(endereco)));
            var esperado = Base32(hmac[..13])[..20] + "@example.com";

            email.Pseudonimizar(endereco, out var saida);

            Assert.True(saida == esperado, "O pseudônimo não é o HMAC da semente.");
        }
    }

    [Fact]
    public void Minusculas_seguem_a_regra_congelada()
    {
        // A-Z e À-Þ (menos ×) viram minúsculas; outras letras, como Σ, Ÿ e İ, ficam como vieram.
        using var email = Criar();
        var dominio = "@" + Texto(new Random(1714), 6) + ".test";

        Assert.True(Pseudonimo(email, "JOÃO" + dominio) == Pseudonimo(email, "joão" + dominio), "Ã não virou ã.");
        Assert.True(Pseudonimo(email, "ÞÉÇ" + dominio) == Pseudonimo(email, "þéç" + dominio), "Letras de À a Þ não viraram minúsculas.");
        Assert.True(Pseudonimo(email, "Σ" + dominio) != Pseudonimo(email, "σ" + dominio), "Σ fora da regra virou minúscula.");
        Assert.True(Pseudonimo(email, "Ÿ" + dominio) != Pseudonimo(email, "ÿ" + dominio), "Ÿ fora da regra virou minúscula.");
        Assert.True(Pseudonimo(email, "×" + dominio) != Pseudonimo(email, "÷" + dominio), "× virou outro caractere.");
    }

    [Fact]
    public void Grafias_que_diferem_so_na_caixa_dao_o_mesmo_pseudonimo()
    {
        // De propósito: a injetividade vale depois da normalização. Numa coluna UNIQUE com colação que diferencia
        // maiúsculas, as duas grafias colidem (o planejar avisa).
        using var email = Criar();
        var endereco = Endereco(new Random(1715));

        Assert.True(Pseudonimo(email, endereco) == Pseudonimo(email, char.ToUpperInvariant(endereco[0]) + endereco[1..]), "Grafias com caixa diferente deram pseudônimos diferentes.");
    }

    [Fact]
    public void Surrogate_sem_par_e_malformado()
    {
        // Viraria U+FFFD e juntaria endereços diferentes num só pseudônimo.
        using var falhar = Criar();
        using var anular = Criar(PoliticaEntradaMalformada.Anular);
        var dominio = "@" + Texto(new Random(1716), 6) + ".test";

        foreach (var entrada in new[] { "a\uD800" + dominio, "a\uDC00" + dominio, "\uDBFFa" + dominio })
        {
            Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _)).Codigo);
            Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out var anulado));
            Assert.Null(anulado);
        }

        Assert.Equal(Desfecho.Pseudonimizado, falhar.Pseudonimizar("a�" + dominio, out _));
    }

    [Fact]
    public void Espacos_a_esquerda_nao_contam_para_o_preenchimento()
    {
        // A entrada tem 51 caracteres (2 espaços, 19 de endereço, 30 de preenchimento); a saída fica com a mesma largura.
        using var email = Criar();
        var entrada = "  " + Texto(new Random(1717), 8) + "@exemplo.ab" + new string(' ', 30);

        Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(entrada, out var saida));

        Assert.Equal(entrada.Length, saida!.Length);
    }

    [Fact]
    public void Descarte_zera_a_chave_de_semente()
    {
        var email = Criar();
        var hmac = typeof(PseudonimizadorEmail).GetField("_hmac", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(email)!;
        var chaveDeSemente = (byte[])typeof(HmacDeSemente).GetField("_chave", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hmac)!;

        email.Dispose();

        Assert.True(Array.TrueForAll(chaveDeSemente, b => b == 0), "O descarte não zerou a chave de semente.");
    }

    [Fact]
    public void Endereco_com_acento_e_pseudonimizado()
    {
        using var email = Criar();
        var sorteio = new Random(1707);
        var local = "joão" + Texto(sorteio, 4);

        Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(local + "@exemplo.test", out var minusculo));
        email.Pseudonimizar(local.ToUpperInvariant() + "@EXEMPLO.TEST", out var maiusculo);

        Assert.True(minusculo == maiusculo, "Acento em maiúscula mudou o pseudônimo.");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Parte_local_de_ate_64_caracteres(int tamanho, bool aceita)
    {
        using var email = Criar();
        var entrada = Texto(new Random(1708), tamanho) + "@exemplo.test";

        if (aceita)
        {
            Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(entrada, out _));
        }
        else
        {
            Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => email.Pseudonimizar(entrada, out _)).Codigo);
        }
    }

    [Theory]
    [InlineData(254, true)]
    [InlineData(255, false)]
    public void Endereco_de_ate_254_caracteres(int tamanho, bool aceita)
    {
        using var email = Criar();
        var sorteio = new Random(1709);
        var local = Texto(sorteio, 60);
        var dominio = Texto(sorteio, tamanho - local.Length - 1 - ".test".Length) + ".test";
        var entrada = local + "@" + dominio;

        Assert.Equal(tamanho, entrada.Length);
        if (aceita)
        {
            Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(entrada, out _));
        }
        else
        {
            Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => email.Pseudonimizar(entrada, out _)).Codigo);
        }
    }

    [Theory]
    [MemberData(nameof(Malformados))]
    public void Entrada_malformada_segue_a_politica(string entrada)
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(40, 27)]
    public void Preenchimento_a_direita_volta_descontado_o_que_o_pseudonimo_tem_a_mais(int espacos, int espacosNaSaida)
    {
        // A entrada tem 19 caracteres e o pseudônimo, 32: os 13 a mais saem do preenchimento.
        using var email = Criar();
        var endereco = Texto(new Random(1710), 8) + "@" + "exemplo.ab";
        email.Pseudonimizar(endereco, out var semEspacos);

        Assert.Equal(Desfecho.Pseudonimizado, email.Pseudonimizar(endereco + new string(' ', espacos), out var saida));

        Assert.True(saida == semEspacos + new string(' ', espacosNaSaida), "O preenchimento não foi descontado.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("          ")]
    public void Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica(string entrada)
    {
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            using var email = Criar(malformada);

            Assert.Equal(Desfecho.Vazio, email.Pseudonimizar(entrada, out var saida));
            Assert.Equal(entrada, saida);
        }
    }

    [Fact]
    public void Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_ou_versao_diferente_nao()
    {
        var entrada = Endereco(new Random(1711));
        using var primeiro = Criar();
        using var segundo = Criar();
        using var outroAmbiente = Criar(ambiente: "desenvolvimento");
        using var outroDominio = Criar(dominio: "email-contato");
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
        using var email = Criar();
        var sorteio = new Random(1712);
        var entradas = Enumerable.Range(0, 100_000).Select(_ => Endereco(sorteio)).ToArray();
        var sequencial = entradas.Select(entrada => { email.Pseudonimizar(entrada, out var saida); return saida; }).ToArray();
        var paralelo = new string?[entradas.Length];

        Parallel.For(0, entradas.Length, i => email.Pseudonimizar(entradas[i], out paralelo[i]));

        Assert.True(sequencial.SequenceEqual(paralelo), "O uso concorrente mudou algum pseudônimo.");
    }

    [Fact]
    public void Depois_do_descarte_toda_entrada_e_recusada()
    {
        var email = Criar(PoliticaEntradaMalformada.Preservar);
        email.Dispose();
        email.Dispose();

        foreach (var entrada in new[] { Endereco(new Random(1713)), "malformado", "" })
        {
            Assert.Throws<ObjectDisposedException>(() => email.Pseudonimizar(entrada, out _));
        }
    }

    [Fact]
    public void Politica_fora_das_definidas_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar((PoliticaEntradaMalformada)7));
    }

    [Fact]
    public void Entrada_nula_e_recusada()
    {
        using var email = Criar();

        Assert.Throws<ArgumentNullException>(() => email.Pseudonimizar(null!, out _));
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorEmail Criar(
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar,
        string ambiente = "homologacao",
        string dominio = "email",
        int versao = 1) =>
        new(_chave, dominio, versao, ambiente, malformada);

    private static string Endereco(Random sorteio) =>
        Texto(sorteio, 3 + sorteio.Next(12)) + "@" + Texto(sorteio, 3 + sorteio.Next(8)) + (sorteio.Next(2) == 0 ? ".test" : ".example");

    private static string Texto(Random sorteio, int tamanho) =>
        new(Enumerable.Range(0, tamanho).Select(_ => Letras[sorteio.Next(Letras.Length)]).ToArray());

    private static string? Pseudonimo(PseudonimizadorEmail email, string entrada)
    {
        email.Pseudonimizar(entrada, out var saida);
        return saida;
    }

    // A regra congelada de minúsculas, escrita à parte da implementação: só A-Z e À-Þ, menos o ×.
    private static string MinusculasCongeladas(string texto) =>
        new(texto.Select(c => c is (>= 'A' and <= 'Z') or (>= 'À' and <= 'Þ' and not '×') ? (char)(c + 32) : c).ToArray());

    // Base32 da RFC 4648 em minúsculas, escrito à parte da implementação.
    private static string Base32(byte[] bytes)
    {
        const string Alfabeto = "abcdefghijklmnopqrstuvwxyz234567";
        var bits = string.Concat(bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        return new string(Enumerable.Range(0, bits.Length / 5).Select(i => Alfabeto[Convert.ToInt32(bits.Substring(i * 5, 5), 2)]).ToArray());
    }
}
