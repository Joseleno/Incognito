using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// O diário de execução: cada regra do contrato (<see cref="DiarioExecucao"/>) num banco próprio, de outro dono, com o
/// login do destino dono só das tabelas.
/// </summary>
public abstract partial class ConformidadeDialetoTestes<TFixture>
{
    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_diario_e_criado_e_lido_por_outra_instancia()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        var concessao = await AdquirirAsync(banco.Destino);
        await using (concessao)
        {
            var antes = await banco.Destino.Diario.LerAsync(cancelamento);
            Assert.True(!antes.Existe && antes.Execucoes.Count == 0 && antes.Pendentes.Count == 0, $"Diário: lido num banco sem diário como {antes}.");

            await banco.Destino.Diario.CriarAsync(cancelamento);
            await banco.Destino.Diario.CriarAsync(cancelamento);
            var criado = await banco.Destino.Diario.LerAsync(cancelamento);
            Assert.True(criado.Existe && criado.Execucoes.Count == 0 && criado.Pendentes.Count == 0, $"Diário: lido depois de criado duas vezes como {criado}.");

            // O que o planejar aceita no destino é exatamente o que a criação deixou no esquema reservado.
            var declarados = banco.Destino.Diario.ObjetosDoDiario;
            var lidos = (await banco.Destino.Esquema.LerAsync(cancelamento)).ObjetosNoEsquemaReservado ?? [];
            Assert.True(
                declarados.Count > 0 && lidos.ToHashSet().SetEquals(declarados),
                $"Diário: o que a criação deixou no esquema reservado [{string.Join(", ", lidos)}] não é o que o diário declara [{string.Join(", ", declarados)}].");
        }

