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

    /// <summary>As tabelas com segurança por linha.</summary>
    public required Caso<IReadOnlyList<NomeObjeto>> SegurancaDeLinha { get; init; }

    /// <summary>
    /// As tabelas publicadas para replicação (a publicação replica a inserção ou o esvaziamento delas), com uma partição
    /// publicada sozinha quando o banco tem partição como tabela.
    /// </summary>
    public required Caso<IReadOnlyList<NomeObjeto>> Publicacao { get; init; }

    /// <summary>As tabelas que uma assinatura de replicação do próprio banco lista, ligada ou não.</summary>
    public required Caso<IReadOnlyList<NomeObjeto>> Assinatura { get; init; }

    /// <summary>A única coluna com máscara dinâmica.</summary>
    public required Caso<ReferenciaColuna> Mascara { get; init; }

    /// <summary>Tabelas cujo nome o leitor tem de devolver exatamente (só caixa diferente, palavra reservada, espaço).</summary>
    public required IReadOnlyList<NomeObjeto> NomesExatos { get; init; }

    /// <summary>Identificadores e a forma citada que o banco espera (na origem e no destino).</summary>
    public required IReadOnlyList<IdentificadorCitado> Citacoes { get; init; }

    /// <summary>
    /// Nomes escritos (com ou sem aspas), nomes do catálogo e a correspondência que o banco da conexão decide: a dobra de
    /// caixa e o limite do tamanho dependem do banco.
    /// </summary>
    public required IReadOnlyList<NomeComparado> ComparacoesDeNome { get; init; }

    /// <summary>Uma tabela que a fixture cria no esquema <c>incognito</c> (como o diário), para a suíte conferir que o leitor a deixa de fora.</summary>
    public required NomeObjeto ObjetoDoIncognito { get; init; }

    /// <summary>As permissões do leitor da origem e do dono do destino.</summary>
    public required Caso<AcessoEsperado> Acesso { get; init; }

    /// <summary>O transporte das conexões da fixture, igual nos dois lados.</summary>
    public required Caso<TransporteEsperado> Transporte { get; init; }

    /// <summary>
    /// O maior valor de colunas inteiras da origem, como a cópia lê: o de uma tabela comum, o de uma coluna sem valor
    /// (nulo), o de uma coluna só com negativos, o da raiz de uma particionada (o maior numa partição) e o da mãe de herança
    /// clássica (sem as herdeiras, que têm valor maior); e uma coluna de texto com o sentinela nas linhas, que o dialeto
    /// recusa sem trazer o valor.
    /// </summary>
    public required MaioresValoresEsperados MaioresValores { get; init; }

    /// <summary>
    /// As tabelas do destino para a existência de linhas, como a cópia lê: uma com linha, uma vazia, a raiz de uma
    /// particionada com linhas só nas partições (tem), a mãe de herança clássica com linha só na herdeira (não tem) e uma
    /// tabela com a segurança por linha forçada para o dono (a leitura falha, nunca esconde linhas).
    /// </summary>
    public required LinhasNoDestino Linhas { get; init; }
}

