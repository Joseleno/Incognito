// A carga do plano guarda o esquema e a identidade lidos, tipos do contrato experimental de dialeto que o motor declara;
// nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O que a execução lê do plano e o <see cref="Plano"/> não publica: a configuração como escrita (nunca resolvida), as
/// leituras dos dois bancos, a forma de cada unidade de cópia e o que cria cada transformador. Nunca guarda transformador,
/// sessão, chave, segredo de conexão nem valor resolvido; o <c>ToString</c> de cada parte não mostra conteúdo.
/// </summary>
/// <param name="Configuracao">A configuração que a execução usa.</param>
/// <param name="Origem">A leitura da origem.</param>
/// <param name="Destino">A leitura do destino.</param>
/// <param name="Unidades">As unidades de cópia, na ordem de <see cref="Plano.Tabelas"/>.</param>
/// <param name="Dominios">A tabela de domínios da sessão.</param>
internal sealed record DadosDeExecucao(
    ConfiguracaoDaExecucao Configuracao,
    LeituraDoBanco Origem,
    LeituraDoBanco Destino,
    IReadOnlyList<UnidadeDeCopia> Unidades,
    IReadOnlyList<DefinicaoDominio> Dominios)
{
    /// <summary>As unidades de cópia, copiadas.</summary>
    public IReadOnlyList<UnidadeDeCopia> Unidades { get; } = [.. Unidades];

    /// <summary>A tabela de domínios, copiada.</summary>
    public IReadOnlyList<DefinicaoDominio> Dominios { get; } = [.. Dominios];

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(DadosDeExecucao)} ({Unidades.Count} unidades, {Dominios.Count} domínios)";
}

/// <summary>Uma referência a segredo como foi escrita: o tipo e o texto do YAML, nunca o alvo resolvido.</summary>
/// <param name="Tipo">Variável de ambiente ou arquivo.</param>
/// <param name="Escrito">O texto como foi escrito, com o separador de caminho normalizado para <c>/</c>.</param>
internal sealed record ReferenciaEscrita(TipoReferenciaSegredo Tipo, string Escrito)
{
    internal static ReferenciaEscrita De(ReferenciaSegredo referencia) => new(referencia.Tipo, referencia.Escrito);

    /// <inheritdoc/>
    public override string ToString() => nameof(ReferenciaEscrita);
}

/// <summary>Origem ou destino como escritos.</summary>
/// <param name="Provedor">O provedor.</param>
/// <param name="Referencia">A referência à string de conexão, como escrita.</param>
/// <param name="BancoEsperado">O nome do banco que a conexão precisa alcançar; nulo quando não escrito.</param>
internal sealed record ConexaoEscrita(ProvedorBanco Provedor, ReferenciaEscrita Referencia, string? BancoEsperado)
{
    internal static ConexaoEscrita De(ConexaoBanco conexao) => new(conexao.Provedor, ReferenciaEscrita.De(conexao.Conexao), conexao.BancoEsperado);

    /// <inheritdoc/>
    public override string ToString() => nameof(ConexaoEscrita);
}

/// <summary>
/// A configuração que a execução usa: um registro próprio, e não a <see cref="ConfiguracaoIncognito"/>, para que nenhum
/// consumidor leia a regra escrita no lugar da efetiva.
/// </summary>
/// <param name="Origem">A origem.</param>
/// <param name="Destino">O destino.</param>
/// <param name="Chave">A referência à chave, como escrita; nula no modo <see cref="ModoTratamento.Efemero"/>.</param>
/// <param name="Modo">O modo de tratamento.</param>
/// <param name="Ambiente">O ambiente que entra nas subchaves.</param>
/// <param name="Consistencia">A consistência da leitura, com o padrão aplicado.</param>
/// <param name="Paralelismo">O paralelismo escrito; nulo quando não escrito (a precedência com as opções fica com a execução).</param>
internal sealed record ConfiguracaoDaExecucao(
    ConexaoEscrita Origem,
    ConexaoEscrita Destino,
    ReferenciaEscrita? Chave,
    ModoTratamento Modo,
    string Ambiente,
    Consistencia Consistencia,
    int? Paralelismo)
{
    internal static ConfiguracaoDaExecucao De(ConfiguracaoIncognito configuracao) => new(
        ConexaoEscrita.De(configuracao.Origem),
        ConexaoEscrita.De(configuracao.Destino),
        configuracao.Chave is { } chave ? ReferenciaEscrita.De(chave) : null,
        configuracao.Modo,
        configuracao.Ambiente,
        configuracao.Politicas.Consistencia ?? Consistencia.MelhorEsforco,
        configuracao.Politicas.Paralelismo);

    /// <inheritdoc/>
    public override string ToString() => nameof(ConfiguracaoDaExecucao);
}

