// A propagação lê as chaves estrangeiras e a herança do esquema que o dialeto devolveu, tipos do contrato experimental de
// dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// A propagação das regras (P6): as colunas ligadas por chave estrangeira (das duas leituras) ou por herança, entre unidades
/// copiadas, formam componentes que precisam ter uma regra só. A coluna sem regra num componente com regra a recebe, com
/// por onde chegou e de onde; regras diferentes, coluna mantida, <c>derivarDe</c> ou coluna gerada num componente de chave
/// recusam o plano.
/// </summary>
internal sealed partial class MontagemDoPlano
{
    private readonly HashSet<No> _recusadas = [];
    private readonly Dictionary<No, Injetividade> _injetividades = [];

    // P6: as efetivas escritas, o grafo das ligações, cada componente e os avisos das ligações.
    private void Propagar()
    {
        var escritas = new Dictionary<No, RegraColunaEscrita>();
        foreach (var unidade in _unidades.Where(_pares.ContainsKey))
        {
            foreach (var regra in _entradas.TryGetValue(unidade, out var entrada) ? entrada.Colunas : [])
            {
                if (!_colunas.TryGetValue(regra, out var coluna))
                {
                    continue;
                }

                var no = new No(unidade, coluna.Nome);
                escritas[no] = regra;
                if (regra.Tipo != TipoRegraColuna.Manter && _resolvidas.TryGetValue(regra, out var resolvida))
                {
                    _efetivas[no] = new Efetiva(regra, resolvida, null, null);
                }
            }
        }

        var grafo = new Dictionary<No, List<Aresta>>();
        LigarChaves(grafo, _origem.Esquema, doDestino: false);
        LigarChaves(grafo, _destino.Esquema, doDestino: true);
        LigarHeranca(grafo);
        _naChave.UnionWith(grafo.Where(par => par.Value.Any(aresta => aresta.Tipo == PropagacaoRegra.ChaveEstrangeira)).Select(par => par.Key));
        var componentes = Componentes(grafo);
        foreach (var componente in componentes)
        {
            ConferirComponente(componente, grafo, escritas);
        }

        // A gerada calculada de uma tratada, ligada por chave a colunas sem regra: o destino a recalcula do pseudônimo, e a
        // outra ponta da chave levaria o valor real. Depois de propagar tudo, porque a coluna de que ela é calculada pode ter
        // recebido a regra noutro componente.
        foreach (var componente in componentes.Where(componente => !componente.Any(_recusadas.Contains) && componente.Any(_naChave.Contains)))
        {
            foreach (var gerada in componente.Where(no => Gerada(no) && CalculadaDeTratada(no)))
            {
                ErroNo(
                    CodigosPlanejar.GeradaNoComponente,
                    "A coluna gerada é calculada de uma coluna tratada e está ligada por chave estrangeira: o destino a recalcula a partir do pseudônimo, e a outra ponta da chave levaria o valor real. Trate a coluna que referencia com a mesma regra da base, ou tire a gerada da chave.",
                    gerada,
                    escritas);
                _recusadas.UnionWith(componente);
            }
        }

        AvisarChavesNaoValidadas();
        AvisarHerdeirasIgnoradas();
    }

    // Cada chave estrangeira entre unidades copiadas, posição a posição. A do destino chega à coluna da origem pelo
    // pareamento; a posição sem par fica de fora (o erro do pareamento já saiu). A que toca esvaziar ou ignorar é da fronteira.
    private void LigarChaves(Dictionary<No, List<Aresta>> grafo, EsquemaBanco esquema, bool doDestino)
    {
        var porNome = esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        foreach (var tabela in esquema.Tabelas)
        {
            if (!_unidadeDe.TryGetValue(tabela, out var filha) || !_pares.ContainsKey(filha))
            {
                continue;
            }

            foreach (var chave in tabela.ChavesEstrangeiras)
            {
                if (!porNome.TryGetValue(chave.Referenciada, out var referenciada) || !_unidadeDe.TryGetValue(referenciada, out var pai) || !_pares.ContainsKey(pai))
                {
                    continue;
                }

                for (var posicao = 0; posicao < chave.Colunas.Count; posicao++)
                {
                    if (NaOrigem(filha, chave.Colunas[posicao], doDestino) is { } daFilha && NaOrigem(pai, chave.ColunasReferenciadas[posicao], doDestino) is { } doPai)
                    {
                        Ligar(grafo, new No(filha, daFilha), new No(pai, doPai), $"chave {NomeTabela.Parte(chave.Nome)}", PropagacaoRegra.ChaveEstrangeira);
                    }
                }
            }
        }
    }

