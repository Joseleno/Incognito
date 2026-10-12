using System.Diagnostics;

namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Faixas de CEP por UF dos Correios, comuns à pseudonimização e à geração de CEP: as de saída, em ordem crescente e
/// sem sobreposição, e as que só decidem a UF de uma entrada.
/// </summary>
/// <remarks>
/// A ordem e o conteúdo fazem parte das duas saídas: mudar uma faixa muda os pseudônimos de CEP (exige nova versão do
/// <c>br.cep</c>) e os CEPs gerados por semente (exige registro no CHANGELOG).
/// </remarks>
internal static class FaixasDeCep
{
    // Siglas na ordem dos índices usados nas faixas.
    private static readonly string[] _ufs =
        ["SP", "RJ", "ES", "MG", "BA", "SE", "PE", "AL", "PB", "RN", "CE", "PI", "MA", "PA", "AP", "AM", "RR", "AC", "DF", "GO", "RO", "TO", "MT", "MS", "PR", "SC", "RS"];

    // O terceiro campo é o índice da UF em _ufs.
    private static readonly FaixaDeCep[] _deSaida =
    [
        new(1_000_000, 19_999_999, 0),
        new(20_000_000, 28_999_999, 1),
        new(29_000_000, 29_999_999, 2),
        new(30_000_000, 39_999_999, 3),
        new(40_000_000, 48_999_999, 4),
        new(49_000_000, 49_999_999, 5),
        new(50_000_000, 56_999_999, 6),
        new(57_000_000, 57_999_999, 7),
        new(58_000_000, 58_999_999, 8),
        new(59_000_000, 59_999_999, 9),
        new(60_000_000, 63_999_999, 10),
        new(64_000_000, 64_999_999, 11),
        new(65_000_000, 65_999_999, 12),
        new(66_000_000, 68_899_999, 13),
        new(68_900_000, 68_999_999, 14),
        new(69_000_000, 69_299_999, 15),
        new(69_300_000, 69_399_999, 16),
        new(69_400_000, 69_899_999, 15),
        new(69_900_000, 69_999_999, 17),
        new(70_000_000, 72_799_999, 18),
        new(72_800_000, 72_999_999, 19),
        new(73_000_000, 73_699_999, 18),
        new(73_700_000, 76_799_999, 19),
        new(76_800_000, 76_999_999, 20),
        new(77_000_000, 77_999_999, 21),
        new(78_000_000, 78_899_999, 22),
        new(79_000_000, 79_999_999, 23),
        new(80_000_000, 87_999_999, 24),
        new(88_000_000, 89_999_999, 25),
        new(90_000_000, 99_999_999, 26),
    ];

    // A 78900-000 a 78999-999, de RO em tabelas antigas e fora da atual dos Correios, ainda pode estar em bases
    // antigas: decide a UF da entrada, mas nunca é escolhida para a saída.
    private static readonly FaixaDeCep[] _soDeEntrada =
    [
        new(78_900_000, 78_999_999, 20),
    ];

    // Quantidade de CEPs de saída de cada UF, somada uma vez a partir das faixas.
    private static readonly ulong[] _cepsPorUf = SomarPorUf();

    /// <summary>Siglas das 27 UFs, na ordem dos índices das faixas.</summary>
    internal static ReadOnlySpan<string> Ufs => _ufs;

    /// <summary>Faixas de saída, em ordem crescente.</summary>
    internal static ReadOnlySpan<FaixaDeCep> DeSaida => _deSaida;

    /// <summary>Faixas que só decidem a UF de uma entrada.</summary>
    internal static ReadOnlySpan<FaixaDeCep> SoDeEntrada => _soDeEntrada;

    /// <summary>Quantidade de CEPs nas faixas de saída de cada UF, pelo índice da UF.</summary>
    internal static ReadOnlySpan<ulong> CepsPorUf => _cepsPorUf;

    /// <summary>Acha a UF de um CEP nas faixas dadas.</summary>
    /// <param name="cep">O CEP como número.</param>
    /// <param name="faixas">As faixas consultadas.</param>
    /// <returns>O índice da UF, ou −1 se o CEP não está em nenhuma.</returns>
    internal static int UfDe(int cep, ReadOnlySpan<FaixaDeCep> faixas)
    {
        foreach (var faixa in faixas)
        {
            if (cep >= faixa.Inicio && cep <= faixa.Fim)
            {
                return faixa.Uf;
            }
        }

        return -1;
    }

    /// <summary>O CEP de uma posição nas faixas de saída de uma UF, contadas na ordem das faixas.</summary>
    /// <param name="uf">O índice da UF.</param>
    /// <param name="indice">A posição, menor que a quantidade de CEPs da UF (<see cref="CepsPorUf"/>).</param>
    /// <returns>O CEP como número.</returns>
    internal static int CepNaPosicao(int uf, ulong indice)
    {
        foreach (var faixa in _deSaida)
        {
            if (faixa.Uf != uf)
            {
                continue;
            }

            if (indice < faixa.Tamanho)
            {
                return faixa.Inicio + (int)indice;
            }

            indice -= faixa.Tamanho;
        }

        throw new UnreachableException("A posição saiu das faixas da UF.");
    }

    private static ulong[] SomarPorUf()
    {
        var quantidades = new ulong[_ufs.Length];
        foreach (var faixa in _deSaida)
        {
            quantidades[faixa.Uf] += faixa.Tamanho;
        }

        return quantidades;
    }
}
