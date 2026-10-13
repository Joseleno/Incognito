using System.Text;
using System.Text.Json;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Leitura estrita dos vetores fixos: propriedade desconhecida, faltando ou repetida falha, os tipos JSON são conferidos
/// (inteiro é inteiro, texto é texto) e os parâmetros de cada transformador são os da lista dele. Assim nenhum parâmetro
/// do caso é lido e ignorado. As mensagens citam arquivo, caso e par, nunca valores.
/// </summary>
internal static class EsquemaDeVetores
{
    private static readonly string[] _propriedadesDoArquivo = ["descricao", "transformador", "versao", "chave", "casos"];

    private static readonly string[] _origens = ["gerador-br", "construido", "inversa", "literal"];

    // Parâmetros do caso por transformador e versão, além de nome, dominio, ambiente e pares. Uma versão nova entra aqui só
    // quando tem implementação: sem isso, o arquivo dela seria recusado em vez de conferido com o algoritmo de outra.
    private static readonly Dictionary<string, string[]> _parametros = new(StringComparer.Ordinal)
    {
        ["br.cpf@1"] = ["entradaInvalida", "entradaMalformada"],
        ["br.cnpj@1"] = ["permutarOrdem", "entradaInvalida", "entradaMalformada"],
        ["br.telefone@1"] = ["entradaMalformada"],
        ["email@1"] = ["entradaMalformada"],
        ["br.cep@1"] = ["entradaMalformada"],
        ["id.permutar@1"] = ["n", "entradaMalformada"],
        ["br.chave-pix@1"] = ["entradaMalformada", "componentes"],
        ["br.chave-dfe@1"] = ["modelos", "entradaInvalida", "entradaMalformada", "componentes"],
    };

    // Os componentes de cada transformador composto: as instâncias das colunas que ele usa.
    private static readonly Dictionary<string, Dictionary<string, string[]>> _componentes = new(StringComparer.Ordinal)
    {
        ["br.chave-pix"] = new(StringComparer.Ordinal)
        {
            ["cpf"] = ["dominio", "versao", "entradaInvalida"],
            ["cnpj"] = ["dominio", "versao", "permutarOrdem", "entradaInvalida"],
            ["email"] = ["dominio", "versao"],
            ["telefone"] = ["dominio", "versao"],
        },
        ["br.chave-dfe"] = new(StringComparer.Ordinal)
        {
            ["cpf"] = ["dominio", "versao"],
            ["cnpj"] = ["dominio", "versao", "permutarOrdem"],
        },
    };

    // Os modelos da chave de DF-e e as ocorrências, na ordem canônica (crescente de valor da flag), escritos aqui à parte
    // das receitas e do enumerado: o esquema não deriva do que confere.
    private static readonly int[] _modelosDaChaveDfe = [55, 65, 57, 58, 67, 64, 63, 66, 62];

    private static readonly string[] _ocorrencias =
        ["NumeroZero", "SequencialZero", "DataNffInvalida", "CodigoProibido", "CodigoIgualAoNumero", "EmitentePontoFixo", "DvDoEmitenteInvalido", "Anomalia"];

    /// <summary>Pasta dos vetores copiados para a saída dos testes.</summary>
    internal static string PastaNaSaida => Path.Combine(AppContext.BaseDirectory, "vetores");

    /// <summary>Lê um vetor da pasta de saída com a leitura estrita e confere o arquivo inteiro.</summary>
    internal static JsonDocument LerDaSaida(string arquivo) => Ler(Path.Combine(PastaNaSaida, arquivo));

    /// <summary>Lê um vetor com a leitura estrita (UTF-8 sem BOM nem byte inválido) e confere o arquivo inteiro.</summary>
    internal static JsonDocument Ler(string caminho)
    {
        var arquivo = Path.GetFileName(caminho);
        var bytes = File.ReadAllBytes(caminho);
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            throw Falha(arquivo, "BOM no início");
        }

        string texto;
        try
        {
            texto = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Falha(arquivo, "UTF-8 inválido");
        }

