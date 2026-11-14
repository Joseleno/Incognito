using System.Collections;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O comparador da carga do <c>planejar</c> com a do replanejamento: campo a campo, com as exclusões declaradas, e nenhuma
/// propriedade pública do modelo nem comparada nem excluída.
/// </summary>
public sealed class ComparadorDaCargaTestes
{
    private const string ComCpf = Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";

    [Fact]
    public async Task Toda_propriedade_publica_alcancavel_do_modelo_e_comparada_ou_excluida()
    {
        var carga = await CargaRicaAsync();
        var lidas = ComparadorDaCarga.Lidas(carga);
        var excluidas = ComparadorDaCarga.ExcluidasSempre.Concat(ComparadorDaCarga.ExcluidasNoDestino).ToHashSet();

        // Cada exclusão nomeia uma propriedade que existe: uma exclusão velha esconderia uma propriedade nova de mesmo nome.
        Assert.All(excluidas, excluida => Assert.NotNull(excluida.Tipo.GetProperty(excluida.Propriedade)));

        var faltando = Alcancaveis()
            .SelectMany(tipo => ComparadorDaCarga.Propriedades(tipo).Select(propriedade => (Tipo: tipo, Propriedade: propriedade.Name)))
            .Where(par => !lidas.Contains(par) && !ComparadorDaCarga.ExcluidasSempre.Contains(par))
            .Select(par => $"{par.Tipo.Name}.{par.Propriedade}")
            .ToArray();
        Assert.Empty(faltando);
    }

    [Fact]
    public async Task Cargas_iguais_nao_divergem_e_a_ordem_das_tabelas_no_catalogo_nao_importa()
    {
        var carga = await CargaRicaAsync();
        var invertida = Com(carga, Inverter(carga.Origem.Esquema), Inverter(carga.Destino.Esquema));

        Assert.Null(ComparadorDaCarga.Divergencia(carga, await CargaRicaAsync()));
        Assert.Null(ComparadorDaCarga.Divergencia(carga, invertida));
    }

