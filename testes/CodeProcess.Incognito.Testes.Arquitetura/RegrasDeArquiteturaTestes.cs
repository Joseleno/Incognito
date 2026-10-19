using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Regras de dependência entre os pacotes, verificadas sobre as referências
/// que o compilador de fato gravou em cada assembly, e regras de nomes dos tipos.
/// </summary>
public sealed class RegrasDeArquiteturaTestes
{
    private const string Nucleo = "CodeProcess.Incognito";
    private const string Motor = "CodeProcess.Incognito.Motor";
    private const string PostgreSql = "CodeProcess.Incognito.PostgreSql";
    private const string SqlServer = "CodeProcess.Incognito.SqlServer";
    private const string EntityFrameworkCore = "CodeProcess.Incognito.EntityFrameworkCore";
    private const string Cli = "CodeProcess.Incognito.Cli";
    private const string Dialetos = Nucleo + ".Dialetos";

    private static readonly string[] _driversEEfCore = ["Npgsql", "Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore"];

    // O HTTP do runtime e os invólucros dele (WebRequest, WebClient, o JSON sobre o HttpClient, o cliente de WebSocket),
    // pelo assembly e pelos tipos: uma biblioteca netstandard os alcança pelo netstandard, por encaminhamento de tipo.
    private static readonly string[] _assembliesDeHttp = ["System.Net.Http", "System.Net.Requests", "System.Net.WebClient", "System.Net.Http.Json", "System.Net.WebSockets.Client"];
    private static readonly string[] _tiposDeHttp = ["System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.WebClient", "System.Net.WebSockets.ClientWebSocket"];

    // Cpf, Cnpj e Email colidem com tipos que já existem em muitos projetos brasileiros; Dominio, com o namespace de
    // projetos DDD.
    private static readonly string[] _nomesDeTipoProibidos = ["Cpf", "Cnpj", "Email", "Dominio"];

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
        var nomes = ReferenciasTransitivasDoProjeto(assembly).Select(referencia => referencia.Name).Intersect(_assembliesDeHttp);

