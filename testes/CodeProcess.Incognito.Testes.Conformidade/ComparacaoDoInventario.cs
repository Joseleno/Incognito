using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A comparação de um esquema lido com o inventário, por nome: a primeira diferença de cada tabela e de cada sequence,
/// em ordem ordinal de esquema e nome (as tabelas antes das sequences).
/// </summary>
internal static class ComparacaoDoInventario
{
    internal static IEnumerable<string> Diferencas(EsquemaBanco lido, Inventario esperado)
    {
        var tabelasLidas = lido.Tabelas.ToLookup(tabela => tabela.Nome);
        var tabelasEsperadas = esperado.Tabelas.ToLookup(tabela => tabela.Nome);
        foreach (var nome in Nomes(tabelasLidas, tabelasEsperadas))
        {
            var diferenca = (tabelasLidas[nome].ToArray(), tabelasEsperadas[nome].ToArray()) switch
            {
                ([], _) => "tabela a menos",
                (_, []) => "tabela a mais",
                (_, [_, _, ..] esperadas) => $"tabela declarada {esperadas.Length} vezes",
                ([_, _, ..] lidas, _) => $"tabela lida {lidas.Length} vezes",
                ([var tabela], [var esperada]) => Diferenca(tabela, esperada),
            };
            if (diferenca is not null)
            {
                yield return $"{nome}: {diferenca}";
            }
        }

        var sequencesLidas = lido.Sequences.ToLookup(sequence => sequence.Nome);
        var sequencesEsperadas = esperado.Sequences.ToLookup(sequence => sequence.Nome);
        foreach (var nome in Nomes(sequencesLidas, sequencesEsperadas))
        {
            var diferenca = (sequencesLidas[nome].ToArray(), sequencesEsperadas[nome].ToArray()) switch
            {
                ([], _) => "sequence a menos",
                (_, []) => "sequence a mais",
                (_, [_, _, ..] esperadas) => $"sequence declarada {esperadas.Length} vezes",
                ([_, _, ..] lidas, _) => $"sequence lida {lidas.Length} vezes",
                ([var sequence], [var esperada]) => MesmosItens(sequence.Colunas, esperada.Colunas)
                    ? null
                    : $"sequence usada por [{Lista(Ordenadas(sequence.Colunas))}], esperada por [{Lista(Ordenadas(esperada.Colunas))}]",
            };
            if (diferenca is not null)
            {
                yield return $"{nome}: {diferenca}";
            }
        }
    }

