using System.Diagnostics.CodeAnalysis;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Dialetos;

// O esquema lido por um dialeto: saída do contrato de extensão, com construtor público (um dialeto de fora o produz) e
// propriedades init validadas; imutável (as listas são copiadas). Cresce por propriedades novas, com o padrão que
// mantém o comportamento de quem ainda não as preenche.

/// <summary>O nome de um objeto do banco: esquema e nome, como o catálogo os guarda (sem aspas, na caixa original).</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class NomeObjeto : IEquatable<NomeObjeto>
{
    /// <summary>Cria o nome.</summary>
    /// <param name="esquema">O esquema.</param>
    /// <param name="nome">O nome.</param>
    /// <exception cref="ArgumentException">Esquema ou nome nulos ou vazios.</exception>
    public NomeObjeto(string esquema, string nome)
    {
        ArgumentException.ThrowIfNullOrEmpty(esquema);
        ArgumentException.ThrowIfNullOrEmpty(nome);
        Esquema = esquema;
        Nome = nome;
    }

    /// <summary>O esquema.</summary>
    public string Esquema { get; }

    /// <summary>O nome.</summary>
    public string Nome { get; }

    /// <summary>Igualdade ordinal: <c>Cliente</c> e <c>cliente</c> são objetos diferentes.</summary>
    /// <param name="other">O outro nome.</param>
    /// <returns>Verdadeiro se esquema e nome são iguais.</returns>
    public bool Equals(NomeObjeto? other) =>
        other is not null && string.Equals(Esquema, other.Esquema, StringComparison.Ordinal) && string.Equals(Nome, other.Nome, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NomeObjeto);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Esquema), StringComparer.Ordinal.GetHashCode(Nome));

    /// <summary>Esquema e nome separados por ponto, sem citar (para mensagens; para SQL, <see cref="DialetoSql.Citar(NomeObjeto)"/>).</summary>
    /// <returns>O nome.</returns>
    public override string ToString() => Esquema + "." + Nome;
}

/// <summary>Uma coluna de uma tabela.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ReferenciaColuna : IEquatable<ReferenciaColuna>
{
    /// <summary>Cria a referência.</summary>
    /// <param name="tabela">A tabela.</param>
    /// <param name="coluna">A coluna, como o catálogo a guarda.</param>
    /// <exception cref="ArgumentNullException">Tabela nula.</exception>
    /// <exception cref="ArgumentException">Coluna nula ou vazia.</exception>
    public ReferenciaColuna(NomeObjeto tabela, string coluna)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentException.ThrowIfNullOrEmpty(coluna);
        Tabela = tabela;
        Coluna = coluna;
    }

    /// <summary>A tabela.</summary>
    public NomeObjeto Tabela { get; }

    /// <summary>A coluna.</summary>
    public string Coluna { get; }

    /// <summary>Igualdade ordinal.</summary>
    /// <param name="other">A outra referência.</param>
    /// <returns>Verdadeiro se tabela e coluna são iguais.</returns>
    public bool Equals(ReferenciaColuna? other) =>
        other is not null && Tabela.Equals(other.Tabela) && string.Equals(Coluna, other.Coluna, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ReferenciaColuna);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Tabela, StringComparer.Ordinal.GetHashCode(Coluna));

    /// <summary>Tabela e coluna separadas por ponto, sem citar.</summary>
    /// <returns>A referência.</returns>
    public override string ToString() => Tabela + "." + Coluna;
}