/// <summary>Uma unidade de cópia: como ler e gravar a tabela, e o que tratar nela.</summary>
/// <param name="Nome">O nome no catálogo.</param>
/// <param name="Acao">A ação efetiva.</param>
/// <param name="SoAPropria">
/// Se a leitura e o esvaziamento precisam se limitar às linhas da própria tabela (<c>ONLY</c> no PostgreSQL): mãe ou
/// herdeira de herança clássica, cujas leituras alcançariam as linhas das herdeiras.
/// </param>
/// <param name="AdmiteFreeze">Se a gravação pode marcar as linhas como congeladas: não admite a particionada.</param>
/// <param name="ColunasLidas">As colunas lidas e gravadas, na ordem do catálogo, sem as geradas (o banco as recalcula).</param>
/// <param name="Tratadas">As colunas com transformador, na ordem do catálogo.</param>
internal sealed record UnidadeDeCopia(
    NomeTabela Nome,
    AcaoTabela Acao,
    bool SoAPropria,
    bool AdmiteFreeze,
    IReadOnlyList<string> ColunasLidas,
    IReadOnlyList<ColunaTratada> Tratadas)
{
    /// <summary>As colunas lidas, copiadas.</summary>
    public IReadOnlyList<string> ColunasLidas { get; } = [.. ColunasLidas];

    /// <summary>As colunas tratadas, copiadas.</summary>
    public IReadOnlyList<ColunaTratada> Tratadas { get; } = [.. Tratadas];

    /// <summary>A forma de leitura e gravação de uma tabela da origem.</summary>
    /// <param name="tabela">A tabela.</param>
    /// <param name="esquema">O esquema da origem, de onde saem as herdeiras.</param>
    /// <returns>Se só a própria tabela, se admite congelar e as colunas lidas.</returns>
    internal static (bool SoAPropria, bool AdmiteFreeze, IReadOnlyList<string> ColunasLidas) Forma(TabelaEsquema tabela, EsquemaBanco esquema)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentNullException.ThrowIfNull(esquema);
        var temHerdeira = esquema.Tabelas.Any(outra =>
            outra.Tipo == TipoTabela.Herdeira && (tabela.Nome.Equals(outra.Pai) || outra.OutrosPais.Contains(tabela.Nome)));
        return (
            tabela.Tipo == TipoTabela.Herdeira || temHerdeira,
            tabela.Tipo != TipoTabela.Particionada,
            [.. tabela.Colunas.Where(coluna => coluna.Geracao == GeracaoColuna.Nenhuma).Select(coluna => coluna.Nome)]);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(UnidadeDeCopia)} ({ColunasLidas.Count} colunas, {Tratadas.Count} tratadas)";
}

/// <summary>Uma coluna com transformador: o que o executor precisa para recriá-lo com a chave real, nunca a instância.</summary>
/// <param name="Coluna">O nome da coluna no catálogo.</param>
/// <param name="Fabrica">A fábrica registrada.</param>
/// <param name="Regra">A regra conferida pela fábrica.</param>
/// <param name="Normalizados">Os parâmetros normalizados pela fábrica, por nome (comparação ordinal).</param>
internal sealed record ColunaTratada(string Coluna, FabricaRegistrada Fabrica, RegraTransformacao Regra, IReadOnlyDictionary<string, string> Normalizados)
{
    /// <summary>Os parâmetros normalizados, copiados.</summary>
    public IReadOnlyDictionary<string, string> Normalizados { get; } = new Dictionary<string, string>(Normalizados, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => nameof(ColunaTratada);
}