    // A herança, só entre unidades copiadas: cada herdeira se liga ao ancestral copiado mais próximo de cada ramo, pelas
    // colunas de mesmo nome; um intermediário esvaziado ou ignorado não interrompe a cadeia.
    private void LigarHeranca(Dictionary<No, List<Aresta>> grafo)
    {
        foreach (var herdeira in _origem.Esquema.Tabelas.Where(tabela => tabela.Tipo == TipoTabela.Herdeira))
        {
            if (!_unidadeDe.TryGetValue(herdeira, out var unidade) || !_pares.ContainsKey(unidade))
            {
                continue;
            }

            foreach (var mae in MaesCopiadas(herdeira))
            {
                foreach (var coluna in herdeira.Colunas.Where(coluna => mae.Origem!.Colunas.Any(daMae => daMae.Nome == coluna.Nome)))
                {
                    Ligar(grafo, new No(unidade, coluna.Nome), new No(mae, coluna.Nome), "herança", PropagacaoRegra.Heranca);
                }
            }
        }
    }

    // Os ancestrais copiados mais próximos de cada ramo da herança (Pai e OutrosPais, subindo além dos que não são copiados).
    private List<Unidade> MaesCopiadas(TabelaEsquema herdeira)
    {
        var porNome = _origem.Esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        var vistas = new HashSet<NomeObjeto>();
        var maes = new List<Unidade>();
        var pendentes = new Stack<TabelaEsquema>([herdeira]);
        while (pendentes.TryPop(out var atual))
        {
            foreach (var nome in new[] { atual.Pai }.OfType<NomeObjeto>().Concat(atual.OutrosPais))
            {
                if (!vistas.Add(nome) || !porNome.TryGetValue(nome, out var acima))
                {
                    continue;
                }

                if (_unidadeDe.TryGetValue(acima, out var unidade) && _pares.ContainsKey(unidade))
                {
                    if (!maes.Contains(unidade))
                    {
                        maes.Add(unidade);
                    }
                }
                else
                {
                    pendentes.Push(acima);
                }
            }
        }

        return maes;
    }

    private static void Ligar(Dictionary<No, List<Aresta>> grafo, No um, No outro, string rotulo, PropagacaoRegra tipo)
    {
        // A chave da coluna para ela mesma não liga nada.
        if (um == outro)
        {
            return;
        }

        (grafo.TryGetValue(um, out var deUm) ? deUm : grafo[um] = []).Add(new Aresta(outro, rotulo, tipo));
        (grafo.TryGetValue(outro, out var deOutro) ? deOutro : grafo[outro] = []).Add(new Aresta(um, rotulo, tipo));
    }

    // O nome da coluna na origem da unidade: a da leitura do destino passa pelo pareamento.
    private string? NaOrigem(Unidade unidade, string coluna, bool doDestino) =>
        doDestino
            ? _pares[unidade].FirstOrDefault(par => par.Destino.Nome == coluna)?.Origem.Nome
            : unidade.Origem!.Colunas.Any(daOrigem => daOrigem.Nome == coluna) ? coluna : null;

    // Os componentes com mais de uma coluna, cada um em ordem de tabela e coluna, e os componentes nessa mesma ordem.
    private static List<List<No>> Componentes(Dictionary<No, List<Aresta>> grafo)
    {
        var vistos = new HashSet<No>();
        var componentes = new List<List<No>>();
        foreach (var inicio in grafo.Keys.Order(Comparer<No>.Create(Comparar)))
        {
            if (!vistos.Add(inicio))
            {
                continue;
            }

            var componente = new List<No> { inicio };
            var fila = new Queue<No>([inicio]);
            while (fila.TryDequeue(out var atual))
            {
                foreach (var aresta in grafo[atual].Where(aresta => vistos.Add(aresta.Para)))
                {
                    componente.Add(aresta.Para);
                    fila.Enqueue(aresta.Para);
                }
            }

            componente.Sort(Comparar);
            componentes.Add(componente);
        }

        return componentes;
    }

