// A unicidade lê os índices, as exclusões e as colações do destino, tipos do contrato experimental de dialeto que o motor
// declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// A injetividade contra a igualdade do destino (P8, com as restrições): em todo índice único, na chave primária, nas exclusões e nas chaves
/// estrangeiras, os pseudônimos de valores distintos precisam continuar distintos pela igualdade do banco (a colação do
/// elemento e as marcas do tipo). Igualdade que o planejar não sabe decidir recusa; a que só a tabela de formato fixo
/// prova relaxa a injetividade efetiva.
/// </summary>
internal sealed partial class MontagemDoPlano
{
    private readonly HashSet<No> _naChave = [];

    // P8: cada unidade copiada com coluna tratada, os índices e as exclusões do destino e as chaves estrangeiras. O
    // componente recusado na propagação fica de fora: os erros dele já dizem o que corrigir.
    private void ConferirUnicidade()
    {
        foreach (var unidade in _unidades.Where(_pares.ContainsKey))
        {
            var porDestino = _pares[unidade]
                .Where(par => _efetivas.ContainsKey(new No(unidade, par.Origem.Nome)) && !_recusadas.Contains(new No(unidade, par.Origem.Nome)))
                .ToDictionary(par => par.Destino.Nome, StringComparer.Ordinal);
            if (porDestino.Count == 0)
            {
                continue;
            }

            var colunas = unidade.Destino!.Colunas.ToDictionary(coluna => coluna.Nome, StringComparer.Ordinal);
            var semInjetividade = new HashSet<string>(StringComparer.Ordinal);
            foreach (var indice in Indices(unidade))
            {
                ConferirIndice(unidade, indice, porDestino, colunas, semInjetividade);
            }

            // A chave estrangeira, quando nenhum índice já recusou a coluna pela mesma causa.
            foreach (var par in porDestino.Values.OrderBy(par => par.Indice))
            {
                var no = new No(unidade, par.Origem.Nome);
                var fabrica = _efetivas[no].Resolvida.Fabrica;
                if (_naChave.Contains(no) && fabrica.Injetividade == Injetividade.Nenhuma && !semInjetividade.Contains(par.Destino.Nome))
                {
                    ErroNaUnicidade(
                        CodigosPlanejar.SemInjetividadeNaUnicidade,
                        $"O transformador {fabrica.Nome}@{fabrica.Versao} não é injetivo, e a coluna está numa chave estrangeira: valores diferentes viram o mesmo, e as linhas se ligariam erradas. Use um transformador injetivo nas colunas ligadas, ou esvazie ou ignore as tabelas.",
                        unidade,
                        par,
                        null);
                }

                if (_naChave.Contains(no) && !fabrica.Embutida)
                {
                    AvisarTerceiroLigado(unidade, par, fabrica);
                }
            }
        }
    }

    // Os índices conferidos: os únicos da tabela e das partições, as exclusões, e a chave primária lida direto quando o
    // dialeto não a entrega como índice nem como exclusão (a chave com sobreposição proibida, WITHOUT OVERLAPS).
    private IEnumerable<IndiceConferido> Indices(Unidade unidade)
    {
        var tabelas = TabelasNoDestino(unidade).ToList();
        foreach (var indice in tabelas.SelectMany(tabela => tabela.IndicesUnicos))
        {
            yield return new IndiceConferido(indice.Nome, indice.Elementos, indice.ReferenciasDasExpressoes, indice.Predicado, indice.NulosDistintos, Exclusao: false);
        }

        foreach (var exclusao in tabelas.SelectMany(tabela => tabela.RestricoesExclusao))
        {
            yield return new IndiceConferido(exclusao.Nome, [], exclusao.Referencias, null, true, Exclusao: true);
        }

        var destino = unidade.Destino!;
        var chave = destino.ChavePrimaria.ToHashSet(StringComparer.Ordinal);
        if (chave.Count > 0
            && !destino.IndicesUnicos.Any(indice => indice.Primario)
            && !destino.RestricoesExclusao.Any(exclusao => !exclusao.Referencias.Incompletas && chave.SetEquals(exclusao.Referencias.Colunas)))
        {
            var elementos = destino.ChavePrimaria
                .Select(coluna => new ElementoIndice { Coluna = coluna, Colacao = destino.Colunas.FirstOrDefault(lida => lida.Nome == coluna)?.Colacao })
                .ToList();
            yield return new IndiceConferido(null, elementos, null, null, true, Exclusao: false);
        }
    }

