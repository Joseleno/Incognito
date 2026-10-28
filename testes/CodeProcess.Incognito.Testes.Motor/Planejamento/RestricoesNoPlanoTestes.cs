using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// As restrições do destino que o pseudônimo pode violar (avisos INC3540 a INC3542): a do tipo da coluna, o CHECK imposto e
/// a chave de partição que citam uma tratada, também por uma gerada calculada a partir dela.
/// </summary>
public sealed class RestricoesNoPlanoTestes
{
    private const string CpfTratado = "tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n";

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Restricao_do_tipo_na_coluna_tratada_da_o_aviso_INC3540(bool? restrita, bool avisa)
    {
        var destino = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.RestritaPeloTipo), restrita));

        var plano = await PlanejarAsync(CpfTratado, Coerente(), destino);
        var semRegra = await PlanejarAsync("", Coerente(), destino);

        Assert.Equal(avisa ? ["INC3540"] : [], plano.Avisos.Select(aviso => aviso.Codigo));
        Assert.Equal(avisa ? "cpf" : null, plano.Avisos.SingleOrDefault()?.Coluna);
        Assert.Empty(semRegra.Avisos);
    }

    [Fact]
    public async Task A_restricao_do_tipo_da_gerada_calculada_da_tratada_tambem_avisa()
    {
        var gerada = BancoFalso.Clonar(
            Texto("cpf_limpo", 11),
            (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
            (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["cpf"], Incompletas = false }),
            (nameof(ColunaEsquema.RestritaPeloTipo), true));
        var esquema = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Append(gerada).ToArray())));

        var plano = await PlanejarAsync(CpfTratado, esquema, esquema);

        var aviso = Assert.Single(plano.Avisos);
        Assert.Equal(("INC3540", "cpf_limpo"), (aviso.Codigo, aviso.Coluna));
    }

    [Fact]
    public async Task CHECK_imposto_que_cita_tratada_ou_gerada_dela_da_o_aviso_INC3541()
    {
        var gerada = BancoFalso.Clonar(
            Texto("cpf_limpo", 11),
            (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
            (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = ["cpf"], Incompletas = false }));
        var semReferencias = BancoFalso.Clonar(Texto("calculada", 11), (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada));
        RestricaoCheck Check(string nome, bool imposta, bool incompletas, params string[] colunas) =>
            new() { Nome = nome, Referencias = new ReferenciasDaExpressao { Colunas = colunas, Incompletas = incompletas }, Imposta = imposta };
        var destino = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), tabela.Colunas.Concat([gerada, semReferencias]).ToArray()),
            (nameof(TabelaEsquema.RestricoesCheck), new[]
            {
                Check("ck_cpf", true, false, "cpf"),
                Check("ck_gerada", true, false, "cpf_limpo"),
                Check("ck_sem_referencias", true, false, "calculada"),
                Check("ck_incompleto", true, true),
                Check("ck_nao_imposto", false, false, "cpf"),
                Check("ck_nome", true, false, "nome"),
            })));
        var origem = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Concat([gerada, semReferencias]).ToArray())));

        var plano = await PlanejarAsync(CpfTratado, origem, destino);
        var semRegra = await PlanejarAsync("", origem, destino);

        Assert.Equal(
            ["ck_cpf", "ck_gerada", "ck_incompleto", "ck_sem_referencias"],
            plano.Avisos.Where(aviso => aviso.Codigo == "INC3541").Select(aviso => aviso.Objeto!).Order(StringComparer.Ordinal));
        Assert.All(plano.Avisos.Where(aviso => aviso.Codigo == "INC3541"), aviso => Assert.Equal("clientes", aviso.Tabela!.Tabela));
        Assert.DoesNotContain(semRegra.Avisos, aviso => aviso.Codigo == "INC3541");
    }

    [Theory]
    [InlineData("cpf", false, true)]
    [InlineData("nome", true, true)]
    [InlineData("nome", false, false)]
    public async Task Chave_de_particao_com_tratada_ou_incompleta_da_o_aviso_INC3542(string coluna, bool incompletas, bool avisa)
    {
        var particionada = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Tipo), TipoTabela.Particionada),
            (nameof(TabelaEsquema.ChaveParticao), new ChaveParticao { Colunas = [coluna], ColunasIncompletas = incompletas })));

        var plano = await PlanejarAsync(CpfTratado, particionada, particionada);
        var semRegra = await PlanejarAsync("", particionada, particionada);

        Assert.Equal(avisa ? ["INC3542"] : [], plano.Avisos.Select(aviso => aviso.Codigo).Where(codigo => codigo == "INC3542"));
        Assert.DoesNotContain(semRegra.Avisos, aviso => aviso.Codigo == "INC3542");
    }

    [Fact]
    public async Task A_regra_propagada_tambem_conta_como_tratada()
    {
        // O CHECK de pedidos cita a coluna que recebe a regra de clientes.cpf pela chave.
        var esquema = Trocar(
            Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Texto("cpf", 14), Texto("nome", 100) }))),
            "pedidos",
            tabela => BancoFalso.Clonar(
                tabela,
                (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id", anulavel: false), Inteiro("cliente"), Texto("cliente_cpf", 14) }),
                (nameof(TabelaEsquema.ChavesEstrangeiras), tabela.ChavesEstrangeiras.Append(new ChaveEstrangeira { Nome = "fk_cpf", Colunas = ["cliente_cpf"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["cpf"], Imposta = true, Validada = true, DesligadaSemRestauracao = false }).ToArray()),
                (nameof(TabelaEsquema.RestricoesCheck), new[] { new RestricaoCheck { Nome = "ck_cliente_cpf", Referencias = new ReferenciasDaExpressao { Colunas = ["cliente_cpf"], Incompletas = false }, Imposta = true } })));

        var plano = await PlanejarAsync(CpfTratado, esquema, esquema);

        var aviso = Assert.Single(plano.Avisos);
        Assert.Equal(("INC3541", "pedidos", "ck_cliente_cpf"), (aviso.Codigo, aviso.Tabela!.Tabela, aviso.Objeto));
    }

    [Fact]
    public async Task O_fecho_das_geradas_nao_depende_da_ordem_das_colunas()
    {
        // A gerada de gerada vem antes, na ordem das colunas, da gerada calculada da tratada.
        ColunaEsquema Gerada(string nome, string de) => BancoFalso.Clonar(
            Texto(nome, 11),
            (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
            (nameof(ColunaEsquema.ReferenciasDaGeracao), new ReferenciasDaExpressao { Colunas = [de], Incompletas = false }));
        var colunas = (TabelaEsquema tabela) => tabela.Colunas.Concat([Gerada("cpf_final", "cpf_limpo"), Gerada("cpf_limpo", "cpf")]).ToArray();
        var esquema = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.Colunas), colunas(tabela)),
            (nameof(TabelaEsquema.RestricoesCheck), new[] { new RestricaoCheck { Nome = "ck_final", Referencias = new ReferenciasDaExpressao { Colunas = ["cpf_final"], Incompletas = false }, Imposta = true } })));

        var plano = await PlanejarAsync(CpfTratado, esquema, esquema);

        var aviso = Assert.Single(plano.Avisos);
        Assert.Equal(("INC3541", "ck_final"), (aviso.Codigo, aviso.Objeto));
    }

    private static Task<Plano> PlanejarAsync(string yaml, EsquemaBanco origem, EsquemaBanco destino) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino), yaml: Yaml.Minimo + yaml);
}
