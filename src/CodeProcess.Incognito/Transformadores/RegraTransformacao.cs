using System.Collections.Frozen;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O que a configuração diz de uma regra de transformação, sem nada do esquema do banco: o domínio, os parâmetros e as
/// políticas já resolvidas (a mais específica vence: coluna, domínio, global). A fábrica a confere uma vez, antes do
/// banco, para o erro apontar a configuração.
/// </summary>
/// <remarks>
/// Imutável. Os parâmetros vêm da configuração como texto (uma lista, como texto separado por vírgula) e são copiados
/// numa tabela ordinal congelada. Nada aqui é dado de coluna.
/// </remarks>
public sealed class RegraTransformacao
{
    private readonly IReadOnlyDictionary<string, string> _parametros = FrozenDictionary<string, string>.Empty;
    private readonly PoliticaEntradaInvalida _entradaInvalida = PoliticaEntradaInvalida.Preservar;
    private readonly PoliticaEntradaMalformada _entradaMalformada = PoliticaEntradaMalformada.Falhar;

    /// <summary>Cria a regra com os padrões: sem parâmetros, DV errado preservado, malformado falha.</summary>
    /// <param name="dominio">O domínio da regra.</param>
    /// <exception cref="ArgumentException">Domínio <c>default</c>.</exception>
    public RegraTransformacao(DominioPseudonimo dominio)
    {
        if (!dominio.Inicializado)
        {
            throw new ArgumentException("Domínio não inicializado (o valor default não é um domínio).", nameof(dominio));
        }

        Dominio = dominio;
    }

    /// <summary>O domínio da regra.</summary>
    public DominioPseudonimo Dominio { get; }

    /// <summary>Os parâmetros do transformador, por nome (comparação ordinal), como texto.</summary>
    /// <exception cref="ArgumentNullException">Na atribuição: nulo, ou nome ou valor nulo.</exception>
    /// <exception cref="ArgumentException">Na atribuição: nome vazio.</exception>
    public IReadOnlyDictionary<string, string> Parametros
    {
        get => _parametros;
        init => _parametros = Congelar(value);
    }

    /// <summary>A política para DV errado (padrão: <see cref="PoliticaEntradaInvalida.Preservar"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Na atribuição: valor fora do enum.</exception>
    public PoliticaEntradaInvalida EntradaInvalida
    {
        get => _entradaInvalida;
        init => _entradaInvalida = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), "Política de entrada inválida desconhecida.");
    }

    /// <summary>A política para valor fora da forma (padrão: <see cref="PoliticaEntradaMalformada.Falhar"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Na atribuição: valor fora do enum.</exception>
    public PoliticaEntradaMalformada EntradaMalformada
    {
        get => _entradaMalformada;
        init => _entradaMalformada = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), "Política de entrada malformada desconhecida.");
    }

    /// <summary>
    /// Se a coluna tem <c>derivarDe</c>; nulo quando não se aplica (a declaração de um domínio, que vale para várias
    /// colunas).
    /// </summary>
    public bool? TemDerivarDe { get; init; }

    /// <summary>Se a coluna tem <c>justificativa</c>; nulo quando não se aplica (a declaração de um domínio).</summary>
    public bool? TemJustificativa { get; init; }

    private static FrozenDictionary<string, string> Congelar(IReadOnlyDictionary<string, string> parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros, nameof(Parametros));
        foreach (var (nome, valor) in parametros)
        {
            ArgumentException.ThrowIfNullOrEmpty(nome, nameof(Parametros));
            ArgumentNullException.ThrowIfNull(valor, nameof(Parametros));
        }

        return parametros.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
