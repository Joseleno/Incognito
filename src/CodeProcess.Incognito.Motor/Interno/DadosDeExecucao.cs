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
/// <param name="ChavesRemovidas">
/// As chaves estrangeiras do destino que o diário remove antes de esvaziar e recria depois da carga: as do filho copiado, e
/// as do pai copiado ou esvaziado, em ordem de tabela e chave.
/// </param>
/// <param name="TriggersDesligadas">
/// As tabelas do destino cujas triggers o diário desliga antes de esvaziar e religa depois da carga: a raiz copiada ou
/// esvaziada com trigger ligada nela ou numa partição, como o catálogo do destino a guarda, em ordem de nome. Calculadas da
/// leitura do destino, porque só o plano sabe que tabela do destino corresponde a cada unidade.
/// </param>
internal sealed record DadosDeExecucao(
    ConfiguracaoDaExecucao Configuracao,
    LeituraDoBanco Origem,
    LeituraDoBanco Destino,
    IReadOnlyList<UnidadeDeCopia> Unidades,
    IReadOnlyList<DefinicaoDominio> Dominios,
    IReadOnlyList<ChaveRemovida> ChavesRemovidas,
    IReadOnlyList<NomeObjeto> TriggersDesligadas)
{
    /// <summary>As unidades de cópia, copiadas.</summary>
    public IReadOnlyList<UnidadeDeCopia> Unidades { get; } = [.. Unidades];

    /// <summary>A tabela de domínios, copiada.</summary>
    public IReadOnlyList<DefinicaoDominio> Dominios { get; } = [.. Dominios];

    /// <summary>As chaves que o diário remove, copiadas.</summary>
    public IReadOnlyList<ChaveRemovida> ChavesRemovidas { get; } = [.. ChavesRemovidas];

    /// <summary>As tabelas cujas triggers o diário desliga, copiadas.</summary>
    public IReadOnlyList<NomeObjeto> TriggersDesligadas { get; } = [.. TriggersDesligadas];

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(DadosDeExecucao)} ({Unidades.Count} unidades, {Dominios.Count} domínios)";
}

/// <summary>Uma chave estrangeira do destino que o diário remove e recria.</summary>
/// <param name="Tabela">A tabela que tem a chave (o filho), como o catálogo a guarda.</param>
/// <param name="Chave">O nome da chave.</param>
internal sealed record ChaveRemovida(NomeTabela Tabela, string Chave)
{
    /// <inheritdoc/>
    public override string ToString() => nameof(ChaveRemovida);
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

/// <summary>Uma unidade de cópia: de onde ler e onde gravar a tabela, e o que tratar nela.</summary>
/// <param name="Nome">O nome no catálogo da origem (ou do destino, na unidade que só existe nele).</param>
/// <param name="Acao">A ação efetiva.</param>
/// <param name="NoDestino">O nome da tabela no catálogo do destino; nulo quando ela não existe no destino.</param>
/// <param name="ColunasLidas">
/// As colunas lidas da origem, na ordem do catálogo dela, sem as geradas de um lado ou do outro (o banco as recalcula); só
/// na unidade copiada.
/// </param>
/// <param name="ColunasNoDestino">O nome no catálogo do destino de cada coluna lida, na mesma ordem.</param>
/// <param name="Tratadas">As colunas com transformador, na ordem do catálogo da origem; cada uma, e a referência dela, lidas.</param>
/// <remarks>
/// Se a leitura e o esvaziamento se limitam à própria tabela e se a gravação pode congelar as linhas, o dialeto decide pelo
/// catálogo dele: a carga não guarda uma segunda fonte disso.
/// </remarks>
internal sealed record UnidadeDeCopia(
    NomeTabela Nome,
    AcaoTabela Acao,
    NomeTabela? NoDestino,
    IReadOnlyList<string> ColunasLidas,
    IReadOnlyList<string> ColunasNoDestino,
    IReadOnlyList<ColunaTratada> Tratadas)
{
    /// <summary>As colunas lidas, copiadas.</summary>
    public IReadOnlyList<string> ColunasLidas { get; } = Lidas(Acao, NoDestino, ColunasLidas, ColunasNoDestino, Tratadas);

    /// <summary>As colunas no destino, copiadas.</summary>
    public IReadOnlyList<string> ColunasNoDestino { get; } = [.. ColunasNoDestino];

    /// <summary>As colunas tratadas, copiadas.</summary>
    public IReadOnlyList<ColunaTratada> Tratadas { get; } = [.. Tratadas];

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(UnidadeDeCopia)} ({ColunasLidas.Count} colunas, {Tratadas.Count} tratadas)";