/// <summary>O esquema de um banco: tabelas, sequences e objetos residuais.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class EsquemaBanco
{
    private readonly IReadOnlyList<TabelaEsquema> _tabelas = [];
    private readonly IReadOnlyList<SequenceEsquema> _sequences = [];
    private readonly IReadOnlyList<ObjetoResidual> _residuais = [];
    private readonly IReadOnlyList<ObjetoReservado>? _reservado;
    private readonly IReadOnlyList<string>? _triggersDeEvento;

    /// <summary>As tabelas, inclusive partições, herdeiras e históricos de temporais.</summary>
    public required IReadOnlyList<TabelaEsquema> Tabelas { get => _tabelas; init => _tabelas = Lista.Copiar(value, nameof(Tabelas)); }

    /// <summary>As sequences, com as colunas que as usam.</summary>
    public IReadOnlyList<SequenceEsquema> Sequences { get => _sequences; init => _sequences = Lista.Copiar(value, nameof(Sequences)); }

    /// <summary>
    /// Os objetos fora das tabelas copiadas que guardam dado (materialized views, views indexadas, CDC, large
    /// objects): num destino restaurado de produção, dado real.
    /// </summary>
    public IReadOnlyList<ObjetoResidual> ObjetosResiduais { get => _residuais; init => _residuais = Lista.Copiar(value, nameof(ObjetosResiduais)); }

    /// <summary>
    /// Os objetos do esquema <c>incognito</c>, reservado ao diário do Incognito (tabelas, views, índices e sequences), com o
    /// tipo de cada um, em ordem ordinal de nome; vazia quando não há nenhum. As tabelas desse esquema nunca entram em
    /// <see cref="Tabelas"/>. Nulo se o dialeto não informa: o <c>planejar</c> trata como o pior caso, porque o esquema pode
    /// esconder tabela ou dado. No destino, o <c>planejar</c> aceita os do próprio diário (<see cref="DiarioExecucao.ObjetosDoDiario"/>).
    /// </summary>
    public IReadOnlyList<ObjetoReservado>? ObjetosNoEsquemaReservado
    {
        get => _reservado;
        init => _reservado = value is null ? null : Lista.Copiar(value, nameof(ObjetosNoEsquemaReservado));
    }

    /// <summary>
    /// Os gatilhos de evento ligados no banco (event triggers no PostgreSQL, triggers DDL do banco no SQL Server), pelo
    /// nome, em ordem ordinal: disparam no que o diário faz no esquema, e só quem administra o servidor os desliga. Vazia se
    /// nenhum; nulo se o dialeto não informa.
    /// </summary>
    public IReadOnlyList<string>? TriggersDeEvento
    {
        get => _triggersDeEvento;
        init => _triggersDeEvento = value is null ? null : Lista.CopiarNomes(value, nameof(TriggersDeEvento));
    }

    /// <summary>
    /// A versão do servidor, a codificação e a colação padrão do banco; nulo se o dialeto não informa (o <c>planejar</c>
    /// trata como o pior caso).
    /// </summary>
    public PropriedadesBanco? Propriedades { get; init; }

    /// <summary>
    /// As permissões do login que leu o esquema; nulo se o dialeto não informa (o <c>planejar</c> não aprova nada que
    /// dependa de permissão antes da execução). Fica fora da comparação entre a origem e o destino: é a parte do esquema
    /// que depende do login, fora as marcas medidas de uma colação, que saem nulas (o pior caso) quando o login não tem
    /// acesso ao esquema dela.
    /// </summary>
    public AcessoLido? Acesso { get; init; }
}

/// <summary>Como uma tabela se relaciona com outras.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum TipoTabela
{
    /// <summary>Tabela comum.</summary>
    Comum,

    /// <summary>Tabela particionada: lida e gravada por ela, nunca pelas partições.</summary>
    Particionada,

    /// <summary>Partição de uma particionada (<see cref="TabelaEsquema.Pai"/>).</summary>
    Particao,

    /// <summary>Filha de herança clássica (<see cref="TabelaEsquema.Pai"/>), lida com <c>ONLY</c>.</summary>
    Herdeira,

    /// <summary>Tabela temporal com histórico (<see cref="TabelaEsquema.Historico"/>).</summary>
    Temporal,

    /// <summary>Histórico de uma temporal: tabela própria, copiada com as regras da principal ou esvaziada.</summary>
    HistoricoDeTemporal,
}

