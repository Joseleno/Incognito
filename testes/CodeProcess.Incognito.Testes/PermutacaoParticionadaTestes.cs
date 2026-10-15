using System.Globalization;
using CodeProcess.Incognito.Criptografia;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Permutação particionada pela presença de letra: bijeção exaustiva em domínios reduzidos (36² e 36³), partes que
/// nunca se misturam, prefixo de zeros fixo nas duas partes, a codificação em base 36 e a recusa de entrada fora do
/// contrato.
/// </summary>
public sealed class PermutacaoParticionadaTestes
{
    private static readonly byte[] _chaveNumerica = Enumerable.Repeat((byte)0x5A, 32).ToArray();
    private static readonly byte[] _chaveAlfanumerica = Enumerable.Repeat((byte)0xA5, 32).ToArray();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Bijecao_exaustiva_no_dominio_reduzido_particionada_pela_presenca_de_letra(int tamanho)
    {
        // Com 2 caracteres é o domínio do critério: 36² cadeias, das quais as 10² só com dígitos formam a parte
        // numérica; com 3, os lados da permutação são diferentes (a ≠ b). Só a cadeia de zeros é fixa.
        using var permutacao = Criar(tamanho, zerosFixos: tamanho);
        var zero = new string('0', tamanho);
        var saidas = new HashSet<string>();
        var saida = new char[tamanho];

        foreach (var entrada in Todas(tamanho))
        {
            var pontoFixo = permutacao.Permutar(entrada, saida);
            var texto = new string(saida);

            Assert.True(pontoFixo == (entrada == zero), "Ponto fixo fora do valor excluído.");
            Assert.True(TemLetra(texto) == TemLetra(entrada), "A presença de letra mudou.");
            Assert.True(entrada == zero || texto != zero, "Uma cadeia caiu no valor excluído.");
            Assert.True(texto.All(c => Alfanumerico.Contains(c)), "Saída fora do alfabeto.");
            saidas.Add(texto);
        }

        Assert.Equal((int)Math.Pow(36, tamanho), saidas.Count);
    }

    [Fact]
    public void Cadeias_com_o_prefixo_de_zeros_ficam_fixas_nas_duas_partes_e_nunca_sao_produzidas()
    {
        // Com 3 caracteres e 2 zeros fixos: as 10 cadeias 00d e as 26 cadeias 00L ficam como vieram.
        using var permutacao = Criar(3, zerosFixos: 2);
        var saidas = new HashSet<string>();
        var saida = new char[3];

        foreach (var entrada in Todas(3))
        {
            var fixa = entrada.StartsWith("00", StringComparison.Ordinal);
            var pontoFixo = permutacao.Permutar(entrada, saida);
            var texto = new string(saida);

            Assert.Equal(fixa, pontoFixo);
            Assert.True(fixa ? texto == entrada : !texto.StartsWith("00", StringComparison.Ordinal), "Cadeia fixa alterada ou produzida.");
            Assert.True(TemLetra(texto) == TemLetra(entrada), "A presença de letra mudou.");
            saidas.Add(texto);
        }

        Assert.Equal(36 * 36 * 36, saidas.Count);
    }

    [Fact]
    public void Sem_zeros_fixos_nada_fica_fixo_e_a_parte_numerica_e_a_permutacao_direta()
    {
        // 3 dígitos: a = 10², b = 10¹ (o lado maior à esquerda), comparado com a permutação Feistel direta.
        using var permutacao = Criar(3, zerosFixos: 0);
        using var direta = new PermutacaoFeistel(_chaveNumerica, 10, 100, 10);
        var saida = new char[3];

        for (ulong valor = 0; valor < 1_000; valor++)
        {
            Assert.False(permutacao.Permutar(valor.ToString("000", CultureInfo.InvariantCulture), saida));
            Assert.Equal(direta.Permutar(valor, []).ToString("000", CultureInfo.InvariantCulture), new string(saida));
        }
    }

