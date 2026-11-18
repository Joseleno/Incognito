using System.Collections.Concurrent;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>Como montar o cenário do executor; cada campo nasce no caminho feliz.</summary>
internal sealed record OpcoesDoCenario
{
    /// <summary>O CPF de <c>loja.clientes</c> pelo transformador de fora <c>exemplo.funcao@1</c>.</summary>
    internal const string ComFuncao = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: exemplo.funcao@1 }\n";

    /// <summary>O que o transformador de fora faz com cada valor; sem ela, <c>TRATADO</c>, substituído.</summary>
    internal Func<object?, ResultadoTransformacao>? Funcao { get; init; }

    /// <summary>O começo do YAML; sem ele, o mínimo (reproduzível).</summary>
    internal string Base { get; init; } = Yaml.Minimo;

    /// <summary>As tabelas do YAML, depois do começo.</summary>
    internal string Tabelas { get; init; } = ComFuncao;

    /// <summary>As tabelas do destino que têm linhas; sem ela, nenhuma.</summary>
    internal Func<NomeObjeto, bool>? Linhas { get; init; }

    /// <summary>O destino que o <c>planejar</c> lê; sem ele, o coerente.</summary>
    internal EsquemaBanco? DestinoNoPlanejar { get; init; }

    /// <summary>O destino que a execução relê; sem ele, o do <c>planejar</c>.</summary>
    internal EsquemaBanco? DestinoNaExecucao { get; init; }

    /// <summary>O CPF de cada linha da origem (ids 1 a 3); sem ela, o sentinela com o id, que nenhuma saída pode trazer.</summary>
    internal Func<int, object?> Cpf { get; init; } = id => $"{ProvedorDoPlanejar.Sentinela}-{id}";

    /// <summary>O diário que o reparo lê, antes de iniciada a execução; vazio por padrão.</summary>
    internal EstadoDiario DiarioAntes { get; init; } = new() { Existe = true };

    /// <summary>As entradas de uma execução anterior que o diário ainda guarda depois de iniciada a nova (as adotadas).</summary>
    internal IReadOnlyList<EntradaDiario> Antigas { get; init; } = [];

    /// <summary>A exceção que o dialeto lança numa operação e lado.</summary>
    internal Func<Operacao, bool, Exception?> Lancar { get; init; } = (_, _) => null;

    /// <summary>Chamado em cada ponto da cópia (<c>leitura</c>, <c>transformacao</c>, <c>gravacao</c>), antes de fazê-lo.</summary>
    internal Action<string>? Ponto { get; init; }

    /// <summary>O que o diário faz antes de cada operação (cancelar, perder o bloqueio), depois de registrá-la na linha.</summary>
    internal Func<Operacao, Task>? NoDiario { get; init; }

    /// <summary>Lançada na leitura da idade do instante.</summary>
    internal Exception? NaIdade { get; init; }

    /// <summary>Chamado no descarte da sessão de leitura (depois da cópia e antes da restauração).</summary>
    internal Action? AoSoltarSessao { get; init; }

    /// <summary>O esquema da origem que a sessão de leitura relê no instante; sem ele, o coerente.</summary>
    internal EsquemaBanco? EsquemaDaSessao { get; init; }

    /// <summary>Lança na gravação do lote de número dado (a partir de 1), em cada tabela.</summary>
    internal Func<int, Exception?> NoLoteGravado { get; init; } = _ => null;

    /// <summary>Lança no <c>ConcluirAsync</c> de toda tabela.</summary>
    internal Exception? NoConcluir { get; init; }
}

/// <summary>
/// O executor sobre o falso do <c>planejar</c>, com a cópia em memória: <c>loja.clientes</c> (3 linhas, o CPF tratado pelo
/// transformador de fora) e <c>loja.pedidos</c> (3 linhas, copiada), um lote por linha, P = 2 e K = 2. O diário, depois de
/// iniciada a execução, tem a chave do pedido recriada sem validação, pendente: a restauração do caminho feliz a valida, e a
/// da falha não (D9).
/// </summary>
internal sealed class CenarioDaExecucao
{
    private readonly Plano _plano;
    private readonly RegistroTransformadores _registro;