    [Fact]
    public async Task O_acesso_as_chaves_do_destino_e_o_estado_das_triggers_do_destino_ficam_de_fora()
    {
        var carga = await CargaRicaAsync();
        var semAcesso = Com(carga, BancoFalso.Clonar(carga.Origem.Esquema, (nameof(EsquemaBanco.Acesso), null)), BancoFalso.Clonar(carga.Destino.Esquema, (nameof(EsquemaBanco.Acesso), null)));
        var destinoMexido = Com(carga, carga.Origem.Esquema, MexerEmTodaTabela(carga.Destino.Esquema, ChavesETriggers));

        Assert.Null(ComparadorDaCarga.Divergencia(carga, semAcesso));
        Assert.Null(ComparadorDaCarga.Divergencia(carga, destinoMexido));

        // Na origem, as mesmas mudanças divergem.
        Assert.StartsWith("a tabela ", ComparadorDaCarga.Divergencia(carga, Com(carga, MexerEmTodaTabela(carga.Origem.Esquema, ChavesETriggers), carga.Destino.Esquema)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_divergencia_aponta_a_coluna_antes_da_tabela_e_do_banco()
    {
        var carga = await CargaPlanejadaAsync();
        var unidade = carga.Unidades.Single(unidade => unidade.Nome.Tabela == "clientes");
        var tratada = unidade.Tratadas.Single();
        var outroContexto = tratada with { Contexto = tratada.Contexto with { ComprimentoMaximo = 12 } };
        var trocada = new DadosDeExecucao(
            carga.Configuracao,
            carga.Origem,
            new LeituraDoBanco(TrocarColuna(carga.Destino.Esquema, "clientes", "cpf", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 12, Unidade = UnidadeComprimento.PontosDeCodigo })), carga.Destino.Identidade, carga.Destino.ObjetosDoDiario),
            [.. carga.Unidades.Select(outra => outra == unidade ? ComTratadas(unidade, outroContexto) : outra)],
            carga.Dominios,
            carga.ChavesRemovidas,
            carga.TriggersDesligadas);

        Assert.Equal("a coluna loja.clientes.cpf", ComparadorDaCarga.Divergencia(carga, trocada));
    }

    [Fact]
    public async Task Ordem_das_colunas_da_origem_e_referencia_do_derivarDe_divergem()
    {
        var carga = await CargaPlanejadaAsync();
        var invertida = Trocar(carga.Origem.Esquema, "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Reverse().ToArray())));
        var unidade = carga.Unidades.Single(unidade => unidade.Nome.Tabela == "clientes");
        var tratada = unidade.Tratadas.Single();
        var comReferencia = ComTratadas(unidade, new ColunaTratada(
            tratada.Coluna, tratada.Fabrica, tratada.Regra, tratada.Normalizados, tratada.Contexto with { TipoReferencia = Transformadores.TipoColuna.Texto }, "nome", tratada.InjetividadeEfetiva));

        Assert.Equal("a tabela loja.clientes da origem", ComparadorDaCarga.Divergencia(carga, Com(carga, invertida, carga.Destino.Esquema)));
        Assert.Equal("a coluna loja.clientes.cpf", ComparadorDaCarga.Divergencia(carga, ComUnidades(carga, [.. carga.Unidades.Select(outra => outra == unidade ? comReferencia : outra)])));
    }

    [Fact]
    public async Task Unidade_a_mais_ou_a_menos_diverge_pelo_nome_dela()
    {
        var carga = await CargaPlanejadaAsync();
        var sem = ComUnidades(carga, [.. carga.Unidades.Where(unidade => unidade.Nome.Tabela != "pedidos")]);

        Assert.Equal("a tabela loja.pedidos", ComparadorDaCarga.Divergencia(carga, sem));
        Assert.Equal("a tabela loja.pedidos", ComparadorDaCarga.Divergencia(sem, carga));
    }

    [Fact]
    public async Task A_forma_canonica_nunca_sai_e_a_divergencia_so_tem_o_lugar()
    {
        const string Sentinela = "Sentinela7f3a9cQ";
        var carga = await CargaPlanejadaAsync();
        var comSentinela = Com(carga, TrocarColuna(carga.Origem.Esquema, "clientes", "nome", (nameof(ColunaEsquema.TipoNativo), Sentinela)), carga.Destino.Esquema);

        var divergencia = ComparadorDaCarga.Divergencia(carga, comSentinela);

        Assert.Equal("a tabela loja.clientes da origem", divergencia);
        Assert.Contains(ComparadorDaCarga.EntradasEmOrdem(comSentinela), entrada => entrada.Value.Contains(Sentinela, StringComparison.Ordinal));
    }

    // Uma carga com todo tipo do modelo instanciado: o esquema completo do falso dos dois lados, as unidades de um plano com
    // tratada e um domínio declarado.
    internal static async Task<DadosDeExecucao> CargaRicaAsync()
    {
        var planejada = await CargaPlanejadaAsync();
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await new LeitorFalso(completo: true, Defeito.Nenhum, perturbar: null, destino: false, privilegiado: true).LerAsync(cancelamento);
        var destino = await new LeitorFalso(completo: true, Defeito.Nenhum, perturbar: null, destino: true, privilegiado: true).LerAsync(cancelamento);
        var dominio = new DefinicaoDominio("id.permutar", new RegraTransformacao(new DominioPseudonimo("pedido-id", 1)));
        return new DadosDeExecucao(
            planejada.Configuracao,
            new LeituraDoBanco(origem, Identidade("loja")),
            new LeituraDoBanco(destino, Identidade("loja_homolog"), BancoFalso.Reservado(completo: true)),
            planejada.Unidades,
            [dominio],
            planejada.ChavesRemovidas,
            planejada.TriggersDesligadas);
    }

    private static async Task<DadosDeExecucao> CargaPlanejadaAsync() =>
        (await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(), yaml: ComCpf)).Execucao;

    private static DadosDeExecucao Com(DadosDeExecucao carga, EsquemaBanco origem, EsquemaBanco destino) => new(
        carga.Configuracao,
        new LeituraDoBanco(origem, carga.Origem.Identidade),
        new LeituraDoBanco(destino, carga.Destino.Identidade, carga.Destino.ObjetosDoDiario),
        carga.Unidades,
        carga.Dominios,
        carga.ChavesRemovidas,
        carga.TriggersDesligadas);

    private static DadosDeExecucao ComUnidades(DadosDeExecucao carga, IReadOnlyList<UnidadeDeCopia> unidades) =>
        new(carga.Configuracao, carga.Origem, carga.Destino, unidades, carga.Dominios, carga.ChavesRemovidas, carga.TriggersDesligadas);

    private static EsquemaBanco Inverter(EsquemaBanco esquema) => BancoFalso.Clonar(
        esquema,
        (nameof(EsquemaBanco.Tabelas), esquema.Tabelas.Reverse().ToArray()),
        (nameof(EsquemaBanco.Sequences), esquema.Sequences.Reverse().ToArray()),
        (nameof(EsquemaBanco.ObjetosResiduais), esquema.ObjetosResiduais.Reverse().ToArray()));

    private static EsquemaBanco MexerEmTodaTabela(EsquemaBanco esquema, Func<TabelaEsquema, TabelaEsquema> mexer) =>
        BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Tabelas), esquema.Tabelas.Select(mexer).ToArray()));

    // Sem as chaves estrangeiras, e cada trigger com o estado trocado.
    private static TabelaEsquema ChavesETriggers(TabelaEsquema tabela) => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.ChavesEstrangeiras), Array.Empty<ChaveEstrangeira>()),
        (nameof(TabelaEsquema.Triggers), tabela.Triggers.Select(trigger => BancoFalso.Clonar(trigger, (nameof(TriggerEsquema.Ligada), !trigger.Ligada))).ToArray()));

    private static UnidadeDeCopia ComTratadas(UnidadeDeCopia unidade, params ColunaTratada[] tratadas) =>
        new(unidade.Nome, unidade.Acao, unidade.NoDestino, unidade.ColunasLidas, unidade.ColunasNoDestino, tratadas);

    // Os tipos do modelo alcançáveis da carga pelas propriedades públicas, fora as sempre excluídas, abrindo coleções e
    // anuláveis.
    private static HashSet<Type> Alcancaveis()
    {
        var vistos = new HashSet<Type>();
        var pendentes = new Queue<Type>([typeof(DadosDeExecucao)]);
        while (pendentes.TryDequeue(out var tipo))
        {
            if (!vistos.Add(tipo))
            {
                continue;
            }

            foreach (var propriedade in ComparadorDaCarga.Propriedades(tipo).Where(propriedade => !ComparadorDaCarga.ExcluidasSempre.Contains((tipo, propriedade.Name))))
            {
                foreach (var alcancado in Abrir(propriedade.PropertyType).Where(ComparadorDaCarga.DoModelo))
                {
                    pendentes.Enqueue(alcancado);
                }
            }
        }

        return vistos;
    }

    private static IEnumerable<Type> Abrir(Type tipo)
    {
        if (Nullable.GetUnderlyingType(tipo) is { } subjacente)
        {
            return Abrir(subjacente);
        }

        if (tipo.IsArray)
        {
            return Abrir(tipo.GetElementType()!);
        }

        if (tipo.IsGenericType && typeof(IEnumerable).IsAssignableFrom(tipo))
        {
            return tipo.GetGenericArguments().SelectMany(Abrir);
        }

        return [tipo];
    }
}
