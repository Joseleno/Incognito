using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O que as ações, o login, os objetos fora das tabelas e o banco permitem (P9): a fronteira entre as ações pelas chaves do
/// destino (INC3035, INC3036, aviso INC3531) e o conjunto que o diário remove; a segurança por linha e as permissões
/// (INC3060 a INC3067, aviso INC3561); os residuais (INC3070, avisos INC3570 e INC3571), a publicação (INC3074) e a
/// assinatura (INC3076); a codificação, a versão e o transporte (INC3080 a INC3082, avisos INC3560, INC3580 e INC3581).
/// Toda permissão em nulo recusa como em <c>false</c>.
/// </summary>
public sealed class AcessoNoPlanoTestes
{
    private const string CpfTratado = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";
    private const string Esvaziadas = "tabelas:\n  loja.clientes: { acao: esvaziar }\n  loja.pedidos: { acao: esvaziar }\n";

    [Fact]
    public async Task Chave_de_tabela_copiada_para_esvaziada_da_INC3035()
    {
        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes: { acao: esvaziar }\n", Coerente(), Coerente()));

        Assert.Equal(("INC3035", "loja.pedidos", "fk_pedidos_clientes"), (erro.Codigo, erro.Tabela!.ToString(), erro.Objeto));
        Assert.Contains("copiada, a loja.clientes, esvaziada", erro.Mensagem, StringComparison.Ordinal);

        // Esvaziar as duas, ou só o filho, não deixa órfão.
        Assert.Empty((await PlanejarAsync(Esvaziadas, Coerente(), Coerente())).Avisos);
        Assert.Empty((await PlanejarAsync("tabelas:\n  loja.pedidos: { acao: esvaziar }\n", Coerente(), Coerente())).Avisos);
    }

    [Fact]
    public async Task Chave_entre_copiada_e_ignorada_com_coluna_tratada_da_INC3036_e_sem_ela_avisa_INC3531()
    {
        var esquema = ComChaveDoCpf();

        // O filho copiado com a coluna da chave tratada, e o pai copiado com a coluna referida tratada.
        var errosDoFilho = await RecusaAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos:\n    colunas:\n      cliente_cpf: { transformador: br.cpf@1 }\n", esquema, esquema);
        var errosDoPai = await RecusaAsync(CpfTratado + "  loja.pedidos: { acao: ignorar }\n", esquema, esquema);
        var semTratada = await PlanejarAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n", esquema, esquema);
        var filhoIgnorado = await PlanejarAsync("tabelas:\n  loja.pedidos: { acao: ignorar }\n", esquema, esquema);
        var doPaiEsvaziado = await PlanejarAsync("tabelas:\n  loja.clientes: { acao: esvaziar }\n  loja.pedidos: { acao: ignorar }\n", esquema, esquema);
        var filhoEsvaziado = await PlanejarAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: esvaziar }\n", esquema, esquema);

