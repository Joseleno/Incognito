using System.Globalization;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// O <c>incognito.yaml</c> não pôde ser lido ou não passou na validação. Código <c>INC2000</c>; cada erro, com o seu
/// código e posição, está em <see cref="Erros"/>. Nenhuma mensagem traz o valor escrito nos campos.
/// </summary>
public sealed class ConfiguracaoInvalidaException : IncognitoException
{
    internal ConfiguracaoInvalidaException(IReadOnlyList<ErroConfiguracao> erros, bool omitidos)
        : base(CodigosConfiguracao.Agregado, Montar(erros, omitidos))
    {
        Erros = erros;
    }

    /// <summary>
    /// Os erros, na ordem do arquivo: no máximo 100 (com mais, os 100 primeiros que a leitura encontrou), e até 20 avisos de
    /// segredo por extenso, que entram mesmo quando o máximo já foi atingido.
    /// </summary>
    public IReadOnlyList<ErroConfiguracao> Erros { get; }

    private static string Montar(IReadOnlyList<ErroConfiguracao> erros, bool omitidos)
    {
        var quantidade = omitidos
            ? string.Create(CultureInfo.InvariantCulture, $"mais de {Leitura.ColetorDeErros.Maximo} erros; {erros.Count} estão em Erros")
            : erros.Count == 1 ? "1 erro" : string.Create(CultureInfo.InvariantCulture, $"{erros.Count} erros");
        return $"Configuração inválida ({quantidade}). Primeiro: {erros[0]}";
    }
}