    private void ConferirIndice(Unidade unidade, IndiceConferido indice, Dictionary<string, Par> porDestino, Dictionary<string, ColunaEsquema> colunas, HashSet<string> semInjetividade)
    {
        var tratadas = porDestino.Keys.ToHashSet(StringComparer.Ordinal);
        var deExpressao = indice.Elementos.Where(elemento => elemento.Coluna is null).ToList();

        // As tratadas que as expressões (ou a exclusão) citam, também por uma gerada calculada delas. Um elemento de
        // expressão sem as referências cita todas: o dialeto não disse quais.
        var citadasPorExpressao = new HashSet<string>(StringComparer.Ordinal);
        if (indice.Exclusao || deExpressao.Count > 0)
        {
            if (indice.Referencias is not { Incompletas: false } referencias)
            {
                citadasPorExpressao.UnionWith(tratadas);
            }
            else
            {
                foreach (var citada in referencias.Colunas)
                {
                    citadasPorExpressao.UnionWith(tratadas.Where(tratada => tratada == citada || CalculadaDe(citada, tratada, colunas)));
                }
            }
        }

        if (indice.Predicado is { } predicado && Cita(predicado, TratadasNoDestino(unidade)))
        {
            _coletor.Aviso(new Aviso(
                CodigosPlanejar.PredicadoComTratada,
                $"O predicado {Do(indice)} cita coluna tratada: a linha pode entrar ou sair do índice com o pseudônimo.",
                unidade.NomeTabela,
                objeto: indice.Nome));
        }

        foreach (var (nome, par) in porDestino.OrderBy(entrada => entrada.Value.Indice))
        {
            var diretos = indice.Elementos.Where(elemento => elemento.Coluna == nome).ToList();
            var geradas = indice.Elementos.Where(elemento => elemento.Coluna is { } gerada && gerada != nome && CalculadaDe(gerada, nome, colunas)).ToList();
            var porExpressao = citadasPorExpressao.Contains(nome);
            if (diretos.Count == 0 && geradas.Count == 0 && !porExpressao)
            {
                continue;
            }

            var no = new No(unidade, par.Origem.Nome);
            var resolvida = _efetivas[no].Resolvida;
            var fabrica = resolvida.Fabrica;
            if (indice.Exclusao)
            {
                if (fabrica.Injetividade == Injetividade.Nenhuma)
                {
                    semInjetividade.Add(nome);
                    ErroNaUnicidade(
                        CodigosPlanejar.SemInjetividadeNaExclusao,
                        $"O transformador {fabrica.Nome}@{fabrica.Versao} não é injetivo, e a coluna está na exclusão {NomeTabela.Parte(indice.Nome!)}: valores diferentes viram o mesmo, e a gravação falharia. Use um transformador injetivo, ou esvazie ou ignore a tabela.",
                        unidade,
                        par,
                        indice.Nome);
                }
                else
                {
                    AvisarNaUnicidade(
                        CodigosPlanejar.ExclusaoComTratada,
                        $"A exclusão {NomeTabela.Parte(indice.Nome!)} cita coluna tratada: o operador dela não está no modelo, e o pseudônimo pode violá-la.",
                        unidade,
                        par,
                        indice.Nome);
                }

                continue;
            }

            if (fabrica.Injetividade == Injetividade.Nenhuma)
            {
                semInjetividade.Add(nome);
                ErroNaUnicidade(
                    CodigosPlanejar.SemInjetividadeNaUnicidade,
                    $"O transformador {fabrica.Nome}@{fabrica.Versao} não é injetivo, e a coluna está {NoIndice(indice)}: valores diferentes viram o mesmo, e a gravação falharia. Use um transformador injetivo, ou esvazie ou ignore a tabela.",
                    unidade,
                    par,
                    indice.Nome);
                continue;
            }

            // A pior classe entre os elementos que alcançam a coluna. A expressão e a gerada (no SQL Server, a expressão
            // mora numa coluna computada) só passam com igualdade exata: a prova do formato fixo cobre a saída do
            // transformador, não uma função dela, e o elemento de expressão sem colação é indeterminado.
            var classe = diretos.Select(elemento => ClasseDoElemento(elemento.Colacao, colunas.GetValueOrDefault(nome))).DefaultIfEmpty(Classe.Exata).Max();
            var dasFuncoes = geradas.Select(elemento => ClasseDoElemento(elemento.Colacao, colunas.GetValueOrDefault(elemento.Coluna!)))
                .Concat(porExpressao ? deExpressao.Select(elemento => ClasseDaColacao(elemento.Colacao)) : [])
                .ToList();
            if (dasFuncoes.Any(daFuncao => daFuncao != Classe.Exata))
            {
                classe = Classe.Indeterminada;
            }

            if (classe == Classe.Indeterminada || (classe == Classe.NaoDeterministicaMedida && !FormatoFixo.Contem(fabrica)))
            {
                ErroNaUnicidade(
                    CodigosPlanejar.IgualdadeIndeterminada,
                    $"{Maiuscula(Do(indice))} compara a coluna tratada por uma igualdade que pode juntar pseudônimos distintos (colação não determinística, com regras ou não informada, também num elemento de expressão ou numa coluna gerada): os pseudônimos podem colidir. Use uma colação determinística no índice do destino, ou esvazie ou ignore a tabela.",
                    unidade,
                    par,
                    indice.Nome);
                continue;
            }

            if (classe == Classe.NaoDeterministicaMedida && fabrica.Injetividade == Injetividade.Garantida)
            {
                _injetividades[no] = Injetividade.Probabilistica;
            }

            if (classe == Classe.Normalizada && !fabrica.Embutida)
            {
                AvisarNaUnicidade(
                    CodigosPlanejar.TerceiroEmNormalizada,
                    $"{Maiuscula(Do(indice))} compara a coluna tratada sem a caixa ou sem os espaços finais, e o transformador {fabrica.Nome}@{fabrica.Versao} é de fora: os pseudônimos podem colidir.",
                    unidade,
                    par,
                    indice.Nome);
            }

            if (dasFuncoes.Count > 0)
            {
                AvisarNaUnicidade(
                    CodigosPlanejar.TratadaEmExpressao,
                    $"{Maiuscula(Do(indice))} usa a coluna tratada numa expressão ou numa coluna gerada: o Incognito não sabe se a função preserva a distinção, e os pseudônimos podem colidir.",
                    unidade,
                    par,
                    indice.Nome);
            }

            if (!indice.NulosDistintos && PoliticasQueAnulam.Da(resolvida, _regras.Dominios).Count > 0)
            {
                ErroNaUnicidade(
                    CodigosPlanejar.NuloNoIndice,
                    $"A regra produz NULL, e {Do(indice)} trata NULLs como iguais: a segunda linha anulada colidiria. Use 'falhar' ou 'preservar' na política.",
                    unidade,
                    par,
                    indice.Nome);
            }

            if (diretos.Count > 0 && ColidePelaNormalizacao(fabrica, colunas.GetValueOrDefault(nome)) is { } motivo)
            {
                AvisarNaUnicidade(
                    CodigosPlanejar.ColisaoPelaNormalizacao,
                    $"{Maiuscula(Do(indice))} pode receber a mesma saída para entradas que o transformador normaliza ({motivo}).",
                    unidade,
                    par,
                    indice.Nome);
            }

            if (PoliticasQueAnulam.UsaEntradaMalformada(fabrica) && resolvida.Regra.EntradaMalformada == PoliticaEntradaMalformada.Preservar
                && (!fabrica.Embutida || classe == Classe.NaoDeterministicaMedida))
            {
                AvisarNaUnicidade(
                    CodigosPlanejar.PreservadaComPseudonimos,
                    $"A entrada fora do formato fica em claro {NoIndice(indice)}, ao lado dos pseudônimos: a injetividade vale entre pseudônimos, não entre um pseudônimo e uma entrada preservada.",
                    unidade,
                    par,
                    indice.Nome);
            }
        }
    }

