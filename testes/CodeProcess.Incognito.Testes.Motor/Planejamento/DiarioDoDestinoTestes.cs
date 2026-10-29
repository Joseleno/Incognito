using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O diário no motor contra o banco do diário falso (o mesmo da suíte): a queda em qualquer escrita do preparo, da
/// restauração e do reparo é reparada sem perder uma chave nem o estado de uma trigger (C1); a chave com órfão fica no
/// diário, sai com 4 e é adotada pela execução seguinte (C2); uma entrada que não se recria trava só ela e o
/// <c>--descartar</c> a apaga; o diário de outro marcador não é executado.
/// </summary>
public sealed class DiarioDoDestinoTestes
{
    private static readonly ProvedorFalso _provedor = new(completo: true, Defeito.Nenhum, perturbar: null);

    private static readonly TimeProvider _relogio = new RelogioFixo(new DateTimeOffset(2026, 10, 28, 9, 30, 15, 250, TimeSpan.Zero));

    // As chaves e as raízes com trigger do banco do diário falso, como a carga as levaria.
    private static readonly ChaveRemovida[] _chaves =
    [
        Chave("compras", "fk_compras_lojas"),
        Chave("pedidos", "fk_pedidos_lojas"),
        Chave("vendas", "fk_vendas_codigo"),
        Chave("vendas", "fk_vendas_lojas"),
        Chave("vendas", "fk_vendas_par"),
        Chave("vendas", "fk_vendas_pendente"),
        Chave("vinculo", "fk_vinculo_solto"),
    ];

    private static readonly NomeObjeto[] _triggers = [BancoFalso.Nome("disparos"), BancoFalso.Nome("pedidos"), BancoFalso.Nome("pessoa")];

    [Fact]
    public async Task A_execucao_sem_queda_devolve_o_banco_exato_e_encerra()
    {
        await using var banco = Banco.Criar();
        var original = banco.Estado.Retrato();

        var resultado = await ExecutarAsync(banco);

        Assert.Equal(0, ClassificadorDeSaida.De(resultado));
        Assert.Equal(original, banco.Estado.Retrato());
        Assert.Empty(banco.Estado.Entradas);
        Assert.Equal(SituacaoExecucao.Concluida, Assert.Single(banco.Estado.Execucoes).Situacao);
        Assert.Equal(banco.Marcador, banco.Estado.Execucoes[0].Marcador);
    }

    [Fact]
    public async Task C1_a_queda_em_qualquer_escrita_da_execucao_e_reparada_sem_perder_nada()
    {
        var escritas = await EscritasDeUmaExecucaoAsync();
        Assert.True(escritas > 20, $"A execução escreveu só {escritas} vezes no diário.");

        foreach (var depoisDoCommit in new[] { false, true })
        {
            for (var queda = 1; queda <= escritas; queda++)
            {
                // Pelo comando reparar, e depois uma execução nova (o executar idempotente).
                await using (var banco = Banco.Criar())
                {
                    var original = banco.Estado.Retrato();
                    await CairAsync(banco, queda, depoisDoCommit);

                    var reparo = await RepararAsync(banco);
                    var onde = $"queda na escrita {queda} ({(depoisDoCommit ? "depois" : "antes")} do commit)";
                    Assert.True(ClassificadorDeSaida.De(reparo) == 0, $"{onde}: o reparo saiu com {ClassificadorDeSaida.De(reparo)} ({reparo}).");
                    Assert.True(original == banco.Estado.Retrato(), $"{onde}: o reparo deixou [{banco.Estado.Retrato()}].");
                    Limpo(banco, onde);
                    Assert.Equal(0, ClassificadorDeSaida.De(await RepararAsync(banco)));

                    Assert.Equal(0, ClassificadorDeSaida.De(await ExecutarAsync(banco)));
                    Assert.True(original == banco.Estado.Retrato(), $"{onde}: a execução seguinte deixou [{banco.Estado.Retrato()}].");
                    Limpo(banco, onde);
                }

                // Direto pela execução seguinte, que repara sem validar e adota as chaves pendentes.
                await using (var banco = Banco.Criar())
                {
                    var original = banco.Estado.Retrato();
                    await CairAsync(banco, queda, depoisDoCommit);

                    var onde = $"queda na escrita {queda} ({(depoisDoCommit ? "depois" : "antes")} do commit), executando de novo";
                    Assert.Equal(0, ClassificadorDeSaida.De(await ExecutarAsync(banco)));
                    Assert.True(original == banco.Estado.Retrato(), $"{onde}: deixou [{banco.Estado.Retrato()}].");
                    Limpo(banco, onde);
                }
            }
        }
    }

