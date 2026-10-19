using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// A string de conexão de um banco, já resolvida pelo motor a partir da referência da configuração
/// (<c>ambiente:</c> ou <c>arquivo:</c>). <see cref="ToString"/> nunca a mostra, e o depurador não a mostra na exibição
/// padrão; <see cref="Revelar"/> é o único ponto de leitura, para o dialeto entregá-la ao driver.
/// </summary>
/// <remarks>
/// Não tira o segredo da memória (o driver pede uma <see cref="string"/>, e um dump ou a exibição bruta do depurador o
/// mostram): só impede que ele vá parar num log, numa mensagem ou num relatório por engano. O dialeto nunca registra
/// nem põe numa exceção o que <see cref="Revelar"/> devolve.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
[DebuggerDisplay("[segredo]")]
public sealed class SegredoConexao
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _valor;

    /// <summary>Guarda a string de conexão.</summary>
    /// <param name="valor">A string de conexão.</param>
    /// <exception cref="ArgumentNullException">Nula.</exception>
    /// <exception cref="ArgumentException">Vazia ou só com espaços; a mensagem não traz o valor.</exception>
    public SegredoConexao(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException("A string de conexão está vazia.", nameof(valor));
        }

        _valor = valor;
    }

    /// <summary>A string de conexão, para entregar ao driver; nunca para registrar.</summary>
    /// <returns>A string de conexão.</returns>
    public string Revelar() => _valor;

    /// <summary>Sempre <c>[segredo]</c>.</summary>
    /// <returns><c>[segredo]</c>.</returns>
    public override string ToString() => "[segredo]";
}
