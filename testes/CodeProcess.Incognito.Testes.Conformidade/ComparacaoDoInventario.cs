using System.Text.Json;
using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A comparação de um esquema lido com o inventário, por nome: a primeira diferença de cada tabela, de cada sequence e de
/// cada objeto residual, em ordem ordinal de esquema e nome (as tabelas, depois as sequences, depois os residuais), e os
/// objetos do esquema reservado, na ordem do contrato. Numa tabela, a ordem dos ramos é fixa (decide a mensagem):
/// estrutura, colunas (tipo base, situação, tipo canônico e a semântica), chave primária, os objetos da tabela (índices
/// únicos, CHECKs, exclusões e triggers, cada lista também na ordem do contrato), chaves estrangeiras.
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

        var residuaisLidos = lido.ObjetosResiduais.ToLookup(residual => residual.Nome);
        var residuaisEsperados = esperado.Residuais.ToLookup(residual => residual.Nome);
        foreach (var nome in Nomes(residuaisLidos, residuaisEsperados))
        {
            var diferenca = (residuaisLidos[nome].ToArray(), residuaisEsperados[nome].ToArray()) switch
            {
                ([], _) => "residual a menos",
                (_, []) => "residual a mais",
                (_, [_, _, ..] esperados) => $"residual declarado {esperados.Length} vezes",
                ([_, _, ..] lidos, _) => $"residual lido {lidos.Length} vezes",
                ([var residual], [var esperadoResidual]) => Diferenca(residual, esperadoResidual),
            };
            if (diferenca is not null)
            {
                yield return $"residual {nome}: {diferenca}";
            }
        }

        // Na ordem do contrato; nulo diz que o dialeto não informa, o que esconderia o que há no esquema reservado.
        if (lido.ObjetosNoEsquemaReservado is not { } reservado)
        {
            yield return $"esquema reservado: objetos não informados, esperados [{Lista(esperado.ObjetosNoEsquemaReservado)}]";
        }
        else if (!reservado.SequenceEqual(esperado.ObjetosNoEsquemaReservado))
        {
            yield return $"esquema reservado: objetos lidos [{Lista(reservado)}], esperados [{Lista(esperado.ObjetosNoEsquemaReservado)}]";
        }

        // Os gatilhos de evento ligados, na ordem do contrato; nulo diz que o dialeto não informa.
        if (lido.TriggersDeEvento is not { } eventos)
        {
            yield return $"gatilhos de evento: não informados, esperados [{Lista(esperado.TriggersDeEvento)}]";
        }
        else if (!eventos.SequenceEqual(esperado.TriggersDeEvento, StringComparer.Ordinal))
        {
            yield return $"gatilhos de evento: lidos [{Lista(eventos)}], esperados [{Lista(esperado.TriggersDeEvento)}]";
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

        if (tabela.Publicada != esperada.Publicada)
        {
            return $"publicação lida {Texto(tabela.Publicada)}, esperada {Texto(esperada.Publicada)}";
        }

        if (tabela.Assinada != esperada.Assinada)
        {
            return $"assinatura lida {Texto(tabela.Assinada)}, esperada {Texto(esperada.Assinada)}";
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

        var objetos = Objetos(new("índice", "índices", Feminino: false), [.. tabela.IndicesUnicos.Select(IndiceInventariado.De)], esperada.IndicesUnicos, indice => indice.Nome)
            ?? Objetos(new("CHECK", "CHECKs", Feminino: false), [.. tabela.RestricoesCheck.Select(CheckInventariado.De)], esperada.Checks, check => check.Nome)
            ?? Objetos(new("exclusão", "exclusões", Feminino: true), [.. tabela.RestricoesExclusao.Select(ExclusaoInventariada.De)], esperada.Exclusoes, exclusao => exclusao.Nome)
            ?? Objetos(new("trigger", "triggers", Feminino: true), [.. tabela.Triggers.Select(TriggerInventariada.De)], esperada.Triggers, trigger => trigger.Nome);
        if (objetos is not null)
        {
            return objetos;
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
        : chave.Imposta != esperada.Imposta ? $": imposta lida {Texto(chave.Imposta)}, esperada {Texto(esperada.Imposta)}"
        : chave.Validada != esperada.Validada ? $": validada lida {Texto(chave.Validada)}, esperada {Texto(esperada.Validada)}"
        : chave.DesligadaSemRestauracao != esperada.DesligadaSemRestauracao
            ? $": desligada sem restauração lida {Texto(chave.DesligadaSemRestauracao)}, esperada {Texto(esperada.DesligadaSemRestauracao)}"
        : null;

    // A ordem das dependências é do contrato: comparada também.
    private static string? Diferenca(ObjetoResidual residual, ResidualInventariado esperado) =>
        residual.Tipo != esperado.Tipo ? $"residual lido como {residual.Tipo}, esperado {esperado.Tipo}"
        : !residual.DependeDe.SequenceEqual(esperado.DependeDe) ? $"depende de [{Lista(residual.DependeDe)}], esperado de [{Lista(esperado.DependeDe)}]"
        : residual.DependenciasIncompletas != esperado.DependenciasIncompletas
            ? $"dependências incompletas lidas {Texto(residual.DependenciasIncompletas)}, esperadas {Texto(esperado.DependenciasIncompletas)}"
        : residual.Populada != esperado.Populada ? $"populada lida {Texto(residual.Populada)}, esperada {Texto(esperado.Populada)}"
        : null;

    // Os objetos de uma lista da tabela, por nome e inteiros (pela projeção, comparada pela estrutura: o texto só serve à
    // mensagem, e dois valores diferentes podem ter o mesmo), e depois a ordem ordinal de nome, que é do
    // contrato e a comparação por nome não vê.
    private static string? Objetos<T>(Rotulo rotulo, IReadOnlyList<T> lidos, IReadOnlyList<T> esperados, Func<T, string> nome)
        where T : notnull
    {
        var porNome = lidos.ToLookup(nome, StringComparer.Ordinal);
        var esperadosPorNome = esperados.ToLookup(nome, StringComparer.Ordinal);
        var (lido, esperado) = rotulo.Feminino ? ("lida", "esperada") : ("lido", "esperado");
        foreach (var nomeDoObjeto in porNome.Select(grupo => grupo.Key).Union(esperadosPorNome.Select(grupo => grupo.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var diferenca = (porNome[nomeDoObjeto].ToArray(), esperadosPorNome[nomeDoObjeto].ToArray()) switch
            {
                ([], _) => "a menos",
                (_, []) => "a mais",
                (_, [_, _, ..] declarados) => $"{(rotulo.Feminino ? "declarada" : "declarado")} {declarados.Length} vezes",
                ([_, _, ..] repetidos, _) => $"{lido} {repetidos.Length} vezes",
                ([var unico], [var declarado]) => JsonSerializer.Serialize(unico) == JsonSerializer.Serialize(declarado) ? null : $"{lido} {unico}, {esperado} {declarado}",
            };
            if (diferenca is not null)
            {
                return $"{rotulo.Singular} {nomeDoObjeto} {diferenca}";
            }
        }

        var nomes = lidos.Select(nome).ToArray();
        return nomes.SequenceEqual(nomes.Order(StringComparer.Ordinal)) ? null : $"{rotulo.Plural} fora da ordem ordinal de nome: [{Lista(nomes)}]";
    }

    // A semântica na ordem das propriedades novas da coluna; cada objeto comparado inteiro, pela projeção no registro
    // esperado.
    private static string? Diferenca(ColunaEsquema coluna, ColunaInventariada esperada) =>
        coluna.TipoNativo != esperada.TipoNativo ? $"tipo nativo lido {coluna.TipoNativo}, esperado {esperada.TipoNativo}"
        : coluna.Anulavel != esperada.Anulavel ? $"anulável lida {coluna.Anulavel}, esperada {esperada.Anulavel}"
        : coluna.Identidade != esperada.Identidade ? $"identity lida {coluna.Identidade}, esperada {esperada.Identidade}"
        : coluna.Geracao != esperada.Geracao ? $"geração lida {coluna.Geracao}, esperada {esperada.Geracao}"
        : coluna.Mascarada != esperada.Mascarada ? $"mascarada lida {coluna.Mascarada}, esperada {esperada.Mascarada}"
        : coluna.TipoBase != esperada.TipoBase ? $"tipo base lido {Texto(coluna.TipoBase)}, esperado {Texto(esperada.TipoBase)}"
        : coluna.Situacao != esperada.Situacao ? $"situação lida {coluna.Situacao}, esperada {esperada.Situacao}"
        : coluna.Tipo != esperada.Tipo ? $"tipo canônico lido {Texto(coluna.Tipo)}, esperado {Texto(esperada.Tipo)}"
        : ColacaoEsperada.De(coluna.Colacao) != esperada.Colacao ? $"colação lida {Texto(ColacaoEsperada.De(coluna.Colacao))}, esperada {Texto(esperada.Colacao)}"
        : coluna.IgnoraCaixaPeloTipo != esperada.IgnoraCaixaPeloTipo
            ? $"caixa ignorada pelo tipo lida {Texto(coluna.IgnoraCaixaPeloTipo)}, esperada {Texto(esperada.IgnoraCaixaPeloTipo)}"
        : coluna.IgnoraEspacoFinalPeloTipo != esperada.IgnoraEspacoFinalPeloTipo
            ? $"espaço final ignorado pelo tipo lido {Texto(coluna.IgnoraEspacoFinalPeloTipo)}, esperado {Texto(esperada.IgnoraEspacoFinalPeloTipo)}"
        : LimiteEsperado.De(coluna.Comprimento) != esperada.Comprimento ? $"comprimento lido {Texto(LimiteEsperado.De(coluna.Comprimento))}, esperado {Texto(esperada.Comprimento)}"
        : coluna.Precisao != esperada.Precisao ? $"precisão lida {Texto(coluna.Precisao)}, esperada {Texto(esperada.Precisao)}"
        : coluna.Escala != esperada.Escala ? $"escala lida {Texto(coluna.Escala)}, esperada {Texto(esperada.Escala)}"
        : FaixaEsperada.De(coluna.Faixa) != esperada.Faixa ? $"faixa lida {Texto(FaixaEsperada.De(coluna.Faixa))}, esperada {Texto(esperada.Faixa)}"
        : !MesmasReferencias(coluna.ReferenciasDaGeracao, esperada.ReferenciasDaGeracao)
            ? $"referências da geração lidas {Texto(ReferenciasEsperadas.De(coluna.ReferenciasDaGeracao))}, esperadas {Texto(esperada.ReferenciasDaGeracao)}"
        : coluna.RestritaPeloTipo != esperada.RestritaPeloTipo ? $"restrita pelo tipo lida {Texto(coluna.RestritaPeloTipo)}, esperada {Texto(esperada.RestritaPeloTipo)}"
        : null;

    // A ordem das colunas é do contrato (a da tabela): comparada também.
    private static bool MesmasReferencias(ReferenciasDaExpressao? lidas, ReferenciasEsperadas? esperadas) =>
        lidas is null || esperadas is null
            ? lidas is null && esperadas is null
            : lidas.Incompletas == esperadas.Incompletas && lidas.Colunas.SequenceEqual(esperadas.Colunas);

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

    // Como a mensagem nomeia o objeto e concorda com ele.
    private sealed record Rotulo(string Singular, string Plural, bool Feminino);
}