    [Fact]
    public async Task C1_a_queda_no_meio_do_reparo_e_reparada_pelo_reparo_seguinte()
    {
        var escritas = await EscritasDeUmaExecucaoAsync();
        foreach (var depoisDoCommit in new[] { false, true })
        {
            for (var queda = 1; queda <= escritas; queda++)
            {
                // As escritas do reparo depois desta queda, contadas num banco igual.
                int doReparo;
                await using (var medida = Banco.Criar())
                {
                    await CairAsync(medida, queda, depoisDoCommit);
                    var antes = medida.Estado.Escritas;
                    await RepararAsync(medida);
                    doReparo = medida.Estado.Escritas - antes;
                }

                for (var noReparo = 1; noReparo <= doReparo; noReparo++)
                {
                    await using var banco = Banco.Criar();
                    var original = banco.Estado.Retrato();
                    await CairAsync(banco, queda, depoisDoCommit);
                    banco.Estado.QuedaNaEscrita = banco.Estado.Escritas + noReparo;
                    banco.Estado.QuedaDepoisDoCommit = depoisDoCommit;
                    await Assert.ThrowsAnyAsync<Exception>(() => RepararAsync(banco));
                    Reviver(banco);

                    var onde = $"queda na escrita {queda} e na {noReparo} do reparo ({(depoisDoCommit ? "depois" : "antes")} do commit)";
                    var reparo = await RepararAsync(banco);
                    Assert.True(ClassificadorDeSaida.De(reparo) == 0, $"{onde}: o reparo saiu com {ClassificadorDeSaida.De(reparo)}.");
                    Assert.True(original == banco.Estado.Retrato(), $"{onde}: deixou [{banco.Estado.Retrato()}].");
                    Limpo(banco, onde);
                }
            }
        }
    }

    [Fact]
    public async Task C2_a_chave_com_orfao_fica_no_diario_sai_com_4_e_a_execucao_seguinte_a_adota()
    {
        await using var banco = Banco.Criar();
        var original = banco.Estado.Retrato();
        var codigo = (BancoFalso.Nome("vendas"), "fk_vendas_codigo");
        banco.Estado.ComOrfao.Add(codigo);

        var primeira = await ExecutarAsync(banco);
        var pelaReparacao = await RepararAsync(banco);
        var segunda = await ExecutarAsync(banco);

        var pendente = Assert.Single(primeira.Pendentes);
        Assert.Equal((codigo.Item1, codigo.Item2, SituacaoEntrada.SemValidacao), (pendente.Tabela, pendente.Nome, pendente.Situacao));
        Assert.Equal(4, ClassificadorDeSaida.De(primeira));
        Assert.Equal(4, ClassificadorDeSaida.De(pelaReparacao));
        Assert.Equal(4, ClassificadorDeSaida.De(segunda));

        // A entrada passa à última execução (o reparar não registra execução), com a definição original.
        var entrada = Assert.Single(banco.Estado.Entradas);
        Assert.Equal(2, banco.Estado.Execucoes.Count);
        Assert.Equal(banco.Estado.Execucoes[^1].Id, entrada.Value.Execucao);

        banco.Estado.ComOrfao.Clear();
        var terceira = await ExecutarAsync(banco);

        Assert.Equal(0, ClassificadorDeSaida.De(terceira));
        Assert.Equal(original, banco.Estado.Retrato());
        Assert.Empty(banco.Estado.Entradas);
    }

