namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Transforma os valores de uma coluna. Criado pela <see cref="FabricaTransformador"/> uma vez por coluna, com os
/// parâmetros já conferidos; classe abstrata para poder ganhar membros virtuais sem quebrar quem estende.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Transformar"/> é chamado por várias threads ao mesmo tempo. Um transformador de terceiro que guarde
/// recursos pode implementar <see cref="IDisposable"/>: o motor o descarta depois de todo uso.
/// </para>
/// <para>
/// Nenhuma exceção pode trazer o valor: o motor nunca registra a mensagem de uma exceção de um transformador que não seja
/// do Incognito (as do .NET, como a de <c>int.Parse</c>, trazem a entrada), só o tipo e o código, com tabela, coluna e
/// linha.
/// </para>
/// </remarks>
public abstract class Transformador
{
    /// <summary>O nome do transformador, igual ao da fábrica (<c>br.cpf</c>).</summary>
    public abstract string Nome { get; }

    /// <summary>A versão do transformador, igual à da fábrica.</summary>
    public abstract int Versao { get; }

    /// <summary>Se valores diferentes saem diferentes.</summary>
    public abstract Injetividade Injetividade { get; }

    /// <summary>
    /// Se o transformador recebe NULL. Falso (padrão): o motor copia o NULL sem chamá-lo e conta
    /// <see cref="Desfecho.Nulo"/>; verdadeiro só para quem troca NULL por um valor.
    /// </summary>
    public virtual bool TransformaNulo => false;

    /// <summary>Transforma um valor.</summary>
    /// <param name="valor">O valor original, na representação do tipo da coluna (<see cref="TipoColuna"/>), ou nulo.</param>
    /// <param name="contexto">O que muda de linha para linha além do valor.</param>
    /// <returns>O valor para o destino e o desfecho.</returns>
    /// <exception cref="IncognitoException">
    /// Pela política da regra: <c>INC1003</c> (valor fora da forma) ou <c>INC1004</c> (DV errado), sem o valor.
    /// </exception>
    public abstract ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto);
}