/// <summary>Uma tabela.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class TabelaEsquema
{
    private readonly NomeObjeto _nome = null!;
    private readonly TipoTabela _tipo;
    private readonly IReadOnlyList<ColunaEsquema> _colunas = [];
    private readonly IReadOnlyList<string> _chavePrimaria = [];
    private readonly IReadOnlyList<ChaveEstrangeira> _chavesEstrangeiras = [];
    private readonly IReadOnlyList<NomeObjeto> _outrosPais = [];
    private readonly string? _extensao;
    private readonly IReadOnlyList<IndiceUnico> _indicesUnicos = [];
    private readonly IReadOnlyList<RestricaoCheck> _restricoesCheck = [];
    private readonly IReadOnlyList<RestricaoExclusao> _restricoesExclusao = [];
    private readonly IReadOnlyList<TriggerEsquema> _triggers = [];

    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>O tipo (padrão: comum).</summary>
    public TipoTabela Tipo { get => _tipo; init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de tabela desconhecido."); }

    /// <summary>
    /// A particionada de uma partição, ou a mãe de uma herdeira; nulo nas outras (não conferido aqui: a suíte de
    /// conformidade e o motor conferem a consistência do esquema).
    /// </summary>
    public NomeObjeto? Pai { get; init; }

    /// <summary>
    /// As outras mães de uma herdeira de herança múltipla, na ordem da declaração: <see cref="Pai"/> é a primeira mãe, e
    /// estas são as demais. Vazia nas outras tabelas (não conferido aqui, como <see cref="Pai"/>).
    /// </summary>
    public IReadOnlyList<NomeObjeto> OutrosPais { get => _outrosPais; init => _outrosPais = Lista.Copiar(value, nameof(OutrosPais)); }

    /// <summary>O histórico de uma temporal; nulo nas outras (não conferido aqui, como <see cref="Pai"/>).</summary>
    public NomeObjeto? Historico { get; init; }

    /// <summary>
    /// A chave de partição de uma particionada, também da partição intermediária (que é particionada); nula nas outras
    /// (não conferido aqui, como <see cref="Pai"/>).
    /// </summary>
    public ChaveParticao? ChaveParticao { get; init; }

    /// <summary>
    /// Se é uma tabela estrangeira (foreign table, também como partição): os dados ficam fora do banco, e as colunas não
    /// são suportadas (a gerada fica fora da cópia, como em qualquer tabela).
    /// </summary>
    public bool Estrangeira { get; init; }

    /// <summary>
    /// A extensão que adotou a tabela, se há. Só para mensagem: o dono do banco pode fazer uma extensão adotar qualquer
    /// tabela, que continua classificada e copiada como as outras.
    /// </summary>
    public string? Extensao
    {
        get => _extensao;
        init
        {
            _extensao = value is null ? null : Lista.Texto(value, nameof(Extensao));
        }
    }

    /// <summary>As colunas, na ordem do catálogo.</summary>
    public required IReadOnlyList<ColunaEsquema> Colunas { get => _colunas; init => _colunas = Lista.Copiar(value, nameof(Colunas)); }

    /// <summary>As colunas da chave primária, na ordem da chave; vazia se não há.</summary>
    public IReadOnlyList<string> ChavePrimaria { get => _chavePrimaria; init => _chavePrimaria = Lista.CopiarNomes(value, nameof(ChavePrimaria)); }

    /// <summary>As chaves estrangeiras desta tabela (o lado que referencia).</summary>
    public IReadOnlyList<ChaveEstrangeira> ChavesEstrangeiras { get => _chavesEstrangeiras; init => _chavesEstrangeiras = Lista.Copiar(value, nameof(ChavesEstrangeiras)); }

    /// <summary>
    /// Se a tabela tem segurança por linha ligada (RLS no PostgreSQL, política de segurança no SQL Server): fato do esquema,
    /// independente do login. Se ela vale para quem lê ou grava (o dono, o superusuário e quem a ignora podem escapar dela)
    /// está em <see cref="EsquemaBanco.Acesso"/>. A contagem de linhas não detecta a leitura filtrada.
    /// </summary>
    public bool SegurancaDeLinha { get; init; }

    /// <summary>
    /// Se a própria tabela está numa publicação de replicação que replica a inserção ou o esvaziamento dela: o que o
    /// Incognito grava nela chega aos assinantes, fora do destino. No PostgreSQL, uma publicação com <c>insert</c> ou
    /// <c>truncate</c> que a inclui pela própria tabela, por uma tabela acima dela na árvore, pelo esquema dela ou de uma
    /// tabela acima, ou por todas as tabelas; no SQL Server, um artigo de publicação transacional ou de mesclagem. Numa
    /// particionada, vale só para ela: cada partição informa a sua, uma partição pode ser publicada sozinha, e o
    /// <c>planejar</c> confere a árvore inteira pela raiz. Nulo: não informado, o pior caso.
    /// </summary>
    public bool? Publicada { get; init; }

    /// <summary>
    /// Se a própria tabela está numa assinatura de replicação deste banco, ligada ou não: o que chega da publicação assinada
    /// grava nela ao lado do que o Incognito gravou, e uma assinatura desligada aplica, ao ser ligada, o que se acumulou no
    /// publicador. No PostgreSQL, uma tabela listada numa assinatura do banco (a de outro banco do mesmo servidor não grava
    /// nele); no SQL Server, o destino de um artigo assinado. Numa particionada, vale só para ela: cada partição informa a
    /// sua, e o <c>planejar</c> confere a árvore inteira pela raiz. Nulo: não informado, o pior caso.
    /// </summary>
    public bool? Assinada { get; init; }

    // Os objetos da tabela, cada lista em ordem ordinal de nome. Vazia diz "não há": um dialeto que não as preenche fica
    // com o caso otimista (a suíte de conformidade confere os do repositório pelo inventário).

    /// <summary>
    /// Os índices únicos, inclusive o da chave primária, em ordem ordinal de nome; vazia se não há (um dialeto que não a
    /// preenche fica com o caso otimista).
    /// </summary>
    public IReadOnlyList<IndiceUnico> IndicesUnicos { get => _indicesUnicos; init => _indicesUnicos = Lista.Copiar(value, nameof(IndicesUnicos)); }

    /// <summary>Os <c>CHECK</c>s, em ordem ordinal de nome; vazia se não há, como em <see cref="IndicesUnicos"/>.</summary>
    public IReadOnlyList<RestricaoCheck> RestricoesCheck { get => _restricoesCheck; init => _restricoesCheck = Lista.Copiar(value, nameof(RestricoesCheck)); }

    /// <summary>As restrições de exclusão, em ordem ordinal de nome; vazia se não há, como em <see cref="IndicesUnicos"/>.</summary>
    public IReadOnlyList<RestricaoExclusao> RestricoesExclusao { get => _restricoesExclusao; init => _restricoesExclusao = Lista.Copiar(value, nameof(RestricoesExclusao)); }

    /// <summary>As triggers de usuário, em ordem ordinal de nome; vazia se não há, como em <see cref="IndicesUnicos"/>.</summary>
    public IReadOnlyList<TriggerEsquema> Triggers { get => _triggers; init => _triggers = Lista.Copiar(value, nameof(Triggers)); }
}

/// <summary>As colunas que decidem a partição: tratar uma delas pode mudar a linha de partição ou recusá-la.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ChaveParticao
{
    private readonly IReadOnlyList<string> _colunas = [];

    /// <summary>
    /// As colunas citadas pela chave, sem repetição: primeiro as que são elementos da chave, na ordem dela; depois as
    /// citadas pelas expressões da chave, na ordem das colunas da tabela. Vazia só com <see cref="ColunasIncompletas"/>,
    /// se a chave só cita o que o dialeto não sabe dizer (não conferido aqui: a suíte de conformidade e o motor conferem).
    /// </summary>
    public required IReadOnlyList<string> Colunas { get => _colunas; init => _colunas = Lista.CopiarNomes(value, nameof(Colunas)); }

    /// <summary>
    /// Se a chave cita mais do que <see cref="Colunas"/> (a linha inteira numa expressão, ou uma expressão que o dialeto
    /// não decompõe): quem usa a chave a trata como se citasse todas as colunas. Uma função chamada pela expressão que leia
    /// outras tabelas continua invisível.
    /// </summary>
    public required bool ColunasIncompletas { get; init; }
}

/// <summary>A situação de um tipo de coluna na matriz de tipos.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum SituacaoTipo
{
    /// <summary>
    /// Cópia direta e transformação quando compatível; <see cref="ColunaEsquema.Tipo"/> preenchido quando há representação
    /// canônica (sem ela, como <c>time</c> e <c>interval</c> no PostgreSQL, só <c>nulo</c> ou <c>fixo</c>).
    /// </summary>
    Suportado,

    /// <summary>Copiado como está; transformação só com <c>nulo</c> ou <c>fixo</c>.</summary>
    ComRessalva,

    /// <summary>Fora da cópia: o destino recalcula ou regenera (colunas geradas e computadas, <c>rowversion</c>).</summary>
    SuportadoPorExclusao,

    /// <summary>Registrado no relatório; ligado a coluna tratada, o <c>planejar</c> recusa.</summary>
    NaoSuportado,
}

/// <summary>Como o banco gera o valor de uma coluna.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum GeracaoColuna
{
    /// <summary>Não é gerada.</summary>
    Nenhuma,

    /// <summary>Gerada e armazenada (<c>GENERATED ... STORED</c>, computada <c>PERSISTED</c>).</summary>
    Armazenada,

    /// <summary>Gerada na leitura (virtual no PostgreSQL 18, computada não persistida).</summary>
    Virtual,
}

/// <summary>A identity de uma coluna.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum IdentidadeColuna
{
    /// <summary>Sem identity.</summary>
    Nenhuma,

    /// <summary>Identity que só o banco gera (<c>GENERATED ALWAYS</c>).</summary>
    Sempre,

    /// <summary>Identity que aceita valor informado (<c>BY DEFAULT</c>; no SQL Server, toda identity com <c>KeepIdentity</c>).</summary>
    PorPadrao,
}

/// <summary>Uma coluna.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ColunaEsquema
{
    private readonly string _nome = null!;
    private readonly string _tipoNativo = null!;
    private readonly SituacaoTipo _situacao;
    private readonly TipoColuna? _tipo;
    private readonly GeracaoColuna _geracao;
    private readonly IdentidadeColuna _identidade;
    private readonly string? _tipoBase;
    private readonly int? _precisao;

    /// <summary>O nome, como o catálogo o guarda.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>O tipo como o banco o nomeia (<c>character varying</c>, <c>nvarchar</c>).</summary>
    public required string TipoNativo { get => _tipoNativo; init => _tipoNativo = Lista.Texto(value, nameof(TipoNativo)); }

    /// <summary>
    /// O tipo do fim da cadeia de domínios (tipo de alias no SQL Server), como o banco o nomeia, numa coluna cujo tipo é
    /// domínio; nulo nas outras.
    /// </summary>
    public string? TipoBase { get => _tipoBase; init => _tipoBase = value is null ? null : Lista.Texto(value, nameof(TipoBase)); }

    /// <summary>A situação do tipo na matriz de tipos.</summary>
    public required SituacaoTipo Situacao
    {
        get => _situacao;
        init => _situacao = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Situacao), "Situação de tipo desconhecida.");
    }

    /// <summary>
    /// A representação canônica do valor, só em <see cref="SituacaoTipo.Suportado"/> e nem sempre nele; nula quando o tipo
    /// não tem representação canônica (a coluna só aceita <c>nulo</c> ou <c>fixo</c>).
    /// </summary>
    public TipoColuna? Tipo
    {
        get => _tipo;
        init => _tipo = value is null || Enum.IsDefined(value.Value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de coluna desconhecido.");
    }

    /// <summary>Se aceita NULL.</summary>
    public bool Anulavel { get; init; }

    /// <summary>Como o banco gera o valor (padrão: não gera).</summary>
    public GeracaoColuna Geracao
    {
        get => _geracao;
        init => _geracao = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Geracao), "Geração de coluna desconhecida.");
    }

    /// <summary>A identity (padrão: nenhuma).</summary>
    public IdentidadeColuna Identidade
    {
        get => _identidade;
        init => _identidade = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Identidade), "Identity desconhecida.");
    }

    /// <summary>Se a coluna tem máscara dinâmica (SQL Server), que a leitura sem permissão devolveria mascarada.</summary>
    public bool Mascarada { get; init; }

    // A semântica da coluna, toda pelo fim da cadeia de domínios e arrays (num array, vale para cada elemento). "Texto" é
    // o tipo de texto no fim da cadeia, também com tipo canônico nulo (um domínio sobre varchar).

    /// <summary>
    /// A colação efetiva da coluna (a do domínio ou a do <c>COLLATE</c> explícito). Nula: sem colação ou não informada; numa
    /// coluna de texto, também de tipo canônico nulo, o <c>planejar</c> trata como o pior caso.
    /// </summary>
    public ColacaoEsquema? Colacao { get; init; }

    /// <summary>
    /// Se o próprio tipo compara sem a caixa (<c>citext</c>), qualquer que seja a colação. Nulo fora de texto; numa coluna
    /// de texto, nulo é o pior caso. O <c>planejar</c> combina com <see cref="Colacao"/>.
    /// </summary>
    public bool? IgnoraCaixaPeloTipo { get; init; }

    /// <summary>
    /// Se o próprio tipo compara sem os espaços finais (<c>char(n)</c>; no SQL Server, todo <c>char</c> e <c>varchar</c>,
    /// em qualquer colação). Nulo como em <see cref="IgnoraCaixaPeloTipo"/>.
    /// </summary>
    public bool? IgnoraEspacoFinalPeloTipo { get; init; }

    /// <summary>
    /// O comprimento que o tipo declara. Nulo: o dialeto não informa ou não mede (o <c>planejar</c> trata como o pior caso
    /// para um valor fixo); sem limite declarado, o limite vem preenchido com o valor nulo.
    /// </summary>
    public LimiteComprimento? Comprimento { get; init; }

    /// <summary>
    /// A precisão de um <c>numeric</c>/<c>decimal</c> (dígitos ao todo); nula se o tipo não a declara ou não é decimal. Não
    /// é a precisão de segundos fracionários de data e hora.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Zero ou negativa.</exception>
    public int? Precisao
    {
        get => _precisao;
        init => _precisao = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Precisao), "A precisão precisa ser positiva.");
    }

    /// <summary>
    /// A escala de um <c>numeric</c>/<c>decimal</c> (dígitos depois da vírgula; negativa no PostgreSQL 15+): só com a
    /// <see cref="Precisao"/>, e nula quando ela é nula. Numa coluna decimal com a precisão, a escala nula (não informada) é
    /// o pior caso.
    /// </summary>
    public int? Escala { get; init; }

    /// <summary>A faixa do tipo inteiro; nula fora de inteiro. Numa coluna inteira, nula é o pior caso.</summary>
    public FaixaInteira? Faixa { get; init; }

    /// <summary>
    /// As colunas de que uma coluna gerada ou computada depende: um índice sobre ela depende delas. Nula fora de coluna
    /// gerada; numa coluna gerada, nula é o pior caso.
    /// </summary>
    public ReferenciasDaExpressao? ReferenciasDaGeracao { get; init; }

    /// <summary>
    /// Se uma restrição do tipo vale para a coluna sem aparecer nas restrições da tabela (o <c>CHECK</c> de um domínio da
    /// cadeia; no SQL Server, uma regra ligada à coluna ou ao tipo de alias): o pseudônimo pode violá-la. Nulo: não
    /// informado, o pior caso.
    /// </summary>
    public bool? RestritaPeloTipo { get; init; }
}