        var documento = JsonDocument.Parse(texto, Opcoes);
        try
        {
            Conferir(documento.RootElement, arquivo);
            return documento;
        }
        catch
        {
            documento.Dispose();
            throw;
        }
    }

    /// <summary>Transformadores e versões com conferidor (<c>br.cpf@1</c>).</summary>
    internal static IEnumerable<string> Transformadores => _parametros.Keys;

    /// <summary>Opções do leitor: comentários, vírgulas sobrando e propriedades repetidas são recusados.</summary>
    internal static JsonDocumentOptions Opcoes { get; } = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    /// <summary>Confere o arquivo inteiro e devolve o transformador declarado.</summary>
    internal static string Conferir(JsonElement raiz, string arquivo)
    {
        Exigir(raiz, _propriedadesDoArquivo, [], arquivo);
        var transformador = Texto(raiz, "transformador", arquivo);
        var versao = Inteiro(raiz, "versao", arquivo);
        if (!_parametros.TryGetValue($"{transformador}@{versao}", out var parametros))
        {
            throw Falha(arquivo, "transformador ou versão sem conferidor");
        }

        if (!string.Equals(arquivo, $"{transformador}@{versao}.json", StringComparison.Ordinal))
        {
            throw Falha(arquivo, "nome do arquivo diferente de transformador@versão");
        }

        Texto(raiz, "descricao", arquivo);
        Texto(raiz, "chave", arquivo);

        var casos = Lista(raiz, "casos", arquivo);
        if (casos.GetArrayLength() == 0)
        {
            throw Falha(arquivo, "sem casos");
        }

        var nomes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var caso in casos.EnumerateArray())
        {
            var nome = caso.ValueKind == JsonValueKind.Object && caso.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()!
                : throw Falha(arquivo, "caso sem nome");
            var onde = $"{arquivo}, caso '{nome}'";
            if (!nomes.Add(nome))
            {
                throw Falha(onde, "nome repetido");
            }

            ConferirCaso(caso, transformador, parametros, onde);
        }

        return transformador;
    }

    private static void ConferirCaso(JsonElement caso, string transformador, string[] parametros, string onde)
    {
        Exigir(caso, ["nome", "dominio", "ambiente", "pares", .. parametros], [], onde);
        var dominio = Texto(caso, "dominio", onde);
        if (transformador == "br.chave-dfe" && dominio != "dfe-numero")
        {
            throw Falha(onde, "o domínio do número e do código da chave de DF-e é sempre dfe-numero");
        }

        Texto(caso, "ambiente", onde);
        foreach (var parametro in parametros)
        {
            switch (parametro)
            {
                case "entradaInvalida" or "entradaMalformada":
                    Politica(caso, parametro, onde);
                    break;
                case "permutarOrdem":
                    Logico(caso, parametro, onde);
                    break;
                case "n":
                    Inteiro(caso, parametro, onde);
                    break;
                case "componentes":
                    ConferirComponentes(caso.GetProperty(parametro), _componentes[transformador], onde);
                    break;
                case "modelos":
                    ConferirModelos(caso, onde);
                    break;
                default:
                    throw Falha(onde, "parâmetro sem conferência");
            }
        }

        var pares = Lista(caso, "pares", onde);
        if (pares.GetArrayLength() == 0)
        {
            throw Falha(onde, "sem pares");
        }

        var indice = 0;
        foreach (var par in pares.EnumerateArray())
        {
            ConferirPar(par, transformador, $"{onde}, par {indice++}");
        }
    }

    private static void ConferirModelos(JsonElement caso, string onde)
    {
        var modelos = Lista(caso, "modelos", onde);
        var vistos = new HashSet<int>();
        foreach (var modelo in modelos.EnumerateArray())
        {
            if (modelo.ValueKind != JsonValueKind.Number || !modelo.TryGetInt32(out var codigo) || !_modelosDaChaveDfe.Contains(codigo) || !vistos.Add(codigo))
            {
                throw Falha(onde, "modelo fora dos nove ou repetido");
            }
        }

        if (vistos.Count == 0)
        {
            throw Falha(onde, "lista de modelos vazia");
        }
    }

    private static void ConferirComponentes(JsonElement componentes, Dictionary<string, string[]> esperados, string onde)
    {
        Exigir(componentes, [.. esperados.Keys], [], onde);
        foreach (var (nome, propriedades) in esperados)
        {
            var componente = componentes.GetProperty(nome);
            var ondeComponente = $"{onde}, componente {nome}";
            Exigir(componente, propriedades, [], ondeComponente);
            Texto(componente, "dominio", ondeComponente);
            if (Inteiro(componente, "versao", ondeComponente) != 1)
            {
                throw Falha(ondeComponente, "versão sem implementação");
            }

            if (componente.TryGetProperty("permutarOrdem", out _))
            {
                Logico(componente, "permutarOrdem", ondeComponente);
            }

            if (componente.TryGetProperty("entradaInvalida", out _))
            {
                Politica(componente, "entradaInvalida", ondeComponente);
            }
        }
    }

    private static void ConferirPar(JsonElement par, string transformador, string onde)
    {
        string[] obrigatorias = ["ramo", "origem"];
        string[] opcionais = transformador switch
        {
            "br.cep" => ["entrada", "entradaUtf16", "entidade", "entidadeUtf16", "saida", "erro", "desfecho"],
            "br.chave-dfe" => ["entrada", "entradaUtf16", "saida", "erro", "desfecho", "ocorrencias"],
            _ => ["entrada", "entradaUtf16", "saida", "erro", "desfecho"],
        };
        Exigir(par, obrigatorias, opcionais, onde);
        Texto(par, "ramo", onde);
        UmDos(par, "entrada", "entradaUtf16", onde);
        if (transformador == "br.cep")
        {
            UmDos(par, "entidade", "entidadeUtf16", onde);
            if (par.TryGetProperty("entidade", out _))
            {
                Texto(par, "entidade", onde);
            }
        }

        var temSaida = par.TryGetProperty("saida", out var saida);
        var temErro = par.TryGetProperty("erro", out _);
        var temDesfecho = par.TryGetProperty("desfecho", out _);
        if (temSaida == temErro || temSaida != temDesfecho)
        {
            throw Falha(onde, "precisa de saída e desfecho, ou só de erro");
        }

        if (temErro && Texto(par, "erro", onde) is not ("INC1003" or "INC1004"))
        {
            throw Falha(onde, "código de erro desconhecido");
        }

        var entradaNumerica = par.TryGetProperty("entrada", out var entrada) && entrada.ValueKind == JsonValueKind.Number;
        if (par.TryGetProperty("entrada", out _) && !entradaNumerica && entrada.ValueKind != JsonValueKind.String)
        {
            throw Falha(onde, "entrada nem texto nem inteiro");
        }

        if (entradaNumerica)
        {
            Inteiro(par, "entrada", onde);
        }

        if (temSaida)
        {
            var esperado = entradaNumerica ? JsonValueKind.Number : JsonValueKind.String;
            if (saida.ValueKind != JsonValueKind.Null && saida.ValueKind != esperado)
            {
                throw Falha(onde, "saída de outro tipo que a entrada");
            }

            if (entradaNumerica && saida.ValueKind == JsonValueKind.Number)
            {
                Inteiro(par, "saida", onde);
            }

            Texto(par, "desfecho", onde);
        }

        // Na chave de DF-e, as ocorrências acompanham a saída: nomes conhecidos, na ordem canônica, sem repetição.
        if (par.TryGetProperty("ocorrencias", out _) != (temSaida && transformador == "br.chave-dfe"))
        {
            throw Falha(onde, "ocorrências só com a saída da chave de DF-e, e sempre com ela");
        }

        if (temSaida && transformador == "br.chave-dfe")
        {
            var anterior = -1;
            foreach (var ocorrencia in Lista(par, "ocorrencias", onde).EnumerateArray())
            {
                var posicao = ocorrencia.ValueKind == JsonValueKind.String ? Array.IndexOf(_ocorrencias, ocorrencia.GetString()) : -1;
                if (posicao <= anterior)
                {
                    throw Falha(onde, "ocorrência desconhecida, repetida ou fora da ordem");
                }

                anterior = posicao;
            }
        }

        ConferirOrigem(par.GetProperty("origem"), $"{onde}, origem");
    }

    private static void ConferirOrigem(JsonElement origem, string onde)
    {
        if (origem.ValueKind != JsonValueKind.Object || !origem.TryGetProperty("tipo", out var tipo) || tipo.ValueKind != JsonValueKind.String)
        {
            throw Falha(onde, "sem tipo");
        }

        switch (tipo.GetString())
        {
            case "gerador-br":
                Exigir(origem, ["tipo", "metodo", "semente", "formas"], [], onde);
                Texto(origem, "metodo", onde);
                break;
            case "construido" or "inversa":
                Exigir(origem, ["tipo", "regra", "semente", "formas"], [], onde);
                Texto(origem, "regra", onde);
                break;
            case "literal":
                Exigir(origem, ["tipo"], [], onde);
                return;
            default:
                throw Falha(onde, $"tipo fora de {string.Join(", ", _origens)}");
        }

        Inteiro(origem, "semente", onde);
        foreach (var forma in Lista(origem, "formas", onde).EnumerateArray())
        {
            if (forma.ValueKind != JsonValueKind.String)
            {
                throw Falha(onde, "forma que não é texto");
            }
        }
    }

    private static void Exigir(JsonElement objeto, string[] obrigatorias, string[] opcionais, string onde)
    {
        if (objeto.ValueKind != JsonValueKind.Object)
        {
            throw Falha(onde, "não é objeto");
        }

        foreach (var propriedade in objeto.EnumerateObject())
        {
            if (!obrigatorias.Contains(propriedade.Name, StringComparer.Ordinal) && !opcionais.Contains(propriedade.Name, StringComparer.Ordinal))
            {
                throw Falha(onde, $"propriedade desconhecida '{propriedade.Name}'");
            }
        }

        foreach (var obrigatoria in obrigatorias)
        {
            if (!objeto.TryGetProperty(obrigatoria, out _))
            {
                throw Falha(onde, $"falta a propriedade '{obrigatoria}'");
            }
        }
    }

    private static void UmDos(JsonElement objeto, string primeira, string segunda, string onde)
    {
        if (objeto.TryGetProperty(primeira, out _) == objeto.TryGetProperty(segunda, out _))
        {
            throw Falha(onde, $"precisa de exatamente uma entre '{primeira}' e '{segunda}'");
        }

        if (objeto.TryGetProperty(segunda, out _))
        {
            Texto(objeto, segunda, onde);
        }
    }

    private static string Texto(JsonElement objeto, string propriedade, string onde) =>
        objeto.GetProperty(propriedade) is { ValueKind: JsonValueKind.String } valor
            ? valor.GetString()!
            : throw Falha(onde, $"'{propriedade}' não é texto");

    private static long Inteiro(JsonElement objeto, string propriedade, string onde) =>
        objeto.GetProperty(propriedade) is { ValueKind: JsonValueKind.Number } valor && valor.TryGetInt64(out var inteiro)
            ? inteiro
            : throw Falha(onde, $"'{propriedade}' não é inteiro de 64 bits");

    private static bool Logico(JsonElement objeto, string propriedade, string onde) =>
        objeto.GetProperty(propriedade).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Falha(onde, $"'{propriedade}' não é lógico"),
        };

    private static JsonElement Lista(JsonElement objeto, string propriedade, string onde) =>
        objeto.GetProperty(propriedade) is { ValueKind: JsonValueKind.Array } lista
            ? lista
            : throw Falha(onde, $"'{propriedade}' não é lista");

    private static void Politica(JsonElement objeto, string propriedade, string onde)
    {
        if (Texto(objeto, propriedade, onde) is not ("preservar" or "anular" or "falhar"))
        {
            throw Falha(onde, $"'{propriedade}' fora de preservar, anular e falhar");
        }
    }

    private static InvalidDataException Falha(string onde, string motivo) => new($"{onde}: {motivo}.");
}
