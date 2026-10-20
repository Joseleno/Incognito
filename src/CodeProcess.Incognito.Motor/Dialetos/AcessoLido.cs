using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

// O que depende do login que leu o esquema, separado do esquema: as permissões no banco e em cada objeto. Só capacidades
// pela operação do motor e nomes de catálogo; nunca o texto de uma política nem a lista de privilégios.

/// <summary>
/// As permissões do login que leu o esquema: no banco, em cada tabela, em cada objeto residual e em cada sequence. Lidas no
/// catálogo atual, não no instante da leitura do esquema: o <c>planejar</c> confere cedo, e a execução continua tratando a
/// falha de permissão.
/// </summary>
/// <remarks>
/// Fica fora da comparação entre a origem e o destino: o leitor da origem e o dono do destino veem permissões diferentes
/// do mesmo esquema. Na origem contam a leitura e a segurança por linha da leitura; no destino, o resto.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class AcessoLido
{
    private readonly PermissoesBanco _banco = null!;
    private readonly IReadOnlyList<PermissoesTabela> _tabelas = [];
    private readonly IReadOnlyList<PermissoesResidual> _residuais = [];
    private readonly IReadOnlyList<PermissoesSequence> _sequences = [];

    /// <summary>As permissões no banco.</summary>
    public required PermissoesBanco Banco { get => _banco; init => _banco = value ?? throw new ArgumentNullException(nameof(Banco)); }

    /// <summary>
    /// Uma por tabela, na ordem de <see cref="EsquemaBanco.Tabelas"/> (não conferido aqui: a suíte e o motor conferem).
    /// </summary>
    public required IReadOnlyList<PermissoesTabela> Tabelas { get => _tabelas; init => _tabelas = Lista.Copiar(value, nameof(Tabelas)); }

    /// <summary>
    /// Uma por objeto residual, na ordem de <see cref="EsquemaBanco.ObjetosResiduais"/> (não conferido aqui: a suíte e o motor
    /// conferem).
    /// </summary>
    public required IReadOnlyList<PermissoesResidual> Residuais { get => _residuais; init => _residuais = Lista.Copiar(value, nameof(Residuais)); }

    /// <summary>
    /// Uma por sequence, na ordem de <see cref="EsquemaBanco.Sequences"/> (não conferido aqui: a suíte e o motor conferem).
    /// </summary>
    public required IReadOnlyList<PermissoesSequence> Sequences { get => _sequences; init => _sequences = Lista.Copiar(value, nameof(Sequences)); }
}

/// <summary>As permissões do login no banco. Nulo em cada uma: não informado, o pior caso.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PermissoesBanco
{
    /// <summary>
    /// Se o login pode marcar o banco como destino do Incognito (no PostgreSQL, ser dono do banco, para o
    /// <c>COMMENT ON DATABASE</c>; no SQL Server, <c>db_owner</c> ou <c>ALTER</c> no banco).
    /// </summary>
    public bool? PodeMarcarBanco { get; init; }

    /// <summary>
    /// Se o login pode criar o esquema do diário do Incognito no banco (no PostgreSQL, <c>CREATE</c> no banco; no SQL Server,
    /// <c>CREATE SCHEMA</c>).
    /// </summary>
    public bool? PodeCriarEsquema { get; init; }
}

