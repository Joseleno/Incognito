namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// Transformador do <c>texto.substituir@1</c>: troca o texto inteiro por um texto fixo, cortado no comprimento máximo
/// da coluna. Não usa a chave e não vaza nada da entrada (nem o tamanho); só o vazio volta como veio.
/// </summary>
internal sealed class TransformadorTextoSubstituir : Transformador
{
    /// <summary>O texto fixo, em ASCII: um caractere é um byte em qualquer colação.</summary>
    internal const string TextoFixo = "Texto removido pelo Incognito.";

    private readonly string _saida;

    internal TransformadorTextoSubstituir(int? comprimentoMaximo) =>
        _saida = comprimentoMaximo < TextoFixo.Length ? TextoFixo[..comprimentoMaximo.Value] : TextoFixo;

    public override string Nome => FabricaTextoSubstituir.NomeDoTransformador;

    public override int Versao => 1;

    public override Injetividade Injetividade => Injetividade.Nenhuma;

    public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => valor switch
    {
        null => new ResultadoTransformacao(null, Desfecho.Nulo),

        // Vazio ou só com espaços (char(n) em branco): não há texto a remover.
        string entrada when entrada.AsSpan().TrimEnd(' ').IsEmpty => new ResultadoTransformacao(entrada, Desfecho.Vazio),
        string => new ResultadoTransformacao(_saida, Desfecho.Substituido),
        _ => throw FabricaEmbutida.ForaDoTipo(TipoColuna.Texto, nameof(valor)),
    };
}