    [Fact]
    public async Task A_entrada_que_nao_se_recria_trava_so_ela_e_o_descartar_a_apaga_depois_da_confirmacao()
    {
        await using var banco = Banco.Criar();
        var original = banco.Estado.Retrato();
        await CairAsync(banco, 9, depoisDoCommit: false);
        var compras = (BancoFalso.Nome("compras"), "fk_compras_lojas");
        banco.Estado.Chaves[compras] = new ChaveFalsa("chave (loja) de loja.outras (id)", Validada: true, Imposta: true);

        var semDescartar = await RepararAsync(banco);
        var negado = await RepararAsync(banco, (_, _) => Task.FromResult(false));
        var perguntadas = new List<EntradaNaoRestaurada>();
        var descartado = await RepararAsync(banco, (falha, _) =>
        {
            perguntadas.Add(falha);
            return Task.FromResult(true);
        });

        var falha = Assert.Single(semDescartar.Restauracao.Falhas);
        Assert.Equal((compras.Item1, compras.Item2, "INC3399"), (falha.Entrada.Tabela, falha.Entrada.Nome, falha.Codigo));
        Assert.Equal(1, ClassificadorDeSaida.De(semDescartar));
        Assert.Equal(1, ClassificadorDeSaida.De(negado));
        Assert.Empty(negado.Descartadas);

        Assert.Equal(compras.Item2, Assert.Single(perguntadas).Entrada.Nome);
        Assert.Equal(compras.Item2, Assert.Single(descartado.Descartadas).Nome);
        Assert.Equal(0, ClassificadorDeSaida.De(descartado));
        Assert.Empty(banco.Estado.Entradas);

        // O resto voltou: só a chave de outra definição ficou no lugar da original.
        Assert.Equal(original.Replace("chave (loja) de loja.lojas (id) apagar restringe", "chave (loja) de loja.outras (id)", StringComparison.Ordinal), banco.Estado.Retrato());
    }

    [Fact]
    public async Task O_diario_de_outro_marcador_e_recusado_sem_executar_nada()
    {
        await using var banco = Banco.Criar();
        await CairAsync(banco, 9, depoisDoCommit: false);
        var retrato = banco.Estado.Retrato();
        var escritas = banco.Estado.Escritas;
        BancoFalso.Estado(banco.Nome).Comentario = EstadoDoBancoFalso.Texto(Guid.NewGuid(), new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => RepararAsync(banco));

        Assert.Equal("INC3608", Assert.Single(recusa.Erros).Codigo);
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.Equal((retrato, escritas), (banco.Estado.Retrato(), banco.Estado.Escritas));
        Assert.True(BancoFalso.Estado(banco.Nome).Livre, "O bloqueio ficou preso depois da recusa.");
    }

    [Fact]
    public async Task O_reparar_sem_o_marcador_valido_e_recusado_e_sem_o_diario_nao_faz_nada()
    {
        await using var semMarcador = Banco.Criar();
        BancoFalso.Estado(semMarcador.Nome).Comentario = null;
        await using var semDiario = Banco.Criar();
        lock (semDiario.Estado.Trava)
        {
            semDiario.Estado.Criado = false;
        }

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => RepararAsync(semMarcador));
        var vazio = await RepararAsync(semDiario);

