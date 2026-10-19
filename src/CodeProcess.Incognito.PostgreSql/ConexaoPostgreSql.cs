using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A conexão de controle com um banco PostgreSQL: lê a string de conexão do usuário, impõe por cima o que o Incognito
/// exige e abre uma conexão só, sem pool, para as partes de metadados, que o contrato não chama em paralelo.
/// </summary>
/// <remarks>
/// As mensagens do próprio dialeto nunca trazem nada da string de conexão: os erros do parser do Npgsql podem trazer
/// pedaços dela (uma senha sem aspas com <c>;</c> vira "chave"), e o <c>ToString</c> do construtor traz a senha. As
/// exceções do Npgsql na abertura (rede, autenticação, banco inexistente) passam como vieram, com servidor, usuário ou
/// banco: o motor deve tratá-las como falha sem mensagem.
/// </remarks>
internal sealed class ConexaoPostgreSql : IAsyncDisposable
{
    /// <summary>A menor versão suportada (<c>server_version_num</c>).</summary>
    internal const int VersaoMinima = 150000;

    private const string NomeDaAplicacao = "Incognito";
    private const string CaminhoDeBusca = "pg_catalog";
    private const string Utf8 = "UTF8";

    // Os métodos de autenticação aceitos por padrão: todos menos a senha em claro (password), que um servidor hostil ou
    // alguém no meio pediria depois de recusar o TLS. None é o trust do pg_hba, que não manda credencial. GSS e SSPI (a
    // credencial do usuário do sistema) só quando a string não traz senha: com senha, um servidor hostil que pedisse SSPI
    // receberia a resposta NTLM de quem roda o Incognito.
    private static readonly string[] _autenticacoesComSenha = ["ScramSHA256", "MD5", "None"];
    private static readonly string[] _autenticacoesSemSenha = ["ScramSHA256", "MD5", "GSS", "SSPI", "None"];
    private static readonly string[] _integradas = ["GSS", "SSPI"];

    // Os servidores na própria máquina, onde o TLS não protege nada.
    private static readonly string[] _locais = ["localhost", "127.0.0.1", "::1"];

    private readonly NpgsqlDataSource _fonte;
    private readonly NpgsqlConnection _conexao;

    private ConexaoPostgreSql(NpgsqlDataSource fonte, NpgsqlConnection conexao, int versao)
    {
        _fonte = fonte;
        _conexao = conexao;
        Versao = versao;
    }

    /// <summary>A versão do servidor (<c>server_version_num</c>, como 180006).</summary>
    internal int Versao { get; }

    /// <summary>A conexão aberta.</summary>
    internal NpgsqlConnection Conexao => _conexao;

    /// <summary>Lê a string de conexão, impõe as opções do Incognito, conecta e confere a versão e a sessão.</summary>
    /// <param name="segredo">A string de conexão.</param>
    /// <param name="origem">Se é a origem (sessão só de leitura).</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>A conexão aberta, que o chamador descarta.</returns>
    /// <exception cref="IncognitoException">String inválida ou recusada, versão não suportada, sessão divergente.</exception>
    internal static async Task<ConexaoPostgreSql> AbrirAsync(SegredoConexao segredo, bool origem, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(segredo);
        var construtor = Ler(segredo);
        Impor(construtor, origem);

        var fonte = Construir(construtor, origem);
        NpgsqlConnection? conexao = null;
        try
        {
            try
            {
                conexao = await fonte.OpenConnectionAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception erro) when (erro is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException or NotSupportedException)
            {
                // Uma opção que só se resolve na abertura (arquivo de senha, certificado, método de autenticação): a
                // mensagem traz o caminho ou o valor escrito.
                throw OpcaoInutilizavel();
            }

            var versao = await ConferirAsync(conexao, origem, cancelamento).ConfigureAwait(false);
            return new ConexaoPostgreSql(fonte, conexao, versao);
        }
        catch
        {
            if (conexao is not null)
            {
                await conexao.DisposeAsync().ConfigureAwait(false);
            }

            await fonte.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Fecha a conexão e a fonte.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _conexao.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _fonte.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static NpgsqlConnectionStringBuilder Ler(SegredoConexao segredo)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(segredo.Revelar());
        }
        catch (Exception erro) when (erro is ArgumentException or FormatException or InvalidCastException or OverflowException or KeyNotFoundException)
        {
            // Sem a mensagem nem a exceção original: as duas podem trazer pedaços da string.
            throw new IncognitoException(CodigosPostgreSql.StringInvalida, "A string de conexão do PostgreSQL não pôde ser lida.");
        }
    }

