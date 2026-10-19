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

    /// <summary>As tabelas, inclusive partições, herdeiras e históricos de temporais.</summary>
    public required IReadOnlyList<TabelaEsquema> Tabelas { get => _tabelas; init => _tabelas = Lista.Copiar(value, nameof(Tabelas)); }

    /// <summary>As sequences, com as colunas que as usam.</summary>
    public IReadOnlyList<SequenceEsquema> Sequences { get => _sequences; init => _sequences = Lista.Copiar(value, nameof(Sequences)); }

    /// <summary>
    /// Os objetos fora das tabelas copiadas que guardam dado (materialized views, views indexadas, CDC, large
    /// objects): num destino restaurado de produção, dado real.
    /// </summary>
    public IReadOnlyList<ObjetoResidual> ObjetosResiduais { get => _residuais; init => _residuais = Lista.Copiar(value, nameof(ObjetosResiduais)); }
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
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrEmpty(value, nameof(Extensao));
            }

            _extensao = value;
        }
    }

    /// <summary>As colunas, na ordem do catálogo.</summary>
    public required IReadOnlyList<ColunaEsquema> Colunas { get => _colunas; init => _colunas = Lista.Copiar(value, nameof(Colunas)); }

    /// <summary>As colunas da chave primária, na ordem da chave; vazia se não há.</summary>
    public IReadOnlyList<string> ChavePrimaria { get => _chavePrimaria; init => _chavePrimaria = Lista.CopiarNomes(value, nameof(ChavePrimaria)); }

    /// <summary>As chaves estrangeiras desta tabela (o lado que referencia).</summary>
    public IReadOnlyList<ChaveEstrangeira> ChavesEstrangeiras { get => _chavesEstrangeiras; init => _chavesEstrangeiras = Lista.Copiar(value, nameof(ChavesEstrangeiras)); }

    /// <summary>
    /// Se a tabela tem segurança por linha que filtraria a leitura (RLS ligado no PostgreSQL, política de segurança
    /// no SQL Server): falha segura, a contagem de linhas não a detecta.
    /// </summary>
    public bool SegurancaDeLinha { get; init; }
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
    public bool ColunasIncompletas { get; init; }
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

    /// <summary>O nome, como o catálogo o guarda.</summary>
    public required string Nome { get => _nome; init => _nome = Texto(value); }

    /// <summary>O tipo como o banco o nomeia (<c>character varying</c>, <c>nvarchar</c>).</summary>
    public required string TipoNativo { get => _tipoNativo; init => _tipoNativo = Texto(value); }

    /// <summary>
    /// O tipo do fim da cadeia de domínios (tipo de alias no SQL Server), como o banco o nomeia, numa coluna cujo tipo é
    /// domínio; nulo nas outras.
    /// </summary>
    public string? TipoBase { get => _tipoBase; init => _tipoBase = value is null ? null : Texto(value); }

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

    private static string Texto(string valor)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor);
        return valor;
    }
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
    public required string Nome
    {
        get => _nome;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _nome = value;
        }
    }

    /// <summary>As colunas que referenciam, na ordem da chave.</summary>
    public required IReadOnlyList<string> Colunas { get => _colunas; init => _colunas = Lista.CopiarNomes(value, nameof(Colunas)); }

    /// <summary>A tabela referenciada (numa particionada, a particionada, nunca as partições).</summary>
    public required NomeObjeto Referenciada { get => _referenciada; init => _referenciada = value ?? throw new ArgumentNullException(nameof(Referenciada)); }

    /// <summary>As colunas referenciadas, na mesma ordem de <see cref="Colunas"/>.</summary>
    public required IReadOnlyList<string> ColunasReferenciadas { get => _colunasReferenciadas; init => _colunasReferenciadas = Lista.CopiarNomes(value, nameof(ColunasReferenciadas)); }
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

    /// <summary>O nome.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>O tipo.</summary>
    public required TipoObjetoResidual Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de objeto residual desconhecido.");
    }
}

/// <summary>O banco, o servidor e a linhagem lidos de um banco.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class IdentidadeLida
{
    private readonly string _banco = null!;
    private readonly string _servidor = null!;
    private readonly IReadOnlyDictionary<string, string> _linhagem = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>O banco conectado (<c>current_database()</c>, <c>DB_NAME()</c>), conferido com <c>bancoEsperado</c>.</summary>
    public required string Banco
    {
        get => _banco;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _banco = value;
        }
    }

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
}

/// <summary>Cópia das listas do modelo, que recusa lista nula e item nulo (e, nas de nomes, vazio).</summary>
internal static class Lista
{
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
