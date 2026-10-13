namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>Um caso de vetor: nome, parâmetros (na ordem em que são gravados) e as receitas dos pares.</summary>
internal sealed record ReceitaDeCaso(string Nome, IReadOnlyList<KeyValuePair<string, object>> Parametros, IReadOnlyList<ReceitaDePar> Pares);
