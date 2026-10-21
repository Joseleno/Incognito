namespace CodeProcess.Incognito.Execucao;

/// <summary>A forma da regra efetiva de uma coluna.</summary>
public enum TipoRegra
{
    /// <summary>Copia sem alterar (<c>manter</c>).</summary>
    Manter = 0,

    /// <summary>Passa o valor por um transformador.</summary>
    Transformar = 1,
}
