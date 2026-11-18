using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A conferência do resultado antes de gravar (9.1 item 30): cada limite do contexto da coluna aceita a borda e recusa o
/// valor logo depois dela, porque o banco truncaria, arredondaria ou recusaria em silêncio.
/// </summary>
public sealed class ConferenciaDoResultadoTestes
{
    private static readonly DateTime _instante = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Unspecified).AddTicks(1230);

    [Theory]
    [InlineData("inteiro no mínimo", true)]
    [InlineData("inteiro abaixo do mínimo", false)]
    [InlineData("inteiro no máximo", true)]
    [InlineData("inteiro acima do máximo", false)]
    [InlineData("decimal na precisão e na escala", true)]
    [InlineData("decimal com uma casa a mais", false)]
    [InlineData("decimal com um dígito inteiro a mais", false)]
    [InlineData("decimal sem escala no limite", true)]
    [InlineData("decimal sem escala acima", false)]
    [InlineData("texto no comprimento", true)]
    [InlineData("texto acima do comprimento", false)]
    [InlineData("binário no comprimento", true)]
    [InlineData("binário acima do comprimento", false)]
    [InlineData("data e hora no microssegundo", true)]
    [InlineData("data e hora com fração abaixo do microssegundo", false)]
    [InlineData("com fuso no microssegundo", true)]
    [InlineData("com fuso com fração abaixo do microssegundo", false)]
    public void Cada_limite_do_contexto_aceita_a_borda_e_recusa_o_valor_logo_depois(string caso, bool aceito)
    {
        var (contexto, valor) = Caso(caso);

        var problema = ConferenciaDoResultado.Problema(contexto, entradaNula: false, new ResultadoTransformacao(valor, Desfecho.Pseudonimizado));

        Assert.True((problema is null) == aceito, $"{caso}: {problema ?? "aceito"}");
    }

    private static (ContextoDaColuna Contexto, object Valor) Caso(string caso)
    {
        var inteiro = Contexto(TipoColuna.Inteiro, menor: -10, maior: 10);
        var decimal_ = Contexto(TipoColuna.Decimal, precisao: 5, escala: 2);
        var semEscala = Contexto(TipoColuna.Decimal, precisao: 3);
        var texto = Contexto(TipoColuna.Texto, comprimento: 3);
        var binario = Contexto(TipoColuna.Binario, comprimento: 3);
        return caso switch
        {
            "inteiro no mínimo" => (inteiro, -10L),
            "inteiro abaixo do mínimo" => (inteiro, -11L),
            "inteiro no máximo" => (inteiro, 10L),
            "inteiro acima do máximo" => (inteiro, 11L),
            "decimal na precisão e na escala" => (decimal_, 999.99m),
            "decimal com uma casa a mais" => (decimal_, 1.234m),
            "decimal com um dígito inteiro a mais" => (decimal_, 1000.5m),
            "decimal sem escala no limite" => (semEscala, 9.99m),
            "decimal sem escala acima" => (semEscala, 99.99m),
            "texto no comprimento" => (texto, "abc"),
            "texto acima do comprimento" => (texto, "abcd"),
            "binário no comprimento" => (binario, new byte[3]),
            "binário acima do comprimento" => (binario, new byte[4]),
            "data e hora no microssegundo" => (Contexto(TipoColuna.DataHora), _instante),
            "data e hora com fração abaixo do microssegundo" => (Contexto(TipoColuna.DataHora), _instante.AddTicks(1)),
            "com fuso no microssegundo" => (Contexto(TipoColuna.DataHoraComFuso), new DateTimeOffset(_instante, TimeSpan.Zero)),
            _ => (Contexto(TipoColuna.DataHoraComFuso), new DateTimeOffset(_instante.AddTicks(1), TimeSpan.Zero)),
        };
    }

    private static ContextoDaColuna Contexto(TipoColuna tipo, int? comprimento = null, long? menor = null, long? maior = null, int? precisao = null, int? escala = null) =>
        new(tipo, comprimento, menor, maior, precisao, escala, null, null);
}
