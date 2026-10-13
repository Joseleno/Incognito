using System.Text.Json;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Testes.Vetores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Vetores fixos dos transformadores (<c>testes/vetores/</c>): contrato de compatibilidade. Um par que mudar quebra o
/// teste e obriga a criar outra versão do transformador. O arquivo fica preso à receita: os casos na mesma ordem, a
/// quantidade de pares, o ramo, a origem, o literal e o resultado que cada ramo exige. Tirar, trocar ou acrescentar à mão
/// um caso ou um par quebra. A referência em Python confere os mesmos arquivos de forma independente. As mensagens citam
/// arquivo, caso, par e desfecho, nunca valores.
/// </summary>
public sealed class VetoresFixosTestes
{
    // Vetores que não vêm das receitas (derivação, Feistel e volume), conferidos nos próprios testes.
    private static readonly string[] _outrosVetores = ["derivacao@1.json", "feistel@1.json", VolumeDeVetores.Arquivo];

    private static readonly string[] _componentes = ["cpf", "cnpj", "email", "telefone"];

    private static readonly string[] _metodosDaFachada = ["Cpf", "Cnpj", "Telefone", "Email", "ChavePix", "ChaveDfe"];

    public static TheoryData<string> Arquivos => [.. ReceitasDeVetores.Todas.Select(r => r.Arquivo)];

    public static TheoryData<string, string> Casos
    {
        get
        {
            var casos = new TheoryData<string, string>();
            foreach (var receita in ReceitasDeVetores.Todas)
            {
                foreach (var caso in receita.Casos)
                {
                    casos.Add(receita.Arquivo, caso.Nome);
                }
            }

            return casos;
        }
    }

    [Fact]
    public void A_pasta_de_vetores_tem_exatamente_os_arquivos_esperados()
    {
        var esperados = ReceitasDeVetores.Todas.Select(r => r.Arquivo).Concat(_outrosVetores).Order(StringComparer.Ordinal).ToList();
        var presentes = Directory.EnumerateFileSystemEntries(EsquemaDeVetores.PastaNaSaida)
            .Select(caminho => Path.GetFileName(caminho)!)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Um vetor renomeado ou apagado deixa a cópia antiga na pasta de saída: limpe-a (dotnet clean) se for o caso.
        Assert.Equal(esperados, presentes);
    }

    [Fact]
    public void Todo_transformador_com_conferidor_tem_arquivo()
    {
        var comArquivo = ReceitasDeVetores.Todas.Select(r => $"{r.Transformador}@{r.Versao}").ToHashSet(StringComparer.Ordinal);

        Assert.All(EsquemaDeVetores.Transformadores, transformador => Assert.Contains(transformador, comArquivo));
    }

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void Os_casos_do_arquivo_sao_os_da_receita_na_mesma_ordem(string arquivo)
    {
        var receita = ReceitasDeVetores.Todas.Single(r => r.Arquivo == arquivo);
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);

        Assert.Equal(receita.Transformador, documento.RootElement.GetProperty("transformador").GetString());
        Assert.Equal(receita.Versao, documento.RootElement.GetProperty("versao").GetInt32());
        Assert.Equal(receita.Descricao, documento.RootElement.GetProperty("descricao").GetString());
        Assert.Equal(
            receita.Casos.Select(c => c.Nome),
            documento.RootElement.GetProperty("casos").EnumerateArray().Select(c => c.GetProperty("nome").GetString()!));

