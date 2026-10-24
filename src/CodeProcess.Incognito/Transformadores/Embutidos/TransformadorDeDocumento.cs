namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>Pseudonimiza um documento em texto.</summary>
internal delegate Desfecho PseudonimizarTexto(string entrada, out string? saida);

/// <summary>Pseudonimiza um documento guardado em coluna numérica.</summary>
internal delegate Desfecho PseudonimizarNumero(long entrada, out long? saida);

/// <summary>
/// Transformador dos embutidos sem <c>derivarDe</c> (CPF, CNPJ, telefone, e-mail e identificador): entrega o valor à
/// instância da sessão, em texto ou em número conforme o tipo da coluna (o identificador só em número).
/// </summary>
internal sealed class TransformadorDeDocumento(
    string nome, Injetividade injetividade, TipoColuna tipo, PseudonimizarTexto? texto, PseudonimizarNumero? numero) : Transformador
{
    public override string Nome => nome;

    public override int Versao => 1;

    public override Injetividade Injetividade => injetividade;

    public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto)
    {
        switch (valor)
        {
            case null:
                return new ResultadoTransformacao(null, Desfecho.Nulo);
            case string entrada when tipo == TipoColuna.Texto && texto is not null:
                var desfecho = texto(entrada, out var saida);
                return new ResultadoTransformacao(saida, desfecho);
            case long entrada when tipo == TipoColuna.Inteiro && numero is not null:
                var desfechoNumerico = numero(entrada, out var saidaNumerica);
                return new ResultadoTransformacao(saidaNumerica, desfechoNumerico);
            default:
                throw FabricaEmbutida.ForaDoTipo(tipo, nameof(valor));
        }
    }
}