    // Um componente: uma regra só, e nada que a regra não alcance; sem erro, a coluna sem regra recebe a da escrita mais
    // próxima (no empate, a primeira em ordem de tabela e coluna).
    private void ConferirComponente(List<No> componente, Dictionary<No, List<Aresta>> grafo, Dictionary<No, RegraColunaEscrita> escritas)
    {
        var transformadas = componente.Where(_efetivas.ContainsKey).ToList();
        if (transformadas.Count == 0)
        {
            return;
        }

        var temChave = componente.Any(no => grafo[no].Any(aresta => aresta.Tipo == PropagacaoRegra.ChaveEstrangeira));
        var recusado = false;
        foreach (var mantida in componente.Where(no => escritas.TryGetValue(no, out var regra) && regra.Tipo == TipoRegraColuna.Manter))
        {
            ErroNo(
                CodigosPlanejar.MantidaNoComponente,
                "A coluna mantida está ligada por chave estrangeira ou herança a uma coluna transformada: o valor real ficaria aqui. Apague o 'manter' para a coluna receber a regra da ligada, ou esvazie ou ignore uma das tabelas.",
                mantida,
                escritas);
            recusado = true;
        }

        var grupos = transformadas.GroupBy(no => Assinatura.De(_efetivas[no].Resolvida, _regras.Dominios)).ToList();
        var primeira = grupos[0].First();
        foreach (var outra in grupos.Skip(1).Select(grupo => grupo.First()))
        {
            var (uma, outraRegra) = (_efetivas[primeira].Resolvida, _efetivas[outra].Resolvida);
            var caminho = Caminho(primeira, outra, grafo);
            if (Assinatura.SoNosCitados(uma, outraRegra, _regras.Dominios) is { } citados)
            {
                ErroNo(CodigosPlanejar.DominiosCitadosDiferentes, $"As colunas ligadas citam domínios diferentes ({citados}): {caminho}. Cite os mesmos domínios nas duas.", outra, escritas);
            }
            else
            {
                ErroNo(
                    CodigosPlanejar.RegrasDiferentesNoComponente,
                    $"As colunas ligadas têm regras diferentes ({Assinatura.Diferenca(uma, outraRegra, _regras.Dominios)}): {caminho}. Escreva a mesma regra nas duas.",
                    outra,
                    escritas);
            }

            recusado = true;
        }

        if (temChave)
        {
            foreach (var derivada in transformadas.Where(no => _efetivas[no].Escrita.DerivarDe is not null))
            {
                ErroNo(
                    CodigosPlanejar.DerivarDeNaChave,
                    "A regra com 'derivarDe' está ligada por chave estrangeira a outras colunas: a saída depende de outra coluna em cada tabela, e a chave deixaria de casar. Tire o 'derivarDe', ou use outro transformador nas colunas ligadas.",
                    derivada,
                    escritas);
                recusado = true;
            }

            foreach (var gerada in componente.Where(Gerada))
            {
                ErroNo(
                    CodigosPlanejar.GeradaNoComponente,
                    "A coluna gerada está ligada por chave estrangeira a uma coluna tratada: o banco a recalcula por uma expressão que o Incognito não reproduz, e o valor real ficaria na outra tabela. Tire a regra da ligação, ou a gerada da chave.",
                    gerada,
                    escritas);
                recusado = true;
            }
        }

        if (recusado)
        {
            // O componente em conflito sai dos passos seguintes: os erros dele já dizem o que corrigir.
            _recusadas.UnionWith(componente);
            return;
        }

        // A busca a partir de todas as escritas, em ordem: cada coluna fica com a escrita que a alcança primeiro.
        var chegada = new Dictionary<No, (No Escrita, PropagacaoRegra Tipo)>();
        var fila = new Queue<No>();
        foreach (var escrita in transformadas)
        {
            chegada[escrita] = (escrita, PropagacaoRegra.ChaveEstrangeira);
            fila.Enqueue(escrita);
        }

        while (fila.TryDequeue(out var atual))
        {
            foreach (var aresta in grafo[atual].OrderBy(aresta => aresta.Para, Comparer<No>.Create(Comparar)).ThenBy(aresta => aresta.Rotulo, StringComparer.Ordinal))
            {
                if (chegada.TryAdd(aresta.Para, (chegada[atual].Escrita, aresta.Tipo)))
                {
                    fila.Enqueue(aresta.Para);
                }
            }
        }

        foreach (var no in componente.Where(no => !_efetivas.ContainsKey(no) && !Gerada(no)))
        {
            var (escrita, tipo) = chegada[no];
            var daEscrita = _efetivas[escrita];
            _efetivas[no] = new Efetiva(daEscrita.Escrita, daEscrita.Resolvida, tipo, new NomeColuna(escrita.Unidade.NomeTabela, escrita.Coluna));
        }
    }