    // O índice na frase: "o índice único x" ou "a chave primária" (a lida direto não tem nome).
    private static string Do(IndiceConferido indice) => indice.Nome is { } nome ? $"o índice único {NomeTabela.Parte(nome)}" : "a chave primária";

    private static string NoIndice(IndiceConferido indice) => indice.Nome is { } nome ? $"no índice único {NomeTabela.Parte(nome)}" : "na chave primária";

    private static string Maiuscula(string texto) => string.Concat(char.ToUpperInvariant(texto[0]).ToString(), texto[1..]);

    // O terceiro ligado a outras colunas por uma igualdade que não é exata: a junção pode casar pseudônimos distintos.
    private void AvisarTerceiroLigado(Unidade unidade, Par par, FabricaRegistrada fabrica)
    {
        var classe = ClasseDoElemento(par.Destino.Colacao, par.Destino);
        if (classe == Classe.Normalizada)
        {
            AvisarNaUnicidade(
                CodigosPlanejar.TerceiroEmNormalizada,
                $"A coluna ligada compara sem a caixa ou sem os espaços finais, e o transformador {fabrica.Nome}@{fabrica.Versao} é de fora: pseudônimos distintos podem se casar na chave.",
                unidade,
                par,
                null);
        }
        else if (classe != Classe.Exata)
        {
            AvisarNaUnicidade(
                CodigosPlanejar.TerceiroLigadoSemIgualdadeExata,
                $"A coluna ligada compara por uma igualdade que não é exata, e o transformador {fabrica.Nome}@{fabrica.Versao} é de fora: pseudônimos distintos podem se casar na chave.",
                unidade,
                par,
                null);
        }
    }

