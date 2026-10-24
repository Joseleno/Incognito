using System.Globalization;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// A fábrica do <c>id.permutar@1</c>: exige o tamanho do domínio (N) e a faixa da coluna até N − 1, um N só por domínio
/// na sessão, e a saída igual à do pseudonimizador do núcleo com o mesmo N. Os identificadores são sorteados na hora; as
/// asserções não imprimem valores.
/// </summary>
public sealed class FabricaIdentificadorTestes : IDisposable
{
    private const string Ambiente = "homologacao";
    private const long N = 10_000_000_000;

    private readonly ChaveIncognito _chave = ChaveIncognito.Gerar();
    private readonly SessaoTransformacao _sessao;

    public FabricaIdentificadorTestes() => _sessao = new SessaoTransformacao(_chave, Ambiente);

    private static FabricaTransformador Fabrica => TransformadoresEmbutidos.Fabricas.Single(fabrica => fabrica.Nome == "id.permutar");

    public void Dispose()
    {
        _sessao.Dispose();
        _chave.Dispose();
    }

    [Fact]
    public void Sem_o_tamanho_do_dominio_a_criacao_e_recusada()
    {
        var recusa = Assert.Throws<RegraInvalidaException>(() => Fabrica.Criar(Contexto(Regra(), tamanho: null)));

        Assert.Contains("O tamanho do domínio (N) não foi informado", recusa.Message, StringComparison.Ordinal);
        Assert.Equal(0, _sessao.Instancias);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(10L)]
    [InlineData(1_000L)]
    [InlineData(12_345L)]
    [InlineData(99_999L)]
    [InlineData(long.MaxValue)]
    public void Tamanho_que_nao_e_potencia_de_10_de_10_a_4_a_10_a_18_e_recusado(long tamanho)
    {
        var recusa = Assert.Throws<RegraInvalidaException>(() => Fabrica.Criar(Contexto(Regra(), tamanho)));

        Assert.Contains("potência de 10 de 10⁴ a 10¹⁸", recusa.Message, StringComparison.Ordinal);
        Assert.Equal(0, _sessao.Instancias);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-10_000L)]
    [InlineData(long.MinValue)]
    public void O_contexto_recusa_tamanho_zero_ou_negativo_sem_o_valor(long tamanho)
    {
        var recusa = Assert.Throws<ArgumentOutOfRangeException>(() => Contexto(Regra(), tamanho));

        Assert.Null(recusa.ActualValue);
        Assert.DoesNotContain(tamanho.ToString(CultureInfo.InvariantCulture), recusa.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10_000L)]
    [InlineData(1_000_000_000_000_000_000L)]
    public void Os_extremos_aceitos_sao_10_a_4_e_10_a_18(long tamanho)
    {
        var transformador = Fabrica.Criar(Contexto(Regra(), tamanho));

        Assert.Equal(("id.permutar", 1, Injetividade.Garantida), (transformador.Nome, transformador.Versao, transformador.Injetividade));
    }

    [Fact]
    public void A_coluna_precisa_comportar_N_menos_1()
    {
        // No limite exato passa; um a menos, ou sem a faixa, é recusado, e a mensagem diz o N (publicável).
        Fabrica.Criar(Contexto(Regra(), N, maior: N - 1));
        var abaixo = Assert.Throws<RegraInvalidaException>(() => Fabrica.Criar(Contexto(Regra(), N, maior: N - 2)));
        Assert.Contains("não comporta 9999999999: o pseudônimo do id.permutar vai até N − 1, com N = 10000000000", abaixo.Message, StringComparison.Ordinal);
        var semFaixa = Assert.Throws<RegraInvalidaException>(() => Fabrica.Criar(Contexto(Regra(), N, maior: null)));
        Assert.Contains("não informa a faixa", semFaixa.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void O_dominio_tem_um_N_so_na_sessao()
    {
        // A primeira coluna fixa o N do domínio; outra coluna dele com o mesmo N usa a mesma instância, e com outro N é
        // recusada (a PK e a FK sairiam com permutações diferentes). Outro domínio tem o próprio N.
        var regra = Regra();
        Fabrica.Criar(Contexto(regra, N));
        Fabrica.Criar(Contexto(regra, N));
        Assert.Equal(1, _sessao.Instancias);

        var recusa = Assert.Throws<RegraInvalidaException>(() => Fabrica.Criar(Contexto(regra, N * 10)));
        Assert.Contains("outro tamanho (N)", recusa.Message, StringComparison.Ordinal);

        Fabrica.Criar(Contexto(Regra("cliente-id"), N * 10));
        Assert.Equal(2, _sessao.Instancias);
    }

    [Fact]
    public void A_saida_e_a_do_pseudonimizador_com_o_mesmo_N()
    {
        var transformador = Fabrica.Criar(Contexto(Regra(), N));
        using var direto = new PseudonimizadorIdentificador(_chave, "pedido-id", 1, Ambiente, N, PoliticaEntradaMalformada.Falhar);
        var aleatorio = new Random(4601);

        for (var i = 0; i < 2_000; i++)
        {
            var entrada = aleatorio.NextInt64(1, N);
            var resultado = transformador.Transformar(entrada, default);
            Assert.Equal(direto.Pseudonimizar(entrada, out var esperado), resultado.Desfecho);
            Assert.True(Equals(esperado, resultado.Valor), "A saída da fábrica diverge da do pseudonimizador.");
            Assert.IsType<long>(resultado.Valor);
        }
    }

    [Fact]
    public void Zero_e_negativos_voltam_como_vieram()
    {
        var transformador = Fabrica.Criar(Contexto(Regra(), N));
        var aleatorio = new Random(4602);

        foreach (var entrada in new[] { 0L, -1L, long.MinValue, -aleatorio.NextInt64(1, long.MaxValue) })
        {
            var resultado = transformador.Transformar(entrada, default);
            Assert.Equal(Desfecho.PontoFixo, resultado.Desfecho);
            Assert.True(Equals(entrada, resultado.Valor), "O zero ou o negativo não voltou como veio.");
        }
    }

    [Theory]
    [InlineData(PoliticaEntradaMalformada.Falhar)]
    [InlineData(PoliticaEntradaMalformada.Anular)]
    [InlineData(PoliticaEntradaMalformada.Preservar)]
    public void A_partir_de_N_vale_a_politica_de_entrada_malformada(PoliticaEntradaMalformada politica)
    {
        var transformador = Fabrica.Criar(Contexto(Regra(malformada: politica), N));
        var entrada = new Random(4603).NextInt64(N, long.MaxValue);

        if (politica == PoliticaEntradaMalformada.Falhar)
        {
            var falha = Assert.Throws<IncognitoException>(() => transformador.Transformar(entrada, default));
            Assert.Equal("INC1003", falha.Codigo);
            Assert.DoesNotContain(entrada.ToString(CultureInfo.InvariantCulture), falha.ToString(), StringComparison.Ordinal);
            return;
        }

        var resultado = transformador.Transformar(entrada, default);
        Assert.Equal(politica == PoliticaEntradaMalformada.Anular ? Desfecho.AnuladoMalformado : Desfecho.CopiadoEmClaro, resultado.Desfecho);
        Assert.True(Equals(politica == PoliticaEntradaMalformada.Anular ? null : entrada, resultado.Valor), "A política não foi aplicada.");
    }

    [Fact]
    public void Os_parametros_e_o_derivarDe_sao_recusados()
    {
        var comParametro = new RegraTransformacao(new DominioPseudonimo("pedido-id", 1)) { Parametros = new Dictionary<string, string> { ["n"] = "10000" } };

        Assert.Equal("n", Assert.Throws<RegraInvalidaException>(() => Fabrica.Validar(comParametro)).Parametro);
        Assert.Throws<RegraInvalidaException>(() => Fabrica.Validar(new RegraTransformacao(new DominioPseudonimo("pedido-id", 1)) { TemDerivarDe = true }));
    }

    private static RegraTransformacao Regra(string dominio = "pedido-id", PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar) =>
        new(new DominioPseudonimo(dominio, 1)) { EntradaMalformada = malformada, TemDerivarDe = false, TemJustificativa = false };

    private ContextoCriacao Contexto(RegraTransformacao regra, long? tamanho, long? maior = long.MaxValue) =>
        new(regra, TipoColuna.Inteiro, _sessao) { MenorInteiro = long.MinValue, MaiorInteiro = maior, TamanhoDominio = tamanho };
}
