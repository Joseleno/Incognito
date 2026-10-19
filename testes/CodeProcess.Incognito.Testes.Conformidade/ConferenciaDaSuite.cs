using System.Reflection;
using Xunit.v3;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A conferência, no assembly compilado de testes de um dialeto, do que a regra de arquitetura só vê pelo texto: uma
/// classe só herda a suíte, pública, selada, de primeiro nível e não genérica (a que o xUnit descobre), e todo teste leva a
/// categoria de banco. Um <c>&lt;Compile Remove&gt;</c>, um comentário ou um atributo de teste derivado enganam o texto,
/// não o assembly.
/// </summary>
public static class ConferenciaDaSuite
{
    /// <summary>Falha por asserção se o assembly do dialeto foge das regras.</summary>
    /// <param name="assembly">O assembly de testes do dialeto.</param>
    public static void Conferir(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var problemas = Problemas(assembly.GetTypes()).ToArray();

        Assert.True(problemas.Length == 0, $"Suíte no assembly {assembly.GetName().Name}: {string.Join("; ", problemas)}.");
    }

    /// <summary>Os problemas entre os tipos de um assembly de testes de dialeto.</summary>
    /// <param name="tipos">Os tipos do assembly.</param>
    /// <returns>Os problemas, vazio se nenhum.</returns>
    public static IEnumerable<string> Problemas(IEnumerable<Type> tipos)
    {
        ArgumentNullException.ThrowIfNull(tipos);
        var todos = tipos.ToArray();
        var classes = todos.Where(tipo => tipo.IsClass && !tipo.IsAbstract && DerivaDaSuite(tipo)).ToArray();
        if (classes.Length != 1)
        {
            yield return $"{classes.Length} classes concretas derivadas da suíte ({string.Join(", ", classes.Select(classe => classe.FullName))}), esperada 1";
        }

        foreach (var classe in classes.Where(classe => !classe.IsPublic || !classe.IsSealed || classe.IsGenericTypeDefinition
            || classe.BaseType is not { IsGenericType: true } baseDireta || baseDireta.GetGenericTypeDefinition() != typeof(ConformidadeDialetoTestes<>)))
        {
            yield return $"a classe {classe.FullName} precisa ser pública, selada, de primeiro nível, não genérica e herdar a suíte direto";
        }

        // O trait no método ou na classe: o filtro do CI vê os dois.
        foreach (var metodo in todos.SelectMany(tipo => tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
        {
            if (metodo.GetCustomAttributes().Any(atributo => atributo is IFactAttribute) && !DeBanco(metodo) && !DeBanco(metodo.DeclaringType!))
            {
                yield return $"o teste {metodo.DeclaringType!.FullName}.{metodo.Name} não tem a categoria de banco";
            }
        }
    }

    private static bool DerivaDaSuite(Type tipo)
    {
        for (var atual = tipo.BaseType; atual is not null; atual = atual.BaseType)
        {
            if (atual.IsGenericType && atual.GetGenericTypeDefinition() == typeof(ConformidadeDialetoTestes<>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DeBanco(MemberInfo membro) =>
        membro.GetCustomAttributes<TraitAttribute>().Any(trait => trait.Name == CategoriaDaSuite.Nome && trait.Value == CategoriaDaSuite.Valor);
}
