using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// Resolve as regras da configuração contra o registro, antes do banco: a fábrica de cada transformador (sem versão
/// escrita, a 1), o domínio (o declarado ou o padrão da fábrica), as políticas (coluna, domínio, global) e a conferência
/// da fábrica. Um nome de domínio tem uma definição só por configuração (transformador, versão e parâmetros de
/// identidade), e o domínio padrão de uma fábrica só com a definição padrão dela. Junta todos os erros, como a leitura.
/// </summary>
/// <remarks>Usado pelo <c>planejar</c>, que cria os transformadores com as colunas do banco.</remarks>
internal sealed class ResolvedorDeRegras
{
    private readonly ConfiguracaoIncognito _configuracao;
    private readonly RegistroTransformadores _registro;
    private readonly ColetorDeErros _erros;
    private readonly Dictionary<string, Definicao> _definicoes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _falhos = new(StringComparer.Ordinal);
    private readonly Dictionary<FabricaRegistrada, (bool Conseguiu, SortedDictionary<string, string>? Padrao)> _padroes = [];
    private readonly List<ColunaResolvida> _colunas = [];

    private ResolvedorDeRegras(ConfiguracaoIncognito configuracao, RegistroTransformadores registro)
    {
        _configuracao = configuracao;
        _registro = registro;
        _erros = new ColetorDeErros(configuracao.Arquivo);
    }

    /// <summary>Resolve as regras.</summary>
    /// <exception cref="ConfiguracaoInvalidaException">Transformador desconhecido, versão inexistente, regra recusada ou domínio em conflito.</exception>
    internal static ResolucaoDeRegras Resolver(ConfiguracaoIncognito configuracao, RegistroTransformadores registro)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(registro);
        var resolvedor = new ResolvedorDeRegras(configuracao, registro);
        resolvedor.ResolverTudo();
        if (!resolvedor._erros.Vazio)
        {
            throw resolvedor._erros.Excecao();
        }