/// <summary>As permissões do login numa tabela, pela operação do motor.</summary>
/// <remarks>
/// Valem para um comando que nomeia esta tabela: a leitura pelo pai dispensa o privilégio na partição e usa as políticas de
/// segurança por linha do pai. Toda capacidade exige também o acesso ao esquema da tabela. Nulo em cada uma: não informado
/// ou a tabela sumiu entre a leitura do esquema e a das permissões; o pior caso.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PermissoesTabela
{
    private readonly NomeObjeto _tabela = null!;
    private readonly IReadOnlyList<string> _colunasSemLeitura = [];

    /// <summary>A tabela.</summary>
    public required NomeObjeto Tabela { get => _tabela; init => _tabela = value ?? throw new ArgumentNullException(nameof(Tabela)); }

    /// <summary>
    /// Se o login lê o valor real de todas as colunas (no PostgreSQL, <c>SELECT</c> em cada coluna, ou na tabela se ela não
    /// tem colunas; no SQL Server, também sem a máscara: a coluna mascarada sem <c>UNMASK</c> não é lida).
    /// </summary>
    public bool? PodeLer { get; init; }

    /// <summary>
    /// As colunas que o login não lê, na ordem das colunas da tabela, para a mensagem: quem decide é <see cref="PodeLer"/>.
    /// Vazia se ele lê todas, e também quando o que falta é só o acesso ao esquema da tabela.
    /// </summary>
    public IReadOnlyList<string> ColunasSemLeitura { get => _colunasSemLeitura; init => _colunasSemLeitura = Lista.CopiarNomes(value, nameof(ColunasSemLeitura)); }

    /// <summary>
    /// Se o login grava todas as colunas que a carga grava (no PostgreSQL, <c>INSERT</c> na tabela ou em cada coluna que não é
    /// gerada; no SQL Server, pela forma de carga do motor, que pode exigir <c>ALTER</c>).
    /// </summary>
    public bool? PodeInserir { get; init; }

    /// <summary>
    /// Se o login grava a coluna de identidade com o valor que veio (no PostgreSQL, o mesmo que inserir; no SQL Server, a
    /// carga que mantém a identidade exige <c>ALTER</c>).
    /// </summary>
    public bool? PodeGravarIdentidade { get; init; }

    /// <summary>
    /// Se o login esvazia a tabela (no PostgreSQL, <c>TRUNCATE</c>; no SQL Server, <c>ALTER</c>). Não basta quando uma chave
    /// estrangeira de uma tabela fora da cópia a referencia: o <c>planejar</c> compõe com as chaves que chegam.
    /// </summary>
    public bool? PodeEsvaziar { get; init; }

    /// <summary>
    /// Se o login cria uma chave estrangeira que referencia a tabela, ao restaurar as chaves (<c>REFERENCES</c> na tabela;
    /// no PostgreSQL, também em todas as colunas).
    /// </summary>
    public bool? PodeReferenciar { get; init; }

    /// <summary>
    /// Se o login desliga e restaura as restrições da tabela (no PostgreSQL, sendo dono dela, com herança do papel dono; no
    /// SQL Server, <c>ALTER</c>).
    /// </summary>
    public bool? PodeAlterarRestricoes { get; init; }

    /// <summary>
    /// Se o login desliga as triggers de usuário da tabela (no PostgreSQL, sendo dono dela; no SQL Server, <c>ALTER</c>).
    /// Numa particionada, não alcança a trigger própria de uma partição de outro dono: o <c>planejar</c> confere em cada
    /// partição.
    /// </summary>
    public bool? PodeDesligarTriggers { get; init; }

    /// <summary>
    /// Se a segurança por linha vale para este login na leitura: ela pode sair filtrada, sem erro (no PostgreSQL, também
    /// sem política nenhuma, que nega tudo; no SQL Server, uma política de filtro ligada).
    /// </summary>
    public bool? LeituraFiltradaPorLinha { get; init; }

    /// <summary>
    /// Se a segurança por linha vale para este login na gravação (no PostgreSQL, a carga em massa falha com qualquer
    /// política, mesmo só de leitura; no SQL Server, uma política de bloqueio ligada recusa a linha barrada e falha a carga).
    /// </summary>
    public bool? EscritaRestritaPorLinha { get; init; }
}

/// <summary>As permissões do login num objeto residual.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PermissoesResidual
{
    private readonly NomeObjeto _nome = null!;
    private readonly TipoObjetoResidual _tipo;

    /// <summary>O objeto.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>O tipo do objeto.</summary>
    public required TipoObjetoResidual Tipo
    {
        get => _tipo;
        init => _tipo = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Tipo), "Tipo de objeto residual desconhecido.");
    }

    /// <summary>
    /// Se o login limpa o objeto: atualiza a materialized view (no PostgreSQL, pelo privilégio <c>MAINTAIN</c> do 17 em
    /// diante ou sendo dono dela) ou apaga os large objects (sendo dono de todos, ou com o modo de compatibilidade dos
    /// privilégios ligado). Nulo: não informado ou o objeto sumiu, o pior caso.
    /// </summary>
    public bool? PodeLimpar { get; init; }
}

/// <summary>As permissões do login numa sequence.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PermissoesSequence
{
    private readonly NomeObjeto _nome = null!;

    /// <summary>A sequence.</summary>
    public required NomeObjeto Nome { get => _nome; init => _nome = value ?? throw new ArgumentNullException(nameof(Nome)); }

    /// <summary>
    /// Se o login ajusta o próximo valor da sequence depois da carga (no PostgreSQL, <c>setval</c>, que exige <c>UPDATE</c> na
    /// sequence). Nulo: não informado ou a sequence sumiu, o pior caso.
    /// </summary>
    public bool? PodeAjustar { get; init; }
}

/// <summary>Como a conexão chega ao servidor.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class TransporteConexao
{
    /// <summary>
    /// Se a conexão é cifrada: <c>true</c> só quando a conexão exige a cifra; <c>false</c> quando não há cifra (pedida assim,
    /// vista pelo servidor ou por socket); nulo quando a cifra foi só preferida e o servidor diz que há (um intermediário pode
    /// cifrar só o trecho dele): trate como em claro. Cifrada não diz com quem: sem <see cref="ServidorVerificado"/>, um
    /// intermediário ativo pode decifrar. No PostgreSQL só o TLS conta: a cifra pelo GSSAPI sai como em claro.
    /// </summary>
    public bool? Cifrado { get; init; }

    /// <summary>
    /// Se o certificado do servidor foi conferido, com o nome dele; <c>true</c> só numa conexão cifrada. Nulo: não informado,
    /// trate como não verificado.
    /// </summary>
    public bool? ServidorVerificado { get; init; }

    /// <summary>
    /// Se o servidor está na própria máquina: endereço de loopback, <c>localhost</c> ou socket Unix; nunca por um prefixo do
    /// nome (<c>127.exemplo</c> é remoto).
    /// </summary>
    public required bool Local { get; init; }
}
