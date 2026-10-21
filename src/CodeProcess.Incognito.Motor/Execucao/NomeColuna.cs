namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O nome de uma coluna como está no catálogo do banco, com a tabela. Dois nomes são iguais quando a tabela e a coluna são
/// iguais caractere a caractere (comparação ordinal).
/// </summary>
public sealed class NomeColuna : IEquatable<NomeColuna>
{
    internal NomeColuna(NomeTabela tabela, string coluna)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentException.ThrowIfNullOrEmpty(coluna);
        Tabela = tabela;
        Coluna = coluna;
    }

    /// <summary>A tabela da coluna.</summary>
    public NomeTabela Tabela { get; }

    /// <summary>A coluna: texto do catálogo, sem escape; para exibir, use o <see cref="ToString"/>.</summary>
    public string Coluna { get; }

    /// <inheritdoc/>
    public bool Equals(NomeColuna? other) =>
        other is not null && Tabela.Equals(other.Tabela) && string.Equals(Coluna, other.Coluna, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NomeColuna);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Tabela, StringComparer.Ordinal.GetHashCode(Coluna));

    /// <summary>No formato <c>esquema.tabela.coluna</c>, para exibir, com o escape de <see cref="NomeTabela.ToString"/>.</summary>
    public override string ToString() => $"{Tabela}.{NomeTabela.Parte(Coluna)}";
}
