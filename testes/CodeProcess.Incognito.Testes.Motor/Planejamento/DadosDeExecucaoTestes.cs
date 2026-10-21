using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;

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
            carga.Unidades[0].ToString(),
        ];

        Assert.All(textos, texto => Assert.DoesNotContain("INCOGNITO_", texto, StringComparison.Ordinal));
        Assert.All(textos, texto => Assert.DoesNotContain("homologacao", texto, StringComparison.Ordinal));
        Assert.Equal("DadosDeExecucao (1 unidades, 0 domínios)", carga.ToString());
        Assert.Equal("UnidadeDeCopia (0 colunas, 0 tratadas)", carga.Unidades[0].ToString());
    }

    [Fact]
    public void A_tabela_comum_le_tudo_menos_as_geradas_e_admite_congelar()
    {
        var tabela = Tabela("comum", TipoTabela.Comum);
        var comGeradas = new TabelaEsquema
        {
            Nome = EsquemaSintetico.Nome("comum"),
            Colunas = [EsquemaSintetico.Inteiro("id"), Gerada("armazenada", GeracaoColuna.Armazenada), EsquemaSintetico.Texto("nome", 10), Gerada("virtual", GeracaoColuna.Virtual)],
        };

        Assert.Equal((false, true), Forma(tabela, tabela));
        Assert.Equal(["id", "nome"], UnidadeDeCopia.Forma(comGeradas, EsquemaSintetico.ComTabelas(comGeradas)).ColunasLidas);
    }

    [Fact]
    public void A_particionada_nao_admite_congelar_e_le_as_particoes_por_ela()
    {
        var particionada = Tabela("vendas", TipoTabela.Particionada);
        var particao = Tabela("vendas_2026", TipoTabela.Particao, pai: "vendas");

        Assert.Equal((false, false), Forma(particionada, particionada, particao));
    }

    [Fact]
    public void Mae_e_herdeira_de_heranca_classica_leem_so_a_propria()
    {
        var mae = Tabela("pessoa", TipoTabela.Comum);
        var outraMae = Tabela("contato", TipoTabela.Comum);
        var herdeira = Tabela("funcionario", TipoTabela.Herdeira, pai: "pessoa", outrosPais: ["contato"]);
        var avulsa = Tabela("avulsa", TipoTabela.Comum);

        Assert.Equal((true, true), Forma(mae, mae, outraMae, herdeira, avulsa));
        Assert.Equal((true, true), Forma(outraMae, mae, outraMae, herdeira, avulsa));
        Assert.Equal((true, true), Forma(herdeira, mae, outraMae, herdeira, avulsa));
        Assert.Equal((false, true), Forma(avulsa, mae, outraMae, herdeira, avulsa));
    }

    [Fact]
    public void O_plano_toma_o_modo_da_carga_e_recusa_carga_que_nao_corresponde()
    {
        var clientes = Mapa("clientes");
        var pedidos = Mapa("pedidos");

        Assert.Equal(ModoTratamento.Reproduzivel, new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes)).Modo);
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, clientes, pedidos)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes, pedidos], [], Hash, CargaSintetica.Para(Yaml.Minimo, pedidos, clientes)));
        Assert.Throws<ArgumentException>(() => new Plano([clientes], [], Hash, CargaSintetica.Para(Yaml.Minimo, Mapa("clientes", AcaoTabela.Esvaziar))));
        Assert.Throws<ArgumentNullException>(() => new Plano([clientes], [], Hash, null!));

        // A coluna publicada como transformada precisa estar entre as tratadas da carga: senão sairia em claro.
        var regra = new RegraColuna(TipoRegra.Transformar, "nulo", 1, null, Transformadores.Injetividade.Nenhuma, null, null, null, OrigemRegra.Configuracao, null, null, null);
        var comRegra = new MapaTabela(new NomeTabela("loja", "clientes"), AcaoTabela.Copiar, false, [new MapaColuna("cpf", null, regra, new Classificacao(false, false))], null);
        Assert.Throws<ArgumentException>(() => new Plano([comRegra], [], Hash, CargaSintetica.Para(Yaml.Minimo, comRegra)));
    }

    private static MapaTabela Mapa(string tabela, AcaoTabela acao = AcaoTabela.Copiar) =>
        new(new NomeTabela("loja", tabela), acao, acaoEscrita: false, [], posicao: null);

    private static (bool SoAPropria, bool AdmiteFreeze) Forma(TabelaEsquema tabela, params TabelaEsquema[] todas)
    {
        var (soAPropria, admiteFreeze, _) = UnidadeDeCopia.Forma(tabela, EsquemaSintetico.ComTabelas(todas));
        return (soAPropria, admiteFreeze);
    }

    private static TabelaEsquema Tabela(string nome, TipoTabela tipo, string? pai = null, string[]? outrosPais = null) => new()
    {
        Nome = EsquemaSintetico.Nome(nome),
        Tipo = tipo,
        Pai = pai is null ? null : EsquemaSintetico.Nome(pai),
        OutrosPais = [.. (outrosPais ?? []).Select(EsquemaSintetico.Nome)],
        Colunas = [EsquemaSintetico.Inteiro("id")],
    };

    private static ColunaEsquema Gerada(string nome, GeracaoColuna geracao) => new()
    {
        Nome = nome,
        TipoNativo = "bigint",
        Situacao = SituacaoTipo.Suportado,
        Geracao = geracao,
    };
}

/// <summary>Uma carga mínima para montar um plano nos testes: as unidades das tabelas dadas, sem colunas tratadas.</summary>
internal static class CargaSintetica
{
    internal static DadosDeExecucao Para(string yaml, params MapaTabela[] tabelas) => new(
        ConfiguracaoDaExecucao.De(Yaml.Ler(yaml)),
        new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja")),
        new LeituraDoBanco(EsquemaSintetico.Coerente(), EsquemaSintetico.Identidade("loja_homolog")),
        [.. tabelas.Select(tabela => new UnidadeDeCopia(tabela.Nome, tabela.Acao, SoAPropria: false, AdmiteFreeze: true, [], []))],
        []);
}
