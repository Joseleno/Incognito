namespace CodeProcess.Incognito;

/// <summary>
/// Erro do Incognito, com código estável no formato <c>INC</c> seguido de 4 dígitos. A mensagem nunca traz valor de
/// coluna tratada nem a chave: identifica só o que falhou e onde.
/// </summary>
public class IncognitoException : Exception
{
    /// <summary>Cria a exceção com o código e a mensagem.</summary>
    /// <param name="codigo">Código estável do erro, como <c>INC1001</c>.</param>
    /// <param name="mensagem">Mensagem sem valores de coluna nem a chave.</param>
    public IncognitoException(string codigo, string mensagem)
        : base(mensagem)
    {
        Codigo = codigo;
    }

    /// <summary>Cria a exceção com o código, a mensagem e a exceção que a causou.</summary>
    /// <param name="codigo">Código estável do erro, como <c>INC1001</c>.</param>
    /// <param name="mensagem">Mensagem sem valores de coluna nem a chave.</param>
    /// <param name="interna">Exceção de origem, já sanitizada.</param>
    public IncognitoException(string codigo, string mensagem, Exception? interna)
        : base(mensagem, interna)
    {
        Codigo = codigo;
    }

    /// <summary>Código estável do erro, como <c>INC1001</c>.</summary>
    public string Codigo { get; }
}