    private static string? Diferenca(TabelaEsquema tabela, TabelaInventariada esperada)
    {
        if (tabela.Tipo != esperada.Tipo)
        {
            return $"tipo lido {tabela.Tipo}, esperado {esperada.Tipo}";
        }

        if (!Equals(tabela.Pai, esperada.Pai))
        {
            return $"pai lido {Texto(tabela.Pai)}, esperado {Texto(esperada.Pai)}";
        }

        if (!tabela.OutrosPais.SequenceEqual(esperada.OutrosPais))
        {
            return $"outros pais lidos [{Lista(tabela.OutrosPais)}], esperados [{Lista(esperada.OutrosPais)}]";
        }

        if (!Equals(tabela.Historico, esperada.Historico))
        {
            return $"histórico lido {Texto(tabela.Historico)}, esperado {Texto(esperada.Historico)}";
        }

        var chave = tabela.ChaveParticao is { } lida ? new ChaveParticaoEsperada(lida.Colunas, lida.ColunasIncompletas) : null;
        if (!MesmaChave(chave, esperada.ChaveParticao))
        {
            return $"chave de partição lida {Texto(chave)}, esperada {Texto(esperada.ChaveParticao)}";
        }

        if (tabela.Estrangeira != esperada.Estrangeira)
        {
            return $"estrangeira lida {tabela.Estrangeira}, esperada {esperada.Estrangeira}";
        }

        if (tabela.Extensao != esperada.Extensao)
        {
            return $"extensão lida {Texto(tabela.Extensao)}, esperada {Texto(esperada.Extensao)}";
        }

        if (tabela.SegurancaDeLinha != esperada.SegurancaDeLinha)
        {
            return $"segurança por linha lida {tabela.SegurancaDeLinha}, esperada {esperada.SegurancaDeLinha}";
        }

        var colunas = tabela.Colunas.Select(coluna => coluna.Nome).ToArray();
        var colunasEsperadas = esperada.Colunas.Select(coluna => coluna.Nome).ToArray();
        if (colunas.FirstOrDefault(coluna => !colunasEsperadas.Contains(coluna)) is { } aMais)
        {
            return $"coluna {aMais} a mais";
        }

        if (colunasEsperadas.FirstOrDefault(coluna => !colunas.Contains(coluna)) is { } aMenos)
        {
            return $"coluna {aMenos} a menos";
        }

        // Mesmos nomes em outra ordem ou com repetição.
        if (!colunas.SequenceEqual(colunasEsperadas))
        {
            return $"colunas lidas [{Lista(colunas)}], esperadas [{Lista(colunasEsperadas)}]";
        }

        foreach (var (coluna, colunaEsperada) in tabela.Colunas.Zip(esperada.Colunas))
        {
            if (Diferenca(coluna, colunaEsperada) is { } diferenca)
            {
                return $"coluna {coluna.Nome}: {diferenca}";
            }
        }

        if (!tabela.ChavePrimaria.SequenceEqual(esperada.ChavePrimaria))
        {
            return $"chave primária lida [{Lista(tabela.ChavePrimaria)}], esperada [{Lista(esperada.ChavePrimaria)}]";
        }

        var chaves = tabela.ChavesEstrangeiras.ToLookup(chave => chave.Nome, StringComparer.Ordinal);
        var chavesEsperadas = esperada.ChavesEstrangeiras.ToLookup(chave => chave.Nome, StringComparer.Ordinal);
        var nomes = chaves.Select(grupo => grupo.Key).Union(chavesEsperadas.Select(grupo => grupo.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var nome in nomes)
        {
            var diferenca = (chaves[nome].ToArray(), chavesEsperadas[nome].ToArray()) switch
            {
                ([], _) => " a menos",
                (_, []) => " a mais",
                (_, [_, _, ..] declaradas) => $" declarada {declaradas.Length} vezes",
                ([_, _, ..] lidas, _) => $" lida {lidas.Length} vezes",
                ([var unica], [var declarada]) => Diferenca(unica, declarada),
            };
            if (diferenca is not null)
            {
                return $"chave estrangeira {nome}{diferenca}";
            }
        }

        return null;
    }

    private static string? Diferenca(ChaveEstrangeira chave, ChaveEstrangeiraInventariada esperada) =>
        !chave.Colunas.SequenceEqual(esperada.Colunas) ? $": colunas lidas [{Lista(chave.Colunas)}], esperadas [{Lista(esperada.Colunas)}]"
        : !chave.Referenciada.Equals(esperada.Referenciada) ? $": referenciada lida {chave.Referenciada}, esperada {esperada.Referenciada}"
        : !chave.ColunasReferenciadas.SequenceEqual(esperada.ColunasReferenciadas)
            ? $": colunas referenciadas lidas [{Lista(chave.ColunasReferenciadas)}], esperadas [{Lista(esperada.ColunasReferenciadas)}]"
        : null;

    private static string? Diferenca(ColunaEsquema coluna, ColunaInventariada esperada) =>
        coluna.TipoNativo != esperada.TipoNativo ? $"tipo nativo lido {coluna.TipoNativo}, esperado {esperada.TipoNativo}"
        : coluna.Anulavel != esperada.Anulavel ? $"anulável lida {coluna.Anulavel}, esperada {esperada.Anulavel}"
        : coluna.Identidade != esperada.Identidade ? $"identity lida {coluna.Identidade}, esperada {esperada.Identidade}"
        : coluna.Geracao != esperada.Geracao ? $"geração lida {coluna.Geracao}, esperada {esperada.Geracao}"
        : coluna.TipoBase != esperada.TipoBase ? $"tipo base lido {Texto(coluna.TipoBase)}, esperado {Texto(esperada.TipoBase)}"
        : null;

    private static bool MesmaChave(ChaveParticaoEsperada? lida, ChaveParticaoEsperada? esperada) =>
        lida is null || esperada is null
            ? lida is null && esperada is null
            : lida.ColunasIncompletas == esperada.ColunasIncompletas && lida.Colunas.SequenceEqual(esperada.Colunas);

    private static IEnumerable<NomeObjeto> Nomes<TLido, TEsperado>(ILookup<NomeObjeto, TLido> lidos, ILookup<NomeObjeto, TEsperado> esperados) =>
        lidos.Select(grupo => grupo.Key).Union(esperados.Select(grupo => grupo.Key))
            .OrderBy(nome => nome.Esquema, StringComparer.Ordinal)
            .ThenBy(nome => nome.Nome, StringComparer.Ordinal);

    // Mesmos itens, na mesma quantidade.
    private static bool MesmosItens(IReadOnlyCollection<ReferenciaColuna> lidas, IReadOnlyCollection<ReferenciaColuna> esperadas) =>
        lidas.Count == esperadas.Count && lidas.Distinct().Count() == lidas.Count && lidas.ToHashSet().SetEquals(esperadas);

    private static IEnumerable<string> Ordenadas(IEnumerable<ReferenciaColuna> colunas) =>
        colunas.Select(coluna => coluna.ToString()).Order(StringComparer.Ordinal);

    private static string Lista<T>(IEnumerable<T> itens) => string.Join(", ", itens);

    private static string Texto(object? valor) => valor switch
    {
        null => "nenhum",
        ChaveParticaoEsperada chave => $"({Lista(chave.Colunas)}{(chave.ColunasIncompletas ? "; incompleta" : "")})",
        _ => valor.ToString()!,
    };
}
