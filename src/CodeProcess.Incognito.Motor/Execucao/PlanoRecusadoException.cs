using System.Globalization;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O plano foi recusado antes de qualquer cópia (saída 2): a configuração e o esquema juntos não permitem copiar com
/// segurança, ou o dialeto recusou a configuração ou o ambiente. Código <c>INC3000</c>; cada erro, com o seu código, está em
/// <see cref="Erros"/>.
/// </summary>
internal sealed class PlanoRecusadoException : IncognitoException
{
    /// <summary>O máximo de erros guardados; acima disso, a mensagem diz quantos ficaram de fora.</summary>
    internal const int Maximo = 100;

    internal PlanoRecusadoException(IReadOnlyList<ErroPlano> erros)
        : base(CodigosPlanejar.Agregado, Montar(erros))
    {
        Erros = [.. erros.Take(Maximo)];
    }

    /// <summary>Os erros, na ordem em que o planejar os encontrou: no máximo 100.</summary>
    internal IReadOnlyList<ErroPlano> Erros { get; }

    private static string Montar(IReadOnlyList<ErroPlano> erros)
    {
        ArgumentOutOfRangeException.ThrowIfZero(erros.Count);
        var quantidade = erros.Count > Maximo
            ? string.Create(CultureInfo.InvariantCulture, $"mais de {Maximo} erros; {Maximo} estão em Erros")
            : erros.Count == 1 ? "1 erro" : string.Create(CultureInfo.InvariantCulture, $"{erros.Count} erros");
        return $"Plano recusado ({quantidade}). Primeiro: {erros[0]}";
    }
}
