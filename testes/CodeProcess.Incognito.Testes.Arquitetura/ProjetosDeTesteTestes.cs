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
        var esperados = _dialetos.Select(projeto => $"testes/{projeto}/{projeto}.csproj").ToArray();
        var intrusos = Repositorio.Versionados("*.csproj").Concat(Repositorio.Versionados("*.props")).Concat(Repositorio.Versionados("*.targets"))
            .Where(arquivo => !esperados.Contains(arquivo) && File.ReadAllText(Path.Combine(raiz, arquivo)).Contains(Conformidade, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(intrusos.Length == 0, $"Só os projetos de dialeto referenciam a suíte de conformidade, cujos testes são de banco: {string.Join(", ", intrusos)}.");
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
