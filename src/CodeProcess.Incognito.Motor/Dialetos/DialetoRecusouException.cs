using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// O dialeto recusa a configuração ou o ambiente (string de conexão que não pode ser lida, opção que o Incognito não
/// aceita, versão do servidor abaixo da mínima, estado do banco que impede a leitura): o motor a mostra como erro do plano,
/// com saída 2, e não como falha de execução. O código fica na faixa do dialeto (<c>INC31xx</c> a <c>INC34xx</c>), e a
/// mensagem é texto do autor do dialeto, nunca dado lido do banco nem a string de conexão.
/// </summary>
/// <remarks>
/// O motor decide a saída pelo tipo, nunca pelo código. Sem construtor com a exceção interna: a do driver pode trazer a
/// string de conexão ou valores. Qualquer outra exceção do dialeto é falha de execução (saída 1).
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class DialetoRecusouException : IncognitoException
{
    /// <summary>Cria a recusa.</summary>
    /// <param name="codigo">Código estável do erro, na faixa do dialeto, como <c>INC3102</c>.</param>
    /// <param name="mensagem">O que o dialeto recusou e o que fazer, sem dado lido nem a string de conexão.</param>
    public DialetoRecusouException(string codigo, string mensagem)
        : base(codigo, mensagem)
    {
    }
}