/// <summary>Uma chave estrangeira, do lado que referencia.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ChaveEstrangeira
{
    private readonly string _nome = null!;
    private readonly IReadOnlyList<string> _colunas = [];
    private readonly NomeObjeto _referenciada = null!;
    private readonly IReadOnlyList<string> _colunasReferenciadas = [];

    /// <summary>O nome da restrição.</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>As colunas que referenciam, na ordem da chave.</summary>
    public required IReadOnlyList<string> Colunas { get => _colunas; init => _colunas = Lista.CopiarNomes(value, nameof(Colunas)); }

    /// <summary>A tabela referenciada (numa particionada, a particionada, nunca as partições).</summary>
    public required NomeObjeto Referenciada { get => _referenciada; init => _referenciada = value ?? throw new ArgumentNullException(nameof(Referenciada)); }

    /// <summary>As colunas referenciadas, na mesma ordem de <see cref="Colunas"/>.</summary>
    public required IReadOnlyList<string> ColunasReferenciadas { get => _colunasReferenciadas; init => _colunasReferenciadas = Lista.CopiarNomes(value, nameof(ColunasReferenciadas)); }

    /// <summary>
    /// Se o banco confere a chave agora, nas gravações de uma sessão comum. <c>false</c>: a conferência está desligada na
    /// chave ou na cópia dela numa partição (triggers de integridade desabilitadas ou só de réplica, <c>NOT ENFORCED</c> no
    /// PostgreSQL 18, desabilitada no SQL Server). <c>true</c> não prova que as linhas de agora a respeitam: quem confere é
    /// a verificação depois da carga. Nulo: não informado, o pior caso. Uma aplicação que grave com
    /// <c>session_replication_role</c> em réplica não é detectável.
    /// </summary>
    public bool? Imposta { get; init; }

    /// <summary>
    /// Se o banco conferiu as linhas que já existiam quando a conferência foi ligada (na criação da chave ou depois), em
    /// todas as cópias dela. <c>false</c> (<c>NOT VALID</c>, <c>WITH NOCHECK</c>): pode haver linha órfã. <c>true</c>
    /// também não prova que não há: as gravadas com a conferência desligada não são revistas (ver <see cref="Imposta"/>).
    /// Nulo: não informado, o pior caso.
    /// </summary>
    public bool? Validada { get; init; }

    /// <summary>
    /// Se a conferência da chave está desligada de um jeito que o login não restaura depois de removê-la e recriá-la (no
    /// PostgreSQL, alguma trigger interna da chave, ou de uma cópia dela numa partição, fora do estado de origem: só o
    /// superuser as desliga). <c>false</c> quando o estado volta igual pela definição (inclusive <c>NOT ENFORCED</c> e, no SQL
    /// Server, <c>NOCHECK</c>). Nulo: não informado, o pior caso.
    /// </summary>
    public bool? DesligadaSemRestauracao { get; init; }
}

