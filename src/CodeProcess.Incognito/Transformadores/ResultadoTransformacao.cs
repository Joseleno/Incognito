using System.Diagnostics;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O valor que vai para o destino e como ele foi tratado: o relatório conta os desfechos sem estado compartilhado
/// entre threads.
/// </summary>
/// <remarks>
/// O valor é dado de coluna (às vezes o próprio dado pessoal, copiado em claro por uma política): o
/// <see cref="ToString"/> e o depurador mostram só o desfecho. O <c>default</c> tem <see cref="Desfecho.Indefinido"/>,
/// que o motor recusa.
/// </remarks>
[DebuggerDisplay("ResultadoTransformacao({Desfecho})")]
public readonly struct ResultadoTransformacao
{
    /// <summary>Cria o resultado.</summary>
    /// <param name="valor">O valor para o destino, na representação do tipo da coluna, ou nulo.</param>
    /// <param name="desfecho">Como o valor foi tratado.</param>
    /// <exception cref="ArgumentOutOfRangeException">Desfecho <see cref="Desfecho.Indefinido"/> ou fora do enum.</exception>
    public ResultadoTransformacao(object? valor, Desfecho desfecho)
    {
        if (desfecho == Desfecho.Indefinido || !Enum.IsDefined(desfecho))
        {
            throw new ArgumentOutOfRangeException(nameof(desfecho), "Desfecho indefinido ou desconhecido.");
        }

        Valor = valor;
        Desfecho = desfecho;
    }

    /// <summary>O valor para o destino, ou nulo.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public object? Valor { get; }

    /// <summary>Como o valor foi tratado.</summary>
    public Desfecho Desfecho { get; }

    /// <summary>Só o desfecho, nunca o valor.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => $"ResultadoTransformacao({Desfecho})";
}
