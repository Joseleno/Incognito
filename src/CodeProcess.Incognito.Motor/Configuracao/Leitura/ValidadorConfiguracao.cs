using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>
/// Valida a árvore do YAML e monta o modelo, juntando todos os erros. Confere só o que dá para conferir com o arquivo:
/// estrutura, tipos, valores, regra dos nomes, referências de segredo e coerência entre campos. Se a tabela ou a coluna
/// existem, se o transformador existe e aceita os parâmetros, e o resto que depende do banco ou do registro, ficam com o
/// planejar e o registro de transformadores.
/// </summary>
/// <remarks>
/// Mensagens nunca trazem o valor escrito num campo: só o nome do campo (os do esquema são fixos) e a regra. Nomes de
/// tabela e coluna aparecem no <see cref="ErroConfiguracao.Local"/>, porque são estrutura, não dado.
/// </remarks>
internal sealed partial class ValidadorConfiguracao
{
    private const int TamanhoMaximoDoNome = 64;
    private const int TamanhoMaximoDaJustificativa = 500;
    private const int ParalelismoMaximo = 64;
    private const string Manter = "manter";
    private const string IdPermutar = "id.permutar";

    private static readonly string[] _chavesDaRaiz = ["versao", "origem", "destino", "modo", "chave", "ambiente", "politica", "dominios", "tabelas"];
    private static readonly string[] _chavesDaOrigem = ["provedor", "conexao"];
    private static readonly string[] _chavesDoDestino = ["provedor", "conexao", "bancoEsperado"];
    private static readonly string[] _chavesDaPolitica = ["semClassificacao", "suspeitaSemRegra", "entradaInvalida", "entradaMalformada", "consistencia", "paralelismo"];
    private static readonly string[] _chavesDaTabela = ["acao", "colunas"];

    // Chaves fixas do mapa de uma coluna ou de um domínio; o resto é parâmetro do transformador.
    private static readonly string[] _chavesReservadas = ["acao", "transformador", "dominio", "derivarDe", "classe", "justificativa", "entradaInvalida", "entradaMalformada"];

    private static readonly Dictionary<string, ProvedorBanco> _provedores = new(StringComparer.Ordinal)
    {
        ["postgresql"] = ProvedorBanco.PostgreSql,
        ["sqlserver"] = ProvedorBanco.SqlServer,
    };

    private static readonly Dictionary<string, ModoTratamento> _modos = new(StringComparer.Ordinal)
    {
        ["reproduzivel"] = ModoTratamento.Reproduzivel,
        ["efemero"] = ModoTratamento.Efemero,
    };

    private static readonly Dictionary<string, PoliticaEntradaInvalida> _entradaInvalida = new(StringComparer.Ordinal)
    {
        ["preservar"] = PoliticaEntradaInvalida.Preservar,
        ["falhar"] = PoliticaEntradaInvalida.Falhar,
        ["anular"] = PoliticaEntradaInvalida.Anular,
    };

    private static readonly Dictionary<string, PoliticaEntradaMalformada> _entradaMalformada = new(StringComparer.Ordinal)
    {
        ["falhar"] = PoliticaEntradaMalformada.Falhar,
        ["anular"] = PoliticaEntradaMalformada.Anular,
        ["preservar"] = PoliticaEntradaMalformada.Preservar,
    };

    private static readonly Dictionary<string, Consistencia> _consistencias = new(StringComparer.Ordinal)
    {
        ["melhorEsforco"] = Consistencia.MelhorEsforco,
        ["exigir"] = Consistencia.Exigir,
    };

    private static readonly Dictionary<string, AcaoTabela> _acoesDeTabela = new(StringComparer.Ordinal)
    {
        ["copiar"] = AcaoTabela.Copiar,
        ["esvaziar"] = AcaoTabela.Esvaziar,
        ["ignorar"] = AcaoTabela.Ignorar,
    };

    // Valores aceitos na 1.0: só os que a documentação da configuração define.
    private static readonly Dictionary<string, bool> _semClassificacao = new(StringComparer.Ordinal) { ["copiar"] = true };
    private static readonly Dictionary<string, bool> _suspeitaSemRegra = new(StringComparer.Ordinal) { ["falhar"] = true };
    private static readonly Dictionary<string, bool> _acoesDeColuna = new(StringComparer.Ordinal) { [Manter] = true };
    private static readonly Dictionary<string, bool> _classes = new(StringComparer.Ordinal) { ["sensivel"] = true };

