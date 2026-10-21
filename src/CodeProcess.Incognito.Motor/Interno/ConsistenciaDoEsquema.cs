// O motor confere o modelo do contrato experimental de dialeto que ele mesmo declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// As regras de coerência do esquema lido (decisão 78c): o que o modelo diz e que se contradiz, como uma partição sem
/// pai, uma chave estrangeira para tabela que não foi lida ou uma colação determinística que ignora a caixa. Uma
/// contradição é defeito do dialeto, e o plano para (<c>INC3002</c>, saída 1).
/// </summary>
/// <remarks>
/// Só as regras de coerência: as de presença (texto sem colação, inteiro sem faixa, gerada sem as referências, chave
/// primária sem índice primário) a suíte de conformidade cobra dos dialetos do repositório, e o motor trata o campo nulo
/// como o pior caso no ponto de uso. A mesma coerência é conferida pela suíte; um teste diferencial mantém as duas iguais.
/// Nulo nunca recusa aqui, salvo as marcas de uma colação determinística, que precisam ser falsas.
/// </remarks>
internal static class ConsistenciaDoEsquema
{
    /// <summary>As contradições do esquema, na ordem em que aparecem; vazio quando é coerente.</summary>
    /// <param name="esquema">O esquema lido.</param>
    /// <returns>Cada contradição, com os nomes escapados.</returns>
    internal static IReadOnlyList<string> Problemas(EsquemaBanco esquema)
    {
        ArgumentNullException.ThrowIfNull(esquema);
        return [.. Todos(esquema)];
    }

    private static IEnumerable<string> Todos(EsquemaBanco esquema)
    {
        var tabelas = esquema.Tabelas.DistinctBy(tabela => tabela.Nome).ToDictionary(tabela => tabela.Nome);
        foreach (var repetida in Repetidos(esquema.Tabelas.Select(tabela => tabela.Nome)))
        {
            yield return $"tabela {Nome(repetida)} repetida";
        }

        foreach (var repetido in Repetidos(esquema.Sequences.Select(sequence => sequence.Nome)).Concat(Repetidos(esquema.ObjetosResiduais.Select(objeto => objeto.Nome))))
        {
            yield return $"objeto {Nome(repetido)} repetido";
        }

        var paisDeParticao = esquema.Tabelas.Where(filha => filha.Tipo == TipoTabela.Particao && filha.Pai is not null).Select(filha => filha.Pai!).ToHashSet();
        var temporaisPorHistorico = esquema.Tabelas.Where(temporal => temporal.Historico is not null).GroupBy(temporal => temporal.Historico!).ToDictionary(grupo => grupo.Key, grupo => grupo.Count());
        foreach (var problema in esquema.Tabelas.SelectMany(tabela => DaTabela(tabela, esquema, tabelas, paisDeParticao, temporaisPorHistorico)))
        {
            yield return problema;
        }

        if (esquema.Propriedades?.Colacao is { } padrao && !Coerente(padrao))
        {
            yield return "colação padrão do banco determinística com marca que não é falsa";
        }

        foreach (var problema in DasSequences(esquema, tabelas).Concat(DosResiduais(esquema)).Concat(DoAcesso(esquema)))
        {
            yield return problema;
        }
    }