    // O caminho mínimo entre duas colunas do componente, com os nomes das ligações (no empate, a ordem de tabela e coluna).
    private static string Caminho(No de, No ate, Dictionary<No, List<Aresta>> grafo)
    {
        var anterior = new Dictionary<No, (No No, string Rotulo)?> { [de] = null };
        var fila = new Queue<No>([de]);
        while (fila.TryDequeue(out var atual) && !anterior.ContainsKey(ate))
        {
            foreach (var aresta in grafo[atual].OrderBy(aresta => aresta.Para, Comparer<No>.Create(Comparar)).ThenBy(aresta => aresta.Rotulo, StringComparer.Ordinal))
            {
                if (anterior.TryAdd(aresta.Para, (atual, aresta.Rotulo)))
                {
                    fila.Enqueue(aresta.Para);
                }
            }
        }

        var partes = new List<string> { Exibir(ate) };
        for (var atual = ate; anterior[atual] is { } passo; atual = passo.No)
        {
            partes.Add($"({passo.Rotulo})");
            partes.Add(Exibir(passo.No));
        }

        partes.Reverse();
        return string.Join(" ", partes);
    }

    // A chave estrangeira da origem entre unidades copiadas que o banco não impõe ou não validou: pode haver linha órfã.
    private void AvisarChavesNaoValidadas()
    {
        var porNome = _origem.Esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        foreach (var tabela in _origem.Esquema.Tabelas)
        {
            if (!_unidadeDe.TryGetValue(tabela, out var filha) || !_pares.ContainsKey(filha))
            {
                continue;
            }

            foreach (var chave in tabela.ChavesEstrangeiras.Where(chave => chave.Imposta != true || chave.Validada != true))
            {
                if (porNome.TryGetValue(chave.Referenciada, out var referenciada) && _unidadeDe.TryGetValue(referenciada, out var pai) && _pares.ContainsKey(pai))
                {
                    _coletor.Aviso(new Aviso(
                        CodigosPlanejar.ChaveNaoValidada,
                        $"A chave estrangeira {NomeTabela.Parte(chave.Nome)} não é imposta ou não foi validada na origem: pode haver linha órfã, e a recriação validada pode falhar.",
                        filha.NomeTabela,
                        objeto: chave.Nome));
                }
            }
        }
    }

    // A herdeira ignorada de uma mãe copiada com coluna tratada fica com o que o destino tem.
    private void AvisarHerdeirasIgnoradas()
    {
        foreach (var herdeira in _origem.Esquema.Tabelas.Where(tabela => tabela.Tipo == TipoTabela.Herdeira))
        {
            if (!_unidadeDe.TryGetValue(herdeira, out var unidade) || _acoes[unidade].Acao != AcaoTabela.Ignorar || unidade.Destino is null)
            {
                continue;
            }

            if (MaesCopiadas(herdeira).Any(mae => herdeira.Colunas.Any(coluna => _efetivas.ContainsKey(new No(mae, coluna.Nome)))))
            {
                _coletor.Aviso(new Aviso(
                    CodigosPlanejar.HerdeiraIgnorada,
                    "A herdeira é ignorada, e a mãe copiada tem coluna tratada: as linhas da herdeira ficam com o que o destino tem, sem a regra.",
                    unidade.NomeTabela));
            }
        }
    }

    // A coluna de derivarDe da regra efetiva: a escrita, ou, na herdeira, a de mesmo nome (a mãe e a herdeira têm a mesma).
    private ColunaEsquema? Derivada(No no, Efetiva efetiva)
    {
        if (efetiva.Escrita.DerivarDe is null || _derivadas.GetValueOrDefault(efetiva.Escrita) is not { } escrita)
        {
            return null;
        }

        return efetiva.Propagacao is null ? escrita : no.Unidade.Origem!.Colunas.FirstOrDefault(coluna => coluna.Nome == escrita.Nome);
    }

