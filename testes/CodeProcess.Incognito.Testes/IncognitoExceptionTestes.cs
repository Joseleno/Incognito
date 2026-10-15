namespace CodeProcess.Incognito.Testes;

/// <summary>A exceção do Incognito guarda o código e, quando há, a exceção de origem.</summary>
public sealed class IncognitoExceptionTestes
{
    [Fact]
    public void Codigo_mensagem_e_excecao_de_origem_sao_guardados()
    {
        var origem = new InvalidOperationException("origem");

        var comOrigem = new IncognitoException("INC1001", "mensagem", origem);
        var semOrigem = new IncognitoException("INC1002", "outra");

        Assert.Equal("INC1001", comOrigem.Codigo);
        Assert.Equal("mensagem", comOrigem.Message);
        Assert.Same(origem, comOrigem.InnerException);
        Assert.Equal("INC1002", semOrigem.Codigo);
        Assert.Equal("outra", semOrigem.Message);
        Assert.Null(semOrigem.InnerException);
    }
}
