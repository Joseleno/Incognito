using System.Collections.Frozen;

namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// Valores que a permutação não pode produzir (cycle-walking): repdígitos do CPF, raízes só com dígitos no CNPJ
/// alfanumérico, códigos proibidos na chave de DF-e.
/// </summary>
/// <remarks>
/// O conjunto precisa ser o mesmo para a mesma chave e o mesmo ajuste: se mudasse entre duas chamadas com o mesmo
/// ajuste, a bijeção se perderia. Quando o conjunto depende do valor (o número pseudonimizado na chave de DF-e), o
/// que o define entra no ajuste.
/// </remarks>
internal abstract class ConjuntoExcluido
{
    /// <summary>Nenhum valor excluído.</summary>
    internal static ConjuntoExcluido Vazio { get; } = new DeValoresFixos(FrozenSet<ulong>.Empty);

    /// <summary>Quantidade de valores excluídos, que define o limite de passos do cycle-walking.</summary>
    internal abstract ulong Quantidade { get; }

    /// <summary>Se o valor está excluído.</summary>
    /// <param name="valor">Valor do domínio da permutação.</param>
    /// <returns><see langword="true"/> se a permutação não pode produzi-lo.</returns>
    internal abstract bool Contem(ulong valor);

    /// <summary>Conjunto com os valores dados; repetições contam uma vez.</summary>
    /// <param name="valores">Valores excluídos.</param>
    /// <returns>O conjunto.</returns>
    internal static ConjuntoExcluido DeValores(params ReadOnlySpan<ulong> valores) =>
        new DeValoresFixos(valores.ToArray().ToFrozenSet());

    /// <summary>Conjunto definido por uma regra, para quando os valores são muitos para listar.</summary>
    /// <param name="regra">Se um valor está excluído.</param>
    /// <param name="quantidade">Quantidade exata de valores que a regra exclui no domínio.</param>
    /// <returns>O conjunto.</returns>
    internal static ConjuntoExcluido PorRegra(Func<ulong, bool> regra, ulong quantidade)
    {
        ArgumentNullException.ThrowIfNull(regra);
        return new DefinidoPorRegra(regra, quantidade);
    }

    private sealed class DeValoresFixos(FrozenSet<ulong> valores) : ConjuntoExcluido
    {
        internal override ulong Quantidade => (ulong)valores.Count;

        internal override bool Contem(ulong valor) => valores.Contains(valor);
    }

    private sealed class DefinidoPorRegra(Func<ulong, bool> regra, ulong quantidade) : ConjuntoExcluido
    {
        internal override ulong Quantidade => quantidade;

        internal override bool Contem(ulong valor) => regra(valor);
    }
}
