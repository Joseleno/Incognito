namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>texto.substituir@1</c>: o texto inteiro trocado por um texto fixo, cortado no comprimento máximo da coluna; sem
/// parâmetros e sem a chave.
/// </summary>
internal sealed class FabricaTextoSubstituir() : FabricaEmbutida(NomeDoTransformador, NomeDoTransformador, Injetividade.Nenhuma, TipoColuna.Texto)
{
    internal const string NomeDoTransformador = "texto.substituir";

    protected override Transformador CriarCore(ContextoCriacao contexto) => new TransformadorTextoSubstituir(contexto.ComprimentoMaximo);
}
