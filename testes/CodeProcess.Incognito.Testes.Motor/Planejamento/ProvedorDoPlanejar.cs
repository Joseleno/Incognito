using System.Collections;
using System.Data;
using System.Data.Common;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Motor.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>Uma chamada que o motor faz ao dialeto.</summary>
public enum Operacao
{
    Nome,
    Conectar,
    LerEsquema,
    LerIdentidade,
    Citar,
    CompararNome,
    Descartar,

    // Os getters das partes do banco conectado.
    ObterEsquema,
    ObterIdentidade,
    ObterSql,
}

/// <summary>
/// O dialeto do planejar nos testes: devolve os esquemas dados, registra cada chamada e o token que recebeu, e lança sob
/// comando, em qualquer operação e lado, de forma síncrona (antes da tarefa) ou na tarefa. Separado do dialeto falso da
/// suíte, que prova a suíte; este prova o motor.
/// </summary>
internal sealed class ProvedorDoPlanejar(EsquemaBanco? origem = null, EsquemaBanco? destino = null, string nome = "postgresql") : ProvedorDialeto
{
    /// <summary>O texto que nenhuma saída do motor pode trazer, sem forma de CPF.</summary>
    internal const string Sentinela = "Sentinela7f3a9cQ";

    /// <summary>As chamadas, na ordem, como <c>operação:lado</c> (os getters não entram).</summary>
    internal List<string> Chamadas { get; } = [];

    /// <summary>Os tokens que o dialeto recebeu, na ordem das chamadas que recebem token.</summary>
    internal List<CancellationToken> Tokens { get; } = [];

    /// <summary>A exceção a lançar numa operação e lado (lado nulo no nome); nula segue normal.</summary>
    internal Func<Operacao, bool, Exception?> Lancar { get; init; } = (_, _) => null;

    /// <summary>Se lança antes de devolver a tarefa, e não dentro dela.</summary>
    internal bool Sincrono { get; init; }

    /// <summary>A operação que devolve nulo no lugar do resultado.</summary>
    internal Operacao? Nulo { get; init; }

    /// <summary>O resultado forçado da comparação de nomes; nulo compara como o PostgreSQL num banco UTF-8.</summary>
    internal CorrespondenciaNome? Correspondencia { get; init; }

    /// <summary>A comparação de nomes por lado (escrito, entre aspas, do catálogo, destino); nulo segue as outras regras.</summary>
    internal Func<string, bool, string, bool, CorrespondenciaNome?>? Comparar { get; init; }

    /// <summary>Compara como o PostgreSQL num banco de um byte por caractere: sem aspas, letra fora do ASCII é indeterminada.</summary>
    internal bool UmBytePorCaractere { get; init; }

    public override string Nome
    {
        get
        {
            Registrar(Operacao.Nome, destino: false);
            return Lancar(Operacao.Nome, false) is { } excecao ? throw excecao : nome;
        }
    }

    internal EsquemaBanco Origem { get; } = origem ?? EsquemaSintetico.Coerente();

    internal EsquemaBanco Destino { get; } = destino ?? EsquemaSintetico.Coerente();

    public override Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Executar(Operacao.Conectar, destino: false, () => (DialetoOrigem)new OrigemDoPlanejar(this), cancelamento);

    public override Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Executar(Operacao.Conectar, destino: true, () => (DialetoDestino)new DestinoDoPlanejar(this), cancelamento);

    // O texto, a igualdade e o hash do provedor são selados no contrato; os das partes lançam abaixo.

    internal Task<T> Executar<T>(Operacao operacao, bool destino, Func<T> resultado, CancellationToken? cancelamento = null)
    {
        Registrar(operacao, destino);
        if (cancelamento is { } token)
        {
            lock (Tokens)
            {
                Tokens.Add(token);
            }
        }

        if (Lancar(operacao, destino) is { } excecao)
        {
            return Sincrono ? throw excecao : Task.FromException<T>(excecao);
        }

        return Task.FromResult(Nulo == operacao ? default! : resultado());
    }

    internal T Chamar<T>(Operacao operacao, bool destino, Func<T> resultado)
    {
        Registrar(operacao, destino);
        return Lancar(operacao, destino) is { } excecao ? throw excecao : Nulo == operacao ? default! : resultado();
    }

    // Um getter de parte: lança sob comando, sem entrar nas chamadas.
    internal T Obter<T>(Operacao operacao, bool destino, T parte) => Lancar(operacao, destino) is { } excecao ? throw excecao : parte;

    private void Registrar(Operacao operacao, bool destino)
    {
        lock (Chamadas)
        {
            Chamadas.Add($"{operacao}:{(destino ? "destino" : "origem")}");
        }
    }
}

internal sealed class OrigemDoPlanejar(ProvedorDoPlanejar provedor) : DialetoOrigem
{
    private readonly LeitorEsquema _esquema = new LeitorDoPlanejar(provedor, destino: false);
    private readonly IdentidadeBanco _identidade = new IdentidadeDoPlanejar(provedor, destino: false);
    private readonly DialetoSql _sql = new SqlDoPlanejar(provedor, destino: false);

    public override LeitorEsquema Esquema => provedor.Obter(Operacao.ObterEsquema, false, _esquema);

    public override IdentidadeBanco Identidade => provedor.Obter(Operacao.ObterIdentidade, false, _identidade);

