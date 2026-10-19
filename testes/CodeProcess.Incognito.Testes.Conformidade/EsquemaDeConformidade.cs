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
    /// Toda tabela e toda sequence do esquema de conformidade (fora o esquema <c>incognito</c>), escritas à mão a partir
    /// do script que cria o esquema, nunca copiadas da saída do leitor: a suíte as compara com a leitura da origem e do
    /// destino.
    /// </summary>
    public required Inventario Inventario { get; init; }

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

/// <summary>O inventário do esquema de conformidade.</summary>
/// <param name="Tabelas">Todas as tabelas, em qualquer ordem.</param>
/// <param name="Sequences">Todas as sequences, com as colunas que as usam.</param>
public sealed record Inventario(IReadOnlyList<TabelaInventariada> Tabelas, IReadOnlyList<SequenceEsperada> Sequences);

/// <summary>
/// Uma tabela do inventário: a estrutura que o modelo tem e os nomes dos objetos filhos. O que é semântico (a situação do
/// tipo, por exemplo) tem fato próprio sobre casos escolhidos.
/// </summary>
public sealed class TabelaInventariada
{
    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get; init; }

    /// <summary>O tipo (padrão: comum).</summary>
    public TipoTabela Tipo { get; init; }

    /// <summary>A particionada ou a primeira mãe.</summary>
    public NomeObjeto? Pai { get; init; }

    /// <summary>As outras mães, na ordem da declaração.</summary>
    public IReadOnlyList<NomeObjeto> OutrosPais { get; init; } = [];

    /// <summary>O histórico de uma temporal.</summary>
    public NomeObjeto? Historico { get; init; }

    /// <summary>A chave de partição; nula se a tabela não é particionada.</summary>
    public ChaveParticaoEsperada? ChaveParticao { get; init; }

    /// <summary>Se é estrangeira.</summary>
    public bool Estrangeira { get; init; }

    /// <summary>A extensão que adotou a tabela.</summary>
    public string? Extensao { get; init; }

    /// <summary>Se tem segurança por linha.</summary>
    public bool SegurancaDeLinha { get; init; }

    /// <summary>As colunas, na ordem do catálogo.</summary>
    public required IReadOnlyList<ColunaInventariada> Colunas { get; init; }

    /// <summary>A chave primária, na ordem da chave.</summary>
    public IReadOnlyList<string> ChavePrimaria { get; init; } = [];

    /// <summary>As chaves estrangeiras, em qualquer ordem.</summary>
    public IReadOnlyList<ChaveEstrangeiraInventariada> ChavesEstrangeiras { get; init; } = [];

    /// <inheritdoc/>
    public override string ToString() => Nome.ToString();
}

/// <summary>Uma coluna do inventário.</summary>
public sealed class ColunaInventariada
{
    /// <summary>O nome.</summary>
    public required string Nome { get; init; }

    /// <summary>O tipo como o banco o nomeia.</summary>
    public required string TipoNativo { get; init; }

    /// <summary>Se aceita NULL (padrão: sim).</summary>
    public bool Anulavel { get; init; } = true;

    /// <summary>A identity.</summary>
    public IdentidadeColuna Identidade { get; init; }

    /// <summary>A geração.</summary>
    public GeracaoColuna Geracao { get; init; }

    /// <summary>O tipo do fim da cadeia de domínios.</summary>
    public string? TipoBase { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Nome;
}

/// <summary>Uma chave estrangeira do inventário.</summary>
/// <param name="Nome">O nome da restrição.</param>
/// <param name="Colunas">As colunas, na ordem da chave.</param>
/// <param name="Referenciada">A tabela referenciada.</param>
/// <param name="ColunasReferenciadas">As colunas referenciadas, na mesma ordem.</param>
public sealed record ChaveEstrangeiraInventariada(string Nome, IReadOnlyList<string> Colunas, NomeObjeto Referenciada, IReadOnlyList<string> ColunasReferenciadas);

/// <summary>Uma chave de partição.</summary>
/// <param name="Colunas">As colunas citadas, na ordem do modelo.</param>
/// <param name="ColunasIncompletas">Se a chave cita mais do que as colunas.</param>
public sealed record ChaveParticaoEsperada(IReadOnlyList<string> Colunas, bool ColunasIncompletas = false);

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