/// <summary>O inventário do esquema de conformidade.</summary>
/// <param name="Tabelas">Todas as tabelas, em qualquer ordem.</param>
/// <param name="Sequences">Todas as sequences, com as colunas que as usam.</param>
/// <param name="Residuais">Todos os objetos residuais (vazia se o banco não tem nenhum).</param>
/// <param name="ObjetosNoEsquemaReservado">Os objetos do esquema <c>incognito</c>, em ordem ordinal de nome.</param>
public sealed record Inventario(
    IReadOnlyList<TabelaInventariada> Tabelas, IReadOnlyList<SequenceEsperada> Sequences, IReadOnlyList<ResidualInventariado> Residuais, IReadOnlyList<NomeObjeto> ObjetosNoEsquemaReservado);

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

    /// <summary>Se está publicada (padrão: não está; nulo: o dialeto não informa).</summary>
    public bool? Publicada { get; init; } = false;

    /// <summary>Se está numa assinatura (padrão: não está; nulo: o dialeto não informa).</summary>
    public bool? Assinada { get; init; } = false;

    /// <summary>As colunas, na ordem do catálogo.</summary>
    public required IReadOnlyList<ColunaInventariada> Colunas { get; init; }

    /// <summary>A chave primária, na ordem da chave.</summary>
    public IReadOnlyList<string> ChavePrimaria { get; init; } = [];

    /// <summary>As chaves estrangeiras, em qualquer ordem.</summary>
    public IReadOnlyList<ChaveEstrangeiraInventariada> ChavesEstrangeiras { get; init; } = [];

    // Os objetos da tabela, inteiros: um índice que falta deixaria a injetividade sem conferência. Declarados em qualquer
    // ordem; a leitura tem de vir na ordem ordinal de nome.

    /// <summary>Os índices únicos, inclusive o da chave primária.</summary>
    public IReadOnlyList<IndiceInventariado> IndicesUnicos { get; init; } = [];

    /// <summary>Os <c>CHECK</c>s.</summary>
    public IReadOnlyList<CheckInventariado> Checks { get; init; } = [];

    /// <summary>As restrições de exclusão.</summary>
    public IReadOnlyList<ExclusaoInventariada> Exclusoes { get; init; } = [];

    /// <summary>As triggers de usuário.</summary>
    public IReadOnlyList<TriggerInventariada> Triggers { get; init; } = [];

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

    /// <summary>Se tem máscara dinâmica.</summary>
    public bool Mascarada { get; init; }

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
/// <param name="DependenciasIncompletas">Se a leitura passa por função (nulo fora de materialized view).</param>
/// <param name="Populada">Se tem dados (nulo fora de materialized view).</param>
public sealed record ResidualInventariado(NomeObjeto Nome, TipoObjetoResidual Tipo, bool? DependenciasIncompletas = null, bool? Populada = null)
{
    /// <summary>As materialized views que ele lê, na ordem ordinal de esquema e nome.</summary>
    public IReadOnlyList<NomeObjeto> DependeDe { get; init; } = [];
}

/// <summary>Uma chave estrangeira do inventário.</summary>
/// <param name="Nome">O nome da restrição.</param>
/// <param name="Colunas">As colunas, na ordem da chave.</param>
/// <param name="Referenciada">A tabela referenciada.</param>
/// <param name="ColunasReferenciadas">As colunas referenciadas, na mesma ordem.</param>
/// <param name="Imposta">Se o banco a confere agora (padrão: sim).</param>
/// <param name="Validada">Se as linhas antigas foram conferidas (padrão: sim).</param>
public sealed record ChaveEstrangeiraInventariada(
    string Nome, IReadOnlyList<string> Colunas, NomeObjeto Referenciada, IReadOnlyList<string> ColunasReferenciadas, bool? Imposta = true, bool? Validada = true);

/// <summary>Um elemento de índice único.</summary>
/// <param name="Coluna">A coluna (nula num elemento de expressão).</param>
/// <param name="Colacao">A colação do índice no elemento.</param>
public sealed record ElementoEsperado(string? Coluna, ColacaoEsperada? Colacao)
{
    /// <summary>O elemento lido, na forma da declaração.</summary>
    /// <param name="elemento">O elemento lido.</param>
    /// <returns>A projeção.</returns>
    public static ElementoEsperado De(ElementoIndice elemento) => new(elemento.Coluna, ColacaoEsperada.De(elemento.Colacao));

    /// <inheritdoc/>
    public override string ToString() => $"{Coluna ?? "(expressão)"} [{Colacao?.ToString() ?? "sem colação"}]";
}

/// <summary>Um índice único do inventário.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Elementos">Os elementos, na ordem da chave.</param>
/// <param name="Primario">Se é o da chave primária.</param>
/// <param name="Expressoes">As colunas das expressões (nulo sem elemento de expressão).</param>
/// <param name="Predicado">As colunas do predicado (nulo se não é parcial).</param>
/// <param name="NulosDistintos">Se cada NULL é distinto (padrão: sim).</param>
public sealed record IndiceInventariado(
    string Nome, IReadOnlyList<ElementoEsperado> Elementos, bool Primario = false, ReferenciasEsperadas? Expressoes = null, ReferenciasEsperadas? Predicado = null,
    bool NulosDistintos = true)
{
    /// <summary>O índice lido, na forma da declaração.</summary>
    /// <param name="indice">O índice lido.</param>
    /// <returns>A projeção.</returns>
    public static IndiceInventariado De(IndiceUnico indice) => new(
        indice.Nome, [.. indice.Elementos.Select(ElementoEsperado.De)], indice.Primario, ReferenciasEsperadas.De(indice.ReferenciasDasExpressoes),
        ReferenciasEsperadas.De(indice.Predicado), indice.NulosDistintos);

    /// <inheritdoc/>
    public override string ToString() =>
        $"({string.Join(", ", Elementos)}){(Primario ? " primário" : "")}; expressões {Expressoes?.ToString() ?? "nenhuma"}; predicado {Predicado?.ToString() ?? "nenhum"}; nulos {(NulosDistintos ? "distintos" : "não distintos")}";
}

