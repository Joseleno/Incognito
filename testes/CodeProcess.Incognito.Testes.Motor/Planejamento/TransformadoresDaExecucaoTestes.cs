using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// Os transformadores da execução (passo 7): a chave do resolvedor, a criação protegida pelo contexto da carga, conferida
/// contra o registro, e o descarte uma vez só (C3).
/// </summary>
public sealed class TransformadoresDaExecucaoTestes
{
    private const string Sentinela = "Sentinela7f3a9cQ";
    private const string Contadas = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: exemplo.contada@1 }\n      nome: { transformador: exemplo.contada@1 }\n";

    [Fact]
    public async Task Cria_um_por_tratada_com_o_contexto_da_carga_e_descarta_cada_um_uma_vez()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        var noPlanejar = fabrica.Criados.Count;

        var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);
        var clientes = new NomeTabela("loja", "clientes");
        var cpf = transformadores.Da(clientes, "cpf");
        var nome = transformadores.Da(clientes, "nome");

        Assert.Equal(2, noPlanejar);
        Assert.Equal(4, fabrica.Criados.Count);
        Assert.NotSame(cpf, nome);
        Assert.Equal([11, 100], fabrica.Contextos.Skip(2).Select(contexto => contexto.ComprimentoMaximo ?? 0));
        Assert.All(fabrica.Contextos.Skip(2), contexto => Assert.Equal("homologacao", contexto.Ambiente));

        Assert.Empty(transformadores.Descartar());
        Assert.Empty(transformadores.Descartar());
        Assert.All(fabrica.Criados, criado => Assert.Equal(1, criado.Descartes));
        Assert.Throws<ObjectDisposedException>(() => transformadores.Da(clientes, "cpf"));
        SessaoDescartada(fabrica.Contextos[2]);
    }

    [Fact]
    public async Task A_instancia_repetida_entre_colunas_e_descartada_uma_vez()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        fabrica.Repetir = true;

        var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);
        var clientes = new NomeTabela("loja", "clientes");

        Assert.Same(transformadores.Da(clientes, "cpf"), transformadores.Da(clientes, "nome"));
        transformadores.Descartar();
        Assert.Equal(1, fabrica.Repetido!.Descartes);
    }

    [Fact]
    public async Task Fabrica_que_falha_so_na_execucao_recusa_com_INC3046_so_com_o_tipo_e_descarta_o_que_criou()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        fabrica.NaCriacao = numero => numero == 4 ? new InvalidOperationException(Sentinela) : null;

        var recusa = Assert.Throws<PlanoRecusadoException>(() => TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken));

        var erro = Assert.Single(recusa.Erros);
        Assert.Equal((CodigosPlanejar.FalhaDaFabrica, "clientes", "nome"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.Contains("(InvalidOperationException)", erro.Mensagem, StringComparison.Ordinal);
        DialetoProtegidoTestes.SemVazamento(recusa);
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.All(fabrica.Criados, criado => Assert.Equal(1, criado.Descartes));
        SessaoDescartada(fabrica.Contextos[2]);
    }

    [Fact]
    public async Task Recusa_e_transformador_diferente_do_registrado_na_execucao_saem_com_2()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();

        fabrica.NaCriacao = numero => numero == 3 ? new RegraInvalidaException("a coluna mudou") : null;
        var recusada = Assert.Single(Assert.Throws<PlanoRecusadoException>(() => TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken)).Erros);
        fabrica.NaCriacao = _ => null;
        fabrica.InjetividadeCriada = Injetividade.Garantida;
        var diferentes = Assert.Throws<PlanoRecusadoException>(() => TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken)).Erros;

        Assert.Equal((CodigosPlanejar.CriacaoRecusada, "cpf"), (recusada.Codigo, recusada.Coluna));
        // Na execução, a recusa de uma fábrica de fora sai só com o tipo (a fábrica já recebeu a sessão com a chave real).
        Assert.Contains("(RegraInvalidaException)", recusada.Mensagem, StringComparison.Ordinal);
        Assert.Contains("planeje de novo", recusada.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("a coluna mudou", recusada.Mensagem, StringComparison.Ordinal);
        Assert.Equal([CodigosPlanejar.FalhaDaFabrica, CodigosPlanejar.FalhaDaFabrica], diferentes.Select(erro => erro.Codigo));
        Assert.All(fabrica.Criados, criado => Assert.Equal(1, criado.Descartes));
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Imita a falta de memória de uma fábrica de fora.")]
    public async Task Cancelamento_no_meio_descarta_o_que_criou_e_a_falta_de_memoria_de_fora_sobe_sem_mensagem()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        using var fonte = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fabrica.NaCriacao = numero =>
        {
            if (numero == 3)
            {
                fonte.Cancel();
            }

            return null;
        };

        var cancelado = Assert.Throws<OperationCanceledException>(() => TransformadoresDaExecucao.Criar(carga, chave, fonte.Token));
        Assert.Equal(fonte.Token, cancelado.CancellationToken);
        Assert.All(fabrica.Criados, criado => Assert.Equal(1, criado.Descartes));
        SessaoDescartada(fabrica.Contextos[2]);

        fabrica.NaCriacao = _ => new OutOfMemoryException(Sentinela);
        var semMemoria = Assert.Throws<OutOfMemoryException>(() => TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Sentinela, semMemoria.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falha_no_descarte_de_um_transformador_de_fora_vira_aviso_so_com_o_tipo_e_os_outros_descartam()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);
        fabrica.NoDescarte = criado => criado == fabrica.Criados[2] ? new InvalidOperationException(Sentinela) : null;

        var aviso = Assert.Single(transformadores.Descartar());

        Assert.Equal((CodigosExecucao.DescarteDoTransformador, "cpf"), (aviso.Codigo, aviso.Coluna));
        Assert.Contains("(InvalidOperationException)", aviso.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, aviso.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fabrica.Criados[3].Descartes);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Imita a falta de memória de um transformador de fora.")]
    public async Task A_falta_de_memoria_no_descarte_de_um_de_fora_sobe_nova_depois_de_descartar_o_resto()
    {
        var fabrica = new FabricaContada();
        var carga = await CargaAsync(fabrica);
        using var chave = ChaveIncognito.Gerar();
        var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);
        fabrica.NoDescarte = criado => criado == fabrica.Criados[2] ? new OutOfMemoryException(Sentinela) : null;

        var semMemoria = Assert.Throws<OutOfMemoryException>(() => transformadores.Descartar());

        Assert.DoesNotContain(Sentinela, semMemoria.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fabrica.Criados[3].Descartes);
    }

    [Fact]
    public async Task A_embutida_e_criada_com_a_chave_real()
    {
        var carga = (await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(), yaml: Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n")).Execucao;
        using var chave = ChaveIncognito.Gerar();

        var transformadores = TransformadoresDaExecucao.Criar(carga, chave, TestContext.Current.CancellationToken);

        Assert.Equal(("br.cpf", 1), (transformadores.Da(new NomeTabela("loja", "clientes"), "cpf").Nome, transformadores.Da(new NomeTabela("loja", "clientes"), "cpf").Versao));
        Assert.Empty(transformadores.Descartar());
    }

    [Fact]
    public void A_chave_do_efemero_e_gerada_sem_chamar_o_resolvedor()
    {
        var efemera = ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Trocando("modo: reproduzivel", "modo: efemero").Replace("chave: ambiente:INCOGNITO_CHAVE\n", "", StringComparison.Ordinal)));

        using var chave = ChaveDaExecucao.Obter(efemera, _ => throw new InvalidOperationException("O resolvedor não pode ser chamado no efêmero."));

        Assert.NotNull(chave);
    }

    [Fact]
    public void A_chave_do_reproduzivel_vem_do_resolvedor_e_a_que_nao_se_resolve_sai_com_2_sem_o_caminho()
    {
        var configuracao = ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Minimo));
        var pedidas = new List<ReferenciaEscrita>();
        var resolvida = ChaveIncognito.Gerar();

        using (var chave = ChaveDaExecucao.Obter(configuracao, referencia =>
        {
            pedidas.Add(referencia);
            return resolvida;
        }))
        {
            Assert.Same(resolvida, chave);
        }

        var nula = Assert.Throws<ConfiguracaoInvalidaException>(() => ChaveDaExecucao.Obter(configuracao, _ => null));
        var lancou = Assert.Throws<ConfiguracaoInvalidaException>(() => ChaveDaExecucao.Obter(configuracao, _ => throw new IOException(Sentinela + "/segredos/INCOGNITO_CHAVE")));
        var propria = new ConfiguracaoInvalidaException([new ErroConfiguracao(CodigosConfiguracao.Referencia, null, null, null, "/chave", "Recusada.")], omitidos: false);
        var repassada = Assert.Throws<ConfiguracaoInvalidaException>(() => ChaveDaExecucao.Obter(configuracao, _ => throw propria));
        var alheia = Assert.Throws<ConfiguracaoInvalidaException>(() => ChaveDaExecucao.Obter(configuracao, _ => throw new IncognitoHostil("INC2099")));

        Assert.Equal([new ReferenciaEscrita(TipoReferenciaSegredo.Ambiente, "INCOGNITO_CHAVE")], pedidas);
        Assert.Equal(("INC2012", "/chave"), (Assert.Single(nula.Erros).Codigo, nula.Erros[0].Local));
        Assert.Contains("(IOException)", lancou.Erros[0].Mensagem, StringComparison.Ordinal);
        Assert.All([nula, lancou], recusa => Assert.DoesNotContain("INCOGNITO_CHAVE", recusa.ToString(), StringComparison.Ordinal));
        DialetoProtegidoTestes.SemVazamento(lancou);
        Assert.Same(propria, repassada);

        // Outra IncognitoException do resolvedor é recusa da referência, só com o tipo: a mensagem dela pode ter o caminho.
        Assert.Equal("INC2012", Assert.Single(alheia.Erros).Codigo);
        Assert.Contains("(IncognitoHostil)", alheia.Erros[0].Mensagem, StringComparison.Ordinal);
        DialetoProtegidoTestes.SemVazamento(alheia);
        Assert.Equal(2, ClassificadorDeSaida.De(nula));
    }

    // A sessão da execução (a do contexto que a fábrica recebeu) não cria mais nada: foi descartada depois dos transformadores.
    // Ela lança antes de olhar a chave, e o nome do objeto distingue a sessão descartada da chave descartada.
    private static void SessaoDescartada(ContextoCriacao contexto)
    {
        var sessao = (SessaoTransformacao)typeof(ContextoCriacao).GetProperty("Sessao", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(contexto)!;
        var descartada = Assert.Throws<ObjectDisposedException>(() => TransformadoresEmbutidos.Fabricas[0].Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("cpf", 1)), TipoColuna.Texto, sessao)));
        Assert.Equal(typeof(SessaoTransformacao).FullName, descartada.ObjectName);
    }

    private static async Task<DadosDeExecucao> CargaAsync(FabricaContada fabrica)
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        return (await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(), yaml: Yaml.Minimo + Contadas, registro: registro)).Execucao;
    }

    // Uma fábrica de fora que conta o que cria e descarta, com falhas por número de criação (a 1 e a 2 são as do planejar).
    private sealed class FabricaContada : FabricaTransformador
    {
        private readonly Lock _trava = new();

        public override string Nome => "exemplo.contada";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        internal List<Contado> Criados { get; } = [];

        internal List<ContextoCriacao> Contextos { get; } = [];

        internal Func<int, Exception?> NaCriacao { get; set; } = _ => null;

        internal Func<Contado, Exception?> NoDescarte { get; set; } = _ => null;

        internal Injetividade InjetividadeCriada { get; set; } = Injetividade.Nenhuma;

        internal bool Repetir { get; set; }

        internal Contado? Repetido { get; private set; }

        protected override Transformador CriarCore(ContextoCriacao contexto)
        {
            lock (_trava)
            {
                Contextos.Add(contexto);
                if (NaCriacao(Contextos.Count) is { } excecao)
                {
                    throw excecao;
                }

                if (Repetir && Repetido is { } repetido)
                {
                    return repetido;
                }

                var criado = new Contado(this, InjetividadeCriada);
                Criados.Add(criado);
                Repetido = Repetir ? criado : null;
                return criado;
            }
        }

        internal sealed class Contado(FabricaContada fabrica, Injetividade injetividade) : Transformador, IDisposable
        {
            private int _descartes;

            public override string Nome => "exemplo.contada";

            public override int Versao => 1;

            public override Injetividade Injetividade => injetividade;

            internal int Descartes => Volatile.Read(ref _descartes);

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);

            public void Dispose()
            {
                Interlocked.Increment(ref _descartes);
                if (fabrica.NoDescarte(this) is { } excecao)
                {
                    throw excecao;
                }
            }
        }
    }
}
