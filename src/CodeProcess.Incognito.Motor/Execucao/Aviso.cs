namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// Um aviso do plano: algo que não impede a cópia, mas que quem a executa deve saber. Código estável, onde está (tabela,
/// coluna, objeto do banco e posição no <c>incognito.yaml</c>) e o que observar. A mensagem nunca traz valor de coluna, a
/// chave nem a string de conexão: só nomes do catálogo e da configuração, já escapados.
/// </summary>
public sealed class Aviso
{
    internal Aviso(string codigo, string mensagem, NomeTabela? tabela = null, string? coluna = null, string? objeto = null, PosicaoConfiguracao? posicao = null)
    {
        Ocorrencia.Conferir(codigo, mensagem, tabela, coluna, objeto);
        Codigo = codigo;
        Mensagem = mensagem;
        Tabela = tabela;
        Coluna = coluna;
        Objeto = objeto;
        Posicao = posicao;
    }

    /// <summary>Código estável do aviso, na faixa <c>INC35xx</c>.</summary>
    public string Codigo { get; }

    /// <summary>O que observar, já escapado.</summary>
    public string Mensagem { get; }

    /// <summary>A tabela do aviso; nula quando o aviso não é de uma tabela.</summary>
    public NomeTabela? Tabela { get; }

    /// <summary>
    /// A coluna do aviso, na <see cref="Tabela"/>; nula quando o aviso não é de uma coluna. Texto do catálogo, sem escape;
    /// para exibir, use o <see cref="ToString"/>.
    /// </summary>
    public string? Coluna { get; }

    /// <summary>
    /// O nome do índice, da restrição, do objeto no esquema reservado ou da outra coluna a que o aviso se refere; nulo quando
    /// não há. Só o nome, texto do catálogo sem escape: a <see cref="Mensagem"/> o traz escapado, com o tipo do objeto.
    /// </summary>
    public string? Objeto { get; }

    /// <summary>Onde, no <c>incognito.yaml</c>, está o que causou o aviso; nula quando o aviso não vem de algo escrito lá.</summary>
    public PosicaoConfiguracao? Posicao { get; }

    /// <summary>
    /// No formato <c>arquivo(linha,coluna): INCnnnn: esquema.tabela.coluna: mensagem</c>; sem posição, sem o prefixo do
    /// arquivo, e sem tabela, sem o nome. O arquivo sai com os caracteres que mudariam o que um terminal mostra trocados por
    /// <c>\uXXXX</c>; a barra invertida fica como está.
    /// </summary>
    public override string ToString() => Ocorrencia.Formatar(Codigo, Mensagem, Tabela, Coluna, Posicao);
}
