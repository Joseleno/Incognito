namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// Um erro do plano: código estável, onde está (tabela, coluna, objeto do banco e posição no <c>incognito.yaml</c>) e o
/// que está errado. A mensagem nunca traz valor de coluna, a chave nem a string de conexão: só nomes do catálogo e da
/// configuração, já escapados.
/// </summary>
/// <remarks>
/// Diferente de <see cref="Configuracao.ErroConfiguracao"/>, em que a coluna é a posição no YAML: aqui
/// <see cref="Coluna"/> é a coluna do banco, e a posição no YAML fica em <see cref="Posicao"/>.
/// </remarks>
public sealed class ErroPlano
{
    internal ErroPlano(string codigo, string mensagem, NomeTabela? tabela = null, string? coluna = null, string? objeto = null, PosicaoConfiguracao? posicao = null)
    {
        Ocorrencia.Conferir(codigo, mensagem, tabela, coluna, objeto);
        Codigo = codigo;
        Mensagem = mensagem;
        Tabela = tabela;
        Coluna = coluna;
        Objeto = objeto;
        Posicao = posicao;
    }

    /// <summary>Código estável do erro: <c>INC30xx</c> do planejar, ou o do dialeto que recusou (<c>INC31xx</c> a <c>INC34xx</c>).</summary>
    public string Codigo { get; }

    /// <summary>O que está errado e o que fazer, já escapado.</summary>
    public string Mensagem { get; }

    /// <summary>A tabela do erro; nula quando o erro não é de uma tabela.</summary>
    public NomeTabela? Tabela { get; }

    /// <summary>
    /// A coluna do erro, na <see cref="Tabela"/>; nula quando o erro não é de uma coluna. Texto do catálogo, sem escape;
    /// para exibir, use o <see cref="ToString"/>.
    /// </summary>
    public string? Coluna { get; }

    /// <summary>
    /// O nome do índice, da restrição, da partição, da sequence, do objeto residual, do objeto no esquema reservado ou da
    /// outra coluna a que o erro se refere; nulo quando não há. Só o nome, sem o esquema, texto do catálogo
    /// sem escape: a <see cref="Mensagem"/> o traz escapado e qualificado, com o tipo do objeto.
    /// </summary>
    public string? Objeto { get; }

    /// <summary>Onde, no <c>incognito.yaml</c>, está o que causou o erro; nula quando o erro não vem de algo escrito lá.</summary>
    public PosicaoConfiguracao? Posicao { get; }

    /// <summary>
    /// No formato <c>arquivo(linha,coluna): INCnnnn: esquema.tabela.coluna: mensagem</c>; sem posição, sem o prefixo do
    /// arquivo, e sem tabela, sem o nome. O arquivo sai com os caracteres que mudariam o que um terminal mostra trocados por
    /// <c>\uXXXX</c>; a barra invertida fica como está.
    /// </summary>
    public override string ToString() => Ocorrencia.Formatar(Codigo, Mensagem, Tabela, Coluna, Posicao);
}