    private CenarioDaExecucao(Plano plano, RegistroTransformadores registro, FabricaContadora fabrica, OpcoesDoCenario opcoes)
    {
        _plano = plano;
        _registro = registro;
        Fabrica = fabrica;
        var tabelas = new Dictionary<string, object?[][]>
        {
            ["clientes"] = [.. Enumerable.Range(1, 3).Select(id => (object?[])[(long)id, opcoes.Cpf(id), $"Pessoa {id}"])],
            ["pedidos"] = [.. Enumerable.Range(1, 3).Select(id => (object?[])[(long)id, (long)id])],
        };
        Gravador = new GravadorEmMemoria
        {
            NoLote = numero =>
            {
                opcoes.Ponto?.Invoke("gravacao");
                return opcoes.NoLoteGravado(numero);
            },
            NoConcluir = opcoes.NoConcluir,
            Registrar = Linha.Enqueue,
        };
        var destino = opcoes.DestinoNoPlanejar ?? Coerente();
        Provedor = new ProvedorDoPlanejar(Coerente(), opcoes.DestinoNaExecucao ?? destino)
        {
            Gravador = Gravador,
            Sessao = pedido => CopiaEmMemoria.Sessao(
                [
                    .. Enumerable.Range(0, pedido.Leitores).Select(_ => (LeitorDaSessao)new LeitorEmMemoria(tabelas)
                    {
                        LinhasPorLote = 1,
                        NoLote = _ =>
                        {
                            opcoes.Ponto?.Invoke("leitura");
                            return null;
                        },
                    }),
                ],
                opcoes.EsquemaDaSessao ?? Coerente(),
                opcoes.AoSoltarSessao,
                opcoes.NaIdade),
            EstadoDoDiario = opcoes.DiarioAntes,
            DiarioDaExecucao = execucao => Diario(execucao, opcoes),
            Lancar = opcoes.Lancar,
            Linhas = opcoes.Linhas,
            NoDiario = (operacao, _) =>
            {
                Linha.Enqueue(operacao.ToString());
                return opcoes.NoDiario?.Invoke(operacao) ?? Task.CompletedTask;
            },
        };
    }

    /// <summary>O falso do dialeto.</summary>
    internal ProvedorDoPlanejar Provedor { get; }

    /// <summary>O gravador do destino.</summary>
    internal GravadorEmMemoria Gravador { get; }

    /// <summary>A fábrica do transformador de fora.</summary>
    internal FabricaContadora Fabrica { get; }

    /// <summary>O plano, como o <c>planejar</c> o montou.</summary>
    internal Plano Plano => _plano;

    /// <summary>As operações do diário e as aberturas e esvaziamentos do gravador, na ordem em que aconteceram.</summary>
    internal ConcurrentQueue<string> Linha { get; } = new();

    /// <summary>A situação com que a execução foi encerrada no diário.</summary>
    internal SituacaoExecucao? Encerrada => Provedor.EncerradaComo;

    /// <summary>O id da linha de <c>loja.clientes</c> a que o valor do CPF pertence (o padrão termina no id).</summary>
    /// <param name="valor">O valor que o transformador recebeu.</param>
    /// <returns>O id.</returns>
    internal static int LinhaDe(object? valor) => ((string)valor!)[^1] - '0';

