using System.Globalization;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O plano foi recusado antes de qualquer cópia (saída 2): a configuração e o esquema juntos não permitem copiar com
/// segurança, ou o dialeto recusou a configuração ou o ambiente. Código <c>INC3000</c>; cada erro, com o seu código, está em
/// <see cref="Erros"/>.
/// </summary>
public sealed class PlanoRecusadoException : IncognitoException
{
    /// <summary>O máximo de erros guardados; acima disso, a mensagem diz quantos ficaram de fora.</summary>
    internal const int Maximo = 100;

    internal PlanoRecusadoException(IReadOnlyList<ErroPlano> erros)
        : base(CodigosPlanejar.Agregado, Montar(erros))
    {
        Erros = [.. erros.Take(Maximo)];
    }

    /// <summary>
    /// Os erros, no máximo 100, em ordem de tabela (ordinal por esquema e nome; sem tabela antes), posição da coluna, código,
    /// objeto e posição no <c>incognito.yaml</c>.
    /// </summary>
    public IReadOnlyList<ErroPlano> Erros { get; }

    private static string Montar(IReadOnlyList<ErroPlano> erros)
    {
        ArgumentNullException.ThrowIfNull(erros);
        ArgumentOutOfRangeException.ThrowIfZero(erros.Count);
        if (erros.Any(erro => erro is null))
        {
            throw new ArgumentNullException(nameof(erros), "A lista tem erro nulo.");
        }

        var quantidade = erros.Count > Maximo
            ? string.Create(CultureInfo.InvariantCulture, $"mais de {Maximo} erros; {Maximo} estão em Erros")
            : erros.Count == 1 ? "1 erro" : string.Create(CultureInfo.InvariantCulture, $"{erros.Count} erros");
        return $"Plano recusado ({quantidade}). Primeiro: {erros[0]}";
    }
}