/// <summary>Uma sequence e as colunas que a usam (mais de uma: sequence compartilhada).</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class SequenceEsquema
{
    private readonly NomeObjeto _nome = null!;
    private readonly IReadOnlyList<ReferenciaColuna> _colunas = [];

    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>
    /// As colunas que tiram valor dela (pelo default, também o de um domínio, ou pela identity), como o catálogo as liga;
    /// vazia se nenhuma. Um uso que o banco não registra fica de fora: o nome da sequence em texto
    /// (<c>nextval('s'::text)</c> no PostgreSQL) ou dentro de uma função chamada pelo default.
    /// </summary>
    public IReadOnlyList<ReferenciaColuna> Colunas { get => _colunas; init => _colunas = Lista.Copiar(value, nameof(Colunas)); }
}

/// <summary>O tipo de um objeto residual.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum TipoObjetoResidual
{
    /// <summary>Materialized view (PostgreSQL), atualizada no fim.</summary>
    VisaoMaterializada,

    /// <summary>View indexada (SQL Server), removida e recriada.</summary>
    VisaoIndexada,

    /// <summary>Tabela de captura de mudanças (CDC, SQL Server).</summary>
    CapturaDeMudancas,

    /// <summary>Large objects (<c>pg_largeobject</c>, PostgreSQL), um objeto para o banco inteiro.</summary>
    ObjetosGrandes,
}

