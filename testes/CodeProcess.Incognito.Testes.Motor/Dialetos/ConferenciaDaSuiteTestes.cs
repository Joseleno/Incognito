using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>A conferência do assembly de testes de um dialeto recusa cada desvio (a aceitação roda no próprio dialeto).</summary>
public sealed class ConferenciaDaSuiteTestes
{
    [Fact]
    public void Sem_classe_da_suite_e_recusado() =>
        Assert.Contains(ConferenciaDaSuite.Problemas([typeof(ConferenciaDaSuiteTestes)]), problema => problema.StartsWith("0 classes concretas derivadas da suíte", StringComparison.Ordinal));

    [Fact]
    public void Classe_interna_e_recusada() =>
        Assert.Contains(ConferenciaDaSuite.Problemas([typeof(SuiteFalsa)]), problema => problema.Contains("SuiteFalsa precisa ser pública", StringComparison.Ordinal));

    [Fact]
    public void Classe_aninhada_e_recusada() =>
        Assert.Contains(ConferenciaDaSuite.Problemas([typeof(SuiteDeConformidadeTestes.SuiteAninhada)]), problema => problema.Contains("SuiteAninhada precisa ser pública", StringComparison.Ordinal));

    [Fact]
    public void Classe_aberta_com_derivada_e_recusada()
    {
        var problemas = ConferenciaDaSuite.Problemas([typeof(SuiteAberta), typeof(SuiteDerivadaDaAberta)]).ToArray();

        Assert.Contains(problemas, problema => problema.StartsWith("2 classes concretas derivadas da suíte", StringComparison.Ordinal));
        Assert.Contains(problemas, problema => problema.Contains("SuiteDerivadaDaAberta precisa ser pública", StringComparison.Ordinal));
    }

    [Fact]
    public void Teste_sem_a_categoria_de_banco_e_recusado() =>
        Assert.Contains(ConferenciaDaSuite.Problemas([typeof(ConferenciaDaSuiteTestes)]), problema => problema.Contains("Teste_sem_a_categoria_de_banco_e_recusado não tem a categoria de banco", StringComparison.Ordinal));
}
