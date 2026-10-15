namespace CodeProcess.Incognito.Normas;

/// <summary>As referências normativas que esta versão do pacote considera, cada uma com a data da última revisão.</summary>
/// <remarks>
/// <para>
/// O catálogo é revisto à mão no repositório do projeto e muda só com uma nova versão do pacote: a biblioteca nunca
/// consulta a rede. Citar uma referência não indica conformidade com ela nem constitui aconselhamento jurídico.
/// </para>
/// <para>
/// Nenhum transformador lê o catálogo, e rever uma referência nunca muda a saída de um transformador.
/// </para>
/// </remarks>
public static class CatalogoNormativo
{
    /// <summary>
    /// As referências. A posição pode mudar entre versões do pacote: procure pelo
    /// <see cref="ReferenciaNormativa.Identificador"/>.
    /// </summary>
    public static IReadOnlyList<ReferenciaNormativa> Referencias => ReferenciasNormativas.Todas;

    /// <summary>
    /// A revisão mais antiga entre as referências: todo o catálogo foi revisto nesta data ou depois. É a data da revisão
    /// normativa embutida no pacote.
    /// </summary>
    public static DateOnly RevisaoMaisAntiga => MaisAntiga(ReferenciasNormativas.Todas);

    /// <summary>A menor <see cref="ReferenciaNormativa.RevisadaEm"/> de uma lista.</summary>
    /// <param name="referencias">As referências; ao menos uma.</param>
    /// <returns>A data mais antiga.</returns>
    /// <exception cref="ArgumentException">Lista vazia.</exception>
    internal static DateOnly MaisAntiga(IReadOnlyList<ReferenciaNormativa> referencias)
    {
        if (referencias.Count == 0)
        {
            throw new ArgumentException("O catálogo normativo não tem referências.", nameof(referencias));
        }

        var maisAntiga = referencias[0].RevisadaEm.DayNumber;
        for (var i = 1; i < referencias.Count; i++)
        {
            maisAntiga = Math.Min(maisAntiga, referencias[i].RevisadaEm.DayNumber);
        }

        return DateOnly.FromDayNumber(maisAntiga);
    }
}
