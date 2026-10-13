using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Gera um arquivo de vetores a partir da receita: para cada par, procura a semente cuja entrada tem a forma pedida e
/// cai no ramo esperado, e grava a saída calculada pelo C#. Num arquivo existente, os pares são reconferidos e só os
/// casos novos se acrescentam: os casos antigos são regravados com o mesmo conteúdo. A referência em Python confere o
/// resultado antes do commit. Nome de método, regra ou forma desconhecido é erro de programação e interrompe.
/// </summary>
internal static class GeradorDeVetores
{
    private const int TentativasPorPar = 200_000;

    /// <summary>Gera ou completa o arquivo; devolve quantos casos foram acrescentados.</summary>
    internal static int Gerar(ReceitaDeArquivo receita, string pasta, ChaveIncognito chave, string textoDaChave)
    {
        var caminho = Path.Combine(pasta, receita.Arquivo);
        var existentes = new List<JsonElement>();
        JsonDocument? documento = null;
        try
        {
            if (File.Exists(caminho))
            {
                documento = EsquemaDeVetores.Ler(caminho);
                Reconferir(documento.RootElement, receita, chave, textoDaChave);
                existentes.AddRange(documento.RootElement.GetProperty("casos").EnumerateArray());
            }

            var nomes = existentes.Select(caso => caso.GetProperty("nome").GetString()).ToHashSet(StringComparer.Ordinal);
            var novos = receita.Casos.Where(caso => !nomes.Contains(caso.Nome)).ToList();
            if (novos.Count == 0)
            {
                return 0;
            }

            var conteudo = Escrever(receita, textoDaChave, existentes, novos, chave);
            var temporario = caminho + ".novo";
            try
            {
                using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write))
                {
                    arquivo.Write(conteudo);
                }

                File.Move(temporario, caminho, overwrite: documento is not null);
            }
            finally
            {
                File.Delete(temporario);
            }

