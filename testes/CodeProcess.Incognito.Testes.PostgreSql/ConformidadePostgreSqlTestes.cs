using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>A suíte de conformidade inteira, no PostgreSQL.</summary>
/// <param name="fixture">A fixture.</param>
public sealed class ConformidadePostgreSqlTestes(FixturePostgreSql fixture) : ConformidadeDialetoTestes<FixturePostgreSql>(fixture);
