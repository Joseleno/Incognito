using System.Diagnostics.CodeAnalysis;
using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// Resolve as regras da configuração contra o registro, antes do banco: a fábrica de cada transformador (sem versão
/// escrita, a 1), o domínio (o declarado ou o padrão da fábrica), as políticas (coluna, domínio, global) e a conferência
/// da fábrica. Um nome de domínio tem uma definição só por configuração (transformador, versão e parâmetros de
/// identidade), e o domínio padrão de uma fábrica só com a definição padrão dela. Os domínios citados por outra regra
/// (<c>dominioCpf</c> na chave Pix e na chave de DF-e) precisam existir e ser do transformador certo; o domínio padrão
/// citado e não usado por nenhuma coluna entra com as políticas globais. Junta todos os erros, como a leitura.
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
    private readonly List<Citacao> _citacoes = [];

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

        return new ResolucaoDeRegras(resolvedor._colunas, resolvedor.Dominios());
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

        // Depois de tudo: um domínio citado pode ser declarado adiante no arquivo, ou numa coluna.
        foreach (var citacao in _citacoes)
        {
            ConferirCitados(citacao);
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
        Citar(fabrica, normalizados, parametros, posicao, local);
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

        // O domínio implícito tem a definição padrão: os parâmetros de identidade já batem com os padrões, e os outros
        // (modelos, dominioCpf...) são da coluna, não do domínio que outra coluna cite pelo nome.
        if (definicao is null)
        {
            _definicoes[nome] = new Definicao(fabrica, new Dictionary<string, string>(StringComparer.Ordinal), PoliticasDeEntrada.Nenhuma, identidade);
        }

        _colunas.Add(new ColunaResolvida(tabela, coluna, fabrica, regra));
        Citar(fabrica, normalizados, coluna.Parametros, coluna.Posicao, coluna.Local);
    }

    private void Citar(FabricaRegistrada fabrica, IReadOnlyDictionary<string, string> normalizados, IReadOnlyList<ParametroRegra> parametros, Posicao posicao, string local)
    {
        if (fabrica.ParametrosDeDominio.Count > 0)
        {
            _citacoes.Add(new Citacao(fabrica, normalizados, parametros, posicao, local));
        }
    }

    // Cada domínio citado existe (declarado, numa coluna ou o padrão do transformador citado) e é desse transformador, na
    // versão 1. O erro aponta o parâmetro escrito, ou a regra quando vale o padrão; nunca traz o nome citado.
    private void ConferirCitados(Citacao citacao)
    {
        foreach (var (parametro, transformador) in citacao.Fabrica.ParametrosDeDominio.OrderBy(citado => citado.Key, StringComparer.Ordinal))
        {
            if (!citacao.Normalizados.TryGetValue(parametro, out var nome) || _falhos.Contains(nome))
            {
                continue;
            }

            var escrito = citacao.Parametros.FirstOrDefault(p => p.Nome == parametro);
            var (posicao, local) = escrito is null ? (citacao.Posicao, citacao.Local) : (escrito.Posicao, escrito.Local);
            if (!_definicoes.TryGetValue(nome, out var definicao))
            {
                if (!_registro.TentarObter(transformador, 1, out var citada) || citada.DominioPadrao != nome)
                {
                    _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local,
                        $"O domínio citado em '{parametro}' não está definido: declare-o em 'dominios' com o {transformador}@1, ou cite o domínio padrão dele.");
                    continue;
                }

                // O padrão citado e não usado por nenhuma coluna: as políticas globais.
                definicao = new Definicao(citada, new Dictionary<string, string>(StringComparer.Ordinal), PoliticasDeEntrada.Nenhuma, []);
                _definicoes[nome] = definicao;
            }

            if (definicao.Fabrica.Nome != transformador || definicao.Fabrica.Versao != 1)
            {
                _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local,
                    $"O domínio citado em '{parametro}' é de outro transformador ou de outra versão: precisa ser do {transformador}@1.");
            }
        }
    }

    // A tabela de domínios da sessão, em ordem de nome: cada um com os parâmetros e as políticas do próprio domínio.
    private List<DefinicaoDominio> Dominios() =>
    [
        .. _definicoes
            .Where(par => !_falhos.Contains(par.Key))
            .OrderBy(par => par.Key, StringComparer.Ordinal)
            .Select(par => new DefinicaoDominio(
                par.Value.Fabrica.Nome,
                Regra(new DominioPseudonimo(par.Key, par.Value.Fabrica.Versao), par.Value.Parametros, [par.Value.Politicas], derivarDe: null, justificativa: null))),
    ];

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
    // recusa com o tipo da exceção (o cancelamento inclusive); o de uma embutida é defeito e sobe.
    private Dictionary<string, string>? Conferir(
        FabricaRegistrada fabrica, RegraTransformacao regra, IReadOnlyList<ParametroRegra> parametros, Posicao posicao, string local)
    {
        try
        {
            fabrica.Fabrica.Validar(regra);
            // Copiado aqui dentro: o dicionário de uma fábrica de fora é código dela, e lido depois rodaria fora do try.
            if (fabrica.Fabrica.Normalizar(regra) is { } normalizados)
            {
                return new Dictionary<string, string>(normalizados, StringComparer.Ordinal);
            }

            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} não devolveu os parâmetros normalizados.");
            return null;
        }
        catch (RegraInvalidaException excecao)
        {
            var parametro = parametros.FirstOrDefault(p => p.Nome == excecao.Parametro);
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, parametro?.Posicao ?? posicao, parametro?.Local ?? local, TextoSeguro.Mensagem(excecao.Message));
            return null;
        }
        catch (OutOfMemoryException) when (DeFora(fabrica))
        {
            throw SemMensagem();
        }
        catch (Exception excecao) when (Inesperado(fabrica, excecao))
        {
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} recusou a regra com um erro inesperado ({TextoSeguro.Nome(excecao.GetType().Name)}).");
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
        catch (OutOfMemoryException) when (DeFora(fabrica))
        {
            throw SemMensagem();
        }
        catch (Exception excecao) when (Inesperado(fabrica, excecao))
        {
            _erros.Adicionar(CodigosConfiguracao.RegraRecusada, posicao, local, $"A fábrica {fabrica.Nome}@{fabrica.Versao} recusou a regra padrão com um erro inesperado ({TextoSeguro.Nome(excecao.GetType().Name)}).");
            padrao = null;
            return false;
        }
    }

    // Erro de uma fábrica de fora vira recusa da regra, só com o tipo; o de uma embutida é defeito e sobe. A resolução roda
    // sem token, e um cancelamento vindo de fora não é do chamador: vira recusa também, para não sair como cancelado (130).
    // A falta de memória nunca vira erro de configuração: sobe nova, sem a mensagem nem a interna de fora.
    private static bool Inesperado(FabricaRegistrada fabrica, Exception excecao) => DeFora(fabrica) && excecao is not OutOfMemoryException;

    private static bool DeFora(FabricaRegistrada fabrica) => !TransformadoresEmbutidos.Fabricas.Contains(fabrica.Fabrica);

    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Relança a falta de memória de uma fábrica de fora sem a mensagem nem a interna dela.")]
    private static OutOfMemoryException SemMensagem() => new();

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

    // Uma regra que cita outros domínios, conferida no fim.
    private sealed record Citacao(
        FabricaRegistrada Fabrica, IReadOnlyDictionary<string, string> Normalizados, IReadOnlyList<ParametroRegra> Parametros, Posicao Posicao, string Local);
}

/// <summary>Uma coluna com transformador: a fábrica e a regra conferida, para o <c>planejar</c> criar o transformador.</summary>
internal sealed record ColunaResolvida(NomeTabela Tabela, RegraColuna Coluna, FabricaRegistrada Fabrica, RegraTransformacao Regra);

/// <summary>
/// As regras resolvidas, na ordem do arquivo, e a tabela de domínios da sessão (os declarados, os implícitos e os
/// citados, com as políticas de cada domínio).
/// </summary>
internal sealed record ResolucaoDeRegras(IReadOnlyList<ColunaResolvida> Colunas, IReadOnlyList<DefinicaoDominio> Dominios);
