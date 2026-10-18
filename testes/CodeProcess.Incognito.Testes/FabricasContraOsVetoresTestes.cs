using System.Text.Json;
using CodeProcess.Incognito.Testes.Vetores;
using CodeProcess.Incognito.Transformadores;
using CodeProcess.Incognito.Transformadores.Embutidos;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// As fábricas embutidas dão o mesmo que os vetores fixos em todos os casos, com o domínio, as políticas e os parâmetros
/// de cada caso: valor, desfecho e código de erro. É a prova de que o motor, pelas fábricas, sai igual aos vetores (e à
/// fachada, que os vetores também conferem). As mensagens citam arquivo, caso e par, nunca valores.
/// </summary>
public sealed class FabricasContraOsVetoresTestes
{
    private static readonly string[] _transformadores =
        ["br.cpf", "br.cnpj", "br.telefone", "email", "br.cep", "br.chave-pix", "br.chave-dfe", "texto.substituir"];

    // O transformador de cada componente das chaves: o domínio-alvo de dominioCpf, dominioCnpj, dominioEmail e
    // dominioTelefone.
    private static readonly Dictionary<string, (string Transformador, string Parametro)> _componentes = new(StringComparer.Ordinal)
    {
        ["cpf"] = ("br.cpf", "dominioCpf"),
        ["cnpj"] = ("br.cnpj", "dominioCnpj"),
        ["email"] = ("email", "dominioEmail"),
        ["telefone"] = ("br.telefone", "dominioTelefone"),
    };

    public static TheoryData<string> Arquivos => [.. _transformadores.Select(transformador => $"{transformador}@1.json")];

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void A_fabrica_da_o_mesmo_que_o_vetor_em_todos_os_casos(string arquivo)
    {
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);
        var raiz = documento.RootElement;
        var nome = raiz.GetProperty("transformador").GetString()!;
        var fabrica = TransformadoresEmbutidos.Fabricas.Single(f => f.Nome == nome && f.Versao == 1);
        using var chave = ChaveIncognito.Ler(raiz.GetProperty("chave").GetString());
        var conferidos = 0;

        // Uma sessão por ambiente para o arquivo inteiro: casos do mesmo domínio com políticas diferentes só saem certos
        // se a identidade das instâncias separar as políticas. Nas chaves, uma sessão por caso: cada caso tem a própria
        // tabela de domínios (os componentes).
        var sessoes = new Dictionary<string, SessaoTransformacao>(StringComparer.Ordinal);
        try
        {
            var indiceDoCaso = 0;
            foreach (var caso in raiz.GetProperty("casos").EnumerateArray())
            {
                var ambiente = caso.GetProperty("ambiente").GetString()!;
                var componentes = caso.TryGetProperty("componentes", out var lidos) ? lidos : (JsonElement?)null;
                var chaveDaSessao = componentes is null ? ambiente : $"{ambiente}#{indiceDoCaso}";
                indiceDoCaso++;
                if (!sessoes.TryGetValue(chaveDaSessao, out var sessao))
                {
                    sessao = new SessaoTransformacao(chave, ambiente) { Dominios = Dominios(componentes) };
                    sessoes.Add(chaveDaSessao, sessao);
                }

                conferidos += ConferirCaso(arquivo, fabrica, nome, caso, sessao);
            }
        }
        finally
        {
            foreach (var sessao in sessoes.Values)
            {
                sessao.Dispose();
            }
        }