    // Se a coluna gerada é calculada a partir da outra (sem as referências, ou com elas incompletas, de todas), também por
    // outra gerada.
    private static bool CalculadaDe(string gerada, string coluna, Dictionary<string, ColunaEsquema> colunas)
    {
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var pendentes = new Stack<string>([gerada]);
        while (pendentes.TryPop(out var atual))
        {
            if (!vistas.Add(atual) || !colunas.TryGetValue(atual, out var lida) || lida.Geracao == GeracaoColuna.Nenhuma)
            {
                continue;
            }

            if (lida.ReferenciasDaGeracao is not { } referencias || referencias.Incompletas || referencias.Colunas.Contains(coluna, StringComparer.Ordinal))
            {
                return true;
            }

            foreach (var citada in referencias.Colunas)
            {
                pendentes.Push(citada);
            }
        }

        return false;
    }

    // A classe de igualdade de um elemento de coluna: fora de texto, exata; em texto, pela colação do elemento e pelas
    // marcas do tipo.
    private static Classe ClasseDoElemento(ColacaoEsquema? colacao, ColunaEsquema? coluna)
    {
        if (coluna?.Tipo is not (null or TipoColuna.Texto))
        {
            return Classe.Exata;
        }

        var daColacao = ClasseDaColacao(colacao);
        if (daColacao == Classe.Indeterminada || coluna?.IgnoraCaixaPeloTipo is not { } caixa || coluna.IgnoraEspacoFinalPeloTipo is not { } espaco)
        {
            return Classe.Indeterminada;
        }

        return daColacao == Classe.Exata && (caixa || espaco) ? Classe.Normalizada : daColacao;
    }