    private static readonly string[] _nomesReservadosDoWindows =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    ];

    private readonly ColetorDeErros _erros;
    private readonly string? _pastaBase;
    private readonly Dictionary<string, Posicao> _dominiosDeclarados = new(StringComparer.Ordinal);
    private readonly List<(string Nome, Posicao Posicao, string Local)> _dominiosCitados = [];

    private ValidadorConfiguracao(ColetorDeErros erros, string? pastaBase)
    {
        _erros = erros;
        _pastaBase = pastaBase;
    }

    /// <summary>O modelo, ou nulo se houve erro (os erros ficam no coletor).</summary>
    /// <param name="raiz">A raiz do documento.</param>
    /// <param name="erros">Onde juntar os erros.</param>
    /// <param name="pastaBase">Pasta contra a qual um <c>arquivo:</c> relativo é resolvido; nula recusa caminho relativo.</param>
    /// <param name="arquivo">O arquivo, como foi pedido; nulo quando lido de texto.</param>
    internal static ConfiguracaoIncognito? Validar(NoYaml raiz, ColetorDeErros erros, string? pastaBase, string? arquivo) =>
        new ValidadorConfiguracao(erros, pastaBase).Raiz(raiz, arquivo);

    /// <summary>O nome do provedor como se escreve na configuração (<c>postgresql</c>).</summary>
    /// <param name="provedor">O provedor.</param>
    internal static string Nome(ProvedorBanco provedor) => _provedores.Single(par => par.Value == provedor).Key;

    private ConfiguracaoIncognito? Raiz(NoYaml no, string? arquivo)
    {
        if (no is not MapaYaml raiz)
        {
            _erros.Adicionar(CodigosConfiguracao.Tipo, no.Posicao, no.Local, "A raiz do arquivo precisa ser um mapa (versao, origem, destino, modo...).");
            return null;
        }

        ConferirChaves(raiz, _chavesDaRaiz);
        Versao(raiz);
        var origem = Conexao(raiz, "origem", _chavesDaOrigem, comBancoEsperado: false);
        var destino = Conexao(raiz, "destino", _chavesDoDestino, comBancoEsperado: true);
        var modo = Enumeracao(Obter(raiz, "modo"), raiz, "modo", _modos, obrigatorio: true);
        var chave = Obter(raiz, "chave") is { } noChave ? Segredo(noChave, "chave") : null;
        var ambiente = Ambiente(raiz);
        var politicas = Politicas(raiz);
        var dominios = Dominios(raiz);
        var tabelas = Tabelas(raiz);

        ConferirModoEChave(raiz, modo);
        ConferirConexoes(origem, destino, chave);
        ConferirDominiosCitados();

        return _erros.Vazio
            ? new ConfiguracaoIncognito(arquivo, origem!, destino!, modo!.Value, chave, ambiente!, politicas, dominios, tabelas)
            : null;
    }

    private void Versao(MapaYaml raiz)
    {
        var no = Obter(raiz, "versao");
        if (no is null)
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, raiz.Posicao, "/versao", "Falta 'versao' (use 'versao: 1').");
            return;
        }

        if (Inteiro(no, "versao") is { } versao && versao != 1)
        {
            _erros.Adicionar(CodigosConfiguracao.Valor, no.Posicao, no.Local, "Versão do arquivo não suportada: esta versão do Incognito lê 'versao: 1'.");
        }
    }

    private ConexaoBanco? Conexao(MapaYaml raiz, string campo, string[] chaves, bool comBancoEsperado)
    {
        var no = Obter(raiz, campo);
        if (no is null)
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, raiz.Posicao, "/" + campo, $"Falta '{campo}' (provedor e conexao).");
            return null;
        }

        if (Mapa(no, campo) is not { } mapa)
        {
            return null;
        }

        ConferirChaves(mapa, chaves);
        var provedor = Enumeracao(Obter(mapa, "provedor"), mapa, "provedor", _provedores, obrigatorio: true);
        ReferenciaSegredo? conexao = null;
        if (Obter(mapa, "conexao") is { } noConexao)
        {
            conexao = Segredo(noConexao, "conexao");
        }
        else
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, mapa.Posicao, mapa.Local + "/conexao", $"Falta '{campo}.conexao' (ambiente:NOME ou arquivo:caminho).");
        }

        string? bancoEsperado = null;
        if (comBancoEsperado)
        {
            var noBanco = Obter(mapa, "bancoEsperado");
            bancoEsperado = Texto(noBanco, "bancoEsperado");
            if (noBanco is null or EscalarYaml { Tipo: TipoEscalar.Nulo } || (bancoEsperado is not null && string.IsNullOrWhiteSpace(bancoEsperado)))
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, mapa.Posicao, mapa.Local + "/bancoEsperado", "Falta 'destino.bancoEsperado': o nome do banco de destino, conferido antes de qualquer escrita.");
            }
        }

        return provedor is { } p && conexao is not null ? new ConexaoBanco(p, conexao, bancoEsperado, mapa.Posicao) : null;
    }

    private string? Ambiente(MapaYaml raiz)
    {
        var no = Obter(raiz, "ambiente");
        if (no is null)
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, raiz.Posicao, "/ambiente", "Falta 'ambiente' (por exemplo, 'ambiente: homologacao'): ele entra em todas as derivações.");
            return null;
        }

        var ambiente = Texto(no, "ambiente");
        if (no is EscalarYaml { Tipo: TipoEscalar.Nulo })
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, no.Posicao, no.Local, "'ambiente' vazio (por exemplo, 'ambiente: homologacao').");
        }

        if (ambiente is not null && !NomeValido(ambiente))
        {
            _erros.Adicionar(CodigosConfiguracao.Nome, no.Posicao, no.Local, MensagemDaRegraDosNomes("O ambiente"));
            return null;
        }

        return ambiente;
    }

    private PoliticasGerais Politicas(MapaYaml raiz)
    {
        if (Obter(raiz, "politica") is not { } no || Mapa(no, "politica") is not { } mapa)
        {
            return new PoliticasGerais(PoliticasDeEntrada.Nenhuma, null, null);
        }

        ConferirChaves(mapa, _chavesDaPolitica);
        Enumeracao(Obter(mapa, "semClassificacao"), mapa, "semClassificacao", _semClassificacao, obrigatorio: false);
        Enumeracao(Obter(mapa, "suspeitaSemRegra"), mapa, "suspeitaSemRegra", _suspeitaSemRegra, obrigatorio: false);
        var consistencia = Enumeracao(Obter(mapa, "consistencia"), mapa, "consistencia", _consistencias, obrigatorio: false);
        int? paralelismo = null;
        if (Obter(mapa, "paralelismo") is { } noParalelismo and not EscalarYaml { Tipo: TipoEscalar.Nulo } && Inteiro(noParalelismo, "paralelismo") is { } valor)
        {
            if (valor is < 1 or > ParalelismoMaximo)
            {
                _erros.Adicionar(CodigosConfiguracao.Valor, noParalelismo.Posicao, noParalelismo.Local, $"'paralelismo' vai de 1 a {ParalelismoMaximo}.");
            }
            else
            {
                paralelismo = valor;
            }
        }

        return new PoliticasGerais(PoliticasDoMapa(mapa), consistencia, paralelismo);
    }

    private PoliticasDeEntrada PoliticasDoMapa(MapaYaml mapa) => new(
        Enumeracao(Obter(mapa, "entradaInvalida"), mapa, "entradaInvalida", _entradaInvalida, obrigatorio: false),
        Enumeracao(Obter(mapa, "entradaMalformada"), mapa, "entradaMalformada", _entradaMalformada, obrigatorio: false));

    private List<RegraDominio> Dominios(MapaYaml raiz)
    {
        var dominios = new List<RegraDominio>();
        if (Obter(raiz, "dominios") is not { } no || Mapa(no, "dominios") is not { } mapa)
        {
            return dominios;
        }

        foreach (var (chave, valor) in mapa.Entradas)
        {
            if (!NomeDeDominioValido(chave))
            {
                continue;
            }

            // Declarado mesmo com a definição errada: as colunas que o citam não ganham um erro a mais cada uma.
            Declarar(chave.Valor, chave.Posicao, chave.Local);
            if (Mapa(valor, "dominios") is not { } definicao)
            {
                continue;
            }

            ConferirReservadas(definicao, ["acao", "dominio", "derivarDe", "classe", "justificativa"], "num domínio");
            var transformador = Obter(definicao, "transformador") is { } noTransformador
                ? Transformador(noTransformador)
                : Faltando(definicao, "transformador", "O domínio precisa de 'transformador' (por exemplo, 'br.cpf@1').");
            var parametros = Parametros(definicao);
            var politicas = PoliticasDoMapa(definicao);
            if (transformador is not null)
            {
                dominios.Add(new RegraDominio(chave.Valor, transformador, parametros, politicas, chave.Posicao, valor.Local));
            }
        }

        return dominios;
    }

    private List<RegraTabelaEscrita> Tabelas(MapaYaml raiz)
    {
        var tabelas = new List<RegraTabelaEscrita>();
        if (Obter(raiz, "tabelas") is not { } no || Mapa(no, "tabelas") is not { } mapa)
        {
            return tabelas;
        }

        foreach (var (chave, valor) in mapa.Entradas)
        {
            if (NomeDeTabela(chave) is not { } nome || Mapa(valor, "tabelas") is not { } definicao)
            {
                continue;
            }

            ConferirChaves(definicao, _chavesDaTabela);
            var acaoEscrita = Enumeracao(Obter(definicao, "acao"), definicao, "acao", _acoesDeTabela, obrigatorio: false);
            var acao = acaoEscrita ?? AcaoTabela.Copiar;
            var colunas = new List<RegraColunaEscrita>();
            if (Obter(definicao, "colunas") is { } noColunas)
            {
                if (acao != AcaoTabela.Copiar)
                {
                    _erros.Adicionar(CodigosConfiguracao.Incoerente, noColunas.Posicao, noColunas.Local, "'colunas' só vale com 'acao: copiar' (o padrão).");
                }
                else if (Mapa(noColunas, "colunas") is { } mapaDeColunas)
                {
                    colunas = Colunas(mapaDeColunas);
                }
            }

            tabelas.Add(new RegraTabelaEscrita(nome, acao, acaoEscrita.HasValue, colunas, chave.Posicao, valor.Local));
        }

        return tabelas;
    }

    private List<RegraColunaEscrita> Colunas(MapaYaml mapa)
    {
        var colunas = new List<RegraColunaEscrita>();
        var locais = new Dictionary<Identificador, string>();
        foreach (var (chave, valor) in mapa.Entradas)
        {
            if (Identificador(chave, "coluna") is not { } coluna)
            {
                continue;
            }

            var regra = Coluna(chave, valor);
            if (regra is not null)
            {
                colunas.Add(regra with { Coluna = coluna });
                locais.TryAdd(coluna, valor.Local);
            }
        }

        ConferirCiclos(colunas, locais);
        return colunas;
    }

    private RegraColunaEscrita? Coluna(EscalarYaml chave, NoYaml valor)
    {
        var vazia = new Identificador("", EntreAspas: false);
        switch (valor)
        {
            case EscalarYaml texto when Texto(texto, "coluna") is { } nome:
                if (nome == Manter)
                {
                    return new RegraColunaEscrita(vazia, TipoRegraColuna.Manter, null, null, null, [], PoliticasDeEntrada.Nenhuma, Sensivel: false, null, OrigemRegra.Configuracao, chave.Posicao, valor.Local);
                }

                if (!NomeValido(nome))
                {
                    _erros.Adicionar(CodigosConfiguracao.Nome, texto.Posicao, texto.Local, "Texto sozinho numa coluna é 'manter' ou o nome de um domínio declarado; " + MensagemDaRegraDosNomes("o nome do domínio"));
                    return null;
                }

                _dominiosCitados.Add((nome, texto.Posicao, texto.Local));
                return new RegraColunaEscrita(vazia, TipoRegraColuna.Dominio, nome, null, null, [], PoliticasDeEntrada.Nenhuma, Sensivel: false, null, OrigemRegra.Configuracao, chave.Posicao, valor.Local);
            case EscalarYaml { Tipo: TipoEscalar.Nulo } semRegra:
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, semRegra.Posicao, semRegra.Local, "Regra de coluna vazia: use 'manter', o nome de um domínio ou um mapa.");
                return null;
            case EscalarYaml:
                return null;
            case MapaYaml mapa:
                return ColunaComMapa(chave, mapa, vazia);
            default:
                _erros.Adicionar(CodigosConfiguracao.Tipo, valor.Posicao, valor.Local, "A regra da coluna é 'manter', o nome de um domínio ou um mapa (transformador, dominio ou acao: manter).");
                return null;
        }
    }

    private RegraColunaEscrita? ColunaComMapa(EscalarYaml chave, MapaYaml mapa, Identificador vazia)
    {
        var sensivel = Enumeracao(Obter(mapa, "classe"), mapa, "classe", _classes, obrigatorio: false) ?? false;
        var justificativa = Justificativa(mapa);
        var politicas = PoliticasDoMapa(mapa);
        var derivarDe = Obter(mapa, "derivarDe") is { } noDerivar && Texto(noDerivar, "derivarDe") is not null
            ? Identificador((EscalarYaml)noDerivar, "derivarDe")
            : null;

        if (Obter(mapa, "acao") is { } noAcao)
        {
            if (Enumeracao(noAcao, mapa, "acao", _acoesDeColuna, obrigatorio: true) is null)
            {
                return null;
            }

            foreach (var (outra, _) in mapa.Entradas.Where(entrada => entrada.Key.Valor is not ("acao" or "classe" or "justificativa")))
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, outra.Posicao, mapa.LocalDoFilho(outra.Valor), "'acao: manter' só aceita 'classe' e 'justificativa'.");
            }

            if (sensivel && justificativa is null)
            {
                _erros.Adicionar(CodigosConfiguracao.Justificativa, mapa.Posicao, mapa.Local, "Coluna 'classe: sensivel' mantida sem 'justificativa': escreva por que o dado sensível pode ir em claro.");
            }

            return new RegraColunaEscrita(vazia, TipoRegraColuna.Manter, null, null, null, [], PoliticasDeEntrada.Nenhuma, sensivel, justificativa, OrigemRegra.Configuracao, chave.Posicao, mapa.Local);
        }

        var noTransformador = Obter(mapa, "transformador");
        var noDominio = Obter(mapa, "dominio");
        string? dominio = null;
        if (noDominio is not null)
        {
            dominio = Texto(noDominio, "dominio");
            if (noDominio is EscalarYaml { Tipo: TipoEscalar.Nulo })
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, noDominio.Posicao, noDominio.Local, "'dominio' vazio.");
            }

            if (dominio is not null && !NomeValido(dominio))
            {
                _erros.Adicionar(CodigosConfiguracao.Nome, noDominio.Posicao, noDominio.Local, MensagemDaRegraDosNomes("O domínio"));
                dominio = null;
            }
        }

        if (noTransformador is not null)
        {
            var transformador = Transformador(noTransformador);
            if (dominio is not null && (transformador is null || transformador.Nome == IdPermutar))
            {
                Declarar(dominio, noDominio!.Posicao, noDominio.Local);
            }

            if (transformador is null)
            {
                return null;
            }

            if (noDominio is not null && transformador.Nome != IdPermutar)
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, noDominio.Posicao, noDominio.Local, "'dominio' junto com 'transformador' só vale no id.permutar; para os outros, declare o domínio em 'dominios' e use o nome dele na coluna.");
                return null;
            }

            // Sem o domínio padrão: cada chave primária permutada tem o seu, com o N dela.
            if (noDominio is null && transformador.Nome == IdPermutar)
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, noTransformador.Posicao, noTransformador.Local, "O id.permutar precisa de 'dominio' na coluna: cada chave primária tem o seu ({ transformador: id.permutar@1, dominio: pedido-id }), e as outras colunas o citam com { dominio: pedido-id }.");
                return null;
            }

            return new RegraColunaEscrita(vazia, TipoRegraColuna.Transformador, dominio, transformador, derivarDe, Parametros(mapa), politicas, sensivel, justificativa, OrigemRegra.Configuracao, chave.Posicao, mapa.Local);
        }

        if (noDominio is not null)
        {
            foreach (var parametro in mapa.Entradas.Where(entrada => !_chavesReservadas.Contains(entrada.Key.Valor)))
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, parametro.Key.Posicao, mapa.LocalDoFilho(parametro.Key.Valor), "Parâmetro de transformador numa coluna que usa um domínio: os parâmetros ficam na declaração do domínio.");
            }

            if (dominio is not null)
            {
                _dominiosCitados.Add((dominio, noDominio.Posicao, noDominio.Local));
            }

            return dominio is null ? null : new RegraColunaEscrita(vazia, TipoRegraColuna.Dominio, dominio, null, derivarDe, [], politicas, sensivel, justificativa, OrigemRegra.Configuracao, chave.Posicao, mapa.Local);
        }

        _erros.Adicionar(CodigosConfiguracao.Obrigatorio, mapa.Posicao, mapa.Local, "A regra da coluna precisa de 'transformador', 'dominio' ou 'acao: manter'.");
        return null;
    }

    private string? Justificativa(MapaYaml mapa)
    {
        if (Obter(mapa, "justificativa") is not { } no || Texto(no, "justificativa") is not { } texto)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (texto.Length > TamanhoMaximoDaJustificativa)
        {
            _erros.Adicionar(CodigosConfiguracao.Valor, no.Posicao, no.Local, $"'justificativa' tem no máximo {TamanhoMaximoDaJustificativa} caracteres (vai para o relatório; nunca ponha dado pessoal nela).");
        }

        return texto;
    }

    private List<ParametroRegra> Parametros(MapaYaml mapa)
    {
        var parametros = new List<ParametroRegra>();
        foreach (var (chave, valor) in mapa.Entradas.Where(entrada => !_chavesReservadas.Contains(entrada.Key.Valor)))
        {
            var local = mapa.LocalDoFilho(chave.Valor);
            if (!NomeDeParametro().IsMatch(chave.Valor))
            {
                _erros.Adicionar(CodigosConfiguracao.ChaveDesconhecida, chave.Posicao, mapa.Local, "Nome de parâmetro fora da forma: letras e dígitos, começando por minúscula (por exemplo, 'precisao').");
                continue;
            }

            switch (valor)
            {
                case EscalarYaml { Tipo: not TipoEscalar.Nulo } escalar:
                    parametros.Add(new(chave.Valor, Canonico(escalar), chave.Posicao, local));
                    break;
                case ListaYaml lista when lista.Itens.Count > 0 && lista.Itens.All(item => item is EscalarYaml { Tipo: not TipoEscalar.Nulo } escalar && !escalar.Valor.Contains(',', StringComparison.Ordinal)):
                    parametros.Add(new(chave.Valor, string.Join(',', lista.Itens.Cast<EscalarYaml>().Select(Canonico)), chave.Posicao, local));
                    break;
                default:
                    _erros.Adicionar(CodigosConfiguracao.Tipo, valor.Posicao, local, "O parâmetro precisa ser um valor ou uma lista não vazia de valores (sem vírgula dentro de um item).");
                    break;
            }
        }

        return parametros;
    }

    // Booleano e inteiro do core schema na forma canônica, para a fábrica conferir um texto só: True e TRUE viram true,
    // 0x37 e 0o67 viram 55. Texto (inclusive entre aspas) vai como escrito.
    private static string Canonico(EscalarYaml escalar) => escalar.Tipo switch
    {
        TipoEscalar.Booleano => escalar.Valor.ToLowerInvariant(),
        TipoEscalar.Inteiro when InteiroCanonico(escalar.Valor) is { } inteiro => inteiro,
        _ => escalar.Valor,
    };

    // Em decimal, de qualquer tamanho até 64 dígitos (o mesmo valor escrito com sinal, zeros à esquerda ou em outra base dá
    // o mesmo texto); acima disso, como escrito.
    private static string? InteiroCanonico(string valor)
    {
        const int MaximoDeDigitos = 64;
        var (digitos, baseNumerica) = valor switch
        {
            ['0', 'x', .. var resto] => (resto, 16),
            ['0', 'o', .. var resto] => (resto, 8),
            _ => (valor, 10),
        };

        if (digitos.Length > MaximoDeDigitos + 1)
        {
            return null;
        }

        BigInteger inteiro;
        switch (baseNumerica)
        {
            case 16:
                // O zero na frente impede que o primeiro dígito alto seja lido como sinal.
                inteiro = BigInteger.Parse("0" + digitos, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                break;
            case 8:
                inteiro = BigInteger.Zero;
                foreach (var digito in digitos)
                {
                    inteiro = (inteiro * 8) + (digito - '0');
                }

                break;
            default:
                inteiro = BigInteger.Parse(digitos, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
        }

        return inteiro.ToString(CultureInfo.InvariantCulture);
    }

    private ReferenciaTransformador? Transformador(NoYaml no)
    {
        if (Texto(no, "transformador") is not { } texto)
        {
            if (no is EscalarYaml { Tipo: TipoEscalar.Nulo })
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, no.Posicao, no.Local, "'transformador' vazio (por exemplo, 'br.cpf@1').");
            }

            return null;
        }

        var achado = FormaDoTransformador().Match(texto);
        if (!achado.Success || achado.Groups["nome"].Length > TamanhoMaximoDoNome)
        {
            _erros.Adicionar(CodigosConfiguracao.Transformador, no.Posicao, no.Local, "Transformador fora da forma 'nome@versao' (por exemplo, 'br.cpf@1'); o nome segue a regra dos nomes.");
            return null;
        }

        int? versao = achado.Groups["versao"].Success ? int.Parse(achado.Groups["versao"].Value, CultureInfo.InvariantCulture) : null;
        return new ReferenciaTransformador(achado.Groups["nome"].Value, versao, no.Posicao, no.Local);
    }

    private ReferenciaSegredo? Segredo(NoYaml no, string campo)
    {
        if (Texto(no, campo) is not { } texto)
        {
            if (no is EscalarYaml { Tipo: TipoEscalar.Nulo })
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, no.Posicao, no.Local, $"'{campo}' vazio: use ambiente:NOME ou arquivo:caminho.");
            }

            return null;
        }

        if (texto.StartsWith("ambiente:", StringComparison.Ordinal))
        {
            var nome = texto["ambiente:".Length..];
            if (!NomeDeVariavel().IsMatch(nome))
            {
                _erros.Adicionar(CodigosConfiguracao.Referencia, no.Posicao, no.Local, $"'{campo}': o nome da variável de ambiente usa letras, dígitos e '_', sem começar por dígito, e vem logo depois de 'ambiente:', sem espaço.");
                return null;
            }

            return new ReferenciaSegredo(TipoReferenciaSegredo.Ambiente, nome, nome, no.Posicao);
        }

        if (texto.StartsWith("arquivo:", StringComparison.Ordinal))
        {
            return Arquivo(texto["arquivo:".Length..], no, campo);
        }

        _erros.Adicionar(CodigosConfiguracao.Referencia, no.Posicao, no.Local, $"'{campo}' nunca vai por extenso no arquivo: use ambiente:NOME ou arquivo:caminho.");
        return null;
    }

    private ReferenciaSegredo? Arquivo(string caminho, NoYaml no, string campo)
    {
        string? motivo = null;
        if (caminho.Length == 0 || caminho.Trim().Length != caminho.Length)
        {
            motivo = "o caminho vem logo depois de 'arquivo:', sem espaço nas pontas";
        }
        else if (caminho.StartsWith('~'))
        {
            motivo = "o '~' não é expandido; use um caminho absoluto ou relativo à pasta do incognito.yaml";
        }
        else if (caminho.StartsWith(@"\\", StringComparison.Ordinal) || caminho.StartsWith("//", StringComparison.Ordinal) || CaminhoDoNt().IsMatch(caminho))
        {
            motivo = "caminho de rede ou de dispositivo (\\\\servidor, \\\\.\\, \\\\?\\) não é aceito: o Incognito não acessa a rede além dos bancos";
        }
        else if (caminho.Contains("://", StringComparison.Ordinal) || (caminho.Contains('=', StringComparison.Ordinal) && caminho.Contains(';', StringComparison.Ordinal)))
        {
            motivo = "isto parece uma URL ou uma string de conexão, não um caminho";
        }
        else if (NomeReservadoDoWindows(caminho))
        {
            motivo = "nome de dispositivo do Windows (CON, NUL, COM1...) não é arquivo";
        }

        if (motivo is null && Path.IsPathRooted(caminho) && !Path.IsPathFullyQualified(caminho))
        {
            motivo = "caminho relativo a uma unidade ou à raiz da unidade atual (C:pasta, \\pasta) não é aceito; use o caminho completo";
        }

        if (motivo is null && !Path.IsPathFullyQualified(caminho) && _pastaBase is null)
        {
            motivo = "caminho relativo só é aceito quando o arquivo é carregado de disco (CarregarYaml); aqui use um caminho absoluto";
        }

        string? completo = null;
        if (motivo is null)
        {
            try
            {
                completo = Path.IsPathFullyQualified(caminho) ? Path.GetFullPath(caminho) : Path.GetFullPath(caminho, _pastaBase!);
            }
            catch (Exception excecao) when (excecao is ArgumentException or NotSupportedException or PathTooLongException)
            {
                motivo = "caminho inválido";
            }

            if (completo is not null && (!CaminhoLocal(completo) || UnidadeDeRede(completo)))
            {
                motivo = "o caminho resolvido não é um arquivo local (rede ou dispositivo), e o Incognito não acessa a rede além dos bancos";
            }
        }

        if (motivo is not null)
        {
            _erros.Adicionar(CodigosConfiguracao.Referencia, no.Posicao, no.Local, $"'{campo}': {motivo}.");
            return null;
        }

        return new ReferenciaSegredo(TipoReferenciaSegredo.Arquivo, completo!, caminho.Replace('\\', '/'), no.Posicao);
    }

    // Windows: letra de unidade e barra invertida (nunca \\servidor, \\?\ nem \??\). Outros: começa por uma barra só.
    private static bool CaminhoLocal(string completo) =>
        OperatingSystem.IsWindows()
            ? completo.Length >= 3 && char.IsAsciiLetter(completo[0]) && completo[1] == ':' && completo[2] == '\\'
            : completo.StartsWith('/') && !completo.StartsWith("//", StringComparison.Ordinal);

    // Unidade mapeada para um compartilhamento: abrir o arquivo acessaria a rede.
    private static bool UnidadeDeRede(string completo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return new DriveInfo(completo[..3]).DriveType == DriveType.Network;
        }
        catch (Exception excecao) when (excecao is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ConferirModoEChave(MapaYaml raiz, ModoTratamento? modo)
    {
        var noChave = Obter(raiz, "chave");
        if (modo == ModoTratamento.Reproduzivel && noChave is null)
        {
            _erros.Adicionar(CodigosConfiguracao.Obrigatorio, raiz.Posicao, "/chave", "Falta 'chave', obrigatória no modo reproduzivel (ambiente:NOME ou arquivo:caminho).");
        }
        else if (modo == ModoTratamento.Efemero && noChave is not null)
        {
            _erros.Adicionar(CodigosConfiguracao.Incoerente, noChave.Posicao, noChave.Local, "'chave' é proibida no modo efemero: a chave é gerada na memória e descartada.");
        }
    }

    private void ConferirConexoes(ConexaoBanco? origem, ConexaoBanco? destino, ReferenciaSegredo? chave)
    {
        if (origem is not null && destino is not null)
        {
            if (origem.Provedor != destino.Provedor)
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, destino.Posicao, "/destino/provedor", "Origem e destino precisam ser do mesmo tipo de banco.");
            }

            if (Mesma(origem.Conexao, destino.Conexao))
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, destino.Conexao.Posicao, "/destino/conexao", "Origem e destino usam a mesma referência de conexão: o destino seria a própria origem.");
            }
        }

        foreach (var conexao in new[] { origem, destino })
        {
            if (chave is not null && conexao is not null && Mesma(chave, conexao.Conexao))
            {
                _erros.Adicionar(CodigosConfiguracao.Incoerente, chave.Posicao, "/chave", "A chave e uma conexão usam a mesma referência.");
            }
        }

        static bool Mesma(ReferenciaSegredo uma, ReferenciaSegredo outra) =>
            uma.Tipo == outra.Tipo && string.Equals(uma.Alvo, outra.Alvo, StringComparison.OrdinalIgnoreCase);
    }

    private void Declarar(string nome, Posicao posicao, string local)
    {
        if (nome == Manter)
        {
            _erros.Adicionar(CodigosConfiguracao.Nome, posicao, local, "'manter' é palavra reservada e não pode ser nome de domínio.");
        }
        else if (!_dominiosDeclarados.TryAdd(nome, posicao))
        {
            _erros.Adicionar(CodigosConfiguracao.DominioRepetido, posicao, local, "Domínio declarado mais de uma vez (em 'dominios' ou com 'dominio' numa coluna do id.permutar).");
        }
    }

    private void ConferirDominiosCitados()
    {
        foreach (var (nome, posicao, local) in _dominiosCitados)
        {
            if (!_dominiosDeclarados.ContainsKey(nome))
            {
                _erros.Adicionar(CodigosConfiguracao.DominioDesconhecido, posicao, local, "Domínio não declarado: declare-o em 'dominios' (ou com 'dominio' na coluna de PK do id.permutar).");
            }
        }
    }

    // derivarDe entre colunas da mesma tabela: autorreferência ou ciclo é erro. A existência da coluna referida é do
    // planejar (ela pode não ter regra no arquivo).
    private void ConferirCiclos(List<RegraColunaEscrita> colunas, Dictionary<Identificador, string> locais)
    {
        var porNome = new Dictionary<Identificador, RegraColunaEscrita>();
        foreach (var coluna in colunas)
        {
            porNome.TryAdd(coluna.Coluna, coluna);
        }

        // Cada coluna tem no máximo uma saída: segue o caminho de cada uma até uma coluna já resolvida, sem regra ou
        // repetida no próprio caminho (aí, o trecho a partir dela é o ciclo). Cada coluna é visitada uma vez.
        var resolvidas = new HashSet<Identificador>();
        var emCiclo = new HashSet<Identificador>();
        foreach (var coluna in colunas)
        {
            var caminho = new List<Identificador>();
            var posicoes = new Dictionary<Identificador, int>();
            var atual = coluna.Coluna;
            while (!resolvidas.Contains(atual) && porNome.TryGetValue(atual, out var regra))
            {
                if (posicoes.TryGetValue(atual, out var inicio))
                {
                    emCiclo.UnionWith(caminho.Skip(inicio));
                    break;
                }

                posicoes[atual] = caminho.Count;
                caminho.Add(atual);
                if (regra.DerivarDe is not { } proxima)
                {
                    break;
                }

                atual = proxima;
            }

            resolvidas.UnionWith(caminho);
        }

        foreach (var coluna in colunas.Where(coluna => emCiclo.Contains(coluna.Coluna)))
        {
            _erros.Adicionar(CodigosConfiguracao.Ciclo, coluna.Posicao, locais[coluna.Coluna], "'derivarDe' em ciclo: a coluna depende, direta ou indiretamente, de si mesma.");
        }
    }

    private NomeTabelaEscrito? NomeDeTabela(EscalarYaml chave)
    {
        var partes = Identificadores.Partes(chave.Valor);
        if (partes is not { Count: 2 })
        {
            _erros.Adicionar(CodigosConfiguracao.Identificador, chave.Posicao, "/tabelas", "Tabela fora da forma 'esquema.tabela' (o esquema é obrigatório; nome com ponto ou maiúsculas vai entre aspas duplas, como '\"Vendas\".\"Clientes\"').");
            return null;
        }

        return new NomeTabelaEscrito(partes[0], partes[1]);
    }

    // Nome de coluna (a chave do mapa, cujo local é o do mapa) ou o valor de derivarDe; o local nunca traz o nome errado.
    private Identificador? Identificador(EscalarYaml escalar, string campo)
    {
        var partes = Identificadores.Partes(escalar.Valor);
        if (partes is not { Count: 1 })
        {
            _erros.Adicionar(CodigosConfiguracao.Identificador, escalar.Posicao, escalar.Local, $"Nome de {campo} fora da forma: identificador simples ou entre aspas duplas (com \"\" para uma aspa).");
            return null;
        }

        return partes[0];
    }

    private bool NomeDeDominioValido(EscalarYaml chave)
    {
        if (NomeValido(chave.Valor))
        {
            return true;
        }

        _erros.Adicionar(CodigosConfiguracao.Nome, chave.Posicao, "/dominios", MensagemDaRegraDosNomes("O nome do domínio"));
        return false;
    }

    private static bool NomeValido(string nome) => nome.Length <= TamanhoMaximoDoNome && RegraDosNomes().IsMatch(nome);

    private static string MensagemDaRegraDosNomes(string sujeito) =>
        $"{sujeito} segue a regra dos nomes: letras minúsculas sem acento e dígitos, com '.', '_' ou '-' entre eles, até {TamanhoMaximoDoNome} caracteres.";

    private static bool NomeReservadoDoWindows(string caminho)
    {
        // O nome do arquivo antes da extensão e de um ':' (fluxo alternativo ou "COM1:"), sem espaço no fim.
        var nome = caminho[(caminho.LastIndexOfAny(['/', '\\']) + 1)..];
        var semExtensao = nome.Split('.', ':')[0].TrimEnd(' ');
        return _nomesReservadosDoWindows.Contains(semExtensao, StringComparer.OrdinalIgnoreCase);
    }

    private void ConferirChaves(MapaYaml mapa, string[] conhecidas)
    {
        foreach (var (chave, _) in mapa.Entradas)
        {
            if (!conhecidas.Contains(chave.Valor))
            {
                var sugestao = Sugestao(chave.Valor, conhecidas);
                // O local é o do mapa: o nome escrito errado não volta na mensagem.
                _erros.Adicionar(CodigosConfiguracao.ChaveDesconhecida, chave.Posicao, mapa.Local,
                    sugestao is null ? $"Chave desconhecida aqui; as aceitas são: {string.Join(", ", conhecidas)}." : $"Chave desconhecida aqui; você quis dizer '{sugestao}'?");
            }
        }
    }

    private void ConferirReservadas(MapaYaml mapa, string[] proibidas, string onde)
    {
        foreach (var (chave, _) in mapa.Entradas.Where(entrada => proibidas.Contains(entrada.Key.Valor)))
        {
            _erros.Adicionar(CodigosConfiguracao.Incoerente, chave.Posicao, mapa.LocalDoFilho(chave.Valor), $"'{chave.Valor}' não vale {onde}.");
        }
    }

    // A chave conhecida mais próxima (até 2 edições), sem repetir o que foi escrito.
    private static string? Sugestao(string escrita, string[] conhecidas)
    {
        if (escrita.Length > TamanhoMaximoDoNome)
        {
            return null;
        }

        return conhecidas
            .Select(conhecida => (Conhecida: conhecida, Distancia: Distancia(escrita, conhecida)))
            .Where(par => par.Distancia <= 2)
            .OrderBy(par => par.Distancia)
            .Select(par => par.Conhecida)
            .FirstOrDefault();
    }

    private static int Distancia(string uma, string outra)
    {
        var anterior = new int[outra.Length + 1];
        var atual = new int[outra.Length + 1];
        for (var j = 0; j <= outra.Length; j++)
        {
            anterior[j] = j;
        }

        for (var i = 1; i <= uma.Length; i++)
        {
            atual[0] = i;
            for (var j = 1; j <= outra.Length; j++)
            {
                var custo = char.ToLowerInvariant(uma[i - 1]) == char.ToLowerInvariant(outra[j - 1]) ? 0 : 1;
                atual[j] = Math.Min(Math.Min(atual[j - 1] + 1, anterior[j] + 1), anterior[j - 1] + custo);
            }

            (anterior, atual) = (atual, anterior);
        }

        return anterior[outra.Length];
    }

    private static NoYaml? Obter(MapaYaml mapa, string chave) =>
        mapa.Entradas.FirstOrDefault(entrada => entrada.Key.Valor == chave).Value;

    private MapaYaml? Mapa(NoYaml no, string campo)
    {
        if (no is MapaYaml mapa)
        {
            return mapa;
        }

        _erros.Adicionar(CodigosConfiguracao.Tipo, no.Posicao, no.Local, $"'{campo}' precisa ser um mapa.");
        return null;
    }

    private ReferenciaTransformador? Faltando(MapaYaml mapa, string campo, string mensagem)
    {
        _erros.Adicionar(CodigosConfiguracao.Obrigatorio, mapa.Posicao, mapa.LocalDoFilho(campo), mensagem);
        return null;
    }

    // Texto: escalar entre aspas, ou sem aspas que o core schema do YAML 1.2 lê como texto. Nulo, vazio e '~' contam como
    // ausente; número e booleano sem aspas onde se espera texto é erro (escreva entre aspas).
    private string? Texto(NoYaml? no, string campo)
    {
        switch (no)
        {
            case null:
                return null;
            case EscalarYaml { Tipo: TipoEscalar.Texto } escalar:
                return escalar.Valor;
            case EscalarYaml { Tipo: TipoEscalar.Nulo }:
                return null;
            case EscalarYaml escalar:
                _erros.Adicionar(CodigosConfiguracao.Tipo, escalar.Posicao, escalar.Local, $"'{campo}' espera texto, e o YAML lê este valor como número ou booleano; escreva-o entre aspas.");
                return null;
            default:
                _erros.Adicionar(CodigosConfiguracao.Tipo, no.Posicao, no.Local, $"'{campo}' espera um valor, não um mapa nem uma lista.");
                return null;
        }
    }

    private int? Inteiro(NoYaml no, string campo)
    {
        if (no is EscalarYaml { Simples: true } escalar && InteiroDecimal().IsMatch(escalar.Valor)
            && int.TryParse(escalar.Valor, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var valor))
        {
            return valor;
        }

        _erros.Adicionar(CodigosConfiguracao.Tipo, no.Posicao, no.Local, $"'{campo}' espera um número inteiro, sem aspas.");
        return null;
    }

    private TValor? Enumeracao<TValor>(NoYaml? no, MapaYaml pai, string campo, Dictionary<string, TValor> valores, bool obrigatorio)
        where TValor : struct
    {
        if (no is null || (no is EscalarYaml { Tipo: TipoEscalar.Nulo }))
        {
            if (obrigatorio)
            {
                _erros.Adicionar(CodigosConfiguracao.Obrigatorio, no?.Posicao ?? pai.Posicao, pai.LocalDoFilho(campo), $"Falta '{campo}' ({string.Join(" ou ", valores.Keys)}).");
            }

            return null;
        }

        if (Texto(no, campo) is not { } texto)
        {
            return null;
        }

        if (valores.TryGetValue(texto, out var valor))
        {
            return valor;
        }

        _erros.Adicionar(CodigosConfiguracao.Valor, no.Posicao, no.Local, $"'{campo}' aceita: {string.Join(", ", valores.Keys)}.");
        return null;
    }

    [GeneratedRegex(@"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RegraDosNomes();

    [GeneratedRegex(@"\A(?<nome>[a-z0-9]+(?:[._-][a-z0-9]+)*)(?:@(?<versao>[1-9][0-9]{0,8}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDoTransformador();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NomeDeVariavel();

    [GeneratedRegex(@"\A[a-z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NomeDeParametro();

    // \??\ (objetos do NT), \\?\ e \\.\ (dispositivo), em qualquer combinação de barras.
    [GeneratedRegex(@"\A[\\/][\\/?.][?.]?[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex CaminhoDoNt();

    [GeneratedRegex(@"\A[-+]?[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex InteiroDecimal();
}