/// <summary>Um objeto fora das tabelas copiadas que guarda dado.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ObjetoResidual
{
    private readonly NomeObjeto _nome = null!;
    private readonly TipoObjetoResidual _tipo;
    private readonly IReadOnlyList<NomeObjeto> _dependeDe = [];

    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>O tipo.</summary>
    public required TipoObjetoResidual Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de objeto residual desconhecido.");
    }

    /// <summary>
    /// As materialized views que esta lê, direto ou passando por views, em ordem ordinal de esquema e nome: atualizadas
    /// antes dela. Vazia se não lê nenhuma. Sem ciclo: a que a leitura traria de volta a esta (ou esta mesma) fica de fora,
    /// e <see cref="DependenciasIncompletas"/> vem <c>true</c>.
    /// </summary>
    public IReadOnlyList<NomeObjeto> DependeDe { get => _dependeDe; init => _dependeDe = Lista.Copiar(value, nameof(DependeDe)); }

    /// <summary>
    /// Se a leitura passa por uma função ou por um operador de usuário, que o banco não liga às tabelas que a função lê, ou
    /// volta a esta materialized view por um ciclo: a ordem de <see cref="DependeDe"/> pode estar incompleta, e a
    /// atualização se repete até nada mudar. Não são
    /// detectadas uma função do sistema que executa SQL em texto (<c>query_to_xml</c> e semelhantes) nem a política de
    /// segurança por linha de uma tabela lida que leia outra materialized view. Nulo fora de materialized view ou não
    /// informado.
    /// </summary>
    public bool? DependenciasIncompletas { get; init; }

    /// <summary>Se a materialized view tem dados (criada ou atualizada sem <c>WITH NO DATA</c>); nulo fora dela ou não informado.</summary>
    public bool? Populada { get; init; }
}

