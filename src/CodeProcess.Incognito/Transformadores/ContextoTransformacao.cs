using System.Diagnostics;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O que muda de linha para linha além do valor: o valor original da coluna de <c>derivarDe</c>. O transformador vê só
/// a coluna que a regra declara, nunca a linha inteira.
/// </summary>
/// <remarks>
/// A referência é dado de coluna (às vezes pessoal): o <see cref="ToString"/> e o depurador não a mostram.
/// </remarks>
[DebuggerDisplay("ContextoTransformacao")]
public readonly struct ContextoTransformacao
{
    /// <summary>Cria o contexto.</summary>
    /// <param name="referencia">
    /// O valor ORIGINAL da coluna de <c>derivarDe</c>, na representação de <see cref="ContextoCriacao.TipoReferencia"/>;
    /// nulo sem <c>derivarDe</c> ou quando a coluna é NULL.
    /// </param>
    public ContextoTransformacao(object? referencia) => Referencia = referencia;

    /// <summary>O valor original da coluna de <c>derivarDe</c>, ou nulo.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public object? Referencia { get; }

    /// <summary>Só o nome do tipo, nunca a referência.</summary>
    /// <returns>O texto fixo.</returns>
    public override string ToString() => nameof(ContextoTransformacao);
}
