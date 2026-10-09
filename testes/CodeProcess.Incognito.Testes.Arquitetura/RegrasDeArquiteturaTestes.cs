using System.Reflection;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Regras de dependência entre os pacotes, verificadas sobre as referências
/// que o compilador de fato gravou em cada assembly.
/// </summary>
public sealed class RegrasDeArquiteturaTestes
{
    private const string Nucleo = "CodeProcess.Incognito";
    private const string Motor = "CodeProcess.Incognito.Motor";
    private const string PostgreSql = "CodeProcess.Incognito.PostgreSql";
    private const string SqlServer = "CodeProcess.Incognito.SqlServer";

    private static readonly string[] _driversEEfCore = ["Npgsql", "Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore"];

    [Fact]
    public void Nucleo_nao_referencia_pacotes_de_terceiros()
    {
        var foraDoRuntime = Referencias(Nucleo)
            .Where(referencia => !ParteDoRuntime(referencia))
            .Select(referencia => referencia.Name)
            .ToArray();

        Assert.True(foraDoRuntime.Length == 0, $"O núcleo referencia assemblies fora do runtime do .NET: {string.Join(", ", foraDoRuntime)}.");
    }

    [Fact]
    public void Motor_nao_referencia_driver_de_banco_nem_EF_Core()
    {
        var proibidas = Referencias(Motor)
            .Select(referencia => referencia.Name!)
            .Where(nome => _driversEEfCore.Any(prefixo => nome == prefixo || nome.StartsWith(prefixo + ".", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(proibidas.Length == 0, $"O motor referencia driver de banco ou EF Core: {string.Join(", ", proibidas)}.");
    }

    [Theory]
    [InlineData(PostgreSql, SqlServer)]
    [InlineData(SqlServer, PostgreSql)]
    public void Dialetos_nao_se_referenciam(string dialeto, string outroDialeto)
    {
        var nomes = ReferenciasTransitivasDoProjeto(dialeto).Select(referencia => referencia.Name);

        Assert.DoesNotContain(outroDialeto, nomes);
    }

    [Theory]
    [InlineData(Nucleo)]
    [InlineData(Motor)]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    public void Nucleo_motor_e_dialetos_nao_usam_System_Net_Http(string assembly)
    {
        var nomes = ReferenciasTransitivasDoProjeto(assembly).Select(referencia => referencia.Name);

        Assert.DoesNotContain("System.Net.Http", nomes);
    }

    private static AssemblyName[] Referencias(string assembly) =>
        Assembly.Load(new AssemblyName(assembly)).GetReferencedAssemblies();

    /// <summary>
    /// Referências diretas do assembly e, recursivamente, as dos assemblies do próprio projeto que ele referencia.
    /// Pacotes de terceiros não são percorridos: a regra vale para o código deste repositório.
    /// </summary>
    private static List<AssemblyName> ReferenciasTransitivasDoProjeto(string assembly)
    {
        var visitados = new HashSet<string>(StringComparer.Ordinal) { assembly };
        var pendentes = new Queue<string>([assembly]);
        var resultado = new List<AssemblyName>();

        while (pendentes.TryDequeue(out var atual))
        {
            foreach (var referencia in Referencias(atual))
            {
                resultado.Add(referencia);
                if (referencia.Name!.StartsWith(Nucleo, StringComparison.Ordinal) && visitados.Add(referencia.Name))
                {
                    pendentes.Enqueue(referencia.Name);
                }
            }
        }

        return resultado;
    }

    /// <summary>Um assembly é do runtime quando está no diretório do framework compartilhado Microsoft.NETCore.App.</summary>
    private static bool ParteDoRuntime(AssemblyName referencia)
    {
        var diretorioDoRuntime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return File.Exists(Path.Combine(diretorioDoRuntime, referencia.Name + ".dll"));
    }
}