/// <summary>O banco, o servidor, a linhagem e o transporte lidos de uma conexão.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class IdentidadeLida
{
    private readonly string _banco = null!;
    private readonly string _servidor = null!;
    private readonly IReadOnlyDictionary<string, string> _linhagem = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>O banco conectado (<c>current_database()</c>, <c>DB_NAME()</c>), conferido com <c>bancoEsperado</c>.</summary>
    public required string Banco { get => _banco; init => _banco = Lista.Texto(value, nameof(Banco)); }

    /// <summary>O servidor, como o banco o informa; só para aviso.</summary>
    public required string Servidor
    {
        get => _servidor;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _servidor = value;
        }
    }

    /// <summary>
    /// A linhagem (<c>system_identifier</c> e oid do banco; <c>database_guid</c> e <c>family_guid</c>), por nome:
    /// iguais entre origem e destino geram aviso, nunca recusa (uma homologação restaurada de produção tem a mesma).
    /// </summary>
    public required IReadOnlyDictionary<string, string> Linhagem
    {
        get => _linhagem;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Linhagem));
            var copia = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (nome, valor) in value)
            {
                ArgumentException.ThrowIfNullOrEmpty(nome, nameof(Linhagem));
                copia.Add(nome, valor ?? throw new ArgumentNullException(nameof(Linhagem), "A linhagem tem valor nulo."));
            }

            _linhagem = copia.AsReadOnly();
        }
    }

    /// <summary>Como a conexão chega ao servidor; nulo se o dialeto não informa (o pior caso: em claro, remoto).</summary>
    public TransporteConexao? Transporte { get; init; }

    /// <summary>
    /// O marcador do Incognito no banco: o destino precisa dele, e a origem com ele é recusada. Nulo se o dialeto
    /// não informa, o pior caso nos dois lados.
    /// </summary>
    public MarcadorLido? Marcador { get; init; }
}

