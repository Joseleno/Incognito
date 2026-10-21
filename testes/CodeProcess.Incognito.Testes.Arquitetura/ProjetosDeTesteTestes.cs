using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Nenhum teste some do CI sem que ele falhe: zero testes executados é erro (código 8), exceto nos projetos listados aqui;
/// nada no repositório afrouxa o código de saída nem corta testes por fora da linha de comando; as categorias que o CI
/// filtra ficam só nos projetos em que algum job as roda; e todo projeto está na solução que o CI testa.
/// </summary>
public sealed class ProjetosDeTesteTestes
{
    private const string Este = "testes/CodeProcess.Incognito.Testes.Arquitetura/ProjetosDeTesteTestes.cs";
    private const string Nucleo = "CodeProcess.Incognito.Testes";
    private const string Conformidade = "CodeProcess.Incognito.Testes.Conformidade";

    // Roda a suíte de conformidade por reflexão contra um dialeto falso, sem banco e sem classe concreta descoberta pelo
    // xUnit: a única referência à suíte fora dos dialetos.
    private const string Motor = "CodeProcess.Incognito.Testes.Motor";

    // Confere por reflexão que a suíte só tem classes de teste abstratas; não roda nenhum teste dela.
    private const string Arquitetura = "CodeProcess.Incognito.Testes.Arquitetura";

    // A única forma permitida de ignorar o código 8, igual nos quatro .csproj.
    private const string LinhaPermitida =
        "<TestingPlatformCommandLineArguments>$(TestingPlatformCommandLineArguments) --ignore-exit-code 8</TestingPlatformCommandLineArguments>";

    // Os de dialeto só têm testes de banco, que o CI sem Docker filtra: lá rodam zero testes, para sempre.
    private static readonly string[] _dialetos = ["CodeProcess.Incognito.Testes.PostgreSql", "CodeProcess.Incognito.Testes.SqlServer"];

    // Os vazios só enquanto não tiverem nenhum .cs versionado: com o primeiro teste, a linha sai do .csproj.
    private static readonly string[] _aindaVazios = ["CodeProcess.Incognito.Testes.Cli"];

    // O que mexe no código de saída, na linha de comando ou na configuração dos testes por fora do comando do CI: as
    // propriedades e itens do MSBuild que chegam ao executável de teste e as variáveis de ambiente do MTP e do xUnit. Os
    // arquivos de perfil e de configuração da execução também mexem, e são recusados à parte.
    private static readonly Regex _ajusteDaExecucao = new(
        "ignore-exit-code|EXITCODE_IGNORE|TESTINGPLATFORM_|TestingPlatformCommandLine|commandLineOptionDefaults|RunArguments|RunCommand|xUnit__",
        RegexOptions.IgnoreCase);

    // As categorias que o CI filtra, pela palavra e sem caixa (o filtro do xUnit também não diferencia): pega o literal, a
    // constante, o nameof e um atributo próprio. Integracao: o rapido a tira (sem Docker), e só os dialetos e a conformidade
    // a usam. Lento: o rapido e o nucleo-so a tiram, e o job lentos a roda só no núcleo (o release roda as duas, em tudo).
    private static readonly Regex _integracao = new(@"\bintegracao\b", RegexOptions.IgnoreCase);
    private static readonly Regex _lento = new(@"\blento\b", RegexOptions.IgnoreCase);

    // O atributo que marca um tipo público de dialeto como parte do contrato experimental, aceito só no código dos
    // dialetos: dentro do tipo marcado, o uso do contrato não avisa, então em outro lugar ele valeria como supressão.
    private const string AtributoExperimental = "[Experimental(\"INCX001\")]";
    private const string ContratoDoMotor = "src/CodeProcess.Incognito.Motor/Dialetos/";
    private const string InternoDoMotor = "src/CodeProcess.Incognito.Motor/Interno/";
    private static readonly string[] _codigoDosDialetos = ["src/CodeProcess.Incognito.PostgreSql/", "src/CodeProcess.Incognito.SqlServer/"];

    // Onde se suprime um diagnóstico do compilador.
    private static readonly string[] _comConfiguracaoDeCompilacao =
        ["*.cs", "*.csproj", "*.props", "*.targets", "*.editorconfig", "*.globalconfig", "*.json", "*.yml", "*.yaml", "*.rsp", "*.ruleset"];


    [Fact]
    public void A_varredura_ve_os_projetos_de_teste()
    {
        var projetos = Repositorio.Versionados("testes/*.csproj");

        foreach (var projeto in _dialetos.Concat(_aindaVazios).Append(Nucleo).Append(Conformidade))
        {
            Assert.Contains($"testes/{projeto}/{projeto}.csproj", projetos);
        }

        Assert.Contains(Este, Repositorio.Versionados("testes/*.cs"));
    }

    [Fact]
    public void So_os_projetos_permitidos_ignoram_zero_testes_e_ninguem_mais_mexe_na_execucao()
    {
        var raiz = Repositorio.Raiz();
        var permitidos = _dialetos.Concat(_aindaVazios).Select(projeto => $"testes/{projeto}/{projeto}.csproj").ToHashSet();
        var achados = new List<string>();
        var ignoram = new HashSet<string>();

        // Todo arquivo de texto, inclusive a documentação: o MSBuild importa um arquivo de qualquer extensão.
        foreach (var arquivo in Repositorio.Versionados("*").Where(arquivo => arquivo != Este && !Binario(arquivo)))
        {
            var linhas = File.ReadAllLines(Path.Combine(raiz, arquivo));
            for (var indice = 0; indice < linhas.Length; indice++)
            {
                if (!_ajusteDaExecucao.IsMatch(linhas[indice]))
                {
                    continue;
                }

                if (permitidos.Contains(arquivo) && linhas[indice].Trim() == LinhaPermitida)
                {
                    ignoram.Add(arquivo);
                }
                else
                {
                    achados.Add($"{arquivo}:{indice + 1}");
                }
            }
        }

        Assert.True(achados.Count == 0, $"Ajuste do código de saída ou da linha de comando dos testes fora da linha permitida: {string.Join(", ", achados)}.");
        Assert.True(ignoram.SetEquals(permitidos), $"Ignoram zero testes: {string.Join(", ", ignoram.Order(StringComparer.Ordinal))}; esperado: {string.Join(", ", permitidos.Order(StringComparer.Ordinal))}.");
    }