        var pares = raiz.GetProperty("casos").EnumerateArray().Sum(caso => caso.GetProperty("pares").GetArrayLength());
        Assert.True(pares >= 20, $"{arquivo}: poucos pares no vetor.");
        Assert.Equal(pares, conferidos);
    }

    private static int ConferirCaso(string arquivo, FabricaTransformador fabrica, string nome, JsonElement caso, SessaoTransformacao sessao)
    {
        var conferidos = 0;
        var regra = Regra(caso, nome);
        var transformadores = new Dictionary<TipoColuna, Transformador>();
        var indice = 0;
        foreach (var par in caso.GetProperty("pares").EnumerateArray())
        {
            var onde = $"{arquivo}, caso '{caso.GetProperty("nome").GetString()}', par {indice++}";
            var entrada = EntradaDoPar.Ler(par);
            var tipo = entrada.EhNumero ? TipoColuna.Inteiro : TipoColuna.Texto;
            if (!transformadores.TryGetValue(tipo, out var transformador))
            {
                var contexto = new ContextoCriacao(regra, tipo, sessao)
                {
                    TipoReferencia = nome == "br.cep" ? TipoColuna.Texto : null,
                    ComprimentoMaximo = caso.TryGetProperty("comprimentoMaximo", out var comprimento) && comprimento.ValueKind == JsonValueKind.Number ? comprimento.GetInt32() : null,
                };
                transformador = fabrica.Criar(contexto);
                transformadores.Add(tipo, transformador);
            }

            var resultado = Executar(transformador, entrada);
            Assert.True(resultado.Confere(par), $"{onde}: o campo '{resultado.Divergencia(par)}' diverge do vetor.");
            conferidos++;
        }

        return conferidos;
    }

    private static RegraTransformacao Regra(JsonElement caso, string transformador)
    {
        var parametros = new Dictionary<string, string>(StringComparer.Ordinal);
        if (caso.TryGetProperty("permutarOrdem", out var permutarOrdem))
        {
            parametros["permutarOrdem"] = permutarOrdem.GetBoolean() ? "true" : "false";
        }

        if (caso.TryGetProperty("modelos", out var modelos))
        {
            parametros["modelos"] = string.Join(",", modelos.EnumerateArray().Select(modelo => modelo.GetInt32()));
        }

        if (caso.TryGetProperty("componentes", out var componentes))
        {
            foreach (var componente in componentes.EnumerateObject())
            {
                parametros[_componentes[componente.Name].Parametro] = componente.Value.GetProperty("dominio").GetString()!;
            }
        }

        return new RegraTransformacao(new DominioPseudonimo(caso.GetProperty("dominio").GetString()!, 1))
        {
            Parametros = parametros,
            EntradaInvalida = caso.TryGetProperty("entradaInvalida", out var invalida) ? Invalida(invalida.GetString()) : PoliticaEntradaInvalida.Preservar,
            EntradaMalformada = caso.TryGetProperty("entradaMalformada", out var malformada) ? Malformada(malformada.GetString()) : PoliticaEntradaMalformada.Falhar,
            TemDerivarDe = transformador == "br.cep",
            TemJustificativa = true,
        };
    }

    // A tabela de domínios da sessão: um domínio por componente, com o transformador, a versão, o permutarOrdem e a
    // política de DV do vetor.
    private static List<DefinicaoDominio> Dominios(JsonElement? componentes)
    {
        var dominios = new List<DefinicaoDominio>();
        if (componentes is not { } lidos)
        {
            return dominios;
        }

        foreach (var componente in lidos.EnumerateObject())
        {
            var valor = componente.Value;
            var parametros = new Dictionary<string, string>(StringComparer.Ordinal);
            if (valor.TryGetProperty("permutarOrdem", out var permutarOrdem))
            {
                parametros["permutarOrdem"] = permutarOrdem.GetBoolean() ? "true" : "false";
            }

            var dominio = new DominioPseudonimo(valor.GetProperty("dominio").GetString()!, valor.GetProperty("versao").GetInt32());
            dominios.Add(new DefinicaoDominio(_componentes[componente.Name].Transformador, new RegraTransformacao(dominio)
            {
                Parametros = parametros,
                EntradaInvalida = valor.TryGetProperty("entradaInvalida", out var invalida) ? Invalida(invalida.GetString()) : PoliticaEntradaInvalida.Preservar,
            }));
        }

        return dominios;
    }

    private static ResultadoDoPar Executar(Transformador transformador, EntradaDoPar entrada)
    {
        try
        {
            object? valor = entrada.EhNumero ? entrada.Numero : entrada.Texto;
            if (transformador is TransformadorChaveDfe chaveDfe)
            {
                // As ocorrências da chave de DF-e ficam internas até o relatório ganhar o canal delas.
                var comOcorrencias = chaveDfe.Transformar(valor, out var ocorrencias);
                return new ResultadoDoPar(comOcorrencias.Valor, comOcorrencias.Desfecho.ToString(), null) { Ocorrencias = ExecutorDeCaso.Nomes(ocorrencias) };
            }

            var resultado = transformador.Transformar(valor, new ContextoTransformacao(entrada.Entidade));
            return new ResultadoDoPar(resultado.Valor, resultado.Desfecho.ToString(), null);
        }
        catch (IncognitoException excecao)
        {
            return new ResultadoDoPar(null, null, excecao.Codigo);
        }
    }

    private static PoliticaEntradaInvalida Invalida(string? politica) => politica switch
    {
        "preservar" => PoliticaEntradaInvalida.Preservar,
        "anular" => PoliticaEntradaInvalida.Anular,
        "falhar" => PoliticaEntradaInvalida.Falhar,
        _ => throw new InvalidDataException("Política desconhecida."),
    };

    private static PoliticaEntradaMalformada Malformada(string? politica) => politica switch
    {
        "preservar" => PoliticaEntradaMalformada.Preservar,
        "anular" => PoliticaEntradaMalformada.Anular,
        "falhar" => PoliticaEntradaMalformada.Falhar,
        _ => throw new InvalidDataException("Política desconhecida."),
    };
}
