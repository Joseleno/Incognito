using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// O que a fixture de um dialeto declara sobre o esquema de conformidade que ela cria: cada caso do catálogo, com o que
/// se espera ler ou por que o recurso não existe naquele banco e versão. Toda propriedade é obrigatória: um caso novo
/// no catálogo obriga todo dialeto a declará-lo.
/// </summary>
/// <remarks>
/// O esquema criado pela fixture também tem nomes que só diferem na caixa e o esquema <c>incognito</c> com o objeto
/// declarado em <see cref="ObjetoDoIncognito"/>, que o leitor nunca devolve.
/// </remarks>
public sealed class EsquemaDeConformidade
{
    /// <summary>
    /// Colunas que cobrem a matriz de tipos (ao menos uma suportada, uma com ressalva e uma suportada por exclusão),
    /// com a situação e o tipo canônico esperados.
    /// </summary>
    public required IReadOnlyList<ColunaEsperada> Tipos { get; init; }

    /// <summary>Uma tabela particionada e as partições dela.</summary>
    public required Caso<Hierarquia> Particao { get; init; }

    /// <summary>Uma mãe de herança clássica e as herdeiras.</summary>
    public required Caso<Hierarquia> Heranca { get; init; }

    /// <summary>Uma sequence usada por colunas de mais de uma tabela.</summary>
    public required Caso<SequenceEsperada> SequenceCompartilhada { get; init; }

    /// <summary>Todas as colunas geradas do esquema, com a forma de geração.</summary>
    public required Caso<IReadOnlyList<ColunaGeradaEsperada>> ColunasGeradas { get; init; }

    /// <summary>Uma chave estrangeira de mais de uma coluna, com a ordem da chave diferente da ordem das colunas.</summary>
    public required ChaveEstrangeiraEsperada ChaveEstrangeiraComposta { get; init; }

    /// <summary>Uma tabela temporal e o histórico dela.</summary>
    public required Caso<TemporalEsperada> Temporal { get; init; }

    /// <summary>Todos os objetos residuais do esquema (vazia se o banco não tem nenhum).</summary>
    public required IReadOnlyList<ObjetoResidualEsperado> ObjetosResiduais { get; init; }

    /// <summary>A única tabela com segurança por linha.</summary>
    public required Caso<NomeObjeto> SegurancaDeLinha { get; init; }

    /// <summary>A única coluna com máscara dinâmica.</summary>
    public required Caso<ReferenciaColuna> Mascara { get; init; }

    /// <summary>Tabelas cujo nome o leitor tem de devolver exatamente (só caixa diferente, palavra reservada, espaço).</summary>
    public required IReadOnlyList<NomeObjeto> NomesExatos { get; init; }

    /// <summary>Identificadores e a forma citada que o banco espera (na origem e no destino).</summary>
    public required IReadOnlyList<IdentificadorCitado> Citacoes { get; init; }

    /// <summary>Uma tabela que a fixture cria no esquema <c>incognito</c> (como o diário), para a suíte conferir que o leitor a deixa de fora.</summary>
    public required NomeObjeto ObjetoDoIncognito { get; init; }
}

/// <summary>Uma coluna da matriz de tipos.</summary>
/// <param name="Coluna">A coluna.</param>
/// <param name="Situacao">A situação esperada.</param>
/// <param name="Tipo">O tipo canônico esperado (nulo fora de <see cref="SituacaoTipo.Suportado"/>).</param>
public sealed record ColunaEsperada(ReferenciaColuna Coluna, SituacaoTipo Situacao, TipoColuna? Tipo);

/// <summary>Uma tabela e as filhas (partições ou herdeiras).</summary>
/// <param name="Pai">A particionada ou a mãe.</param>
/// <param name="Filhas">As partições ou herdeiras.</param>
public sealed record Hierarquia(NomeObjeto Pai, IReadOnlyList<NomeObjeto> Filhas);

/// <summary>Uma sequence e as colunas que a usam.</summary>
/// <param name="Nome">A sequence.</param>
/// <param name="Colunas">As colunas, em qualquer ordem.</param>
public sealed record SequenceEsperada(NomeObjeto Nome, IReadOnlyList<ReferenciaColuna> Colunas);

/// <summary>Uma coluna gerada.</summary>
/// <param name="Coluna">A coluna.</param>
/// <param name="Geracao">Armazenada ou virtual.</param>
public sealed record ColunaGeradaEsperada(ReferenciaColuna Coluna, GeracaoColuna Geracao);

/// <summary>Uma chave estrangeira.</summary>
/// <param name="Tabela">A tabela que referencia.</param>
/// <param name="Nome">O nome da restrição.</param>
/// <param name="Colunas">As colunas, na ordem da chave.</param>
/// <param name="Referenciada">A tabela referenciada.</param>
/// <param name="ColunasReferenciadas">As colunas referenciadas, na mesma ordem.</param>
public sealed record ChaveEstrangeiraEsperada(
    NomeObjeto Tabela, string Nome, IReadOnlyList<string> Colunas, NomeObjeto Referenciada, IReadOnlyList<string> ColunasReferenciadas);

/// <summary>Uma temporal e o histórico.</summary>
/// <param name="Tabela">A temporal.</param>
/// <param name="Historico">O histórico.</param>
public sealed record TemporalEsperada(NomeObjeto Tabela, NomeObjeto Historico);

/// <summary>Um objeto residual.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Tipo">O tipo.</param>
public sealed record ObjetoResidualEsperado(NomeObjeto Nome, TipoObjetoResidual Tipo);

/// <summary>Um identificador e a forma citada.</summary>
/// <param name="Identificador">O identificador, como o catálogo o guarda.</param>
/// <param name="Citado">A forma citada esperada.</param>
public sealed record IdentificadorCitado(string Identificador, string Citado);