/// <summary>Um <c>CHECK</c> do inventário.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Referencias">As colunas citadas.</param>
/// <param name="Imposta">Se o banco o confere (padrão: sim).</param>
public sealed record CheckInventariado(string Nome, ReferenciasEsperadas Referencias, bool Imposta = true)
{
    /// <summary>O <c>CHECK</c> lido, na forma da declaração.</summary>
    /// <param name="check">O <c>CHECK</c> lido.</param>
    /// <returns>A projeção.</returns>
    public static CheckInventariado De(RestricaoCheck check) => new(check.Nome, ReferenciasEsperadas.De(check.Referencias)!, check.Imposta);

    /// <inheritdoc/>
    public override string ToString() => $"{Referencias}{(Imposta ? "" : " não imposto")}";
}

/// <summary>Uma restrição de exclusão do inventário.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Referencias">As colunas citadas.</param>
public sealed record ExclusaoInventariada(string Nome, ReferenciasEsperadas Referencias)
{
    /// <summary>A exclusão lida, na forma da declaração.</summary>
    /// <param name="exclusao">A exclusão lida.</param>
    /// <returns>A projeção.</returns>
    public static ExclusaoInventariada De(RestricaoExclusao exclusao) => new(exclusao.Nome, ReferenciasEsperadas.De(exclusao.Referencias)!);

    /// <inheritdoc/>
    public override string ToString() => Referencias.ToString();
}

/// <summary>Uma trigger do inventário.</summary>
/// <param name="Nome">O nome.</param>
/// <param name="Ligada">Se dispara numa sessão comum (padrão: sim).</param>
/// <param name="Herdada">Se é a cópia de uma trigger da particionada.</param>
public sealed record TriggerInventariada(string Nome, bool Ligada = true, bool Herdada = false)
{
    /// <summary>A trigger lida, na forma da declaração.</summary>
    /// <param name="trigger">A trigger lida.</param>
    /// <returns>A projeção.</returns>
    public static TriggerInventariada De(TriggerEsquema trigger) => new(trigger.Nome, trigger.Ligada, trigger.Herdada);

    /// <inheritdoc/>
    public override string ToString() => $"{(Ligada ? "ligada" : "desligada")}{(Herdada ? ", herdada" : "")}";
}

/// <summary>As permissões esperadas em cada lado.</summary>
/// <param name="Origem">As do leitor da origem.</param>
/// <param name="Destino">As do dono do destino.</param>
public sealed record AcessoEsperado(LadoDoAcesso Origem, LadoDoAcesso Destino);

/// <summary>
/// As permissões de um login: um padrão para as tabelas, os objetos residuais e as sequences, e as exceções por objeto. Toda
/// tabela, todo objeto residual e toda sequence lidos são comparados com a exceção dele ou com o padrão.
/// </summary>
public sealed class LadoDoAcesso
{
    /// <summary>As permissões no banco.</summary>
    public required PermissoesBancoEsperadas Banco { get; init; }

    /// <summary>As permissões de toda tabela sem exceção.</summary>
    public required PermissoesTabelaEsperadas Tabelas { get; init; }

    /// <summary>As tabelas com outras permissões.</summary>
    public IReadOnlyDictionary<NomeObjeto, PermissoesTabelaEsperadas> ExcecoesDasTabelas { get; init; } = new Dictionary<NomeObjeto, PermissoesTabelaEsperadas>();

    /// <summary>Se o login limpa todo objeto residual sem exceção.</summary>
    public required bool? PodeLimpar { get; init; }

    /// <summary>Os objetos residuais com outra resposta.</summary>
    public IReadOnlyDictionary<NomeObjeto, bool?> ExcecoesDosResiduais { get; init; } = new Dictionary<NomeObjeto, bool?>();

    /// <summary>Se o login ajusta toda sequence sem exceção.</summary>
    public required bool? PodeAjustar { get; init; }

    /// <summary>As sequences com outra resposta.</summary>
    public IReadOnlyDictionary<NomeObjeto, bool?> ExcecoesDasSequences { get; init; } = new Dictionary<NomeObjeto, bool?>();
}