        // A chave é a pública de teste, e cada caso tem exatamente os parâmetros da receita.
        using var derivacao = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        Assert.True(
            documento.RootElement.GetProperty("chave").GetString() == ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(derivacao, "chaves", "aleatoria"), "texto"),
            $"{arquivo}: chave diferente da chave pública de teste.");
        foreach (var (receitaDoCaso, caso) in receita.Casos.Zip(documento.RootElement.GetProperty("casos").EnumerateArray()))
        {
            using var esperados = JsonDocument.Parse(GeradorDeVetores.ParametrosEmJson(receitaDoCaso));
            var gravados = caso.EnumerateObject().Where(p => p.Name is not ("nome" or "pares")).ToList();
            var iguais = gravados.Count == esperados.RootElement.EnumerateObject().Count()
                && gravados.All(p => esperados.RootElement.TryGetProperty(p.Name, out var valor) && JsonElement.DeepEquals(valor, p.Value));
            Assert.True(iguais, $"{arquivo}, caso '{receitaDoCaso.Nome}': parâmetros diferentes dos da receita.");
        }
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void Vetor_fixo_confere(string arquivo, string nomeDoCaso)
    {
        var receita = ReceitasDeVetores.Todas.Single(r => r.Arquivo == arquivo);
        var receitaDoCaso = receita.Casos.Single(c => c.Nome == nomeDoCaso);
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);
        var raiz = documento.RootElement;
        var caso = raiz.GetProperty("casos").EnumerateArray().SingleOrDefault(c => c.GetProperty("nome").GetString() == nomeDoCaso);
        Assert.True(caso.ValueKind == JsonValueKind.Object, $"{arquivo}: falta o caso '{nomeDoCaso}'.");

        var receitasDosPares = receitaDoCaso.Pares.SelectMany(p => Enumerable.Repeat(p, p.Vezes)).ToList();
        var pares = caso.GetProperty("pares").EnumerateArray().ToList();
        Assert.True(pares.Count == receitasDosPares.Count, $"{arquivo}, caso '{nomeDoCaso}': {pares.Count} pares, a receita pede {receitasDosPares.Count}.");

        using var chave = ChaveIncognito.Ler(raiz.GetProperty("chave").GetString());
        using var executor = ExecutorDeCaso.Criar(receita.Transformador, receita.Versao, chave, caso);
        for (var i = 0; i < pares.Count; i++)
        {
            var onde = $"{arquivo}, caso '{nomeDoCaso}', par {i}";
            var par = pares[i];
            var receitaDoPar = receitasDosPares[i];
            ConferirContraAReceita(par, receitaDoPar, onde);

            var entrada = EntradaDoPar.Ler(par);
            var resultado = executor.Executar(entrada);
            var obtido = resultado.Erro ?? resultado.Desfecho;
            var esperado = par.TryGetProperty("erro", out var erro) ? erro.GetString() : par.GetProperty("desfecho").GetString();
            var divergencia = resultado.Divergencia(par);
            Assert.True(divergencia is null, $"{onde}: o campo '{divergencia}' diverge do vetor (obtido {obtido}, esperado {esperado}).");
            Assert.True(
                receitaDoPar.Condicao is null || receitaDoPar.Condicao(entrada.EhNumero ? entrada.Numero : entrada.Texto!, resultado),
                $"{onde}: o par não cumpre a condição do ramo.");
        }
    }

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void As_sementes_crescem_em_todo_o_arquivo(string arquivo)
    {
        // O gerador anda a semente par a par e continua de onde o caso anterior parou: dois pares trocados de lugar, ou
        // uma semente trocada à mão, quebram a ordem mesmo quando cada entrada ainda se refaz e cada saída confere.
        using var documento = EsquemaDeVetores.LerDaSaida(arquivo);
        var anterior = 0L;
        var indice = 0;
        foreach (var par in documento.RootElement.GetProperty("casos").EnumerateArray().SelectMany(c => c.GetProperty("pares").EnumerateArray()))
        {
            if (par.GetProperty("origem").TryGetProperty("semente", out var semente))
            {
                Assert.True(semente.GetInt64() > anterior, $"{arquivo}, par {indice}: semente fora da ordem.");
                anterior = semente.GetInt64();
            }

            indice++;
        }

        Assert.True(anterior > 0, $"{arquivo}: nenhum par com semente.");
    }

    [Fact]
    public void Entradas_com_surrogate_sem_par_chegam_inteiras_ao_teste()
    {
        // O JSON não carrega surrogate sem par como texto; o par guarda as unidades UTF-16 e a leitura as refaz.
        using var documento = EsquemaDeVetores.LerDaSaida("email@1.json");
        var entradas = documento.RootElement.GetProperty("casos").EnumerateArray()
            .SelectMany(c => c.GetProperty("pares").EnumerateArray())
            .Where(p => p.TryGetProperty("entradaUtf16", out _))
            .Select(p => EntradaDoPar.Ler(p).Texto!)
            .ToList();

        Assert.NotEmpty(entradas);
        for (var i = 0; i < entradas.Count; i++)
        {
            Assert.True(EntradaDoPar.TemUtf16Invalido(entradas[i]), $"Entrada em UTF-16 {i} sem surrogate sem par.");
        }
    }

    [Fact]
    public void A_fachada_da_o_mesmo_que_os_vetores_com_os_dominios_padrao()
    {
        var conferidos = new Dictionary<string, int>(StringComparer.Ordinal);
        var ambientes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var receita in ReceitasDeVetores.Todas)
        {
            using var documento = EsquemaDeVetores.LerDaSaida(receita.Arquivo);
            using var chave = ChaveIncognito.Ler(documento.RootElement.GetProperty("chave").GetString());
            foreach (var caso in documento.RootElement.GetProperty("casos").EnumerateArray())
            {
                // A fachada é a versão 1 de cada transformador.
                var metodo = receita.Versao == 1 ? MetodoDaFachada(receita.Transformador, caso) : null;
                if (metodo is null)
                {
                    continue;
                }

                var ambiente = caso.GetProperty("ambiente").GetString()!;
                using var fachada = new Pseudonimizador(chave, ambiente);
                var indice = 0;
                foreach (var par in caso.GetProperty("pares").EnumerateArray())
                {
                    var onde = $"{receita.Arquivo}, caso '{caso.GetProperty("nome").GetString()}', par {indice++}";
                    var entrada = EntradaDoPar.Ler(par);
                    if (entrada.EhNumero)
                    {
                        continue;
                    }

                    ConferirNaFachada(fachada, metodo, entrada.Texto!, par, onde);
                    conferidos[metodo] = conferidos.GetValueOrDefault(metodo) + 1;
                    ambientes.TryAdd(metodo, new HashSet<string>(StringComparer.Ordinal));
                    ambientes[metodo].Add(ambiente);
                }
            }
        }

        foreach (var metodo in _metodosDaFachada)
        {
            Assert.True(conferidos.GetValueOrDefault(metodo) >= 20, $"Poucos pares conferidos na fachada para {metodo}.");
            Assert.True(ambientes.GetValueOrDefault(metodo)?.Count >= 2, $"A fachada de {metodo} foi conferida em menos de dois ambientes.");
        }
    }

    // O par corresponde à receita: mesmo ramo, mesma origem (sem a semente, que o gerador procura), mesmo literal e o
    // resultado que o ramo exige; entidade fixa igual à da receita.
    private static void ConferirContraAReceita(JsonElement par, ReceitaDePar receita, string onde)
    {
        Assert.True(par.GetProperty("ramo").GetString() == receita.Ramo, $"{onde}: ramo fora da ordem da receita.");
        var origem = par.GetProperty("origem");
        Assert.True(origem.GetProperty("tipo").GetString() == receita.Tipo, $"{onde}: tipo de origem diferente da receita.");
        if (receita.Tipo == "literal")
        {
            var entrada = EntradaDoPar.Ler(par);
            var igual = receita.Literal switch
            {
                long numero => entrada.EhNumero && entrada.Numero == numero,
                string texto => !entrada.EhNumero && string.Equals(entrada.Texto, texto, StringComparison.Ordinal),
                _ => false,
            };
            Assert.True(igual, $"{onde}: literal diferente do da receita.");
        }
        else
        {
            var nome = origem.GetProperty(receita.Tipo == "gerador-br" ? "metodo" : "regra").GetString();
            var formas = origem.GetProperty("formas").EnumerateArray().Select(f => f.GetString()!).ToArray();
            Assert.True(nome == receita.MetodoOuRegra && formas.SequenceEqual(receita.Formas, StringComparer.Ordinal), $"{onde}: origem diferente da receita.");
        }

        if (receita.Esperado is not null)
        {
            var esperado = par.TryGetProperty("erro", out var erro) ? erro.GetString() : par.GetProperty("desfecho").GetString();
            Assert.True(esperado == receita.Esperado, $"{onde}: desfecho {esperado}, o ramo exige {receita.Esperado}.");
        }

        if (receita.Entidade is { } entidade)
        {
            var gravada = EntradaDoPar.Ler(par).Entidade;
            var confere = entidade.Contains("{n}", StringComparison.Ordinal)
                ? gravada is not null && Regex.IsMatch(gravada, "^" + Regex.Escape(entidade).Replace(@"\{n}", @"\d+", StringComparison.Ordinal) + "$")
                : string.Equals(gravada, entidade.StartsWith("utf16:", StringComparison.Ordinal) ? EntradaDoPar.DeUtf16(entidade["utf16:".Length..]) : entidade, StringComparison.Ordinal);
            Assert.True(confere, $"{onde}: entidade diferente da receita.");
        }
    }

    private static void ConferirNaFachada(Pseudonimizador fachada, string metodo, string entrada, JsonElement par, string onde)
    {
        Func<string, string> chamada = metodo switch
        {
            "Cpf" => fachada.Cpf,
            "Cnpj" => fachada.Cnpj,
            "Telefone" => fachada.Telefone,
            "Email" => fachada.Email,
            "ChavePix" => fachada.ChavePix,
            "ChaveDfe" => fachada.ChaveDfe,
            _ => throw new InvalidOperationException("Método da fachada desconhecido."),
        };
        if (par.TryGetProperty("erro", out var erro))
        {
            var excecao = Record.Exception(() => chamada(entrada));
            Assert.True(excecao is IncognitoException incognito && incognito.Codigo == erro.GetString(), $"{onde}: a fachada não deu o mesmo erro.");
            return;
        }

        Assert.True(string.Equals(chamada(entrada), par.GetProperty("saida").GetString(), StringComparison.Ordinal), $"{onde}: a fachada diverge do vetor.");
    }

    // Os casos com os parâmetros fixos da fachada: domínios padrão, versão 1, DV preservado, malformado falha, CNPJ com a
    // ordem preservada.
    private static string? MetodoDaFachada(string transformador, JsonElement caso)
    {
        var dominio = caso.GetProperty("dominio").GetString();
        var malformadaFalha = caso.GetProperty("entradaMalformada").GetString() == "falhar";
        var invalidaPreserva = !caso.TryGetProperty("entradaInvalida", out var invalida) || invalida.GetString() == "preservar";
        var semOrdem = !caso.TryGetProperty("permutarOrdem", out var ordem) || !ordem.GetBoolean();
        if (!malformadaFalha || !invalidaPreserva || !semOrdem)
        {
            return null;
        }

        return (transformador, dominio) switch
        {
            ("br.cpf", "cpf") => "Cpf",
            ("br.cnpj", "cnpj") => "Cnpj",
            ("br.telefone", "telefone") => "Telefone",
            ("email", "email") => "Email",
            ("br.chave-pix", "chave-pix") when ComponentesPadrao(caso.GetProperty("componentes")) => "ChavePix",
            ("br.chave-dfe", "dfe-numero") when ComponentesPadraoDaChaveDfe(caso) => "ChaveDfe",
            _ => null,
        };
    }

    // A fachada aceita os nove modelos, com o CPF e o CNPJ dos domínios padrão e a ordem preservada.
    private static bool ComponentesPadraoDaChaveDfe(JsonElement caso)
    {
        var componentes = caso.GetProperty("componentes");
        return caso.GetProperty("modelos").GetArrayLength() == 9
            && componentes.GetProperty("cpf").GetProperty("dominio").GetString() == "cpf"
            && componentes.GetProperty("cpf").GetProperty("versao").GetInt32() == 1
            && componentes.GetProperty("cnpj").GetProperty("dominio").GetString() == "cnpj"
            && componentes.GetProperty("cnpj").GetProperty("versao").GetInt32() == 1
            && !componentes.GetProperty("cnpj").GetProperty("permutarOrdem").GetBoolean();
    }

    private static bool ComponentesPadrao(JsonElement componentes) =>
        componentes.GetProperty("cpf").GetProperty("dominio").GetString() == "cpf"
        && componentes.GetProperty("cpf").GetProperty("entradaInvalida").GetString() == "preservar"
        && componentes.GetProperty("cnpj").GetProperty("dominio").GetString() == "cnpj"
        && !componentes.GetProperty("cnpj").GetProperty("permutarOrdem").GetBoolean()
        && componentes.GetProperty("cnpj").GetProperty("entradaInvalida").GetString() == "preservar"
        && componentes.GetProperty("email").GetProperty("dominio").GetString() == "email"
        && componentes.GetProperty("telefone").GetProperty("dominio").GetString() == "telefone"
        && _componentes.All(nome => componentes.GetProperty(nome).GetProperty("versao").GetInt32() == 1);
}
