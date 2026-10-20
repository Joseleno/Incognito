using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

// Os objetos de uma tabela que o planejar confere contra os pseudônimos: os índices únicos (a injetividade), os CHECKs e
// as exclusões (o pseudônimo pode violá-los) e as triggers (disparam na carga). Só nomes de catálogo, as colunas citadas e
// marcas; nunca a expressão, o predicado, o corpo nem os argumentos.

/// <summary>Um elemento da chave de um índice único: uma coluna ou uma expressão, com a colação do índice.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ElementoIndice
{
    private readonly string? _coluna;

    /// <summary>
    /// A coluna; nula num elemento de expressão (as colunas que a expressão cita ficam em
    /// <see cref="IndiceUnico.ReferenciasDasExpressoes"/>). No SQL Server, a expressão mora numa coluna computada: o
    /// elemento vem com a coluna, e as colunas base ficam em <see cref="ColunaEsquema.ReferenciasDaGeracao"/>.
    /// </summary>
    public string? Coluna { get => _coluna; init => _coluna = value is null ? null : Lista.Texto(value, nameof(Coluna)); }

    /// <summary>
    /// A colação do índice neste elemento, que pode diferir da coluna (num índice único criado à parte, com
    /// <c>COLLATE</c>). Nula: sem colação ou não informada; num elemento de coluna de texto, também de tipo canônico nulo,
    /// e num elemento de expressão (o modelo não diz o tipo da expressão), o <c>planejar</c> trata como não
    /// determinística. Uma classe de operadores que não é a padrão pode definir outra igualdade.
    /// </summary>
    public ColacaoEsquema? Colacao { get; init; }
}

/// <summary>
/// Um índice ou uma restrição que declara unicidade, inclusive a chave primária e o índice inválido ou em construção: o
/// motor exige que os pseudônimos não colidam em todo índice listado.
/// </summary>
/// <remarks>
/// A chave primária ou única com sobreposição proibida (<c>WITHOUT OVERLAPS</c>) fica em
/// <see cref="TabelaEsquema.RestricoesExclusao"/>. Numa particionada, só os índices do pai e os declarados direto na
/// partição: o índice da partição anexado ao do pai fica no pai. Uma classe de operadores que não é a padrão pode definir
/// outra igualdade, mesmo com colação determinística.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class IndiceUnico
{
    private readonly string _nome = null!;
    private readonly IReadOnlyList<ElementoIndice> _elementos = [];

    /// <summary>O nome do índice.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>Se é o índice da chave primária.</summary>
    public required bool Primario { get; init; }

    /// <summary>Os elementos da chave, na ordem dela, sem as colunas só incluídas (<c>INCLUDE</c>).</summary>
    /// <exception cref="ArgumentException">Lista vazia.</exception>
    public required IReadOnlyList<ElementoIndice> Elementos
    {
        get => _elementos;
        init
        {
            var copia = Lista.Copiar(value, nameof(Elementos));
            _elementos = copia.Count > 0 ? copia : throw new ArgumentException("O índice precisa de ao menos um elemento.", nameof(Elementos));
        }
    }

    /// <summary>
    /// As colunas que os elementos de expressão citam, juntas, sem dizer qual elemento cita qual; nulo se o índice não tem
    /// elemento de expressão (no SQL Server, sempre: a expressão mora numa coluna computada).
    /// </summary>
    public ReferenciasDaExpressao? ReferenciasDasExpressoes { get; init; }

    /// <summary>As colunas que o predicado de um índice parcial cita; nulo se o índice não é parcial.</summary>
    public ReferenciasDaExpressao? Predicado { get; init; }

    /// <summary>
    /// <c>false</c> quando o índice aceita um NULL só por combinação de valores (<c>NULLS NOT DISTINCT</c> no PostgreSQL;
    /// sempre no SQL Server).
    /// </summary>
    public required bool NulosDistintos { get; init; }
}

/// <summary>Um <c>CHECK</c> da tabela: o pseudônimo de uma coluna citada pode violá-lo.</summary>
/// <remarks>
/// Numa partição, só os declarados nela (os do pai valem pelo pai); numa herdeira, todos. Inclui os não validados, que
/// valem para as linhas novas.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class RestricaoCheck
{
    private readonly string _nome = null!;
    private readonly ReferenciasDaExpressao _referencias = null!;

    /// <summary>O nome da restrição.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>As colunas que a restrição cita; sem colunas e sem <see cref="ReferenciasDaExpressao.Incompletas"/>, uma restrição constante.</summary>
    public required ReferenciasDaExpressao Referencias { get => _referencias; init => _referencias = value ?? throw new ArgumentNullException(nameof(Referencias)); }

    /// <summary>
    /// <c>false</c> quando o banco não a confere (<c>NOT ENFORCED</c> no PostgreSQL 18, desabilitada no SQL Server): não
    /// recusa linha.
    /// </summary>
    public required bool Imposta { get; init; }
}

/// <summary>
/// Uma restrição de exclusão (inclusive a chave primária ou única com sobreposição proibida): dois pseudônimos podem
/// passar a se excluir.
/// </summary>
/// <remarks>
/// O operador não é modelado. Numa particionada, só a do pai: a cópia que o banco cria em cada partição fica no pai. O SQL
/// Server não tem restrição de exclusão: a lista vem vazia.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class RestricaoExclusao
{
    private readonly string _nome = null!;
    private readonly ReferenciasDaExpressao _referencias = null!;

    /// <summary>O nome da restrição.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>As colunas que a chave (sem as só incluídas), as expressões e o predicado citam.</summary>
    public required ReferenciasDaExpressao Referencias { get => _referencias; init => _referencias = value ?? throw new ArgumentNullException(nameof(Referencias)); }
}

/// <summary>Uma trigger de usuário da tabela (nunca as internas do banco, de chave estrangeira e de unicidade).</summary>
/// <remarks>O estado exato (réplica, sempre) não é modelado: o diário do dialeto o guarda para restaurar.</remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class TriggerEsquema
{
    private readonly string _nome = null!;

    /// <summary>O nome.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>Se dispara numa sessão comum (no PostgreSQL, habilitada ou sempre; não desabilitada nem só de réplica).</summary>
    public required bool Ligada { get; init; }

    /// <summary>Se é a cópia, numa partição, de uma trigger da particionada; sempre <c>false</c> no SQL Server.</summary>
    public required bool Herdada { get; init; }
}
