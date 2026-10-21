using System.Reflection;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.DialetoProtegidoTestes;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>Os primeiros passos do planejar: as regras, o dialeto de cada lado, as duas leituras e a coerência delas.</summary>
public sealed class PlanejadorInternoTestes
{
    private const string Efemero = """
        versao: 1
        origem:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_ORIGEM
        destino:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_DESTINO
          bancoEsperado: loja_homolog
        modo: efemero
        ambiente: homologacao

        """;

    [Theory]
    [InlineData(Yaml.Minimo)]
    [InlineData(Efemero)]
    public async Task O_resolvedor_so_recebe_a_origem_e_o_destino_nunca_a_chave(string yaml)
    {
        var resolvidas = new List<ConexaoBanco>();
        var configuracao = ConfiguracaoIncognito.LerYaml(yaml);

        await PlanejarAsync(new ProvedorDoPlanejar(), yaml: yaml, resolvidas: resolvidas);

        Assert.Equal([configuracao.Origem, configuracao.Destino], resolvidas);
    }

    [Fact]
    public void O_resolvedor_de_conexoes_so_aceita_uma_conexao_de_banco()
    {
        // A garantia é de tipo: a referência da chave é um ReferenciaSegredo solto, que o resolvedor não aceita.
        var parametro = typeof(PlanejadorInterno).GetMethod(nameof(PlanejadorInterno.PlanejarAsync), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()
            .Single(parametro => parametro.Name == "resolverConexao");

        Assert.Equal(typeof(Func<ConexaoBanco, SegredoConexao>), parametro.ParameterType);
    }

    [Fact]
    public async Task Sem_dialeto_para_o_provedor_o_plano_e_recusado_antes_de_conectar()
    {
        var recusa = Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(null)), exactMatch: true);

        Assert.Equal(["INC3003", "INC3003"], recusa.Erros.Select(erro => erro.Codigo));
        Assert.Contains("postgresql da origem", recusa.Erros[0].Mensagem, StringComparison.Ordinal);
        Assert.Contains("postgresql do destino", recusa.Erros[1].Mensagem, StringComparison.Ordinal);
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
    }

    [Fact]
    public async Task O_dialeto_de_outro_provedor_e_recusado_antes_de_conectar()
    {
        // O nome confere sem ignorar a caixa, como a configuração o escreve.
        foreach (var nome in new[] { "sqlserver", "PostgreSql" })
        {
            var provedor = new ProvedorDoPlanejar(nome: nome);

            var recusa = Assert.IsType<PlanoRecusadoException>(await FalharAsync(provedor), exactMatch: true);

            Assert.Equal(["INC3003", "INC3003"], recusa.Erros.Select(erro => erro.Codigo));
            Assert.DoesNotContain(provedor.Chamadas, chamada => chamada.StartsWith("Conectar", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_regra_recusada_para_antes_do_banco()
    {
        var provedor = new ProvedorDoPlanejar();
        var yaml = Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.inexistente@1 }\n";

        Assert.IsType<ConfiguracaoInvalidaException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(provedor, yaml: yaml)), exactMatch: true);

        Assert.Empty(provedor.Chamadas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task O_esquema_que_se_contradiz_para_o_plano_como_defeito_do_dialeto(bool destino)
    {
        var incoerente = EsquemaSintetico.Trocar(
            EsquemaSintetico.Coerente(),
            "pedidos",
            tabela => Dialetos.BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.ChavesEstrangeiras), new[]
            {
                new ChaveEstrangeira { Nome = "fk_\u001b[2J", Colunas = ["cliente"], Referenciada = EsquemaSintetico.Nome("sumida"), ColunasReferenciadas = ["id"] },
            })));
        var provedor = destino ? new ProvedorDoPlanejar(destino: incoerente) : new ProvedorDoPlanejar(origem: incoerente);

        var falha = Assert.IsType<FalhaDialetoException>(await FalharAsync(provedor), exactMatch: true);

        Assert.Equal("INC3002", falha.Codigo);
        Assert.Contains(destino ? "do destino" : "da origem", falha.Message, StringComparison.Ordinal);
        Assert.Contains("chave fk_\\u001B[2J de loja.pedidos referencia tabela ou coluna fora do esquema", falha.Message, StringComparison.Ordinal);
        Assert.Equal(1, ClassificadorDeSaida.De(falha));
        Assert.Contains(destino ? "Descartar:destino" : "Descartar:origem", provedor.Chamadas);
    }

    [Fact]
    public async Task As_leituras_sao_devolvidas_com_as_regras()
    {
        var provedor = new ProvedorDoPlanejar();
        var yaml = Yaml.Minimo + "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";

        var leituras = await PlanejarAsync(provedor, yaml: yaml);

        Assert.Same(provedor.Origem, leituras.Origem.Esquema);
        Assert.Same(provedor.Destino, leituras.Destino.Esquema);
        Assert.Equal(("loja", "loja_homolog"), (leituras.Origem.Identidade.Banco, leituras.Destino.Identidade.Banco));
        Assert.Equal("br.cpf", Assert.Single(leituras.Regras.Colunas).Fabrica.Nome);
    }
}
