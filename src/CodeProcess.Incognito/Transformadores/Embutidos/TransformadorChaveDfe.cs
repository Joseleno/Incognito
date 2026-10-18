using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// Transformador do <c>br.chave-dfe@1</c>: entrega a chave à instância da sessão. As ocorrências da chave (DV do
/// emitente errado, anomalia) ficam por dentro até o relatório ganhar o canal delas.
/// </summary>
internal sealed class TransformadorChaveDfe(PseudonimizadorChaveDfe chaveDfe) : Transformador
{
    public override string Nome => FabricaChaveDfe.NomeDoTransformador;

    public override int Versao => 1;

    public override Injetividade Injetividade => Injetividade.Garantida;

    public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => Transformar(valor, out _);

    /// <summary>Pseudonimiza a chave e devolve também as ocorrências.</summary>
    internal ResultadoTransformacao Transformar(object? valor, out OcorrenciasChaveDfe ocorrencias)
    {
        switch (valor)
        {
            case null:
                ocorrencias = OcorrenciasChaveDfe.Nenhuma;
                return new ResultadoTransformacao(null, Desfecho.Nulo);
            case string entrada:
                var desfecho = chaveDfe.Pseudonimizar(entrada, out var saida, out ocorrencias);
                return new ResultadoTransformacao(saida, desfecho);
            default:
                throw FabricaEmbutida.ForaDoTipo(TipoColuna.Texto, nameof(valor));
        }
    }
}
