using CodeProcess.Incognito.Configuracao;
using CsCheck;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// Texto qualquer nunca derruba a leitura: ou sai uma configuração, ou a <see cref="ConfiguracaoInvalidaException"/>.
/// Gera texto solto e YAML montado de pedaços (aninhamento, âncoras, aliases, tags, aspas, blocos, chaves repetidas).
/// </summary>
public sealed class PropriedadeDaLeituraTestes
{
    private static readonly string[] _pedacos =
    [
        "versao: 1\n", "origem:\n", "destino:\n", "  provedor: postgresql\n", "  conexao: ambiente:X\n", "  bancoEsperado: b\n",
        "modo: reproduzivel\n", "chave: arquivo:/x\n", "ambiente: a\n", "dominios:\n", "tabelas:\n", "  public.t:\n",
        "    colunas:\n", "      c: manter\n", "      d: { transformador: br.cpf@1, derivarDe: c }\n", "  cpf: { transformador: br.cpf }\n",
        "&a ", "*a ", "!!str ", "!x ", "? ", ": ", "- ", "[", "]", "{", "}", ",", "'", "\"", "\\", "|\n", ">-\n", "#", "%YAML 1.2\n",
        "---\n", "...\n", "~", "null", "true", "0x1F", ".nan", "  ", "\t", "\n", "\r\n", "a", "1", "inc1_", "=", ";",
        "\"\\x41\"", "\"\\u00e9\"", "@", "`", "<<: ", "é", ((char)0x2028).ToString(), ((char)0xFEFF).ToString(),
        char.ConvertFromUtf32(0x1F600),
    ];

    [Fact]
    public void Texto_solto_so_gera_a_excecao_da_configuracao()
    {
        Gen.String[Gen.Char[" \t\n\r:-[]{}&*!%#'\"|>?,.@~\\abc019"], 0, 300].Sample(LerSemOutraExcecao, iter: 3000);
    }

    [Fact]
    public void Yaml_montado_de_pedacos_so_gera_a_excecao_da_configuracao()
    {
        Gen.Int[0, _pedacos.Length - 1].Select(indice => _pedacos[indice]).Array[1, 80].Select(string.Concat).Sample(LerSemOutraExcecao, iter: 5000);
    }

    private static void LerSemOutraExcecao(string texto)
    {
        try
        {
            ConfiguracaoIncognito.LerYaml(texto);
        }
        catch (ConfiguracaoInvalidaException excecao)
        {
            Assert.NotEmpty(excecao.Erros);
            Assert.Null(excecao.InnerException);
        }
    }
}
