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
    LerMaiorValor,
    GravarMarcador,
    TemLinhas,
    AdquirirBloqueio,
    ConferirBloqueio,
    LiberarBloqueio,

    // Os getters das partes do banco conectado.
    ObterEsquema,
    ObterIdentidade,
    ObterSql,
    ObterDados,
    ObterMarcador,
    ObterBloqueio,
    ObterDiario,
    ObjetosDoDiario,

    // O diário.
    CriarDiario,
    LerDiario,
    IniciarExecucao,
    RemoverChave,
    DesligarTriggers,
    ReligarTriggers,
    RecriarChave,
    ValidarChave,
    DescartarEntrada,
    EncerrarExecucao,
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

    /// <summary>A identidade de cada lado (pelo destino); nulo devolve a sintética, cifrada e com o servidor verificado.</summary>
    internal Func<bool, IdentidadeLida>? Identidades { get; init; }

    /// <summary>O maior valor de cada coluna da origem; nulo devolve nulo (nenhuma linha).</summary>
    internal Func<ReferenciaColuna, long?>? Maiores { get; init; }

    /// <summary>As colunas cujo maior valor foi pedido, na ordem.</summary>
    internal List<ReferenciaColuna> MaioresPedidos { get; } = [];

    /// <summary>Se o bloqueio do destino está com outra sessão (a tentativa devolve nulo).</summary>
    internal bool Ocupado { get; init; }

    /// <summary>Se cada tabela do destino tem linhas; nulo responde que nenhuma tem.</summary>
    internal Func<NomeObjeto, bool>? Linhas { get; init; }

    /// <summary>As concessões que o bloqueio do destino deu, na ordem.</summary>
    internal List<ConcessaoDoPlanejar> Concessoes { get; } = [];

    /// <summary>Os marcadores que o destino gravou, na ordem.</summary>
    internal List<(Guid Identificador, DateTimeOffset GravadoEm)> Gravados { get; } = [];

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

    /// <summary>O que o diário do destino declara deixar no esquema reservado; vazio por padrão (nenhum objeto aceito).</summary>
    internal IReadOnlyList<ObjetoReservado> ObjetosDoDiario { get; init; } = [];

    /// <summary>O diário lido; um diário vazio por padrão (o destino preparado).</summary>
    internal EstadoDiario EstadoDoDiario { get; init; } = new() { Existe = true };

    /// <summary>Se a criação do diário passou: a leitura seguinte o vê, mesmo com <see cref="EstadoDoDiario"/> sem diário.</summary>
    internal bool DiarioCriado { get; set; }

    /// <summary>A situação que a recriação e a validação devolvem.</summary>
    internal SituacaoEntrada? NaRestauracao { get; init; }

    /// <summary>As triggers que a religação pula.</summary>
    internal IReadOnlyList<TriggerDoDiario> Puladas { get; init; } = [];

    /// <summary>O que o diário faz antes de cada operação, com o token que recebeu (esperar, perder o bloqueio).</summary>
    internal Func<Operacao, CancellationToken, Task>? NoDiario { get; init; }

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
    private readonly LeitorDadosOrigem _dados = new DadosDoPlanejar(provedor);

    public override LeitorEsquema Esquema => provedor.Obter(Operacao.ObterEsquema, false, _esquema);

    public override IdentidadeBanco Identidade => provedor.Obter(Operacao.ObterIdentidade, false, _identidade);

    public override DialetoSql Sql => provedor.Obter(Operacao.ObterSql, false, _sql);

    public override LeitorDadosOrigem Dados => provedor.Obter(Operacao.ObterDados, false, _dados);

    // O planejar não lê em sessão.
    protected override Task<SessaoLeitura> AbrirLeituraAsyncCore(PedidoLeitura pedido, CancellationToken cancelamento) => throw new NotSupportedException();

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
    private readonly BloqueioExecucao _bloqueio = new BloqueioDoPlanejar(provedor);
    private readonly MarcadorDestino _marcador = new MarcadorDoPlanejar(provedor);
    private readonly LeitorDadosDestino _dados = new DadosDestinoDoPlanejar(provedor);
    private readonly DiarioExecucao _diario = new DiarioDoPlanejar(provedor);

    public override LeitorEsquema Esquema => provedor.Obter(Operacao.ObterEsquema, true, _esquema);

    public override IdentidadeBanco Identidade => provedor.Obter(Operacao.ObterIdentidade, true, _identidade);

    public override DialetoSql Sql => provedor.Obter(Operacao.ObterSql, true, _sql);

    public override BloqueioExecucao Bloqueio => provedor.Obter(Operacao.ObterBloqueio, true, _bloqueio);

    public override MarcadorDestino Marcador => provedor.Obter(Operacao.ObterMarcador, true, _marcador);

    public override LeitorDadosDestino Dados => provedor.Obter(Operacao.ObterDados, true, _dados);

    public override DiarioExecucao Diario => provedor.Obter(Operacao.ObterDiario, true, _diario);

    // O planejar não lê em sessão.
    protected override Task<SessaoLeitura> AbrirLeituraAsyncCore(PedidoLeitura pedido, CancellationToken cancelamento) => throw new NotSupportedException();

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

internal sealed class DadosDoPlanejar(ProvedorDoPlanejar provedor) : LeitorDadosOrigem
{
    public override Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        lock (provedor.MaioresPedidos)
        {
            provedor.MaioresPedidos.Add(coluna);
        }

        return provedor.Executar(Operacao.LerMaiorValor, destino: false, () => provedor.Maiores?.Invoke(coluna), cancelamento);
    }

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