    private static IncognitoException OpcaoInutilizavel() => new(
        CodigosPostgreSql.StringInvalida,
        "A string de conexão do PostgreSQL tem uma opção que não pôde ser usada (por exemplo, arquivo de senha, certificado, método de autenticação ou negociação de TLS).");

    private static void Impor(NpgsqlConnectionStringBuilder construtor, bool origem)
    {
        if (string.IsNullOrWhiteSpace(construtor.Host) || string.IsNullOrWhiteSpace(construtor.Database))
        {
            // Sem servidor ou banco, o Npgsql cairia nas variáveis PG* do ambiente ou no nome do usuário.
            throw new IncognitoException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL precisa dizer o servidor (Host) e o banco (Database).");
        }

        if (construtor.Host.Contains(',', StringComparison.Ordinal) || construtor.LoadBalanceHosts)
        {
            // O bloqueio, o marcador, a identidade e o snapshot precisam do mesmo servidor em todas as conexões.
            throw new IncognitoException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL tem mais de um servidor ou balanceamento entre servidores (Load Balance Hosts), que o Incognito não aceita.");
        }

        // Opções obsoletas, lidas pelo nome para não usar as propriedades marcadas como obsoletas.
        if (Preenchida(construtor, "Server Compatibility Mode", "None") || Preenchida(construtor, "Load Table Composites", "False"))
        {
            throw new IncognitoException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL usa Server Compatibility Mode ou Load Table Composites, que o Incognito não aceita.");
        }

        var comSenha = !string.IsNullOrEmpty(construtor.Password) || !string.IsNullOrEmpty(construtor.Passfile);
        if (!comSenha && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGPASSWORD")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGPASSFILE"))))
        {
            // Sem senha na string, o Npgsql usaria a do ambiente, que pode ser de outro banco (a da produção, numa string
            // de destino descuidada).
            throw new IncognitoException(
                CodigosPostgreSql.OpcaoRecusada,
                "A string de conexão do PostgreSQL não tem senha, e PGPASSWORD ou PGPASSFILE estão definidas no ambiente: ponha a senha (ou o Passfile) na própria string.");
        }

        construtor.RequireAuth = Autenticacoes(construtor.RequireAuth, comSenha);

        // TLS obrigatório fora da própria máquina: no Prefer (padrão), um servidor sem TLS ou alguém no meio faria a sessão
        // seguir em claro. Disable e Allow escritos na string ficam: são escolha explícita.
        if (construtor.SslMode == SslMode.Prefer && !Local(construtor.Host))
        {
            construtor.SslMode = SslMode.Require;
        }

        // O detalhe do servidor, os parâmetros e o comando que falhou trazem valores de linha; a senha não volta pela
        // string.
        construtor.IncludeErrorDetail = false;
        construtor.LogParameters = false;
        construtor.IncludeFailedBatchedCommand = false;
        construtor.PersistSecurityInfo = false;

        // Uma conexão só, fora do pool, fora de uma transação do hospedeiro e sem multiplexação; sem tempo-limite por
        // comando (o cancelamento é do chamador, e o catálogo de um banco grande demora).
        construtor.Pooling = false;
        construtor.Enlist = false;
        construtor.Multiplexing = false;
        construtor.CommandTimeout = 0;
        construtor.ApplicationName = NomeDaAplicacao;

        // A codificação pelo parâmetro de início que o Npgsql manda (vence o Options e o PGCLIENTENCODING) e a do .NET:
        // com outra, nomes com acento sairiam trocados ou a leitura falharia.
        construtor.ClientEncoding = Utf8;
        construtor.Encoding = Utf8;

        // Pelo pacote de início, que vale antes da primeira consulta e vence ALTER ROLE/DATABASE ... SET e o PGOPTIONS:
        // só o pg_catalog no caminho de busca (nenhuma função ou operador do usuário sombreia os do sistema), a barra
        // invertida literal nas strings (as consultas usam literais), nenhum tempo-limite do servidor (a conexão de
        // controle fica parada entre os passos) e, na origem, transações só de leitura por padrão.
        construtor.SearchPath = CaminhoDeBusca;
        construtor.Options = $"-c search_path={CaminhoDeBusca} -c standard_conforming_strings=on"
            + " -c statement_timeout=0 -c idle_session_timeout=0 -c idle_in_transaction_session_timeout=0"
            + (origem ? " -c default_transaction_read_only=on" : "");
    }

    // Loopback ou socket Unix (caminho de diretório).
    private static bool Local(string servidor) =>
        _locais.Contains(servidor, StringComparer.OrdinalIgnoreCase)
        || servidor.StartsWith('/')
        || servidor.StartsWith("127.", StringComparison.Ordinal);

    // A lista do usuário sem a senha em claro, ou a lista padrão; uma negação (!) ou uma lista só com a senha em claro é
    // recusada.
    private static string Autenticacoes(string? pedidas, bool comSenha)
    {
        if (string.IsNullOrWhiteSpace(pedidas))
        {
            return string.Join(',', comSenha ? _autenticacoesComSenha : _autenticacoesSemSenha);
        }

        var metodos = pedidas.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var aceitos = metodos.Where(metodo => !metodo.Equals("Password", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (metodos.Any(metodo => metodo.StartsWith('!')) || aceitos.Length == 0)
        {
            throw new IncognitoException(
                CodigosPostgreSql.OpcaoRecusada,
                "A string de conexão do PostgreSQL pede só a autenticação por senha em claro (Require Auth=Password) ou usa negação em Require Auth; o Incognito não manda a senha em claro.");
        }

        return string.Join(',', aceitos);
    }

    private static bool Preenchida(NpgsqlConnectionStringBuilder construtor, string chave, string padrao) =>
        construtor.TryGetValue(chave, out var valor)
        && valor is not null
        && !string.Equals(Convert.ToString(valor, CultureInfo.InvariantCulture), padrao, StringComparison.OrdinalIgnoreCase);

    private static NpgsqlDataSource Construir(NpgsqlConnectionStringBuilder construtor, bool origem)
    {
        var fonte = new NpgsqlSlimDataSourceBuilder(construtor.ConnectionString)
        {
            // O nome do pool nas métricas, para não vir da string de conexão.
            Name = origem ? "incognito-origem" : "incognito-destino",
        };

        // O construtor enxuto não tem TLS sem isto: o Require falharia, e o Prefer (permitido só na própria máquina) nem
        // tentaria. A autenticação integrada só quando a lista a aceita.
        fonte.EnableTransportSecurity();
        if (construtor.RequireAuth!.Split(',').Intersect(_integradas, StringComparer.OrdinalIgnoreCase).Any())
        {
            fonte.EnableIntegratedSecurity();
        }

        fonte.EnableArrays();

        // Nunca o registro de quem hospeda a biblioteca: o Npgsql registra comandos e erros. Nenhum rastreamento de
        // comando, abertura física ou primeira resposta.
        fonte.UseLoggerFactory(NullLoggerFactory.Instance);
        fonte.EnableParameterLogging(false);
        fonte.ConfigureTracing(rastreamento => rastreamento
            .ConfigureCommandFilter(_ => false)
            .ConfigureBatchFilter(_ => false)
            .ConfigureCopyOperationFilter(_ => false)
            .EnablePhysicalOpenTracing(false)
            .EnableFirstResponseEvent(false));
        try
        {
            return fonte.Build();
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Uma combinação de opções que o Npgsql recusa ao montar (por exemplo, SSL Negotiation=Direct com Prefer).
            throw OpcaoInutilizavel();
        }
    }

    private static async Task<int> ConferirAsync(NpgsqlConnection conexao, bool origem, CancellationToken cancelamento)
    {
        // O papel também: um gatilho de login pode trocar de papel (SET ROLE) e ler com outros privilégios.
        await using var comando = new NpgsqlCommand(
            """
            SELECT pg_catalog.current_setting('server_version_num'), pg_catalog.current_setting('search_path'),
                   pg_catalog.current_setting('default_transaction_read_only'), pg_catalog.current_setting('client_encoding'),
                   pg_catalog.current_setting('standard_conforming_strings'), current_user = session_user
            """,
            conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        var versaoLida = leitor.GetString(0);

        // O número, nunca o texto da versão (o servidor controla o texto).
        if (!int.TryParse(versaoLida, NumberStyles.None, CultureInfo.InvariantCulture, out var versao))
        {
            throw new IncognitoException(CodigosPostgreSql.VersaoNaoSuportada, "O servidor PostgreSQL informou uma versão que não é um número.");
        }

        if (versao < VersaoMinima)
        {
            throw new IncognitoException(
                CodigosPostgreSql.VersaoNaoSuportada,
                $"O servidor PostgreSQL está na versão {versao / 10000}; o Incognito exige a {VersaoMinima / 10000} ou mais nova.");
        }

        if (leitor.GetString(1) != CaminhoDeBusca || (origem && leitor.GetString(2) != "on") || leitor.GetString(3) != Utf8 || leitor.GetString(4) != "on"
            || !leitor.GetBoolean(5))
        {
            throw new IncognitoException(
                CodigosPostgreSql.SessaoDivergente,
                "A sessão do PostgreSQL não ficou com os parâmetros que o Incognito impõe (search_path, client_encoding, standard_conforming_strings, o papel do login e, na origem, default_transaction_read_only).");
        }

        return versao;
    }
}
