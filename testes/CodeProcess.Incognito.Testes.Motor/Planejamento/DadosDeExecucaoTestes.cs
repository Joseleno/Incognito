using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>A carga interna do plano: a configuração como escrita, a forma de cada unidade e a correspondência com o plano.</summary>
public sealed class DadosDeExecucaoTestes
{
    private const string Hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void A_configuracao_da_execucao_guarda_as_referencias_como_escritas_e_o_padrao_da_consistencia()
    {
        var configuracao = ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Minimo));

        Assert.Equal(new ConexaoEscrita(ProvedorBanco.PostgreSql, new ReferenciaEscrita(TipoReferenciaSegredo.Ambiente, "INCOGNITO_ORIGEM"), null), configuracao.Origem);
        Assert.Equal(new ConexaoEscrita(ProvedorBanco.PostgreSql, new ReferenciaEscrita(TipoReferenciaSegredo.Ambiente, "INCOGNITO_DESTINO"), "loja_homolog"), configuracao.Destino);
        Assert.Equal(new ReferenciaEscrita(TipoReferenciaSegredo.Ambiente, "INCOGNITO_CHAVE"), configuracao.Chave);
        Assert.Equal((ModoTratamento.Reproduzivel, "homologacao", Consistencia.MelhorEsforco, (int?)null), (configuracao.Modo, configuracao.Ambiente, configuracao.Consistencia, configuracao.Paralelismo));
    }

    [Fact]
    public void A_configuracao_da_execucao_leva_as_politicas_escritas_e_nenhuma_chave_no_efemero()
    {
        var escrita = ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Com("politica:\n  consistencia: exigir\n  paralelismo: 4\n")));
        var efemera = ConfiguracaoDaExecucao.De(Yaml.Ler(Yaml.Trocando("modo: reproduzivel", "modo: efemero").Replace("chave: ambiente:INCOGNITO_CHAVE\n", "", StringComparison.Ordinal)));

        Assert.Equal((Consistencia.Exigir, (int?)4), (escrita.Consistencia, escrita.Paralelismo));
        Assert.Equal(ModoTratamento.Efemero, efemera.Modo);
        Assert.Null(efemera.Chave);
    }

    [Fact]
    public void O_texto_das_partes_nao_mostra_conteudo()
    {
        var carga = CargaSintetica.Para(Yaml.Minimo, Mapa("clientes"));
        string[] textos =
        [
            carga.ToString(), carga.Configuracao.ToString(), carga.Configuracao.Origem.ToString(), carga.Configuracao.Chave!.ToString(),
            carga.Unidades[0].ToString(), new ChaveRemovida(new NomeTabela("loja", "homologacao"), "homologacao").ToString(),
        ];

        Assert.All(textos, texto => Assert.DoesNotContain("INCOGNITO_", texto, StringComparison.Ordinal));
        Assert.All(textos, texto => Assert.DoesNotContain("homologacao", texto, StringComparison.Ordinal));
        Assert.Equal("ChaveRemovida", textos[^1]);
        Assert.Equal("DadosDeExecucao (1 unidades, 0 domínios)", carga.ToString());
        Assert.Equal("UnidadeDeCopia (0 colunas, 0 tratadas)", carga.Unidades[0].ToString());
    }

    [Fact]
    public void A_unidade_pareia_as_lidas_com_o_destino_e_recusa_tratada_sem_a_coluna_lida()
    {
        var nome = new NomeTabela("loja", "clientes");
        var tratada = Tratada("cpf", referencia: "nome");

        var unidade = new UnidadeDeCopia(nome, AcaoTabela.Copiar, new NomeTabela("loja", "Clientes"), ["id", "cpf", "nome"], ["id", "CPF", "nome"], [tratada]);

        Assert.Equal(["id", "cpf", "nome"], unidade.ColunasLidas);
        Assert.Equal(["id", "CPF", "nome"], unidade.ColunasNoDestino);
        Assert.Equal("Clientes", unidade.NoDestino!.Tabela);

        // Lida sem par, repetida, tratada sem a coluna lida ou com a referência fora das lidas: defeito do motor.
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "cpf"], ["id"], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "id"], ["id", "id"], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "nome"], ["id", "nome"], [tratada]));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "cpf"], ["id", "cpf"], [tratada]));

        // Copiada ou esvaziada sem a tabela no destino, ação fora do enum, não copiada com lidas ou tratadas, tratada repetida
        // ou nula.
        var outra = Tratada("cpf");
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, null, ["id"], ["id"], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Esvaziar, null, [], [], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, (AcaoTabela)99, nome, [], [], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Esvaziar, nome, ["id"], ["id"], []));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Ignorar, null, ["cpf"], ["cpf"], [outra]));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "cpf"], ["id", "cpf"], [outra, outra]));
        Assert.Throws<InvalidOperationException>(() => new UnidadeDeCopia(nome, AcaoTabela.Copiar, nome, ["id", "cpf"], ["id", "cpf"], [null!]));
        Assert.Null(new UnidadeDeCopia(nome, AcaoTabela.Ignorar, null, [], [], []).NoDestino);
    }

    [Fact]
    public void A_tratada_recusa_a_referencia_sem_o_tipo_dela_e_o_tipo_sem_a_referencia()
    {
        Assert.Throws<InvalidOperationException>(() => Tratada("cpf", referencia: "nome", tipoDaReferencia: false));
        Assert.Throws<InvalidOperationException>(() => Tratada("cpf", referencia: null, tipoDaReferencia: true));
        Assert.Equal("nome", Tratada("cpf", referencia: "nome").Referencia);
    }

    [Fact]
    public async Task A_carga_publicada_tem_as_lidas_sem_as_geradas_e_o_nome_no_destino()
    {
        var gerada = (nameof(ColunaEsquema.Geracao), (object?)GeracaoColuna.Armazenada);
        var origem = EsquemaSintetico.Trocar(
            EsquemaSintetico.Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(BancoFalso.Clonar(EsquemaSintetico.Inteiro("calculada"), gerada)).Append(EsquemaSintetico.Inteiro("so_no_destino_gerada")).ToArray())));
        var destino = EsquemaSintetico.Trocar(
            EsquemaSintetico.Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(BancoFalso.Clonar(EsquemaSintetico.Inteiro("calculada"), gerada)).Append(BancoFalso.Clonar(EsquemaSintetico.Inteiro("so_no_destino_gerada"), gerada)).ToArray())));

        var plano = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino), yaml: Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n");
        var clientes = plano.Execucao.Unidades.Single(unidade => unidade.Nome.Tabela == "clientes");

        Assert.Equal(["id", "cpf", "nome"], clientes.ColunasLidas);
        Assert.Equal(clientes.ColunasLidas, clientes.ColunasNoDestino);
        Assert.Equal(new NomeTabela("loja", "clientes"), clientes.NoDestino);
        Assert.Null(Assert.Single(clientes.Tratadas).Referencia);
        Assert.Same(plano.FonteDoPlano, plano.FonteDoPlano);
        Assert.Equal(ConfiguracaoDaExecucao.De(plano.FonteDoPlano), plano.Execucao.Configuracao);
    }

    [Fact]
    public void O_plano_toma_o_modo_da_carga_e_recusa_carga_que_nao_corresponde()
    {
        var clientes = Mapa("clientes");
        var pedidos = Mapa("pedidos");

        Assert.Equal(ModoTratamento.Reproduzivel, new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes), Yaml.Ler(Yaml.Minimo)).Modo);
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo), Yaml.Ler(Yaml.Minimo)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes, pedidos), Yaml.Ler(Yaml.Minimo)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes, pedidos], [], Hash, CargaSintetica.Para(Yaml.Minimo, pedidos, clientes), Yaml.Ler(Yaml.Minimo)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, Mapa("clientes", AcaoTabela.Esvaziar)), Yaml.Ler(Yaml.Minimo)));
        Assert.Throws<ArgumentNullException>(() => new Plano([clientes], [], Hash, null!, Yaml.Ler(Yaml.Minimo)));
        Assert.Throws<ArgumentNullException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes), null!));
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes), Yaml.Ler(Yaml.Com("politica:\n  paralelismo: 2\n"))));

        // A coluna publicada como transformada precisa estar entre as tratadas da carga: senão sairia em claro.
        var regra = new RegraColuna(TipoRegra.Transformar, "nulo", 1, null, Transformadores.Injetividade.Nenhuma, null, null, null, OrigemRegra.Configuracao, null, null, null);
        var comRegra = new MapaTabela(new NomeTabela("loja", "clientes"), AcaoTabela.Copiar, false, [new MapaColuna("cpf", null, regra, new Classificacao(false, false))], null);
        Assert.Throws<ArgumentException>(() => new Plano([comRegra], [], Hash, CargaSintetica.Para(Yaml.Minimo, comRegra), Yaml.Ler(Yaml.Minimo)));
    }

    private static MapaTabela Mapa(string tabela, AcaoTabela acao = AcaoTabela.Copiar) =>
        new(new NomeTabela("loja", tabela), acao, acaoEscrita: false, [], posicao: null);

    private static ColunaTratada Tratada(string coluna, string? referencia = null, bool? tipoDaReferencia = null)
    {
        var registro = new RegistroTransformadores();
        Assert.True(registro.TentarObter("br.cpf", 1, out var fabrica));
        var regra = new Transformadores.RegraTransformacao(new Transformadores.DominioPseudonimo("br.cpf", 1));
        return new ColunaTratada(coluna, fabrica, regra, new Dictionary<string, string>(), new ContextoDaColuna(Transformadores.TipoColuna.Texto, 11, null, null, null, null, null, (tipoDaReferencia ?? referencia is not null) ? Transformadores.TipoColuna.Texto : null), referencia, Transformadores.Injetividade.Garantida);
    }
}

/// <summary>Uma carga mínima para montar um plano nos testes: as unidades das tabelas dadas, sem colunas tratadas.</summary>
internal static class CargaSintetica
{
    internal static DadosDeExecucao Para(string yaml, params MapaTabela[] tabelas) => new(
        ConfiguracaoDaExecucao.De(Yaml.Ler(yaml)),
        new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja")),
        new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja_homolog")),
        [.. tabelas.Select(tabela => new UnidadeDeCopia(tabela.Nome, tabela.Acao, tabela.Nome, [], [], []))],
        [],
        [],
        []);
}
