using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A tabela de formato fixo contra o modelo da igualdade numérica (<c>kn</c>): as saídas das embutidas listadas nunca se
/// igualam por ela, porque todo trecho de dígitos tem comprimento e posição fixos, sem encostar numa letra, ou o alfabeto
/// nunca começa um trecho de dígitos por <c>0</c>.
/// </summary>
public sealed class FormatoFixoTestes
{
    // A forma de entrada de cada amostra e o transformador da tabela que a produz.
    private static readonly Dictionary<string, string> _transformadorDaForma = new(StringComparer.Ordinal)
    {
        ["cpf"] = "br.cpf@1",
        ["cpf-mascara"] = "br.cpf@1",
        ["cpf-preservado"] = "br.cpf@1",
        ["email"] = "email@1",
    };

    [Theory]
    [InlineData("ab01c2", "ab1c02", true)]
    [InlineData("a2b", "a02b", true)]
    [InlineData("a23b4", "a2b34", false)]
    [InlineData("00012345678", "12345678", true)]
    [InlineData("000", "0", true)]
    [InlineData("12.3B5.678/01A2-XX", "12.3B5.678/1A02-XX", true)]
    [InlineData("123.456.789-00", "12345678900", false)]
    [InlineData("0900123456", "900123456", true)]
    public void O_modelo_da_igualdade_numerica_reproduz_os_pares_conhecidos(string um, string outro, bool iguais)
    {
        Assert.Equal(iguais, IgualdadeNumerica.Iguais(um, outro));
        Assert.Equal(iguais, IgualdadeNumerica.Iguais(outro, um));
    }

    [Fact]
    public void A_amostra_cobre_toda_a_tabela()
    {
        Assert.Equal(FormatoFixo.Transformadores.Order(StringComparer.Ordinal), _transformadorDaForma.Values.Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void O_telefone_fica_fora_porque_o_servico_preservado_se_iguala_ao_celular_sem_DDD()
    {
        // O 0900 do formato antigo volta como veio; o pseudônimo de um celular sem DDD pode sair com os mesmos dígitos sem o
        // zero, e a comparação numérica junta os dois.
        Assert.True(IgualdadeNumerica.Iguais("0900123456", "900123456"));
        Assert.DoesNotContain("br.telefone@1", (IReadOnlySet<string>)FormatoFixo.Transformadores);
    }

    [Fact]
    public void A_chave_de_DFe_fica_fora_porque_o_emitente_alfanumerico_poe_letra_ao_lado_de_digito()
    {
        // A forma numérica cumpriria a invariante, mas a coluna recebe as duas: a alfanumérica derruba a chave inteira.
        var alfanumericas = IgualdadeNumerica.SaidasDaTabela(50, 34043).Where(par => par.Forma == "dfe-alfanumerica").Select(par => par.Saida).ToList();

        Assert.Contains(alfanumericas, saida => Enumerable.Range(1, saida.Length - 1).Any(indice => char.IsAsciiDigit(saida[indice]) != char.IsAsciiDigit(saida[indice - 1])));
        Assert.DoesNotContain("br.chave-dfe@1", (IReadOnlySet<string>)FormatoFixo.Transformadores);
    }

    [Fact]
    public void Saidas_distintas_da_tabela_nunca_se_igualam_pela_comparacao_numerica()
    {
        // Pelo modelo, a chave de cada saída é o texto com os zeros à esquerda de cada trecho de dígitos tirados: duas saídas
        // distintas com a mesma chave seriam iguais no índice.
        var saidas = IgualdadeNumerica.SaidasDaTabela(400, 34041).Where(par => _transformadorDaForma.ContainsKey(par.Forma)).Select(par => par.Saida).Distinct(StringComparer.Ordinal).ToList();
        var porChave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var saida in saidas)
        {
            var chave = Chave(saida);
            if (porChave.TryGetValue(chave, out var outra))
            {
                Assert.Fail($"Duas saídas da tabela se igualam pela comparação numérica: {outra} e {saida}.");
            }

            porChave[chave] = saida;
        }
    }

    [Fact]
    public void Toda_saida_da_tabela_tem_os_trechos_de_digitos_fixos_e_longe_das_letras_ou_sem_zero_inicial()
    {
        foreach (var grupo in IgualdadeNumerica.SaidasDaTabela(200, 34042).Where(par => _transformadorDaForma.ContainsKey(par.Forma)).GroupBy(par => par.Forma))
        {
            var saidas = grupo.Select(par => par.Saida).ToList();
            var semZeroInicial = saidas.All(saida => Trechos(saida).All(trecho => trecho[0] != '0'));
            var fixos = saidas.Select(Posicoes).Distinct(StringComparer.Ordinal).Count() == 1;
            var longeDasLetras = saidas.All(saida => !Enumerable.Range(1, saida.Length - 1).Any(indice => char.IsAsciiDigit(saida[indice]) != char.IsAsciiDigit(saida[indice - 1]) && char.IsLetter(saida[indice]) | char.IsLetter(saida[indice - 1])));

            Assert.True(semZeroInicial || (fixos && longeDasLetras), $"A forma {grupo.Key} ({_transformadorDaForma[grupo.Key]}) não cumpre a invariante da tabela de formato fixo.");
        }
    }

    private static string Chave(string texto) =>
        string.Concat(Separar(texto).Select(trecho => char.IsAsciiDigit(trecho[0]) ? "#" + (trecho.TrimStart('0') is { Length: > 0 } valor ? valor : "0") + "#" : trecho));

    private static IEnumerable<string> Trechos(string texto) => Separar(texto).Where(trecho => char.IsAsciiDigit(trecho[0]));

    // As posições dos dígitos da saída: dois textos da mesma forma precisam ter os mesmos trechos de dígitos.
    private static string Posicoes(string texto) => new([.. texto.Select(caractere => char.IsAsciiDigit(caractere) ? '9' : '.')]);

    private static List<string> Separar(string texto)
    {
        var trechos = new List<string>();
        for (var inicio = 0; inicio < texto.Length;)
        {
            var fim = inicio;
            while (fim < texto.Length && char.IsAsciiDigit(texto[fim]) == char.IsAsciiDigit(texto[inicio]))
            {
                fim++;
            }

            trechos.Add(texto[inicio..fim]);
            inicio = fim;
        }

        return trechos;
    }
}
