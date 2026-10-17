using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O tipo de uma coluna, que fixa a representação .NET do valor entregue ao transformador. O dialeto converte só as
/// colunas tratadas; as outras passam como bytes opacos e nunca chegam ao transformador. Novos tipos podem ser
/// acrescentados, e quem os lê trata um valor desconhecido.
/// </summary>
public enum TipoColuna
{
    /// <summary><see cref="string"/>.</summary>
    Texto,

    /// <summary><see cref="long"/> (um CPF guardado como <c>bigint</c> chega como <see cref="long"/>, sem os zeros à esquerda).</summary>
    Inteiro,

    /// <summary><see cref="decimal"/> (as colunas <c>numeric</c> e <c>decimal</c>).</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "É o nome do tipo SQL, o mesmo da tabela da representação canônica.")]
    Decimal,

    /// <summary><see cref="DateOnly"/>.</summary>
    Data,

    /// <summary><see cref="DateTime"/> sem fuso, sempre com <see cref="DateTimeKind.Unspecified"/>.</summary>
    DataHora,

    /// <summary><see cref="DateTimeOffset"/>, com o deslocamento que o banco guarda.</summary>
    DataHoraComFuso,

    /// <summary><see cref="byte"/>[].</summary>
    Binario,

    /// <summary><see cref="bool"/>.</summary>
    Booleano,

    /// <summary><see cref="Guid"/>.</summary>
    Uuid,
}