    [Fact]
    public void Projeto_ainda_vazio_nao_tem_codigo()
    {
        var comCodigo = _aindaVazios
            .Where(projeto => Repositorio.Versionados($"testes/{projeto}/*.cs").Count > 0)
            .ToArray();

        Assert.True(comCodigo.Length == 0, $"Projeto com código que ainda ignora zero testes (tirar a linha do .csproj e o projeto de {nameof(_aindaVazios)}): {string.Join(", ", comCodigo)}.");
    }

    [Fact]
    public void Nenhum_perfil_nem_configuracao_de_execucao_dos_testes()
    {
        // Os perfis (launchSettings.json, <app>.run.json) trocam variáveis e argumentos; são recusados só nos testes, porque
        // a CLI pode ter os seus. As configurações do MTP e do xUnit podem pular todos os testes de um projeto, onde estiverem.
        var arquivos = Repositorio.Versionados("*")
            .Where(arquivo =>
            {
                var nome = Path.GetFileName(arquivo);
                var perfil = nome.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase) || nome.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase);
                var configuracao = nome.Equals("testconfig.json", StringComparison.OrdinalIgnoreCase)
                    || nome.Equals("xunit.runner.json", StringComparison.OrdinalIgnoreCase)
                    || nome.EndsWith(".runsettings", StringComparison.OrdinalIgnoreCase);
                return configuracao || (perfil && arquivo.StartsWith("testes/", StringComparison.Ordinal));
            })
            .ToArray();

        Assert.True(arquivos.Length == 0, $"Perfil ou configuração de execução dos testes versionado (troca argumentos, variáveis ou testes executados por fora do CI): {string.Join(", ", arquivos)}.");
    }

    [Fact]
    public void Categoria_Integracao_so_nos_dialetos_e_na_conformidade()
    {
        var achados = Ocorrencias(_integracao, projeto => _dialetos.Contains(projeto) || projeto == Conformidade);

        Assert.True(achados.Count == 0, $"\"Integracao\" fora dos projetos de dialeto e da conformidade (o teste sumiria do CI sem Docker): {string.Join(", ", achados)}.");
    }

    [Fact]
    public void Categoria_Lento_so_no_nucleo()
    {
        var achados = Ocorrencias(_lento, projeto => projeto == Nucleo);

        Assert.True(achados.Count == 0, $"\"Lento\" fora do projeto de testes do núcleo (o job lentos só roda o núcleo): {string.Join(", ", achados)}.");
    }

    [Fact]
    public void So_os_dialetos_usam_a_suite_de_conformidade()
    {
        var raiz = Repositorio.Raiz();
        var esperados = _dialetos.Append(Motor).Append(Arquitetura).Select(projeto => $"testes/{projeto}/{projeto}.csproj").ToArray();
        var intrusos = Repositorio.Versionados("*.csproj").Concat(Repositorio.Versionados("*.props")).Concat(Repositorio.Versionados("*.targets"))
            .Where(arquivo => !esperados.Contains(arquivo) && File.ReadAllText(Path.Combine(raiz, arquivo)).Contains(Conformidade, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(intrusos.Length == 0, $"Só os projetos de dialeto (e o do motor, pelo dialeto falso, e o de arquitetura) referenciam a suíte de conformidade, cujos testes são de banco: {string.Join(", ", intrusos)}.");
    }

    [Fact]
    public void Cada_dialeto_implementado_herda_a_suite_numa_unica_classe_selada()
    {
        // Pelo texto: o projeto de testes do dialeto traz o Testcontainers, que este projeto não referencia. Um dialeto
        // conta como implementado quando o assembly dele tem um provedor concreto (também por uma base intermediária); os
        // outros não podem herdar a suíte ainda. Qualquer outra menção à suíte (uma base intermediária, um uso fora de
        // declaração) também conta: a herança precisa ser direta, na única classe, não genérica, declarada na coluna 0 de um
        // arquivo com namespace de arquivo (então nunca aninhada; a compilação condicional e o <Compile> são recusados à
        // parte). O texto não vê comentário nem o que o compilador tirou: o projeto do dialeto confere o mesmo no assembly
        // compilado (ConferenciaDaSuite), numa chamada que esta regra exige.
        var heranca = new Regex(@"^((?:public|internal|sealed|abstract|partial|file)\s+)*class\s+\w+\b[^{;]*?:\s*(?:global::)?(?:\w+\.)*ConformidadeDialetoTestes\s*<", RegexOptions.Multiline);
        var namespaceDeArquivo = new Regex(@"^namespace\s+[\w.]+\s*;", RegexOptions.Multiline);
        var mencao = new Regex(@"\bConformidadeDialetoTestes\s*<");
        var conferencia = new Regex(@"\bConferenciaDaSuite\.Conferir\s*\(");
        var achados = new List<string>();
        foreach (var dialeto in _dialetos)
        {
            var nome = dialeto["CodeProcess.Incognito.Testes.".Length..];
            var implementado = Assembly.Load(new AssemblyName($"CodeProcess.Incognito.{nome}")).GetTypes()
                .Any(tipo => !tipo.IsAbstract && Provedor(tipo));
            var textos = Repositorio.Versionados($"testes/{dialeto}/*.cs")
                .Select(arquivo => (arquivo, texto: File.ReadAllText(Path.Combine(Repositorio.Raiz(), arquivo))))
                .ToArray();
            var classes = textos.SelectMany(par => heranca.Matches(par.texto).Select(casamento => (par.arquivo, casamento))).ToArray();
            var mencoes = textos.Sum(par => mencao.Count(par.texto));
            var conferencias = textos.Sum(par => conferencia.Count(par.texto));

            if (classes.Length != (implementado ? 1 : 0) || mencoes != classes.Length)
            {
                achados.Add($"{dialeto}: {classes.Length} classes derivadas da suíte e {mencoes} menções a ela, esperada {(implementado ? 1 : 0)}");
            }

            if (conferencias != (implementado ? 1 : 0))
            {
                achados.Add($"{dialeto}: {conferencias} chamadas a ConferenciaDaSuite.Conferir, esperada {(implementado ? 1 : 0)}");
            }

            // Os modificadores pelo grupo deles, não pelo casamento inteiro (um parâmetro chamado @public não conta).
            achados.AddRange(classes
                .Where(classe =>
                {
                    var modificadores = classe.casamento.Groups[1].Captures.Select(captura => captura.Value.Trim()).ToArray();
                    return !modificadores.Contains("sealed") || !modificadores.Contains("public");
                })
                .Select(classe => $"{classe.arquivo}: a classe derivada da suíte precisa ser public sealed (o xUnit não descobre as internas)"));
            achados.AddRange(classes.Where(classe => Regex.IsMatch(classe.casamento.Value, @"\bclass\s+\w+\s*<"))
                .Select(classe => $"{classe.arquivo}: a classe derivada da suíte não pode ser genérica (o xUnit não roda classe aberta)"));
            achados.AddRange(classes.Where(classe => !namespaceDeArquivo.IsMatch(textos.Single(par => par.arquivo == classe.arquivo).texto))
                .Select(classe => $"{classe.arquivo}: a classe derivada da suíte precisa estar num arquivo com namespace de arquivo"));
        }

        Assert.True(achados.Count == 0, $"Suíte de conformidade nos dialetos: {string.Join("; ", achados)}.");
    }

    [Fact]
    public void Os_testes_dos_dialetos_nao_tem_compilacao_condicional()
    {
        // As regras que leem o texto (a classe derivada da suíte, a categoria de cada teste) não sabem o que o compilador
        // vê: um #if false esconderia a declaração que elas contam.
        var diretiva = new Regex(@"^\s*#\s*(if|elif|else|define|undef)\b", RegexOptions.Multiline);
        var achados = _dialetos
            .SelectMany(dialeto => Repositorio.Versionados($"testes/{dialeto}/*.cs"))
            .Where(arquivo => diretiva.IsMatch(File.ReadAllText(Path.Combine(Repositorio.Raiz(), arquivo))))
            .ToArray();

        Assert.True(achados.Length == 0, $"Compilação condicional num projeto de testes de dialeto: {string.Join(", ", achados)}.");
    }

    [Fact]
    public void Os_testes_dos_dialetos_compilam_todo_arquivo_versionado()
    {
        // Um <Compile Remove> (ou os itens padrão desligados) tiraria um arquivo do compilador sem tirá-lo do texto que as
        // regras leem. Nos arquivos do MSBuild que chegam aos projetos de testes: os de testes/ e os da raiz.
        var raiz = Repositorio.Raiz();
        var achados = ArquivosDoMsBuild()
            .Where(arquivo => !arquivo.Contains('/', StringComparison.Ordinal) || arquivo.StartsWith("testes/", StringComparison.Ordinal))
            .Where(arquivo => XDocument.Load(Path.Combine(raiz, arquivo)).Descendants()
                .Any(elemento => elemento.Name.LocalName is "Compile" or "EnableDefaultCompileItems" or "EnableDefaultItems"))
            .ToArray();

        Assert.True(achados.Length == 0, $"Item Compile ou itens padrão desligados num arquivo do MSBuild dos testes: {string.Join(", ", achados)}.");
    }

    [Fact]
    public void Os_projetos_de_dialeto_so_tem_testes_de_banco()
    {
        // A fixture de assembly sobe o contêiner quando qualquer teste do projeto roda: um teste sem a categoria rodaria no
        // job sem banco e o derrubaria. Cada [Fact] ou [Theory] leva o trait da suíte no mesmo bloco de atributos (as linhas
        // de atributo seguidas antes do método), não em outro método do arquivo. A linha do atributo de teste só tem
        // atributos: com o método na mesma linha, o bloco seguinte seria de outro método. O nome qualificado, o sufixo
        // Attribute e a lista de atributos também contam; um atributo derivado, só o assembly vê (ConferenciaDaSuite).
        var teste = new Regex(@"[\[,]\s*(?:global::)?(?:\w+\.)*(?:Fact|Theory)(?:Attribute)?\s*[\](,]");
        const string Trait = "[Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]";
        var achados = new List<string>();
        foreach (var arquivo in _dialetos.SelectMany(dialeto => Repositorio.Versionados($"testes/{dialeto}/*.cs")))
        {
            var linhas = File.ReadAllLines(Path.Combine(Repositorio.Raiz(), arquivo));
            for (var indice = 0; indice < linhas.Length; indice++)
            {
                if (!teste.IsMatch(linhas[indice]))
                {
                    continue;
                }

                var bloco = BlocoDeAtributos(linhas, indice);
                var soAtributos = linhas[indice].Trim() is ['[', .., ']'];
                if (!soAtributos || !bloco.Any(linha => linha.Trim() == Trait))
                {
                    achados.Add($"{arquivo}:{indice + 1}");
                }
            }
        }

        Assert.True(achados.Count == 0, $"Testes sem a categoria de banco num projeto de dialeto: {string.Join(", ", achados)}.");
    }

    [Theory]
    [InlineData("Npgsql", "src/CodeProcess.Incognito.PostgreSql/")]
    [InlineData("Microsoft.Data.SqlClient", "src/CodeProcess.Incognito.SqlServer/")]
    [InlineData("Testcontainers", "testes/CodeProcess.Incognito.Testes.PostgreSql/", "testes/CodeProcess.Incognito.Testes.SqlServer/")]
    public void Driver_e_conteiner_so_nos_projetos_do_dialeto(string pacote, params string[] pastas)
    {
        // Pelo PackageReference e pelo GlobalPackageReference de qualquer arquivo do MSBuild (o pacote e os que começam com o
        // nome dele); a versão central (PackageVersion) fica de fora. A referência transitiva é conferida pelos lock files.
        var raiz = Repositorio.Raiz();
        var achados = ArquivosDoMsBuild()
            .Where(arquivo => !pastas.Any(pasta => arquivo.StartsWith(pasta, StringComparison.Ordinal)))
            .Where(arquivo => ReferenciasDePacote(XDocument.Load(Path.Combine(raiz, arquivo)))
                .Any(nome => nome.Equals(pacote, StringComparison.OrdinalIgnoreCase) || nome.StartsWith(pacote + ".", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(achados.Length == 0, $"{pacote} referenciado fora de {string.Join(" e ", pastas)}: {string.Join(", ", achados)}.");
    }

    [Fact]
    public void As_referencias_de_pacote_tem_o_nome_escrito()
    {
        // Um nome montado por propriedade ou item ($(...), @(...), %(...)) escaparia da regra acima.
        var raiz = Repositorio.Raiz();
        var achados = ArquivosDoMsBuild()
            .SelectMany(arquivo => ReferenciasDePacote(XDocument.Load(Path.Combine(raiz, arquivo)))
                .Where(nome => nome.Contains("$(", StringComparison.Ordinal) || nome.Contains("@(", StringComparison.Ordinal) || nome.Contains("%(", StringComparison.Ordinal))
                .Select(nome => $"{arquivo} ({nome})"))
            .ToArray();

        Assert.True(achados.Length == 0, $"Referência de pacote com nome montado por propriedade ou item: {string.Join(", ", achados)}.");
    }

    [Fact]
    public void Todo_projeto_tem_lock_file_versionado()
    {
        // As regras sobre pacotes transitivos leem os lock files: um projeto sem eles escaparia. Um projeto por pasta: o
        // segundo usaria packages.<projeto>.lock.json, que nem o CI nem estas regras leem.
        var locks = Repositorio.Versionados("*packages.lock.json").ToHashSet(StringComparer.Ordinal);
        var projetos = Repositorio.Versionados("*.csproj");
        var semLock = projetos
            .Where(projeto => !locks.Contains(Path.GetDirectoryName(projeto)!.Replace('\\', '/') + "/packages.lock.json"))
            .ToArray();
        var pastasComMaisDeUm = projetos.GroupBy(projeto => Path.GetDirectoryName(projeto)!.Replace('\\', '/')).Where(pasta => pasta.Count() > 1).Select(pasta => pasta.Key).ToArray();
        var outrosLocks = Repositorio.Versionados("*.lock.json").Where(arquivo => Path.GetFileName(arquivo) != "packages.lock.json").ToArray();

        Assert.True(semLock.Length == 0, $"Projeto sem packages.lock.json versionado: {string.Join(", ", semLock)}.");
        Assert.True(pastasComMaisDeUm.Length == 0, $"Pasta com mais de um projeto: {string.Join(", ", pastasComMaisDeUm)}.");
        Assert.True(outrosLocks.Length == 0, $"Lock file com outro nome, que nenhuma regra lê: {string.Join(", ", outrosLocks)}.");
    }

    [Theory]
    [InlineData("Npgsql", "codeprocess.incognito.postgresql", "src/CodeProcess.Incognito.PostgreSql/")]
    [InlineData("Microsoft.Data.SqlClient", "codeprocess.incognito.sqlserver", "src/CodeProcess.Incognito.SqlServer/")]
    [InlineData("Testcontainers", "", "testes/CodeProcess.Incognito.Testes.PostgreSql/", "testes/CodeProcess.Incognito.Testes.SqlServer/")]
    public void Driver_e_conteiner_so_chegam_pelo_projeto_do_dialeto(string pacote, string projetoDoDialeto, params string[] pastas)
    {
        // Pelo grafo do lock file de cada projeto fora das pastas permitidas: o pacote (ou um que começa com o nome dele) só
        // pode ser alcançado através do projeto do dialeto. Um pacote que traz o driver por baixo (como o
        // Microsoft.EntityFrameworkCore.SqlServer traz o Microsoft.Data.SqlClient) é pego aqui.
        var raiz = Repositorio.Raiz();
        var achados = new List<string>();
        foreach (var lockFile in Repositorio.Versionados("*packages.lock.json").Where(arquivo => !pastas.Any(pasta => arquivo.StartsWith(pasta, StringComparison.Ordinal))))
        {
            using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(raiz, lockFile)));
            foreach (var alvo in documento.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                var pacotes = alvo.Value.EnumerateObject().ToDictionary(
                    entrada => entrada.Name,
                    entrada => (
                        Tipo: entrada.Value.TryGetProperty("type", out var tipo) ? tipo.GetString() : null,
                        Dependencias: entrada.Value.TryGetProperty("dependencies", out var dependencias) ? dependencias.EnumerateObject().Select(dependencia => dependencia.Name).ToArray() : []),
                    StringComparer.OrdinalIgnoreCase);
                var raizes = pacotes.Where(par => par.Value.Tipo is "Direct" or "Project" && !par.Key.Equals(projetoDoDialeto, StringComparison.OrdinalIgnoreCase)).Select(par => par.Key);
                foreach (var origem in raizes)
                {
                    var alcancados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var pendentes = new Queue<string>([origem]);
                    while (pendentes.TryDequeue(out var atual))
                    {
                        if (atual.Equals(projetoDoDialeto, StringComparison.OrdinalIgnoreCase) || !alcancados.Add(atual))
                        {
                            continue;
                        }

                        if (!pacotes.TryGetValue(atual, out var entrada))
                        {
                            achados.Add($"{lockFile} ({alvo.Name}): {atual}, dependência de outro pacote, sem entrada no lock");
                            continue;
                        }

                        foreach (var dependencia in entrada.Dependencias)
                        {
                            pendentes.Enqueue(dependencia);
                        }
                    }

                    achados.AddRange(alcancados
                        .Where(nome => nome.Equals(pacote, StringComparison.OrdinalIgnoreCase) || nome.StartsWith(pacote + ".", StringComparison.OrdinalIgnoreCase))
                        .Select(nome => $"{lockFile} ({alvo.Name}): {nome} por {origem}"));
                }
            }
        }

        Assert.True(achados.Count == 0, $"{pacote} alcançado fora do projeto do dialeto: {string.Join(", ", achados.Distinct())}.");
    }

    [Fact]
    public void O_projeto_de_um_dialeto_nao_chega_ao_outro()
    {
        // Uma ProjectReference sem uso de tipo não fica no assembly (o compilador não grava a referência), mas o pacote
        // declara a dependência: pelo lock do projeto de cada dialeto.
        var raiz = Repositorio.Raiz();
        var achados = new List<string>();
        foreach (var pasta in _codigoDosDialetos)
        {
            var lockFile = pasta + "packages.lock.json";
            using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(raiz, lockFile)));
            var outros = _codigoDosDialetos.Where(outra => outra != pasta).Select(outra => outra["src/".Length..^1]).ToArray();
            achados.AddRange(documento.RootElement.GetProperty("dependencies").EnumerateObject()
                .SelectMany(alvo => alvo.Value.EnumerateObject().Select(entrada => (alvo: alvo.Name, entrada.Name)))
                .Where(par => outros.Contains(par.Name, StringComparer.OrdinalIgnoreCase))
                .Select(par => $"{lockFile} ({par.alvo}): {par.Name}"));
        }

        Assert.True(achados.Count == 0, $"Projeto de dialeto que chega ao de outro dialeto: {string.Join(", ", achados)}.");
    }

    [Fact]
    public void A_suite_de_conformidade_e_biblioteca_com_classes_de_teste_abstratas()
    {
        // Uma classe concreta com testes numa biblioteca nunca roda; o xunit.v3 (ou o core) faria dela um executável de
        // teste, que o dotnet test da solução rodaria sem banco.
        var raiz = Repositorio.Raiz();
        var projeto = XDocument.Load(Path.Combine(raiz, $"testes/{Conformidade}/{Conformidade}.csproj"));
        var pacotes = projeto.Descendants("PackageReference").Select(pacote => (string?)pacote.Attribute("Include")).ToArray();
        Assert.Equal("false", projeto.Descendants("IsTestProject").Single().Value);
        Assert.DoesNotContain("xunit.v3", pacotes);
        Assert.DoesNotContain("xunit.v3.core", pacotes);

        var concretas = Assembly.Load(new AssemblyName(Conformidade)).GetTypes()
            .Where(tipo => !tipo.IsAbstract && tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Any(metodo => metodo.GetCustomAttributes(inherit: true).OfType<FactAttribute>().Any()))
            .Select(tipo => tipo.FullName)
            .ToArray();
        Assert.True(concretas.Length == 0, $"Classes concretas com testes na suíte de conformidade: {string.Join(", ", concretas)}.");
    }

    [Fact]
    public void O_INCX001_so_e_suprimido_nos_projetos_que_consomem_o_contrato_de_dialeto()
    {
        // O contrato experimental de dialeto avisa quem o usa; suprimir em lugar mais largo (Directory.Build.props,
        // .editorconfig, pragma fora da pasta interna do motor) esconderia o aviso de quem não sabe que o contrato muda. O
        // próprio contrato o declara e o documenta; o motor o consome só na pasta interna, com o pragma justificado em cada
        // arquivo e nenhum tipo público (decisão 86).
        var raiz = Repositorio.Raiz();

        // Pode suprimir: o .csproj que referencia o motor ou a suíte de conformidade (dialetos, CLI, testes que
        // implementam ou consomem o contrato). O .csproj usa a barra invertida, que o Linux não separa.
        var permitidos = Repositorio.Versionados("*.csproj")
            .Where(projeto => XDocument.Load(Path.Combine(raiz, projeto)).Descendants("ProjectReference")
                .Select(referencia => ((string?)referencia.Attribute("Include") ?? "").Replace('\\', '/').Split('/')[^1])
                .Any(nome => nome is "CodeProcess.Incognito.Motor.csproj" or $"{Conformidade}.csproj"))
            .Append(Este)
            .Append("testes/CodeProcess.Incognito.Testes.Arquitetura/RegrasDeArquiteturaTestes.cs")
            .ToHashSet();
        var achados = _comConfiguracaoDeCompilacao
            .SelectMany(Repositorio.Versionados)
            .Where(arquivo => !permitidos.Contains(arquivo) && !arquivo.StartsWith(ContratoDoMotor, StringComparison.Ordinal)
                && SemOPragmaDoInterno(arquivo, SemOAtributoDosDialetos(arquivo, File.ReadAllText(Path.Combine(raiz, arquivo)))).Contains("INCX001", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // A constante do diagnóstico também marca um tipo como experimental, e dentro dele o contrato não avisa: fora do
        // contrato, ela fugiria da busca pelo texto (também por using static ou por alias).
        var constante = Repositorio.Versionados("src/*.cs")
            .Where(arquivo => !arquivo.StartsWith(ContratoDoMotor, StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(raiz, arquivo)).Contains("ContratoExperimental", StringComparison.Ordinal))
            .ToArray();

        Assert.True(achados.Length == 0, $"INCX001 citado fora dos projetos que consomem o contrato de dialeto: {string.Join(", ", achados)}.");
        Assert.True(constante.Length == 0, $"Constante do INCX001 usada fora do contrato: {string.Join(", ", constante)}.");
    }

    [Fact]
    public void O_INCX001_so_marca_o_contrato_e_os_dialetos()
    {
        // Pelos metadados, que nenhum truque de texto contorna ("INC" + "X001", alias, atributo de assembly): marcar algo
        // como experimental fora do contrato e dos tipos dos dialetos tiraria o aviso de quem o usa ali dentro.
        string[] assemblies = ["CodeProcess.Incognito", "CodeProcess.Incognito.Motor", "CodeProcess.Incognito.PostgreSql", "CodeProcess.Incognito.SqlServer", "CodeProcess.Incognito.EntityFrameworkCore", "CodeProcess.Incognito.Cli"];
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var achados = new List<string>();
        foreach (var assembly in assemblies.Select(nome => Assembly.Load(new AssemblyName(nome))))
        {
            if (Experimental(assembly.GetCustomAttributesData()) || assembly.Modules.Any(modulo => Experimental(modulo.GetCustomAttributesData())))
            {
                achados.Add($"{assembly.GetName().Name} (assembly ou módulo)");
            }

            foreach (var tipo in assembly.GetTypes().Where(tipo => !NoContrato(tipo)))
            {
                var membros = tipo.GetMembers(Todos).Where(membro => Experimental(membro.GetCustomAttributesData())).Select(membro => $"{tipo.FullName}.{membro.Name}");
                achados.AddRange(Experimental(tipo.GetCustomAttributesData()) ? [tipo.FullName!] : membros);
            }
        }

        Assert.True(achados.Count == 0, $"INCX001 marcando algo fora do contrato e dos dialetos: {string.Join(", ", achados)}.");

        static bool Experimental(IEnumerable<CustomAttributeData> atributos) => atributos.Any(atributo =>
            atributo.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute"
            && atributo.ConstructorArguments is [{ Value: "INCX001" }]);

        // O tipo de primeiro nível no namespace do contrato ou de um dialeto (os gerados ficam no tipo que os escreveu).
        static bool NoContrato(Type tipo)
        {
            var externo = tipo;
            while (externo.DeclaringType is { } dono)
            {
                externo = dono;
            }

            return externo.Namespace is "CodeProcess.Incognito.Dialetos" || externo.Namespace?.StartsWith("CodeProcess.Incognito.Dialetos.", StringComparison.Ordinal) == true;
        }
    }

    [Fact]
    public void A_pasta_interna_do_motor_so_tem_tipos_internos_do_namespace_dela()
    {
        // A isenção do INCX001 vale pelo caminho: um arquivo da pasta que declarasse outro namespace, ou um tipo público,
        // levaria o contrato experimental para a superfície do motor sem aviso.
        var raiz = Repositorio.Raiz();
        var arquivos = Repositorio.Versionados(InternoDoMotor + "*.cs").ToArray();
        var foraDoNamespace = arquivos
            .Where(arquivo => !Regex.IsMatch(File.ReadAllText(Path.Combine(raiz, arquivo)), @"^namespace CodeProcess\.Incognito\.Interno;\s*$", RegexOptions.Multiline))
            .ToArray();
        var publicos = Assembly.Load(new AssemblyName("CodeProcess.Incognito.Motor")).GetTypes()
            .Where(tipo => tipo.Namespace == "CodeProcess.Incognito.Interno" && (tipo.IsPublic || tipo.IsNestedPublic || tipo.IsNestedFamily || tipo.IsNestedFamORAssem))
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.NotEmpty(arquivos);
        Assert.True(foraDoNamespace.Length == 0, $"Arquivo da pasta interna do motor fora do namespace CodeProcess.Incognito.Interno: {string.Join(", ", foraDoNamespace)}.");
        Assert.True(publicos.Length == 0, $"Tipo visível fora do motor no namespace interno: {string.Join(", ", publicos)}.");
    }

    [Fact]
    public void Os_codigos_de_erro_sao_unicos_em_todo_o_codigo()
    {
        // Os códigos INCnnnn são declarados em mais de um assembly (núcleo, motor, dialetos): um número repetido seria
        // dois erros com o mesmo código.
        var declaracao = new Regex(@"const\s+string\s+\w+\s*=\s*""(INC\d{4})""");
        var raiz = Repositorio.Raiz();
        var codigos = Repositorio.Versionados("src/*.cs")
            .SelectMany(arquivo => declaracao.Matches(File.ReadAllText(Path.Combine(raiz, arquivo))).Select(casamento => (codigo: casamento.Groups[1].Value, arquivo)))
            .ToArray();
        var repetidos = codigos.GroupBy(par => par.codigo).Where(grupo => grupo.Count() > 1)
            .Select(grupo => $"{grupo.Key} ({string.Join(", ", grupo.Select(par => par.arquivo))})")
            .ToArray();

        // Um código escrito direto (fora de uma constante) escaparia da conferência acima.
        var literal = new Regex(@"""INC\d{4}""");
        var soltos = Repositorio.Versionados("src/*.cs")
            .SelectMany(arquivo => File.ReadAllLines(Path.Combine(raiz, arquivo))
                .Select((linha, indice) => (linha, onde: $"{arquivo}:{indice + 1}"))
                .Where(par => literal.IsMatch(par.linha) && !declaracao.IsMatch(par.linha)))
            .Select(par => par.onde)
            .ToArray();

        Assert.Contains(codigos, par => par.codigo == "INC3101");
        Assert.True(repetidos.Length == 0, $"Códigos de erro repetidos: {string.Join("; ", repetidos)}.");
        Assert.True(soltos.Length == 0, $"Código de erro escrito fora de uma constante: {string.Join(", ", soltos)}.");
    }

    [Fact]
    public void Cada_arquivo_de_codigos_fica_na_sua_faixa_e_todo_codigo_do_planejar_tem_teste()
    {
        // As faixas fecham quem pode usar cada código (decisão 86): o núcleo INC1xxx, a configuração INC2xxx, o planejar
        // INC300x a INC309x nos erros e INC351x a INC359x nos avisos, e cada dialeto a sua; um código fora da faixa do arquivo
        // seria um dialeto se passando por erro do planejar, ou o contrário.
        var faixas = new Dictionary<string, Regex>(StringComparer.Ordinal)
        {
            ["src/CodeProcess.Incognito/"] = new(@"^INC1[0-9]{3}$", RegexOptions.ECMAScript),
            ["src/CodeProcess.Incognito.Motor/Configuracao/CodigosConfiguracao.cs"] = new(@"^INC2[0-9]{3}$", RegexOptions.ECMAScript),
            ["src/CodeProcess.Incognito.Motor/Interno/CodigosPlanejar.cs"] = new(@"^INC30[0-9]{2}$|^INC35[1-9][0-9]$", RegexOptions.ECMAScript),
            ["src/CodeProcess.Incognito.PostgreSql/CodigosPostgreSql.cs"] = new(@"^INC31[0-9]{2}$", RegexOptions.ECMAScript),
            ["src/CodeProcess.Incognito.SqlServer/"] = new(@"^INC32[0-9]{2}$", RegexOptions.ECMAScript),
        };
        var declaracao = new Regex(@"const\s+string\s+\w+\s*=\s*""(INC[^""X][^""]*)""");
        var raiz = Repositorio.Raiz();
        var declarados = Repositorio.Versionados("src/*.cs")
            .SelectMany(arquivo => declaracao.Matches(File.ReadAllText(Path.Combine(raiz, arquivo))).Select(casamento => (arquivo, codigo: casamento.Groups[1].Value)))
            .ToArray();
        var semFaixa = declarados.Where(par => !faixas.Keys.Any(faixa => par.arquivo.StartsWith(faixa, StringComparison.Ordinal)))
            .Select(par => par.arquivo).Distinct().ToArray();
        var foraDaFaixa = declarados
            .Where(par => faixas.Where(faixa => par.arquivo.StartsWith(faixa.Key, StringComparison.Ordinal)).Any(faixa => !faixa.Value.IsMatch(par.codigo)))
            .Select(par => $"{par.codigo} ({par.arquivo})")
            .ToArray();

        // O código entra em CodigosPlanejar quando a verificação que o usa é implementada, sempre com teste.
        var testes = string.Concat(Repositorio.Versionados("testes/*.cs").Select(arquivo => File.ReadAllText(Path.Combine(raiz, arquivo))));
        var doPlanejar = declarados.Where(par => par.arquivo.EndsWith("/CodigosPlanejar.cs", StringComparison.Ordinal)).Select(par => par.codigo).ToArray();
        var semTeste = doPlanejar.Where(codigo => !testes.Contains($"\"{codigo}\"", StringComparison.Ordinal)).ToArray();

        // Um código fora do formato (dígito que não é ASCII, falta ou sobra de dígito) fugiria da faixa e da unicidade; o
        // INCX001 é o diagnóstico do contrato, não um código de erro.
        var malformados = declarados.Where(par => !Regex.IsMatch(par.codigo, "^INC[0-9]{4}$", RegexOptions.ECMAScript)).Select(par => $"{par.codigo} ({par.arquivo})").ToArray();

        Assert.NotEmpty(doPlanejar);
        Assert.True(malformados.Length == 0, $"Código fora do formato INC e 4 dígitos: {string.Join(", ", malformados)}.");
        Assert.True(semFaixa.Length == 0, $"Arquivo que declara códigos sem faixa definida: {string.Join(", ", semFaixa)}.");
        Assert.True(foraDaFaixa.Length == 0, $"Código fora da faixa do arquivo: {string.Join(", ", foraDaFaixa)}.");
        Assert.True(semTeste.Length == 0, $"Código do planejar sem teste que o cite: {string.Join(", ", semTeste)}.");
    }

    [Fact]
    public void A_copia_do_leitor_de_IL_e_igual_a_do_nucleo()
    {
        // A guarda da fronteira do dialeto usa uma cópia consciente (a do núcleo é interna ao projeto dele): as duas não podem
        // se afastar sem aviso.
        var raiz = Repositorio.Raiz();
        static string Corpo(string texto) => texto[texto.IndexOf("internal static class MembrosNoIl", StringComparison.Ordinal)..].ReplaceLineEndings("\n");

        Assert.Equal(
            Corpo(File.ReadAllText(Path.Combine(raiz, "testes/CodeProcess.Incognito.Testes/MembrosNoIl.cs"))),
            Corpo(File.ReadAllText(Path.Combine(raiz, "testes/CodeProcess.Incognito.Testes.Arquitetura/MembrosNoIl.cs"))));
    }

    [Fact]
    public void Todo_projeto_esta_na_solucao()
    {
        var solucao = XDocument.Load(Path.Combine(Repositorio.Raiz(), "Incognito.slnx"));
        var naSolucao = solucao.Descendants("Project").Select(projeto => (string?)projeto.Attribute("Path")).ToHashSet();
        var fora = Repositorio.Versionados("*.csproj")
            .Where(projeto => !projeto.StartsWith("benchmarks/", StringComparison.Ordinal) && !naSolucao.Contains(projeto))
            .ToArray();

        // Na solução, só pastas e projetos, sem configuração: um <Build Project="false" /> tiraria o projeto do build e dos
        // testes com ele ainda listado.
        var configuracao = solucao.Descendants()
            .Where(elemento => elemento.Name.LocalName is not ("Solution" or "Folder" or "Project")
                || (elemento.Name.LocalName == "Project" && elemento.Attributes().Any(atributo => atributo.Name.LocalName != "Path")))
            .Select(elemento => elemento.Name.LocalName)
            .ToArray();

        Assert.True(fora.Length == 0, $"Projeto fora do Incognito.slnx (o CI não o compila nem testa): {string.Join(", ", fora)}.");
        Assert.True(configuracao.Length == 0, $"Configuração de projeto no Incognito.slnx (pode tirar um projeto do build e dos testes): {string.Join(", ", configuracao)}.");
    }

    private static IEnumerable<string> ArquivosDoMsBuild() =>
        Repositorio.Versionados("*.csproj").Concat(Repositorio.Versionados("*.props")).Concat(Repositorio.Versionados("*.targets"));

    // O nome de cada referência de pacote (PackageReference e GlobalPackageReference), pelo Include ou pelo Update, um por
    // item da lista separada por ponto e vírgula.
    private static IEnumerable<string> ReferenciasDePacote(XDocument documento) =>
        documento.Descendants()
            .Where(elemento => elemento.Name.LocalName is "PackageReference" or "GlobalPackageReference")
            .SelectMany(elemento => ((string?)elemento.Attribute("Include") ?? (string?)elemento.Attribute("Update") ?? "").Split(';'))
            .Select(nome => nome.Trim());

    // Se o tipo deriva do provedor de dialeto, direto ou por uma base intermediária (pelo nome: o contrato é experimental).
    private static bool Provedor(Type tipo)
    {
        for (var atual = tipo.BaseType; atual is not null; atual = atual.BaseType)
        {
            if (atual.FullName == "CodeProcess.Incognito.Dialetos.ProvedorDialeto")
            {
                return true;
            }
        }

        return false;
    }

    // As linhas de atributo seguidas em volta da linha (para cima e para baixo), até a primeira que não é atributo.
    private static string[] BlocoDeAtributos(string[] linhas, int indice)
    {
        static bool Atributo(string linha) => linha.TrimStart().StartsWith('[');
        var inicio = indice;
        while (inicio > 0 && Atributo(linhas[inicio - 1]))
        {
            inicio--;
        }

        var fim = indice;
        while (fim < linhas.Length - 1 && Atributo(linhas[fim + 1]))
        {
            fim++;
        }

        return linhas[inicio..(fim + 1)];
    }

    // O texto do arquivo sem o atributo experimental exato, só no código dos dialetos.
    // Na pasta interna do motor, só um .cs pode suprimir, e só pela linha exata do pragma logo depois de uma linha de
    // comentário com a justificativa (decisão 86, D4); qualquer outra citação continua sendo violação.
    private static string SemOPragmaDoInterno(string arquivo, string texto)
    {
        if (!arquivo.StartsWith(InternoDoMotor, StringComparison.Ordinal) || !arquivo.EndsWith(".cs", StringComparison.Ordinal))
        {
            return texto;
        }

        var linhas = texto.Split('\n');
        return string.Join('\n', linhas.Where((linha, indice) =>
            !(linha.TrimEnd('\r') == "#pragma warning disable INCX001" && indice > 0 && linhas[indice - 1].TrimStart().StartsWith("//", StringComparison.Ordinal))));
    }

    private static string SemOAtributoDosDialetos(string arquivo, string texto) =>
        _codigoDosDialetos.Any(pasta => arquivo.StartsWith(pasta, StringComparison.Ordinal))
            ? texto.Replace(AtributoExperimental, "", StringComparison.Ordinal)
            : texto;

    // As linhas dos arquivos versionados das pastas de testes/ que não podem citar a categoria (o arquivo desta guarda fica
    // de fora). Vale para qualquer arquivo da pasta: o .csproj também pode marcar o assembly inteiro.
    private static List<string> Ocorrencias(Regex categoria, Func<string, bool> permitido)
    {
        var raiz = Repositorio.Raiz();
        var achados = new List<string>();
        foreach (var arquivo in Repositorio.Versionados("testes/*").Where(arquivo => arquivo != Este && !Binario(arquivo)))
        {
            var partes = arquivo.Split('/');
            if (partes.Length < 3 || permitido(partes[1]))
            {
                continue;
            }

            var linhas = File.ReadAllLines(Path.Combine(raiz, arquivo));
            for (var indice = 0; indice < linhas.Length; indice++)
            {
                if (categoria.IsMatch(linhas[indice]))
                {
                    achados.Add($"{arquivo}:{indice + 1}");
                }
            }
        }

        return achados;
    }

    // Os binários do .gitattributes e os que um projeto .NET costuma ter.
    private static bool Binario(string arquivo) =>
        Path.GetExtension(arquivo).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".pdf" or ".snk" or ".pfx";
}
