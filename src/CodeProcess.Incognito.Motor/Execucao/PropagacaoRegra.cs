namespace CodeProcess.Incognito.Execucao;

/// <summary>Por onde a regra chegou a uma coluna que não a escreveu.</summary>
public enum PropagacaoRegra
{
    /// <summary>Por uma chave estrangeira, da coluna referenciada para a que referencia, ou o contrário.</summary>
    ChaveEstrangeira = 0,

    /// <summary>Pela herança de tabelas, entre a mãe e as herdeiras.</summary>
    Heranca = 1,
}
