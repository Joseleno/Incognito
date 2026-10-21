using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Transformadores;
using CsCheck;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>Os tipos públicos do plano: igualdade e exibição dos nomes, formato dos avisos e erros, invariantes.</summary>
public sealed class TiposDoPlanoTestes
{
    private const string Hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Os_nomes_comparam_de_forma_ordinal()
    {
        var cliente = new NomeTabela("loja", "cliente");

        Assert.Equal(cliente, new NomeTabela("loja", "cliente"));
        Assert.Equal(cliente.GetHashCode(), new NomeTabela("loja", "cliente").GetHashCode());
        Assert.NotEqual(cliente, new NomeTabela("loja", "Cliente"));
        Assert.NotEqual(cliente, new NomeTabela("Loja", "cliente"));
        Assert.NotEqual(cliente, new NomeTabela("cliente", "loja"));
        Assert.False(cliente.Equals(null));
        Assert.False(cliente.Equals((object)"loja.cliente"));

        var cpf = new NomeColuna(cliente, "cpf");
        Assert.Equal(cpf, new NomeColuna(new NomeTabela("loja", "cliente"), "cpf"));
        Assert.Equal(cpf.GetHashCode(), new NomeColuna(new NomeTabela("loja", "cliente"), "cpf").GetHashCode());
        Assert.NotEqual(cpf, new NomeColuna(cliente, "CPF"));
        Assert.NotEqual(cpf, new NomeColuna(new NomeTabela("loja", "pedido"), "cpf"));
        Assert.False(cpf.Equals(null));
    }

    [Theory]
    [InlineData("loja", "cliente", "loja.cliente")]
    [InlineData("loja", "nota fiscal", "loja.\"nota fiscal\"")]
    [InlineData("a.b", "c", "\"a.b\".c")]
    [InlineData("a", "b.c", "a.\"b.c\"")]
    [InlineData("loja", "diz\"oi", "loja.\"diz\"\"oi\"")]
    [InlineData("loja", "a\\b", "loja.a\\\\b")]
    [InlineData("loja", "ação", "loja.ação")]
    public void O_nome_da_tabela_sai_escapado_e_entre_aspas_quando_preciso(string esquema, string tabela, string exibido) =>
        Assert.Equal(exibido, new NomeTabela(esquema, tabela).ToString());

    [Fact]
    public void O_nome_nao_leva_caractere_de_controle_ao_terminal()
    {
        Assert.Equal("loja.\\u001B[2Jx", new NomeTabela("loja", "\u001B[2Jx").ToString());
        Assert.Equal("loja.a\\u202Eb", new NomeTabela("loja", "a‮b").ToString());
        Assert.Equal("loja.cliente.\\u001B", new NomeColuna(new NomeTabela("loja", "cliente"), "\u001B").ToString());
        Assert.Equal("loja.cliente.\"meu cpf\"", new NomeColuna(new NomeTabela("loja", "cliente"), "meu cpf").ToString());
    }

    [Fact]
    public void A_exibicao_do_nome_e_injetiva()
    {
        // Sem as aspas, "a.b"."c" e "a"."b.c" sairiam iguais; sem dobrar a aspa, a."b"".c" e "a.""b".c também.
        var partes = Gen.OneOf(Gen.Const("."), Gen.Const("\""), Gen.Const(" "), Gen.Const("\\"), Gen.Const("a"), Gen.Const("\n"))
            .Array[1, 5].Select(pedacos => string.Concat(pedacos));
        Gen.Select(partes, partes, partes, partes).Sample(
            (esquema, tabela, outroEsquema, outraTabela) =>
                (esquema == outroEsquema && tabela == outraTabela)
                || new NomeTabela(esquema, tabela).ToString() != new NomeTabela(outroEsquema, outraTabela).ToString(),
            iter: 20000);
    }