        await using var outro = await Fixtura.ConectarOutroAsync(banco, cancelamento);
        await using var daOutra = await AdquirirAsync(outro);
        var relido = await outro.Diario.LerAsync(cancelamento);
        Assert.True(relido.Existe, "Diário: outra instância não leu o diário criado.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_diario_so_trabalha_com_a_concessao_viva()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        var semConcessao = await Record.ExceptionAsync(() => banco.Destino.Diario.CriarAsync(cancelamento));
        Assert.True(semConcessao is InvalidOperationException, $"Diário: a criação sem a concessão viva {(semConcessao is null ? "passou" : $"lançou {semConcessao.GetType().Name}")}, e não InvalidOperationException.");

        var concessao = await AdquirirAsync(banco.Destino);
        await using (concessao)
        {
            await banco.Destino.Diario.CriarAsync(cancelamento);
            await Fixtura.DerrubarBloqueioAsync(banco, cancelamento);

            var depoisDaQueda = await Record.ExceptionAsync(() => banco.Destino.Diario.LerAsync(cancelamento));
            Assert.True(depoisDaQueda is not null, "Diário: lido depois da queda da conexão do bloqueio.");
            Assert.True(concessao.Perdido.IsCancellationRequested, "Diário: a operação recusada pela queda do bloqueio não marcou a perda.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_execucao_e_registrada_e_encerrada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var diario = banco.Destino.Diario;
        var (primeira, segunda, marcador, inicio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AgoraEmSegundos());

        var semDiario = await Record.ExceptionAsync(() => diario.IniciarAsync(primeira, marcador, inicio, cancelamento));
        Assert.True(semDiario is DialetoRecusouException, $"Diário: a execução sem o diário {Descrever(semDiario)}, e não foi recusada.");

        await diario.CriarAsync(cancelamento);
        await diario.IniciarAsync(primeira, marcador, inicio, cancelamento);
        var lido = Assert.Single((await diario.LerAsync(cancelamento)).Execucoes);
        Assert.True(
            lido.Id == primeira && lido.Marcador == marcador && lido.IniciadaEm == inicio && lido.Situacao == SituacaoExecucao.EmAndamento,
            $"Diário: a execução registrada lida como {lido.Id} {lido.Marcador} {lido.IniciadaEm:O} {lido.Situacao}.");

        var dupla = await Record.ExceptionAsync(() => diario.IniciarAsync(segunda, marcador, inicio, cancelamento));
        Assert.True(dupla is DialetoRecusouException, $"Diário: a segunda execução em andamento {Descrever(dupla)}, e não foi recusada.");

        await diario.EncerrarAsync(primeira, SituacaoExecucao.Concluida, inicio.AddSeconds(1), cancelamento);
        var encerrada = await diario.LerAsync(cancelamento);
        Assert.True(encerrada.Execucoes.Count == 0, $"Diário: a execução concluída sem entrada pendente continua lida ({encerrada}).");
        var denovo = await Record.ExceptionAsync(() => diario.EncerrarAsync(primeira, SituacaoExecucao.Falhou, inicio.AddSeconds(2), cancelamento));
        Assert.True(denovo is DialetoRecusouException, $"Diário: a execução encerrada foi encerrada de novo ({Descrever(denovo)}).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_removida_volta_no_mesmo_estado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, execucao) = await IniciarAsync(banco);
        Assert.True(Esperado.Diario.Chaves.Count > 0, "Diário: a fixture precisa declarar as chaves do banco do diário.");

        foreach (var caso in Esperado.Diario.Chaves)
        {
            var nome = $"{caso.Tabela}.{caso.Nome}";
            var antes = await Fixtura.LerChaveAsync(banco, caso.Tabela, caso.Nome, cancelamento);
            Assert.True(antes is not null, $"Diário: a chave {nome} declarada não existe no banco do diário.");

            await diario.RemoverChaveAsync(execucao, caso.Tabela, caso.Nome, cancelamento);
            Assert.True(await Fixtura.LerChaveAsync(banco, caso.Tabela, caso.Nome, cancelamento) is null, $"Diário: a chave {nome} continuou no banco depois de removida.");
            var pendente = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
            Assert.True(
                pendente.Tipo == TipoEntrada.ChaveEstrangeira && pendente.Tabela.Equals(caso.Tabela) && pendente.Nome == caso.Nome && pendente.Situacao == SituacaoEntrada.Removida && pendente.Execucao == execucao,
                $"Diário: a chave {nome} removida ficou no diário como {pendente.Tipo} {pendente.Tabela}.{pendente.Nome} {pendente.Situacao}.");

            var recriada = await diario.RecriarChaveAsync(pendente, cancelamento);
            Assert.True(recriada == caso.NaRecriacao, $"Diário: a recriação da chave {nome} devolveu {Texto(recriada)}, esperada {Texto(caso.NaRecriacao)}.");
            if (recriada == SituacaoEntrada.SemValidacao)
            {
                var validada = await diario.ValidarChaveAsync(Assert.Single((await diario.LerAsync(cancelamento)).Pendentes), cancelamento);
                Assert.True(validada is null, $"Diário: a validação da chave {nome} sem órfão devolveu {Texto(validada)}.");
            }

            var depois = await Fixtura.LerChaveAsync(banco, caso.Tabela, caso.Nome, cancelamento);
            Assert.True(depois == antes, $"Diário: a chave {nome} voltou como [{depois}], era [{antes}].");
            Assert.True((await diario.LerAsync(cancelamento)).Pendentes.Count == 0, $"Diário: a chave {nome} restaurada continuou no diário.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_triggers_voltam_no_estado_exato()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, execucao) = await IniciarAsync(banco);
        Assert.True(Esperado.Diario.ComTriggers.Count > 0, "Diário: a fixture precisa declarar as tabelas com triggers do banco do diário.");

        foreach (var tabela in Esperado.Diario.ComTriggers)
        {
            var antes = await Fixtura.LerTriggersAsync(banco, tabela, cancelamento);
            await diario.DesligarTriggersAsync(execucao, tabela, cancelamento);
            var desligadas = await Fixtura.LerTriggersAsync(banco, tabela, cancelamento);
            Assert.True(desligadas != antes, $"Diário: as triggers de {tabela} não foram desligadas ({antes}).");
            var pendente = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
            Assert.True(
                pendente.Tipo == TipoEntrada.Triggers && pendente.Tabela.Equals(tabela) && pendente.Nome is null,
                $"Diário: as triggers desligadas de {tabela} ficaram no diário como {pendente.Tipo} {pendente.Tabela}.");

            var puladas = await diario.ReligarTriggersAsync(pendente, cancelamento);
            var depois = await Fixtura.LerTriggersAsync(banco, tabela, cancelamento);
            Assert.True(puladas.Count == 0, $"Diário: triggers de {tabela} puladas sem terem sumido.");
            Assert.True(depois == antes, $"Diário: as triggers de {tabela} voltaram como [{depois}], eram [{antes}].");
            Assert.True((await diario.LerAsync(cancelamento)).Pendentes.Count == 0, $"Diário: as triggers religadas de {tabela} continuaram no diário.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_falha_no_meio_nao_deixa_metade()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, execucao) = await IniciarAsync(banco);
        var chave = Esperado.Diario.ComOutraDefinicao;
        var tabela = Esperado.Diario.ComTriggers[0];
        var chaveAntes = await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento);
        var triggersAntes = await Fixtura.LerTriggersAsync(banco, tabela, cancelamento);

        // A alteração feita e desfeita no fim do comando: nem o registro sem a alteração, nem a alteração sem o registro.
        await using (await Fixtura.FalharNoFimDoDdlAsync(banco, cancelamento))
        {
            Assert.True(await Record.ExceptionAsync(() => diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento)) is not null, "Diário: a remoção passou com a alteração falhando no fim.");
            Assert.True(await Record.ExceptionAsync(() => diario.DesligarTriggersAsync(execucao, tabela, cancelamento)) is not null, "Diário: o desligamento passou com a alteração falhando no fim.");
        }

        var semMetade = await diario.LerAsync(cancelamento);
        Assert.True(semMetade.Pendentes.Count == 0, $"Diário: a operação que falhou deixou {semMetade.Pendentes.Count} entrada(s).");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) == chaveAntes, "Diário: a remoção que falhou mudou a chave.");
        Assert.True(await Fixtura.LerTriggersAsync(banco, tabela, cancelamento) == triggersAntes, "Diário: o desligamento que falhou mudou as triggers.");

        // Outra sessão segurando a tabela: a operação desiste sem metade, em vez de esperar sem fim.
        await using (await Fixtura.SegurarAsync(banco, chave.Tabela, cancelamento))
        {
            Assert.True(await Record.ExceptionAsync(() => diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento)) is not null, "Diário: a remoção passou com a tabela segura por outra sessão.");
        }

        Assert.True((await diario.LerAsync(cancelamento)).Pendentes.Count == 0, "Diário: a remoção que esperou demais deixou entrada.");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) == chaveAntes, "Diário: a remoção que esperou demais mudou a chave.");

        // A recriação que falha no fim deixa a chave fora e a entrada como estava.
        await diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento);
        var entrada = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        await using (await Fixtura.FalharNoFimDoDdlAsync(banco, cancelamento))
        {
            Assert.True(await Record.ExceptionAsync(() => diario.RecriarChaveAsync(entrada, cancelamento)) is not null, "Diário: a recriação passou com a alteração falhando no fim.");
        }

