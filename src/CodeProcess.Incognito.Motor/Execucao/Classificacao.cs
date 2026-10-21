namespace CodeProcess.Incognito.Execucao;

/// <summary>O que o plano sabe sobre o conteúdo de uma coluna, independente da regra que ela recebeu.</summary>
public sealed class Classificacao
{
    internal Classificacao(bool sensivel, bool identificador)
    {
        Sensivel = sensivel;
        Identificador = identificador;
    }

    /// <summary>Se a coluna foi marcada <c>sensivel: true</c> no <c>incognito.yaml</c>.</summary>
    public bool Sensivel { get; }

    /// <summary>
    /// Se a coluna está numa chave primária, numa chave estrangeira ou num índice único: mesmo sem dado pessoal, um
    /// identificador interno copiado permite ligar a cópia à produção.
    /// </summary>
    public bool Identificador { get; }
}