    private static IEnumerable<string> DaTabela(
        TabelaEsquema tabela, EsquemaBanco esquema, Dictionary<NomeObjeto, TabelaEsquema> tabelas, HashSet<NomeObjeto> paisDeParticao, Dictionary<NomeObjeto, int> temporaisPorHistorico)
    {
        var nome = Nome(tabela.Nome);
        foreach (var coluna in Repetidos(tabela.Colunas.Select(coluna => coluna.Nome)))
        {
            yield return $"coluna {nome}.{Nome(coluna)} repetida";
        }

        var colunas = tabela.Colunas.Select(coluna => coluna.Nome).ToHashSet(StringComparer.Ordinal);
        var precisaDePai = tabela.Tipo is TipoTabela.Particao or TipoTabela.Herdeira;
        if (precisaDePai != (tabela.Pai is not null) || (tabela.Pai is not null && !tabelas.ContainsKey(tabela.Pai)))
        {
            yield return $"pai de {nome} ({tabela.Tipo}) ausente, sobrando ou fora do esquema";
        }

        // A partição é de uma particionada ou de uma intermediária; a herdeira, de tabelas comuns ou herdeiras.
        TipoTabela[] tiposDasMaes = tabela.Tipo == TipoTabela.Particao ? [TipoTabela.Particionada, TipoTabela.Particao] : [TipoTabela.Comum, TipoTabela.Herdeira];
        if (precisaDePai
            && new[] { tabela.Pai }.Concat(tabela.OutrosPais).Any(mae => mae is not null && tabelas.TryGetValue(mae, out var lida) && !tiposDasMaes.Contains(lida.Tipo)))
        {
            yield return $"pai de {nome} ({tabela.Tipo}) de tipo que não pode ter filha desse tipo";
        }

        if ((tabela.OutrosPais.Count > 0 && tabela.Tipo != TipoTabela.Herdeira)
            || tabela.OutrosPais.Any(pai => pai.Equals(tabela.Pai) || !tabelas.ContainsKey(pai))
            || Repetidos(tabela.OutrosPais).Any())
        {
            yield return $"outros pais de {nome} ({tabela.Tipo}) fora de herdeira, repetidos, com o pai ou fora do esquema";
        }

        var particionada = tabela.Tipo == TipoTabela.Particionada || paisDeParticao.Contains(tabela.Nome);
        if (particionada != (tabela.ChaveParticao is not null))
        {
            yield return $"chave de partição de {nome} ({tabela.Tipo}) ausente ou sobrando";
        }

        if (tabela.ChaveParticao is { } chave && (chave.Colunas.Any(coluna => !colunas.Contains(coluna)) || Repetidos(chave.Colunas).Any()))
        {
            yield return $"chave de partição de {nome} com coluna que não existe ou repetida";
        }

        if (tabela.ChaveParticao is { Colunas.Count: 0, ColunasIncompletas: false })
        {
            yield return $"chave de partição de {nome} sem coluna e marcada completa";
        }

        var precisaDeHistorico = tabela.Tipo == TipoTabela.Temporal;
        if (precisaDeHistorico != (tabela.Historico is not null)
            || (tabela.Historico is not null && (!tabelas.TryGetValue(tabela.Historico, out var historico) || historico.Tipo != TipoTabela.HistoricoDeTemporal)))
        {
            yield return $"histórico de {nome} ({tabela.Tipo}) ausente, sobrando ou não marcado como histórico";
        }

        if (tabela.Tipo == TipoTabela.HistoricoDeTemporal && temporaisPorHistorico.GetValueOrDefault(tabela.Nome) != 1)
        {
            yield return $"histórico {nome} sem uma temporal só que o aponte";
        }

        foreach (var coluna in tabela.ChavePrimaria.Where(coluna => !colunas.Contains(coluna)))
        {
            yield return $"chave primária de {nome} com a coluna {Nome(coluna)}, que não existe";
        }

        foreach (var coluna in tabela.Colunas.Where(coluna => coluna.Tipo is not null && coluna.Situacao != SituacaoTipo.Suportado))
        {
            yield return $"coluna {nome}.{Nome(coluna.Nome)} com tipo canônico fora de 'suportado'";
        }

        foreach (var problema in tabela.Colunas.SelectMany(coluna => DaColuna(tabela, coluna, esquema.Propriedades)).Concat(DosObjetos(tabela)).Concat(DasChaves(tabela, tabelas)))
        {
            yield return problema;
        }
    }

    private static IEnumerable<string> DasChaves(TabelaEsquema tabela, Dictionary<NomeObjeto, TabelaEsquema> tabelas)
    {
        var nome = Nome(tabela.Nome);
        var colunas = tabela.Colunas.Select(coluna => coluna.Nome).ToHashSet(StringComparer.Ordinal);
        foreach (var repetida in Repetidos(tabela.ChavesEstrangeiras.Select(chave => chave.Nome)))
        {
            yield return $"chave {Nome(repetida)} de {nome} repetida";
        }

        foreach (var chave in tabela.ChavesEstrangeiras)
        {
            var chaveNome = Nome(chave.Nome);
            if (chave.Colunas.Count == 0 || chave.Colunas.Count != chave.ColunasReferenciadas.Count)
            {
                yield return $"chave {chaveNome} de {nome} com {chave.Colunas.Count} colunas e {chave.ColunasReferenciadas.Count} referenciadas";
            }

            if (chave.Colunas.Any(coluna => !colunas.Contains(coluna)))
            {
                yield return $"chave {chaveNome} de {nome} com coluna que não existe";
            }

            if (!tabelas.TryGetValue(chave.Referenciada, out var referenciada)
                || chave.ColunasReferenciadas.Any(coluna => !referenciada.Colunas.Any(lida => lida.Nome == coluna)))
            {
                yield return $"chave {chaveNome} de {nome} referencia tabela ou coluna fora do esquema";
            }
        }
    }