        var depois = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        Assert.True(depois.Situacao == SituacaoEntrada.Removida, $"Diário: a recriação que falhou deixou a entrada {depois.Situacao}.");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) is null, "Diário: a recriação que falhou deixou a chave no banco.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_com_orfao_fica_no_diario_e_a_execucao_seguinte_a_adota()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, primeira) = await IniciarAsync(banco);
        var chave = Esperado.Diario.ComOrfao;
        Assert.True(chave.NaRecriacao == SituacaoEntrada.SemValidacao, "Diário: a chave do órfão precisa ser validada numa tabela comum.");
        await Fixtura.CriarOrfaoAsync(banco, SentinelaDoDiario, cancelamento);

        // O órfão nunca é exceção (a mensagem do banco traz o valor): a entrada fica, sem validação.
        await diario.RemoverChaveAsync(primeira, chave.Tabela, chave.Nome, cancelamento);
        var recriada = await diario.RecriarChaveAsync(Assert.Single((await diario.LerAsync(cancelamento)).Pendentes), cancelamento);
        Assert.True(recriada == SituacaoEntrada.SemValidacao, $"Diário: a recriação da chave com órfão devolveu {Texto(recriada)}.");
        var validada = await diario.ValidarChaveAsync(Assert.Single((await diario.LerAsync(cancelamento)).Pendentes), cancelamento);
        Assert.True(validada == SituacaoEntrada.SemValidacao, $"Diário: a chave com órfão validou ({Texto(validada)}).");
        var pendente = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        Assert.True(pendente.Situacao == SituacaoEntrada.SemValidacao, $"Diário: a chave com órfão ficou no diário como {pendente.Situacao}.");

        // A execução seguinte adota a entrada: remove a chave de novo, com a definição original.
        await diario.EncerrarAsync(primeira, SituacaoExecucao.Falhou, AgoraEmSegundos(), cancelamento);
        var segunda = Guid.NewGuid();
        await diario.IniciarAsync(segunda, Guid.NewGuid(), AgoraEmSegundos(), cancelamento);
        var adocao = await Record.ExceptionAsync(() => diario.RemoverChaveAsync(segunda, chave.Tabela, chave.Nome, cancelamento));
        Assert.True(adocao is null, $"Diário: a execução seguinte não adotou a chave pendente ({adocao?.GetType().Name}).");
        var adotada = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        Assert.True(adotada.Execucao == segunda && adotada.Situacao == SituacaoEntrada.Removida, $"Diário: a chave adotada ficou no diário como {adotada.Situacao}, da execução {(adotada.Execucao == segunda ? "nova" : "anterior")}.");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) is null, "Diário: a chave adotada continuou no banco.");

        // O descarte tira a entrada sem restaurar.
        await diario.DescartarAsync(adotada, cancelamento);
        Assert.True((await diario.LerAsync(cancelamento)).Pendentes.Count == 0, "Diário: a entrada descartada continuou no diário.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_restauracao_e_idempotente()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, execucao) = await IniciarAsync(banco);
        var chave = Esperado.Diario.ComOutraDefinicao;
        var tabela = Esperado.Diario.ComTriggers[0];
        var chaveAntes = await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento);
        var triggersAntes = await Fixtura.LerTriggersAsync(banco, tabela, cancelamento);

        // A entrada de antes de uma queda chamada de novo: o resultado não muda.
        await diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento);
        var entrada = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        var primeira = await diario.RecriarChaveAsync(entrada, cancelamento);
        Assert.True(primeira == chave.NaRecriacao, $"Diário: a primeira recriação devolveu {Texto(primeira)}, esperada {Texto(chave.NaRecriacao)}.");
        var segunda = await diario.RecriarChaveAsync(entrada, cancelamento);
        Assert.True(segunda == primeira, $"Diário: a segunda recriação devolveu {Texto(segunda)}, a primeira {Texto(primeira)}.");
        var semValidacao = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        Assert.True(await diario.ValidarChaveAsync(semValidacao, cancelamento) is null, "Diário: a validação depois da recriação repetida não passou.");
        Assert.True(await diario.ValidarChaveAsync(semValidacao, cancelamento) is null, "Diário: a validação repetida devolveu uma situação.");
        Assert.True(await diario.RecriarChaveAsync(entrada, cancelamento) is null, "Diário: a recriação de uma entrada que já saiu devolveu uma situação.");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) == chaveAntes, "Diário: a restauração repetida mudou a chave.");

        await diario.DesligarTriggersAsync(execucao, tabela, cancelamento);
        var triggers = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);
        await diario.ReligarTriggersAsync(triggers, cancelamento);
        var repetida = await Record.ExceptionAsync(() => diario.ReligarTriggersAsync(triggers, cancelamento));
        Assert.True(repetida is null, $"Diário: religar de novo as triggers {Descrever(repetida)}.");
        Assert.True(await Fixtura.LerTriggersAsync(banco, tabela, cancelamento) == triggersAntes, "Diário: religar de novo mudou as triggers.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_com_outra_definicao_trava_so_a_sua_entrada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
        await using var concessao = await AdquirirAsync(banco.Destino);
        var (diario, execucao) = await IniciarAsync(banco);
        var chave = Esperado.Diario.ComOutraDefinicao;

        await diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento);
        await Fixtura.RecriarComOutraDefinicaoAsync(banco, chave.Tabela, chave.Nome, cancelamento);
        var outra = await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento);
        var entrada = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);

        var recusa = await Record.ExceptionAsync(() => diario.RecriarChaveAsync(entrada, cancelamento));
        Assert.True(recusa is DialetoRecusouException, $"Diário: a recriação sobre uma chave de outra definição {Descrever(recusa)}, e não foi recusada.");
        Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) == outra, "Diário: a recriação recusada trocou a chave de outra definição.");

        await diario.DescartarAsync(entrada, cancelamento);
        Assert.True((await diario.LerAsync(cancelamento)).Pendentes.Count == 0, "Diário: a entrada descartada continuou no diário.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_diario_adulterado_e_recusado_sem_executar()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var chave = Esperado.Diario.ComOutraDefinicao;
        foreach (var adulteracao in Enum.GetValues<Adulteracao>())
        {
            await using var banco = await Fixtura.CriarBancoDoDiarioAsync(cancelamento);
            await using var concessao = await AdquirirAsync(banco.Destino);
            var (diario, execucao) = await IniciarAsync(banco);
            await diario.RemoverChaveAsync(execucao, chave.Tabela, chave.Nome, cancelamento);
            var entrada = Assert.Single((await diario.LerAsync(cancelamento)).Pendentes);

            await Fixtura.AdulterarAsync(banco, adulteracao, SentinelaDoDiario, cancelamento);
            var recusa = await Record.ExceptionAsync(() => diario.RecriarChaveAsync(entrada, cancelamento));
            Assert.True(recusa is DialetoRecusouException, $"Diário: a recriação com o diário adulterado ({adulteracao}) {Descrever(recusa)}, e não foi recusada.");
            Assert.True(!recusa.Message.Contains(SentinelaDoDiario, StringComparison.OrdinalIgnoreCase), $"Diário: a recusa do diário adulterado ({adulteracao}) trouxe o que estava nele.");
            Assert.True(await Fixtura.LerChaveAsync(banco, chave.Tabela, chave.Nome, cancelamento) is null, $"Diário: a recriação com o diário adulterado ({adulteracao}) mexeu na chave.");
        }
    }

    // O sentinela declarado (a declaração vazia é pega pelo fato do sentinela; aqui, um de reserva).
    private string SentinelaDoDiario => Esperado.Sentinelas is [{ Length: > 0 } primeiro, ..] ? primeiro : "zqDIARIOqz";

    private static string Texto(SituacaoEntrada? situacao) => situacao?.ToString() ?? "nula (o estado voltou)";

    private static string Descrever(Exception? excecao) => excecao is null ? "passou" : $"lançou {excecao.GetType().Name}";

    private static async Task<ConcessaoBloqueio> AdquirirAsync(DialetoDestino destino)
    {
        var concessao = await destino.Bloqueio.TentarAdquirirAsync(TestContext.Current.CancellationToken);
        Assert.True(concessao is not null, "Diário: o banco do diário não deu o bloqueio livre.");
        return concessao;
    }

    // O diário criado e uma execução em andamento.
    private static async Task<(DiarioExecucao Diario, Guid Execucao)> IniciarAsync(BancoDoDiario banco)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var execucao = Guid.NewGuid();
        await banco.Destino.Diario.CriarAsync(cancelamento);
        await banco.Destino.Diario.IniciarAsync(execucao, Guid.NewGuid(), AgoraEmSegundos(), cancelamento);
        return (banco.Destino.Diario, execucao);
    }
}