        return new ResolucaoDeRegras(resolvedor._colunas);
    }

    private void ResolverTudo()
    {
        foreach (var dominio in _configuracao.Dominios)
        {
            Declarar(dominio.Nome, dominio.Transformador, dominio.Parametros, dominio.Politicas, dominio.Posicao, dominio.Local);
        }

        // O domínio declarado numa coluna (o do id.permutar) vale para as outras colunas que o citam, com as políticas
        // escritas na coluna que o declara.
        foreach (var (_, coluna) in Colunas().Where(par => par.Coluna is { Tipo: TipoRegraColuna.Transformador, Dominio: not null }))
        {
            Declarar(coluna.Dominio!, coluna.Transformador!, coluna.Parametros, coluna.Politicas, coluna.Posicao, coluna.Local);
        }

        foreach (var (tabela, coluna) in Colunas())
        {
            switch (coluna)
            {
                case { Tipo: TipoRegraColuna.Manter }:
                    break;
                case { Tipo: TipoRegraColuna.Dominio } or { Tipo: TipoRegraColuna.Transformador, Dominio: not null }:
                    UsarDominio(tabela, coluna);
                    break;
                default:
                    UsarTransformador(tabela, coluna);
                    break;
            }
        }
    }

    private IEnumerable<(NomeTabela Tabela, RegraColuna Coluna)> Colunas() =>
        _configuracao.Tabelas.SelectMany(tabela => tabela.Colunas.Select(coluna => (tabela.Nome, coluna)));

    private void Declarar(
        string nome, ReferenciaTransformador referencia, IReadOnlyList<ParametroRegra> parametros, PoliticasDeEntrada politicas, Posicao posicao, string local)
    {
        if (Fabrica(referencia) is not { } fabrica)
        {
            _falhos.Add(nome);
            return;
        }

        var regra = Regra(new DominioPseudonimo(nome, fabrica.Versao), parametros, [politicas], derivarDe: null, justificativa: null);
        if (Conferir(fabrica, regra, parametros, posicao, local) is not { } normalizados)
        {
            _falhos.Add(nome);
            return;
        }

        var identidade = Identidade(fabrica, normalizados);
        if (_registro.ComDominioPadrao(nome).FirstOrDefault(dona => dona.Nome != fabrica.Nome) is { } outra)
        {
            Conflito(nome, referencia.Posicao, referencia.Local, $"Este nome é o domínio padrão de {outra.Nome}; um domínio de outro transformador precisa de outro nome.");
            return;
        }

        if (fabrica.DominioPadrao == nome)
        {
            if (!TentarIdentidadePadrao(fabrica, posicao, local, out var padrao))
            {
                _falhos.Add(nome);
                return;
            }

            if (!MesmaIdentidade(identidade, padrao))
            {
                var (onde, caminho) = OndeDifere(parametros, identidade, padrao, posicao, local);
                Conflito(nome, onde, caminho, $"Este nome é o domínio padrão de {fabrica.Nome}, com {Lista(fabrica.ParametrosDeIdentidade)} no padrão; com outro valor, use outro nome.");
                return;
            }
        }

        _definicoes[nome] = new Definicao(fabrica, regra.Parametros, politicas, identidade);
    }

    private void UsarDominio(NomeTabela tabela, RegraColuna coluna)
    {
        // Domínio que falhou na declaração (ou não declarado, já apontado na leitura): sem erro em cascata.
        if (!_definicoes.TryGetValue(coluna.Dominio!, out var definicao))
        {
            return;
        }

        var regra = Regra(
            new DominioPseudonimo(coluna.Dominio!, definicao.Fabrica.Versao),
            definicao.Parametros,
            [coluna.Politicas, definicao.Politicas],
            coluna.DerivarDe is not null,
            coluna.Justificativa is not null);
        if (Conferir(definicao.Fabrica, regra, coluna.Parametros, coluna.Posicao, coluna.Local) is not null)
        {
            _colunas.Add(new ColunaResolvida(tabela, coluna, definicao.Fabrica, regra));
        }
    }

    private void UsarTransformador(NomeTabela tabela, RegraColuna coluna)
    {
        if (Fabrica(coluna.Transformador!) is not { } fabrica)
        {
            return;
        }

        // No mesmo universo, as mesmas políticas: a coluna sem 'dominio' herda as do domínio declarado com esse nome.
        var nome = fabrica.DominioPadrao;
        _definicoes.TryGetValue(nome, out var definicao);
        PoliticasDeEntrada[] niveis = definicao is null ? [coluna.Politicas] : [coluna.Politicas, definicao.Politicas];
        var regra = Regra(new DominioPseudonimo(nome, fabrica.Versao), coluna.Parametros, niveis, coluna.DerivarDe is not null, coluna.Justificativa is not null);
        if (Conferir(fabrica, regra, coluna.Parametros, coluna.Posicao, coluna.Local) is not { } normalizados)
        {
            return;
        }

        var identidade = Identidade(fabrica, normalizados);
        if (!TentarIdentidadePadrao(fabrica, coluna.Posicao, coluna.Local, out var padrao))
        {
            return;
        }

        if (!MesmaIdentidade(identidade, padrao))
        {
            var (onde, caminho) = OndeDifere(coluna.Parametros, identidade, padrao, coluna.Posicao, coluna.Local);
            Conflito(null, onde, caminho, $"Sem 'dominio', a coluna usa o domínio padrão {nome}, e {Lista(fabrica.ParametrosDeIdentidade)} fora do padrão criaria outro universo com o mesmo nome: declare um domínio com outro nome em 'dominios' e use-o na coluna.");
            return;
        }

        if (_falhos.Contains(nome))
        {
            return;
        }

        // O transformador e os parâmetros de identidade já batem (o Declarar recusa outro transformador ou outra
        // identidade no nome de um domínio padrão, e o implícito tem a identidade padrão): só a versão pode divergir.
        if (definicao is not null && definicao.Fabrica.Versao != fabrica.Versao)
        {
            Conflito(null, coluna.Transformador!.Posicao, coluna.Transformador.Local, $"O domínio {nome} já está definido com a versão {definicao.Fabrica.Versao}: um nome de domínio tem uma definição só; use outro nome para a versão {fabrica.Versao}.");
            return;
        }

        if (definicao is null)
        {
            _definicoes[nome] = new Definicao(fabrica, regra.Parametros, PoliticasDeEntrada.Nenhuma, identidade);
        }

        _colunas.Add(new ColunaResolvida(tabela, coluna, fabrica, regra));
    }

    private FabricaRegistrada? Fabrica(ReferenciaTransformador referencia)
    {
        if (_registro.TentarObter(referencia.Nome, referencia.Versao ?? 1, out var fabrica))
        {
            return fabrica;
        }

        var recente = _registro.VersaoMaisRecente(referencia.Nome);
        var (codigo, mensagem) = (recente, referencia.Versao) switch
        {
            (null, _) => (CodigosConfiguracao.TransformadorDesconhecido,
                $"Transformador desconhecido: nenhuma fábrica registrada tem esse nome (embutidos: {string.Join(", ", TransformadoresEmbutidos.Fabricas.Select(f => f.Nome))})."),
            (_, null) => (CodigosConfiguracao.VersaoInexistente,
                $"Sem versão escrita vale a versão 1, que este transformador não tem: escreva a versão (a mais recente é @{recente})."),
            _ => (CodigosConfiguracao.VersaoInexistente, $"Versão não registrada para este transformador (a mais recente é @{recente})."),
        };
        _erros.Adicionar(codigo, referencia.Posicao, referencia.Local, mensagem);
        return null;
    }

    // A fábrica confere a regra; a recusa aponta o parâmetro, quando há. Erro inesperado de uma fábrica de fora vira
    // recusa com o tipo da exceção; o de uma embutida é defeito e sobe.
    private IReadOnlyDictionary<string, string>? Conferir(
        FabricaRegistrada fabrica, RegraTransformacao regra, IReadOnlyList<ParametroRegra> parametros, Posicao posicao, string local)
    {
        try
        {
            fabrica.Fabrica.Validar(regra);
            if (fabrica.Fabrica.Normalizar(regra) is { } normalizados)
            {
                return normalizados;
            }

            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} não devolveu os parâmetros normalizados.");
            return null;
        }
        catch (RegraInvalidaException excecao)
        {
            var parametro = parametros.FirstOrDefault(p => p.Nome == excecao.Parametro);
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, parametro?.Posicao ?? posicao, parametro?.Local ?? local, excecao.Message);
            return null;
        }
        catch (Exception excecao) when (Inesperado(fabrica, excecao))
        {
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} recusou a regra com um erro inesperado ({excecao.GetType().Name}).");
            return null;
        }
    }

    private RegraTransformacao Regra(
        DominioPseudonimo dominio, IEnumerable<KeyValuePair<string, string>> parametros, PoliticasDeEntrada[] niveis, bool? derivarDe, bool? justificativa)
    {
        var global = _configuracao.Politicas.Entrada;
        return new RegraTransformacao(dominio)
        {
            Parametros = parametros.ToDictionary(StringComparer.Ordinal),
            EntradaInvalida = niveis.Append(global).Select(nivel => nivel.EntradaInvalida).FirstOrDefault(politica => politica is not null) ?? PoliticaEntradaInvalida.Preservar,
            EntradaMalformada = niveis.Append(global).Select(nivel => nivel.EntradaMalformada).FirstOrDefault(politica => politica is not null) ?? PoliticaEntradaMalformada.Falhar,
            TemDerivarDe = derivarDe,
            TemJustificativa = justificativa,
        };
    }

    private RegraTransformacao Regra(
        DominioPseudonimo dominio, IReadOnlyList<ParametroRegra> parametros, PoliticasDeEntrada[] niveis, bool? derivarDe, bool? justificativa) =>
        Regra(dominio, parametros.Select(parametro => new KeyValuePair<string, string>(parametro.Nome, parametro.Valor)), niveis, derivarDe, justificativa);

    // Os parâmetros que mudam o universo, normalizados; nenhum quando o transformador não liga valores (injetividade
    // Nenhuma), porque aí não há universo a dividir.
    private static SortedDictionary<string, string> Identidade(FabricaRegistrada fabrica, IReadOnlyDictionary<string, string> normalizados)
    {
        var identidade = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (fabrica.Injetividade == Injetividade.Nenhuma)
        {
            return identidade;
        }

        foreach (var parametro in fabrica.ParametrosDeIdentidade)
        {
            identidade[parametro] = normalizados.TryGetValue(parametro, out var valor) ? valor : "";
        }

        return identidade;
    }

    // A identidade da regra sem parâmetros; nula se a fábrica não aceita a regra vazia (aí não há padrão a usar). Erro
    // inesperado de uma fábrica de fora vira recusa, como no Conferir; o de uma embutida sobe.
    // Calculada uma vez por fábrica: o erro de uma fábrica de fora sai uma vez, não em cada coluna.
    private bool TentarIdentidadePadrao(FabricaRegistrada fabrica, Posicao posicao, string local, out SortedDictionary<string, string>? padrao)
    {
        if (!_padroes.TryGetValue(fabrica, out var calculada))
        {
            calculada.Conseguiu = CalcularIdentidadePadrao(fabrica, posicao, local, out calculada.Padrao);
            _padroes.Add(fabrica, calculada);
        }

        padrao = calculada.Padrao;
        return calculada.Conseguiu;
    }

    private bool CalcularIdentidadePadrao(FabricaRegistrada fabrica, Posicao posicao, string local, out SortedDictionary<string, string>? padrao)
    {
        padrao = [];
        if (fabrica.Injetividade == Injetividade.Nenhuma || fabrica.ParametrosDeIdentidade.Count == 0)
        {
            return true;
        }

        try
        {
            var normalizados = fabrica.Fabrica.Normalizar(new RegraTransformacao(new DominioPseudonimo(fabrica.DominioPadrao, fabrica.Versao)));
            padrao = normalizados is null ? null : Identidade(fabrica, normalizados);
            return true;
        }
        catch (RegraInvalidaException)
        {
            padrao = null;
            return true;
        }
        catch (Exception excecao) when (Inesperado(fabrica, excecao))
        {
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} recusou a regra padrão com um erro inesperado ({excecao.GetType().Name}).");
            padrao = null;
            return false;
        }
    }

    // Erro de uma fábrica de fora vira recusa da regra; o de uma embutida é defeito e sobe, e o cancelamento e a falta de
    // memória nunca viram erro de configuração.
    private static bool Inesperado(FabricaRegistrada fabrica, Exception excecao) =>
        !TransformadoresEmbutidos.Fabricas.Contains(fabrica.Fabrica) && excecao is not (OperationCanceledException or OutOfMemoryException);

    private static bool MesmaIdentidade(SortedDictionary<string, string> uma, SortedDictionary<string, string>? outra) =>
        outra is null ? uma.Count == 0 : uma.SequenceEqual(outra);

    // O primeiro parâmetro de identidade escrito com valor diferente do padrão; sem nenhum, a regra.
    private static (Posicao Posicao, string Local) OndeDifere(
        IReadOnlyList<ParametroRegra> parametros, SortedDictionary<string, string> identidade, SortedDictionary<string, string>? padrao, Posicao posicao, string local)
    {
        foreach (var parametro in parametros)
        {
            if (identidade.TryGetValue(parametro.Nome, out var valor) && (padrao is null || !padrao.TryGetValue(parametro.Nome, out var original) || original != valor))
            {
                return (parametro.Posicao, parametro.Local);
            }
        }

        return (posicao, local);
    }

    private static string Lista(IEnumerable<string> nomes) => string.Join(", ", nomes.Order(StringComparer.Ordinal));

    // Um domínio em conflito fica de fora, como o que falhou na conferência: as colunas que o citam não ganham outro erro.
    private void Conflito(string? dominio, Posicao posicao, string local, string mensagem)
    {
        if (dominio is not null)
        {
            _falhos.Add(dominio);
        }

        _erros.Adicionar(CodigosConfiguracao.ConflitoDeDominio, posicao, local, mensagem);
    }

    private sealed record Definicao(
        FabricaRegistrada Fabrica, IReadOnlyDictionary<string, string> Parametros, PoliticasDeEntrada Politicas, SortedDictionary<string, string> Identidade);
}

/// <summary>Uma coluna com transformador: a fábrica e a regra conferida, para o <c>planejar</c> criar o transformador.</summary>
internal sealed record ColunaResolvida(NomeTabela Tabela, RegraColuna Coluna, FabricaRegistrada Fabrica, RegraTransformacao Regra);

/// <summary>As regras resolvidas, na ordem do arquivo.</summary>
internal sealed record ResolucaoDeRegras(IReadOnlyList<ColunaResolvida> Colunas);