    // A semântica da coluna: os campos que só valem para um tipo canônico ficam fora dos outros (um tipo nulo pode ter
    // qualquer um). Sem as regras de presença.
    private static IEnumerable<string> DaColuna(TabelaEsquema tabela, ColunaEsquema coluna, PropriedadesBanco? propriedades)
    {
        var nome = $"coluna {Nome(tabela.Nome)}.{Nome(coluna.Nome)}";
        var texto = coluna.Tipo is null or TipoColuna.Texto;
        if (coluna.Colacao is not null && !texto)
        {
            yield return $"{nome} com colação fora de texto";
        }

        if (coluna.Colacao is { } colacao && !Coerente(colacao))
        {
            yield return $"{nome} com colação determinística e marca que não é falsa";
        }

        if ((coluna.IgnoraCaixaPeloTipo is not null || coluna.IgnoraEspacoFinalPeloTipo is not null) && !texto)
        {
            yield return $"{nome} com marca de texto fora de texto";
        }

        if (coluna.Comprimento is not null && coluna.Tipo is not (null or TipoColuna.Texto or TipoColuna.Binario))
        {
            yield return $"{nome} com comprimento fora de texto e binário";
        }

        if (coluna.Comprimento is { Unidade: UnidadeComprimento.PontosDeCodigo } && propriedades?.Codificacao.Definida == false)
        {
            yield return $"{nome} com comprimento em caracteres num banco sem codificação declarada";
        }

        if ((coluna.Precisao is not null || coluna.Escala is not null) && coluna.Tipo is not (null or TipoColuna.Decimal))
        {
            yield return $"{nome} com precisão ou escala fora de decimal";
        }

        if (coluna.Escala is not null && coluna.Precisao is null)
        {
            yield return $"{nome} com escala sem precisão";
        }

        if (coluna.Faixa is not null && coluna.Tipo is not (null or TipoColuna.Inteiro))
        {
            yield return $"{nome} com faixa fora de inteiro";
        }

        if (coluna.Faixa is { Minimo: var minimo, Maximo: var maximo } && minimo >= maximo)
        {
            yield return $"{nome} com faixa vazia";
        }

        if (coluna.Faixa is { Maximo: <= 0 })
        {
            yield return $"{nome} com faixa sem positivos";
        }

        // As referências sobrando numa coluna que não é gerada se contradizem; as que faltam numa gerada são o pior caso.
        if (coluna.ReferenciasDaGeracao is not null && coluna.Geracao == GeracaoColuna.Nenhuma)
        {
            yield return $"{nome} com referências da geração sem ser gerada";
        }

        if (coluna.ReferenciasDaGeracao is { } referencias)
        {
            if (referencias.Colunas.Contains(coluna.Nome))
            {
                yield return $"{nome} com referência da geração a ela mesma";
            }
            else if (referencias.Colunas.Any(citada => !tabela.Colunas.Any(lida => lida.Nome == citada)))
            {
                yield return $"{nome} com referência da geração a coluna que não existe";
            }
        }
    }

