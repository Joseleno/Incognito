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
    /// Toda tabela, toda sequence e todo objeto residual do esquema de conformidade (fora o esquema <c>incognito</c>),
    /// escritos à mão a partir do script que cria o esquema, nunca copiados da saída do leitor: a suíte os compara com a
    /// leitura da origem e do destino.
    /// </summary>
    public required Inventario Inventario { get; init; }

    /// <summary>
    /// Os valores da matriz de tipos (situação, tipo canônico e unidade de comprimento) que nenhuma coluna do inventário
    /// tem, cada um com o motivo (por exemplo, "o PostgreSQL não mede em UTF-16"). Todo outro valor precisa aparecer em
    /// alguma coluna.
    /// </summary>
    public required IReadOnlyDictionary<Enum, string> Inexistentes { get; init; }

    /// <summary>A versão, a codificação e a colação padrão do banco, iguais nos dois lados.</summary>
    public required Caso<PropriedadesEsperadas> Propriedades { get; init; }

    /// <summary>
    /// Os textos que o esquema de conformidade guarda em todo lugar opaco (default, <c>CHECK</c>, expressão, comentário,
    /// locale, linhas) e nas senhas: nenhuma leitura pode devolvê-los, nem em bytes.
    /// </summary>
    public required IReadOnlyList<string> Sentinelas { get; init; }

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
/// <param name="Residuais">Todos os objetos residuais (vazia se o banco não tem nenhum).</param>
public sealed record Inventario(IReadOnlyList<TabelaInventariada> Tabelas, IReadOnlyList<SequenceEsperada> Sequences, IReadOnlyList<ResidualInventariado> Residuais);

/// <summary>Uma tabela do inventário: tudo o que o modelo diz dela e das colunas.</summary>
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

/// <summary>
/// Uma coluna do inventário. A situação e o tipo canônico vêm da matriz de tipos lida à mão a partir do script, nunca da
/// classificação do dialeto. Os campos semânticos têm padrão nulo, mas são sempre comparados: um limite que o leitor
/// devolve e a declaração esquece falha.
/// </summary>
public sealed class ColunaInventariada
{
    /// <summary>O nome.</summary>
    public required string Nome { get; init; }

    /// <summary>O tipo como o banco o nomeia.</summary>
    public required string TipoNativo { get; init; }

    /// <summary>A situação na matriz de tipos.</summary>
    public required SituacaoTipo Situacao { get; init; }

    /// <summary>O tipo canônico (nulo fora de suportado e nos suportados sem representação canônica).</summary>
    public required TipoColuna? Tipo { get; init; }

    /// <summary>Se aceita NULL (padrão: sim).</summary>
    public bool Anulavel { get; init; } = true;

    /// <summary>A identity.</summary>
    public IdentidadeColuna Identidade { get; init; }

    /// <summary>A geração.</summary>
    public GeracaoColuna Geracao { get; init; }

    /// <summary>O tipo do fim da cadeia de domínios.</summary>
    public string? TipoBase { get; init; }

    /// <summary>A colação efetiva.</summary>
    public ColacaoEsperada? Colacao { get; init; }

    /// <summary>Se o tipo compara sem a caixa.</summary>
    public bool? IgnoraCaixaPeloTipo { get; init; }

    /// <summary>Se o tipo compara sem os espaços finais.</summary>
    public bool? IgnoraEspacoFinalPeloTipo { get; init; }

    /// <summary>O comprimento declarado.</summary>
    public LimiteEsperado? Comprimento { get; init; }

    /// <summary>A precisão do decimal.</summary>
    public int? Precisao { get; init; }

    /// <summary>A escala do decimal.</summary>
    public int? Escala { get; init; }

    /// <summary>A faixa do inteiro.</summary>
    public FaixaEsperada? Faixa { get; init; }

    /// <summary>As colunas de que a coluna gerada depende.</summary>
    public ReferenciasEsperadas? ReferenciasDaGeracao { get; init; }

    /// <summary>Se uma restrição do tipo vale para a coluna.</summary>
    public bool? RestritaPeloTipo { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Nome;
}

/// <summary>Uma colação.</summary>
/// <param name="Nome">O nome no catálogo.</param>
/// <param name="Esquema">O esquema (nulo na padrão).</param>
/// <param name="Deterministica">Se é determinística.</param>
/// <param name="TemRegras">Se tem regras do usuário.</param>
/// <param name="IgnoraCaixa">Se iguala caixa (nulo: não medido).</param>
/// <param name="IgnoraAcento">Se iguala acento (nulo: não medido).</param>
/// <param name="IgnoraPontuacao">Se iguala pontuação (nulo: não medido).</param>
public sealed record ColacaoEsperada(
    string Nome, string? Esquema, bool Deterministica, bool TemRegras, bool? IgnoraCaixa, bool? IgnoraAcento, bool? IgnoraPontuacao)
{
    /// <summary>Uma colação determinística (as três marcas falsas).</summary>
    /// <param name="nome">O nome.</param>
    /// <param name="esquema">O esquema.</param>
    /// <param name="temRegras">Se tem regras.</param>
    /// <returns>A colação.</returns>
    public static ColacaoEsperada Determinista(string nome, string? esquema, bool temRegras = false) => new(nome, esquema, true, temRegras, false, false, false);

    /// <summary>A colação lida, na forma da declaração.</summary>
    /// <param name="colacao">A colação lida.</param>
    /// <returns>A projeção; nula se a colação é nula.</returns>
    public static ColacaoEsperada? De(ColacaoEsquema? colacao) => colacao is null
        ? null
        : new(colacao.Nome, colacao.Esquema, colacao.Deterministica, colacao.TemRegras, colacao.IgnoraCaixa, colacao.IgnoraAcento, colacao.IgnoraPontuacao);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(Esquema is null ? "" : Esquema + ".")}{Nome} ({(Deterministica ? "determinística" : "não determinística")}{(TemRegras ? ", com regras" : "")}; caixa {Marca(IgnoraCaixa)}, acento {Marca(IgnoraAcento)}, pontuação {Marca(IgnoraPontuacao)})";

