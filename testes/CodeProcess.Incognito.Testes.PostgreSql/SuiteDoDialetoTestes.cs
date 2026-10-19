using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A classe da suíte e a categoria dos testes, conferidas no assembly compilado: a regra de arquitetura lê o texto, e um
/// <c>&lt;Compile Remove&gt;</c>, um comentário ou um atributo de teste derivado a enganariam.
/// </summary>
public sealed class SuiteDoDialetoTestes
{
    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void O_assembly_tem_a_classe_da_suite_e_so_testes_de_banco() => ConferenciaDaSuite.Conferir(typeof(SuiteDoDialetoTestes).Assembly);
}
