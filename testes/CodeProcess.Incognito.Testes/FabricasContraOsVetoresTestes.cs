using System.Text.Json;
using CodeProcess.Incognito.Testes.Vetores;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// As fábricas embutidas dão o mesmo que os vetores fixos em todos os casos, com o domínio, as políticas e os parâmetros
/// de cada caso: valor, desfecho e código de erro. É a prova de que o motor, pelas fábricas, sai igual aos vetores (e à
/// fachada, que os vetores também conferem). As mensagens citam arquivo, caso e par, nunca valores.
/// </summary>
public sealed class FabricasContraOsVetoresTestes
{
    private static readonly string[] _transformadores = ["br.cpf", "br.cnpj", "br.telefone", "email", "br.cep", "texto.substituir"];

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
        // se a identidade das instâncias separar as políticas.
        var sessoes = new Dictionary<string, SessaoTransformacao>(StringComparer.Ordinal);
        try
        {
            foreach (var caso in raiz.GetProperty("casos").EnumerateArray())
            {
                var ambiente = caso.GetProperty("ambiente").GetString()!;
                if (!sessoes.TryGetValue(ambiente, out var sessao))
                {
                    sessao = new SessaoTransformacao(chave, ambiente);
                    sessoes.Add(ambiente, sessao);
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

        Assert.True(conferidos >= 20, $"{arquivo}: poucos pares conferidos.");
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

        return new RegraTransformacao(new DominioPseudonimo(caso.GetProperty("dominio").GetString()!, 1))
        {
            Parametros = parametros,
            EntradaInvalida = caso.TryGetProperty("entradaInvalida", out var invalida) ? Invalida(invalida.GetString()) : PoliticaEntradaInvalida.Preservar,
            EntradaMalformada = caso.TryGetProperty("entradaMalformada", out var malformada) ? Malformada(malformada.GetString()) : PoliticaEntradaMalformada.Falhar,
            TemDerivarDe = transformador == "br.cep",
        };
    }

    private static ResultadoDoPar Executar(Transformador transformador, EntradaDoPar entrada)
    {
        try
        {
            var resultado = transformador.Transformar(
                entrada.EhNumero ? entrada.Numero : entrada.Texto, new ContextoTransformacao(entrada.Entidade));
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
