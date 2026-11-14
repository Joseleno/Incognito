using System.Reflection;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Quem pode ler ou chamar o quê na execução: a configuração de que o plano saiu (<c>Plano.FonteDoPlano</c>) só serve ao
/// replanejamento (nenhum outro consumidor lê a regra escrita no lugar da efetiva, e depois de replanejar o executor lê só a
/// carga refeita); e só a criação protegida chama a fábrica registrada (a tradução das exceções de fora vale em todo lugar).
/// </summary>
public sealed class GuardasDaExecucaoTestes
{
    private const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void So_o_replanejador_le_a_configuracao_guardada_no_plano()
    {
        var leitura = typeof(Plano).GetProperty("FonteDoPlano", BindingFlags.NonPublic | BindingFlags.Instance)?.GetMethod;
        Assert.NotNull(leitura);

        Assert.Equal(["CodeProcess.Incognito.Interno.Replanejador"], QuemCita(leitura));
    }

    [Fact]
    public void So_a_criacao_protegida_chama_a_fabrica_registrada()
    {
        var criar = typeof(Plano).Assembly.GetType("CodeProcess.Incognito.Configuracao.FabricaRegistrada")?.GetMethod("Criar", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(criar);

        Assert.Equal(["CodeProcess.Incognito.Interno.CriacaoProtegida"], QuemCita(criar));
    }

    // Os tipos de primeiro nível do motor com algum método que cita o membro.
    private static string?[] QuemCita(MemberInfo membro) =>
        typeof(Plano).Assembly.GetTypes()
            .SelectMany(tipo => tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)))
            .Where(metodo => MembrosNoIl.Citados(metodo).Contains(membro))
            .Select(metodo => Externo(metodo.DeclaringType!).FullName)
            .Distinct()
            .ToArray();

    // O tipo de primeiro nível: as máquinas de estado e os fechamentos ficam aninhados no tipo que os escreveu.
    private static Type Externo(Type tipo)
    {
        var atual = tipo;
        while (atual.DeclaringType is { } dono)
        {
            atual = dono;
        }

        return atual;
    }
}
