namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// Um caso do esquema de conformidade, declarado pela fixture de cada dialeto: o recurso existe no banco (com o que se
/// espera ler dele) ou não existe (com o motivo). Inexistente não é teste pulado: a suíte confere que o leitor não
/// inventa o recurso.
/// </summary>
/// <typeparam name="T">O que se espera ler quando o recurso existe.</typeparam>
public sealed class Caso<T>
    where T : class
{
    private readonly T? _valor;

    internal Caso(T? valor, string? motivo)
    {
        _valor = valor;
        Motivo = motivo;
    }

    /// <summary>Se o recurso existe no banco.</summary>
    public bool Existe => _valor is not null;

    /// <summary>O que se espera ler.</summary>
    /// <exception cref="InvalidOperationException">O recurso não existe no banco.</exception>
    public T Valor => _valor ?? throw new InvalidOperationException($"O caso não existe neste banco: {Motivo}.");

    /// <summary>Por que o recurso não existe no banco; nulo se existe.</summary>
    public string? Motivo { get; }

    /// <inheritdoc/>
    public override string ToString() => Existe ? $"Presente({_valor})" : $"Inexistente({Motivo})";
}

/// <summary>A criação dos casos.</summary>
public static class Caso
{
    /// <summary>O recurso existe, e isto é o que se espera ler dele.</summary>
    /// <typeparam name="T">O que se espera ler.</typeparam>
    /// <param name="valor">O que se espera ler.</param>
    /// <returns>O caso.</returns>
    public static Caso<T> Presente<T>(T valor)
        where T : class => new(valor ?? throw new ArgumentNullException(nameof(valor)), null);

    /// <summary>O recurso não existe no banco (ou na versão), pelo motivo dado.</summary>
    /// <typeparam name="T">O que se esperaria ler.</typeparam>
    /// <param name="motivo">Por que não existe (por exemplo, "o PostgreSQL não tem máscara dinâmica").</param>
    /// <returns>O caso.</returns>
    public static Caso<T> Inexistente<T>(string motivo)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        return new(null, motivo);
    }
}
