namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O dialeto falhou ao dizer o nome, conectar, ler, citar, comparar nomes (inclusive devolvendo um resultado desconhecido)
/// ou descartar, ou leu um esquema que se contradiz (saída 1). Código
/// <c>INC3001</c> quando a exceção não era do dialeto ou o código estava fora da faixa dos dialetos (só o tipo, a operação e
/// o lado saem), <c>INC3002</c> quando o esquema lido se contradiz, ou o código do próprio dialeto (<c>INC31xx</c> a
/// <c>INC34xx</c>). Nunca traz a exceção original: a do driver pode trazer a string de conexão ou valores.
/// </summary>
public sealed class FalhaDialetoException : IncognitoException
{
    internal FalhaDialetoException(string codigo, string mensagem)
        : base(codigo, mensagem)
    {
    }
}
