using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O que o <c>planejar</c> confere do diário (decisão 96): o esquema reservado do destino aceita só os objetos do diário,
/// pelo nome e pelo tipo, e avisa quando o diário ainda não está todo lá; a chave que o diário remove precisa voltar no
/// mesmo estado; e os gatilhos de evento do destino são avisados.
/// </summary>
public sealed class DiarioNoPlanoTestes
{
    private static readonly ObjetoReservado[] _doDiario =
    [
        Reservado("execucao", TipoObjetoReservado.Tabela),
        Reservado("execucao_pkey", TipoObjetoReservado.Indice),
        Reservado("restricao_removida", TipoObjetoReservado.Tabela),
        Reservado("restricao_removida_pkey", TipoObjetoReservado.Indice),
    ];

    private static readonly string[] _eventos = ["auditoria", "zz\u001B[2J"];

    [Fact]
    public async Task O_destino_aceita_os_objetos_do_diario_pelo_nome_e_pelo_tipo_e_a_origem_nenhum()
    {
        var plano = await PlanejarAsync(Coerente(), ComReservado(_doDiario));
        var parte = await PlanejarAsync(Coerente(), ComReservado(_doDiario[0]));

        // O mesmo nome com outro tipo (uma view no lugar da tabela) não é o diário; na origem, nem o diário.
        var outroTipo = Assert.Single(await RecusaAsync(Coerente(), ComReservado(Reservado("execucao", TipoObjetoReservado.Outro))));
        var naOrigem = Assert.Single(await RecusaAsync(ComReservado(_doDiario[0]), ComReservado(_doDiario)));

        Assert.DoesNotContain(plano.Avisos, aviso => aviso.Codigo is "INC3073" or "INC3584");
        Assert.Contains(parte.Avisos, aviso => aviso.Codigo == "INC3584");
        Assert.Equal(("INC3073", "execucao"), (outroTipo.Codigo, outroTipo.Objeto));
        Assert.Contains("no destino", outroTipo.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3073", "execucao"), (naOrigem.Codigo, naOrigem.Objeto));
        Assert.Contains("na origem", naOrigem.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_destino_sem_o_diario_so_avisa_e_o_dialeto_sem_diario_nao_avisa()
    {
        // O planejar roda antes do preparar-destino: sem o diário, avisa (a execução o exige).
        var semDiario = await PlanejarAsync(Coerente(), Coerente());
        var semDeclaracao = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(Coerente(), Coerente()), yaml: Yaml.Minimo);

        var aviso = Assert.Single(semDiario.Avisos, aviso => aviso.Codigo == "INC3584");
        Assert.Contains("rode o preparar-destino", aviso.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(semDeclaracao.Avisos, aviso => aviso.Codigo == "INC3584");
    }

    [Fact]
    public async Task A_chave_que_o_login_nao_restaura_da_INC3077()
    {
        var destino = Trocar(Coerente(), "pedidos", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.ChavesEstrangeiras), new[] { BancoFalso.Clonar(tabela.ChavesEstrangeiras[0], (nameof(ChaveEstrangeira.DesligadaSemRestauracao), true)) })));

        var erro = Assert.Single(await RecusaAsync(Coerente(), destino));

        Assert.Equal(("INC3077", "loja.pedidos", "fk_pedidos_clientes"), (erro.Codigo, erro.Tabela!.ToString(), erro.Objeto));
        Assert.Contains("religue a conferência da chave antes de copiar", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_gatilhos_de_evento_do_destino_sao_avisados_pelo_nome_escapado()
    {
        var comEventos = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.TriggersDeEvento), _eventos));

        var aviso = Assert.Single((await PlanejarAsync(Coerente(), comEventos)).Avisos, aviso => aviso.Codigo == "INC3585");
        var naOrigem = await PlanejarAsync(comEventos, Coerente());

        Assert.Contains("auditoria", aviso.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", aviso.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(naOrigem.Avisos, outro => outro.Codigo == "INC3585");
    }

    private static ObjetoReservado Reservado(string nome, TipoObjetoReservado tipo) => new() { Nome = new NomeObjeto("incognito", nome), Tipo = tipo };

    private static EsquemaBanco ComReservado(params ObjetoReservado[] objetos) => BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosNoEsquemaReservado), objetos));

    private static Task<Plano> PlanejarAsync(EsquemaBanco origem, EsquemaBanco destino) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino) { ObjetosDoDiario = _doDiario }, yaml: Yaml.Minimo);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(EsquemaBanco origem, EsquemaBanco destino) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(origem, destino)), exactMatch: true).Erros;
}
