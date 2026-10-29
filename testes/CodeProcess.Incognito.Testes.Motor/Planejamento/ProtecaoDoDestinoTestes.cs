using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.DialetoProtegidoTestes;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.ProtecaoNoPlanoTestes;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// A proteção do destino antes da primeira escrita: o bloqueio vem antes de tudo e, ocupado, sai com código 1 sem mais nada
/// no destino (C3); o destino sem marcador, com o nome errado ou a origem marcada são recusados juntos, com código 2 (C1); a
/// linhagem igual é aceita com aviso (C2); as tabelas ignoradas com linhas recusam, salvo com os dados existentes aceitos; o
/// bloqueio é liberado em toda falha e na confirmação negada; nada é gravado no destino.
/// </summary>
public sealed class ProtecaoDoDestinoTestes
{
    private static readonly NomeObjeto _historico = new("loja", "historico");
    private static readonly NomeObjeto _vazia = new("loja", "vazia");

    [Fact]
    public async Task C1_destino_sem_marcador_banco_errado_e_origem_marcada_sao_recusados_sem_escrever()
    {
        var casos = new (IdentidadeLida Origem, IdentidadeLida Destino, string[] Codigos)[]
        {
            (Identidade("loja"), Identidade("loja_homolog").Com(Ausente), ["INC3600"]),
            (Identidade("loja"), Identidade("loja_homolog").Com(null), ["INC3600"]),
            (Identidade("loja"), Identidade("loja_homolog").Com(new MarcadorLido { Situacao = SituacaoMarcador.Alheio }), ["INC3600"]),
            (Identidade("loja"), BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), "loja")), ["INC3603"]),
            (Identidade("loja").Com(Marcado), Identidade("loja_homolog"), ["INC3602"]),
            (Identidade("loja").Com(null), Identidade("loja_homolog"), ["INC3602"]),
            (Identidade("loja").Com(Marcado), BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), "loja"), (nameof(IdentidadeLida.Marcador), Ausente)), ["INC3600", "INC3602", "INC3603"]),
        };

        foreach (var (origem, destino, codigos) in casos)
        {
            var provedor = Com(origem, destino);
            var confirmacoes = 0;

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ConferirAsync(provedor, [_historico], confirmar: (_, _) =>
            {
                confirmacoes++;
                return Task.FromResult(true);
            }));

            Assert.Equal(codigos, recusa.Erros.Select(erro => erro.Codigo));
            Assert.Equal(2, ClassificadorDeSaida.De(recusa));
            Assert.Equal(0, confirmacoes);
            Assert.True(Assert.Single(provedor.Concessoes).Liberada);
            SemEscrita(provedor);
        }
    }

    [Fact]
    public async Task C2_a_linhagem_igual_e_aceita_com_o_aviso_que_vai_para_a_confirmacao()
    {
        var linhagem = new Dictionary<string, string> { ["cluster"] = "7", ["banco"] = "16384" };
        var provedor = Com(
            BancoFalso.Clonar(Identidade("loja"), (nameof(IdentidadeLida.Linhagem), linhagem)),
            BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Linhagem), new Dictionary<string, string>(linhagem))));
        ResumoDoDestino? resumo = null;

        var resultado = await ConferirAsync(provedor, [], confirmar: (dado, _) =>
        {
            resumo = dado;
            return Task.FromResult(true);
        });

        await using (resultado.Concessao)
        {
            Assert.NotNull(resultado.Concessao);
            Assert.Equal("INC3582", Assert.Single(resultado.Avisos).Codigo);
            Assert.Equal(resultado.Avisos, resumo!.Avisos);
            Assert.False(Assert.Single(provedor.Concessoes).Liberada);
        }

        Assert.True(provedor.Concessoes[0].Liberada);
        SemEscrita(provedor);
    }

    [Fact]
    public async Task C3_com_o_bloqueio_ocupado_sai_com_codigo_1_e_nada_mais_e_pedido_ao_destino()
    {
        var provedor = new ProvedorDoPlanejar { Ocupado = true };
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            var antes = provedor.Chamadas.Count;

            var ocupado = await Assert.ThrowsAsync<BloqueioDestinoException>(() => ProtecaoDoDestino.ConferirAsync(origem, destino, "loja_homolog", [_historico], false, Confirmar, TestContext.Current.CancellationToken));

            Assert.Equal("INC3004", ocupado.Codigo);
            Assert.Equal(1, ClassificadorDeSaida.De(ocupado));
            Assert.Equal(["AdquirirBloqueio:destino"], provedor.Chamadas.Skip(antes));
            Assert.Empty(provedor.Concessoes);
        }
    }

    [Fact]
    public async Task O_bloqueio_vem_antes_da_identidade_e_a_conferencia_le_os_dois_lados()
    {
        var provedor = Com();
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            var antes = provedor.Chamadas.Count;

            var resultado = await ProtecaoDoDestino.ConferirAsync(origem, destino, "loja_homolog", [_historico], false, Confirmar, TestContext.Current.CancellationToken);
            await resultado.Concessao!.DisposeAsync();

            Assert.Equal(
                ["AdquirirBloqueio:destino", "LerIdentidade:origem", "LerIdentidade:destino", "LerDiario:destino", "TemLinhas:destino", "LiberarBloqueio:destino"],
                provedor.Chamadas.Skip(antes));
            Assert.Empty(resultado.Avisos);
        }
    }

    [Fact]
    public async Task O_destino_sem_o_diario_e_recusado_sem_perguntar_nem_ler_as_linhas()
    {
        var provedor = new ProvedorDoPlanejar { EstadoDoDiario = new() { Existe = false }, Linhas = _ => true };
        var confirmacoes = 0;

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ConferirAsync(provedor, [_historico], confirmar: (_, _) =>
        {
            confirmacoes++;
            return Task.FromResult(true);
        }));

        var erro = Assert.Single(recusa.Erros);
        Assert.Equal("INC3607", erro.Codigo);
        Assert.Contains("preparar-destino", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.Equal(0, confirmacoes);
        Assert.DoesNotContain(provedor.Chamadas, chamada => chamada.StartsWith("TemLinhas", StringComparison.Ordinal));
        Assert.True(Assert.Single(provedor.Concessoes).Liberada);
        SemEscrita(provedor);
    }

    [Fact]
    public async Task A_protecao_devolve_o_marcador_do_destino()
    {
        var resultado = await ConferirAsync(new ProvedorDoPlanejar(), []);
        await resultado.Concessao!.DisposeAsync();

        Assert.Equal(Marcado.Identificador, resultado.Marcador);
    }

    [Fact]
    public async Task A_tabela_ignorada_com_linhas_e_recusada_e_aceita_com_os_dados_existentes()
    {
        var provedor = new ProvedorDoPlanejar { Linhas = tabela => tabela.Equals(_historico) };

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ConferirAsync(provedor, [_vazia, _historico]));

        var erro = Assert.Single(recusa.Erros);
        Assert.Equal(("INC3604", "loja.historico"), (erro.Codigo, erro.Tabela?.ToString()));
        Assert.True(Assert.Single(provedor.Concessoes).Liberada);

        var aceitando = new ProvedorDoPlanejar { Linhas = provedor.Linhas };
        var resultado = await ConferirAsync(aceitando, [_vazia, _historico], aceitar: true);
        await resultado.Concessao!.DisposeAsync();
        Assert.DoesNotContain(aceitando.Chamadas, chamada => chamada.StartsWith("TemLinhas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task As_linhas_nao_sao_lidas_no_banco_que_nao_passou_na_identidade()
    {
        foreach (var destino in new[] { Identidade("loja_homolog").Com(Ausente), BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Banco), "producao")) })
        {
            var provedor = new ProvedorDoPlanejar { Identidades = lado => lado ? destino : Identidade("loja"), Linhas = _ => true };

            await Assert.ThrowsAsync<PlanoRecusadoException>(() => ConferirAsync(provedor, [_historico]));

            Assert.DoesNotContain(provedor.Chamadas, chamada => chamada.StartsWith("TemLinhas", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_confirmacao_negada_libera_o_bloqueio_e_devolve_os_avisos_sem_escrever()
    {
        var provedor = Com();

        var resultado = await ConferirAsync(provedor, [_historico], confirmar: (_, _) => Task.FromResult(false));

        Assert.Null(resultado.Concessao);
        Assert.Empty(resultado.Avisos);
        Assert.True(Assert.Single(provedor.Concessoes).Liberada);
        SemEscrita(provedor);
    }

    [Fact]
    public async Task A_confirmacao_recebe_o_servidor_e_o_banco_escapados()
    {
        var sino = ((char)0x07).ToString();
        var destino = BancoFalso.Clonar(Identidade("loja_homolog"), (nameof(IdentidadeLida.Servidor), "destino" + sino + ":5432"));
        ResumoDoDestino? resumo = null;

        var resultado = await ConferirAsync(Com(destino: destino), [], confirmar: (dado, _) =>
        {
            resumo = dado;
            return Task.FromResult(true);
        });
        await resultado.Concessao!.DisposeAsync();

        Assert.Equal(("destino" + "\\" + "u0007:5432", "loja_homolog"), (resumo!.Servidor, resumo.Banco));
    }

    [Fact]
    public async Task O_bloqueio_e_liberado_em_toda_falha_e_o_erro_que_sai_e_o_da_conferencia()
    {
        var casos = new (string Nome, ProvedorDoPlanejar Provedor, Func<ResumoDoDestino, CancellationToken, Task<bool>> Confirmar, Type Esperado)[]
        {
            ("identidade da origem", new ProvedorDoPlanejar { Lancar = Em(Operacao.LerIdentidade, false, () => new ExcecaoDoDriver()) }, Confirmar, typeof(FalhaDialetoException)),
            ("identidade do destino", new ProvedorDoPlanejar { Lancar = Em(Operacao.LerIdentidade, true, () => new ExcecaoDoDriver()) }, Confirmar, typeof(FalhaDialetoException)),
            ("linhas", new ProvedorDoPlanejar { Lancar = Em(Operacao.TemLinhas, true, () => new DialetoRecusouException("INC3115", "recusa com " + ProvedorDoPlanejar.Sentinela)) }, Confirmar, typeof(PlanoRecusadoException)),
            ("confirmação que lança", new ProvedorDoPlanejar(), (_, _) => throw new InvalidOperationException("da confirmação"), typeof(InvalidOperationException)),
            ("confirmação cancelada", new ProvedorDoPlanejar(), (_, _) => Task.FromCanceled<bool>(new CancellationToken(canceled: true)), typeof(TaskCanceledException)),
            ("liberação que falha", new ProvedorDoPlanejar { Lancar = Em(Operacao.LiberarBloqueio, true, () => new ExcecaoDoDriver()) }, (_, _) => Task.FromResult(false), typeof(void)),
        };

        foreach (var (nome, provedor, confirmar, esperado) in casos)
        {
            if (esperado == typeof(void))
            {
                var negado = await ConferirAsync(provedor, [_historico], confirmar: confirmar);
                Assert.Null(negado.Concessao);
            }
            else
            {
                var excecao = await Assert.ThrowsAnyAsync<Exception>(() => ConferirAsync(provedor, [_historico], confirmar: confirmar));
                Assert.True(esperado == excecao.GetType(), $"{nome}: {excecao.GetType().Name}");
                SemVazamento(excecao);
            }

            Assert.True(Assert.Single(provedor.Concessoes).Liberada, nome);
            SemEscrita(provedor);
        }
    }

    [Fact]
    public async Task O_cancelamento_do_chamador_sai_como_cancelamento_e_libera_o_bloqueio()
    {
        using var fonte = new CancellationTokenSource();
        var provedor = new ProvedorDoPlanejar { Lancar = (operacao, _) => operacao == Operacao.LerIdentidade ? Cancelar(fonte) : null };
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            var cancelado = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProtecaoDoDestino.ConferirAsync(origem, destino, null, [], false, Confirmar, fonte.Token));

            Assert.Equal(130, ClassificadorDeSaida.De(cancelado));
            Assert.True(Assert.Single(provedor.Concessoes).Liberada);
        }
    }

    // Grava nada: nem o marcador nem o que o executor gravará.
    internal static void SemEscrita(ProvedorDoPlanejar provedor) =>
        Assert.DoesNotContain(provedor.Chamadas, chamada => chamada.StartsWith("GravarMarcador", StringComparison.Ordinal) || chamada.StartsWith("CriarDiario", StringComparison.Ordinal));

    internal static async Task<(DialetoProtegido Origem, DialetoProtegido Destino)> ConectarAsync(ProvedorDoPlanejar provedor)
    {
        var conexao = new SegredoConexao("Host=servidor;Password=" + ProvedorDoPlanejar.Sentinela);
        var origem = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Origem, conexao, TestContext.Current.CancellationToken);
        var destino = await DialetoProtegido.ConectarAsync(provedor, "postgresql", LadoBanco.Destino, conexao, TestContext.Current.CancellationToken);
        return (origem, destino);
    }

    private static Task<bool> Confirmar(ResumoDoDestino resumo, CancellationToken cancelamento) => Task.FromResult(true);

    private static OperationCanceledException Cancelar(CancellationTokenSource fonte)
    {
        fonte.Cancel();
        return new OperationCanceledException(fonte.Token);
    }

    private static async Task<ResultadoDaProtecao> ConferirAsync(
        ProvedorDoPlanejar provedor,
        NomeObjeto[] ignoradas,
        bool aceitar = false,
        Func<ResumoDoDestino, CancellationToken, Task<bool>>? confirmar = null)
    {
        var (origem, destino) = await ConectarAsync(provedor);
        await using (origem)
        await using (destino)
        {
            return await ProtecaoDoDestino.ConferirAsync(origem, destino, "loja_homolog", ignoradas, aceitar, confirmar ?? Confirmar, TestContext.Current.CancellationToken);
        }
    }
}