    private static string Propagada(Efetiva efetiva) => efetiva.PropagadaDe is { } de ? $" Regra propagada de {de}." : "";

    // Se a gerada é calculada de uma coluna tratada (sem as referências, ou com elas incompletas, de qualquer uma), pelo
    // destino quando há o par e pela origem sempre.
    private bool CalculadaDeTratada(No no)
    {
        var tratadas = _pares[no.Unidade].Where(par => _efetivas.ContainsKey(new No(no.Unidade, par.Origem.Nome))).Select(par => par.Origem.Nome).ToHashSet(StringComparer.Ordinal);
        if (tratadas.Count == 0)
        {
            return false;
        }

        var colunas = no.Unidade.Origem!.Colunas.ToDictionary(coluna => coluna.Nome, StringComparer.Ordinal);
        if (tratadas.Any(tratada => CalculadaDe(no.Coluna, tratada, colunas)))
        {
            return true;
        }

        return _pares[no.Unidade].FirstOrDefault(par => par.Origem.Nome == no.Coluna) is { } par && TratadasNoDestino(no.Unidade).Contains(par.Destino.Nome);
    }

    // A gerada não recebe regra: o destino a recalcula.
    private bool Gerada(No no) =>
        _pares[no.Unidade].FirstOrDefault(par => par.Origem.Nome == no.Coluna) is { } par
            ? par.Origem.Geracao != GeracaoColuna.Nenhuma || par.Destino.Geracao != GeracaoColuna.Nenhuma
            : no.Unidade.Origem!.Colunas.Any(coluna => coluna.Nome == no.Coluna && coluna.Geracao != GeracaoColuna.Nenhuma);

    private void ErroNo(string codigo, string mensagem, No no, Dictionary<No, RegraColunaEscrita> escritas)
    {
        var escrita = escritas.GetValueOrDefault(no) ?? (_efetivas.TryGetValue(no, out var efetiva) ? efetiva.Escrita : null);
        _coletor.Erro(
            new ErroPlano(codigo, mensagem, no.Unidade.NomeTabela, no.Coluna, posicao: escrita is null ? null : NoYaml(escrita.Posicao, escrita.Local)),
            no.Unidade.Origem!.Colunas.ToList().FindIndex(coluna => coluna.Nome == no.Coluna));
    }

    private static int Comparar(No um, No outro)
    {
        var porTabela = ColetorDoPlano.CompararTabelas(um.Unidade.NomeTabela, outro.Unidade.NomeTabela);
        return porTabela != 0 ? porTabela : string.CompareOrdinal(um.Coluna, outro.Coluna);
    }

    private static string Exibir(No no) => new NomeColuna(no.Unidade.NomeTabela, no.Coluna).ToString();

    // Uma coluna de uma unidade, pelo nome na origem.
    private readonly record struct No(Unidade Unidade, string Coluna);

    // Uma coluna da origem com o par no destino e a posição no catálogo da origem.
    private sealed record Par(int Indice, ColunaEsquema Origem, ColunaEsquema Destino);

    // A regra efetiva: a escrita (a da coluna, ou a de onde se propagou) e, na propagada, por onde chegou e de onde.
    private sealed record Efetiva(RegraColunaEscrita Escrita, ColunaResolvida Resolvida, PropagacaoRegra? Propagacao, NomeColuna? PropagadaDe);

    private readonly record struct Aresta(No Para, string Rotulo, PropagacaoRegra Tipo);

    // O que precisa ser igual em todo um componente: o transformador, o domínio, os parâmetros normalizados (inclui os
    // domínios citados) e as políticas que produzem NULL; nunca as políticas escritas que o transformador ignora.
    private sealed record Assinatura(string Transformador, string Dominio, string Parametros, string Politicas)
    {
        internal static Assinatura De(ColunaResolvida coluna, IReadOnlyList<DefinicaoDominio> dominios) => new(
            $"{coluna.Fabrica.Nome}@{coluna.Fabrica.Versao}",
            coluna.Regra.Dominio.ToString(),
            string.Join("|", coluna.Normalizados.OrderBy(parametro => parametro.Key, StringComparer.Ordinal).Select(parametro => $"{parametro.Key}={parametro.Value}")),
            string.Join("|", PoliticasQueAnulam.Da(coluna, dominios).Select(politica => $"{politica.Politica}/{politica.Dominio}/{politica.Parametro}")));