    /// <summary>Planeja e monta o cenário.</summary>
    /// <param name="opcoes">As opções; nulas, o caminho feliz.</param>
    /// <returns>O cenário, pronto para executar.</returns>
    internal static async Task<CenarioDaExecucao> CriarAsync(OpcoesDoCenario? opcoes = null)
    {
        opcoes ??= new OpcoesDoCenario();
        var funcao = opcoes.Funcao ?? (_ => new ResultadoTransformacao("TRATADO", Desfecho.Substituido));
        var ponto = opcoes.Ponto;
        var fabrica = new FabricaContadora(valor =>
        {
            ponto?.Invoke("transformacao");
            return funcao(valor);
        });
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        var destino = opcoes.DestinoNoPlanejar ?? Coerente();
        var plano = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(Coerente(), destino), yaml: opcoes.Base + opcoes.Tabelas, registro: registro);
        return new CenarioDaExecucao(plano, registro, fabrica, opcoes);
    }

    /// <summary>Executa o plano.</summary>
    /// <param name="confirmar">A resposta à confirmação.</param>
    /// <param name="resolverChave">O resolvedor da chave; nulo gera uma.</param>
    /// <param name="cancelamento">O token do usuário; nulo, o do teste.</param>
    /// <param name="restauracao">O token próprio da restauração; nulo, o do teste.</param>
    /// <param name="tratamentos">Os tratamentos por tabela (K).</param>
    /// <returns>O resultado.</returns>
    internal Task<ResultadoDaExecucao> ExecutarAsync(
        bool confirmar = true, Func<ReferenciaEscrita, ChaveIncognito?>? resolverChave = null, CancellationToken? cancelamento = null, CancellationToken? restauracao = null, int tratamentos = 2) =>
        ExecutorInterno.ExecutarAsync(
            _plano,
            _registro,
            new Dictionary<ProvedorBanco, ProvedorDialeto> { [ProvedorBanco.PostgreSql] = Provedor },
            _ => DialetoProtegidoTestes.Conexao(),
            resolverChave ?? (_ => ChaveIncognito.Gerar()),
            new OpcoesDaExecucao((_, _) => Task.FromResult(confirmar), AceitarDadosExistentes: false, restauracao ?? TestContext.Current.CancellationToken, Paralelismo: 2, Tratamentos: tratamentos),
            TimeProvider.System,
            cancelamento ?? TestContext.Current.CancellationToken);

    /// <summary>As chamadas de escrita do diário que o falso registrou, na ordem.</summary>
    /// <returns>As chamadas.</returns>
    internal string[] EscritasDoDiario()
    {
        string[] escritas = ["IniciarExecucao", "RemoverChave", "DesligarTriggers", "ReligarTriggers", "RecriarChave", "ValidarChave", "DescartarEntrada", "EncerrarExecucao", "GravarMarcador"];
        lock (Provedor.Chamadas)
        {
            return [.. Provedor.Chamadas.Where(chamada => escritas.Any(escrita => chamada == escrita + ":destino"))];
        }
    }

    /// <summary>Nenhum membro de escrita da cópia: nem o diário da execução, nem esvaziamento, nem gravação.</summary>
    internal void NenhumaEscritaDaCopia()
    {
        Assert.DoesNotContain(EscritasDoDiario(), chamada => chamada is "IniciarExecucao:destino" or "RemoverChave:destino" or "DesligarTriggers:destino" or "ValidarChave:destino");
        Assert.Empty(Gravador.Pedidos);
        Assert.Empty(Gravador.Esvaziadas);
    }

    /// <summary>Se a operação ou o registro veio antes do outro na <see cref="Linha"/>.</summary>
    /// <param name="primeira">O que vem antes.</param>
    /// <param name="depois">O que vem depois.</param>
    internal void Antes(string primeira, string depois)
    {
        var linha = Linha.ToList();
        var indice = linha.IndexOf(primeira);
        var outro = linha.LastIndexOf(depois);
        Assert.True(indice >= 0 && outro > indice, $"{primeira} antes de {depois}: {string.Join(" | ", linha)}");
    }

    // As execuções anteriores continuam no diário, já marcadas como interrompidas pelo reparo.
    private static EstadoDiario Diario(Guid execucao, OpcoesDoCenario opcoes) => new()
    {
        Existe = true,
        Execucoes =
        [
            new ExecucaoLida { Id = execucao, Marcador = Marcado.Identificador!.Value, IniciadaEm = DateTimeOffset.UnixEpoch, Situacao = SituacaoExecucao.EmAndamento },
            .. opcoes.DiarioAntes.Execucoes.Select(anterior => new ExecucaoLida
            {
                Id = anterior.Id,
                Marcador = anterior.Marcador,
                IniciadaEm = anterior.IniciadaEm,
                Situacao = anterior.Situacao == SituacaoExecucao.EmAndamento ? SituacaoExecucao.Interrompida : anterior.Situacao,
            }),
        ],
        Pendentes =
        [
            new EntradaDiario { Execucao = execucao, Tipo = TipoEntrada.ChaveEstrangeira, Tabela = Nome("pedidos"), Nome = "fk_pedidos_clientes", Situacao = SituacaoEntrada.SemValidacao },
            .. opcoes.Antigas,
        ],
    };
}

/// <summary>Um transformador de fora pela função dada, que conta criações e descartes e avisa cada descarte.</summary>
internal sealed class FabricaContadora(Func<object?, ResultadoTransformacao> funcao) : FabricaTransformador
{
    private int _criacoes;
    private int _descartes;

    public override string Nome => "exemplo.funcao";

    public override int Versao => 1;

    public override Injetividade Injetividade => Injetividade.Nenhuma;

    public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

    internal int Criacoes => Volatile.Read(ref _criacoes);

    /// <summary>Todos os descartes: o <c>planejar</c> e o replanejamento criam e descartam um cada; a execução, o seu.</summary>
    internal int Descartes => Volatile.Read(ref _descartes);

    /// <summary>Chamado em cada descarte, antes de contá-lo.</summary>
    internal Action? AoDescartar { get; set; }

    /// <summary>Lança na criação de número dado (a partir de 1: o planejar cria a primeira, o replanejamento a segunda).</summary>
    internal Func<int, Exception?> NaCriacao { get; set; } = _ => null;

    public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        if (NaCriacao(Interlocked.Increment(ref _criacoes)) is { } excecao)
        {
            throw excecao;
        }

        return new Criado(this);
    }

    private sealed class Criado(FabricaContadora fabrica) : Transformador, IDisposable
    {
        public override string Nome => "exemplo.funcao";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => fabrica.Funcao(valor);

        public void Dispose()
        {
            fabrica.AoDescartar?.Invoke();
            Interlocked.Increment(ref fabrica._descartes);
        }
    }

    private Func<object?, ResultadoTransformacao> Funcao => funcao;
}