/// <summary>As permissões no banco.</summary>
/// <param name="PodeMarcarBanco">Se marca o banco como destino.</param>
/// <param name="PodeCriarEsquema">Se cria o esquema do diário.</param>
public sealed record PermissoesBancoEsperadas(bool? PodeMarcarBanco, bool? PodeCriarEsquema)
{
    /// <summary>As permissões lidas, na forma da declaração.</summary>
    /// <param name="banco">As permissões lidas.</param>
    /// <returns>A projeção.</returns>
    public static PermissoesBancoEsperadas De(PermissoesBanco banco) => new(banco.PodeMarcarBanco, banco.PodeCriarEsquema);

    /// <inheritdoc/>
    public override string ToString() => $"marca {Marcas.Texto(PodeMarcarBanco)}, cria esquema {Marcas.Texto(PodeCriarEsquema)}";
}

/// <summary>As permissões numa tabela.</summary>
/// <param name="PodeLer">Se lê todas as colunas.</param>
/// <param name="PodeInserir">Se grava as colunas da carga.</param>
/// <param name="PodeGravarIdentidade">Se grava a identidade como veio.</param>
/// <param name="PodeEsvaziar">Se esvazia.</param>
/// <param name="PodeReferenciar">Se cria chave estrangeira para ela.</param>
/// <param name="PodeAlterarRestricoes">Se desliga as restrições.</param>
/// <param name="PodeDesligarTriggers">Se desliga as triggers.</param>
/// <param name="LeituraFiltradaPorLinha">Se a leitura sai filtrada por linha.</param>
/// <param name="EscritaRestritaPorLinha">Se a gravação é restrita por linha.</param>
public sealed record PermissoesTabelaEsperadas(
    bool? PodeLer, bool? PodeInserir, bool? PodeGravarIdentidade, bool? PodeEsvaziar, bool? PodeReferenciar, bool? PodeAlterarRestricoes, bool? PodeDesligarTriggers,
    bool? LeituraFiltradaPorLinha, bool? EscritaRestritaPorLinha)
{
    /// <summary>As colunas que o login não lê, na ordem da tabela.</summary>
    public IReadOnlyList<string> ColunasSemLeitura { get; init; } = [];

    /// <summary>Se as duas dizem o mesmo: as marcas e as colunas sem leitura, na ordem.</summary>
    /// <param name="outra">As outras permissões.</param>
    /// <returns>Se são iguais.</returns>
    public bool Igual(PermissoesTabelaEsperadas outra) =>
        this with { ColunasSemLeitura = Array.Empty<string>() } == outra with { ColunasSemLeitura = Array.Empty<string>() }
        && ColunasSemLeitura.SequenceEqual(outra.ColunasSemLeitura);

    /// <summary>As permissões lidas, na forma da declaração.</summary>
    /// <param name="tabela">As permissões lidas.</param>
    /// <returns>A projeção.</returns>
    public static PermissoesTabelaEsperadas De(PermissoesTabela tabela) => new(
        tabela.PodeLer, tabela.PodeInserir, tabela.PodeGravarIdentidade, tabela.PodeEsvaziar, tabela.PodeReferenciar, tabela.PodeAlterarRestricoes, tabela.PodeDesligarTriggers,
        tabela.LeituraFiltradaPorLinha, tabela.EscritaRestritaPorLinha)
    {
        ColunasSemLeitura = tabela.ColunasSemLeitura,
    };

    /// <inheritdoc/>
    public override string ToString() =>
        $"lê {Marcas.Texto(PodeLer)} (sem leitura [{string.Join(", ", ColunasSemLeitura)}]), insere {Marcas.Texto(PodeInserir)}, identidade {Marcas.Texto(PodeGravarIdentidade)}, "
        + $"esvazia {Marcas.Texto(PodeEsvaziar)}, referencia {Marcas.Texto(PodeReferenciar)}, restrições {Marcas.Texto(PodeAlterarRestricoes)}, triggers {Marcas.Texto(PodeDesligarTriggers)}, "
        + $"leitura por linha {Marcas.Texto(LeituraFiltradaPorLinha)}, escrita por linha {Marcas.Texto(EscritaRestritaPorLinha)}";
}

