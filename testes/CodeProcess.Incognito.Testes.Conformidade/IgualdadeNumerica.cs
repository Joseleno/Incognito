using System.Globalization;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// O modelo da igualdade de uma colação de comparação numérica (ICU <c>kn</c>) com as três marcas falsas: dois textos são
/// iguais quando os trechos que não são dígitos são idênticos e cada trecho de dígitos tem o mesmo valor (os zeros à
/// esquerda não contam). O .NET não reproduz o ICU do servidor: um fato do dialeto PostgreSQL prende o modelo ao servidor.
/// </summary>
public static class IgualdadeNumerica
{
    /// <summary>Se os dois textos são iguais pela comparação numérica.</summary>
    /// <param name="um">Um texto.</param>
    /// <param name="outro">O outro.</param>
    /// <returns>Se são iguais.</returns>
    public static bool Iguais(string um, string outro)
    {
        ArgumentNullException.ThrowIfNull(um);
        ArgumentNullException.ThrowIfNull(outro);
        var (deUm, deOutro) = (Trechos(um), Trechos(outro));
        return deUm.Count == deOutro.Count
            && deUm.Zip(deOutro).All(par => par.First.Digitos == par.Second.Digitos
                && (par.First.Digitos ? SemZeros(par.First.Texto) == SemZeros(par.Second.Texto) : par.First.Texto == par.Second.Texto));
    }

    /// <summary>
    /// Uma amostra de saídas reais de embutidas (CPF, com e sem máscara e o preservado; e-mail; e, fora da tabela de formato
    /// fixo, celular e chave de DF-e numérica e alfanumérica), pelo <see cref="Pseudonimizador"/> com uma chave derivada da
    /// semente: o que um índice com essa colação receberia.
    /// </summary>
    /// <param name="quantidade">Quantas entradas de cada forma.</param>
    /// <param name="semente">A semente do gerador das entradas.</param>
    /// <returns>As saídas, por transformador e forma de entrada.</returns>
    public static IReadOnlyList<(string Forma, string Saida)> SaidasDaTabela(int quantidade, int semente)
    {
        var sorteio = new Random(semente);
        var bytes = new byte[32];
        sorteio.NextBytes(bytes);
        bytes[0] |= 1;
        using var chave = ChaveIncognito.Ler("inc1_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        using var pseudonimizador = new Pseudonimizador(chave, "homologacao");
        var gerador = new GeradorBr(semente);
        string[] ddds = ["11", "21", "31", "41", "51", "61", "71", "81", "91"];
        var saidas = new List<(string, string)>();
        for (var indice = 0; indice < quantidade; indice++)
        {
            saidas.Add(("cpf", pseudonimizador.Cpf(gerador.Cpf())));
            saidas.Add(("cpf-mascara", pseudonimizador.Cpf(gerador.Cpf(formatado: true))));
            var repetido = (char)('0' + (indice % 10));
            saidas.Add(("cpf-preservado", pseudonimizador.Cpf(new string(repetido, 11))));
            var ddd = ddds[sorteio.Next(ddds.Length)];
            var celular = "9" + sorteio.Next(60_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture);
            saidas.Add(("celular", pseudonimizador.Telefone(ddd + celular)));
            saidas.Add(("celular-mascara", pseudonimizador.Telefone($"({ddd}) {celular[..5]}-{celular[5..]}")));
            saidas.Add(("dfe", pseudonimizador.ChaveDfe(gerador.ChaveDfe(55))));
            saidas.Add(("dfe-alfanumerica", pseudonimizador.ChaveDfe(gerador.ChaveDfe(55, alfanumerico: true))));
            saidas.Add(("email", pseudonimizador.Email($"pessoa{indice.ToString(CultureInfo.InvariantCulture)}@exemplo.com.br")));
        }

        return saidas;
    }

    private static string SemZeros(string digitos) => digitos.TrimStart('0') is { Length: > 0 } valor ? valor : "0";

    private static List<(bool Digitos, string Texto)> Trechos(string texto)
    {
        var trechos = new List<(bool, string)>();
        for (var inicio = 0; inicio < texto.Length;)
        {
            var digitos = char.IsAsciiDigit(texto[inicio]);
            var fim = inicio;
            while (fim < texto.Length && char.IsAsciiDigit(texto[fim]) == digitos)
            {
                fim++;
            }

            trechos.Add((digitos, texto[inicio..fim]));
            inicio = fim;
        }

        return trechos;
    }
}
