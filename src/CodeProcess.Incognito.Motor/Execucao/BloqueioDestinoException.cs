namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O bloqueio exclusivo de execução no destino (saída 1): <c>INC3004</c> quando outra execução já o tem, antes de qualquer
/// escrita no destino; <c>INC4001</c> quando ele se perdeu no meio (a conexão do bloqueio caiu ou o batimento falhou), e
/// nada mais é escrito. A mensagem nunca traz quem tem o bloqueio nem a exceção original.
/// </summary>
public sealed class BloqueioDestinoException : IncognitoException
{
    internal BloqueioDestinoException(string codigo, string mensagem)
        : base(codigo, mensagem)
    {
    }
}
