namespace CodeProcess.Incognito.Testes;

/// <summary>A variável da mutação noturna só vale no workflow <c>noturno</c>.</summary>
public sealed class MutacaoTestes
{
    [Fact]
    public void No_CI_a_mutacao_so_vale_no_workflow_noturno()
    {
        // Fora do noturno, a variável encolheria ou pularia os testes pesados sem aviso, em qualquer job que rode a suíte.
        var noCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var workflow = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW");

        Assert.False(noCi && Mutacao.Ativa && workflow != "noturno", "INCOGNITO_MUTACAO definida fora do workflow noturno.");
    }
}