            return novos.Count;
        }
        finally
        {
            documento?.Dispose();
        }
    }

    private static void Reconferir(JsonElement raiz, ReceitaDeArquivo receita, ChaveIncognito chave, string textoDaChave)
    {
        if (raiz.GetProperty("transformador").GetString() != receita.Transformador
            || raiz.GetProperty("versao").GetInt32() != receita.Versao
            || raiz.GetProperty("chave").GetString() != textoDaChave)
        {
            throw new InvalidDataException($"{receita.Arquivo}: cabeçalho diferente da receita.");
        }

        foreach (var caso in raiz.GetProperty("casos").EnumerateArray())
        {
            using var executor = ExecutorDeCaso.Criar(receita.Transformador, receita.Versao, chave, caso);
            var indice = 0;
            foreach (var par in caso.GetProperty("pares").EnumerateArray())
            {
                if (!executor.Executar(EntradaDoPar.Ler(par)).Confere(par))
                {
                    throw new InvalidDataException($"{receita.Arquivo}, caso '{caso.GetProperty("nome").GetString()}', par {indice}: a saída mudou.");
                }

                indice++;
            }
        }
    }

    private static byte[] Escrever(ReceitaDeArquivo receita, string textoDaChave, List<JsonElement> existentes, List<ReceitaDeCaso> novos, ChaveIncognito chave)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            escritor.WriteStartObject();
            escritor.WriteString("descricao", receita.Descricao);
            escritor.WriteString("transformador", receita.Transformador);
            escritor.WriteNumber("versao", receita.Versao);
            escritor.WriteString("chave", textoDaChave);
            escritor.WriteStartArray("casos");
            foreach (var existente in existentes)
            {
                existente.WriteTo(escritor);
            }

            // A semente continua de onde o arquivo parou, para nenhuma se repetir entre casos.
            var semente = 1 + existentes.SelectMany(c => c.GetProperty("pares").EnumerateArray())
                .Select(p => p.GetProperty("origem").TryGetProperty("semente", out var s) ? s.GetInt64() : 0)
                .DefaultIfEmpty(0)
                .Max();
            foreach (var caso in novos)
            {
                semente = EscreverCaso(escritor, receita, caso, chave, semente);
            }

            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }

        memoria.WriteByte((byte)'\n');
        return memoria.ToArray();
    }

    private static long EscreverCaso(Utf8JsonWriter escritor, ReceitaDeArquivo receita, ReceitaDeCaso caso, ChaveIncognito chave, long semente)
    {
        using var parametros = JsonDocument.Parse(ParametrosEmJson(caso));
        var elemento = parametros.RootElement;
        using var executor = ExecutorDeCaso.Criar(receita.Transformador, receita.Versao, chave, elemento);
        var contexto = ContextoDeOrigem.Do(chave, receita.Versao, elemento);

        escritor.WriteStartObject();
        escritor.WriteString("nome", caso.Nome);
        foreach (var propriedade in elemento.EnumerateObject())
        {
            propriedade.WriteTo(escritor);
        }

        escritor.WriteStartArray("pares");
        foreach (var receitaDoPar in caso.Pares)
        {
            for (var vez = 0; vez < receitaDoPar.Vezes; vez++)
            {
                semente = EscreverPar(escritor, receita.Transformador, receitaDoPar, executor, contexto, semente, $"{receita.Arquivo}, caso '{caso.Nome}', ramo {receitaDoPar.Ramo}");
            }
        }

        escritor.WriteEndArray();
        escritor.WriteEndObject();
        return semente;
    }

    private static long EscreverPar(
        Utf8JsonWriter escritor, string transformador, ReceitaDePar receita, ExecutorDeCaso executor, ContextoDeOrigem contexto, long semente, string onde)
    {
        var buscaPorSemente = receita.Tipo != "literal" || receita.Entidade?.Contains("{n}", StringComparison.Ordinal) == true;
        for (var tentativa = 0; tentativa < (buscaPorSemente ? TentativasPorPar : 1); tentativa++, semente++)
        {
            object entrada;
            using var origem = JsonDocument.Parse(OrigemEmJson(receita, semente));
            try
            {
                entrada = receita.Tipo == "literal" ? receita.Literal! : OrigensDeVetores.Construir(origem.RootElement, contexto);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            var entidade = receita.Entidade?.Replace("{n}", semente.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (entidade?.StartsWith("utf16:", StringComparison.Ordinal) == true)
            {
                entidade = EntradaDoPar.DeUtf16(entidade["utf16:".Length..]);
            }

            if ((transformador == "br.cep") != (entidade is not null))
            {
                throw new InvalidDataException($"{onde}: entidade só no CEP, e sempre no CEP.");
            }

            var entradaDoPar = entrada is long numero ? EntradaDoPar.DeNumero(numero, entidade) : EntradaDoPar.DeTexto((string)entrada, entidade);
            var resultado = executor.Executar(entradaDoPar);
            if ((receita.Esperado is not null && receita.Esperado != (resultado.Erro ?? resultado.Desfecho))
                || (receita.Condicao is not null && !receita.Condicao(entrada, resultado)))
            {
                continue;
            }

            // Nenhuma saída da chave de DF-e traz emitente de raiz baixa com DV certo (as entradas já são conferidas na origem).
            if (transformador == "br.chave-dfe" && OrigensChaveDfe.ChaveDaForma(resultado.Saida as string) is { } chaveDaSaida
                && OrigensChaveDfe.RaizBaixaComDvCerto(chaveDaSaida.AsSpan(6, 14)))
            {
                continue;
            }

            if (resultado.Saida is string saida && EntradaDoPar.TemUtf16Invalido(saida))
            {
                throw new InvalidDataException($"{onde}: saída com UTF-16 inválido não cabe no vetor.");
            }

            escritor.WriteStartObject();
            if (entrada is long inteiro)
            {
                escritor.WriteNumber("entrada", inteiro);
            }
            else if (EntradaDoPar.TemUtf16Invalido((string)entrada))
            {
                escritor.WriteString("entradaUtf16", EntradaDoPar.ParaUtf16((string)entrada));
            }
            else
            {
                escritor.WriteString("entrada", (string)entrada);
            }

            if (entidade is not null)
            {
                if (EntradaDoPar.TemUtf16Invalido(entidade))
                {
                    escritor.WriteString("entidadeUtf16", EntradaDoPar.ParaUtf16(entidade));
                }
                else
                {
                    escritor.WriteString("entidade", entidade);
                }
            }

            if (resultado.Erro is not null)
            {
                escritor.WriteString("erro", resultado.Erro);
            }
            else
            {
                switch (resultado.Saida)
                {
                    case null:
                        escritor.WriteNull("saida");
                        break;
                    case string texto:
                        escritor.WriteString("saida", texto);
                        break;
                    case long valor:
                        escritor.WriteNumber("saida", valor);
                        break;
                }

                escritor.WriteString("desfecho", resultado.Desfecho);
                if (resultado.Ocorrencias is { } ocorrencias)
                {
                    escritor.WriteStartArray("ocorrencias");
                    foreach (var ocorrencia in ocorrencias)
                    {
                        escritor.WriteStringValue(ocorrencia);
                    }

                    escritor.WriteEndArray();
                }
            }

            escritor.WriteString("ramo", receita.Ramo);
            escritor.WritePropertyName("origem");
            origem.RootElement.WriteTo(escritor);
            escritor.WriteEndObject();
            return semente + 1;
        }

        throw new InvalidDataException($"{onde}: nenhuma entrada caiu no ramo esperado.");
    }

    private static string OrigemEmJson(ReceitaDePar receita, long semente)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            escritor.WriteStartObject();
            escritor.WriteString("tipo", receita.Tipo);
            if (receita.Tipo != "literal")
            {
                escritor.WriteString(receita.Tipo == "gerador-br" ? "metodo" : "regra", receita.MetodoOuRegra);
                escritor.WriteNumber("semente", semente);
                escritor.WriteStartArray("formas");
                foreach (var forma in receita.Formas)
                {
                    escritor.WriteStringValue(forma);
                }

                escritor.WriteEndArray();
            }

            escritor.WriteEndObject();
        }

        return Encoding.UTF8.GetString(memoria.ToArray());
    }

    /// <summary>Os parâmetros do caso em JSON, como o gerador grava.</summary>
    internal static string ParametrosEmJson(ReceitaDeCaso caso)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            EscreverObjeto(escritor, caso.Parametros);
        }

        return Encoding.UTF8.GetString(memoria.ToArray());
    }

    private static void EscreverObjeto(Utf8JsonWriter escritor, IEnumerable<KeyValuePair<string, object>> propriedades)
    {
        escritor.WriteStartObject();
        foreach (var (nome, valor) in propriedades)
        {
            switch (valor)
            {
                case string texto:
                    escritor.WriteString(nome, texto);
                    break;
                case bool logico:
                    escritor.WriteBoolean(nome, logico);
                    break;
                case long inteiro:
                    escritor.WriteNumber(nome, inteiro);
                    break;
                case long[] inteiros:
                    escritor.WriteStartArray(nome);
                    foreach (var item in inteiros)
                    {
                        escritor.WriteNumberValue(item);
                    }

                    escritor.WriteEndArray();
                    break;
                case KeyValuePair<string, object>[] objeto:
                    escritor.WritePropertyName(nome);
                    EscreverObjeto(escritor, objeto);
                    break;
                default:
                    throw new InvalidDataException($"Parâmetro '{nome}' de tipo sem escrita.");
            }
        }

        escritor.WriteEndObject();
    }
}
