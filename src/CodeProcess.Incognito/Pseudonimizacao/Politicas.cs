using System.Diagnostics;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Aplicação das políticas de entrada comum aos pseudonimizadores: validação no construtor, DV errado e valor
/// malformado, com os códigos de erro estáveis. Nenhuma mensagem traz o valor.
/// </summary>
internal static class Politicas
{
    /// <summary>Código do valor fora da forma do documento com a política de falhar.</summary>
    internal const string CodigoMalformado = "INC1003";

    /// <summary>Código do DV errado com a política de falhar.</summary>
    internal const string CodigoDvInvalido = "INC1004";

    /// <summary>Recusa uma política de entrada inválida fora das definidas.</summary>
    /// <param name="politica">A política recebida.</param>
    /// <param name="parametro">O nome do parâmetro de quem chama.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal static void Validar(PoliticaEntradaInvalida politica, string parametro)
    {
        if (!Enum.IsDefined(politica))
        {
            throw new ArgumentOutOfRangeException(parametro, "Política de entrada inválida desconhecida.");
        }
    }

    /// <summary>Recusa uma política de entrada malformada fora das definidas.</summary>
    /// <param name="politica">A política recebida.</param>
    /// <param name="parametro">O nome do parâmetro de quem chama.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal static void Validar(PoliticaEntradaMalformada politica, string parametro)
    {
        if (!Enum.IsDefined(politica))
        {
            throw new ArgumentOutOfRangeException(parametro, "Política de entrada malformada desconhecida.");
        }
    }

    /// <summary>Aplica a política a um documento com DV errado.</summary>
    /// <param name="politica">A política de entrada inválida (já validada).</param>
    /// <param name="documento">O nome do documento na mensagem (<c>CPF</c>, <c>CNPJ</c>).</param>
    /// <returns>
    /// <see langword="true"/> para preservar (seguir com o deslocamento do DV); <see langword="false"/> para gravar
    /// nulo (<see cref="Desfecho.AnuladoDvInvalido"/>).
    /// </returns>
    /// <exception cref="IncognitoException"><c>INC1004</c>, com a política de falhar.</exception>
    internal static bool SeguirComDvInvalido(PoliticaEntradaInvalida politica, string documento) => politica switch
    {
        PoliticaEntradaInvalida.Preservar => true,
        PoliticaEntradaInvalida.Anular => false,
        PoliticaEntradaInvalida.Falhar => throw new IncognitoException(
            CodigoDvInvalido,
            $"{documento} com dígito verificador inválido; a política entradaInvalida é falhar. O valor não é exibido."),
        _ => throw new UnreachableException("Política de entrada inválida desconhecida."),
    };

    /// <summary>Aplica a política a um texto malformado e define a saída.</summary>
    /// <param name="politica">A política de entrada malformada (já validada).</param>
    /// <param name="mensagem">A mensagem da exceção, sem o valor.</param>
    /// <param name="entrada">O texto como veio.</param>
    /// <param name="saida">Nulo se anulado; a própria entrada se copiada em claro.</param>
    /// <returns><see cref="Desfecho.AnuladoMalformado"/> ou <see cref="Desfecho.CopiadoEmClaro"/>.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c>, com a política de falhar.</exception>
    internal static Desfecho AplicarMalformado(PoliticaEntradaMalformada politica, string mensagem, string entrada, out string? saida)
    {
        var desfecho = AplicarMalformado(politica, mensagem);
        saida = desfecho == Desfecho.CopiadoEmClaro ? entrada : null;
        return desfecho;
    }

    /// <summary>Aplica a política a um número malformado e define a saída.</summary>
    /// <param name="politica">A política de entrada malformada (já validada).</param>
    /// <param name="mensagem">A mensagem da exceção, sem o valor.</param>
    /// <param name="entrada">O número como veio.</param>
    /// <param name="saida">Nulo se anulado; a própria entrada se copiada em claro.</param>
    /// <returns><see cref="Desfecho.AnuladoMalformado"/> ou <see cref="Desfecho.CopiadoEmClaro"/>.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c>, com a política de falhar.</exception>
    internal static Desfecho AplicarMalformado(PoliticaEntradaMalformada politica, string mensagem, long entrada, out long? saida)
    {
        var desfecho = AplicarMalformado(politica, mensagem);
        saida = desfecho == Desfecho.CopiadoEmClaro ? entrada : null;
        return desfecho;
    }

    private static Desfecho AplicarMalformado(PoliticaEntradaMalformada politica, string mensagem) => politica switch
    {
        PoliticaEntradaMalformada.Anular => Desfecho.AnuladoMalformado,
        PoliticaEntradaMalformada.Preservar => Desfecho.CopiadoEmClaro,
        PoliticaEntradaMalformada.Falhar => throw new IncognitoException(CodigoMalformado, mensagem),
        _ => throw new UnreachableException("Política de entrada malformada desconhecida."),
    };
}
