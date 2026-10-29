using System.Globalization;
using System.Net;
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

    /// <summary>O nome das conexões da sessão de leitura no <c>pg_stat_activity</c>, para quem administra as achar.</summary>
    internal const string NomeDaLeitura = "Incognito leitura";
    private const string CaminhoDeBusca = "pg_catalog";
    private const string Utf8 = "UTF8";

    // Os métodos de autenticação aceitos por padrão: todos menos a senha em claro (password), que um servidor hostil ou
    // alguém no meio pediria depois de recusar o TLS. None é o trust do pg_hba, que não manda credencial. GSS e SSPI (a
    // credencial do usuário do sistema) só quando a string não traz senha: com senha, um servidor hostil que pedisse SSPI
    // receberia a resposta NTLM de quem roda o Incognito.
    private static readonly string[] _autenticacoesComSenha = ["ScramSHA256", "MD5", "None"];
    private static readonly string[] _autenticacoesSemSenha = ["ScramSHA256", "MD5", "GSS", "SSPI", "None"];
    private static readonly string[] _integradas = ["GSS", "SSPI"];

    private readonly NpgsqlDataSource _fonte;
    private readonly NpgsqlConnection _conexao;

    // O modo de TLS e o servidor depois do que o Incognito impõe: o transporte sai deles.
    private readonly SslMode _modo;
    private readonly string _servidor;
    private readonly bool _origem;

    private ConexaoPostgreSql(NpgsqlDataSource fonte, NpgsqlConnection conexao, bool origem, int versao, RegraDosNomes nomes, SslMode modo, string servidor)
    {
        _fonte = fonte;
        _conexao = conexao;
        _origem = origem;
        Versao = versao;
        Nomes = nomes;
        _modo = modo;
        _servidor = servidor;
    }

    /// <summary>A versão do servidor (<c>server_version_num</c>, como 180006).</summary>
    internal int Versao { get; }

    /// <summary>Como o banco desta conexão decide a igualdade dos nomes: a codificação e o limite do tipo <c>name</c>.</summary>
    internal RegraDosNomes Nomes { get; }

    /// <summary>A conexão aberta.</summary>
    internal NpgsqlConnection Conexao => _conexao;

    /// <summary>Lê a string de conexão, impõe as opções do Incognito, conecta e confere a versão e a sessão.</summary>
    /// <param name="segredo">A string de conexão.</param>
    /// <param name="origem">Se é a origem (sessão só de leitura).</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>A conexão aberta, que o chamador descarta.</returns>
    /// <exception cref="DialetoRecusouException">String inválida ou recusada, versão não suportada.</exception>
    /// <exception cref="IncognitoException">Sessão divergente.</exception>
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

            var (versao, nomes) = await ConferirAsync(conexao, origem, cancelamento).ConfigureAwait(false);
            if (!origem)
            {
                await VerificarClienteAsync(conexao, cancelamento).ConfigureAwait(false);
            }

            return new ConexaoPostgreSql(fonte, conexao, origem, versao, nomes, construtor.SslMode, construtor.Host!);
        }
        catch
        {
            await Descarte.DepoisDaFalhaAsync(conexao, fonte).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Se o servidor está na própria máquina: endereço de loopback, <c>localhost</c> ou socket Unix (caminho de diretório),
    /// com ou sem a porta, que o Npgsql aceita no próprio servidor. Nunca por prefixo do nome: <c>127.exemplo.invalid</c> é
    /// um nome de DNS, e remoto.
    /// </summary>
    /// <param name="servidor">O servidor da string de conexão.</param>
    /// <returns>Se é local.</returns>
    internal static bool Local(string servidor)
    {
        if (servidor.StartsWith('/'))
        {
            return true;
        }

        var semPorta = SemPorta(servidor);
        return string.Equals(semPorta, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(semPorta, out var endereco) && IPAddress.IsLoopback(endereco));
    }

    /// <summary>
    /// O transporte pelo modo de TLS que a conexão exigiu: cifrado só quando o modo exige a cifra, em claro quando a recusa
    /// ou por socket (o servidor nunca cifra ali), e sem resposta quando só a prefere (o servidor diz que a sessão está
    /// cifrada, mas um intermediário pode cifrar só o trecho dele); o que o servidor diz só rebaixa. O servidor só é verificado
    /// com o nome conferido.
    /// </summary>
    /// <param name="modo">O modo de TLS depois do que o Incognito impõe.</param>
    /// <param name="servidor">O servidor da string de conexão.</param>
    /// <param name="cifradaNoServidor">Se o servidor diz que a sessão está cifrada; nulo se não diz.</param>
    /// <returns>O transporte.</returns>
    internal static TransporteConexao Transporte(SslMode modo, string servidor, bool? cifradaNoServidor)
    {
        bool? cifrado = cifradaNoServidor == false || servidor.StartsWith('/') ? false : modo switch
        {
            SslMode.Disable => false,
            SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull => true,
            _ => null,
        };
        return new TransporteConexao
        {
            Cifrado = cifrado,
            ServidorVerificado = modo == SslMode.VerifyFull && cifrado == true,
            Local = Local(servidor),
        };
    }

    // O servidor sem a porta ([::1]:5433, 127.0.0.1:5433, localhost:5433); um IPv6 sem colchetes fica como está.
    private static string SemPorta(string servidor)
    {
        if (servidor.StartsWith('[') && servidor.IndexOf(']', StringComparison.Ordinal) is var fim and > 0)
        {
            return servidor[1..fim];
        }

        var separador = servidor.IndexOf(':', StringComparison.Ordinal);
        return separador > 0 && separador == servidor.LastIndexOf(':')
            && int.TryParse(servidor.AsSpan(separador + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? servidor[..separador]
            : servidor;
    }

    /// <summary>O transporte desta conexão.</summary>
    /// <param name="cifradaNoServidor">Se o servidor diz que a sessão está cifrada; nulo se não diz.</param>
    /// <returns>O transporte.</returns>
    internal TransporteConexao Transporte(bool? cifradaNoServidor) => Transporte(_modo, _servidor, cifradaNoServidor);

    /// <summary>
    /// Abre outra conexão física pela mesma fonte (sem pool, com o mesmo registro e rastreamento desligados e as mesmas
    /// opções impostas), conferida como a de controle, e confere que ela chegou ao mesmo banco: o mesmo cluster, oid,
    /// endereço e porta, numa primária. O chamador a descarta.
    /// </summary>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A conexão aberta.</returns>
    /// <exception cref="IncognitoException">Sessão divergente, ou outro banco, servidor ou uma réplica.</exception>
    internal async Task<NpgsqlConnection> AbrirOutraAsync(CancellationToken cancelamento)
    {
        var outra = await _fonte.OpenConnectionAsync(cancelamento).ConfigureAwait(false);
        try
        {
            await ConferirAsync(outra, _origem, cancelamento).ConfigureAwait(false);
            if (!_origem)
            {
                await VerificarClienteAsync(outra, cancelamento).ConfigureAwait(false);
            }

            var (desta, primaria) = await ImpressaoAsync(_conexao, cancelamento).ConfigureAwait(false);
            var (daOutra, outraPrimaria) = await ImpressaoAsync(outra, cancelamento).ConfigureAwait(false);
            if (desta != daOutra || !primaria || !outraPrimaria)
            {
                throw new IncognitoException(
                    CodigosPostgreSql.BloqueioEmOutroBanco,
                    "A conexão do bloqueio não chegou ao mesmo banco da conexão de controle, ou o banco é uma réplica: o bloqueio não protegeria o destino.");
            }

            return outra;
        }
        catch
        {
            await outra.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Abre uma conexão da sessão de leitura pela mesma fonte (sem pool, com as mesmas opções impostas), conferida como a de
    /// controle, no mesmo banco dela (o mesmo cluster, oid, endereço e porta); a primária só é exigida no destino (a leitura
    /// da origem numa réplica é o caso recomendado). Ajusta a sessão para durar a cópia inteira: keepalives TCP em minutos e,
    /// do 17 em diante, sem <c>transaction_timeout</c> (que não existe no 15 e no 16, e no pacote de início derrubaria a
    /// conexão); e a nomeia (<see cref="NomeDaLeitura"/>). Conferências e ajustes antes de qualquer transação. O chamador a
    /// descarta.
    /// </summary>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A conexão aberta.</returns>
    /// <exception cref="IncognitoException">Sessão divergente, outro banco ou servidor, ou réplica no destino.</exception>
    internal async Task<NpgsqlConnection> AbrirParaLeituraAsync(CancellationToken cancelamento)
    {
        var outra = await _fonte.OpenConnectionAsync(cancelamento).ConfigureAwait(false);
        try
        {
            await ConferirAsync(outra, _origem, cancelamento).ConfigureAwait(false);
            if (!_origem)
            {
                await VerificarClienteAsync(outra, cancelamento).ConfigureAwait(false);
            }

            var (desta, primaria) = await ImpressaoAsync(_conexao, cancelamento).ConfigureAwait(false);
            var (daOutra, outraPrimaria) = await ImpressaoAsync(outra, cancelamento).ConfigureAwait(false);
            if (desta != daOutra || (!_origem && (!primaria || !outraPrimaria)))
            {
                throw new IncognitoException(
                    CodigosPostgreSql.LeituraEmOutroBanco,
                    "Uma conexão da sessão de leitura não chegou ao mesmo banco da conexão de controle (ou, no destino, o banco é uma réplica).");
            }

            await using (var ajustar = new NpgsqlCommand(
                $"SET application_name = '{NomeDaLeitura}'; SET tcp_keepalives_idle = 60; SET tcp_keepalives_interval = 10; SET tcp_keepalives_count = 6"
                    + (Versao >= 170000 ? "; SET transaction_timeout = 0" : ""),
                outra))
            {
                await ajustar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
            }

            if (Versao >= 170000)
            {
                await using var conferir = new NpgsqlCommand("SELECT pg_catalog.current_setting('transaction_timeout') = '0'", outra);
                if (await conferir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
                {
                    throw new IncognitoException(
                        CodigosPostgreSql.SessaoDivergente,
                        "A sessão de leitura do PostgreSQL não ficou sem o transaction_timeout, que a derrubaria no meio da cópia.");
                }
            }

            return outra;
        }
        catch
        {
            await outra.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // No destino, o servidor confere de segundo em segundo, durante um comando longo, se o cliente ainda está lá: a
    // validação de uma chave ou o lock de um processo que morreu não seguram as tabelas por minutos. Onde o servidor não
    // oferece a conferência (fora de Linux, macOS e BSD), recusa o valor e fica como estava.
    private static async Task VerificarClienteAsync(NpgsqlConnection conexao, CancellationToken cancelamento)
    {
        try
        {
            await using var comando = new NpgsqlCommand("SET client_connection_check_interval = '1s'", conexao);
            await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
        }
        catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
        }
    }

    // O banco de uma conexão: o oid, o endereço e a porta do servidor e, quando o login lê, o cluster; e se é primária.
    private static async Task<(string Impressao, bool Primaria)> ImpressaoAsync(NpgsqlConnection conexao, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand(
            """
            SELECT d.oid, pg_catalog.host(pg_catalog.inet_server_addr()), pg_catalog.inet_server_port(),
                   pg_catalog.has_function_privilege('pg_catalog.pg_control_system()', 'EXECUTE'),
                   NOT pg_catalog.pg_is_in_recovery()
            FROM pg_catalog.pg_database d
            WHERE d.datname = pg_catalog.current_database()
            """,
            conexao);
        string impressao;
        bool primaria;
        bool podeLerSistema;
        await using (var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false))
        {
            await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
            impressao = string.Join(
                '|',
                leitor.GetFieldValue<uint>(0).ToString(CultureInfo.InvariantCulture),
                leitor.IsDBNull(1) ? "" : leitor.GetString(1),
                leitor.IsDBNull(2) ? "" : leitor.GetInt32(2).ToString(CultureInfo.InvariantCulture));
            (podeLerSistema, primaria) = (leitor.GetBoolean(3), leitor.GetBoolean(4));
        }

        if (podeLerSistema)
        {
            await using var sistema = new NpgsqlCommand("SELECT system_identifier FROM pg_catalog.pg_control_system()", conexao);
            if (await sistema.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is long identificador)
            {
                impressao += "|" + identificador.ToString(CultureInfo.InvariantCulture);
            }
        }

        return (impressao, primaria);
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
            throw new DialetoRecusouException(CodigosPostgreSql.StringInvalida, "A string de conexão do PostgreSQL não pôde ser lida.");
        }
    }

    private static DialetoRecusouException OpcaoInutilizavel() => new(
        CodigosPostgreSql.StringInvalida,
        "A string de conexão do PostgreSQL tem uma opção que não pôde ser usada (por exemplo, arquivo de senha, certificado, método de autenticação ou negociação de TLS).");

    /// <summary>Impõe por cima da string de conexão o que o Incognito exige.</summary>
    /// <param name="construtor">A string de conexão lida.</param>
    /// <param name="origem">Se é a origem (sessão só de leitura).</param>
    /// <exception cref="DialetoRecusouException">Opção recusada.</exception>
    internal static void Impor(NpgsqlConnectionStringBuilder construtor, bool origem)
    {
        if (string.IsNullOrWhiteSpace(construtor.Host) || string.IsNullOrWhiteSpace(construtor.Database))
        {
            // Sem servidor ou banco, o Npgsql cairia nas variáveis PG* do ambiente ou no nome do usuário.
            throw new DialetoRecusouException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL precisa dizer o servidor (Host) e o banco (Database).");
        }

        if (construtor.Host.Contains(',', StringComparison.Ordinal) || construtor.LoadBalanceHosts)
        {
            // O bloqueio, o marcador, a identidade e o snapshot precisam do mesmo servidor em todas as conexões.
            throw new DialetoRecusouException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL tem mais de um servidor ou balanceamento entre servidores (Load Balance Hosts), que o Incognito não aceita.");
        }

        // Opções obsoletas, lidas pelo nome para não usar as propriedades marcadas como obsoletas.
        if (Preenchida(construtor, "Server Compatibility Mode", "None") || Preenchida(construtor, "Load Table Composites", "False"))
        {
            throw new DialetoRecusouException(CodigosPostgreSql.OpcaoRecusada, "A string de conexão do PostgreSQL usa Server Compatibility Mode ou Load Table Composites, que o Incognito não aceita.");
        }

        var comSenha = !string.IsNullOrEmpty(construtor.Password) || !string.IsNullOrEmpty(construtor.Passfile);
        if (!comSenha && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGPASSWORD")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGPASSFILE"))))
        {
            // Sem senha na string, o Npgsql usaria a do ambiente, que pode ser de outro banco (a da produção, numa string
            // de destino descuidada).
            throw new DialetoRecusouException(
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
        // controle fica parada entre os passos), o formato monetário C, que sempre existe (o money sai e entra igual nos dois
        // lados, qualquer que seja o locale do login; conferido na abertura, e código do usuário pode trocá-lo depois), e, na
        // origem, transações só de leitura por padrão.
        construtor.SearchPath = CaminhoDeBusca;
        construtor.Options = $"-c search_path={CaminhoDeBusca} -c standard_conforming_strings=on"
            + " -c statement_timeout=0 -c idle_session_timeout=0 -c idle_in_transaction_session_timeout=0 -c lc_monetary=C"
            + (origem ? " -c default_transaction_read_only=on" : "");
    }

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
            throw new DialetoRecusouException(
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

    private static async Task<(int Versao, RegraDosNomes Nomes)> ConferirAsync(NpgsqlConnection conexao, bool origem, CancellationToken cancelamento)
    {
        // O papel também: um gatilho de login pode trocar de papel (SET ROLE) e ler com outros privilégios. A codificação
        // do banco pelo número (6 é UTF8) e o limite do tipo name decidem a igualdade dos nomes.
        await using var comando = new NpgsqlCommand(
            """
            SELECT pg_catalog.current_setting('server_version_num'), pg_catalog.current_setting('search_path'),
                   pg_catalog.current_setting('default_transaction_read_only'), pg_catalog.current_setting('client_encoding'),
                   pg_catalog.current_setting('standard_conforming_strings'), current_user = session_user,
                   pg_catalog.current_setting('lc_monetary') = 'C', d.encoding = 6, pg_catalog.pg_encoding_max_length(d.encoding),
                   pg_catalog.current_setting('max_identifier_length')::integer
            FROM pg_catalog.pg_database d
            WHERE d.datname = pg_catalog.current_database()
            """,
            conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        var versaoLida = leitor.GetString(0);

        // O número, nunca o texto da versão (o servidor controla o texto).
        if (!int.TryParse(versaoLida, NumberStyles.None, CultureInfo.InvariantCulture, out var versao))
        {
            throw new DialetoRecusouException(CodigosPostgreSql.VersaoNaoSuportada, "O servidor PostgreSQL informou uma versão que não é um número.");
        }

        if (versao < VersaoMinima)
        {
            throw new DialetoRecusouException(
                CodigosPostgreSql.VersaoNaoSuportada,
                $"O servidor PostgreSQL está na versão {versao / 10000}; o Incognito exige a {VersaoMinima / 10000} ou mais nova.");
        }

        if (leitor.GetString(1) != CaminhoDeBusca || (origem && leitor.GetString(2) != "on") || leitor.GetString(3) != Utf8 || leitor.GetString(4) != "on"
            || !leitor.GetBoolean(5) || !leitor.GetBoolean(6))
        {
            throw new IncognitoException(
                CodigosPostgreSql.SessaoDivergente,
                "A sessão do PostgreSQL não ficou com os parâmetros que o Incognito impõe (search_path, client_encoding, standard_conforming_strings, lc_monetary, o papel do login e, na origem, default_transaction_read_only).");
        }

        return (versao, new RegraDosNomes(leitor.GetBoolean(7), leitor.GetInt32(8), leitor.GetInt32(9)));
    }
}
