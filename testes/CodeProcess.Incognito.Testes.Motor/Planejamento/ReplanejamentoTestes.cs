using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O replanejamento da execução (passos 5 e 6): a montagem de novo, pelo caminho do <c>planejar</c>, com a origem relida no
/// instante e o destino relido depois do reparo, e a recusa com 2 (<c>INC3005</c>) quando o banco mudou.
/// </summary>
public sealed class ReplanejamentoTestes
{
    private const string Sentinela = "Sentinela7f3a9cQ";
    private const string ComCpf = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";
    private const string ComPermutar = "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido-id }\n";

    [Fact]
    public async Task Banco_igual_replaneja_a_mesma_carga_que_a_execucao_usa_dali_em_diante()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());

        var refeita = await ReplanejarAsync(plano, Coerente(), Coerente());

        Assert.NotSame(plano.Execucao, refeita);
        Assert.Null(ComparadorDaCarga.Divergencia(plano.Execucao, refeita));
        Assert.Equal(plano.Execucao.Unidades.Select(unidade => unidade.Nome), refeita.Unidades.Select(unidade => unidade.Nome));
    }

    [Fact]
    public async Task Coluna_do_destino_que_encolhe_recusa_com_INC3005_so_com_o_nome_da_tabela()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var encolhida = TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 37, Unidade = UnidadeComprimento.PontosDeCodigo }));

        var erro = Assert.Single(await RecusaAsync(plano, Coerente(), encolhida));

        Assert.Equal("INC3005", erro.Codigo);
        Assert.Equal(
            "O banco mudou desde o planejar (a tabela loja.clientes do destino não é mais o que o plano viu): planeje de novo antes de executar.",
            erro.Mensagem);
        Assert.Equal(2, ClassificadorDeSaida.De(new PlanoRecusadoException([erro])));
    }

    [Fact]
    public async Task Tratada_com_o_contexto_mudado_aponta_a_coluna()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var maior = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 14, Unidade = UnidadeComprimento.PontosDeCodigo }));

        var erro = Assert.Single(await RecusaAsync(plano, Coerente(), maior));

        Assert.Equal("INC3005", erro.Codigo);
        Assert.Contains("(a coluna loja.clientes.cpf não é mais", erro.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("14", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N_aumentado_recusa_na_coluna_do_id_permutar_sem_o_N_e_le_pelo_leitor_da_sessao()
    {
        var plano = await PlanejarAsync(ComPermutar, Coerente(), Coerente(), maiores: _ => 5);
        var lidas = new List<ReferenciaColuna>();

        // N = 10^max(4, dígitos(M) + 1): de 10.000 (M = 5) para 1.000.000 (M = 50.000).
        await ReplanejarAsync(plano, Coerente(), Coerente(), maiores: _ => 9, lidas: lidas);
        var erro = Assert.Single(await RecusaAsync(plano, Coerente(), Coerente(), maiores: _ => 50_000));

        Assert.Equal([new ReferenciaColuna(Nome("pedidos"), "id")], lidas);
        Assert.Equal("INC3005", erro.Codigo);
        Assert.Contains("(a coluna loja.pedidos.id não é mais", erro.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("000", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tabela_so_no_destino_criada_depois_recusa_com_INC3005_e_os_erros_da_montagem()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var comNova = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("novidade"), Colunas = [Inteiro("id")] });

        var erros = await RecusaAsync(plano, Coerente(), comNova);

        Assert.Equal("INC3005", erros[0].Codigo);
        Assert.Contains("(o banco não é mais", erros[0].Mensagem, StringComparison.Ordinal);
        Assert.Contains(erros.Skip(1), erro => erro.Codigo == CodigosPlanejar.SoNoDestino && erro.Tabela!.Tabela == "novidade");
    }

    [Fact]
    public async Task Seguranca_por_linha_ligada_depois_na_origem_recusa()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var comRls = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.SegurancaDeLinha), true)));

        var erros = await RecusaAsync(plano, comRls, Coerente());

        Assert.Equal("INC3005", erros[0].Codigo);
    }

    [Fact]
    public async Task Chave_estrangeira_nova_e_trigger_desligada_no_destino_entram_na_carga_refeita_sem_recusa()
    {
        var comTrigger = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Triggers), new[] { new TriggerEsquema { Nome = "auditoria", Ligada = true, Herdada = false } })));
        var plano = await PlanejarAsync(ComCpf, Coerente(), comTrigger);
        var novaChave = new ChaveEstrangeira { Nome = "fk_pedidos_clientes_2", Colunas = ["cliente"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["id"], Imposta = true, Validada = true, DesligadaSemRestauracao = false };
        var mudado = Trocar(
            Trocar(comTrigger, "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), tabela.ChavesEstrangeiras.Append(novaChave).ToArray()))),
            "clientes",
            tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Triggers), new[] { new TriggerEsquema { Nome = "auditoria", Ligada = false, Herdada = false } })));

        var refeita = await ReplanejarAsync(plano, Coerente(), mudado);

        Assert.DoesNotContain(plano.Execucao.ChavesRemovidas, chave => chave.Chave == "fk_pedidos_clientes_2");
        Assert.Contains(refeita.ChavesRemovidas, chave => chave.Chave == "fk_pedidos_clientes_2");
        Assert.Contains(new NomeObjeto("loja", "clientes"), plano.Execucao.TriggersDesligadas);
        Assert.DoesNotContain(new NomeObjeto("loja", "clientes"), refeita.TriggersDesligadas);
    }

    [Fact]
    public async Task So_o_acesso_mudou_nao_recusa()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var origem = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(Coerente().Acesso!, (nameof(AcessoLido.Banco), new PermissoesBanco { PodeMarcarBanco = false, PodeCriarEsquema = false }))));

        var refeita = await ReplanejarAsync(plano, origem, Coerente());

        Assert.Null(ComparadorDaCarga.Divergencia(plano.Execucao, refeita));
    }

    [Fact]
    public async Task Fabrica_inconstante_recusa_na_criacao_do_replanejamento()
    {
        var fabrica = new FabricaInconstante();
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        const string Yaml = "tabelas:\n  loja.clientes:\n    colunas:\n      nome: { transformador: exemplo.inconstante@1 }\n";
        var plano = await PlanejarAsync(Yaml, Coerente(), Coerente(), registro: registro);

        var erros = await RecusaAsync(plano, Coerente(), Coerente(), registro: registro);

        Assert.Equal(["INC3005", CodigosPlanejar.FalhaDaFabrica], erros.Select(erro => erro.Codigo));
        Assert.Equal(2, fabrica.Criacoes);
    }

    [Fact]
    public async Task Hash_da_configuracao_diferente_recusa_apontando_a_configuracao_mesmo_com_a_carga_igual()
    {
        // O registro que mudou entre o planejar e o executar muda o hash das regras resolvidas; a carga, aqui, fica igual.
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var outro = new Plano(plano.Tabelas, plano.Avisos, "sha256:" + new string('0', 64), plano.Execucao, plano.FonteDoPlano);

        var erro = Assert.Single(await RecusaAsync(outro, Coerente(), Coerente()));

        // O hash vem antes da montagem: com o banco também mudado a ponto de ela recusar, a causa apontada é a configuração.
        var comTabelaNova = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("novidade"), Colunas = [Inteiro("id")] });
        Assert.Equal([erro.Mensagem], (await RecusaAsync(outro, Coerente(), comTabelaNova)).Select(recusa => recusa.Mensagem));

        Assert.Equal("INC3005", erro.Codigo);
        Assert.Contains("A configuração ou o registro de transformadores mudou desde o planejar", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_diario_criado_pelo_preparar_destino_depois_do_planejar_nao_recusa()
    {
        // O planejar sobre o destino ainda sem diário avisa para rodar o preparar-destino; os objetos que ele cria no esquema
        // reservado não são mudança do banco (a montagem refeita já recusa objeto alheio ali).
        var objetos = BancoFalso.Reservado(completo: true);
        var plano = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(Coerente(), Coerente()) { ObjetosDoDiario = objetos }, yaml: Yaml.Minimo + ComCpf);
        Assert.Contains(plano.Avisos, aviso => aviso.Codigo == "INC3584");
        var preparado = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosNoEsquemaReservado), objetos));

        var refeita = await ReplanejarAsync(plano, Coerente(), preparado, doDiario: objetos);

        Assert.Equal(plano.Execucao.Unidades.Count, refeita.Unidades.Count);
    }

    [Fact]
    public async Task Nenhuma_mensagem_traz_trecho_do_esquema()
    {
        var plano = await PlanejarAsync(ComCpf, Coerente(), Coerente());
        var comSentinela = TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.TipoNativo), Sentinela));

        var recusa = Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => ReplanejarAsync(plano, comSentinela, Coerente())), exactMatch: true);

        DialetoProtegidoTestes.SemVazamento(recusa);
        Assert.All(recusa.Erros, erro => Assert.DoesNotContain(Sentinela, erro.Mensagem, StringComparison.OrdinalIgnoreCase));
    }

    private static Task<Plano> PlanejarAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino, Func<ReferenciaColuna, long?>? maiores = null, RegistroTransformadores? registro = null) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino) { Maiores = maiores }, yaml: Yaml.Minimo + yaml, registro: registro);

    private static Task<DadosDeExecucao> ReplanejarAsync(
        Plano plano,
        EsquemaBanco origem,
        EsquemaBanco destino,
        Func<ReferenciaColuna, long?>? maiores = null,
        RegistroTransformadores? registro = null,
        List<ReferenciaColuna>? lidas = null,
        IReadOnlyList<ObjetoReservado>? doDiario = null) =>
        Replanejador.ReplanejarAsync(
            plano,
            registro ?? new RegistroTransformadores(),
            new LeituraDoBanco(origem, Identidade("loja")),
            new LeituraDoBanco(destino, Identidade("loja_homolog"), doDiario ?? []),
            Comparar,
            Comparar,
            (coluna, _) =>
            {
                lidas?.Add(coluna);
                return Task.FromResult(maiores?.Invoke(coluna));
            },
            ChaveIncognito.Gerar,
            TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(
        Plano plano, EsquemaBanco origem, EsquemaBanco destino, Func<ReferenciaColuna, long?>? maiores = null, RegistroTransformadores? registro = null) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => ReplanejarAsync(plano, origem, destino, maiores, registro)), exactMatch: true).Erros;

    // Como o falso do planejar: o nome sem aspas em minúsculas ASCII, e a comparação ordinal.
    private static CorrespondenciaNome Comparar(string escrito, bool entreAspas, string doCatalogo) =>
        string.Equals(entreAspas ? escrito : string.Concat(escrito.Select(caractere => char.IsAsciiLetterUpper(caractere) ? char.ToLowerInvariant(caractere) : caractere)), doCatalogo, StringComparison.Ordinal)
            ? CorrespondenciaNome.Corresponde
            : CorrespondenciaNome.NaoCorresponde;

    // Uma fábrica que cria, da segunda vez, um transformador com outra injetividade: a criação conferida a recusa.
    private sealed class FabricaInconstante : FabricaTransformador
    {
        private int _criacoes;

        public override string Nome => "exemplo.inconstante";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        internal int Criacoes => Volatile.Read(ref _criacoes);

        protected override Transformador CriarCore(ContextoCriacao contexto) =>
            new Criado(Interlocked.Increment(ref _criacoes) == 1 ? Injetividade.Garantida : Injetividade.Nenhuma);

        private sealed class Criado(Injetividade injetividade) : Transformador
        {
            public override string Nome => "exemplo.inconstante";

            public override int Versao => 1;

            public override Injetividade Injetividade => injetividade;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);
        }
    }
}
