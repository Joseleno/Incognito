using System.Globalization;
using System.Text.Json;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Testes.Vetores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Proveniência das entradas dos vetores fixos: toda entrada que não é literal se refaz a partir da origem registrada
/// (semente do <see cref="GeradorBr"/>, regra de construção ou inversa, e formas). Preso à versão maior 1 do gerador: uma
/// mudança na sequência quebra aqui, não nos vetores. Literal de CPF ou CNPJ nunca é documento válido, e nenhum par traz
/// CNPJ válido de raiz 00000000 com ordem numérica, que é cadastrado.
/// </summary>
public sealed class ProvenienciaDosVetoresTestes
{
    public static TheoryData<string> Arquivos => [.. ReceitasDeVetores.Todas.Select(r => r.Arquivo)];

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void Toda_entrada_se_refaz_a_partir_da_origem(string arquivo)
    {
        var receita = ReceitasDeVetores.Todas.Single(r => r.Arquivo == arquivo);
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);
        using var chave = ChaveIncognito.Ler(documento.RootElement.GetProperty("chave").GetString());
        var refeitas = 0;
        foreach (var caso in documento.RootElement.GetProperty("casos").EnumerateArray())
        {
            var contexto = ContextoDeOrigem.Do(chave, receita.Versao, caso);
            var indice = 0;
            foreach (var par in caso.GetProperty("pares").EnumerateArray())
            {
                var onde = $"{arquivo}, caso '{caso.GetProperty("nome").GetString()}', par {indice++}";
                var origem = par.GetProperty("origem");
                var entrada = EntradaDoPar.Ler(par);
                if (origem.GetProperty("tipo").GetString() == "literal")
                {
                    Assert.False(EhDocumentoValido(receita.Transformador, entrada), $"{onde}: literal com documento válido.");
                    continue;
                }

                var refeita = OrigensDeVetores.Construir(origem, contexto);
                var confere = refeita switch
                {
                    long numero => entrada.EhNumero && entrada.Numero == numero,
                    string texto => !entrada.EhNumero && string.Equals(entrada.Texto, texto, StringComparison.Ordinal),
                    _ => false,
                };
                Assert.True(confere, $"{onde}: a entrada não se refaz a partir da origem.");
                refeitas++;
            }
        }

        Assert.True(refeitas > 0, $"{arquivo}: nenhuma entrada refeita.");
    }

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void Nenhum_par_traz_cnpj_cadastrado_de_raiz_zero(string arquivo)
    {
        // Inteiro só é CNPJ na coluna de CNPJ; nas outras, os 14 dígitos com zeros à esquerda são outro número.
        var numeroECnpj = arquivo.StartsWith("br.cnpj@", StringComparison.Ordinal);
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);
        var indice = 0;
        foreach (var par in documento.RootElement.GetProperty("casos").EnumerateArray().SelectMany(c => c.GetProperty("pares").EnumerateArray()))
        {
            var entrada = EntradaDoPar.Ler(par);
            var saida = par.TryGetProperty("saida", out var s) ? s : default;
            string?[] valores =
            [
                entrada.EhNumero ? numeroECnpj ? Catorze(entrada.Numero) : null : entrada.Texto,
                saida.ValueKind switch
                {
                    JsonValueKind.String => saida.GetString(),
                    JsonValueKind.Number when numeroECnpj => Catorze(saida.GetInt64()),
                    _ => null,
                },
            ];
            Assert.False(valores.Any(EhCnpjDeRaizZeroComOrdemNumerica), $"{arquivo}, par {indice}: CNPJ válido de raiz 00000000 com ordem numérica.");
            indice++;
        }
    }

    private static string? Catorze(long numero) =>
        numero is >= 0 and <= 99_999_999_999_999 ? numero.ToString("D14", CultureInfo.InvariantCulture) : null;

    // Com ou sem máscara, com as letras em qualquer caixa (a chave Pix aceita minúsculas), e com 12 ou 13 dígitos, que o
    // transformador completa com zeros. O CNPJ todo zerado é valor de preenchimento (a ordem 0000 não existe).
    private static bool EhCnpjDeRaizZeroComOrdemNumerica(string? valor)
    {
        if (valor is null)
        {
            return false;
        }

        var cnpj = valor.TrimEnd(' ').Replace(".", "", StringComparison.Ordinal).Replace("/", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        if (cnpj.Length is 12 or 13 && cnpj.All(char.IsAsciiDigit))
        {
            cnpj = cnpj.PadLeft(14, '0');
        }

        return cnpj.Length == 14
            && cnpj.StartsWith("00000000", StringComparison.Ordinal)
            && cnpj.All(char.IsAsciiDigit)
            && cnpj != "00000000000000"
            && DigitosVerificadores.Cnpj(cnpj.AsSpan(0, 12)) == ((cnpj[12] - '0') * 10) + (cnpj[13] - '0');
    }

    private static bool EhDocumentoValido(string transformador, EntradaDoPar entrada)
    {
        var texto = entrada.EhNumero
            ? entrada.Numero >= 0 ? entrada.Numero.ToString(transformador == "br.cnpj" ? "D14" : "D11", CultureInfo.InvariantCulture) : ""
            : entrada.Texto!;
        return transformador switch
        {
            "br.cpf" => ValidadorBr.Cpf(texto),
            "br.cnpj" => ValidadorBr.Cnpj(texto),
            "br.chave-pix" => ValidadorBr.Cpf(texto) || ValidadorBr.Cnpj(texto),
            _ => false,
        };
    }
}
