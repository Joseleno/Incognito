using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CodeProcess.Incognito.Testes.PostgreSql.ServidorPostgreSql))]

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O servidor dos testes: um contêiner por execução (fixture de assembly), na versão de <c>INCOGNITO_POSTGRES</c> (padrão
/// 18), com a imagem fixada por digest no <c>Dockerfile</c> da versão. Cria os papéis dos testes e os dois bancos-modelo com
/// o esquema de conformidade, que cada teste clona: o da origem, todo do administrador, e o do destino, todo do dono.
/// </summary>
/// <remarks>
/// Três logins: o administrador do contêiner (monta e apaga), <c>incognito_leitor</c> (lê a origem, nunca dono) e
/// <c>incognito_dono</c> (dono do destino), os dois últimos sem superusuário; e <c>incognito_outro</c>, sem login, dono de
/// alguns objetos nos dois bancos. Senhas aleatórias por execução, com o sentinela: nenhuma saída pode trazê-las.
/// </remarks>
public sealed class ServidorPostgreSql : IAsyncLifetime
{
    /// <summary>Texto que nenhuma saída do dialeto pode trazer (está nas senhas e no esquema de conformidade).</summary>
    public const string Sentinela = "zqSENTINELAqz";

    /// <summary>
    /// O sentinela onde só cabem 8 letras minúsculas (a subtag privada do locale de uma colação ICU, que o BCP 47 limita).
    /// </summary>
    public const string SentinelaCurto = "zqsentin";

    /// <summary>O login que lê a origem.</summary>
    public const string Leitor = "incognito_leitor";

    /// <summary>O login dono do destino.</summary>
    public const string Dono = "incognito_dono";

    /// <summary>O papel sem login, nem leitor nem dono, que tem alguns objetos do esquema de conformidade.</summary>
    public const string Outro = "incognito_outro";

    private const string Administrador = "postgres";
    private const int VersaoPadrao = 18;

    // Um clone por vez: em série, uma falha de clonagem (o PostgreSQL recusa clonar um modelo com outra conexão, como a
    // de um autovacuum que não o larga a tempo) fica num teste só.
    private readonly SemaphoreSlim _clonagem = new(1, 1);
    private readonly string _senhaAdministrador = NovaSenha();
    private PostgreSqlContainer? _conteiner;
    private string? _modeloOrigem;
    private string? _modeloDestino;

    /// <summary>A senha do leitor.</summary>
    public string SenhaLeitor { get; } = NovaSenha();

    /// <summary>A senha do dono do destino.</summary>
    public string SenhaDono { get; } = NovaSenha();

    /// <summary>A versão do servidor (<c>server_version_num</c>).</summary>
    public int Versao { get; private set; }

    /// <summary>Sobe o contêiner, cria os papéis e os bancos-modelo.</summary>
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
            $"CREATE ROLE {Leitor} LOGIN PASSWORD '{SenhaLeitor}'; CREATE ROLE {Dono} LOGIN PASSWORD '{SenhaDono}'; CREATE ROLE {Outro};",
            cancelamento);

        _modeloOrigem = NovoNome("modelo_origem");
        await ExecutarAsync(Administrador, $"CREATE DATABASE {_modeloOrigem}", cancelamento);
        await ExecutarAsync(_modeloOrigem, LerRecurso("esquema-de-conformidade.sql"), cancelamento);
        await ConferirAsync(_modeloOrigem, "presenca-do-sentinela.sql", "Lugares do esquema de conformidade sem o sentinela", cancelamento);

        // O mesmo esquema, com a posse passada ao dono e conferida por uma guarda que não depende do leitor.
        _modeloDestino = NovoNome("modelo_destino");
        await ExecutarAsync(Administrador, $"CREATE DATABASE {_modeloDestino}", cancelamento);
        await ExecutarAsync(_modeloDestino, LerRecurso("esquema-de-conformidade.sql"), cancelamento);
        await ExecutarAsync(_modeloDestino, LerRecurso("posse-do-destino.sql"), cancelamento);
        await ConferirAsync(_modeloDestino, "guarda-de-posse.sql", "Objetos do destino que continuam do administrador (acrescentar à posse-do-destino.sql)", cancelamento);
        await ConferirAsync(_modeloDestino, "presenca-do-sentinela.sql", "Lugares do esquema de conformidade sem o sentinela no destino", cancelamento);

        // As conexões do administrador não ficam em pool: fechadas aqui, os modelos podem ser clonados.
        await ExecutarAsync(
            Administrador,
            $"ALTER DATABASE {_modeloOrigem} ALLOW_CONNECTIONS false; ALTER DATABASE {_modeloDestino} ALLOW_CONNECTIONS false",
            cancelamento);
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

    /// <summary>Clona o banco-modelo da origem num banco novo (a origem de um teste).</summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O nome do banco.</returns>
    public Task<string> ClonarModeloAsync(CancellationToken cancelamento) => ClonarAsync("origem", _modeloOrigem, "", cancelamento);

    /// <summary>Clona o banco-modelo do destino num banco novo do dono (o destino de um teste).</summary>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>O nome do banco.</returns>
    /// <remarks>O clone não herda o dono do modelo: o dono vem no CREATE DATABASE.</remarks>
    public Task<string> CriarDestinoAsync(CancellationToken cancelamento) => ClonarAsync("destino", _modeloDestino, $" OWNER {Dono}", cancelamento);

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

    /// <summary>A string de conexão do administrador do servidor a um banco (para ler como quem vê o catálogo inteiro).</summary>
    /// <param name="banco">O banco.</param>
    /// <returns>A string de conexão.</returns>
    public string StringDeConexaoDoAdministrador(string banco) => StringDoAdministrador(banco);

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

    private static string LerRecurso(string nome)
    {
        using var recurso = typeof(ServidorPostgreSql).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"{nome} não embutido.");
        using var leitor = new StreamReader(recurso);
        return leitor.ReadToEnd();
    }

    // Um clone por vez, atrás do mesmo semáforo para os dois modelos.
    private async Task<string> ClonarAsync(string papel, string? modelo, string complemento, CancellationToken cancelamento)
    {
        var nome = NovoNome(papel);
        await _clonagem.WaitAsync(cancelamento);
        try
        {
            await ExecutarAsync(Administrador, $"CREATE DATABASE {nome} TEMPLATE {modelo}{complemento}", cancelamento);
        }
        finally
        {
            _clonagem.Release();
        }

        return nome;
    }

    // Falha se a conferência do recurso devolve alguma linha: objeto do esquema que sobrou com o administrador (o destino
    // seria lido por um dono que não é dono de tudo) ou lugar sem o sentinela (o canário não provaria nada ali).
    private async Task ConferirAsync(string banco, string recurso, string problema, CancellationToken cancelamento)
    {
        await using var conexao = await AbrirComoAdministradorAsync(banco, cancelamento);
        await using var comando = new NpgsqlCommand(LerRecurso(recurso), conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento);
        var achados = new List<string>();
        while (await leitor.ReadAsync(cancelamento))
        {
            achados.Add(leitor.GetString(0));
        }

        if (achados.Count > 0)
        {
            throw new InvalidOperationException($"{problema}: {string.Join(", ", achados)}.");
        }
    }

    private PostgreSqlContainer Conteiner() => _conteiner ?? throw new InvalidOperationException("O servidor ainda não subiu.");

    private string StringDoAdministrador(string banco) => new NpgsqlConnectionStringBuilder(StringDeConexao(banco, Administrador, _senhaAdministrador))
    {
        // Sem pool: uma conexão aberta no modelo impediria o clone.
        Pooling = false,
    }.ConnectionString;
}
