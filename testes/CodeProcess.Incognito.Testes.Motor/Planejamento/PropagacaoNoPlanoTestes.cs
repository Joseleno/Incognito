using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A propagação das regras pelas chaves estrangeiras e pela herança (INC3030 a INC3037, avisos INC3530 e INC3533): as
/// colunas ligadas formam um componente com uma regra só, e a coluna sem regra recebe a da escrita mais próxima.
/// </summary>
public sealed class PropagacaoNoPlanoTestes
{
    private static readonly string[] _cpf = ["cpf"];

    [Fact]
    public async Task Dominio_divergente_numa_FK_sai_com_codigo_2_antes_de_qualquer_copia()
    {
        // Transitiva em três tabelas: o caminho mínimo sai na mensagem, com os nomes das chaves.
        var esquema = Cadeia();
        var yaml = "dominios:\n  cpf-outro: { transformador: br.cpf@1 }\ntabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.itens:\n    colunas:\n      comprador: { dominio: cpf-outro }\n";

        var erro = Assert.Single(await RecusaAsync(yaml, esquema));

        Assert.Equal(("INC3030", "loja.itens", "comprador", "/tabelas/loja.itens/colunas/comprador"), (erro.Codigo, erro.Tabela!.ToString(), erro.Coluna, erro.Posicao!.Local));
        Assert.Contains("(domínio cpf@1 ≠ cpf-outro@1)", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("loja.clientes.cpf (chave fk_pedidos) loja.pedidos.cliente (chave fk_itens) loja.itens.comprador", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_coluna_sem_regra_recebe_a_da_escrita_mais_proxima_e_e_criada_com_o_contexto_dela()
    {
        var plano = await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Cadeia());

        var pedidos = Regra(plano, "pedidos", "cliente");
        var itens = Regra(plano, "itens", "comprador");
        Assert.Equal((PropagacaoRegra.ChaveEstrangeira, "loja.clientes.cpf", "/tabelas/loja.clientes/colunas/cpf"), (pedidos.Propagacao!.Value, pedidos.PropagadaDe!.ToString(), pedidos.Posicao!.Local));
        Assert.Equal(("br.cpf", new DominioPseudonimo("cpf", 1), "loja.clientes.cpf"), (itens.Transformador, itens.Dominio!.Value, itens.PropagadaDe!.ToString()));
        Assert.Null(Regra(plano, "clientes", "cpf").Propagacao);
        Assert.Equal(["cliente"], plano.Execucao.Unidades.Single(unidade => unidade.Nome.Tabela == "pedidos").Tratadas.Select(tratada => tratada.Coluna));
        Assert.True(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "itens").Colunas.Single(coluna => coluna.Nome == "comprador").Classificacao.Identificador);
    }

    [Fact]
    public async Task Chave_composta_e_autorreferencia_ligam_posicao_a_posicao()
    {
        var contratos = Tabela("contratos", [Texto("doc", 14), Texto("tipo", 14), Texto("indicado", 14)], chave: ["doc", "tipo"], chaves: [Chave("fk_indicado", ["indicado"], "contratos", ["doc"])]);
        var parcelas = Tabela("parcelas", [Inteiro("id", anulavel: false), Texto("doc", 14), Texto("tipo", 14)], chave: ["id"], chaves: [Chave("fk_contrato", ["doc", "tipo"], "contratos", ["doc", "tipo"])]);
        var esquema = Esquema(contratos, parcelas);

        var plano = await PlanejarAsync("tabelas:\n  loja.contratos:\n    colunas:\n      doc: { transformador: br.cpf@1 }\n", esquema);

        Assert.Equal("loja.contratos.doc", Regra(plano, "parcelas", "doc").PropagadaDe!.ToString());
        Assert.Equal("loja.contratos.doc", Regra(plano, "contratos", "indicado").PropagadaDe!.ToString());
        Assert.Null(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "parcelas").Colunas.Single(coluna => coluna.Nome == "tipo").Regra);
    }

