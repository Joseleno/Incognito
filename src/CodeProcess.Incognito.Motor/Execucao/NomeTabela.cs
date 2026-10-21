using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O nome de uma tabela como está no catálogo do banco, já resolvido contra o que a configuração escreveu. Dois nomes são
/// iguais quando o esquema e a tabela são iguais caractere a caractere (comparação ordinal).
/// </summary>
public sealed class NomeTabela : IEquatable<NomeTabela>
{
    internal NomeTabela(string esquema, string tabela)
    {
        ArgumentException.ThrowIfNullOrEmpty(esquema);
        ArgumentException.ThrowIfNullOrEmpty(tabela);
        Esquema = esquema;
        Tabela = tabela;
    }

    /// <summary>O esquema: texto do catálogo, sem escape; para exibir, use o <see cref="ToString"/>.</summary>
    public string Esquema { get; }

    /// <summary>A tabela: texto do catálogo, sem escape; para exibir, use o <see cref="ToString"/>.</summary>
    public string Tabela { get; }

    /// <inheritdoc/>
    public bool Equals(NomeTabela? other) =>
        other is not null && string.Equals(Esquema, other.Esquema, StringComparison.Ordinal) && string.Equals(Tabela, other.Tabela, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NomeTabela);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Esquema), StringComparer.Ordinal.GetHashCode(Tabela));

    /// <summary>
    /// No formato <c>esquema.tabela</c>, para exibir: os caracteres que mudariam o que um terminal mostra saem como
    /// <c>\uXXXX</c>, e a parte com ponto, espaço ou aspa sai entre aspas duplas, com a aspa dobrada, sem as aspas de
    /// nenhum dialeto.
    /// </summary>
    public override string ToString() => $"{Parte(Esquema)}.{Parte(Tabela)}";

    // Uma parte do nome, escapada e, se preciso, entre aspas: sem elas, "a.b"."c" e "a"."b.c" sairiam iguais.
    internal static string Parte(string nome)
    {
        var escapado = TextoSeguro.Nome(nome);
        return nome.AsSpan().IndexOfAny(". \"") < 0 ? escapado : $"\"{escapado.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