        var doFilho = Assert.Single(errosDoFilho);
        var doPai = Assert.Single(errosDoPai);
        Assert.Equal(("INC3036", "INC3036"), (doFilho.Codigo, doPai.Codigo));
        Assert.Equal(("loja.pedidos", "fk_cpf"), (doFilho.Tabela!.ToString(), doFilho.Objeto));
        Assert.Equal([("INC3531", "fk_cpf"), ("INC3531", "fk_pedidos_clientes")], filhoIgnorado.Avisos.Select(aviso => (aviso.Codigo, aviso.Objeto!)).Order());
        Assert.All(filhoIgnorado.Avisos, aviso => Assert.Contains("ignorada, a loja.clientes, copiada: esvaziar loja.clientes exige remover a chave", aviso.Mensagem, StringComparison.Ordinal));
        Assert.Contains("liga coluna tratada desta tabela, copiada, a loja.clientes, ignorada", doFilho.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("loja.pedidos", "fk_cpf"), (doPai.Tabela!.ToString(), doPai.Objeto));
        Assert.Contains("ignorada, a coluna tratada de loja.clientes, copiada", doPai.Mensagem, StringComparison.Ordinal);
        Assert.Equal([("INC3531", "fk_cpf"), ("INC3531", "fk_pedidos_clientes")], semTratada.Avisos.Select(aviso => (aviso.Codigo, aviso.Objeto!)).Order());
        Assert.All(doPaiEsvaziado.Avisos, aviso => Assert.Contains("esvaziada: esvaziar loja.clientes exige remover a chave", aviso.Mensagem, StringComparison.Ordinal));
        Assert.Equal(2, doPaiEsvaziado.Avisos.Count);
        Assert.Empty(filhoEsvaziado.Avisos);
    }

    [Fact]
    public async Task A_gerada_calculada_da_tratada_na_chave_para_a_ignorada_tambem_da_INC3036()
    {
        var gerada = BancoFalso.Clonar(
            Texto("cpf_limpo", 11),
            (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
            (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["cliente_cpf"], Incompletas = false }));
        var esquema = Trocar(ComChaveDoCpf(), "pedidos", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(gerada).ToArray()),
            (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { new ChaveEstrangeira { Nome = "fk_limpo", Colunas = ["cpf_limpo"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["cpf"], Imposta = true, Validada = true, DesligadaSemRestauracao = false } })));

        var erros = await RecusaAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos:\n    colunas:\n      cliente_cpf: { transformador: br.cpf@1 }\n", esquema, esquema);

        Assert.Equal(("INC3036", "fk_limpo"), (Assert.Single(erros).Codigo, erros[0].Objeto));
    }

    [Fact]
    public async Task O_conjunto_que_o_diario_remove_vai_na_carga()
    {
        // O filho copiado, ou o pai copiado ou esvaziado; a do filho esvaziado para o pai ignorado fica.
        var esquema = ComChaveDoCpf();

        var copiadas = await PlanejarAsync("", esquema, esquema);
        var paiIgnorado = await PlanejarAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: esvaziar }\n", esquema, esquema);
        var filhoIgnorado = await PlanejarAsync("tabelas:\n  loja.pedidos: { acao: ignorar }\n", esquema, esquema);

        Assert.Equal([("loja.pedidos", "fk_cpf"), ("loja.pedidos", "fk_pedidos_clientes")], copiadas.Execucao.ChavesRemovidas.Select(chave => (chave.Tabela.ToString(), chave.Chave)));
        Assert.Empty(paiIgnorado.Execucao.ChavesRemovidas);
        Assert.Equal(2, filhoIgnorado.Execucao.ChavesRemovidas.Count);
    }

    [Fact]
    public async Task Sem_as_permissoes_de_um_lado_da_INC3062_uma_vez_por_lado()
    {
        var semAcesso = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Acesso), null));

        var naOrigem = Assert.Single(await RecusaAsync("", semAcesso, Coerente()));
        var nosDois = await RecusaAsync("", semAcesso, semAcesso);

        Assert.Equal(("INC3062", null), (naOrigem.Codigo, naOrigem.Tabela));
        Assert.Contains("do login na origem", naOrigem.Mensagem, StringComparison.Ordinal);
        Assert.Equal(["INC3062", "INC3062"], nosDois.Select(erro => erro.Codigo));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Seguranca_por_linha_na_leitura_da_origem_copiada_da_INC3060(bool? filtrada, bool recusa)
    {
        var origem = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.LeituraFiltradaPorLinha), filtrada));

        var codigos = await CodigosAsync("", origem, Coerente());
        var esvaziada = await CodigosAsync(Esvaziadas, origem, Coerente());

        Assert.Equal(recusa ? ["INC3060"] : [], codigos);
        Assert.Empty(esvaziada);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Seguranca_por_linha_na_gravacao_do_destino_copiado_da_INC3061(bool? restrita, bool recusa)
    {
        var destino = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.EscritaRestritaPorLinha), restrita));

        var codigos = await CodigosAsync("", Coerente(), destino);
        var esvaziada = await CodigosAsync(Esvaziadas, Coerente(), destino);

        Assert.Equal(recusa ? ["INC3061"] : [], codigos);
        Assert.Empty(esvaziada);
    }

    [Fact]
    public async Task Origem_copiada_sem_leitura_de_todas_as_colunas_da_INC3063_com_as_colunas()
    {
        var semLeitura = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeLer), false), (nameof(PermissoesTabela.ColunasSemLeitura), (string[])["cpf", "nome do"]));
        var semResposta = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeLer), null));

        var erro = Assert.Single(await RecusaAsync("", semLeitura, Coerente()));
        var nulo = Assert.Single(await RecusaAsync("", semResposta, Coerente()));

        Assert.Equal(("INC3063", "loja.clientes"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Contains("(sem leitura: cpf, \"nome do\")", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC3063", nulo.Codigo);
        Assert.DoesNotContain("sem leitura:", nulo.Mensagem, StringComparison.Ordinal);
        Assert.Empty(await CodigosAsync(Esvaziadas, semLeitura, Coerente()));
    }

    [Fact]
    public async Task Destino_sem_esvaziar_inserir_ou_gravar_a_identidade_da_INC3064()
    {
        var semEsvaziar = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeEsvaziar), false));
        var semInserir = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeInserir), null));
        var sempre = TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre));
        var semIdentidade = ComPermissao(sempre, "clientes", (nameof(PermissoesTabela.PodeGravarIdentidade), false));
        var porPadrao = ComPermissao(TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.PorPadrao)), "clientes", (nameof(PermissoesTabela.PodeGravarIdentidade), false));

        Assert.Contains("não pode esvaziar a tabela,", Assert.Single(await RecusaAsync("", Coerente(), semEsvaziar)).Mensagem, StringComparison.Ordinal);
        Assert.Contains("não pode inserir nela,", Assert.Single(await RecusaAsync("", Coerente(), semInserir)).Mensagem, StringComparison.Ordinal);
        var identidade = Assert.Single(await RecusaAsync("", sempre, semIdentidade));
        Assert.Equal(("INC3064", "loja.clientes"), (identidade.Codigo, identidade.Tabela!.ToString()));
        Assert.Contains("não pode gravar a identidade que só o banco gera", identidade.Mensagem, StringComparison.Ordinal);
        Assert.Empty(await CodigosAsync("", Coerente(), porPadrao));

        // Esvaziar só pede o esvaziamento.
        Assert.Empty(await CodigosAsync(Esvaziadas, Coerente(), semInserir));
        Assert.Equal(["INC3064"], await CodigosAsync(Esvaziadas, Coerente(), semEsvaziar));
    }

    [Fact]
    public async Task Trigger_ligada_na_arvore_sem_poder_desligar_da_INC3065()
    {
        var ligada = new TriggerEsquema { Nome = "tg", Ligada = true, Herdada = false };
        var desligada = new TriggerEsquema { Nome = "tg", Ligada = false, Herdada = false };
        var clone = new TriggerEsquema { Nome = "tg", Ligada = true, Herdada = true };
        var propria = new TriggerEsquema { Nome = "tg_propria", Ligada = true, Herdada = false };
        EsquemaBanco SemDesligar(EsquemaBanco esquema, string tabela) => ComPermissao(esquema, tabela, (nameof(PermissoesTabela.PodeDesligarTriggers), false));

        var naRaiz = await RecusaAsync("", Coerente(), SemDesligar(ComParticao([ligada], []), "clientes"));
        var soNoClone = await RecusaAsync("", Coerente(), SemDesligar(ComParticao([desligada], [clone]), "clientes"));
        var naParticao = Assert.Single(await RecusaAsync("", Coerente(), SemDesligar(ComParticao([], [propria]), "clientes_1")));
        var semLigada = await CodigosAsync("", Coerente(), SemDesligar(SemDesligar(ComParticao([desligada], [desligada]), "clientes"), "clientes_1"));
        var ignorada = await CodigosAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: ignorar }\n", Coerente(), SemDesligar(ComParticao([ligada], [propria]), "clientes"));

        Assert.Equal(("INC3065", "loja.clientes", (string?)null), (Assert.Single(naRaiz).Codigo, naRaiz[0].Tabela!.ToString(), naRaiz[0].Objeto));
        Assert.Equal("INC3065", Assert.Single(soNoClone).Codigo);
        Assert.Equal(("INC3065", "loja.clientes", "clientes_1"), (naParticao.Codigo, naParticao.Tabela!.ToString(), naParticao.Objeto));
        Assert.DoesNotContain("INC3065", semLigada);
        Assert.DoesNotContain("INC3065", ignorada);
    }

    [Fact]
    public async Task Chave_que_o_diario_remove_sem_poder_recriar_da_INC3066()
    {
        var semAlterar = ComPermissao(Coerente(), "pedidos", (nameof(PermissoesTabela.PodeAlterarRestricoes), false));
        var semReferenciar = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeReferenciar), null));

        var doFilho = Assert.Single(await RecusaAsync("", Coerente(), semAlterar));
        var doPai = Assert.Single(await RecusaAsync("", Coerente(), semReferenciar));
        var foraDoConjunto = await CodigosAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: esvaziar }\n", Coerente(), ComPermissao(semAlterar, "clientes", (nameof(PermissoesTabela.PodeReferenciar), false)));

        Assert.Equal(("INC3066", "loja.pedidos", "fk_pedidos_clientes"), (doFilho.Codigo, doFilho.Tabela!.ToString(), doFilho.Objeto));
        Assert.Contains("não pode alterar as restrições de loja.pedidos", doFilho.Mensagem, StringComparison.Ordinal);
        Assert.Contains("não pode referenciar loja.clientes", doPai.Mensagem, StringComparison.Ordinal);
        Assert.Empty(foraDoConjunto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Sequence_de_tabela_copiada_sem_ajuste_da_INC3067_e_a_outra_avisa_INC3561(bool? pode)
    {
        var destino = ComAjuste(Coerente(), pode);

        var erro = Assert.Single(await RecusaAsync("", Coerente(), destino));
        var esvaziada = await PlanejarAsync(Esvaziadas, Coerente(), destino);

        Assert.Equal(("INC3067", "loja.clientes", "clientes_id_seq"), (erro.Codigo, erro.Tabela!.ToString(), erro.Objeto));
        var aviso = Assert.Single(esvaziada.Avisos);
        Assert.Equal(("INC3561", null, "clientes_id_seq"), (aviso.Codigo, aviso.Tabela, aviso.Objeto));
        Assert.Empty(await CodigosAsync("", Coerente(), ComAjuste(Coerente(), true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Objeto_residual_que_nao_pode_ser_limpo_sai_com_codigo_2(bool? podeLimpar)
    {
        var visao = new ObjetoResidual { Nome = Nome("mv_clientes"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependenciasIncompletas = false, Populada = true };
        var grandes = new ObjetoResidual { Nome = new NomeObjeto("pg_catalog", "pg_largeobject"), Tipo = TipoObjetoResidual.ObjetosGrandes };

        var daVisao = Assert.Single(await RecusaAsync("", Coerente(), ComResidual(visao, podeLimpar)));
        var dosGrandes = Assert.Single(await RecusaAsync("", Coerente(), ComResidual(grandes, podeLimpar)));
        var semDados = await CodigosAsync("", Coerente(), ComResidual(BancoFalso.Clonar(visao, (nameof(ObjetoResidual.Populada), false)), podeLimpar));
        var semResposta = await RecusaAsync("", Coerente(), ComResidual(BancoFalso.Clonar(visao, (nameof(ObjetoResidual.Populada), null)), podeLimpar));

        Assert.Equal(("INC3070", null, "mv_clientes"), (daVisao.Codigo, daVisao.Tabela, daVisao.Objeto));
        Assert.Contains("não pode limpar a materialized view loja.mv_clientes", daVisao.Mensagem, StringComparison.Ordinal);
        Assert.Contains("não pode limpar os large objects do banco", dosGrandes.Mensagem, StringComparison.Ordinal);
        Assert.Empty(semDados);
        Assert.Equal("INC3070", Assert.Single(semResposta).Codigo);
        Assert.Empty(await CodigosAsync("", Coerente(), ComResidual(visao, true)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Materialized_view_com_dependencias_incompletas_avisa_INC3570(bool? incompletas, bool avisa)
    {
        var visao = new ObjetoResidual { Nome = Nome("mv_clientes"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependenciasIncompletas = incompletas, Populada = true };

        var plano = await PlanejarAsync("", Coerente(), ComResidual(visao, true));
        var semDados = await PlanejarAsync("", Coerente(), ComResidual(BancoFalso.Clonar(visao, (nameof(ObjetoResidual.Populada), false)), true));

        Assert.Equal(avisa ? [("INC3570", "mv_clientes")] : [], plano.Avisos.Select(aviso => (aviso.Codigo, aviso.Objeto!)));
        Assert.Empty(semDados.Avisos);
    }

    [Fact]
    public async Task Coluna_nao_suportada_copiada_sem_regra_avisa_INC3571()
    {
        var naoSuportada = BancoFalso.Clonar(Texto("arquivo", 10), (nameof(ColunaEsquema.TipoNativo), "oid"), (nameof(ColunaEsquema.Situacao), SituacaoTipo.NaoSuportado), (nameof(ColunaEsquema.Tipo), null));
        var esquema = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(naoSuportada).ToArray())));

        var plano = await PlanejarAsync("", esquema, esquema);
        var esvaziada = await PlanejarAsync(Esvaziadas, esquema, esquema);

        var aviso = Assert.Single(plano.Avisos);
        Assert.Equal(("INC3571", "loja.clientes", "arquivo"), (aviso.Codigo, aviso.Tabela!.ToString(), aviso.Coluna));
        Assert.Empty(esvaziada.Avisos);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Tabela_tocada_do_destino_publicada_da_INC3074(bool? publicada, bool recusa)
    {
        var destino = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), publicada)));

        var copiada = await CodigosAsync("", Coerente(), destino);
        var esvaziada = await CodigosAsync(Esvaziadas, Coerente(), destino);
        var ignorada = await CodigosAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: ignorar }\n", Coerente(), destino);
        var naOrigem = await CodigosAsync("", destino, Coerente());

        Assert.Equal(recusa ? ["INC3074"] : [], copiada);
        Assert.Equal(recusa ? ["INC3074"] : [], esvaziada);
        Assert.Empty(ignorada);
        Assert.Empty(naOrigem);
    }

    [Fact]
    public async Task A_particao_publicada_sozinha_tambem_da_INC3074()
    {
        var destino = Trocar(ComParticao([], []), "clientes_1", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), true)));

        var erro = Assert.Single(await RecusaAsync("", Coerente(), destino));

        Assert.Equal(("INC3074", "loja.clientes"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Contains("(loja.clientes_1)", erro.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Tabela_tocada_do_destino_assinada_da_INC3076(bool? assinada, bool recusa)
    {
        var destino = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Assinada), assinada)));

        var copiada = await CodigosAsync("", Coerente(), destino);
        var esvaziada = await CodigosAsync(Esvaziadas, Coerente(), destino);
        var ignorada = await CodigosAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: ignorar }\n", Coerente(), destino);
        var naOrigem = await CodigosAsync("", destino, Coerente());

        Assert.Equal(recusa ? ["INC3076"] : [], copiada);
        Assert.Equal(recusa ? ["INC3076"] : [], esvaziada);
        Assert.Empty(ignorada);
        Assert.Empty(naOrigem);
    }

    [Fact]
    public async Task A_particao_assinada_tambem_da_INC3076_pela_raiz()
    {
        var destino = Trocar(Trocar(ComParticao([], []), "clientes_1", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Assinada), true))), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Assinada), null)));

        var erro = Assert.Single(await RecusaAsync("", Coerente(), destino));

        Assert.Equal(("INC3076", "loja.clientes", null), (erro.Codigo, erro.Tabela!.ToString(), erro.Objeto));
        Assert.Contains("(loja.clientes, loja.clientes_1)", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("está numa assinatura de replicação, ligada ou não, ou o dialeto não informa", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tabela_publicada_e_assinada_da_os_dois_erros()
    {
        var destino = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), true), (nameof(TabelaEsquema.Assinada), true)));

        Assert.Equal(["INC3074", "INC3076"], await CodigosAsync("", Coerente(), destino));
    }

    [Fact]
    public async Task Sem_as_propriedades_de_um_lado_da_INC3082()
    {
        var semPropriedades = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Propriedades), null));

        var erro = Assert.Single(await RecusaAsync("", Coerente(), semPropriedades));

        Assert.Equal(("INC3082", null), (erro.Codigo, erro.Tabela));
        Assert.Contains("do banco no destino", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal(["INC3082", "INC3082"], (await RecusaAsync("", semPropriedades, semPropriedades)).Select(outro => outro.Codigo));
    }

    [Theory]
    [InlineData("SQL_ASCII", false, false, "UTF8", true, true, "INC3080")]
    [InlineData("UTF8", true, true, "SQL_ASCII", false, false, "INC3581")]
    [InlineData("UTF8", true, true, "LATIN1", true, false, "INC3081")]
    [InlineData("LATIN1", true, false, "LATIN2", true, false, "INC3081")]
    [InlineData("LATIN1", true, false, "UTF8", true, true, null)]
    [InlineData("LATIN1", true, false, "LATIN1", true, false, null)]
    [InlineData("SQL_ASCII", false, false, "SQL_ASCII", false, false, null)]
    [InlineData("UTF8", true, true, "UTF8", true, true, null)]
    public async Task A_codificacao_dos_dois_lados_e_conferida(string daOrigem, bool origemDefinida, bool origemUnicode, string doDestino, bool destinoDefinida, bool destinoUnicode, string? codigo)
    {
        var codigos = await CodigosAsync("", ComCodificacao(daOrigem, origemDefinida, origemUnicode), ComCodificacao(doDestino, destinoDefinida, destinoUnicode));

        // O limite em bytes de um lado só também avisa a coluna mais estreita (INC3521), que não é daqui.
        Assert.Equal(codigo is null ? [] : [codigo], codigos.Where(outro => outro is "INC3080" or "INC3081" or "INC3581"));
        Assert.DoesNotContain(codigos, outro => outro is not ("INC3080" or "INC3081" or "INC3581" or "INC3521"));
    }

    [Theory]
    [InlineData(15, 0, 18, 0, true)]
    [InlineData(18, 0, 18, 3, false)]
    public async Task Versao_principal_diferente_avisa_INC3580(int origem, int secundariaDaOrigem, int destino, int secundariaDoDestino, bool avisa)
    {
        var plano = await PlanejarAsync("", ComVersao(new Version(origem, secundariaDaOrigem)), ComVersao(new Version(destino, secundariaDoDestino)));

        Assert.Equal(avisa ? ["INC3580"] : [], plano.Avisos.Select(aviso => aviso.Codigo));
        Assert.Contains(avisa ? "origem (15) e o destino (18)" : "", plano.Avisos.SingleOrDefault()?.Mensagem ?? "", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, null, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    public async Task Transporte_sem_cifra_com_o_servidor_verificado_fora_da_maquina_avisa_INC3560(bool? cifrado, bool? verificado, bool local, bool avisa)
    {
        var provedor = new ProvedorDoPlanejar(Coerente(), Coerente())
        {
            Identidades = destino => destino
                ? BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Transporte), new TransporteConexao { Cifrado = cifrado, ServidorVerificado = verificado, Local = local }))
                : Identidade("loja"),
        };

        var plano = await DialetoProtegidoTestes.PlanejarAsync(provedor, Yaml.Minimo);

        Assert.Equal(avisa ? ["INC3560"] : [], plano.Avisos.Select(aviso => aviso.Codigo));
        Assert.Contains(avisa ? "A conexão com o destino" : "", plano.Avisos.SingleOrDefault()?.Mensagem ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_o_transporte_informado_avisa_INC3560_nos_dois_lados()
    {
        var provedor = new ProvedorDoPlanejar(Coerente(), Coerente())
        {
            Identidades = destino => BancoFalso.Clonar(Identidade(destino ? "loja_homolog" : "loja"), (nameof(IdentidadeLida.Transporte), null)),
        };

        var plano = await DialetoProtegidoTestes.PlanejarAsync(provedor, Yaml.Minimo);

        Assert.Equal(["INC3560", "INC3560"], plano.Avisos.Select(aviso => aviso.Codigo));
        Assert.Contains(plano.Avisos, aviso => aviso.Mensagem.StartsWith("A conexão com a origem", StringComparison.Ordinal));
        Assert.Contains(plano.Avisos, aviso => aviso.Mensagem.StartsWith("A conexão com o destino", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dialeto_que_nao_informa_nada_opcional_e_recusado_sem_excecao()
    {
        // O pior caso em cada ponto (N8): sem permissões, propriedades, esquema reservado, publicação, assinatura e transporte.
        var vazio = new EsquemaBanco { Tabelas = [Clientes(), Pedidos()] };
        var provedor = new ProvedorDoPlanejar(vazio, vazio)
        {
            Identidades = destino => BancoFalso.Clonar(Identidade(destino ? "loja_homolog" : "loja"), (nameof(IdentidadeLida.Transporte), null)),
        };

        var recusa = Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => DialetoProtegidoTestes.PlanejarAsync(provedor, Yaml.Minimo)), exactMatch: true);

        Assert.Equal(
            ["INC3062", "INC3062", "INC3073", "INC3073", "INC3074", "INC3074", "INC3076", "INC3076", "INC3082", "INC3082"],
            recusa.Erros.Select(erro => erro.Codigo).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("copiar", "copiar", true)]
    [InlineData("copiar", "esvaziar", true)]
    [InlineData("copiar", "ignorar", true)]
    [InlineData("esvaziar", "copiar", true)]
    [InlineData("esvaziar", "esvaziar", true)]
    [InlineData("esvaziar", "ignorar", false)]
    [InlineData("ignorar", "copiar", true)]
    [InlineData("ignorar", "esvaziar", true)]
    [InlineData("ignorar", "ignorar", false)]
    public async Task A_chave_entra_no_conjunto_do_diario_pelo_filho_copiado_ou_pelo_pai_tocado(string filho, string pai, bool noConjunto)
    {
        // O filho sem poder alterar as restrições só recusa quando a chave entra no conjunto, mesmo o filho ignorado.
        var semAlterar = ComPermissao(Coerente(), "pedidos", (nameof(PermissoesTabela.PodeAlterarRestricoes), false));

        var codigos = await CodigosAsync($"tabelas:\n  loja.pedidos: {{ acao: {filho} }}\n  loja.clientes: {{ acao: {pai} }}\n", Coerente(), semAlterar);

        Assert.Equal(noConjunto, codigos.Contains("INC3066"));
    }

    [Fact]
    public async Task A_identidade_que_so_o_banco_gera_e_a_do_destino()
    {
        var sempre = TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre));

        var soNoDestino = await CodigosAsync("", Coerente(), ComPermissao(sempre, "clientes", (nameof(PermissoesTabela.PodeGravarIdentidade), false)));
        var soNaOrigem = await CodigosAsync("", sempre, ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeGravarIdentidade), false)));

        Assert.Contains("INC3064", soNoDestino);
        Assert.DoesNotContain("INC3064", soNaOrigem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task A_particao_com_trigger_ligada_tambem_a_copia_exige_poder_desligar_nela(bool? pode)
    {
        // A cópia ligada de uma trigger também: o desligamento pela raiz não alcança a cópia de uma trigger própria de uma
        // partição intermediária.
        var clone = new TriggerEsquema { Nome = "tg", Ligada = true, Herdada = true };
        var propria = new TriggerEsquema { Nome = "tg_propria", Ligada = true, Herdada = false };

        var daCopia = await RecusaAsync("", Coerente(), ComPermissao(ComParticao([], [clone]), "clientes_1", (nameof(PermissoesTabela.PodeDesligarTriggers), pode)));
        var daPropria = await RecusaAsync("", Coerente(), ComPermissao(ComParticao([], [propria]), "clientes_1", (nameof(PermissoesTabela.PodeDesligarTriggers), pode)));

        Assert.Equal(("INC3065", "clientes_1"), (Assert.Single(daCopia).Codigo, daCopia[0].Objeto));
        Assert.Equal(("INC3065", "clientes_1"), (Assert.Single(daPropria).Codigo, daPropria[0].Objeto));
        Assert.Contains("A partição loja.clientes_1 do destino tem trigger, e o login não pode desligar e religar", daPropria[0].Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Com_a_arvore_desligada_a_particao_so_com_trigger_desligada_tambem_exige_poder_religar_nela(bool? pode)
    {
        // O diário restaura cada tabela física sozinha: a cópia desligada numa partição volta desligada, e só quem pode
        // alterar a partição a deixa assim.
        var daRaiz = new TriggerEsquema { Nome = "tg", Ligada = true, Herdada = false };
        var desligada = new TriggerEsquema { Nome = "tg", Ligada = false, Herdada = true };

        var erros = await RecusaAsync("", Coerente(), ComPermissao(ComParticao([daRaiz], [desligada]), "clientes_1", (nameof(PermissoesTabela.PodeDesligarTriggers), pode)));
        var semArvoreDesligada = await PlanejarAsync("", Coerente(), ComPermissao(ComParticao([], [desligada]), "clientes_1", (nameof(PermissoesTabela.PodeDesligarTriggers), pode)));

        Assert.Equal(("INC3065", "clientes_1"), (Assert.Single(erros).Codigo, erros[0].Objeto));
        Assert.NotNull(semArvoreDesligada);
    }

    [Fact]
    public async Task As_listas_das_mensagens_saem_juntas_e_em_ordem()
    {
        var semNada = ComPermissao(ComPermissao(Coerente(), "pedidos", (nameof(PermissoesTabela.PodeAlterarRestricoes), false)), "clientes", (nameof(PermissoesTabela.PodeReferenciar), false));
        var sempre = TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre));
        var semGravar = ComPermissao(sempre, "clientes", (nameof(PermissoesTabela.PodeEsvaziar), false), (nameof(PermissoesTabela.PodeInserir), false), (nameof(PermissoesTabela.PodeGravarIdentidade), false));
        var publicadas = Trocar(Trocar(ComParticao([], []), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), true))), "clientes_1", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), true)));

        var chave = Assert.Single(await RecusaAsync("", Coerente(), semNada));
        var gravacao = Assert.Single(await RecusaAsync("", sempre, semGravar));
        var publicacao = Assert.Single(await RecusaAsync("", Coerente(), publicadas));

        Assert.Contains("não pode alterar as restrições de loja.pedidos nem referenciar loja.clientes,", chave.Mensagem, StringComparison.Ordinal);
        Assert.Contains("não pode esvaziar a tabela, inserir nela nem gravar a identidade que só o banco gera,", gravacao.Mensagem, StringComparison.Ordinal);
        Assert.Contains("(loja.clientes, loja.clientes_1)", publicacao.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chave_declarada_na_particao_diz_a_particao()
    {
        var chave = new ChaveEstrangeira { Nome = "fk_dono", Colunas = ["id"], Referenciada = Nome("pedidos"), ColunasReferenciadas = ["id"], Imposta = true, Validada = true, DesligadaSemRestauracao = false };
        var destino = Trocar(ComParticao([], []), "clientes_1", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { chave })));

        var aviso = Assert.Single((await PlanejarAsync("tabelas:\n  loja.pedidos: { acao: ignorar }\n", Coerente(), destino)).Avisos, aviso => aviso.Objeto == "fk_dono");

        Assert.Equal(("INC3531", "loja.clientes"), (aviso.Codigo, aviso.Tabela!.ToString()));
        Assert.Contains("fk_dono (da partição loja.clientes_1)", aviso.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_unidade_recusada_pela_estrutura_nao_recebe_erros_de_permissao()
    {
        // A tabela só no destino, fora da configuração, já sai com o INC3021: inserir nela não é o problema.
        var destino = ComPermissao(ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("avulsa"), Colunas = [Inteiro("id")] }), "avulsa", (nameof(PermissoesTabela.PodeInserir), false), (nameof(PermissoesTabela.EscritaRestritaPorLinha), true));

        var erro = Assert.Single(await RecusaAsync("", Coerente(), destino));

        Assert.Equal(("INC3021", "loja.avulsa"), (erro.Codigo, erro.Tabela!.ToString()));
    }

    [Fact]
    public async Task Os_lados_saem_nas_mensagens_do_acesso_e_das_propriedades()
    {
        var semAcesso = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Acesso), null));
        var semPropriedades = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Propriedades), null));

        Assert.Contains("do login no destino", Assert.Single(await RecusaAsync("", Coerente(), semAcesso)).Mensagem, StringComparison.Ordinal);
        Assert.Contains("do banco na origem", Assert.Single(await RecusaAsync("", semPropriedades, Coerente())).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_as_permissoes_do_destino_o_residual_nao_e_conferido_e_nada_lanca()
    {
        var visao = new ObjetoResidual { Nome = Nome("mv_clientes"), Tipo = TipoObjetoResidual.VisaoMaterializada, DependenciasIncompletas = false, Populada = true };
        var destino = BancoFalso.Clonar(ComResidual(visao, false), (nameof(EsquemaBanco.Acesso), null));

        Assert.Equal(["INC3062"], (await RecusaAsync("", Coerente(), destino)).Select(erro => erro.Codigo));
    }

    [Fact]
    public async Task A_coluna_nao_suportada_de_um_lado_so_ou_numa_tabela_ignorada_tambem_avisa_INC3571()
    {
        var naoSuportada = BancoFalso.Clonar(Texto("arquivo", 10), (nameof(ColunaEsquema.TipoNativo), "oid"), (nameof(ColunaEsquema.Situacao), SituacaoTipo.NaoSuportado), (nameof(ColunaEsquema.Tipo), null));
        var comArquivo = (ColunaEsquema arquivo) => Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(arquivo).ToArray())));

        var soNaOrigem = await PlanejarAsync("", comArquivo(naoSuportada), comArquivo(Texto("arquivo", 10)));
        var ignorada = await PlanejarAsync("tabelas:\n  loja.clientes: { acao: ignorar }\n  loja.pedidos: { acao: ignorar }\n", comArquivo(naoSuportada), comArquivo(naoSuportada));

        Assert.Contains(soNaOrigem.Avisos, aviso => (aviso.Codigo, aviso.Coluna) == ("INC3571", "arquivo"));
        Assert.Equal([("INC3571", "loja.clientes", "arquivo")], ignorada.Avisos.Select(aviso => (aviso.Codigo, aviso.Tabela!.ToString(), aviso.Coluna)));
    }

    [Fact]
    public async Task Os_nomes_que_o_dialeto_devolve_saem_escapados_nas_mensagens_do_acesso()
    {
        // ESC e U+202E nos nomes da chave, da partição, da sequence, do residual, das colunas sem leitura e da codificação.
        var esc = ((char)0x1B).ToString();
        var rlo = ((char)0x202E).ToString();
        var chave = new ChaveEstrangeira { Nome = "fk" + esc + "[2J", Colunas = ["cliente"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["id"], Imposta = true, Validada = true, DesligadaSemRestauracao = false };
        var comChave = Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { chave })));
        var sequence = new SequenceEsquema { Nome = Nome("seq" + rlo), Colunas = [new ReferenciaColuna(Nome("clientes"), "id")] };
        var comSequence = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.Sequences), new[] { sequence }), (nameof(EsquemaBanco.Acesso), Acesso(Coerente().Tabelas, [sequence], [])));
        var visao = new ObjetoResidual { Nome = Nome("mv" + esc), Tipo = TipoObjetoResidual.VisaoMaterializada, DependenciasIncompletas = true, Populada = true };
        var particao = BancoFalso.Clonar(
            Clientes(),
            (nameof(TabelaEsquema.Nome), Nome("parte" + rlo)),
            (nameof(TabelaEsquema.Tipo), TipoTabela.Particao),
            (nameof(TabelaEsquema.Pai), Nome("clientes")),
            (nameof(TabelaEsquema.Publicada), true),
            (nameof(TabelaEsquema.Triggers), new[] { new TriggerEsquema { Nome = "tg", Ligada = true, Herdada = false } }));
        var raiz = BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Tipo), TipoTabela.Particionada), (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = ["id"], ColunasIncompletas = false }));
        var comParticao = ComPermissao(ComTabelas(raiz, particao, Pedidos()), particao.Nome.Nome, (nameof(PermissoesTabela.PodeDesligarTriggers), false));
        var semLeitura = ComPermissao(Coerente(), "clientes", (nameof(PermissoesTabela.PodeLer), false), (nameof(PermissoesTabela.ColunasSemLeitura), (string[])["cpf" + esc]));

        var mensagens = new List<string>();
        mensagens.AddRange(await MensagensAsync("tabelas:\n  loja.clientes: { acao: esvaziar }\n", Coerente(), ComPermissao(comChave, "pedidos", (nameof(PermissoesTabela.PodeAlterarRestricoes), false))));
        mensagens.AddRange(await MensagensAsync("", Coerente(), ComAjuste(comSequence, false)));
        mensagens.AddRange(await MensagensAsync("", Coerente(), ComResidual(visao, false)));
        mensagens.AddRange(await MensagensAsync("", Coerente(), ComResidual(visao, true)));
        mensagens.AddRange(await MensagensAsync("", Coerente(), comParticao));
        mensagens.AddRange(await MensagensAsync("", semLeitura, Coerente()));
        mensagens.AddRange(await MensagensAsync("", ComCodificacao("UM" + esc, true, false), ComCodificacao("OUTRO" + rlo, true, false)));

        Assert.Equal(9, mensagens.Count);
        Assert.All(mensagens, mensagem => Assert.DoesNotContain(esc, mensagem, StringComparison.Ordinal));
        Assert.All(mensagens, mensagem => Assert.DoesNotContain(rlo, mensagem, StringComparison.Ordinal));
    }

    // As mensagens e o texto de cada erro do plano recusado, ou de cada aviso do plano aprovado.
    private static async Task<IEnumerable<string>> MensagensAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino)
    {
        try
        {
            return (await PlanejarAsync(yaml, origem, destino)).Avisos.Select(aviso => aviso.Mensagem + aviso);
        }
        catch (PlanoRecusadoException recusa)
        {
            return recusa.Erros.Select(erro => erro.Mensagem + erro);
        }
    }

    // clientes.cpf referido por pedidos.cliente_cpf, além da chave do id.
    private static EsquemaBanco ComChaveDoCpf() => Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(
        tabela,
        (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(Texto("cliente_cpf", 11)).ToArray()),
        (nameof(TabelaEsquema.ChavesEstrangeiras), tabela.ChavesEstrangeiras.Append(new ChaveEstrangeira { Nome = "fk_cpf", Colunas = ["cliente_cpf"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["cpf"], Imposta = true, Validada = true, DesligadaSemRestauracao = false }).ToArray())));

    // clientes particionada com uma partição, cada uma com as triggers dadas.
    private static EsquemaBanco ComParticao(TriggerEsquema[] daRaiz, TriggerEsquema[] daParticao)
    {
        var raiz = BancoFalso.Clonar(
            Clientes(),
            (nameof(TabelaEsquema.Tipo), TipoTabela.Particionada),
            (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = ["id"], ColunasIncompletas = false }),
            (nameof(TabelaEsquema.Triggers), daRaiz));
        var particao = BancoFalso.Clonar(
            Clientes(),
            (nameof(TabelaEsquema.Nome), Nome("clientes_1")),
            (nameof(TabelaEsquema.Tipo), TipoTabela.Particao),
            (nameof(TabelaEsquema.Pai), Nome("clientes")),
            (nameof(TabelaEsquema.Triggers), daParticao));
        return ComTabelas(raiz, particao, Pedidos());
    }

    // O esquema com as trocas na permissão de uma tabela.
    internal static EsquemaBanco ComPermissao(EsquemaBanco esquema, string tabela, params (string Propriedade, object? Valor)[] trocas) =>
        BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(
            esquema.Acesso!,
            (nameof(AcessoLido.Tabelas), esquema.Acesso!.Tabelas.Select(permissao => permissao.Tabela.Equals(Nome(tabela)) ? BancoFalso.Clonar(permissao, trocas) : permissao).ToArray()))));

    internal static EsquemaBanco ComAjuste(EsquemaBanco esquema, bool? pode) =>
        BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Acesso), BancoFalso.Clonar(
            esquema.Acesso!,
            (nameof(AcessoLido.Sequences), esquema.Acesso!.Sequences.Select(permissao => new PermissoesSequence { Nome = permissao.Nome, PodeAjustar = pode }).ToArray()))));

    internal static EsquemaBanco ComResidual(ObjetoResidual residual, bool? podeLimpar)
    {
        var esquema = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosResiduais), new[] { residual }));
        var acesso = BancoFalso.Clonar(Acesso(esquema.Tabelas, esquema.Sequences, [residual]), (nameof(AcessoLido.Residuais), new[] { new PermissoesResidual { Nome = residual.Nome, Tipo = residual.Tipo, PodeLimpar = podeLimpar } }));
        return BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Acesso), acesso));
    }

    // Sem codificação declarada, o limite de texto conta bytes (a coerência do esquema exige).
    private static EsquemaBanco ComCodificacao(string nome, bool definida, bool unicode)
    {
        var esquema = ComPropriedade((nameof(PropriedadesBanco.Codificacao), new CodificacaoTexto { Nome = nome, Definida = definida, Unicode = unicode, BytesPorCaractereMaximo = unicode || !definida ? 4 : 1 }));
        return definida ? esquema : BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Tabelas), esquema.Tabelas.Select(tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), tabela.Colunas.Select(coluna => coluna.Comprimento is { } limite
                ? BancoFalso.Clonar(coluna, (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = limite.Valor, Unidade = UnidadeComprimento.Bytes }))
                : coluna).ToArray()))).ToArray()));
    }

    private static EsquemaBanco ComVersao(Version versao) => ComPropriedade((nameof(PropriedadesBanco.Versao), versao));

    private static EsquemaBanco ComPropriedade((string Propriedade, object? Valor) troca)
    {
        var esquema = Coerente();
        return BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Propriedades), BancoFalso.Clonar(esquema.Propriedades!, troca)));
    }

    private static Task<Plano> PlanejarAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino), yaml: Yaml.Minimo + yaml);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(yaml, origem, destino)), exactMatch: true).Erros;

    // Os códigos dos erros, se o plano é recusado, ou dos avisos, se não é.
    private static async Task<string[]> CodigosAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino)
    {
        try
        {
            return [.. (await PlanejarAsync(yaml, origem, destino)).Avisos.Select(aviso => aviso.Codigo)];
        }
        catch (PlanoRecusadoException recusa)
        {
            return [.. recusa.Erros.Select(erro => erro.Codigo)];
        }
    }
}
