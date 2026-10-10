using System.Buffers.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Formato, leitura, exportação e proteção da chave. Toda chave usada aqui é gerada na hora: nenhuma chave fica
/// escrita no código. Os casos de entrada inválida aparecem na saída só pelo nome, e as asserções que envolvem a
/// chave não imprimem o valor quando falham.
/// </summary>
public sealed class ChaveIncognitoTestes
{
    private const string Prefixo = "inc1_";
    private const string CodigoFormatoInvalido = "INC1001";

    public static TheoryData<string> CasosInvalidos =>
    [
        "senha digitada",
        "vazia",
        "sem prefixo",
        "prefixo em maiusculas",
        "outra versao do formato",
        "um caractere a menos",
        "um caractere a mais",
        "com padding",
        "alfabeto base64 padrao",
        "espaco no meio",
        "espaco no inicio",
        "quebra de linha no fim",
        "nulo no meio",
        "nulo no fim",
        "BOM no inicio",
        "letra de largura total",
        "espaco de largura zero",
        "bits de sobra diferentes de zero",
        "caractere fora do ASCII",
        "caractere fora do ASCII com byte baixo de letra valida",
        "entrada de 1 MB",
        "chave toda zero",
    ];

    [Fact]
    public void Chave_gerada_exporta_no_formato_inc1_com_43_caracteres_base64url()
    {
        using var chave = ChaveIncognito.Gerar();

        var texto = Exportar(chave);

        Assert.True(Regex.IsMatch(texto, "^inc1_[A-Za-z0-9_-]{43}$"), "A chave exportada está fora do formato.");
    }

    [Fact]
    public void Chave_exportada_e_lida_de_volta_e_a_mesma_chave()
    {
        using var original = ChaveIncognito.Gerar();
        var texto = Exportar(original);

        using var lida = ChaveIncognito.Ler(texto);

        Assert.True(Exportar(lida) == texto, "A chave lida difere da exportada.");
        Assert.Equal(original.ImpressaoDigital, lida.ImpressaoDigital);
    }

    [Fact]
    public void Leitura_em_UTF8_equivale_a_leitura_do_texto()
    {
        using var original = ChaveIncognito.Gerar();
        var texto = Exportar(original);

        using var lida = ChaveIncognito.Ler(Encoding.UTF8.GetBytes(texto));

        Assert.True(Exportar(lida) == texto, "A chave lida em UTF-8 difere da exportada.");
    }

    [Fact]
    public void Chaves_geradas_sao_diferentes()
    {
        using var primeira = ChaveIncognito.Gerar();
        using var segunda = ChaveIncognito.Gerar();

        Assert.True(Exportar(primeira) != Exportar(segunda), "Duas chaves geradas são iguais.");
        Assert.NotEqual(primeira.ImpressaoDigital, segunda.ImpressaoDigital);
    }

    [Theory]
    [MemberData(nameof(CasosInvalidos))]
    public void Entrada_fora_do_formato_canonico_e_recusada_sem_revelar_o_valor(string caso)
    {
        var entrada = EntradaInvalida(caso);

        var porTexto = Assert.Throws<IncognitoException>(() => ChaveIncognito.Ler(entrada));
        var porUtf8 = Assert.Throws<IncognitoException>(() => ChaveIncognito.Ler(Encoding.UTF8.GetBytes(entrada)));

        foreach (var excecao in new[] { porTexto, porUtf8 })
        {
            Assert.Equal(CodigoFormatoInvalido, excecao.Codigo);
            Assert.Null(excecao.InnerException);
            Assert.Empty(excecao.Data);
            AssertNaoRevela(excecao.ToString(), entrada);
        }
    }

    [Fact]
    public void Mensagem_de_recusa_e_a_mesma_para_qualquer_entrada()
    {
        // Mensagem constante prova que ela não depende da entrada, em qualquer trecho.
        var mensagens = CasosInvalidos
            .Select(caso => EntradaInvalida(caso.Data))
            .SelectMany(entrada => new[]
            {
                Assert.Throws<IncognitoException>(() => ChaveIncognito.Ler(entrada)).Message,
                Assert.Throws<IncognitoException>(() => ChaveIncognito.Ler(Encoding.UTF8.GetBytes(entrada))).Message,
            })
            .Distinct()
            .ToArray();

        Assert.Single(mensagens);
    }