    private static string Marca(bool? marca) => marca switch { null => "?", true => "sim", false => "não" };
}

/// <summary>Um limite de comprimento.</summary>
/// <param name="Valor">O limite (nulo: sem limite declarado).</param>
/// <param name="Unidade">A unidade.</param>
public sealed record LimiteEsperado(int? Valor, UnidadeComprimento Unidade)
{
    /// <summary>O limite lido, na forma da declaração.</summary>
    /// <param name="limite">O limite lido.</param>
    /// <returns>A projeção; nula se o limite é nulo.</returns>
    public static LimiteEsperado? De(LimiteComprimento? limite) => limite is null ? null : new(limite.Valor, limite.Unidade);

    /// <inheritdoc/>
    public override string ToString() => $"{(Valor is { } valor ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture) : "sem limite")} em {Unidade}";
}

/// <summary>A faixa de um inteiro.</summary>
/// <param name="Minimo">O menor valor.</param>
/// <param name="Maximo">O maior valor.</param>
public sealed record FaixaEsperada(long Minimo, long Maximo)
{
    /// <summary>A faixa do <c>smallint</c>.</summary>
    public static FaixaEsperada Pequeno { get; } = new(short.MinValue, short.MaxValue);

    /// <summary>A faixa do <c>integer</c>.</summary>
    public static FaixaEsperada Inteiro { get; } = new(int.MinValue, int.MaxValue);

    /// <summary>A faixa do <c>bigint</c>.</summary>
    public static FaixaEsperada Grande { get; } = new(long.MinValue, long.MaxValue);

    /// <summary>A faixa lida, na forma da declaração.</summary>
    /// <param name="faixa">A faixa lida.</param>
    /// <returns>A projeção; nula se a faixa é nula.</returns>
    public static FaixaEsperada? De(FaixaInteira? faixa) => faixa is null ? null : new(faixa.Minimo, faixa.Maximo);

    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant($"[{Minimo}, {Maximo}]");
}

/// <summary>As colunas que uma expressão cita.</summary>
/// <param name="Colunas">As colunas, na ordem da tabela.</param>
/// <param name="Incompletas">Se cita mais do que as colunas.</param>
public sealed record ReferenciasEsperadas(IReadOnlyList<string> Colunas, bool Incompletas = false)
{
    /// <summary>As referências lidas, na forma da declaração.</summary>
    /// <param name="referencias">As referências lidas.</param>
    /// <returns>A projeção; nula se as referências são nulas.</returns>
    public static ReferenciasEsperadas? De(ReferenciasDaExpressao? referencias) => referencias is null ? null : new(referencias.Colunas, referencias.Incompletas);

    /// <inheritdoc/>
    public override string ToString() => $"({string.Join(", ", Colunas)}{(Incompletas ? "; incompletas" : "")})";
}

/// <summary>O que vale para o banco inteiro.</summary>
/// <param name="Versao">A versão do servidor, na precisão que o dialeto informa.</param>
/// <param name="Codificacao">A codificação.</param>
/// <param name="Colacao">A colação padrão.</param>
public sealed record PropriedadesEsperadas(Version Versao, CodificacaoEsperada Codificacao, ColacaoEsperada Colacao)
{
    /// <summary>As propriedades lidas, na forma da declaração.</summary>
    /// <param name="propriedades">As propriedades lidas.</param>
    /// <returns>A projeção; nula se as propriedades são nulas.</returns>
    public static PropriedadesEsperadas? De(PropriedadesBanco? propriedades) => propriedades is null
        ? null
        : new(
            propriedades.Versao,
            new(propriedades.Codificacao.Nome, propriedades.Codificacao.Definida, propriedades.Codificacao.Unicode, propriedades.Codificacao.BytesPorCaractereMaximo),
            ColacaoEsperada.De(propriedades.Colacao)!);

    /// <inheritdoc/>
    public override string ToString() => $"versão {Versao}, {Codificacao}, colação {Colacao}";
}

/// <summary>A codificação do banco.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Definida">Se é declarada.</param>
/// <param name="Unicode">Se tem o repertório Unicode completo.</param>
/// <param name="BytesPorCaractereMaximo">Bytes por caractere no máximo.</param>
public sealed record CodificacaoEsperada(string Nome, bool Definida, bool Unicode, int BytesPorCaractereMaximo);

/// <summary>Um objeto residual do inventário.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Tipo">O tipo.</param>
public sealed record ResidualInventariado(NomeObjeto Nome, TipoObjetoResidual Tipo);

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

/// <summary>Um identificador e a forma citada.</summary>
/// <param name="Identificador">O identificador, como o catálogo o guarda.</param>
/// <param name="Citado">A forma citada esperada.</param>
public sealed record IdentificadorCitado(string Identificador, string Citado);
