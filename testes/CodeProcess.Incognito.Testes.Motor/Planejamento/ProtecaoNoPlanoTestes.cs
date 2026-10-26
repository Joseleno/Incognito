using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.DialetoProtegidoTestes;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O que o <c>planejar</c> confere da proteção do destino pela identidade dos dois lados: a origem marcada (ou que não
/// informa) e o marcador ilegível recusam; o <c>bancoEsperado</c> diferente recusa, por comparação exata; o destino ainda sem
/// marcador e a linhagem ou o endereço iguais só avisam, sem os valores lidos.
/// </summary>
public sealed class ProtecaoNoPlanoTestes
{
    private static readonly MarcadorLido _alheio = new() { Situacao = SituacaoMarcador.Alheio };
    private static readonly MarcadorLido _invalido = new() { Situacao = SituacaoMarcador.Invalido };

    [Fact]
    public async Task A_origem_marcada_ou_que_nao_informa_e_recusada_e_a_ilegivel_tambem()
    {
        var casos = new (MarcadorLido? Marcador, string Codigo)[] { (Marcado, "INC3602"), (null, "INC3602"), (_invalido, "INC3601") };

        foreach (var (marcador, codigo) in casos)
        {
            var provedor = Com(origem: Identidade("loja").Com(marcador), destino: Identidade("loja_homolog"));

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(provedor));

            var erro = Assert.Single(recusa.Erros);
            Assert.Equal((codigo, null, null), (erro.Codigo, erro.Tabela, erro.Coluna));
            Assert.Contains("origem", erro.Mensagem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task O_destino_sem_marcador_so_avisa_e_o_ilegivel_recusa()
    {
        foreach (var marcador in new[] { null, Ausente, _alheio })
        {
            var plano = await PlanejarAsync(Com(destino: Identidade("loja_homolog").Com(marcador)));

            var aviso = Assert.Single(plano.Avisos);
            Assert.Equal("INC3583", aviso.Codigo);
            Assert.Contains("preparar-destino", aviso.Mensagem, StringComparison.Ordinal);
        }

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(Com(destino: Identidade("loja_homolog").Com(_invalido))));
        Assert.Equal("INC3601", Assert.Single(recusa.Erros).Codigo);

        // O marcado não avisa nada.
        Assert.Empty((await PlanejarAsync(Com())).Avisos);
    }

    [Fact]
    public async Task O_banco_esperado_diferente_e_recusado_pela_comparacao_exata_com_os_nomes_escapados()
    {
        var sino = ((char)0x07).ToString();
        foreach (var conectado in new[] { "Loja_Homolog", "loja_homolog ", "outro" + sino })
        {
            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(Com(destino: BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), conectado)))));

            var erro = Assert.Single(recusa.Erros);
            Assert.Equal("INC3603", erro.Codigo);
            Assert.Contains("loja_homolog", erro.Mensagem, StringComparison.Ordinal);
            Assert.DoesNotContain(sino, erro.ToString(), StringComparison.Ordinal);
        }

        var escapado = Assert.Single((await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(Com(destino: BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), "outro" + sino)))))).Erros);
        Assert.Contains("outro" + "\\" + "u0007", escapado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_linhagem_ou_o_endereco_iguais_so_avisam_e_sem_os_valores()
    {
        var par = new Dictionary<string, string> { ["cluster"] = "7311111111111111111", ["banco"] = "16384" };
        var casos = new (Dictionary<string, string> Origem, Dictionary<string, string> Destino, string Servidor, string? Esperado)[]
        {
            (par, new(par), "destino-sintetico:5432", "a mesma linhagem (o mesmo banco"),
            (par, new() { ["cluster"] = "7311111111111111111", ["banco"] = "16385" }, "origem-sintetica:5432", "o mesmo endereço"),
            (par, new(par), "origem-sintetica:5432", "a mesma linhagem e o mesmo endereço"),

            // Uma entrada só coincide por acaso entre servidores, e chaves diferentes não se comparam.
            (new() { ["banco"] = "16384" }, new() { ["banco"] = "16384" }, "destino-sintetico:5432", null),
            (par, new() { ["cluster"] = "7311111111111111111", ["outro"] = "16384" }, "destino-sintetico:5432", null),
            (par, new() { ["cluster"] = "7311111111111111111", ["banco"] = "16384", ["mais"] = "1" }, "destino-sintetico:5432", null),
        };

        foreach (var (daOrigem, doDestino, servidor, esperado) in casos)
        {
            var origem = BancoFalso.Clonar(Identidade("loja"), (nameof(IdentidadeLida.Linhagem), daOrigem));
            var destino = BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Linhagem), doDestino), (nameof(IdentidadeLida.Servidor), servidor));

            var plano = await PlanejarAsync(Com(origem, destino));

            if (esperado is null)
            {
                Assert.Empty(plano.Avisos);
                continue;
            }

            var aviso = Assert.Single(plano.Avisos);
            Assert.Equal("INC3582", aviso.Codigo);
            Assert.Contains(esperado, aviso.Mensagem, StringComparison.Ordinal);
            Assert.DoesNotContain("7311111111111111111", aviso.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("16384", aviso.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("5432", aviso.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task O_endereco_vazio_nao_informa_e_nunca_coincide()
    {
        var origem = BancoFalso.Clonar(Identidade("loja"), (nameof(IdentidadeLida.Servidor), ""));
        var destino = BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Servidor), ""));

        Assert.Empty((await PlanejarAsync(Com(origem, destino))).Avisos);
    }

    [Fact]
    public async Task Os_tres_erros_saem_juntos_com_o_plano()
    {
        var provedor = Com(
            origem: Identidade("loja").Com(Marcado),
            destino: BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), "outro"), (nameof(IdentidadeLida.Marcador), _invalido)));

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejarAsync(provedor));

        Assert.Equal(["INC3601", "INC3602", "INC3603"], recusa.Erros.Select(erro => erro.Codigo));
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
    }

    internal static ProvedorDoPlanejar Com(IdentidadeLida? origem = null, IdentidadeLida? destino = null) => new()
    {
        Identidades = lado => lado ? destino ?? Identidade("loja_homolog") : origem ?? Identidade("loja"),
    };
}

/// <summary>A identidade com outro marcador.</summary>
internal static class IdentidadeComMarcador
{
    internal static IdentidadeLida Com(this IdentidadeLida identidade, MarcadorLido? marcador) =>
        BancoFalso.Clonar(identidade, (nameof(IdentidadeLida.Marcador), marcador));
}