    [Fact]
    public async Task A_chave_lida_so_no_destino_tambem_liga()
    {
        var origem = Cadeia(semChaves: true);
        var plano = await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", origem, Cadeia());

        Assert.Equal(PropagacaoRegra.ChaveEstrangeira, Regra(plano, "itens", "comprador").Propagacao);
        Assert.True(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "itens").Colunas.Single(coluna => coluna.Nome == "comprador").Classificacao.Identificador);
    }

    [Fact]
    public async Task A_chave_lida_so_no_destino_chega_a_coluna_da_origem_pelo_pareamento()
    {
        // O destino grafa a coluna de outro jeito, e o dialeto dele a pareia sem a caixa: a chave liga a coluna da origem.
        var destino = Esquema(
            Tabela("clientes", [Texto("cpf", 14)], chave: _cpf),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("Cliente", 14)], chave: ["id"], chaves: [Chave("fk_pedidos", ["Cliente"], "clientes", _cpf)]),
            Tabela("itens", [Inteiro("id", anulavel: false), Texto("comprador", 14)], chave: ["id"], chaves: [Chave("fk_itens", ["comprador"], "pedidos", ["Cliente"])]));
        var provedor = new ProvedorDoPlanejar(Cadeia(semChaves: true), destino)
        {
            Comparar = (escrito, _, doCatalogo, doDestino) => doDestino && string.Equals(escrito, doCatalogo, StringComparison.OrdinalIgnoreCase) ? CorrespondenciaNome.Corresponde : null,
        };

        var plano = await DialetoProtegidoTestes.PlanejarAsync(provedor, yaml: Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n");

        Assert.Equal(("loja.clientes.cpf", "loja.clientes.cpf"), (Regra(plano, "pedidos", "cliente").PropagadaDe!.ToString(), Regra(plano, "itens", "comprador").PropagadaDe!.ToString()));
        Assert.Equal(["cliente"], plano.Execucao.Unidades.Single(unidade => unidade.Nome.Tabela == "pedidos").Tratadas.Select(tratada => tratada.Coluna));
    }

    [Fact]
    public async Task No_empate_a_coluna_recebe_a_regra_da_primeira_escrita_em_ordem_de_tabela_e_coluna()
    {
        // pedidos.cliente fica a uma ligação de clientes.cpf e de itens.comprador, as duas com a mesma regra escrita.
        var plano = await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.itens:\n    colunas:\n      comprador: { transformador: br.cpf@1 }\n", Cadeia());

        var pedidos = Regra(plano, "pedidos", "cliente");
        Assert.Equal(("loja.clientes.cpf", "/tabelas/loja.clientes/colunas/cpf"), (pedidos.PropagadaDe!.ToString(), pedidos.Posicao!.Local));
    }

    [Fact]
    public async Task No_empate_da_busca_a_ligacao_vem_da_coluna_alcancada_primeiro_em_ordem()
    {
        // contas.doc liga a mae.doc e a titular.doc; a herdeira chega pelas duas a duas ligações, pela herança (de mae)
        // e pela chave (de titular): vale a de mae, a primeira em ordem de tabela.
        var esquema = Esquema(
            Tabela("contas", [Texto("doc", 14)], chaves: [Chave("fk_mae", ["doc"], "mae", ["doc"]), Chave("fk_titular", ["doc"], "titular", ["doc"])]),
            Tabela("herdeira", [Texto("doc", 14)], chaves: [Chave("fk_herdeira", ["doc"], "titular", ["doc"])], tipo: TipoTabela.Herdeira, pai: "mae"),
            Tabela("mae", [Texto("doc", 14)]),
            Tabela("titular", [Texto("doc", 14)]));

        var plano = await PlanejarAsync("tabelas:\n  loja.contas:\n    colunas:\n      doc: { transformador: br.cpf@1 }\n", esquema);

        var herdeira = Regra(plano, "herdeira", "doc");
        Assert.Equal((PropagacaoRegra.Heranca, "loja.contas.doc"), (herdeira.Propagacao!.Value, herdeira.PropagadaDe!.ToString()));
    }

    [Fact]
    public async Task No_empate_o_caminho_do_INC3030_passa_pela_primeira_coluna_em_ordem()
    {
        // Dois caminhos de duas ligações entre clientes.cpf e itens.comprador, por pedidos_a e por pedidos_b.
        var esquema = Esquema(
            Tabela("clientes", [Texto("cpf", 14)], chave: _cpf),
            Tabela("pedidos_a", [Inteiro("id", anulavel: false), Texto("cliente", 14)], chave: ["id"], chaves: [Chave("fk_a", ["cliente"], "clientes", _cpf)]),
            Tabela("pedidos_b", [Inteiro("id", anulavel: false), Texto("cliente", 14)], chave: ["id"], chaves: [Chave("fk_b", ["cliente"], "clientes", _cpf)]),
            Tabela("itens", [Inteiro("id", anulavel: false), Texto("comprador", 14)], chave: ["id"], chaves: [Chave("fk_itens_a", ["comprador"], "pedidos_a", ["cliente"]), Chave("fk_itens_b", ["comprador"], "pedidos_b", ["cliente"])]));
        var yaml = "dominios:\n  cpf-outro: { transformador: br.cpf@1 }\ntabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.itens:\n    colunas:\n      comprador: { dominio: cpf-outro }\n";

        var erro = Assert.Single(await RecusaAsync(yaml, esquema));

        Assert.Equal(("INC3030", "itens", "comprador"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.Contains("loja.clientes.cpf (chave fk_a) loja.pedidos_a.cliente (chave fk_itens_a) loja.itens.comprador", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ transformador: br.chave-dfe@1, dominioCnpj: cnpj-a, modelos: [55] }", "{ transformador: br.chave-dfe@1, dominioCnpj: cnpj-b }", "modelos: 55 ≠ ")]
    [InlineData("{ transformador: br.chave-pix@1, dominioCpf: cpf-a }", "{ dominio: pix-outro }", "domínio chave-pix@1 ≠ pix-outro@1")]
    public async Task Citados_diferentes_com_outro_parametro_ou_outro_dominio_dao_INC3030(string conta, string pagamento, string diferenca)
    {
        var esquema = Esquema(
            Tabela("contas", [Texto("chave", 100)], chave: ["chave"]),
            Tabela("pagamentos", [Inteiro("id", anulavel: false), Texto("chave", 100)], chave: ["id"], chaves: [Chave("fk_pagamentos", ["chave"], "contas", ["chave"])]));
        var yaml = $"dominios:\n  cpf-a: {{ transformador: br.cpf@1 }}\n  cpf-b: {{ transformador: br.cpf@1 }}\n  cnpj-a: {{ transformador: br.cnpj@1 }}\n  cnpj-b: {{ transformador: br.cnpj@1 }}\n  pix-outro: {{ transformador: br.chave-pix@1, dominioCpf: cpf-b }}\ntabelas:\n  loja.contas:\n    colunas:\n      chave: {conta}\n  loja.pagamentos:\n    colunas:\n      chave: {pagamento}\n";

        var erro = Assert.Single(await RecusaAsync(yaml, esquema));

        Assert.Equal(("INC3030", "pagamentos"), (erro.Codigo, erro.Tabela!.Tabela));
        Assert.Contains(diferenca, erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gerada_calculada_da_tratada_so_pelo_destino_tambem_da_INC3037()
    {
        // Na origem, a gerada é calculada do id; no destino, da tratada: o destino a recalcula do pseudônimo.
        var gerada = (nameof(ColunaEsquema.Geracao), (object?)GeracaoColuna.Armazenada);
        EsquemaBanco Ligadas(string de) => Esquema(
            Tabela("clientes", [Texto("cpf_norm", 14)], chave: ["cpf_norm"]),
            Tabela(
                "pedidos",
                [Inteiro("id", anulavel: false), Texto("cpf", 14), BancoFalso.Clonar(Texto("cpf_norm", 14), gerada, (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = [de], Incompletas = false }))],
                chave: ["id"],
                chaves: [Chave("fk_pedidos", ["cpf_norm"], "clientes", ["cpf_norm"])]));

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.pedidos:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Ligadas("id"), Ligadas("cpf")));

        Assert.Equal(("INC3037", "pedidos", "cpf_norm"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
    }

    [Fact]
    public async Task A_coluna_gerada_so_no_destino_nao_recebe_a_regra_e_da_INC3037()
    {
        var destino = TrocarColuna(Cadeia(), "pedidos", "cliente", (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada));

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Cadeia(), destino));

        Assert.Equal(("INC3037", "pedidos", "cliente"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
    }

    [Fact]
    public async Task Politicas_que_produzem_NULL_diferentes_dao_INC3030_e_as_que_o_transformador_ignora_nao()
    {
        var esquema = Cadeia();
        var invalida = Assert.Single(await RecusaAsync(Duas("{ transformador: br.cpf@1 }", "{ transformador: br.cpf@1, entradaInvalida: anular }"), esquema));
        var malformada = Assert.Single(await RecusaAsync(Duas("{ transformador: br.cpf@1, entradaMalformada: falhar }", "{ transformador: br.cpf@1, entradaMalformada: anular }"), esquema));

        // Falhar e preservar não produzem NULL: a mesma assinatura. O e-mail ignora a entradaInvalida.
        await PlanejarAsync(Duas("{ transformador: br.cpf@1, entradaInvalida: falhar }", "{ transformador: br.cpf@1, entradaInvalida: preservar }"), esquema);
        var deEmail = Esquema(
            Tabela("clientes", [Texto("cpf", 100)], chave: _cpf),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cliente", 100)], chave: ["id"], chaves: [Chave("fk_pedidos", ["cliente"], "clientes", _cpf)]));
        await PlanejarAsync(Duas("{ transformador: email@1 }", "{ transformador: email@1, entradaInvalida: anular }"), deEmail);

        Assert.Equal(("INC3030", "INC3030"), (invalida.Codigo, malformada.Codigo));
        Assert.Equal(("pedidos", "cliente"), (invalida.Tabela!.Tabela, invalida.Coluna));
        Assert.Contains("(produz NULL com nada ≠ com entradaInvalida: anular)", invalida.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chave_pix_com_dominio_citado_diferente_na_FK_sai_com_codigo_2()
    {
        var esquema = Esquema(
            Tabela("contas", [Texto("pix", 100)], chave: ["pix"]),
            Tabela("pagamentos", [Inteiro("id", anulavel: false), Texto("pix", 100)], chave: ["id"], chaves: [Chave("fk_pagamentos", ["pix"], "contas", ["pix"])]));
        const string Dominios = "dominios:\n  cpf-a: { transformador: br.cpf@1 }\n  cpf-b: { transformador: br.cpf@1 }\n  cnpj-a: { transformador: br.cnpj@1 }\n";
        string Pix(string conta, string pagamento) =>
            $"{Dominios}tabelas:\n  loja.contas:\n    colunas:\n      pix: {conta}\n  loja.pagamentos:\n    colunas:\n      pix: {pagamento}\n";

        var pix = Assert.Single(await RecusaAsync(Pix("{ transformador: br.chave-pix@1, dominioCpf: cpf-a }", "{ transformador: br.chave-pix@1, dominioCpf: cpf-b }"), esquema));
        var dfe = Assert.Single(await RecusaAsync(Pix("{ transformador: br.chave-dfe@1, dominioCnpj: cnpj-a }", "{ transformador: br.chave-dfe@1 }"), esquema));

        // O citado escrito igual ao padrão é o mesmo componente que o omitido; o pagamento sem regra recebe a da conta.
        await PlanejarAsync(Pix("{ transformador: br.chave-pix@1, dominioCpf: cpf }", "{ transformador: br.chave-pix@1 }"), esquema);
        var propagada = await PlanejarAsync($"{Dominios}tabelas:\n  loja.contas:\n    colunas:\n      pix: {{ transformador: br.chave-pix@1, dominioCpf: cpf-a }}\n", esquema);

        Assert.Equal(("INC3031", "pagamentos"), (pix.Codigo, pix.Tabela!.Tabela));
        Assert.Contains("(dominioCpf: cpf-a ≠ cpf-b)", pix.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC3031", dfe.Codigo);
        Assert.Contains("dominioCnpj: cnpj-a ≠ cnpj", dfe.Mensagem, StringComparison.Ordinal);
        Assert.Equal("loja.contas.pix", Regra(propagada, "pagamentos", "pix").PropagadaDe!.ToString());
    }

    [Fact]
    public async Task Coluna_mantida_ligada_a_uma_transformada_da_INC3032()
    {
        var erro = Assert.Single(await RecusaAsync(Duas("{ transformador: br.cpf@1 }", "{ acao: manter }"), Cadeia()));

        Assert.Equal(("INC3032", "pedidos", "cliente", "/tabelas/loja.pedidos/colunas/cliente"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna, erro.Posicao!.Local));
    }

    [Fact]
    public async Task DerivarDe_ligado_por_chave_da_INC3033_e_coluna_de_derivarDe_sem_forma_canonica_da_INC3034()
    {
        var comCep = Esquema(
            Tabela("clientes", [Texto("cep", 10), Texto("cidade", 50)]),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cep", 10)], chave: ["id"], chaves: [Chave("fk_pedidos", ["cep"], "clientes", ["cep"])]));
        var semForma = Esquema(Tabela("clientes", [Texto("cep", 10), BancoFalso.Clonar(Texto("cidade", 1), (nameof(ColunaEsquema.Tipo), null), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva))]));
        const string Yaml = "tabelas:\n  loja.clientes:\n    colunas:\n      cep: { transformador: br.cep@1, derivarDe: cidade }\n";

        // O componente recusado sai dos passos seguintes: o br.cep, que também não é injetivo na chave, não soma o 3050.
        var naChave = Assert.Single(await RecusaAsync(Yaml, comCep));
        var naColuna = Assert.Single(await RecusaAsync(Yaml, semForma));

        Assert.Equal(("INC3033", "clientes", "cep"), (naChave.Codigo, naChave.Tabela!.Tabela, naChave.Coluna));
        Assert.Equal(("INC3034", "cep"), (naColuna.Codigo, naColuna.Coluna));
        Assert.Contains("A coluna cidade de 'derivarDe' não tem forma canônica", naColuna.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gerada_ligada_por_chave_a_uma_tratada_da_INC3037_dos_dois_lados_da_chave()
    {
        var gerada = (nameof(ColunaEsquema.Geracao), (object?)GeracaoColuna.Armazenada);
        var daGerada = Esquema(
            Tabela("clientes", [Texto("cpf", 14)], chave: _cpf),
            Tabela("pedidos", [Inteiro("id", anulavel: false), BancoFalso.Clonar(Texto("cliente", 14), gerada)], chave: ["id"], chaves: [Chave("fk_pedidos", ["cliente"], "clientes", _cpf)]));
        var paraGerada = Esquema(
            Tabela("clientes", [Texto("cpf", 14), BancoFalso.Clonar(Texto("calculado", 14), gerada)], chave: _cpf),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cliente", 14)], chave: ["id"], chaves: [Chave("fk_pedidos", ["cliente"], "clientes", ["calculado"])]));

        var partindo = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", daGerada));
        var apontando = Assert.Single(await RecusaAsync("tabelas:\n  loja.pedidos:\n    colunas:\n      cliente: { transformador: br.cpf@1 }\n", paraGerada));

        Assert.Equal(("INC3037", "pedidos", "cliente"), (partindo.Codigo, partindo.Tabela!.Tabela, partindo.Coluna));
        Assert.Equal(("INC3037", "clientes", "calculado"), (apontando.Codigo, apontando.Tabela!.Tabela, apontando.Coluna));
    }

    [Fact]
    public async Task A_propagada_passa_pela_criacao_e_pelo_tipo_com_a_marca_de_onde_veio()
    {
        var curta = Trocar(Cadeia(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Texto("cliente", 10) })));
        var outroTipo = Trocar(Cadeia(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Inteiro("cliente") })));
        const string Yaml = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";

        var criacao = Assert.Single(await RecusaAsync(Yaml, Cadeia(), curta));
        var tipo = Assert.Single(await RecusaAsync(Yaml, Cadeia(), outroTipo));

        Assert.Equal(("INC3045", "pedidos", "cliente", "/tabelas/loja.clientes/colunas/cpf"), (criacao.Codigo, criacao.Tabela!.Tabela, criacao.Coluna, criacao.Posicao!.Local));
        Assert.EndsWith("Regra propagada de loja.clientes.cpf.", criacao.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3023", "cliente"), (tipo.Codigo, tipo.Coluna));
        Assert.EndsWith("Regra propagada de loja.clientes.cpf.", tipo.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_heranca_propaga_entre_copiadas_e_atravessa_a_ignorada()
    {
        // pessoa (copiar) → funcionario (ignorar) → gerente (copiar): a neta se liga à avó, e a ignorada avisa.
        var pessoa = Tabela("pessoa", [Texto("cpf", 14), Texto("nome", 50)]);
        var funcionario = Tabela("funcionario", [Texto("cpf", 14), Texto("nome", 50), Inteiro("salario")], tipo: TipoTabela.Herdeira, pai: "pessoa");
        var gerente = Tabela("gerente", [Texto("cpf", 14), Texto("nome", 50), Inteiro("salario"), Inteiro("nivel")], tipo: TipoTabela.Herdeira, pai: "funcionario");
        var esquema = Esquema(pessoa, funcionario, gerente);

        var plano = await PlanejarAsync("tabelas:\n  loja.pessoa:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.funcionario:\n    acao: ignorar\n", esquema);

        var doGerente = Regra(plano, "gerente", "cpf");
        Assert.Equal((PropagacaoRegra.Heranca, "loja.pessoa.cpf"), (doGerente.Propagacao!.Value, doGerente.PropagadaDe!.ToString()));
        Assert.Null(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "funcionario").Colunas.Single(coluna => coluna.Nome == "cpf").Regra);
        var aviso = Assert.Single(plano.Avisos, aviso => aviso.Codigo == "INC3533");
        Assert.Equal("funcionario", aviso.Tabela!.Tabela);

        // A herdeira com regra divergente é recusada como a filha de uma chave.
        var divergente = Assert.Single(await RecusaAsync(
            "dominios:\n  cpf-outro: { transformador: br.cpf@1 }\ntabelas:\n  loja.pessoa:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.funcionario:\n    acao: ignorar\n  loja.gerente:\n    colunas:\n      cpf: { dominio: cpf-outro }\n",
            esquema));
        Assert.Equal("INC3030", divergente.Codigo);
        Assert.Contains("(herança)", divergente.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chave_da_origem_nao_imposta_ou_nao_validada_entre_copiadas_da_o_aviso_INC3530()
    {
        var naoValidada = Trocar(Cadeia(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { Chave("fk_pedidos", ["cliente"], "clientes", _cpf, validada: false) })));
        var semResposta = Trocar(Cadeia(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { Chave("fk_pedidos", ["cliente"], "clientes", _cpf, imposta: null) })));

        var plano = await PlanejarAsync("", naoValidada);
        var nula = await PlanejarAsync("", semResposta);
        var esvaziada = await PlanejarAsync("tabelas:\n  loja.pedidos:\n    acao: esvaziar\n  loja.itens:\n    acao: esvaziar\n", naoValidada);
        var validada = await PlanejarAsync("", Cadeia());

        var aviso = Assert.Single(plano.Avisos);
        Assert.Equal(("INC3530", "pedidos", "fk_pedidos"), (aviso.Codigo, aviso.Tabela!.Tabela, aviso.Objeto));
        Assert.Equal("INC3530", Assert.Single(nula.Avisos).Codigo);
        Assert.DoesNotContain(esvaziada.Avisos, aviso => aviso.Codigo == "INC3530");
        Assert.Empty(validada.Avisos);
    }

    [Fact]
    public async Task Gerada_calculada_de_tratada_e_ligada_por_chave_a_coluna_sem_regra_da_INC3037()
    {
        // A FK que aponta para uma gerada STORED UNIQUE calculada da tratada, e a FK que parte de uma gerada calculada da
        // tratada: nos dois, a outra ponta da chave não tem regra e levaria o valor real.
        var gerada = (nameof(ColunaEsquema.Geracao), (object?)GeracaoColuna.Armazenada);
        ColunaEsquema Calculada(string nome, string de) =>
            BancoFalso.Clonar(Texto(nome, 14), gerada, (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = [de], Incompletas = false }));
        var apontando = Esquema(
            Tabela("clientes", [Texto("cpf", 14), Calculada("cpf_limpo", "cpf")], chave: _cpf),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cliente", 14)], chave: ["id"], chaves: [Chave("fk_pedidos", ["cliente"], "clientes", ["cpf_limpo"])]));
        var partindo = Esquema(
            Tabela("clientes", [Texto("cpf_norm", 14)], chave: ["cpf_norm"]),
            Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cpf", 14), Calculada("cpf_norm", "cpf")], chave: ["id"], chaves: [Chave("fk_pedidos", ["cpf_norm"], "clientes", ["cpf_norm"])]));

        var naReferenciada = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", apontando));
        var naQueReferencia = Assert.Single(await RecusaAsync("tabelas:\n  loja.pedidos:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", partindo));

        Assert.Equal(("INC3037", "clientes", "cpf_limpo"), (naReferenciada.Codigo, naReferenciada.Tabela!.Tabela, naReferenciada.Coluna));
        Assert.Equal(("INC3037", "pedidos", "cpf_norm"), (naQueReferencia.Codigo, naQueReferencia.Tabela!.Tabela, naQueReferencia.Coluna));
        Assert.Contains("é calculada de uma coluna tratada", naReferenciada.Mensagem, StringComparison.Ordinal);

        // Sem regra na base, a mesma ligação passa.
        await PlanejarAsync("", apontando);
    }

    [Fact]
    public async Task A_chave_composta_liga_cada_posicao_e_recusa_a_divergente_em_qualquer_uma()
    {
        var contratos = Tabela("contratos", [Texto("doc", 14), Texto("tipo", 14)], chave: ["doc", "tipo"]);
        var parcelas = Tabela("parcelas", [Inteiro("id", anulavel: false), Texto("doc", 14), Texto("tipo", 14)], chave: ["id"], chaves: [Chave("fk_contrato", ["doc", "tipo"], "contratos", ["doc", "tipo"])]);
        var esquema = Esquema(contratos, parcelas);

        var plano = await PlanejarAsync("tabelas:\n  loja.contratos:\n    colunas:\n      tipo: { transformador: br.cpf@1 }\n", esquema);
        var erro = Assert.Single(await RecusaAsync(
            "dominios:\n  cpf-outro: { transformador: br.cpf@1 }\ntabelas:\n  loja.contratos:\n    colunas:\n      tipo: { transformador: br.cpf@1 }\n  loja.parcelas:\n    colunas:\n      tipo: { dominio: cpf-outro }\n",
            esquema));

        Assert.Equal("loja.contratos.tipo", Regra(plano, "parcelas", "tipo").PropagadaDe!.ToString());
        Assert.Null(plano.Tabelas.Single(tabela => tabela.Nome.Tabela == "parcelas").Colunas.Single(coluna => coluna.Nome == "doc").Regra);
        Assert.Equal(("INC3030", "parcelas", "tipo"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.Contains("loja.contratos.tipo (chave fk_contrato) loja.parcelas.tipo", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chave_lida_so_na_origem_tambem_liga()
    {
        var plano = await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Cadeia(), Cadeia(semChaves: true));

        Assert.Equal(PropagacaoRegra.ChaveEstrangeira, Regra(plano, "itens", "comprador").Propagacao);
    }

    [Fact]
    public async Task A_chave_da_coluna_para_ela_mesma_nao_liga()
    {
        var esquema = Esquema(Tabela("clientes", [Texto("cep", 10), Texto("cidade", 50)], chaves: [Chave("fk_propria", ["cep"], "clientes", ["cep"])]));

        // Sem ligação, o derivarDe não cai no 3033 e o br.cep não cai no 3050 de chave: o plano aprova.
        var plano = await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cep: { transformador: br.cep@1, derivarDe: cidade }\n", esquema);

        Assert.Equal("br.cep", Regra(plano, "clientes", "cep").Transformador);
    }

    [Fact]
    public async Task DerivarDe_que_sobe_pela_heranca_sem_a_coluna_referida_da_INC3016()
    {
        // A regra escrita na herdeira chega à mãe, que não tem a coluna de derivarDe.
        var pessoa = Tabela("pessoa", [Texto("cep", 10)]);
        var funcionario = Tabela("funcionario", [Texto("cep", 10), Texto("cidade", 50)], tipo: TipoTabela.Herdeira, pai: "pessoa");

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.funcionario:\n    colunas:\n      cep: { transformador: br.cep@1, derivarDe: cidade }\n", Esquema(pessoa, funcionario)));

        Assert.Equal(("INC3016", "pessoa", "cep"), (erro.Codigo, erro.Tabela!.Tabela, erro.Coluna));
        Assert.EndsWith("Regra propagada de loja.funcionario.cep.", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task So_a_chave_estrangeira_marca_identificador_a_heranca_nao()
    {
        var pessoa = Tabela("pessoa", [Texto("cpf", 14), Texto("nome", 50)]);
        var funcionario = Tabela("funcionario", [Texto("cpf", 14), Texto("nome", 50)], tipo: TipoTabela.Herdeira, pai: "pessoa");

        var plano = await PlanejarAsync("tabelas:\n  loja.pessoa:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Esquema(pessoa, funcionario));

        Assert.All(plano.Tabelas.SelectMany(tabela => tabela.Colunas), coluna => Assert.False(coluna.Classificacao.Identificador));
        Assert.Equal(PropagacaoRegra.Heranca, Regra(plano, "funcionario", "cpf").Propagacao);
    }

    [Fact]
    public async Task A_heranca_multipla_liga_pelos_outros_pais_e_a_gerada_da_herdeira_nao_recebe()
    {
        var pessoa = Tabela("pessoa", [Texto("cpf", 14)]);
        var contato = Tabela("contato", [Texto("email", 100)]);
        var gerada = BancoFalso.Clonar(Texto("cpf", 14), (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada));
        var cliente = BancoFalso.Clonar(
            Tabela("cliente", [Texto("cpf", 14), Texto("email", 100)], tipo: TipoTabela.Herdeira, pai: "pessoa"),
            (nameof(TabelaEsquema.OutrosPais), new[] { Nome("contato") }));
        var comGerada = BancoFalso.Clonar(cliente, (nameof(TabelaEsquema.Colunas), new[] { gerada, Texto("email", 100) }));

        var plano = await PlanejarAsync("tabelas:\n  loja.contato:\n    colunas:\n      email: { transformador: email@1 }\n", Esquema(pessoa, contato, cliente));
        var semGerada = await PlanejarAsync("tabelas:\n  loja.pessoa:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", Esquema(pessoa, contato, comGerada));

        Assert.Equal((PropagacaoRegra.Heranca, "loja.contato.email"), (Regra(plano, "cliente", "email").Propagacao!.Value, Regra(plano, "cliente", "email").PropagadaDe!.ToString()));
        Assert.Null(semGerada.Tabelas.Single(tabela => tabela.Nome.Tabela == "cliente").Colunas.Single(coluna => coluna.Nome == "cpf").Regra);
    }

    [Fact]
    public async Task A_herdeira_esvaziada_ou_a_ignorada_de_mae_sem_tratada_nao_avisam()
    {
        var pessoa = Tabela("pessoa", [Texto("cpf", 14), Texto("nome", 50)]);
        var funcionario = Tabela("funcionario", [Texto("cpf", 14), Texto("nome", 50)], tipo: TipoTabela.Herdeira, pai: "pessoa");
        var esquema = Esquema(pessoa, funcionario);

        var esvaziada = await PlanejarAsync("tabelas:\n  loja.pessoa:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n  loja.funcionario:\n    acao: esvaziar\n", esquema);
        var semTratada = await PlanejarAsync("tabelas:\n  loja.funcionario:\n    acao: ignorar\n", esquema);

        Assert.DoesNotContain(esvaziada.Avisos, aviso => aviso.Codigo == "INC3533");
        Assert.DoesNotContain(semTratada.Avisos, aviso => aviso.Codigo == "INC3533");
    }

    [Fact]
    public async Task Citados_diferentes_com_outra_diferenca_dao_INC3030_e_nao_INC3031()
    {
        var esquema = Esquema(
            Tabela("contas", [Texto("pix", 100)], chave: ["pix"]),
            Tabela("pagamentos", [Inteiro("id", anulavel: false), Texto("pix", 100)], chave: ["id"], chaves: [Chave("fk_pagamentos", ["pix"], "contas", ["pix"])]));
        const string Yaml = "dominios:\n  cpf-a: { transformador: br.cpf@1 }\n  cpf-b: { transformador: br.cpf@1 }\ntabelas:\n  loja.contas:\n    colunas:\n      pix: { transformador: br.chave-pix@1, dominioCpf: cpf-a, entradaMalformada: anular }\n  loja.pagamentos:\n    colunas:\n      pix: { transformador: br.chave-pix@1, dominioCpf: cpf-b }\n";

        var erro = Assert.Single(await RecusaAsync(Yaml, esquema));

        Assert.Equal("INC3030", erro.Codigo);
        Assert.Contains("dominioCpf: cpf-a ≠ cpf-b", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("produz NULL com entradaMalformada: anular ≠ com nada", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_nulos_do_3530_e_do_3034_contam_como_o_pior_caso()
    {
        var semValidacao = Trocar(Cadeia(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { Chave("fk_pedidos", ["cliente"], "clientes", _cpf, validada: null) })));
        var semTipo = Esquema(Tabela("clientes", [Texto("cep", 10), BancoFalso.Clonar(Texto("cidade", 1), (nameof(ColunaEsquema.Tipo), null))]));

        var plano = await PlanejarAsync("", semValidacao);
        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cep: { transformador: br.cep@1, derivarDe: cidade }\n", semTipo));

        Assert.Equal("INC3530", Assert.Single(plano.Avisos).Codigo);
        Assert.Equal("INC3034", erro.Codigo);
    }

    private static RegraColuna Regra(Plano plano, string tabela, string coluna) =>
        plano.Tabelas.Single(mapa => mapa.Nome.Tabela == tabela).Colunas.Single(mapa => mapa.Nome == coluna).Regra!;

    private static string Duas(string clientes, string pedidos) =>
        $"tabelas:\n  loja.clientes:\n    colunas:\n      cpf: {clientes}\n  loja.pedidos:\n    colunas:\n      cliente: {pedidos}\n";

    // clientes(cpf) ← pedidos(cliente) ← itens(comprador), cada chave pela coluna de documento.
    private static EsquemaBanco Cadeia(bool semChaves = false) => Esquema(
        Tabela("clientes", [Texto("cpf", 14)], chave: _cpf),
        Tabela("pedidos", [Inteiro("id", anulavel: false), Texto("cliente", 14)], chave: ["id"], chaves: semChaves ? [] : [Chave("fk_pedidos", ["cliente"], "clientes", _cpf)]),
        Tabela("itens", [Inteiro("id", anulavel: false), Texto("comprador", 14)], chave: ["id"], chaves: semChaves ? [] : [Chave("fk_itens", ["comprador"], "pedidos", ["cliente"])]));

    // As tabelas dadas, sem sequences: o esquema sintético liga uma sequence ao clientes.id, que estas tabelas não têm.
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
            IndicesUnicos = chave is null ? [] : [new IndiceUnico { Nome = $"pk_{nome}", Primario = true, Elementos = [.. chave.Select(coluna => new ElementoIndice { Coluna = coluna, Colacao = colunas.First(lida => lida.Nome == coluna).Colacao })], NulosDistintos = true }],
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
        DesligadaSemRestauracao = false,
    };

    private static Task<Plano> PlanejarAsync(string yaml, EsquemaBanco origem, EsquemaBanco? destino = null) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino ?? origem), yaml: Yaml.Minimo + yaml);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(string yaml, EsquemaBanco origem, EsquemaBanco? destino = null) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(yaml, origem, destino)), exactMatch: true).Erros;
}
