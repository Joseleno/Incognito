using System.Collections.Frozen;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>Uma política que faz a regra produzir NULL.</summary>
/// <param name="Politica">A política como se escreve (<c>entradaInvalida: anular</c>).</param>
/// <param name="Dominio">O domínio citado de onde ela vem (<c>dominioCpf</c> da chave Pix); nulo quando é da própria regra.</param>
/// <param name="Parametro">O parâmetro que cita o domínio; nulo junto com <paramref name="Dominio"/>.</param>
internal sealed record PoliticaQueAnula(string Politica, string? Dominio, string? Parametro);

/// <summary>
/// As políticas que fazem uma regra produzir NULL, definidas num ponto só, porque o núcleo aplica cada política conforme o
/// transformador: nas embutidas, a <c>entradaMalformada</c> nas que leem o formato (todas menos o <c>texto.substituir</c>,
/// que troca o texto inteiro) e a <c>entradaInvalida</c> só nas que têm DV (CPF, CNPJ, chave de DF-e); na chave Pix, a
/// <c>entradaInvalida</c> dos domínios de documento citados (a da própria regra não se aplica); numa fábrica de fora, as
/// duas, como pior caso.
/// </summary>
internal static class PoliticasQueAnulam
{
    /// <summary>As embutidas que usam a <c>entradaInvalida</c> da própria regra; as outras a ignoram.</summary>
    internal static readonly FrozenSet<string> UsamEntradaInvalida = new[] { "br.cpf", "br.cnpj", "br.chave-dfe" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>As embutidas que ignoram a <c>entradaMalformada</c>: nunca anulam nem copiam em claro pelo formato.</summary>
    internal static readonly FrozenSet<string> IgnoramEntradaMalformada = new[] { "texto.substituir" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>A embutida que aplica a <c>entradaInvalida</c> dos domínios citados, e não a dela.</summary>
    internal const string ChavePix = "br.chave-pix";

    /// <summary>As políticas da regra que produzem NULL, na ordem: as da própria regra, depois as dos citados por parâmetro.</summary>
    /// <param name="coluna">A coluna resolvida.</param>
    /// <param name="dominios">A tabela de domínios, de onde saem as políticas dos citados.</param>
    /// <returns>As políticas; vazia quando a regra nunca produz NULL.</returns>
    internal static IReadOnlyList<PoliticaQueAnula> Da(ColunaResolvida coluna, IReadOnlyList<DefinicaoDominio> dominios)
    {
        ArgumentNullException.ThrowIfNull(coluna);
        ArgumentNullException.ThrowIfNull(dominios);
        var regra = coluna.Regra;
        var fabrica = coluna.Fabrica;
        var politicas = new List<PoliticaQueAnula>();
        var embutida = fabrica.Embutida;
        var invalidaPropria = !embutida || UsamEntradaInvalida.Contains(fabrica.Nome);
        if (invalidaPropria && regra.EntradaInvalida == PoliticaEntradaInvalida.Anular)
        {
            politicas.Add(new PoliticaQueAnula("entradaInvalida: anular", null, null));
        }

        if (UsaEntradaMalformada(fabrica) && regra.EntradaMalformada == PoliticaEntradaMalformada.Anular)
        {
            politicas.Add(new PoliticaQueAnula("entradaMalformada: anular", null, null));
        }

        if (embutida && fabrica.Nome == ChavePix)
        {
            foreach (var (parametro, transformador) in fabrica.ParametrosDeDominio.OrderBy(par => par.Key, StringComparer.Ordinal))
            {
                if (!UsamEntradaInvalida.Contains(transformador) || !coluna.Normalizados.TryGetValue(parametro, out var dominio))
                {
                    continue;
                }

                if (dominios.FirstOrDefault(definicao => definicao.Nome == dominio) is { Regra.EntradaInvalida: PoliticaEntradaInvalida.Anular })
                {
                    politicas.Add(new PoliticaQueAnula("entradaInvalida: anular", dominio, parametro));
                }
            }
        }

        return politicas;
    }

    /// <summary>Se o transformador aplica a <c>entradaMalformada</c>: toda fábrica de fora, como pior caso.</summary>
    /// <param name="fabrica">A fábrica registrada.</param>
    /// <returns>Se aplica.</returns>
    internal static bool UsaEntradaMalformada(FabricaRegistrada fabrica) => !fabrica.Embutida || !IgnoramEntradaMalformada.Contains(fabrica.Nome);
}
