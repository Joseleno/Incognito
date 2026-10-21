namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// Um erro do plano: código estável e o que está errado. A mensagem nunca traz valor de coluna, a chave nem a string de
/// conexão: só nomes do catálogo e da configuração, já escapados.
/// </summary>
internal sealed class ErroPlano
{
    internal ErroPlano(string codigo, string mensagem)
    {
        Codigo = codigo;
        Mensagem = mensagem;
    }

    /// <summary>Código estável do erro: <c>INC30xx</c> do planejar, ou o do dialeto que recusou (<c>INC31xx</c> a <c>INC34xx</c>).</summary>
    internal string Codigo { get; }

    /// <summary>O que está errado e o que fazer, já escapado.</summary>
    internal string Mensagem { get; }

    /// <summary>No formato <c>INCnnnn: mensagem</c>.</summary>
    public override string ToString() => $"{Codigo}: {Mensagem}";
}