/// <summary>O que o banco tem no lugar do marcador do Incognito.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum SituacaoMarcador
{
    /// <summary>Nada: o banco não tem o comentário (ou a propriedade) em que o marcador fica.</summary>
    Ausente = 0,

    /// <summary>Um marcador do Incognito que se lê inteiro: a versão, o identificador e a data.</summary>
    Valido = 1,

    /// <summary>Começa como um marcador do Incognito, mas não se lê (versão desconhecida, identificador ou data malformados).</summary>
    Invalido = 2,

    /// <summary>Um comentário que não é do Incognito: o texto dele nunca sai do dialeto.</summary>
    Alheio = 3,
}

/// <summary>
/// O marcador lido. Só o válido traz o identificador e a data; o texto de um comentário alheio ou de um marcador ilegível
/// nunca chega aqui (é texto de quem administra o banco, que pode ter qualquer coisa).
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class MarcadorLido
{
    /// <summary>O que o banco tem.</summary>
    public required SituacaoMarcador Situacao { get; init; }

    /// <summary>O identificador aleatório gravado pelo <c>preparar-destino</c>; só no válido.</summary>
    public Guid? Identificador { get; init; }

    /// <summary>Quando o marcador foi gravado, em UTC e em segundos; só no válido.</summary>
    public DateTimeOffset? GravadoEm { get; init; }
}

/// <summary>
/// Cópia das listas do modelo, que recusa lista nula e item nulo (e, nas de nomes, vazio), e a conferência do texto
/// obrigatório.
/// </summary>
internal static class Lista
{
    internal static string Texto(string valor, string propriedade)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor, propriedade);
        return valor;
    }

    // Copia antes de conferir: uma lista que mude entre as duas passadas não traz item nulo.
    internal static IReadOnlyList<T> Copiar<T>(IReadOnlyList<T> valor, string propriedade)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(valor, propriedade);
        var copia = valor.ToArray();
        if (Array.Exists(copia, item => item is null))
        {
            throw new ArgumentNullException(propriedade, "A lista tem item nulo.");
        }

        return Array.AsReadOnly(copia);
    }

    internal static IReadOnlyList<string> CopiarNomes(IReadOnlyList<string> valor, string propriedade)
    {
        var copia = Copiar(valor, propriedade);
        if (copia.Any(nome => nome.Length == 0))
        {
            throw new ArgumentException("A lista tem nome vazio.", propriedade);
        }

        return copia;
    }
}