/// <summary>O transporte de uma conexão.</summary>
/// <param name="Cifrado">Se é cifrada (nulo: sem resposta).</param>
/// <param name="ServidorVerificado">Se o servidor foi verificado.</param>
/// <param name="Local">Se o servidor está na própria máquina.</param>
public sealed record TransporteEsperado(bool? Cifrado, bool? ServidorVerificado, bool Local)
{
    /// <summary>O transporte lido, na forma da declaração.</summary>
    /// <param name="transporte">O transporte lido.</param>
    /// <returns>A projeção; nula se o transporte é nulo.</returns>
    public static TransporteEsperado? De(TransporteConexao? transporte) => transporte is null ? null : new(transporte.Cifrado, transporte.ServidorVerificado, transporte.Local);

    /// <inheritdoc/>
    public override string ToString() => $"cifrado {Marcas.Texto(Cifrado)}, servidor verificado {Marcas.Texto(ServidorVerificado)}, local {Marcas.Texto(Local)}";
}

/// <summary>Uma marca em texto: sim, não ou "?" (sem resposta).</summary>
internal static class Marcas
{
    internal static string Texto(bool? marca) => marca switch { null => "?", true => "sim", false => "não" };
}

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

/// <summary>Um nome escrito, um nome do catálogo e se o banco os iguala.</summary>
/// <param name="Escrito">O nome como foi escrito, sem as aspas.</param>
/// <param name="EntreAspas">Se foi escrito entre aspas.</param>
/// <param name="DoCatalogo">O nome como o catálogo o guarda.</param>
/// <param name="Esperada">A correspondência esperada, na origem e no destino.</param>
public sealed record NomeComparado(string Escrito, bool EntreAspas, string DoCatalogo, CorrespondenciaNome Esperada);

/// <summary>O maior valor que a leitura agregada da origem devolve para uma coluna; nulo quando nenhuma linha tem valor.</summary>
/// <param name="Coluna">A coluna inteira.</param>
/// <param name="Valor">O maior valor.</param>
public sealed record MaiorValorEsperado(ReferenciaColuna Coluna, long? Valor);

/// <summary>Os casos do maior valor (<see cref="EsquemaDeConformidade.MaioresValores"/>).</summary>
public sealed class MaioresValoresEsperados
{
    /// <summary>Uma coluna de tabela comum, com valor positivo.</summary>
    public required MaiorValorEsperado Comum { get; init; }

    /// <summary>Uma coluna sem valor em nenhuma linha (tabela vazia, ou só NULL).</summary>
    public required MaiorValorEsperado SemValor { get; init; }

    /// <summary>Uma coluna só com valores negativos.</summary>
    public required MaiorValorEsperado SoNegativos { get; init; }

    /// <summary>A raiz de uma particionada, com o maior valor numa partição.</summary>
    public required Caso<MaiorValorEsperado> Particionada { get; init; }

    /// <summary>A mãe de herança clássica, com valor maior numa herdeira (que não conta).</summary>
    public required Caso<MaiorValorEsperado> MaeDeHeranca { get; init; }

    /// <summary>Uma coluna de texto com um sentinela nas linhas.</summary>
    public required ReferenciaColuna ColunaDeTexto { get; init; }
}

/// <summary>Os casos da existência de linhas no destino (<see cref="EsquemaDeConformidade.Linhas"/>).</summary>
public sealed class LinhasNoDestino
{
    /// <summary>Uma tabela comum com linha.</summary>
    public required NomeObjeto ComLinha { get; init; }

    /// <summary>Uma tabela comum sem linha.</summary>
    public required NomeObjeto Vazia { get; init; }

    /// <summary>A raiz de uma particionada, com linhas só nas partições: tem linha.</summary>
    public required Caso<NomeObjeto> Particionada { get; init; }

    /// <summary>A mãe de herança clássica, com linha só numa herdeira: não tem (a cópia a lê sem as herdeiras).</summary>
    public required Caso<NomeObjeto> MaeComLinhaSoNaHerdeira { get; init; }

    /// <summary>Uma tabela com a segurança por linha que vale para o dono do destino: a leitura falha.</summary>
    public required Caso<NomeObjeto> ComSegurancaPorLinha { get; init; }
}

/// <summary>O que a fixture grava, como administrador, no lugar do marcador de um banco do par.</summary>
public enum ComentarioDoBanco
{
    /// <summary>Um comentário que não é do Incognito, com o sentinela.</summary>
    Alheio,

    /// <summary>O prefixo do marcador do Incognito seguido do sentinela: não se lê.</summary>
    Invalido,

    /// <summary>Um marcador válido, com o identificador e a data dados.</summary>
    Valido,
}