    // A classe pela colação só (num elemento de expressão, o tipo não é conhecido): nula, com regras ou não determinística
    // sem as três marcas medidas falsas é indeterminada. A medida só relaxa o que a tabela de formato fixo prova: a
    // comparação numérica que não ignora caixa, acento nem pontuação.
    private static Classe ClasseDaColacao(ColacaoEsquema? colacao) => colacao switch
    {
        null or { TemRegras: true } => Classe.Indeterminada,
        { Deterministica: true } => Classe.Exata,
        { IgnoraCaixa: false, IgnoraAcento: false, IgnoraPontuacao: false } => Classe.NaoDeterministicaMedida,
        _ => Classe.Indeterminada,
    };

    // As normalizações das embutidas que juntam entradas: CPF e CNPJ de texto completam os zeros; e-mail e chave Pix só
    // pseudonimizam igual sem a caixa quando a coluna compara sem ela.
    private static string? ColidePelaNormalizacao(FabricaRegistrada fabrica, ColunaEsquema? coluna)
    {
        if (!fabrica.Embutida)
        {
            return null;
        }

        if (fabrica.Nome is "br.cpf" or "br.cnpj" && coluna?.Tipo == TipoColuna.Texto)
        {
            return "o CPF e o CNPJ de texto completam os zeros à esquerda";
        }

        // Só o tipo pode comparar sem a caixa aqui: a colação que a ignora não chega a este ponto (é indeterminada).
        var semCaixa = coluna?.IgnoraCaixaPeloTipo == true;
        return (fabrica.Nome, semCaixa) switch
        {
            ("email", false) => "o e-mail é pseudonimizado sem a caixa, e o índice compara com ela",
            (PoliticasQueAnulam.ChavePix, false) => "a chave Pix de e-mail e a aleatória são pseudonimizadas sem a caixa, e o índice compara com ela",
            _ => null,
        };
    }

    private void ErroNaUnicidade(string codigo, string mensagem, Unidade unidade, Par par, string? objeto)
    {
        var efetiva = _efetivas[new No(unidade, par.Origem.Nome)];
        _coletor.Erro(
            new ErroPlano(codigo, mensagem + Propagada(efetiva), unidade.NomeTabela, par.Origem.Nome, objeto, NoYaml(efetiva.Escrita.Posicao, efetiva.Escrita.Local)),
            par.Indice);
    }

    private void AvisarNaUnicidade(string codigo, string mensagem, Unidade unidade, Par par, string? objeto)
    {
        var efetiva = _efetivas[new No(unidade, par.Origem.Nome)];
        _coletor.Aviso(
            new Aviso(codigo, mensagem + Propagada(efetiva), unidade.NomeTabela, par.Origem.Nome, objeto, NoYaml(efetiva.Escrita.Posicao, efetiva.Escrita.Local)),
            par.Indice);
    }

    // Um índice único, uma exclusão ou a chave primária lida direto (sem nome): os elementos (nas exclusões, vazios: só as
    // referências), as colunas citadas pelas expressões, o predicado e se cada NULL é distinto.
    private sealed record IndiceConferido(string? Nome, IReadOnlyList<ElementoIndice> Elementos, ReferenciasDaExpressao? Referencias, ReferenciasDaExpressao? Predicado, bool NulosDistintos, bool Exclusao);

    // A igualdade que o índice aplica à coluna, da mais segura à que recusa.
    private enum Classe
    {
        Exata,
        Normalizada,
        NaoDeterministicaMedida,
        Indeterminada,
    }
}
