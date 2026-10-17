using System.Globalization;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// Transformador do <c>br.cep@1</c>: o CEP sai da faixa da mesma UF, derivado da entidade (o valor original da coluna
/// de <c>derivarDe</c>), não do próprio valor.
/// </summary>
/// <remarks>
/// A entidade vai como texto: o texto como veio, o inteiro em decimal sem zeros à esquerda e NULL como vazio. Um CPF
/// guardado em <c>bigint</c> e o mesmo CPF em texto dão entidades (e CEPs) diferentes: normalize a grafia antes, se as
/// tabelas precisarem bater.
/// </remarks>
internal sealed class TransformadorCep(PseudonimizadorCep cep, TipoColuna tipo, TipoColuna tipoReferencia) : Transformador
{
    public override string Nome => FabricaCep.NomeDoTransformador;

    public override int Versao => 1;

    public override Injetividade Injetividade => Injetividade.Nenhuma;

    public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto)
    {
        if (valor is null)
        {
            return new ResultadoTransformacao(null, Desfecho.Nulo);
        }

        var entidade = contexto.Referencia switch
        {
            null => "",
            string texto when tipoReferencia == TipoColuna.Texto => texto,
            long numero when tipoReferencia == TipoColuna.Inteiro => numero.ToString(CultureInfo.InvariantCulture),
            _ => throw FabricaEmbutida.ForaDoTipo(tipoReferencia, nameof(contexto)),
        };

        switch (valor)
        {
            case string entrada when tipo == TipoColuna.Texto:
                var desfecho = cep.Pseudonimizar(entrada, entidade, out var saida);
                return new ResultadoTransformacao(saida, desfecho);
            case long entrada when tipo == TipoColuna.Inteiro:
                var desfechoNumerico = cep.Pseudonimizar(entrada, entidade, out var saidaNumerica);
                return new ResultadoTransformacao(saidaNumerica, desfechoNumerico);
            default:
                throw FabricaEmbutida.ForaDoTipo(tipo, nameof(valor));
        }
    }
}