        Assert.Empty(nomes);
    }

    [Theory]
    [InlineData(PostgreSql, "Npgsql")]
    [InlineData(SqlServer, null)]
    public void Nenhum_assembly_de_terceiros_que_vai_com_o_dialeto_usa_System_Net_Http(string dialeto, string? driver)
    {
        // A regra acima não percorre pacotes de terceiros, que vão junto em todo uso do dialeto: aqui o fecho inteiro das
        // referências, a partir do dialeto, por todo assembly fora do runtime (o driver e as dependências dele).
        var visitados = new HashSet<string>(StringComparer.Ordinal) { dialeto };
        var pendentes = new Queue<string>([dialeto]);
        var usam = new List<string>();
        while (pendentes.TryDequeue(out var atual))
        {
            foreach (var referencia in Referencias(atual))
            {
                if (_assembliesDeHttp.Contains(referencia.Name))
                {
                    usam.Add($"{atual} ({referencia.Name})");
                }
                else if (!ParteDoRuntime(referencia) && visitados.Add(referencia.Name!))
                {
                    pendentes.Enqueue(referencia.Name!);
                }
            }

            usam.AddRange(TiposDeHttpReferenciados(Assembly.Load(new AssemblyName(atual))).Select(tipo => $"{atual} ({tipo})"));
        }

        Assert.True(driver is null || visitados.Contains(driver), $"O fecho de {dialeto} não chegou ao driver {driver}.");
        Assert.True(usam.Count == 0, $"Assemblies que vão com {dialeto} e usam System.Net.Http: {string.Join(", ", usam)}.");
    }

    [Fact]
    public void A_leitura_dos_tipos_referenciados_acha_o_HttpClient()
    {
        // O controle da regra acima: este assembly referencia o HttpClient só aqui, e a leitura dos metadados o acha.
        Assert.Equal("HttpClient", typeof(System.Net.Http.HttpClient).Name);

        Assert.Contains("System.Net.Http.HttpClient", TiposDeHttpReferenciados(typeof(RegrasDeArquiteturaTestes).Assembly));
    }

    [Theory]
    [InlineData(Nucleo)]
    [InlineData(Motor)]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    [InlineData(EntityFrameworkCore)]
    [InlineData(Cli)]
    public void Nenhum_tipo_nem_namespace_se_chama_Cpf_Cnpj_Email_ou_Dominio(string assembly)
    {
        // Sem diferenciar maiúsculas: CPF, EMail ou DOMINIO colidiriam do mesmo jeito.
        var proibidos = Tipos(assembly)
            .Where(tipo => _nomesDeTipoProibidos.Contains(NomeSemAridade(tipo.Name), StringComparer.OrdinalIgnoreCase)
                || (tipo.Namespace ?? "").Split('.').Intersect(_nomesDeTipoProibidos, StringComparer.OrdinalIgnoreCase).Any())
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(proibidos.Length == 0, $"Tipos com nome ou namespace proibido: {string.Join(", ", proibidos)}.");
    }

    [Fact]
    public void Assinaturas_do_gerador_nao_citam_a_chave_nem_a_pseudonimizacao()
    {
        // O GeradorBr não é criptográfico e não pode ser confundido com a pseudonimização: nenhum campo, parâmetro,
        // retorno ou tipo aninhado dele e do namespace Geracao vem da criptografia, da pseudonimização ou da chave.
        var gerador = Tipos(Nucleo).Where(tipo => tipo.FullName == Nucleo + ".GeradorBr" || tipo.Namespace == Nucleo + ".Geracao").ToArray();
        string[] proibidos = [Nucleo + ".Criptografia", Nucleo + ".Pseudonimizacao"];

        var usados = gerador
            .SelectMany(TiposDaAssinatura)
            .Where(tipo => proibidos.Contains(tipo.Namespace) || tipo.FullName == Nucleo + ".ChaveIncognito")
            .Select(tipo => tipo.FullName)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(gerador);
        Assert.True(usados.Length == 0, $"O gerador usa tipos da chave ou da pseudonimização: {string.Join(", ", usados)}.");
    }

    [Fact]
    public void Os_contratos_de_extensao_sao_classes_abstratas_sem_construtor_publico_e_o_Criar_nao_e_virtual()
    {
        // ADR 0017: classe abstrata pode ganhar membro virtual sem quebrar quem estende; o Criar confere a regra antes e
        // o transformador depois, e quem estende não consegue pular a conferência.
        var contratos = Tipos(Nucleo).Where(tipo => tipo.FullName is Nucleo + ".Transformadores.Transformador" or Nucleo + ".Transformadores.FabricaTransformador").ToArray();
        var criar = contratos.Single(tipo => tipo.Name == "FabricaTransformador").GetMethod("Criar")!;

        Assert.Equal(2, contratos.Length);
        Assert.All(contratos, tipo => Assert.True(tipo.IsAbstract && !tipo.IsInterface && tipo.GetConstructors().Length == 0, $"{tipo.FullName} precisa ser classe abstrata sem construtor público."));
        Assert.False(criar.IsVirtual, "O Criar da fábrica é o método-modelo: não pode ser virtual.");
    }

    [Fact]
    public void Tipos_publicos_do_motor_so_nascem_do_motor()
    {
        // ADR 0017: o modelo produzido pelo motor (configuração, erros, plano) não tem construtor público. O contrato de
        // dialeto fica de fora: o esquema lido é saída de um dialeto, que pode ser de fora, e tem construtor público.
        var comConstrutor = Tipos(Motor)
            .Where(tipo => tipo.IsVisible && !NoContrato(tipo) && tipo.GetConstructors().Length > 0)
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(comConstrutor.Length == 0, $"Tipos públicos do motor com construtor público: {string.Join(", ", comConstrutor)}.");
    }

    [Fact]
    public void Os_tipos_publicos_do_contrato_sao_selados_abstratos_structs_ou_enums()
    {
        // ADR 0017: as entradas e saídas do contrato têm construtor público, mas ninguém herda delas; os contratos de
        // extensão são abstratos.
        var abertos = Tipos(Nucleo)
            .Where(tipo => tipo.IsVisible && tipo.Namespace == Nucleo + ".Transformadores")
            .Where(tipo => tipo.IsClass && !tipo.IsSealed && !tipo.IsAbstract)
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(abertos.Length == 0, $"Tipos do contrato que se podem herdar sem serem contratos: {string.Join(", ", abertos)}.");
    }

    [Fact]
    public void O_contrato_de_dialeto_e_experimental_e_so_tem_classes_seladas_ou_abstratas()
    {
        // O contrato muda até a 1.0 (INCX001); contratos de extensão como classes abstratas (ADR 0017), sem interface.
        var tipos = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToArray();

        Assert.NotEmpty(tipos);
        Assert.All(tipos, tipo =>
        {
            Assert.True(tipo.GetCustomAttribute<ExperimentalAttribute>()?.DiagnosticId == "INCX001", $"{tipo.Name} sem [Experimental(\"INCX001\")].");
            Assert.False(tipo.IsInterface, $"{tipo.Name} é interface: o contrato de extensão é classe abstrata.");
            Assert.True(!tipo.IsClass || tipo.IsSealed || tipo.IsAbstract, $"{tipo.Name} se pode herdar sem ser contrato.");
            Assert.True(!tipo.IsAbstract || tipo.IsSealed || tipo.GetConstructors().Length == 0, $"{tipo.Name} é abstrato com construtor público.");
        });
    }

    [Fact]
    public void Nenhum_tipo_estavel_do_motor_expoe_o_contrato_de_dialeto()
    {
        // Um membro público fora do namespace experimental que use um tipo do contrato espalharia o INCX001 por quem só usa
        // a parte estável.
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var expostos = Tipos(Motor)
            .Where(tipo => tipo.IsVisible && !NoContrato(tipo))
            .SelectMany(tipo => tipo.GetMembers(Todos).Where(VisivelFora).SelectMany(TiposDoMembro)
                .Append(tipo.BaseType).Concat(tipo.GetInterfaces()).OfType<Type>().SelectMany(Desembrulhar).Select(usado => (tipo, usado)))
            .Where(par => NoContrato(par.usado))
            .Select(par => $"{par.tipo.Name} usa {par.usado.Name}")
            .Distinct()
            .ToArray();

        Assert.True(expostos.Length == 0, $"Tipos estáveis do motor que expõem o contrato de dialeto: {string.Join(", ", expostos)}.");
    }

    [Theory]
    [InlineData("ContextoTransformacao")]
    [InlineData("ResultadoTransformacao")]
    public void Os_structs_com_valor_de_coluna_nao_sao_record_e_nao_herdam_o_ToString(string nome)
    {
        // O ToString de um record mostraria o valor (às vezes o próprio dado pessoal).
        var tipo = Tipos(Nucleo).Single(t => t.FullName == $"{Nucleo}.Transformadores.{nome}");

        Assert.True(tipo.IsValueType, $"{nome} precisa ser struct.");
        Assert.Null(tipo.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(tipo, tipo.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
    }

    // Os tipos que aparecem nos campos, propriedades, parâmetros, retornos, tipo base e tipos aninhados (inclusive os
    // gerados para lambdas). Não percorre o corpo dos métodos.
    private static IEnumerable<Type> TiposDaAssinatura(Type tipo)
    {
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var tipos = new List<Type>();
        if (tipo.BaseType is not null)
        {
            tipos.Add(tipo.BaseType);
        }

        tipos.AddRange(tipo.GetFields(Todos).Select(campo => campo.FieldType));
        tipos.AddRange(tipo.GetProperties(Todos).Select(propriedade => propriedade.PropertyType));
        foreach (var metodo in tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)))
        {
            tipos.AddRange(metodo.GetParameters().Select(parametro => parametro.ParameterType));
            if (metodo is MethodInfo comRetorno)
            {
                tipos.Add(comRetorno.ReturnType);
            }
        }

        return tipos
            .SelectMany(Desembrulhar)
            .Concat(tipo.GetNestedTypes(Todos).SelectMany(TiposDaAssinatura));
    }

    private static bool NoContrato(Type tipo) => tipo.Namespace is { } nome && (nome == Dialetos || nome.StartsWith(Dialetos + ".", StringComparison.Ordinal));

    // Público ou protegido: o que quem herda de um tipo público também enxerga.
    private static bool VisivelFora(MemberInfo membro) => membro switch
    {
        MethodBase metodo => metodo.IsPublic || metodo.IsFamily || metodo.IsFamilyOrAssembly,
        FieldInfo campo => campo.IsPublic || campo.IsFamily || campo.IsFamilyOrAssembly,
        PropertyInfo propriedade => propriedade.GetAccessors(nonPublic: true).Any(VisivelFora),
        EventInfo evento => evento.AddMethod is { } adicionar && VisivelFora(adicionar),
        Type aninhado => aninhado.IsNestedPublic || aninhado.IsNestedFamily || aninhado.IsNestedFamORAssem,
        _ => false,
    };

    private static IEnumerable<Type> TiposDoMembro(MemberInfo membro) => membro switch
    {
        FieldInfo campo => [campo.FieldType],
        PropertyInfo propriedade => [propriedade.PropertyType],
        MethodInfo metodo => metodo.GetParameters().Select(parametro => parametro.ParameterType).Append(metodo.ReturnType),
        ConstructorInfo construtor => construtor.GetParameters().Select(parametro => parametro.ParameterType),
        EventInfo evento => evento.EventHandlerType is { } tipo ? [tipo] : [],
        _ => [],
    };

    // O tipo dos elementos de array, ponteiro e referência, e os argumentos de tipos genéricos.
    private static IEnumerable<Type> Desembrulhar(Type tipo)
    {
        if (tipo.HasElementType)
        {
            return Desembrulhar(tipo.GetElementType()!);
        }

        return tipo.IsGenericType ? tipo.GetGenericArguments().SelectMany(Desembrulhar).Prepend(tipo) : [tipo];
    }

    // Um tipo que não carrega não pode esconder os outros da verificação.
    private static IEnumerable<Type> Tipos(string assembly)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assembly)).GetTypes();
        }
        catch (ReflectionTypeLoadException excecao)
        {
            return excecao.Types.OfType<Type>();
        }
    }

    private static string NomeSemAridade(string nome)
    {
        var crase = nome.IndexOf('`', StringComparison.Ordinal);
        return crase < 0 ? nome : nome[..crase];
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
    // Os tipos de HTTP que o assembly referencia, pelos metadados, qualquer que seja o assembly que os resolve.
    private static string[] TiposDeHttpReferenciados(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();
        return metadados.TypeReferences
            .Select(manipulador => metadados.GetTypeReference(manipulador))
            .Select(tipo => metadados.GetString(tipo.Namespace) + "." + metadados.GetString(tipo.Name))
            .Where(nome => nome.StartsWith("System.Net.Http.", StringComparison.Ordinal) || _tiposDeHttp.Contains(nome))
            .Distinct()
            .ToArray();
    }

    private static bool ParteDoRuntime(AssemblyName referencia)
    {
        var diretorioDoRuntime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return File.Exists(Path.Combine(diretorioDoRuntime, referencia.Name + ".dll"));
    }
}
