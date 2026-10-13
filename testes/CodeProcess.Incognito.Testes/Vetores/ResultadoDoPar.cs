using System.Text.Json;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>O resultado de um par: saída (texto, inteiro ou nula) e desfecho, ou o código do erro.</summary>
internal sealed record ResultadoDoPar(object? Saida, string? Desfecho, string? Erro)
{
    /// <summary>O campo do par que diverge deste resultado (<c>erro</c>, <c>desfecho</c> ou <c>saida</c>), ou nulo.</summary>
    internal string? Divergencia(JsonElement par)
    {
        if (Confere(par))
        {
            return null;
        }

        var erroDoPar = par.TryGetProperty("erro", out var erro) ? erro.GetString() : null;
        if (Erro is not null || erroDoPar is not null)
        {
            return "erro";
        }

        return par.GetProperty("desfecho").GetString() != Desfecho ? "desfecho" : "saida";
    }

    /// <summary>Verdadeiro se o par do vetor tem exatamente este resultado.</summary>
    internal bool Confere(JsonElement par)
    {
        if (Erro is not null)
        {
            return par.TryGetProperty("erro", out var erro) && erro.GetString() == Erro && !par.TryGetProperty("saida", out _);
        }

        if (!par.TryGetProperty("saida", out var saida) || par.GetProperty("desfecho").GetString() != Desfecho)
        {
            return false;
        }

        return Saida switch
        {
            null => saida.ValueKind == JsonValueKind.Null,
            string texto => saida.ValueKind == JsonValueKind.String && string.Equals(saida.GetString(), texto, StringComparison.Ordinal),
            long numero => saida.ValueKind == JsonValueKind.Number && saida.TryGetInt64(out var esperado) && esperado == numero,
            _ => false,
        };
    }
}
