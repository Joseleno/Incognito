using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using CsCheck;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O <c>id.permutar</c> no plano (INC3090 a INC3092): o N de cada domínio sai do maior valor das colunas dele na chave
/// primária da origem, lido como a cópia lê; a coluna que não comporta N − 1 no destino, o domínio sem fonte e o valor fora
/// do domínio numa coluna sem chave validada recusam o plano. O maior valor lido nunca aparece em nenhuma saída.
/// </summary>
public sealed class IdentificadorNoPlanoTestes
{
    // O maior valor que nenhuma saída pode trazer: dígitos misturados, 12 dígitos, nem N nem N − 1.
    private const long Sentinela = 731_826_459_017;

    private const string Pedidos = "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido-id }\n";

    private static readonly string[] _id = ["id"];

    [Fact]
    public async Task FK_com_dominio_diferente_da_PK_permutada_sai_com_codigo_2()
    {
        var outro = Pedidos + "  loja.itens:\n    colunas:\n      pedido: { transformador: id.permutar@1, dominio: outro-id }\n";
        var cpf = Pedidos + "  loja.itens:\n    colunas:\n      pedido: { transformador: br.cpf@1 }\n";

        var erroDoOutro = Assert.Single(await RecusaAsync(outro, ComItens()));
        var erroDoCpf = Assert.Single(await RecusaAsync(cpf, ComItens()));

        // O erro sai na segunda em ordem de tabela e coluna: itens.pedido vem antes de pedidos.id.
        Assert.Equal(("INC3030", "pedidos", "id"), (erroDoOutro.Codigo, erroDoOutro.Tabela!.Tabela, erroDoOutro.Coluna));
        Assert.Contains("domínio outro-id@1 ≠ pedido-id@1", erroDoOutro.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC3030", erroDoCpf.Codigo);
        Assert.Contains("transformador ", erroDoCpf.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_FK_sem_regra_recebe_o_dominio_e_o_mesmo_N_e_a_carga_leva_o_N()
    {
        var provedor = Provedor(ComItens(), maiores: coluna => coluna.Tabela.Nome == "pedidos" ? 4_321 : 999_999);

        var plano = await PlanejarAsync(provedor, Pedidos);

        var pk = Regra(plano, "pedidos", "id");
        var fk = Regra(plano, "itens", "pedido");
        Assert.Equal((100_000L, 100_000L), (pk.TamanhoDominio, fk.TamanhoDominio));
        Assert.Equal(new DominioPseudonimo("pedido-id", 1), fk.Dominio);
        Assert.Null(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "itens").Colunas.Single(coluna => coluna.Nome == "id").Regra);
        Assert.All(plano.Execucao.Unidades.SelectMany(unidade => unidade.Tratadas), tratada => Assert.Equal(100_000L, tratada.Contexto.TamanhoDominio));

        // A FK imposta e validada até a fonte não é lida.
        Assert.Equal(["loja.pedidos.id"], provedor.MaioresPedidos.Select(Texto));
        Assert.Contains("LerMaiorValor:origem", provedor.Chamadas);
        Assert.DoesNotContain("LerMaiorValor:destino", provedor.Chamadas);
    }

    [Theory]
    [InlineData(100_000_000L, true)]
    [InlineData(99_999_999L, false)]
    public async Task Integer_com_maior_valor_de_100_milhoes_sai_com_codigo_2(long maior, bool recusa)
    {
        var esquema = Esquema(Tabela("pedidos", [Inteiro32("id")], chave: _id));
        var provedor = Provedor(esquema, maiores: _ => maior);

        if (!recusa)
        {
            Assert.Equal(1_000_000_000L, Regra(await PlanejarAsync(provedor, Pedidos), "pedidos", "id").TamanhoDominio);
            return;
        }

        var erro = Assert.Single(await RecusaAsync(provedor, Pedidos));
        Assert.Equal(("INC3090", "pedidos", "id", "/tabelas/loja.pedidos/colunas/id"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna, erro.Posicao!.Local));
        Assert.Contains("não comporta no destino 9999999999", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("N = 10^10", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L, 10_000L)]
    [InlineData(1L, 10_000L)]
    [InlineData(999L, 10_000L)]
    [InlineData(1_000L, 100_000L)]
    [InlineData(99_999_999L, 1_000_000_000L)]
    [InlineData(100_000_000L, 10_000_000_000L)]
    [InlineData(99_999_999_999_999_999L, 1_000_000_000_000_000_000L)]
    public void N_e_a_menor_potencia_de_10_a_partir_de_10_mil_acima_de_dez_vezes_o_maior(long maior, long tamanho) =>
        Assert.Equal(tamanho, MontagemDoPlano.Tamanho(maior));

    [Theory]
    [InlineData(100_000_000_000_000_000L)]
    [InlineData(long.MaxValue)]
    public void A_partir_de_10_elevado_a_17_o_N_passaria_de_10_elevado_a_18(long maior) => Assert.Null(MontagemDoPlano.Tamanho(maior));

    [Fact]
    public void A_conta_do_N_vale_para_todo_maior_abaixo_de_10_elevado_a_17() =>
        Gen.Long[0, 99_999_999_999_999_999L].Sample(maior =>
        {
            var tamanho = MontagemDoPlano.Tamanho(maior)!.Value;
            var potencia = tamanho.ToString(CultureInfo.InvariantCulture);
            return potencia[0] == '1' && potencia.Skip(1).All(digito => digito == '0')
                && tamanho / 10 > maior && tamanho >= 10_000 && tamanho / 10 <= Math.Max(maior * 10, 1_000);
        });

    [Theory]
    [InlineData(null)]
    [InlineData(-5L)]
    [InlineData(0L)]
    public async Task Sem_linha_ou_so_com_zero_e_negativos_o_N_e_10_mil(long? maior)
    {
        var plano = await PlanejarAsync(Provedor(ComItens(), maiores: _ => maior), Pedidos);

        Assert.Equal(10_000L, Regra(plano, "pedidos", "id").TamanhoDominio);
    }

    [Fact]
    public async Task O_maior_que_passaria_de_10_elevado_a_18_recusa_o_dominio_na_declaracao_e_nao_cria_nenhuma_coluna()
    {
        var erro = Assert.Single(await RecusaAsync(Provedor(ComItens(), maiores: _ => long.MaxValue), Pedidos));

        Assert.Equal(("INC3090", null, "/tabelas/loja.pedidos/colunas/id"), (erro.Codigo, erro.Tabela, erro.Posicao!.Local));
        Assert.Contains("passaria de 10^18", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("declarado em loja.pedidos.id", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(999L, false)]
    [InlineData(1_000L, true)]
    public async Task Numa_smallint_999_passa_e_1000_recusa(long maior, bool recusa)
    {
        var esquema = Esquema(Tabela("pedidos", [Inteiro16("id")], chave: _id));
        var provedor = Provedor(esquema, maiores: _ => maior);

        if (recusa)
        {
            Assert.Equal("INC3090", Assert.Single(await RecusaAsync(provedor, Pedidos)).Codigo);
        }
        else
        {
            Assert.Equal(10_000L, Regra(await PlanejarAsync(provedor, Pedidos), "pedidos", "id").TamanhoDominio);
        }
    }

    [Theory]
    [InlineData(9_999L, false)]
    [InlineData(9_998L, true)]
    public async Task A_coluna_precisa_comportar_exatamente_N_menos_1(long maximo, bool recusa)
    {
        var coluna = BancoFalso.Clonar(Inteiro("id", anulavel: false), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = 0, Maximo = maximo }));
        var provedor = Provedor(Esquema(Tabela("pedidos", [coluna], chave: _id)), maiores: _ => 1);

        if (recusa)
        {
            Assert.Equal("INC3090", Assert.Single(await RecusaAsync(provedor, Pedidos)).Codigo);
        }
        else
        {
            Assert.Equal(10_000L, Regra(await PlanejarAsync(provedor, Pedidos), "pedidos", "id").TamanhoDominio);
        }
    }

    [Fact]
    public async Task FK_integer_ligada_a_PK_bigint_com_N_de_10_elevado_a_10_recusa_so_a_FK()
    {
        var esquema = ComItens(pedido: Inteiro32("pedido"));
        var provedor = Provedor(esquema, maiores: _ => 100_000_000);

        var erro = Assert.Single(await RecusaAsync(provedor, Pedidos));

        Assert.Equal(("INC3090", "itens", "pedido"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.Contains("Regra propagada de loja.pedidos.id.", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_herdeira_ligada_a_fonte_tambem_e_fonte_e_o_maior_dela_faz_o_N_subir()
    {
        var esquema = Esquema(
            Tabela("mae", [Inteiro("id", anulavel: false)], chave: _id),
            Tabela("herdeira", [Inteiro("id", anulavel: false)], tipo: TipoTabela.Herdeira, pai: "mae"));
        var provedor = Provedor(esquema, maiores: coluna => coluna.Tabela.Nome == "mae" ? 5 : 50_000);

        var plano = await PlanejarAsync(provedor, "tabelas:\n  loja.mae:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pessoa-id }\n");

        Assert.Equal((1_000_000L, 1_000_000L), (Regra(plano, "mae", "id").TamanhoDominio, Regra(plano, "herdeira", "id").TamanhoDominio));
        Assert.Equal(["loja.herdeira.id", "loja.mae.id"], provedor.MaioresPedidos.Select(Texto));
    }

    [Fact]
    public async Task A_PK_composta_conta_a_coluna_em_qualquer_posicao()
    {
        var esquema = Esquema(Tabela("eventos", [Inteiro("criado", anulavel: false), Inteiro("id", anulavel: false)], chave: ["criado", "id"]));
        var provedor = Provedor(esquema, maiores: _ => 123_456);

        var plano = await PlanejarAsync(provedor, "tabelas:\n  loja.eventos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: evento-id }\n");

        Assert.Equal(10_000_000L, Regra(plano, "eventos", "id").TamanhoDominio);
    }

    [Fact]
    public async Task A_citacao_sem_FK_recebe_o_mesmo_N_e_e_lida_como_fora_da_fonte()
    {
        var esquema = ComItens(chaves: []);
        var provedor = Provedor(esquema, maiores: coluna => coluna.Tabela.Nome == "pedidos" ? 50 : 9_999);

        var plano = await PlanejarAsync(provedor, Pedidos + "  loja.itens:\n    colunas:\n      pedido: { dominio: pedido-id }\n");

        Assert.Equal((10_000L, 10_000L), (Regra(plano, "pedidos", "id").TamanhoDominio, Regra(plano, "itens", "pedido").TamanhoDominio));
        Assert.Equal(["loja.itens.pedido", "loja.pedidos.id"], provedor.MaioresPedidos.Select(Texto));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    public async Task Fora_da_FK_validada_o_valor_a_partir_de_N_recusa_a_coluna(bool? validada, bool imposta)
    {
        var esquema = ComItens(chaves: [Chave("fk_itens", ["pedido"], "pedidos", _id, imposta: imposta, validada: validada)]);
        var provedor = Provedor(esquema, maiores: coluna => coluna.Tabela.Nome == "pedidos" ? 50 : 10_000);

        var erro = Assert.Single(await RecusaAsync(provedor, Pedidos));

        Assert.Equal(("INC3092", "itens", "pedido"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.Contains("a partir de N = 10^4", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("Regra propagada de loja.pedidos.id.", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_FK_composta_nao_cobre_a_coluna_e_ela_e_lida()
    {
        var pedidos = Tabela("pedidos", [Inteiro("id", anulavel: false), Inteiro("loja", anulavel: false)], chave: ["id", "loja"]);
        var itens = Tabela("itens", [Inteiro("id", anulavel: false), Inteiro("pedido"), Inteiro("loja")], chave: _id, chaves: [Chave("fk_itens", ["pedido", "loja"], "pedidos", ["id", "loja"])]);
        var provedor = Provedor(Esquema(pedidos, itens), maiores: _ => 7);

        await PlanejarAsync(provedor, Pedidos);

        Assert.Equal(["loja.itens.pedido", "loja.pedidos.id"], provedor.MaioresPedidos.Select(Texto));
    }

    [Theory]
    [InlineData("anular", 1)]
    [InlineData("preservar", 0)]
    public async Task So_a_entrada_malformada_que_falha_recusa_o_valor_fora_do_dominio(string politica, int erros)
    {
        // Com 'anular', a coluna NOT NULL dá o 3048, nunca o 3092; com 'preservar', o plano passa (e avisa o 3591).
        var esquema = ComItens(chaves: [], pedido: Inteiro("pedido", anulavel: erros == 0));
        var provedor = Provedor(esquema, maiores: coluna => coluna.Tabela.Nome == "pedidos" ? 50 : 10_000);
        var yaml = Pedidos + $"  loja.itens:\n    colunas:\n      pedido: {{ dominio: pedido-id, entradaMalformada: {politica} }}\n";

        if (erros == 0)
        {
            Assert.Contains((await PlanejarAsync(provedor, yaml)).Avisos, aviso => aviso.Codigo == "INC3591");
            return;
        }

        Assert.Equal("INC3048", Assert.Single(await RecusaAsync(provedor, yaml)).Codigo);
    }

    [Fact]
    public async Task Dominio_so_em_coluna_fora_da_PK_da_INC3091_na_declaracao()
    {
        var provedor = Provedor(ComItens(chaves: []));

        var erro = Assert.Single(await RecusaAsync(provedor, "tabelas:\n  loja.itens:\n    colunas:\n      pedido: { transformador: id.permutar@1, dominio: pedido-id }\n"));

        Assert.Equal(("INC3091", "/tabelas/loja.itens/colunas/pedido"), (erro.Codigo, erro.Posicao!.Local));
        Assert.Contains("O domínio pedido-id do id.permutar não tem coluna na chave primária", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("declarado em loja.itens.pedido", erro.Mensagem, StringComparison.Ordinal);
        Assert.Empty(provedor.MaioresPedidos);
    }

    [Fact]
    public async Task A_fonte_num_componente_recusado_nao_da_INC3091_na_citacao()
    {
        // pedidos.id e itens.pedido em conflito (3030); vendas.pedido cita o domínio sem chave e fica sem N, sem erro a mais.
        var esquema = Esquema(
            Tabela("pedidos", [Inteiro("id", anulavel: false)], chave: _id),
            Tabela("itens", [Inteiro("id", anulavel: false), Inteiro("pedido")], chave: _id, chaves: [Chave("fk_itens", ["pedido"], "pedidos", _id)]),
            Tabela("vendas", [Inteiro("id", anulavel: false), Inteiro("pedido")], chave: _id));
        var provedor = Provedor(esquema, maiores: _ => 1);
        var yaml = Pedidos + "  loja.itens:\n    colunas:\n      pedido: { transformador: br.cpf@1 }\n  loja.vendas:\n    colunas:\n      pedido: { dominio: pedido-id }\n";

        var erros = await RecusaAsync(provedor, yaml);

        Assert.Equal(["INC3030"], erros.Select(erro => erro.Codigo));
        Assert.Empty(provedor.MaioresPedidos);
    }

    [Fact]
    public async Task O_dominio_declarado_na_raiz_tambem_vale_e_o_INC3091_aponta_a_raiz()
    {
        var raiz = "dominios:\n  pedido-id: { transformador: id.permutar@1 }\n";

        var plano = await PlanejarAsync(Provedor(ComItens(), maiores: _ => 12), raiz + "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { dominio: pedido-id }\n");
        var erro = Assert.Single(await RecusaAsync(Provedor(ComItens(chaves: [])), raiz + "tabelas:\n  loja.itens:\n    colunas:\n      pedido: { dominio: pedido-id }\n"));

        Assert.Equal(10_000L, Regra(plano, "itens", "pedido").TamanhoDominio);
        Assert.Equal(("INC3091", "/dominios/pedido-id"), (erro.Codigo, erro.Posicao!.Local));
        Assert.DoesNotContain("declarado em", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_id_permutar_sem_dominio_e_erro_de_configuracao()
    {
        var excecao = await Assert.ThrowsAsync<ConfiguracaoInvalidaException>(() => PlanejarAsync(Provedor(ComItens()), "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1 }\n"));

        var erro = Assert.Single(excecao.Erros);
        Assert.Equal(("INC2007", "/tabelas/loja.pedidos/colunas/id/transformador"), (erro.Codigo, erro.Local));
        Assert.Contains("O id.permutar precisa de 'dominio'", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "é da chave primária no destino")]
    [InlineData(false, "é da chave primária na origem")]
    public async Task Anular_na_PK_permutada_da_INC3048_pela_chave_do_destino_ou_da_origem(bool chaveNoDestino, string porque)
    {
        var origem = Esquema(Tabela("pedidos", [Inteiro("id")], chave: _id));
        var destino = chaveNoDestino ? origem : Esquema(Tabela("pedidos", [Inteiro("id")]));
        var provedor = new ProvedorDoPlanejar(origem, destino) { Maiores = _ => 1 };

        var erros = await RecusaAsync(provedor, "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido-id, entradaMalformada: anular }\n");

        var erro = Assert.Single(erros, erro => erro.Codigo == "INC3048");
        Assert.Contains(porque, erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("(escrita na coluna)", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Na_FK_que_herda_o_anular_o_INC3048_aponta_a_declaracao_na_coluna()
    {
        var provedor = Provedor(ComItens(pedido: Inteiro("pedido", anulavel: false)), maiores: _ => 1);

        var erros = await RecusaAsync(provedor, "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido-id, entradaMalformada: anular }\n");

        var naFk = Assert.Single(erros, erro => erro.Codigo == "INC3048" && erro.Tabela!.Tabela == "itens");
        Assert.Contains("(escrita no domínio pedido-id, na coluna loja.pedidos.id)", naFk.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.pedidos/colunas/id", naFk.Posicao!.Local);
    }

    [Fact]
    public async Task Preservar_na_declaracao_avisa_na_coluna_que_cita_de_onde_vem_a_politica()
    {
        var esquema = ComItens(chaves: []);
        var provedor = Provedor(esquema, maiores: _ => 1);
        var yaml = "tabelas:\n  loja.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido-id, entradaMalformada: preservar }\n  loja.itens:\n    colunas:\n      pedido: { dominio: pedido-id }\n";

        var avisos = (await PlanejarAsync(provedor, yaml)).Avisos.Where(aviso => aviso.Codigo == "INC3591").ToList();

        Assert.Equal(2, avisos.Count);
        var naCitacao = Assert.Single(avisos, aviso => aviso.Tabela!.Tabela == "itens");
        Assert.Contains("(escrita no domínio pedido-id, na coluna loja.pedidos.id)", naCitacao.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.pedidos/colunas/id", naCitacao.Posicao!.Local);
        Assert.Contains("(escrita na coluna)", Assert.Single(avisos, aviso => aviso.Tabela!.Tabela == "pedidos").Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PodeLer")]
    [InlineData("LeituraFiltradaPorLinha")]
    public async Task Fonte_sem_leitura_nao_chama_o_dialeto_e_so_sai_o_erro_da_leitura(string permissao)
    {
        var esquema = ComItens();
        var trocada = BancoFalso.Clonar(
            esquema,
            (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(esquema.Acesso!, (nameof(AcessoLido.Tabelas), esquema.Acesso!.Tabelas.Select(lida => lida.Tabela.Nome == "pedidos" ? BancoFalso.Clonar(lida, (permissao, permissao == "PodeLer" ? false : null)) : lida).ToArray()))));
        var provedor = new ProvedorDoPlanejar(trocada, esquema) { Maiores = _ => 1 };

        var erros = await RecusaAsync(provedor, Pedidos);

        Assert.Empty(provedor.MaioresPedidos);
        Assert.Equal([permissao == "PodeLer" ? "INC3063" : "INC3060"], erros.Select(erro => erro.Codigo));
    }

    [Fact]
    public async Task So_o_id_permutar_embutido_recebe_o_N_e_as_outras_colunas_publicam_nulo()
    {
        var fabrica = new FabricaQueGuarda();
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        var esquema = ComItens(chaves: []);
        var provedor = Provedor(esquema, maiores: _ => 1);

        var plano = await DialetoProtegidoTestes.PlanejarAsync(provedor, yaml: Yaml.Minimo + Pedidos + "  loja.itens:\n    colunas:\n      pedido: { transformador: exemplo.inteiro@1 }\n", registro: registro);

        Assert.True(fabrica.Criou);
        Assert.Null(fabrica.Tamanho);
        Assert.Null(Regra(plano, "itens", "pedido").TamanhoDominio);
        Assert.Null(plano.Execucao.Unidades.Single(unidade => unidade.Nome.Tabela == "itens").Tratadas.Single().Contexto.TamanhoDominio);
        Assert.Equal(["loja.pedidos.id"], provedor.MaioresPedidos.Select(Texto));
    }

    [Fact]
    public async Task O_maior_valor_lido_nunca_aparece_em_nenhuma_saida()
    {
        // Aprovado: o N e os contextos, nunca o maior. Recusado: a FK integer que não comporta N − 1 e a coluna fora do
        // domínio, com o maior das outras também sentinela.
        var aprovado = await PlanejarAsync(Provedor(ComItens(), maiores: _ => Sentinela), Pedidos);
        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() =>
            PlanejarAsync(Provedor(ComItens(pedido: Inteiro32("pedido"), chaves: []), maiores: _ => Sentinela), Pedidos + "  loja.itens:\n    colunas:\n      pedido: { dominio: pedido-id }\n"));
        var grande = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(Provedor(ComItens(), maiores: _ => long.MaxValue - Sentinela), Pedidos));

        var textos = aprovado.Avisos.Select(aviso => aviso.ToString())
            .Concat(aprovado.Execucao.Unidades.SelectMany(unidade => unidade.Tratadas).Select(tratada => tratada.Contexto.ToString()))
            .Concat(recusa.Erros.Select(erro => erro.ToString())).Append(recusa.ToString())
            .Concat(grande.Erros.Select(erro => erro.ToString())).Append(grande.ToString())
            .ToList();
        Assert.Contains(recusa.Erros, erro => erro.Codigo == "INC3090");
        foreach (var forma in Formas(Sentinela).Concat(Formas(long.MaxValue - Sentinela)))
        {
            Assert.All(textos, texto => Assert.DoesNotContain(forma, texto, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(10_000_000_000_000L, Regra(aprovado, "pedidos", "id").TamanhoDominio);
        Assert.All(aprovado.Execucao.Unidades.SelectMany(unidade => unidade.Tratadas), tratada => Assert.NotEqual(Sentinela, tratada.Contexto.TamanhoDominio));
    }

    // As formas do número que uma mensagem poderia trazer: decimal, com o milhar em pt-BR e no invariante, e hexadecimal.
    private static IEnumerable<string> Formas(long valor) =>
    [
        valor.ToString(CultureInfo.InvariantCulture),
        valor.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")),
        valor.ToString("N0", CultureInfo.InvariantCulture),
        valor.ToString("X", CultureInfo.InvariantCulture),
    ];

    private static ColunaEsquema Inteiro32(string nome, bool anulavel = false) =>
        BancoFalso.Clonar(Inteiro(nome, anulavel), (nameof(ColunaEsquema.TipoNativo), "integer"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = int.MinValue, Maximo = int.MaxValue }));

    private static ColunaEsquema Inteiro16(string nome) =>
        BancoFalso.Clonar(Inteiro(nome, anulavel: false), (nameof(ColunaEsquema.TipoNativo), "smallint"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = short.MinValue, Maximo = short.MaxValue }));

    // pedidos(id) ← itens(pedido), pela chave fk_itens, imposta e validada.
    private static EsquemaBanco ComItens(ColunaEsquema? pedido = null, ChaveEstrangeira[]? chaves = null) => Esquema(
        Tabela("pedidos", [Inteiro("id", anulavel: false)], chave: _id),
        Tabela("itens", [Inteiro("id", anulavel: false), pedido ?? Inteiro("pedido")], chave: _id, chaves: chaves ?? [Chave("fk_itens", ["pedido"], "pedidos", _id)]));

    private static ProvedorDoPlanejar Provedor(EsquemaBanco esquema, Func<ReferenciaColuna, long?>? maiores = null) => new(esquema, esquema) { Maiores = maiores };

    private static string Texto(ReferenciaColuna coluna) => $"{coluna.Tabela.Esquema}.{coluna.Tabela.Nome}.{coluna.Coluna}";

    private static RegraColuna Regra(Plano plano, string tabela, string coluna) =>
        plano.Tabelas.Single(mapa => mapa.Nome.Tabela == tabela).Colunas.Single(mapa => mapa.Nome == coluna).Regra!;

    private static EsquemaBanco Esquema(params TabelaEsquema[] tabelas) =>
        BancoFalso.Clonar(ComTabelas(tabelas), (nameof(EsquemaBanco.Sequences), Array.Empty<SequenceEsquema>()), (nameof(EsquemaBanco.Acesso), Acesso(tabelas, [], [])));

    private static TabelaEsquema Tabela(
        string nome, ColunaEsquema[] colunas, string[]? chave = null, ChaveEstrangeira[]? chaves = null, TipoTabela tipo = TipoTabela.Comum, string? pai = null) => new()
        {
            Nome = Nome(nome),
            Tipo = tipo,
            Pai = pai is null ? null : Nome(pai),
            Colunas = colunas,
            ChavePrimaria = chave ?? [],
            IndicesUnicos = chave is null ? [] : [new IndiceUnico { Nome = $"pk_{nome}", Primario = true, Elementos = [.. chave.Select(coluna => new ElementoIndice { Coluna = coluna })], NulosDistintos = true }],
            ChavesEstrangeiras = chaves ?? [],
        };

    private static ChaveEstrangeira Chave(string nome, string[] colunas, string referenciada, string[] referenciadas, bool? imposta = true, bool? validada = true) => new()
    {
        Nome = nome,
        Colunas = colunas,
        Referenciada = Nome(referenciada),
        ColunasReferenciadas = referenciadas,
        Imposta = imposta,
        Validada = validada,
    };

    private static Task<Plano> PlanejarAsync(ProvedorDoPlanejar provedor, string yaml) => DialetoProtegidoTestes.PlanejarAsync(provedor, yaml: Yaml.Minimo + yaml);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(string yaml, EsquemaBanco esquema) => await RecusaAsync(Provedor(esquema, _ => 1), yaml);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(ProvedorDoPlanejar provedor, string yaml) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(provedor, yaml)), exactMatch: true).Erros;

    // Uma fábrica de fora, de coluna inteira, que guarda o tamanho do domínio que recebeu.
    private sealed class FabricaQueGuarda : FabricaTransformador
    {
        internal bool Criou { get; private set; }

        internal long? Tamanho { get; private set; }

        public override string Nome => "exemplo.inteiro";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Inteiro };

        public override void Validar(RegraTransformacao regra) => ArgumentNullException.ThrowIfNull(regra);

        protected override Transformador CriarCore(ContextoCriacao contexto)
        {
            (Criou, Tamanho) = (true, contexto.TamanhoDominio);
            return new Copia();
        }

        private sealed class Copia : Transformador
        {
            public override string Nome => "exemplo.inteiro";

            public override int Versao => 1;

            public override Injetividade Injetividade => Injetividade.Garantida;

            public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);
        }
    }
}