    // Uma tratada fora das lidas, ou com a referência fora delas, ficaria sem valor no pipeline; a copiada ou esvaziada sem a
    // tabela no destino não teria onde gravar; a não copiada não lê nem trata: tudo isso é defeito do motor.
    private static string[] Lidas(AcaoTabela acao, NomeTabela? tabelaNoDestino, IReadOnlyList<string> lidas, IReadOnlyList<string> noDestino, IReadOnlyList<ColunaTratada> tratadas)
    {
        ArgumentNullException.ThrowIfNull(lidas);
        ArgumentNullException.ThrowIfNull(noDestino);
        ArgumentNullException.ThrowIfNull(tratadas);
        if (!Enum.IsDefined(acao)
            || (acao is AcaoTabela.Copiar or AcaoTabela.Esvaziar && tabelaNoDestino is null)
            || (acao != AcaoTabela.Copiar && (lidas.Count > 0 || tratadas.Count > 0))
            || tratadas.Any(tratada => tratada is null)
            || tratadas.Select(tratada => tratada.Coluna).Distinct(StringComparer.Ordinal).Count() != tratadas.Count)
        {
            throw new InvalidOperationException("A unidade de cópia se contradiz: ação, tabela no destino ou colunas tratadas.");
        }

        var copia = lidas.ToArray();
        var conjunto = copia.ToHashSet(StringComparer.Ordinal);
        if (copia.Length != noDestino.Count || conjunto.Count != copia.Length
            || tratadas.Any(tratada => !conjunto.Contains(tratada.Coluna) || (tratada.Referencia is { } referencia && !conjunto.Contains(referencia))))
        {
            throw new InvalidOperationException("A unidade de cópia se contradiz: colunas lidas sem par, repetidas, ou tratada sem a coluna lida.");
        }

        return copia;
    }
}

/// <summary>
/// Uma coluna com transformador: o que o executor precisa para recriá-lo com a chave real e conferi-lo contra o plano,
/// nunca a instância. A fábrica registrada guarda o nome, a versão e a injetividade conferidos na criação do planejar.
/// </summary>
/// <param name="Coluna">O nome da coluna no catálogo.</param>
/// <param name="Fabrica">A fábrica registrada.</param>
/// <param name="Regra">A regra conferida pela fábrica.</param>
/// <param name="Normalizados">Os parâmetros normalizados pela fábrica, por nome (comparação ordinal).</param>
/// <param name="Contexto">O contexto de criação, montado pela coluna do destino, com que o planejar criou o transformador.</param>
/// <param name="Referencia">O nome no catálogo da origem da coluna de <c>derivarDe</c>; nulo sem ele.</param>
/// <param name="InjetividadeEfetiva">
/// A injetividade que o plano publica para a coluna, nunca acima da registrada pela fábrica (a colação do destino pode
/// rebaixá-la).
/// </param>
internal sealed record ColunaTratada(
    string Coluna,
    FabricaRegistrada Fabrica,
    RegraTransformacao Regra,
    IReadOnlyDictionary<string, string> Normalizados,
    ContextoDaColuna Contexto,
    string? Referencia,
    Injetividade InjetividadeEfetiva)
{
    /// <summary>A injetividade efetiva, conferida contra a registrada: acima dela, o plano prometeria o que a fábrica não dá.</summary>
    public Injetividade InjetividadeEfetiva { get; } = Enum.IsDefined(InjetividadeEfetiva) && InjetividadeEfetiva <= Fabrica.Injetividade
        ? InjetividadeEfetiva
        : throw new InvalidOperationException("A injetividade efetiva passa da registrada pela fábrica.");

    /// <summary>A coluna de <c>derivarDe</c>, sempre junto com o tipo dela no contexto (um sem o outro é defeito).</summary>
    public string? Referencia { get; } = (Referencia is null) == (Contexto?.TipoReferencia is null)
        ? Referencia
        : throw new InvalidOperationException("A referência de 'derivarDe' e o tipo dela no contexto não andam juntos.");

    /// <summary>Os parâmetros normalizados, copiados.</summary>
    public IReadOnlyDictionary<string, string> Normalizados { get; } = new Dictionary<string, string>(Normalizados, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => nameof(ColunaTratada);
}
