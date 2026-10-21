using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Execucao;

/// <summary>Uma coluna da origem no plano: o que a execução faz com ela.</summary>
public sealed class MapaColuna
{
    internal MapaColuna(string nome, TipoColuna? tipo, RegraColuna? regra, Classificacao classificacao)
    {
        ArgumentException.ThrowIfNullOrEmpty(nome);
        ArgumentNullException.ThrowIfNull(classificacao);
        Nome = nome;
        Tipo = tipo;
        Regra = regra;
        Classificacao = classificacao;
    }

    /// <summary>
    /// O nome da coluna: texto do catálogo, sem escape. Para exibir, escape os caracteres de controle, os de direção e os
    /// invisíveis, como o <see cref="NomeColuna.ToString"/> faz.
    /// </summary>
    public string Nome { get; }

    /// <summary>A forma canônica do tipo da coluna; nula quando o tipo não tem forma canônica (copiado como está).</summary>
    public TipoColuna? Tipo { get; }

    /// <summary>A regra efetiva; nula quando a coluna é copiada como está, sem regra.</summary>
    public RegraColuna? Regra { get; }

    /// <summary>O que o plano sabe sobre o conteúdo da coluna.</summary>
    public Classificacao Classificacao { get; }
}