    // Os índices, CHECKs, exclusões e triggers da tabela, sem as regras de presença.
    private static IEnumerable<string> DosObjetos(TabelaEsquema tabela)
    {
        var nome = Nome(tabela.Nome);
        var colunas = tabela.Colunas.DistinctBy(coluna => coluna.Nome).ToDictionary(coluna => coluna.Nome, StringComparer.Ordinal);
        var repetidos = Repetidos(tabela.IndicesUnicos.Select(indice => $"índice {Nome(indice.Nome)} de {nome} repetido"))
            .Concat(Repetidos(tabela.RestricoesCheck.Select(check => $"CHECK {Nome(check.Nome)} de {nome} repetido")))
            .Concat(Repetidos(tabela.RestricoesExclusao.Select(exclusao => $"exclusão {Nome(exclusao.Nome)} de {nome} repetida")))
            .Concat(Repetidos(tabela.Triggers.Select(trigger => $"trigger {Nome(trigger.Nome)} de {nome} repetida")));
        foreach (var repetido in repetidos)
        {
            yield return repetido;
        }

        foreach (var indice in tabela.IndicesUnicos)
        {
            var objeto = $"índice {Nome(indice.Nome)} de {nome}";
            foreach (var inexistente in indice.Elementos.Select(elemento => elemento.Coluna).OfType<string>().Where(coluna => !colunas.ContainsKey(coluna)))
            {
                yield return $"{objeto} com o elemento {Nome(inexistente)}, coluna que não existe";
            }

            if (indice.ReferenciasDasExpressoes is not null && indice.Elementos.All(elemento => elemento.Coluna is not null))
            {
                yield return $"{objeto} com referências das expressões sem expressão";
            }

            foreach (var elemento in indice.Elementos.Where(elemento => elemento.Coluna is { } coluna && colunas.ContainsKey(coluna) && elemento.Colacao is not null))
            {
                if (colunas[elemento.Coluna!].Tipo is not (null or TipoColuna.Texto))
                {
                    yield return $"{objeto} com colação no elemento {Nome(elemento.Coluna!)}, que não é texto";
                }
            }

            foreach (var (elemento, posicao) in indice.Elementos.Select((elemento, posicao) => (elemento, posicao + 1)).Where(par => par.elemento.Colacao is { } colacao && !Coerente(colacao)))
            {
                yield return $"{objeto} com colação determinística e marca que não é falsa no elemento {posicao}";
            }

            foreach (var (parte, referencias) in new[] { ("expressões", indice.ReferenciasDasExpressoes), ("predicado", indice.Predicado) })
            {
                if (referencias is not null && Inexistente(referencias, tabela) is { } inexistente)
                {
                    yield return $"{objeto} com {parte} citando {Nome(inexistente)}, coluna que não existe";
                }
            }
        }

        var primarios = tabela.IndicesUnicos.Where(indice => indice.Primario).ToArray();
        if (primarios.Length > 1)
        {
            yield return $"tabela {nome} com {primarios.Length} índices primários";
        }
        else if (primarios is [var primario] && !primario.Elementos.Select(elemento => elemento.Coluna).SequenceEqual(tabela.ChavePrimaria))
        {
            yield return $"índice primário {Nome(primario.Nome)} de {nome} diferente da chave primária";
        }

        foreach (var (objeto, referencias) in tabela.RestricoesCheck.Select(check => ($"CHECK {Nome(check.Nome)}", check.Referencias))
            .Concat(tabela.RestricoesExclusao.Select(exclusao => ($"exclusão {Nome(exclusao.Nome)}", exclusao.Referencias))))
        {
            if (Inexistente(referencias, tabela) is { } inexistente)
            {
                yield return $"{objeto} de {nome} citando {Nome(inexistente)}, coluna que não existe";
            }
        }

        foreach (var trigger in tabela.Triggers.Where(trigger => trigger.Herdada && tabela.Tipo != TipoTabela.Particao))
        {
            yield return $"trigger {Nome(trigger.Nome)} de {nome} herdada fora de partição";
        }
    }

    private static IEnumerable<string> DasSequences(EsquemaBanco esquema, Dictionary<NomeObjeto, TabelaEsquema> tabelas)
    {
        foreach (var sequence in esquema.Sequences)
        {
            foreach (var repetida in Repetidos(sequence.Colunas))
            {
                yield return $"coluna {Nome(repetida)} repetida na sequence {Nome(sequence.Nome)}";
            }

            foreach (var coluna in sequence.Colunas.Where(coluna => !tabelas.TryGetValue(coluna.Tabela, out var tabela) || !tabela.Colunas.Any(lida => lida.Nome == coluna.Coluna)))
            {
                yield return $"sequence {Nome(sequence.Nome)} usada pela coluna {Nome(coluna)}, que não existe";
            }
        }
    }

