namespace CodeProcess.Incognito.Benchmarks;

/// <summary>Tamanho dos lotes de entradas: cada benchmark processa um lote por chamada e informa o custo por valor.</summary>
internal static class Lote
{
    /// <summary>Valores por chamada (o <c>OperationsPerInvoke</c> dos benchmarks).</summary>
    internal const int Tamanho = 1_024;

    /// <summary>Semente do <see cref="GeradorBr"/> das entradas.</summary>
    internal const long Semente = 2_021;
}