    [Fact]
    public void Nome_vazio_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => new NomeTabela("", "cliente"));
        Assert.Throws<ArgumentException>(() => new NomeTabela("loja", ""));
        Assert.Throws<ArgumentException>(() => new NomeColuna(new NomeTabela("loja", "cliente"), ""));
        Assert.Throws<ArgumentNullException>(() => new NomeColuna(null!, "cpf"));
    }

    [Fact]
    public void Aviso_e_erro_saem_com_posicao_e_alvo()
    {
        var cliente = new NomeTabela("loja", "cliente");
        var posicao = new PosicaoConfiguracao("config/incognito.yaml", 12, 7, "/tabelas/loja.cliente/colunas/cpf");

        Assert.Equal("config/incognito.yaml(12,7): INC3541: loja.cliente.cpf: Mensagem.", new Aviso("INC3541", "Mensagem.", cliente, "cpf", posicao: posicao).ToString());
        Assert.Equal("incognito.yaml(1,1): INC3010: loja.cliente: Mensagem.", new ErroPlano("INC3010", "Mensagem.", cliente, posicao: new PosicaoConfiguracao(null, 1, 1, "")).ToString());
        Assert.Equal("INC3530: loja.cliente: Mensagem.", new Aviso("INC3530", "Mensagem.", cliente).ToString());
        Assert.Equal("INC3530: loja.cliente.\"meu cpf\": Mensagem.", new Aviso("INC3530", "Mensagem.", cliente, "meu cpf").ToString());
        Assert.Equal("INC3003: Mensagem.", new ErroPlano("INC3003", "Mensagem.").ToString());
        Assert.Equal("INC3003: Mensagem.", new ErroPlano("INC3003", "Mensagem.", objeto: "idx_cpf").ToString());
    }

    [Fact]
    public void Aviso_e_erro_guardam_cada_campo()
    {
        var cliente = new NomeTabela("loja", "cliente");
        var posicao = new PosicaoConfiguracao("incognito.yaml", 3, 5, "/tabelas/loja.cliente");
        var aviso = new Aviso("INC3530", "Mensagem.", cliente, "cpf", "idx_cpf", posicao);
        var erro = new ErroPlano("INC3010", "Outra.", cliente, "cpf", "idx_cpf", posicao);

        Assert.Equal(("INC3530", "Mensagem.", cliente, "cpf", "idx_cpf", posicao), (aviso.Codigo, aviso.Mensagem, aviso.Tabela, aviso.Coluna, aviso.Objeto, aviso.Posicao));
        Assert.Equal(("INC3010", "Outra.", cliente, "cpf", "idx_cpf", posicao), (erro.Codigo, erro.Mensagem, erro.Tabela, erro.Coluna, erro.Objeto, erro.Posicao));
        Assert.Equal(("incognito.yaml", 3, 5, "/tabelas/loja.cliente"), (posicao.Arquivo, posicao.Linha, posicao.Coluna, posicao.Local));
    }

    [Fact]
    public void Aviso_e_erro_recusam_o_que_nao_faz_sentido()
    {
        var cliente = new NomeTabela("loja", "cliente");

        Assert.Throws<ArgumentException>(() => new Aviso("", "Mensagem."));
        Assert.Throws<ArgumentException>(() => new Aviso("INC3530", ""));
        Assert.Throws<ArgumentException>(() => new Aviso("INC3530", "Mensagem.", coluna: "cpf"));
        Assert.Throws<ArgumentException>(() => new Aviso("INC3530", "Mensagem.", cliente, ""));
        Assert.Throws<ArgumentException>(() => new Aviso("INC3530", "Mensagem.", objeto: ""));
        Assert.Throws<ArgumentException>(() => new ErroPlano("", "Mensagem."));
        Assert.Throws<ArgumentException>(() => new ErroPlano("INC3010", ""));
        Assert.Throws<ArgumentException>(() => new ErroPlano("INC3010", "Mensagem.", coluna: "cpf"));
        Assert.Throws<ArgumentException>(() => new ErroPlano("INC3010", "Mensagem.", cliente, ""));
        Assert.Throws<ArgumentException>(() => new ErroPlano("INC3010", "Mensagem.", objeto: ""));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void A_posicao_comeca_em_1(int linha, int coluna) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PosicaoConfiguracao(null, linha, coluna, ""));

    [Fact]
    public void A_regra_que_transforma_tem_transformador_versao_e_injetividade()
    {
        var dominio = new DominioPseudonimo("cpf", 1);
        var origem = new NomeColuna(new NomeTabela("loja", "cliente"), "cpf");
        var transformar = Regra(TipoRegra.Transformar, "br.cpf", 1, dominio, Injetividade.Garantida);
        var propagada = Regra(TipoRegra.Transformar, "br.cpf", 1, dominio, Injetividade.Garantida, PropagacaoRegra.ChaveEstrangeira, origem);
        var manter = Regra(TipoRegra.Manter, null, null, null, null);

        Assert.Equal(("br.cpf", 1, dominio, Injetividade.Garantida), (transformar.Transformador, transformar.Versao, transformar.Dominio, transformar.Injetividade));
        Assert.Equal((PropagacaoRegra.ChaveEstrangeira, origem), (propagada.Propagacao, propagada.PropagadaDe));
        Assert.Equal(TipoRegra.Manter, manter.Tipo);
        Assert.Null(Regra(TipoRegra.Transformar, "nulo", 1, null, Injetividade.Nenhuma).Dominio);
    }

    [Fact]
    public void A_regra_incoerente_e_recusada()
    {
        var dominio = new DominioPseudonimo("cpf", 1);
        var origem = new NomeColuna(new NomeTabela("loja", "cliente"), "cpf");

        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Manter, "br.cpf", null, null, null));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Manter, null, 1, null, null));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Manter, null, null, null, Injetividade.Garantida));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Manter, null, null, dominio, null));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cpf", 1, default(DominioPseudonimo), Injetividade.Garantida));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, null, 1, null, Injetividade.Garantida));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cpf", null, null, Injetividade.Garantida));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cpf", 1, null, null));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "", 1, null, Injetividade.Garantida));
        Assert.Throws<ArgumentOutOfRangeException>(() => Regra(TipoRegra.Transformar, "br.cpf", 0, null, Injetividade.Garantida));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cpf", 1, null, Injetividade.Garantida, PropagacaoRegra.Heranca, null));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cpf", 1, null, Injetividade.Garantida, null, origem));
        Assert.Throws<ArgumentException>(() => Regra(TipoRegra.Transformar, "br.cep", 1, null, Injetividade.Garantida, derivarDe: ""));
    }

    [Fact]
    public void O_plano_confere_o_hash_e_copia_as_listas()
    {
        var colunas = new List<MapaColuna> { new("cpf", TipoColuna.Texto, null, new Classificacao(sensivel: true, identificador: false)) };
        var tabela = new MapaTabela(new NomeTabela("loja", "cliente"), AcaoTabela.Copiar, acaoEscrita: false, colunas, posicao: null);
        var tabelas = new List<MapaTabela> { tabela };
        var plano = new Plano(tabelas, [], Hash, CargaSintetica.Para(Yaml.Minimo, tabela));
        colunas.Clear();
        tabelas.Clear();

        Assert.Equal(ModoTratamento.Reproduzivel, plano.Modo);
        Assert.Equal(Hash, plano.HashConfiguracao);
        Assert.Same(tabela, Assert.Single(plano.Tabelas));
        Assert.Single(tabela.Colunas);
        Assert.True(((ICollection<MapaTabela>)plano.Tabelas).IsReadOnly);
        Assert.True(((ICollection<MapaColuna>)tabela.Colunas).IsReadOnly);
        Assert.True(((ICollection<Aviso>)plano.Avisos).IsReadOnly);
        Assert.Equal($"Plano {Hash} (1 tabela, 0 avisos)", plano.ToString());
        Assert.Equal($"Plano {Hash} (0 tabelas, 1 aviso)", new Plano([], [new Aviso("INC3530", "Mensagem.")], Hash, CargaSintetica.Para(Yaml.Minimo)).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData("sha256:0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef1234567")]
    public void O_plano_recusa_hash_fora_do_formato(string hash) =>
        Assert.Throws<ArgumentException>(() => new Plano([], [], hash, CargaSintetica.Para(Yaml.Minimo)));

    [Fact]
    public void Item_nulo_nas_listas_e_recusado()
    {
        Assert.Throws<ArgumentNullException>(() => new Plano([null!], [], Hash, CargaSintetica.Para(Yaml.Minimo)));
        Assert.Throws<ArgumentNullException>(() => new Plano([], [null!], Hash, CargaSintetica.Para(Yaml.Minimo)));
        Assert.Throws<ArgumentNullException>(() => new MapaTabela(new NomeTabela("loja", "cliente"), AcaoTabela.Copiar, false, [null!], null));
        Assert.Throws<ArgumentNullException>(() => new MapaColuna("cpf", null, null, null!));
        Assert.Throws<ArgumentException>(() => new MapaColuna("", null, null, new Classificacao(false, false)));
    }

    private static RegraColuna Regra(
        TipoRegra tipo,
        string? transformador,
        int? versao,
        DominioPseudonimo? dominio,
        Injetividade? injetividade,
        PropagacaoRegra? propagacao = null,
        NomeColuna? propagadaDe = null,
        string? derivarDe = null) =>
        new(tipo, transformador, versao, dominio, injetividade, null, null, derivarDe, OrigemRegra.Configuracao, propagacao, propagadaDe, null);
}
