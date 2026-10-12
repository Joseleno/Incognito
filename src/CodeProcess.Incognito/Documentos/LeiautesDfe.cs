using System.Diagnostics.CodeAnalysis;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Documentos;

/// <summary>Os leiautes dos nove modelos de DF-e, pelo código das posições 21 e 22.</summary>
/// <remarks>
/// Um array de 100 posições em vez de dicionário: a busca não calcula hash nem aloca, e um código sem modelo cai em
/// nulo.
/// </remarks>
internal static class LeiautesDfe
{
    private const int Codigos = 100;

    // NF-e e NFC-e (NT 2021.002, item 2.1): dia, mês, tipo do emissor e sequencial de 4 dígitos.
    private static readonly LeiauteNff _nffNfe =
        new(indiceDia: 25, indiceMes: 27, indiceTipo: 29, indiceSequencial: 30, tamanhoSequencial: 4);

    // CT-e e MDF-e (NT CT-e 2020.001 e NT MDF-e 2020.002, v1.05): mês, dia e sequencial de 5 dígitos.
    private static readonly LeiauteNff _nffTransporte =
        new(indiceDia: 27, indiceMes: 25, indiceTipo: null, indiceSequencial: 29, tamanhoSequencial: 5);

    private static readonly LeiauteDfe?[] _porCodigo = Montar(
    [
        new(ModeloDfe.Nfe, RegraEmitente.SeriesComPaa, 8, _nffNfe),
        new(ModeloDfe.Nfce, RegraEmitente.Series, 8, _nffNfe),
        new(ModeloDfe.Cte, RegraEmitente.Zeros, 8, _nffTransporte),
        new(ModeloDfe.Mdfe, RegraEmitente.Zeros, 8, _nffTransporte),
        new(ModeloDfe.CteOs, RegraEmitente.SempreCnpj, 8, null),
        new(ModeloDfe.Gtve, RegraEmitente.SempreCnpj, 8, null),
        new(ModeloDfe.Bpe, RegraEmitente.SempreCnpj, 8, null),
        new(ModeloDfe.Nf3e, RegraEmitente.SempreCnpj, 7, null),
        new(ModeloDfe.Nfcom, RegraEmitente.SempreCnpj, 7, null),
    ]);

    /// <summary>O leiaute de um modelo.</summary>
    /// <param name="modelo">Um dos nove modelos.</param>
    /// <returns>O leiaute.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Valor fora dos nove modelos.</exception>
    internal static LeiauteDfe Obter(ModeloDfe modelo) =>
        (int)modelo < Codigos && _porCodigo[(int)modelo] is { } leiaute
            ? leiaute
            : throw new ArgumentOutOfRangeException(nameof(modelo), "Modelo de DF-e sem leiaute.");

    /// <summary>O leiaute pelo código do modelo, como está na chave.</summary>
    /// <param name="codigo">Os dois caracteres das posições 21 e 22.</param>
    /// <param name="leiaute">O leiaute, ou nulo se o código não é de um dos nove modelos.</param>
    /// <returns><see langword="true"/> se o código é de um dos nove modelos.</returns>
    internal static bool TentarObter(ReadOnlySpan<char> codigo, [NotNullWhen(true)] out LeiauteDfe? leiaute)
    {
        leiaute = codigo.Length == 2 && SoDigitos(codigo) ? _porCodigo[Valor(codigo)] : null;
        return leiaute is not null;
    }

    private static LeiauteDfe?[] Montar(ReadOnlySpan<LeiauteDfe> leiautes)
    {
        var porCodigo = new LeiauteDfe?[Codigos];
        foreach (var leiaute in leiautes)
        {
            porCodigo[(int)leiaute.Modelo] = leiaute;
        }

        return porCodigo;
    }
}
