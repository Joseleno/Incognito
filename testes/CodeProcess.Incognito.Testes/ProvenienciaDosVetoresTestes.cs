using System.Globalization;
using System.Text.Json;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Testes.Vetores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Proveniência das entradas dos vetores fixos: toda entrada que não é literal se refaz a partir da origem registrada
/// (semente do <see cref="GeradorBr"/> ou do <see cref="GeradorChaveDfe"/>, regra de construção ou inversa, e formas).
/// Preso à versão maior 1 do gerador: uma mudança na sequência quebra aqui, não nos vetores. Literal de CPF, CNPJ ou chave
/// de DF-e nunca é documento válido (fora a chave de 44 zeros, que não é de ninguém), nenhum par traz CNPJ válido de raiz
/// 00000000 com ordem numérica, que é cadastrado, nem no emitente da chave de DF-e, e nenhuma chave de DF-e, de entrada ou
/// de saída, traz um emitente de raiz baixa com DV certo (<see cref="OrigensChaveDfe.RaizBaixaComDvCerto"/>).
/// </summary>
public sealed class ProvenienciaDosVetoresTestes
{
    // Ramos da chave de DF-e cuja entrada não tem uma das formas aceitas: a chave dentro deles é achada por janela.
    private static readonly string[] _ramosSemChave =
    [
        "vazio", "chave-da-nfse", "tamanho", "grupos-com-outro-separador", "grupos-com-espaco-duplo", "grupos-com-prefixo",
        "espaco-a-esquerda", "prefixo-desconhecido", "zerada-com-prefixo-desconhecido",
    ];

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
            Assert.False(
                arquivo == "br.chave-dfe@1.json" && valores.SelectMany(OrigensChaveDfe.Janelas).Any(janela => EhCnpjDeRaizZeroComOrdemNumerica(janela[6..20])),
                $"{arquivo}, par {indice}: emitente da chave de DF-e com CNPJ válido de raiz 00000000 e ordem numérica.");
            indice++;
        }
    }

    [Fact]
    public void Nenhuma_chave_de_DF_e_traz_emitente_de_raiz_baixa_com_DV_certo()
    {
        // Raízes 00xxxxxx são de bancos, órgãos públicos e estatais: nas entradas, esses emitentes saem com o DV
        // deslocado, e o gerador pula a semente cuja saída traria um.
        using var documento = EsquemaDeVetores.LerDaSaida("br.chave-dfe@1.json");
        var indice = 0;
        foreach (var par in documento.RootElement.GetProperty("casos").EnumerateArray().SelectMany(c => c.GetProperty("pares").EnumerateArray()))
        {
            var onde = $"br.chave-dfe@1.json, par {indice++}";
            var entrada = EntradaDoPar.Ler(par).Texto;
            var saida = par.TryGetProperty("saida", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var semChave = _ramosSemChave.Contains(par.GetProperty("ramo").GetString(), StringComparer.Ordinal);
            Assert.True(semChave == (OrigensChaveDfe.ChaveDaForma(entrada) is null), $"{onde}: forma da entrada diferente da do ramo.");

            // Nas formas aceitas, a chave alinhada; nas outras, toda janela que tem a estrutura de uma chave.
            IEnumerable<string> chaves = semChave
                ? OrigensChaveDfe.Janelas(entrada)
                : [OrigensChaveDfe.ChaveDaForma(entrada)!, .. OrigensChaveDfe.ChaveDaForma(saida) is { } daSaida ? [daSaida] : Array.Empty<string>()];
            Assert.False(chaves.Any(chave => OrigensChaveDfe.RaizBaixaComDvCerto(chave.AsSpan(6, 14))), $"{onde}: emitente de raiz baixa com DV certo.");
        }

        Assert.True(indice > 280, "Poucas chaves conferidas.");
    }

    [Fact]
    public void Regra_da_raiz_baixa_so_recusa_raiz_00_com_ordem_de_estabelecimento_e_DV_certo()
    {
        // Emitentes sorteados na hora (raiz começando com 00, 01 ou 000; ordem numérica, com letra ou 0000), com o DV certo
        // e deslocado: só a raiz 00 com ordem diferente de 0000 e DV certo é recusada.
        var fonte = SequenciaXoshiro.DaSemente(2920);
        string[] inicios = ["00", "000", "01"];
        for (var vez = 0; vez < 300; vez++)
        {
            var inicio = inicios[vez % inicios.Length];
            var raiz = inicio + OrigensDeVetores.Sortear(fonte, "0123456789", 8 - inicio.Length);
            var ordem = (vez % 4) switch
            {
                0 => "0000",
                1 => OrigensDeVetores.Sortear(fonte, "0123456789", 4),
                _ => OrigensDeVetores.Sortear(fonte, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", 1) + OrigensDeVetores.Sortear(fonte, "0123456789", 3),
            };
            var base12 = raiz + ordem;
            var dv = DigitosVerificadores.Cnpj(base12);
            var recusavel = raiz.StartsWith("00", StringComparison.Ordinal) && ordem != "0000";

            Assert.Equal(recusavel, OrigensChaveDfe.RaizBaixaComDvCerto(base12 + dv.ToString("00", CultureInfo.InvariantCulture)));
            Assert.False(OrigensChaveDfe.RaizBaixaComDvCerto(base12 + ((dv + 1) % 100).ToString("00", CultureInfo.InvariantCulture)), "Com o DV deslocado, o emitente não é CNPJ válido.");
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
            "br.chave-dfe" => OrigensChaveDfe.Janelas(texto).Any(ValidadorBr.ChaveDfe),
            _ => false,
        };
    }
}