internal sealed class BloqueioDoPlanejar(ProvedorDoPlanejar provedor) : BloqueioExecucao
{
    public override Task<ConcessaoBloqueio?> TentarAdquirirAsync(CancellationToken cancelamento) =>
        provedor.Executar<ConcessaoBloqueio?>(
            Operacao.AdquirirBloqueio,
            destino: true,
            () =>
            {
                if (provedor.Ocupado)
                {
                    return null;
                }

                var concessao = new ConcessaoDoPlanejar(provedor);
                lock (provedor.Concessoes)
                {
                    provedor.Concessoes.Add(concessao);
                }

                return concessao;
            },
            cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

/// <summary>Uma concessão do provedor de teste: conta as chamadas, lança sob comando e marca a perda quando pedido.</summary>
internal sealed class ConcessaoDoPlanejar(ProvedorDoPlanejar provedor) : ConcessaoBloqueio
{
    /// <summary>Se a concessão foi liberada.</summary>
    internal bool Liberada { get; private set; }

    /// <summary>Marca a perda, como o dialeto faria ao perceber a queda da conexão.</summary>
    internal void Perder() => SinalizarPerda();

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    protected override Task ConferirAsyncCore(CancellationToken cancelamento) => provedor.Executar(Operacao.ConferirBloqueio, destino: true, () => true, cancelamento);

    protected override async ValueTask LiberarAsyncCore()
    {
        Liberada = true;
        await provedor.Executar(Operacao.LiberarBloqueio, destino: true, () => true);
    }
}

internal sealed class MarcadorDoPlanejar(ProvedorDoPlanejar provedor) : MarcadorDestino
{
    public override Task GravarAsync(Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento) =>
        provedor.Executar(
            Operacao.GravarMarcador,
            destino: true,
            () =>
            {
                lock (provedor.Gravados)
                {
                    provedor.Gravados.Add((identificador, gravadoEm));
                }

                return true;
            },
            cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

internal sealed class DadosDestinoDoPlanejar(ProvedorDoPlanejar provedor) : LeitorDadosDestino
{
    public override Task<bool> TemLinhasAsync(NomeObjeto tabela, CancellationToken cancelamento) =>
        provedor.Executar(Operacao.TemLinhas, destino: true, () => provedor.Linhas?.Invoke(tabela) == true, cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);
}

internal sealed class IdentidadeDoPlanejar(ProvedorDoPlanejar provedor, bool destino) : IdentidadeBanco
{
    public override Task<IdentidadeLida> LerAsync(CancellationToken cancelamento) =>
        provedor.Executar(Operacao.LerIdentidade, destino, () => provedor.Identidades?.Invoke(destino) ?? EsquemaSintetico.Identidade(destino ? "loja_homolog" : "loja"), cancelamento);

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

// O diário do planejar e do envoltório: cada operação entra nas chamadas, lança ou espera sob comando e devolve o que o
// provedor manda.
internal sealed class DiarioDoPlanejar(ProvedorDoPlanejar provedor) : DiarioExecucao
{
    public override IReadOnlyList<ObjetoReservado> ObjetosDoDiario => provedor.Obter(Operacao.ObjetosDoDiario, true, provedor.ObjetosDoDiario);

    public override Task CriarAsync(CancellationToken cancelamento) => Fazer(Operacao.CriarDiario, () => provedor.DiarioCriado = true, cancelamento);

    public override Task<EstadoDiario> LerAsync(CancellationToken cancelamento) =>
        Fazer(Operacao.LerDiario, () => provedor.DiarioCriado && !provedor.EstadoDoDiario.Existe ? new EstadoDiario { Existe = true } : provedor.EstadoDoDiario, cancelamento);

    public override Task IniciarAsync(Guid execucao, Guid marcador, DateTimeOffset iniciadaEm, CancellationToken cancelamento) => Fazer(Operacao.IniciarExecucao, () => true, cancelamento);

    public override Task RemoverChaveAsync(Guid execucao, NomeObjeto tabela, string chave, CancellationToken cancelamento) => Fazer(Operacao.RemoverChave, () => true, cancelamento);

    public override Task DesligarTriggersAsync(Guid execucao, NomeObjeto tabela, CancellationToken cancelamento) => Fazer(Operacao.DesligarTriggers, () => true, cancelamento);

    public override Task<IReadOnlyList<TriggerDoDiario>> ReligarTriggersAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(Operacao.ReligarTriggers, () => provedor.Puladas, cancelamento);

    public override Task<SituacaoEntrada?> RecriarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(Operacao.RecriarChave, () => provedor.NaRestauracao, cancelamento);

    public override Task<SituacaoEntrada?> ValidarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(Operacao.ValidarChave, () => provedor.NaRestauracao, cancelamento);

    public override Task DescartarAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(Operacao.DescartarEntrada, () => true, cancelamento);

    public override Task EncerrarAsync(Guid execucao, SituacaoExecucao situacao, DateTimeOffset encerradaEm, CancellationToken cancelamento) => Fazer(Operacao.EncerrarExecucao, () => true, cancelamento);

    public override string ToString() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override bool Equals(object? obj) => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    public override int GetHashCode() => throw new InvalidOperationException(ProvedorDoPlanejar.Sentinela);

    private async Task<T> Fazer<T>(Operacao operacao, Func<T> resultado, CancellationToken cancelamento)
    {
        if (provedor.NoDiario is { } antes)
        {
            await antes(operacao, cancelamento);
        }

        return await provedor.Executar(operacao, destino: true, resultado, cancelamento);
    }
}
