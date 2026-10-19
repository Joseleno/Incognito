using System.Reflection;
using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// Tudo o que o modelo do esquema diz, em texto, para comparar duas leituras: pelas propriedades públicas, para que um
/// campo novo do modelo entre sozinho.
/// </summary>
public static class DespejoDoModelo
{
    /// <summary>O texto de um objeto do modelo, de uma lista ou de um valor.</summary>
    /// <param name="valor">O que despejar.</param>
    /// <returns>O texto.</returns>
    public static string Texto(object? valor) => valor switch
    {
        null => "nulo",
        string texto => "\"" + texto + "\"",
        NomeObjeto or ReferenciaColuna or Enum or ValueType => valor.ToString()!,
        IEnumerable<object?> itens => "[" + string.Join(", ", itens.Select(Texto)) + "]",
        _ => valor.GetType().Name + " { " + string.Join(
            ", ",
            valor.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(propriedade => propriedade.GetIndexParameters().Length == 0)
                .OrderBy(propriedade => propriedade.Name, StringComparer.Ordinal)
                .Select(propriedade => propriedade.Name + " = " + Texto(propriedade.GetValue(valor)))) + " }",
    };
}
