using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CodeProcess.Incognito.Testes.PostgreSql.ServidorPostgreSql))]

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O servidor dos testes: um contêiner por execução (fixture de assembly), na versão de <c>INCOGNITO_POSTGRES</c> (padrão
/// 18), com a imagem fixada por digest no <c>Dockerfile</c> da versão. Cria os logins dos testes e o banco-modelo com o
/// esquema de conformidade, que cada teste clona.
/// </summary>
/// <remarks>
/// Três logins: o administrador do contêiner (monta e apaga), <c>incognito_leitor</c> (lê a origem, nunca dono) e
/// <c>incognito_dono</c> (dono do destino), os dois últimos sem superusuário. Senhas aleatórias por execução, com o
/// sentinela: nenhuma saída pode trazê-las.
/// </remarks>
public sealed class ServidorPostgreSql : IAsyncLifetime
{
    /// <summary>Texto que nenhuma saída do dialeto pode trazer (está nas senhas e no esquema de conformidade).</summary>
    public const string Sentinela = "zqSENTINELAqz";

    /// <summary>O login que lê a origem.</summary>
    public const string Leitor = "incognito_leitor";

    /// <summary>O login dono do destino.</summary>
    public const string Dono = "incognito_dono";

    private const string Administrador = "postgres";
    private const int VersaoPadrao = 18;

    // Um clone por vez: o PostgreSQL recusa clonar um modelo com conexão aberta, e os clones em série deixam a falha, se
    // houver, num teste só.
    private readonly SemaphoreSlim _clonagem = new(1, 1);
    private readonly string _senhaAdministrador = NovaSenha();
    private PostgreSqlContainer? _conteiner;
    private string? _modelo;

    /// <summary>A senha do leitor.</summary>
    public string SenhaLeitor { get; } = NovaSenha();

    /// <summary>A senha do dono do destino.</summary>
    public string SenhaDono { get; } = NovaSenha();

    /// <summary>A versão do servidor (<c>server_version_num</c>).</summary>
    public int Versao { get; private set; }

    /// <summary>Sobe o contêiner, cria os logins e o banco-modelo.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask InitializeAsync()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var pedida = VersaoPedida();
        _conteiner = Conteiner(Imagem(pedida), _senhaAdministrador);
        await _conteiner.StartAsync(cancelamento);

        // A imagem do Dockerfile precisa ser da versão pedida: um FROM trocado testaria outra versão sem avisar.
        var lida = (string?)await ConsultarAsync(Administrador, "SELECT current_setting('server_version_num')", cancelamento);
        if (!int.TryParse(lida, NumberStyles.None, CultureInfo.InvariantCulture, out var versao) || versao / 10000 != pedida)
        {
            throw new InvalidOperationException($"O contêiner do PostgreSQL {pedida} informou a versão {lida}: confira o FROM de imagens/{pedida}/Dockerfile.");
        }

        Versao = versao;
        await ExecutarAsync(
            Administrador,
            $"CREATE ROLE {Leitor} LOGIN PASSWORD '{SenhaLeitor}'; CREATE ROLE {Dono} LOGIN PASSWORD '{SenhaDono}';",
            cancelamento);

        _modelo = NovoNome("modelo");
        await ExecutarAsync(Administrador, $"CREATE DATABASE {_modelo}", cancelamento);
        await ExecutarAsync(_modelo, LerEsquemaDeConformidade(), cancelamento);

        // As conexões do administrador não ficam em pool: fechadas aqui, o modelo pode ser clonado.
        await ExecutarAsync(Administrador, $"ALTER DATABASE {_modelo} ALLOW_CONNECTIONS false", cancelamento);
    }

    /// <summary>Para o contêiner.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_conteiner is not null)
        {
            await _conteiner.DisposeAsync();
        }

        _clonagem.Dispose();
    }

    /// <summary>Clona o banco-modelo num banco novo (a origem de um teste).</summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O nome do banco.</returns>
    public async Task<string> ClonarModeloAsync(CancellationToken cancelamento)
    {
        var nome = NovoNome("origem");
        await _clonagem.WaitAsync(cancelamento);
        try
        {
            await ExecutarAsync(Administrador, $"CREATE DATABASE {nome} TEMPLATE {_modelo}", cancelamento);
        }
        finally
        {
            _clonagem.Release();
        }

        return nome;
    }

    /// <summary>Cria um banco vazio do dono (o destino de um teste).</summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O nome do banco.</returns>
    public async Task<string> CriarDestinoAsync(CancellationToken cancelamento)
    {
        var nome = NovoNome("destino");
        await ExecutarAsync(Administrador, $"CREATE DATABASE {nome} OWNER {Dono}", cancelamento);
        return nome;
    }

    /// <summary>Apaga o banco, derrubando as conexões que sobrarem.</summary>
    /// <param name="banco">O banco.</param>
    /// <returns>A tarefa.</returns>
    public Task ApagarAsync(string banco) => ExecutarAsync(Administrador, $"DROP DATABASE IF EXISTS {banco} WITH (FORCE)", CancellationToken.None);

    /// <summary>A string de conexão de um login a um banco.</summary>
    /// <param name="banco">O banco.</param>
    /// <param name="usuario">O login.</param>
    /// <param name="senha">A senha.</param>
    /// <returns>A string de conexão.</returns>
    public string StringDeConexao(string banco, string usuario, string senha) => new NpgsqlConnectionStringBuilder
    {
        Host = Conteiner().Hostname,
        Port = Conteiner().GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort),
        Database = banco,
        Username = usuario,
        Password = senha,
    }.ConnectionString;

    /// <summary>Executa comandos como administrador num banco.</summary>
    /// <param name="banco">O banco.</param>
    /// <param name="sql">Os comandos.</param>
    /// <param name="cancelamento">Cancela a execução.</param>
    /// <returns>A tarefa.</returns>
    public async Task ExecutarAsync(string banco, string sql, CancellationToken cancelamento)
    {
        await using var conexao = new NpgsqlConnection(StringDoAdministrador(banco));
        await conexao.OpenAsync(cancelamento);
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(cancelamento);
    }

    /// <summary>Abre uma conexão do administrador num banco, que o chamador descarta.</summary>
    /// <param name="banco">O banco.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A conexão aberta.</returns>
    public async Task<NpgsqlConnection> AbrirComoAdministradorAsync(string banco, CancellationToken cancelamento)
    {
        var conexao = new NpgsqlConnection(StringDoAdministrador(banco));
        await conexao.OpenAsync(cancelamento);
        return conexao;
    }

    /// <summary>Consulta um valor como administrador num banco.</summary>
    /// <param name="banco">O banco.</param>
    /// <param name="sql">A consulta.</param>
    /// <param name="cancelamento">Cancela a consulta.</param>
    /// <returns>O valor.</returns>
    public async Task<object?> ConsultarAsync(string banco, string sql, CancellationToken cancelamento)
    {
        await using var conexao = new NpgsqlConnection(StringDoAdministrador(banco));
        await conexao.OpenAsync(cancelamento);
        await using var comando = new NpgsqlCommand(sql, conexao);
        return await comando.ExecuteScalarAsync(cancelamento);
    }

    /// <summary>Sobe um contêiner de outra versão (para provar a recusa de versão), que o chamador descarta.</summary>
    /// <param name="versao">A versão principal, com <c>Dockerfile</c> em <c>imagens/</c>.</param>
    /// <param name="senha">A senha do administrador.</param>
    /// <returns>O contêiner, ainda parado.</returns>
    internal static PostgreSqlContainer Conteiner(int versao, string senha) => Conteiner(Imagem(versao), senha);

    /// <summary>Uma senha aleatória com o sentinela.</summary>
    /// <returns>A senha.</returns>
    internal static string NovaSenha() => Sentinela + Guid.NewGuid().ToString("N");

    private static PostgreSqlContainer Conteiner(string imagem, string senha) => new PostgreSqlBuilder(imagem)
        .WithPassword(senha)

        // Clones e DDL mais rápidos: o banco é descartável.
        .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off", "-c", "synchronous_commit=off")

        // A porta só na interface local da máquina, nunca em todas.
        .WithCreateParameterModifier(parametros =>
        {
            foreach (var ligacao in parametros.HostConfig?.PortBindings?.Values.SelectMany(ligacoes => ligacoes ?? []) ?? [])
            {
                ligacao.HostIP = "127.0.0.1";
            }
        })
        .Build();

    private static int VersaoPedida()
    {
        var pedida = Environment.GetEnvironmentVariable("INCOGNITO_POSTGRES");
        if (string.IsNullOrEmpty(pedida))
        {
            return VersaoPadrao;
        }

        return int.TryParse(pedida, NumberStyles.None, CultureInfo.InvariantCulture, out var versao)
            ? versao
            : throw new InvalidOperationException("INCOGNITO_POSTGRES precisa ser a versão principal do PostgreSQL (por exemplo, 18).");
    }

    // A imagem do FROM do Dockerfile da versão, que precisa estar fixada por digest.
    private static string Imagem(int versao)
    {
        var arquivo = Path.Combine(AppContext.BaseDirectory, "imagens", versao.ToString(CultureInfo.InvariantCulture), "Dockerfile");
        if (!File.Exists(arquivo))
        {
            throw new InvalidOperationException($"Sem imagem do PostgreSQL {versao} em imagens/{versao}/Dockerfile.");
        }

        var imagem = File.ReadLines(arquivo)
            .Select(linha => linha.Trim())
            .Where(linha => linha.StartsWith("FROM ", StringComparison.Ordinal))
            .Select(linha => linha["FROM ".Length..].Trim())
            .Single();
        return imagem.Contains("@sha256:", StringComparison.Ordinal)
            ? imagem
            : throw new InvalidOperationException($"A imagem do PostgreSQL {versao} precisa estar fixada por digest (@sha256:).");
    }

    private static string NovoNome(string papel) => $"inc_{papel}_{Guid.NewGuid():N}";

    private static string LerEsquemaDeConformidade()
    {
        using var recurso = typeof(ServidorPostgreSql).Assembly.GetManifestResourceStream("esquema-de-conformidade.sql")
            ?? throw new InvalidOperationException("Esquema de conformidade não embutido.");
        using var leitor = new StreamReader(recurso);
        return leitor.ReadToEnd();
    }

    private PostgreSqlContainer Conteiner() => _conteiner ?? throw new InvalidOperationException("O servidor ainda não subiu.");

    private string StringDoAdministrador(string banco) => new NpgsqlConnectionStringBuilder(StringDeConexao(banco, Administrador, _senhaAdministrador))
    {
        // Sem pool: uma conexão aberta no modelo impediria o clone.
        Pooling = false,
    }.ConnectionString;
}