    [Fact]
    public void Parte_alfanumerica_e_a_permutacao_direta_em_base_36_sem_as_cadeias_so_com_digitos()
    {
        // 3 caracteres: a = 36², b = 36, exclusão e base 36 escritas à parte da implementação.
        using var permutacao = Criar(3, zerosFixos: 0);
        using var direta = new PermutacaoFeistel(_chaveAlfanumerica, 36, 36 * 36, 36);
        var soDigitos = ConjuntoExcluido.PorRegra(valor => !TemLetra(DeBase36(valor, 3)), 1_000);
        var saida = new char[3];

        foreach (var entrada in Todas(3).Where(TemLetra))
        {
            permutacao.Permutar(entrada, saida);

            Assert.Equal(DeBase36(direta.Permutar(ParaBase36(entrada), [], soDigitos), 3), new string(saida));
        }
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 0, 1)]
    public void Com_zeros_excluidos_e_bijecao_exaustiva_e_a_parte_numerica_nunca_sai_com_o_prefixo(int tamanho, int zerosFixos, int zerosExcluidos)
    {
        // A redução da raiz do CNPJ (8 caracteres, 8 fixos, 3 excluídos): as cadeias só com dígitos que começam com os
        // zeros excluídos voltam como vieram e nunca são produzidas; fora delas, a saída só difere da permutação sem a
        // exclusão quando esta cairia no prefixo; a inversa desfaz.

        // Sob a mutação, um caractere a menos (36² cadeias em vez de 36³). Com 2 zeros fixos em 2 caracteres, a exclusão
        // coincide com os fixos e nada caminha; os outros dois casos caminham.
        var caracteres = Mutacao.Tamanho(tamanho, tamanho - 1);
        using var permutacao = Criar(caracteres, Math.Min(zerosFixos, caracteres), zerosExcluidos);
        var prefixo = new string('0', zerosExcluidos);
        var saidas = new HashSet<string>();
        var saida = new char[caracteres];
        var semExclusao = new char[caracteres];
        var volta = new char[caracteres];
        var caminhadas = 0;

        foreach (var entrada in Todas(caracteres))
        {
            var excluida = !TemLetra(entrada) && entrada.StartsWith(prefixo, StringComparison.Ordinal);
            var pontoFixo = permutacao.Permutar(entrada, saida, excluirZeros: true);
            var fixaSemExclusao = permutacao.Permutar(entrada, semExclusao);
            var texto = new string(saida);

            Assert.Equal(excluida || fixaSemExclusao, pontoFixo);
            Assert.True(excluida || pontoFixo ? texto == entrada : !(!TemLetra(texto) && texto.StartsWith(prefixo, StringComparison.Ordinal)), "Cadeia excluída produzida ou fixa alterada.");
            Assert.True(TemLetra(texto) == TemLetra(entrada), "A presença de letra mudou.");
            var semExclusaoTexto = new string(semExclusao);
            if (!excluida && !(!TemLetra(semExclusaoTexto) && semExclusaoTexto.StartsWith(prefixo, StringComparison.Ordinal)))
            {
                Assert.True(semExclusaoTexto == texto, "A exclusão mudou uma saída que já estava fora do prefixo.");
            }
            else if (!excluida)
            {
                caminhadas++;
            }

            Assert.Equal(pontoFixo, permutacao.Inverter(saida, volta, excluirZeros: true));
            Assert.True(new string(volta) == entrada, "A inversa não desfez a permutação.");
            saidas.Add(texto);
        }

        Assert.Equal((int)Math.Pow(36, caracteres), saidas.Count);
        Assert.True(caminhadas > 0 || (Mutacao.Ativa && zerosExcluidos == caracteres), "Nenhuma saída precisou do cycle-walking.");
    }

    [Fact]
    public void Na_reducao_da_raiz_do_CNPJ_a_parte_numerica_com_zeros_excluidos_e_bijecao_exaustiva()
    {
        // 4 dígitos, os 4 fixos (só 0000) e 2 excluídos: as 100 cadeias 00dd ficam e nunca saem; as outras 9.900 vão
        // para as mesmas 9.900, como as raízes de 10⁵ a 10⁸ na chave.
        using var permutacao = Criar(4, zerosFixos: 4, zerosExcluidos: 2);
        var saidas = new HashSet<string>();
        var saida = new char[4];
        var volta = new char[4];

        for (var valor = 0; valor < 10_000; valor++)
        {
            var entrada = valor.ToString("0000", CultureInfo.InvariantCulture);
            var pontoFixo = permutacao.Permutar(entrada, saida, excluirZeros: true);
            var texto = new string(saida);

            Assert.Equal(valor < 100, pontoFixo);
            Assert.True(valor < 100 ? texto == entrada : !texto.StartsWith("00", StringComparison.Ordinal), "Raiz com o prefixo produzida.");
            permutacao.Inverter(saida, volta, excluirZeros: true);
            Assert.True(new string(volta) == entrada, "A inversa não desfez a permutação.");
            saidas.Add(texto);
        }

        Assert.Equal(10_000, saidas.Count);
    }

    [Fact]
    public void Inversa_sem_exclusao_desfaz_a_permutacao_nas_duas_partes()
    {
        using var permutacao = Criar(3, zerosFixos: 2);
        var saida = new char[3];
        var volta = new char[3];

        foreach (var entrada in Todas(3))
        {
            var pontoFixo = permutacao.Permutar(entrada, saida);

            Assert.Equal(pontoFixo, permutacao.Inverter(saida, volta, excluirZeros: false));
            Assert.True(new string(volta) == entrada, "A inversa não desfez a permutação.");
        }
    }

    [Fact]
    public void Exclusao_pedida_a_permutacao_criada_sem_zeros_excluidos_e_recusada()
    {
        using var permutacao = Criar(8, zerosFixos: 8);

        Assert.Throws<InvalidOperationException>(() => permutacao.Permutar("12345678", new char[8], excluirZeros: true));
        Assert.Throws<InvalidOperationException>(() => permutacao.Inverter("12345678", new char[8], excluirZeros: true));
    }

    [Fact]
    public void Entrada_e_saida_podem_ser_o_mesmo_trecho()
    {
        using var permutacao = Criar(8, zerosFixos: 8);
        var sorteio = new Random(1501);
        for (var vez = 0; vez < 1_000; vez++)
        {
            var entrada = new string(Enumerable.Range(0, 8).Select(_ => Alfanumerico[sorteio.Next(36)]).ToArray());
            var separada = new char[8];
            permutacao.Permutar(entrada, separada);
            var mesma = entrada.ToCharArray();

            permutacao.Permutar(mesma, mesma);

            Assert.Equal(separada, mesma);
        }
    }

    [Theory]
    [InlineData("0000000Z", "00000016")]
    [InlineData("000000AZ", "000000B6")]
    public void Base_36_nao_confunde_cadeias_que_teriam_o_mesmo_valor_ASCII_menos_48(string uma, string outra)
    {
        // Com ASCII − 48, Z valeria 42: o primeiro par valeria 42 e o segundo, com letra nas duas, 654.
        using var permutacao = Criar(8, zerosFixos: 8);
        var saidaDeUma = new char[8];
        var saidaDaOutra = new char[8];

        permutacao.Permutar(uma, saidaDeUma);
        permutacao.Permutar(outra, saidaDaOutra);

        Assert.NotEqual(saidaDeUma, saidaDaOutra);
    }

    [Theory]
    [InlineData("0000000a")]
    [InlineData("0000000-")]
    [InlineData("0000000É")]
    [InlineData("0000000٣")]
    [InlineData("0000000Ａ")]
    public void Caractere_fora_do_alfabeto_e_recusado_sem_o_valor_na_mensagem(string cadeia)
    {
        using var permutacao = Criar(8, zerosFixos: 8);

        var excecao = Assert.Throws<ArgumentException>("entrada", () => permutacao.Permutar(cadeia, new char[8]));
        Assert.DoesNotContain(cadeia, excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tamanho_de_entrada_ou_de_destino_diferente_e_recusado()
    {
        using var permutacao = Criar(8, zerosFixos: 8);

        Assert.Throws<ArgumentException>("entrada", () => permutacao.Permutar("1234567", new char[8]));
        Assert.Throws<ArgumentException>("entrada", () => permutacao.Permutar("123456789", new char[8]));
        Assert.Throws<ArgumentException>("saida", () => permutacao.Permutar("12345678", new char[7]));
        Assert.Throws<ArgumentException>("saida", () => permutacao.Permutar("12345678", new char[9]));
    }

    [Theory]
    [InlineData(1, 0, "tamanho")]
    [InlineData(13, 0, "tamanho")]
    [InlineData(8, -1, "zerosFixos")]
    [InlineData(8, 9, "zerosFixos")]
    public void Parametros_fora_da_faixa_sao_recusados(int tamanho, int zerosFixos, string parametro)
    {
        Assert.Throws<ArgumentOutOfRangeException>(parametro, () => Criar(tamanho, zerosFixos));
    }

    [Theory]
    [InlineData(8, 8, -1)]
    [InlineData(8, 2, 3)]
    [InlineData(8, 0, 9)]
    public void Zeros_excluidos_fora_da_faixa_sao_recusados(int tamanho, int zerosFixos, int excluidos)
    {
        // Com zeros fixos, o conjunto excluído precisa conter o dos fixos (menos zeros, mais cadeias).
        Assert.Throws<ArgumentOutOfRangeException>("zerosExcluidos", () => Criar(tamanho, zerosFixos, excluidos));
    }

    [Theory]
    [InlineData(8, 8, 8)]
    [InlineData(8, 0, 8)]
    [InlineData(8, 8, 1)]
    public void Zeros_excluidos_nas_bordas_sao_aceitos(int tamanho, int zerosFixos, int zerosExcluidos)
    {
        using var permutacao = Criar(tamanho, zerosFixos, zerosExcluidos);
        var saida = new char[8];
        var semExclusao = new char[8];
        var volta = new char[8];

        Assert.False(permutacao.Permutar("12345678", saida, excluirZeros: true));
        permutacao.Permutar("12345678", semExclusao);
        permutacao.Inverter(saida, volta, excluirZeros: true);
        Assert.Equal("12345678", new string(volta));
        Assert.True(zerosExcluidos != tamanho || saida.SequenceEqual(semExclusao), "Excluir só a cadeia de zeros mudou a saída.");
    }

    private static PermutacaoParticionada Criar(int tamanho, int zerosFixos, int zerosExcluidos = 0) =>
        new(_chaveNumerica, _chaveAlfanumerica, tamanho, zerosFixos, zerosExcluidos);

    private static IEnumerable<string> Todas(int tamanho) =>
        tamanho == 0
            ? [string.Empty]
            : Todas(tamanho - 1).SelectMany(prefixo => Alfanumerico.Select(c => prefixo + c));

    private static bool TemLetra(string cadeia) => cadeia.Any(char.IsAsciiLetterUpper);

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