    // As dependências entre materialized views: só das que existem, sem a própria e sem ciclo (num ciclo real, o leitor
    // tira a dependência que o fecha e marca as incompletas, decisão 84a).
    private static IEnumerable<string> DosResiduais(EsquemaBanco esquema)
    {
        var visoes = esquema.ObjetosResiduais.Where(objeto => objeto.Tipo == TipoObjetoResidual.VisaoMaterializada).DistinctBy(objeto => objeto.Nome).ToDictionary(objeto => objeto.Nome);
        foreach (var residual in esquema.ObjetosResiduais)
        {
            var nome = Nome(residual.Nome);
            if (residual.Tipo != TipoObjetoResidual.VisaoMaterializada && (residual.DependeDe.Count > 0 || residual.DependenciasIncompletas is not null || residual.Populada is not null))
            {
                yield return $"residual {nome} ({residual.Tipo}) com dependências ou marcas de materialized view";
            }
            else if (residual.DependeDe.Contains(residual.Nome))
            {
                yield return $"residual {nome} depende dele mesmo";
            }
            else if (residual.DependeDe.FirstOrDefault(dependencia => !visoes.ContainsKey(dependencia)) is { } fora)
            {
                yield return $"residual {nome} depende de {Nome(fora)}, que não é materialized view do esquema";
            }
            else if (EmCiclo(residual, visoes))
            {
                yield return $"residual {nome} numa dependência circular";
            }
        }
    }

    // As permissões casadas pelo objeto (o nome, e o tipo nos residuais) com o que foi lido: uma entrada por tabela,
    // objeto residual e sequence, nem a mais nem a menos. Nunca pelo texto do nome: "a.b"."c" e "a"."b.c" são outros.
    private static IEnumerable<string> DoAcesso(EsquemaBanco esquema)
    {
        if (esquema.Acesso is not { } acesso)
        {
            yield break;
        }

        foreach (var problema in Casar("da tabela", "de tabela", "lida", esquema.Tabelas.Select(tabela => tabela.Nome), acesso.Tabelas.Select(permissao => permissao.Tabela), Nome)
            .Concat(Casar(
                "do objeto residual", "de objeto residual", "lido",
                esquema.ObjetosResiduais.Select(objeto => (objeto.Nome, objeto.Tipo)), acesso.Residuais.Select(permissao => (permissao.Nome, permissao.Tipo)),
                par => $"{Nome(par.Nome)} ({par.Tipo})"))
            .Concat(Casar("da sequence", "de sequence", "lida", esquema.Sequences.Select(sequence => sequence.Nome), acesso.Sequences.Select(permissao => permissao.Nome), Nome)))
        {
            yield return problema;
        }
    }

    private static IEnumerable<string> Casar<T>(string daParte, string deParte, string lida, IEnumerable<T> lidos, IEnumerable<T> permissoes, Func<T, string> texto)
        where T : notnull
    {
        var contagem = permissoes.GroupBy(chave => chave).ToDictionary(grupo => grupo.Key, grupo => grupo.Count());
        var conhecidos = lidos.ToHashSet();
        foreach (var lido in conhecidos.Where(lido => contagem.GetValueOrDefault(lido) != 1))
        {
            yield return $"permissões {daParte} {texto(lido)} lidas {contagem.GetValueOrDefault(lido)} vezes";
        }

        foreach (var sobrando in contagem.Keys.Where(permissao => !conhecidos.Contains(permissao)))
        {
            yield return $"permissões {deParte} {texto(sobrando)}, que não foi {lida}";
        }
    }

    private static string? Inexistente(ReferenciasDaExpressao referencias, TabelaEsquema tabela) =>
        referencias.Colunas.FirstOrDefault(citada => !tabela.Colunas.Any(lida => lida.Nome == citada));

    private static bool EmCiclo(ObjetoResidual inicio, Dictionary<NomeObjeto, ObjetoResidual> visoes)
    {
        var vistas = new HashSet<NomeObjeto>();
        var pendentes = new Stack<NomeObjeto>(inicio.DependeDe);
        while (pendentes.TryPop(out var atual))
        {
            if (atual.Equals(inicio.Nome))
            {
                return true;
            }

            if (vistas.Add(atual) && visoes.TryGetValue(atual, out var visao))
            {
                foreach (var proxima in visao.DependeDe)
                {
                    pendentes.Push(proxima);
                }
            }
        }

        return false;
    }

    // Uma colação determinística não ignora nada, e mediu isso: as três marcas falsas, nunca nulas.
    private static bool Coerente(ColacaoEsquema colacao) =>
        !colacao.Deterministica || colacao is { IgnoraCaixa: false, IgnoraAcento: false, IgnoraPontuacao: false };

    private static string Nome(object nome) => TextoSeguro.Nome(nome.ToString() ?? "");

    private static IEnumerable<T> Repetidos<T>(IEnumerable<T> itens)
        where T : notnull => itens.GroupBy(item => item).Where(grupo => grupo.Count() > 1).Select(grupo => grupo.Key);
}