    [Fact]
    public void Versao_canonica_da_entrada_com_bits_de_sobra_e_aceita()
    {
        // Controle do caso "bits de sobra diferentes de zero": a mesma chave com os bits zerados é válida.
        using var original = ChaveIncognito.Gerar();
        var texto = Exportar(original);

        using var lida = ChaveIncognito.Ler(ComBitsDeSobra(texto, 0));

        Assert.True(Exportar(lida) == texto, "A chave canônica não foi lida de volta igual.");
    }

    [Fact]
    public void ToString_mostra_so_a_mascara()
    {
        using var chave = ChaveIncognito.Gerar();

        Assert.Equal("ChaveIncognito(inc1_****)", chave.ToString());
        AssertNaoRevela(chave.ToString(), Exportar(chave));
    }

    [Fact]
    public void Impressao_digital_sao_os_8_primeiros_bytes_do_HMAC_da_subchave_de_impressao()
    {
        using var chave = ChaveIncognito.Gerar();
        var valor = Base64Url.DecodeFromChars(Exportar(chave).AsSpan(Prefixo.Length));

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, valor, "CodeProcess.Incognito/v1"u8.ToArray());
        var chaveDeImpressao = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, "impressao-digital"u8.ToArray());
        var esperada = Convert.ToHexStringLower(HMACSHA256.HashData(chaveDeImpressao, "v1"u8.ToArray()).AsSpan(0, 8));

        Assert.Equal(esperada, chave.ImpressaoDigital);
        Assert.Matches("^[0-9a-f]{16}$", chave.ImpressaoDigital);
        AssertNaoRevela(chave.ImpressaoDigital, Exportar(chave));
    }

    [Fact]
    public void PRK_e_o_HKDF_Extract_da_chave_com_o_sal_do_Incognito()
    {
        using var chave = ChaveIncognito.Gerar();
        var valor = Base64Url.DecodeFromChars(Exportar(chave).AsSpan(Prefixo.Length));
        var esperado = HKDF.Extract(HashAlgorithmName.SHA256, valor, "CodeProcess.Incognito/v1"u8.ToArray());
        var prk = new byte[32];

        chave.ExtrairPrk(prk);

        Assert.True(prk.AsSpan().SequenceEqual(esperado), "O PRK difere do HKDF-Extract esperado.");
    }

    [Fact]
    public void Exportacao_para_destino_curto_falha_sem_escrever()
    {
        using var chave = ChaveIncognito.Gerar();
        var destino = new char[47];

        var exportou = chave.TentarExportar(destino, out var escritos);

        Assert.False(exportou);
        Assert.Equal(0, escritos);
        Assert.True(Array.TrueForAll(destino, caractere => caractere == '\0'), "O destino curto foi alterado.");
    }

    [Fact]
    public void Exportacao_para_destino_longo_escreve_so_48_caracteres()
    {
        using var chave = ChaveIncognito.Gerar();
        var destino = Enumerable.Repeat('#', 100).ToArray();

        Assert.True(chave.TentarExportar(destino, out var escritos));

        Assert.Equal(48, escritos);
        Assert.True(destino.AsSpan(48).IndexOfAnyExcept('#') < 0, "A exportação escreveu além dos 48 caracteres.");
    }

    [Fact]
    public void Descartar_zera_o_valor_e_impede_o_uso()
    {
        var chave = ChaveIncognito.Gerar();
        var valor = (byte[])typeof(ChaveIncognito).GetField("_valor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chave)!;

        chave.Dispose();

        Assert.True(Array.TrueForAll(valor, octeto => octeto == 0), "O valor da chave não foi zerado ao descartar.");
        Assert.Throws<ObjectDisposedException>(() => chave.TentarExportar(new char[48], out _));
        Assert.Throws<ObjectDisposedException>(() => chave.ExtrairPrk(new byte[32]));
        Assert.Equal("ChaveIncognito(inc1_****)", chave.ToString());
        chave.Dispose();
    }

    [Fact]
    public void Exportacao_concorrente_com_o_descarte_nunca_entrega_chave_zerada()
    {
        // Quem cruza com o descarte recebe ObjectDisposedException; nunca true com a chave total ou parcialmente zerada.
        var cancelamento = TestContext.Current.CancellationToken;
        for (var rodada = 0; rodada < 2_000; rodada++)
        {
            var chave = ChaveIncognito.Gerar();
            var original = Exportar(chave);
            var destino = new char[48];
            var exportou = false;
            using var largada = new Barrier(2);

            var exportacao = new Thread(() =>
            {
                largada.SignalAndWait(cancelamento);
                try
                {
                    exportou = chave.TentarExportar(destino, out _);
                }
                catch (ObjectDisposedException)
                {
                }
            });
            exportacao.Start();
            largada.SignalAndWait(cancelamento);
            chave.Dispose();
            exportacao.Join();

            if (exportou)
            {
                Assert.True(new string(destino) == original, "A exportação cruzada com o descarte entregou outra chave.");
            }
            else
            {
                Assert.True(Array.TrueForAll(destino, caractere => caractere == '\0'), "Sobrou parte da chave no destino.");
            }
        }
    }

    private static string Exportar(ChaveIncognito chave)
    {
        var destino = new char[48];
        Assert.True(chave.TentarExportar(destino, out var escritos));
        return new string(destino, 0, escritos);
    }

    private static string EntradaInvalida(string caso)
    {
        using var chave = ChaveIncognito.Gerar();
        var texto = Exportar(chave);
        var base64 = texto[Prefixo.Length..];

        return caso switch
        {
            "senha digitada" => "correto-cavalo-bateria-grampo",
            "vazia" => "",
            "sem prefixo" => base64,
            "prefixo em maiusculas" => "INC1_" + base64,
            "outra versao do formato" => "inc2_" + base64,
            "um caractere a menos" => texto[..^1],
            "um caractere a mais" => texto + "A",
            "com padding" => texto[..^1] + "=",
            "alfabeto base64 padrao" => Prefixo + "+" + base64[1..],
            "espaco no meio" => texto[..20] + " " + texto[21..],
            "espaco no inicio" => " " + texto[1..],
            "quebra de linha no fim" => texto[..^1] + "\n",
            "nulo no meio" => texto[..20] + "\0" + texto[21..],
            "nulo no fim" => texto[..^1] + "\0",
            "BOM no inicio" => "﻿" + texto[1..],
            "letra de largura total" => Prefixo + "Ａ" + base64[1..],
            "espaco de largura zero" => texto[..20] + "​" + texto[21..],
            "bits de sobra diferentes de zero" => ComBitsDeSobra(texto, 1),
            "caractere fora do ASCII" => Prefixo + "ç" + base64[1..],
            // U+01xx com o mesmo byte baixo do caractere original: truncar para byte o transformaria numa chave válida.
            "caractere fora do ASCII com byte baixo de letra valida" => Prefixo + (char)(0x100 | base64[0]) + base64[1..],
            "entrada de 1 MB" => texto + new string('A', 1024 * 1024),
            // Válida no formato, mas é uma chave pública conhecida (32 bytes zero); nenhum gerador aleatório a produz.
            "chave toda zero" => Prefixo + new string('A', 43),
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de teste desconhecido."),
        };
    }

    // 32 bytes ocupam 256 dos 258 bits dos 43 caracteres: os 2 bits menos significativos do último caractere sobram.
    private static string ComBitsDeSobra(string texto, int bits)
    {
        const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var ultimo = Alfabeto.IndexOf(texto[^1], StringComparison.Ordinal);
        return texto[..^1] + Alfabeto[(ultimo & ~0b11) | bits];
    }

    // Procura a entrada inteira, sem prefixo e qualquer janela de 8 caracteres dela. Não imprime o segredo se falhar.
    private static void AssertNaoRevela(string saida, string segredo)
    {
        var semPrefixo = segredo.StartsWith(Prefixo, StringComparison.Ordinal) ? segredo[Prefixo.Length..] : segredo;
        if (semPrefixo.Length > 64)
        {
            semPrefixo = semPrefixo[..64];
        }

        for (var inicio = 0; inicio + 8 <= semPrefixo.Length; inicio++)
        {
            Assert.False(saida.Contains(semPrefixo.Substring(inicio, 8), StringComparison.Ordinal), "A saída contém um trecho da chave.");
        }
    }
}
