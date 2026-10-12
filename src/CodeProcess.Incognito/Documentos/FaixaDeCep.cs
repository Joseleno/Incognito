namespace CodeProcess.Incognito.Documentos;

/// <summary>Uma faixa de CEP de uma UF.</summary>
internal readonly struct FaixaDeCep(int inicio, int fim, int uf)
{
    /// <summary>O primeiro CEP da faixa.</summary>
    internal int Inicio { get; } = inicio;

    /// <summary>O último CEP da faixa.</summary>
    internal int Fim { get; } = fim;

    /// <summary>O índice da UF em <see cref="FaixasDeCep.Ufs"/>.</summary>
    internal int Uf { get; } = uf;

    /// <summary>A quantidade de CEPs da faixa.</summary>
    internal ulong Tamanho => (ulong)(Fim - Inicio + 1);
}
