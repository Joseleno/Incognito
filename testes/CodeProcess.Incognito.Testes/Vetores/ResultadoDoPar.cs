using System.Text.Json;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// O resultado de um par: saída (texto, inteiro ou nula) e desfecho, ou o código do erro. Na chave de DF-e, as
/// ocorrências acompanham a saída, pelos nomes das flags em ordem crescente de valor.
/// </summary>
internal sealed record ResultadoDoPar(object? Saida, string? Desfecho, string? Erro)
{
    /// <summary>Os nomes das ocorrências da chave de DF-e (lista vazia se nenhuma); nulo nos outros transformadores.</summary>
    internal IReadOnlyList<string>? Ocorrencias { get; init; }

    /// <summary>
    /// O campo do par que diverge deste resultado (<c>erro</c>, <c>desfecho</c>, <c>ocorrencias</c> ou <c>saida</c>), ou
    /// nulo.
    /// </summary>
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

        if (par.GetProperty("desfecho").GetString() != Desfecho)
        {
            return "desfecho";
        }

        return OcorrenciasConferem(par) ? "saida" : "ocorrencias";
    }

    /// <summary>Verdadeiro se o par do vetor tem exatamente este resultado.</summary>
    internal bool Confere(JsonElement par)
    {
        if (Erro is not null)
        {
            return par.TryGetProperty("erro", out var erro) && erro.GetString() == Erro && !par.TryGetProperty("saida", out _)
                && !par.TryGetProperty("ocorrencias", out _);
        }

        if (!par.TryGetProperty("saida", out var saida) || par.GetProperty("desfecho").GetString() != Desfecho || !OcorrenciasConferem(par))
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

    private bool OcorrenciasConferem(JsonElement par)
    {
        if (!par.TryGetProperty("ocorrencias", out var ocorrencias))
        {
            return Ocorrencias is null;
        }

        return Ocorrencias is not null
            && ocorrencias.ValueKind == JsonValueKind.Array
            && ocorrencias.EnumerateArray().Select(o => o.GetString()).SequenceEqual(Ocorrencias, StringComparer.Ordinal);
    }
}