        // A diferença só nos domínios citados ("dominioCpf: a ≠ b"); nula quando há outra diferença (inclusive nas políticas
        // da própria regra) ou nenhuma nos citados.
        internal static string? SoNosCitados(ColunaResolvida uma, ColunaResolvida outra, IReadOnlyList<DefinicaoDominio> dominios)
        {
            if (Proprias(uma, dominios) != Proprias(outra, dominios))
            {
                return null;
            }

            if (uma.Fabrica.Nome != outra.Fabrica.Nome || uma.Fabrica.Versao != outra.Fabrica.Versao || uma.Regra.Dominio != outra.Regra.Dominio)
            {
                return null;
            }

            var citados = uma.Fabrica.ParametrosDeDominio.Keys.Order(StringComparer.Ordinal).ToList();
            string Fora(ColunaResolvida coluna) =>
                string.Join("|", coluna.Normalizados.Where(parametro => !citados.Contains(parametro.Key)).OrderBy(parametro => parametro.Key, StringComparer.Ordinal).Select(parametro => $"{parametro.Key}={parametro.Value}"));
            if (Fora(uma) != Fora(outra))
            {
                return null;
            }

            var diferentes = citados
                .Where(parametro => uma.Normalizados.GetValueOrDefault(parametro) != outra.Normalizados.GetValueOrDefault(parametro))
                .Select(parametro => $"{parametro}: {uma.Normalizados.GetValueOrDefault(parametro)} ≠ {outra.Normalizados.GetValueOrDefault(parametro)}")
                .ToList();
            return diferentes.Count == 0 ? null : string.Join("; ", diferentes);
        }

        // O que difere entre as duas regras: o transformador; ou o domínio, os parâmetros e as políticas que produzem NULL.
        internal static string Diferenca(ColunaResolvida uma, ColunaResolvida outra, IReadOnlyList<DefinicaoDominio> dominios)
        {
            string Transformador(ColunaResolvida coluna) => $"{coluna.Fabrica.Nome}@{coluna.Fabrica.Versao}";
            if (Transformador(uma) != Transformador(outra))
            {
                return $"transformador {Transformador(uma)} ≠ {Transformador(outra)}";
            }

            var partes = new List<string>();
            if (uma.Regra.Dominio != outra.Regra.Dominio)
            {
                partes.Add($"domínio {uma.Regra.Dominio} ≠ {outra.Regra.Dominio}");
            }

            foreach (var parametro in uma.Normalizados.Keys.Union(outra.Normalizados.Keys).Order(StringComparer.Ordinal))
            {
                var (deUma, deOutra) = (uma.Normalizados.GetValueOrDefault(parametro), outra.Normalizados.GetValueOrDefault(parametro));
                if (deUma != deOutra)
                {
                    partes.Add($"{parametro}: {deUma ?? "—"} ≠ {deOutra ?? "—"}");
                }
            }

            var (anulaUma, anulaOutra) = (Anula(uma, dominios), Anula(outra, dominios));
            if (anulaUma != anulaOutra)
            {
                partes.Add($"produz NULL com {anulaUma} ≠ com {anulaOutra}");
            }

            return string.Join("; ", partes);
        }

        // As políticas que produzem NULL, numa frase; "nada" quando nenhuma.
        private static string Anula(ColunaResolvida coluna, IReadOnlyList<DefinicaoDominio> dominios) =>
            PoliticasQueAnulam.Da(coluna, dominios).Select(politica => politica.Dominio is null ? politica.Politica : $"{politica.Politica} do domínio {politica.Dominio}").ToList() is { Count: > 0 } politicas
                ? string.Join(" e ", politicas)
                : "nada";

        // As políticas que produzem NULL da própria regra, fora as dos domínios citados.
        private static string Proprias(ColunaResolvida coluna, IReadOnlyList<DefinicaoDominio> dominios) =>
            string.Join("|", PoliticasQueAnulam.Da(coluna, dominios).Where(politica => politica.Dominio is null).Select(politica => politica.Politica));
    }
}