    public override DialetoSql Sql => provedor.Obter(Operacao.ObterSql, false, _sql);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await provedor.Executar(Operacao.Descartar, destino: false, () => true);
        }
        finally
        {
            await base.DisposeAsyncCore();
        }
    }
}

internal sealed class DestinoDoPlanejar(ProvedorDoPlanejar provedor) : DialetoDestino
{
    private readonly LeitorEsquema _esquema = new LeitorDoPlanejar(provedor, destino: true);
    private readonly IdentidadeBanco _identidade = new IdentidadeDoPlanejar(provedor, destino: true);
    private readonly DialetoSql _sql = new SqlDoPlanejar(provedor, destino: true);

    public override LeitorEsquema Esquema => provedor.Obter(Operacao.ObterEsquema, true, _esquema);

    public override IdentidadeBanco Identidade => provedor.Obter(Operacao.ObterIdentidade, true, _identidade);

    public override DialetoSql Sql => provedor.Obter(Operacao.ObterSql, true, _sql);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await provedor.Executar(Operacao.Descartar, destino: true, () => true);
        }
        finally
        {
            await base.DisposeAsyncCore();
        }
    }
}

internal sealed class LeitorDoPlanejar(ProvedorDoPlanejar provedor, bool destino) : LeitorEsquema
{
    public override Task<EsquemaBanco> LerAsync(CancellationToken cancelamento) =>
        provedor.Executar(Operacao.LerEsquema, destino, () => destino ? provedor.Destino : provedor.Origem, cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

internal sealed class IdentidadeDoPlanejar(ProvedorDoPlanejar provedor, bool destino) : IdentidadeBanco
{
    public override Task<IdentidadeLida> LerAsync(CancellationToken cancelamento) =>
        provedor.Executar(Operacao.LerIdentidade, destino, () => EsquemaSintetico.Identidade(destino ? "loja_homolog" : "loja"), cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

internal sealed class SqlDoPlanejar(ProvedorDoPlanejar provedor, bool destino) : DialetoSql
{
    public override string Citar(string identificador) =>
        provedor.Chamar(Operacao.Citar, destino, () => "\"" + identificador.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");

    public override CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo) =>
        provedor.Chamar(Operacao.CompararNome, destino, () => provedor.Comparar?.Invoke(escrito, entreAspas, doCatalogo, destino)
            ?? provedor.Correspondencia
            ?? (provedor.UmBytePorCaractere && !entreAspas && !System.Text.Ascii.IsValid(escrito) ? (CorrespondenciaNome?)CorrespondenciaNome.Indeterminada : null)
            ?? (string.Equals(entreAspas ? escrito : string.Concat(escrito.Select(caractere => char.IsAsciiLetterUpper(caractere) ? char.ToLowerInvariant(caractere) : caractere)), doCatalogo, StringComparison.Ordinal)
                ? CorrespondenciaNome.Corresponde
                : CorrespondenciaNome.NaoCorresponde));

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

/// <summary>
/// Uma exceção que imita a do driver: o sentinela na mensagem, nos dados (inclusive em minúsculas, como o parser do
/// Npgsql ecoa a palavra-chave desconhecida), na interna, no texto e no comando que falhou.
/// </summary>
internal sealed class ExcecaoDoDriver() : DbException(ProvedorDoPlanejar.Sentinela, new InvalidOperationException("Password=" + ProvedorDoPlanejar.Sentinela))
{
    public override IDictionary Data { get; } = new Dictionary<string, object>
    {
        ["Hint"] = ProvedorDoPlanejar.Sentinela,
        ["Where"] = "Couldn't set " + ProvedorDoPlanejar.Sentinela.ToLowerInvariant(),
    };

    protected override DbBatchCommand? DbBatchCommand { get; } = new ComandoDoDriver();

    public override string ToString() => "Password=abc;" + ProvedorDoPlanejar.Sentinela + "=1";

    // O comando que falhou, com o valor no texto, como os do INC-038 terão.
    private sealed class ComandoDoDriver : DbBatchCommand
    {
        public override string CommandText { get; set; } = $"INSERT INTO loja.clientes VALUES ('{ProvedorDoPlanejar.Sentinela}')";

        public override CommandType CommandType { get; set; }

        public override int RecordsAffected => 0;

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
    }
}

/// <summary>Uma <see cref="IncognitoException"/> de fora, com a mensagem virtual: devolve o sentinela, ou lança ao ser lida.</summary>
internal sealed class IncognitoHostil(string codigo, bool lancaAoLer = false) : IncognitoException(codigo, "mensagem do construtor")
{
    public override string Message => lancaAoLer ? throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela) : ProvedorDoPlanejar.Sentinela;
}

/// <summary>Uma agregada de fora, com a mensagem virtual que lança: o motor nunca pode lê-la.</summary>
internal sealed class AgregadaHostil(params Exception[] internas) : AggregateException(internas)
{
    public override string Message => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

/// <summary>Atalhos de leitura dos esquemas do falso da suíte para o planejar.</summary>
internal static class EsquemaDoFalso
{
    internal static async Task<EsquemaBanco> LerAsync(bool completo, Defeito defeito)
    {
        var provedor = new ProvedorFalso(completo, defeito, perturbar: null);
        await using var origem = await provedor.ConectarOrigemAsync(new SegredoConexao("origem"), CancellationToken.None);
        return await origem.Esquema.LerAsync(CancellationToken.None);
    }
}