        Assert.Equal("INC3600", Assert.Single(recusa.Erros).Codigo);
        Assert.Equal(0, semMarcador.Estado.Escritas);
        Assert.Same(ResultadoDoReparo.Vazio, vazio);
        Assert.Equal(0, semDiario.Estado.Escritas);
    }

    // As escritas de uma execução inteira, sem queda.
    private static async Task<int> EscritasDeUmaExecucaoAsync()
    {
        await using var banco = Banco.Criar();
        await ExecutarAsync(banco);
        return banco.Estado.Escritas;
    }

    // O executar (INC-038) sem a cópia: reparo sem validar, preparo e restauração, com a concessão do começo ao fim.
    private static async Task<ResultadoDaRestauracao> ExecutarAsync(Banco banco)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var destino = await banco.ConectarAsync();
        await using var concessao = (await destino.TentarAdquirirBloqueioAsync(cancelamento))!;
        var reparo = await ReparadorDoDestino.RepararAsync(destino, concessao, banco.Marcador, new OpcoesDoReparo(Validar: false), _relogio, cancelamento);
        Assert.Empty(reparo.Restauracao.Falhas);
        var execucao = await DiarioDoDestino.PrepararAsync(destino, concessao, banco.Marcador, _chaves, _triggers, _relogio, cancelamento);
        return await DiarioDoDestino.RestaurarAsync(destino, concessao, execucao, _relogio, cancelamento);
    }

    private static async Task<ResultadoDoReparo> RepararAsync(Banco banco, Func<EntradaNaoRestaurada, CancellationToken, Task<bool>>? descartar = null)
    {
        await using var destino = await banco.ConectarAsync();
        return await ReparadorDoDestino.RepararComBloqueioAsync(destino, bancoEsperado: null, new OpcoesDoReparo(Validar: true, descartar), _relogio, TestContext.Current.CancellationToken);
    }

    // Uma execução que cai na escrita dada: o processo some (nada mais escreve, o bloqueio se solta), e o banco volta.
    private static async Task CairAsync(Banco banco, int queda, bool depoisDoCommit)
    {
        banco.Estado.QuedaNaEscrita = banco.Estado.Escritas + queda;
        banco.Estado.QuedaDepoisDoCommit = depoisDoCommit;
        await Assert.ThrowsAnyAsync<Exception>(() => ExecutarAsync(banco));
        Assert.True(banco.Estado.Caido, $"A execução terminou sem chegar à escrita {queda}.");
        Reviver(banco);
    }

    private static void Reviver(Banco banco)
    {
        lock (banco.Estado.Trava)
        {
            (banco.Estado.Caido, banco.Estado.QuedaNaEscrita) = (false, null);
        }

        Assert.True(BancoFalso.Estado(banco.Nome).Livre, "O bloqueio ficou preso depois da queda.");
    }

    // Nada no diário nem em andamento.
    private static void Limpo(Banco banco, string onde)
    {
        Assert.True(banco.Estado.Entradas.Count == 0, $"{onde}: ficaram {banco.Estado.Entradas.Count} entradas no diário.");
        Assert.True(banco.Estado.Execucoes.All(execucao => execucao.Situacao != SituacaoExecucao.EmAndamento), $"{onde}: ficou execução em andamento.");
    }

    private static ChaveRemovida Chave(string tabela, string nome) => new(new NomeTabela("loja", tabela), nome);

    /// <summary>Um banco do diário falso, criado e marcado.</summary>
    private sealed class Banco : IAsyncDisposable
    {
        private Banco(string nome, Guid marcador)
        {
            Nome = nome;
            Marcador = marcador;
            Estado = BancoFalso.Diario(nome);
        }

        internal string Nome { get; }

        internal Guid Marcador { get; }

        internal EstadoDoDiarioFalso Estado { get; }

        internal static Banco Criar()
        {
            var banco = new Banco(ProvedorFalso.BancoDoDiario + Guid.NewGuid().ToString("N"), Guid.NewGuid());
            lock (banco.Estado.Trava)
            {
                banco.Estado.Criado = true;
            }

            BancoFalso.Estado(banco.Nome).Comentario = EstadoDoBancoFalso.Texto(banco.Marcador, new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
            return banco;
        }

        internal Task<DialetoProtegido> ConectarAsync() =>
            DialetoProtegido.ConectarAsync(_provedor, "falso", LadoBanco.Destino, new SegredoConexao(Nome), TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync()
        {
            BancoFalso.Esquecer(Nome);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Um relógio parado.</summary>
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
