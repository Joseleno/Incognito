using System.Diagnostics;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A gravação no destino (<see cref="DialetoDestino.Gravador"/>), com lotes lidos da origem pela sessão: o conteúdo
/// substituído, as colunas não escritas e as mantidas como vieram, o valor lido escrito de volta que devolve a tabela igual,
/// os valores escritos na forma canônica, a gravação desfeita e a cancelada no meio, no tempo, o lote de outro dialeto
/// recusado, a conexão própria fechada no fim, duas gravações em paralelo, a particionada com as partições e a mãe sem as
/// herdeiras, a tabela segurada que falha no tempo e a violação de uma restrição sem o valor. Num banco que não grava a
/// tabela numa transação só, a tabela desfeita fica contida no que estava e no que foi gravado. As regras da base (a posse
/// do lote, a célula tratada sem escrita, uma chamada por vez) ficam no teste do contrato.
/// </summary>
public abstract partial class ConformidadeDialetoTestes<TFixture>
{
    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_substitui_o_conteudo_da_tabela()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var unica = Esperado.Gravacao.Unica;
        var chaves = await ChavesAsync(Par.Origem, cancelamento);
        Assert.True(chaves.Count >= 2, $"Gravação: a fixture precisa declarar a tabela {canonica.Tabela} com ao menos duas linhas.");

        // Duas vezes: a segunda grava sobre a primeira, e nada se repete.
        for (var vez = 1; vez <= 2; vez++)
        {
            await CopiarAsync(
                canonica.Tabela,
                [canonica.Chave, unica],
                [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
                [new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
                lote => Escrever(lote, 1, chave => $"gravada-{chave}"),
                cancelamento);

            var destino = await LerDoDestinoAsync(canonica.Tabela, [canonica.Chave, unica], [TipoColuna.Inteiro, TipoColuna.Texto], cancelamento);
            Assert.True(
                destino.Select(linha => (long?)linha[0]).Order().SequenceEqual(chaves.Order()),
                $"Gravação: depois da gravação {vez}, {canonica.Tabela} ficou com {destino.Count} linhas, esperadas as {chaves.Count} da origem, uma vez cada.");
            Assert.True(
                destino.All(linha => linha[1] is string texto && texto == $"gravada-{linha[0]}"),
                $"Gravação: depois da gravação {vez}, {canonica.Tabela}.{unica} não ficou com o valor escrito em toda linha.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_colunas_nao_escritas_e_as_mantidas_chegam_como_vieram()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var colunas = ColunasDe(canonica.Tabela);
        var origem = await Fixtura.LerTextoAsync(Par, destino: false, canonica.Tabela, cancelamento);

        // Primeiro só a chave convertida, e as outras opacas; depois as canônicas convertidas também, todas mantidas.
        var chave = colunas.IndexOf(canonica.Chave);
        var todas = canonica.Colunas.Select(coluna => new ColunaConvertida { Indice = colunas.IndexOf(coluna.Nome), Tipo = coluna.Tipo }).ToList();
        Assert.True(chave >= 0 && todas.All(convertida => convertida.Indice >= 0), $"Gravação: a fixture declarou colunas que {canonica.Tabela} não tem.");
        var casos = new (string Nome, IReadOnlyList<ColunaConvertida> Convertidas)[]
        {
            ("só com a chave convertida", [new ColunaConvertida { Indice = chave, Tipo = TipoColuna.Inteiro }]),
            ("com as canônicas convertidas e mantidas", [new ColunaConvertida { Indice = chave, Tipo = TipoColuna.Inteiro }, .. todas.Where(convertida => convertida.Indice != chave)]),
        };
        foreach (var (nome, convertidas) in casos)
        {
            // Esvaziada antes: o destino nasce com o mesmo conteúdo da origem, e a comparação só prova algo depois de esvaziar.
            await EsvaziarCanonicaAsync(cancelamento);
            await CopiarAsync(canonica.Tabela, colunas, convertidas, convertidas, lote => Manter(lote, convertidas), cancelamento);
            var destino = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);
            Assert.True(destino.SequenceEqual(origem), $"Gravação: {canonica.Tabela} gravada {nome} não chegou como veio da origem ({destino.Count} linhas, {origem.Count} na origem).");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Escrever_o_valor_lido_devolve_a_tabela_igual()
    {
        // As canônicas que não são texto convertidas nas posições delas (com colunas opacas no meio, e em ordem inversa no
        // pedido), e cada valor lido escrito de volta: a linha com escrita grava as células escritas e as outras como vieram.
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var colunas = ColunasDe(canonica.Tabela);
        var origem = await Fixtura.LerTextoAsync(Par, destino: false, canonica.Tabela, cancelamento);
        List<ColunaConvertida> convertidas =
        [
            new ColunaConvertida { Indice = colunas.IndexOf(canonica.Chave), Tipo = TipoColuna.Inteiro },
            .. canonica.Colunas.Where(coluna => coluna.Tipo != TipoColuna.Texto).Select(coluna => new ColunaConvertida { Indice = colunas.IndexOf(coluna.Nome), Tipo = coluna.Tipo }),
        ];
        convertidas.Reverse();
        Assert.True(convertidas.All(convertida => convertida.Indice >= 0), $"Gravação: a fixture declarou colunas que {canonica.Tabela} não tem.");

        await EsvaziarCanonicaAsync(cancelamento);
        await CopiarAsync(
            canonica.Tabela,
            colunas,
            convertidas,
            convertidas,
            lote =>
            {
                for (var linha = 0; linha < lote.Quantidade; linha++)
                {
                    foreach (var convertida in convertidas)
                    {
                        if (lote.TentarLer(linha, convertida.Indice, out var valor))
                        {
                            lote.Escrever(linha, convertida.Indice, valor);
                        }
                        else
                        {
                            lote.Manter(linha, convertida.Indice);
                        }
                    }
                }
            },
            cancelamento);

        var destino = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);
        Assert.True(destino.SequenceEqual(origem), $"Gravação: {canonica.Tabela}, com cada valor lido escrito de volta, não chegou como veio da origem ({destino.Count} linhas, {origem.Count} na origem).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_valores_escritos_chegam_na_forma_canonica()
    {
        // Cada linha declarada recebe os valores da seguinte (em roda); as outras ficam como vieram.
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var declaradas = canonica.Linhas.ToList();
        Assert.True(declaradas.Count >= 2, $"Gravação: a fixture precisa declarar ao menos duas linhas de {canonica.Tabela}.");
        var colunas = canonica.Colunas.ToList();
        IReadOnlyList<ColunaConvertida> convertidas = [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, .. colunas.Select((coluna, i) => new ColunaConvertida { Indice = i + 1, Tipo = coluna.Tipo })];
        IReadOnlyList<string> nomes = [canonica.Chave, .. colunas.Select(coluna => coluna.Nome)];
        var seguinte = declaradas.Select((linha, i) => (linha.Chave, Valores: declaradas[(i + 1) % declaradas.Count].Valores)).ToDictionary(par => par.Chave, par => par.Valores);

        await CopiarAsync(
            canonica.Tabela,
            nomes,
            convertidas,
            convertidas.Skip(1).ToList(),
            lote =>
            {
                for (var linha = 0; linha < lote.Quantidade; linha++)
                {
                    var valores = lote.TentarLer(linha, 0, out var lida) && lida is long chave && seguinte.TryGetValue(chave, out var escolhidos) ? escolhidos : null;
                    for (var coluna = 1; coluna < nomes.Count; coluna++)
                    {
                        if (valores is null)
                        {
                            lote.Manter(linha, coluna);
                        }
                        else
                        {
                            lote.Escrever(linha, coluna, valores[coluna - 1]);
                        }
                    }
                }
            },
            cancelamento);

        var destino = await LerDoDestinoAsync(canonica.Tabela, nomes, [.. convertidas.Select(convertida => convertida.Tipo)], cancelamento);
        foreach (var (chave, valores) in seguinte)
        {
            var linha = destino.Where(lida => lida[0] is long lidaChave && lidaChave == chave).ToList();
            Assert.True(linha.Count == 1, $"Gravação: a linha de chave {chave} de {canonica.Tabela} ficou {linha.Count} vezes no destino.");
            for (var i = 0; i < colunas.Count; i++)
            {
                Assert.True(
                    Igual(linha[0][i + 1], valores[i]),
                    $"Gravação: {canonica.Tabela}.{colunas[i].Nome} ({colunas[i].Tipo}) escrita na linha de chave {chave} foi lida no destino {(ReferenceEquals(linha[0][i + 1], _semForma) ? "sem forma" : linha[0][i + 1] is null ? "nula" : "com outro valor")}.");
            }
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_desfeita_volta_logo_e_deixa_a_tabela_como_estava()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var unica = Esperado.Gravacao.Unica;
        var antes = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);

        await using (var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento))
        {
            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(
                new PedidoLeituraTabela { Tabela = canonica.Tabela, Colunas = [canonica.Chave, unica], Convertidas = [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }] },
                cancelamento);
            var gravacao = await Par.Destino.Gravador.AbrirAsync(
                new PedidoGravacao { Tabela = canonica.Tabela, Colunas = [canonica.Chave, unica], Tratadas = [new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }] },
                cancelamento);
            var relogio = Stopwatch.StartNew();
            try
            {
                var lote = await leitura.LerLoteAsync(cancelamento);
                Assert.True(lote is not null, $"Gravação: a leitura de {canonica.Tabela} não trouxe linha.");
                Escrever(lote, 1, chave => $"desfeita-{chave}");
                await gravacao.GravarAsync(lote, cancelamento);
                relogio.Restart();
            }
            finally
            {
                await gravacao.DisposeAsync();
            }

            Assert.True(relogio.Elapsed <= Fixtura.PrazoDaInterrupcao, $"Gravação: a gravação de {canonica.Tabela} desfeita no meio levou {relogio.Elapsed.TotalSeconds:0.0} s para voltar, acima de {Fixtura.PrazoDaInterrupcao.TotalSeconds:0.0} s.");
        }

        await ConferirDesfeitaAsync(canonica.Tabela, antes, "desfeita-", "a gravação desfeita sem a conclusão", cancelamento);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_gravacao_cancelada_no_meio_volta_logo_e_deixa_a_tabela_como_estava()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var longa = await Fixtura.CriarTabelaLongaAsync(Par, cancelamento);
        var tabela = longa.Coluna.Tabela;
        var colunas = ColunasDe(tabela);
        var antes = await LerInteirosDoDestinoAsync(longa.Coluna, cancelamento);

        Exception? erro = null;
        var cancelou = false;
        var relogio = new Stopwatch();
        await using (var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento))
        {
            await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = tabela, Colunas = colunas }, cancelamento);
            using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            var gravacao = await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = tabela, Colunas = colunas, Tratadas = [] }, cancelamento);
            try
            {
                // O primeiro lote gravado; o segundo, cancelado logo depois de começar.
                for (var vez = 1; await leitura.LerLoteAsync(cancelamento) is { } lote; vez++)
                {
                    if (vez < 2)
                    {
                        await gravacao.GravarAsync(lote, parar.Token);
                        continue;
                    }

                    var gravar = gravacao.GravarAsync(lote, parar.Token);
                    await parar.CancelAsync();
                    cancelou = true;
                    relogio.Start();
                    erro = await Record.ExceptionAsync(() => gravar);
                    break;
                }
            }
            finally
            {
                await gravacao.DisposeAsync();
                relogio.Stop();
            }
        }

        Assert.True(cancelou, $"Gravação: a leitura de {tabela} não trouxe dois lotes.");
        Assert.True(erro is null or OperationCanceledException, $"Gravação: a gravação de {tabela} cancelada no meio lançou {erro?.GetType().Name}, e não OperationCanceledException.");
        Assert.True(relogio.Elapsed <= Fixtura.PrazoDaInterrupcao, $"Gravação: a gravação de {tabela} cancelada no meio levou {relogio.Elapsed.TotalSeconds:0.0} s para voltar, acima de {Fixtura.PrazoDaInterrupcao.TotalSeconds:0.0} s.");
        var depois = await LerInteirosDoDestinoAsync(longa.Coluna, cancelamento);
        Assert.True(
            Esperado.Gravacao.Atomica ? MesmosValores(depois, [.. antes.Select(valor => valor!.Value)]) : depois.All(antes.Contains),
            $"Gravação: a gravação de {tabela} cancelada no meio não deixou a tabela como estava ({depois.Count} linhas, {antes.Count} antes).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_lote_de_outro_dialeto_e_recusado_sem_gravar()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var antes = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);
        Exception? erro;
        await using (var gravacao = await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = canonica.Tabela, Colunas = [canonica.Chave], Tratadas = [] }, cancelamento))
        {
            var lote = new LoteDeFora(new PedidoLeituraTabela { Tabela = canonica.Tabela, Colunas = [canonica.Chave] });
            erro = await FalhaDoDialetoAsync(() => gravacao.GravarAsync(lote, cancelamento));
        }

        Assert.True(erro is not null, $"Gravação: um lote de outro dialeto foi gravado em {canonica.Tabela}.");
        Assert.True(erro is InvalidOperationException, $"Gravação: o lote de outro dialeto foi recusado com {erro.GetType().Name}, e não InvalidOperationException.");
        await ConferirDesfeitaAsync(canonica.Tabela, antes, null, "a recusa do lote de outro dialeto", cancelamento);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Cada_gravacao_usa_uma_conexao_propria_fechada_no_fim()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var outra = OutraTabela()?.Coluna;
        var antes = await Fixtura.ContarConexoesDeGravacaoAsync(Par, cancelamento);

        var uma = await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = canonica.Tabela, Colunas = [canonica.Chave], Tratadas = [] }, cancelamento);
        var duas = outra is null ? null : await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = outra.Tabela, Colunas = [outra.Coluna], Tratadas = [] }, cancelamento);
        var durante = await Fixtura.ContarConexoesDeGravacaoAsync(Par, cancelamento);
        Assert.True(durante == antes + (duas is null ? 1 : 2), $"Gravação: {durante - antes} conexões da gravação com {(duas is null ? "uma gravação viva" : "duas gravações vivas")}, esperada uma por gravação.");
        await uma.DisposeAsync();
        if (duas is not null)
        {
            await duas.ConcluirAsync(cancelamento);
            await duas.DisposeAsync();
        }

        Assert.True(await EsperarConexoesDeGravacaoAsync(antes), "Gravação: a gravação concluída ou desfeita deixou a conexão dela aberta.");
        await Par.Destino.Gravador.EsvaziarAsync(canonica.Tabela, cancelamento);
        Assert.True(await EsperarConexoesDeGravacaoAsync(antes), "Gravação: o esvaziamento deixou a conexão dele aberta.");

        // Descartar o dialeto com a gravação viva a fecha.
        var outro = await Fixtura.ConectarOutroDestinoAsync(Par, cancelamento);
        await outro.Gravador.AbrirAsync(new PedidoGravacao { Tabela = canonica.Tabela, Colunas = [canonica.Chave], Tratadas = [] }, cancelamento);
        await outro.DisposeAsync();
        Assert.True(await EsperarConexoesDeGravacaoAsync(antes), "Gravação: descartar o destino com a gravação viva deixou a conexão dela aberta.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Duas_gravacoes_em_paralelo_gravam_cada_uma_a_sua_tabela()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var unica = Esperado.Gravacao.Unica;
        if (OutraTabela() is not { } outra)
        {
            return;
        }

        var colunas = ColunasDe(outra.Coluna.Tabela);
        var chaves = await ChavesAsync(Par.Origem, cancelamento);
        await Task.WhenAll(
            CopiarAsync(
                canonica.Tabela,
                [canonica.Chave, unica],
                [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
                [new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
                lote => Escrever(lote, 1, chave => $"paralela-{chave}"),
                cancelamento),
            CopiarAsync(outra.Coluna.Tabela, colunas, [new ColunaConvertida { Indice = colunas.IndexOf(outra.Coluna.Coluna), Tipo = TipoColuna.Inteiro }], [], _ => { }, cancelamento));

        var destino = await LerDoDestinoAsync(canonica.Tabela, [canonica.Chave, unica], [TipoColuna.Inteiro, TipoColuna.Texto], cancelamento);
        Assert.True(
            destino.Select(linha => (long?)linha[0]).Order().SequenceEqual(chaves.Order()) && destino.All(linha => linha[1] is string texto && texto == $"paralela-{linha[0]}"),
            $"Gravação: com outra gravação em paralelo, {canonica.Tabela} ficou com {destino.Count} linhas, esperadas as {chaves.Count} da origem com o valor escrito.");
        var daOutra = await LerInteirosDoDestinoAsync(outra.Coluna, cancelamento);
        Assert.True(MesmosValores(daOutra, outra.Valores), $"Gravação: com outra gravação em paralelo, {outra.Coluna.Tabela} ficou com {daOutra.Count} linhas, esperadas {outra.Valores.Count}, ou com outros valores.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particionada_e_gravada_com_as_particoes_e_a_mae_sem_as_herdeiras()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var gravacao = Esperado.Gravacao;
        var mae = Esperado.LeituraDasLinhas.MaeDeHeranca;
        var casos = new List<(ValoresEsperados Tabela, ValoresEsperados? Herdeira)>();
        if (gravacao.Particionada.Existe)
        {
            casos.Add((gravacao.Particionada.Valor, null));
        }

        if (mae.Existe)
        {
            Assert.True(gravacao.HerdeiraDaMae.Existe, "Gravação: a fixture declarou a mãe de herança da leitura sem a herdeira dela.");
            casos.Add((mae.Valor, gravacao.HerdeiraDaMae.Valor));
        }

        foreach (var (caso, herdeira) in casos)
        {
            var tabela = caso.Coluna.Tabela;
            var colunas = ColunasDe(tabela);
            var indice = colunas.IndexOf(caso.Coluna.Coluna);
            await Par.Destino.Gravador.EsvaziarAsync(tabela, cancelamento);
            await ConferirAsync("esvaziada", []);

            // Duas vezes: a segunda grava sobre a primeira, e nada se repete.
            for (var vez = 1; vez <= 2; vez++)
            {
                await CopiarAsync(tabela, colunas, [new ColunaConvertida { Indice = indice, Tipo = TipoColuna.Inteiro }], [], _ => { }, cancelamento);
                await ConferirAsync(vez == 1 ? "gravada" : "gravada de novo", caso.Valores);
            }

            async Task ConferirAsync(string quando, IReadOnlyList<long> esperados)
            {
                await using var sessao = await Par.Destino.AbrirLeituraAsync(Pedido(1), cancelamento);
                var lidos = await LerInteirosAsync(sessao.Leitores[0], caso.Coluna, cancelamento);
                Assert.True(MesmosValores(lidos, esperados), $"Gravação: {tabela} {quando} ficou com {lidos.Count} linhas no destino, esperadas {esperados.Count}, ou com outros valores.");
                if (herdeira is not null)
                {
                    var daHerdeira = await LerInteirosAsync(sessao.Leitores[0], herdeira.Coluna, cancelamento);
                    Assert.True(MesmosValores(daHerdeira, herdeira.Valores), $"Gravação: com {tabela} {quando}, a herdeira {herdeira.Coluna.Tabela} mudou no destino.");
                }
            }
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_segurada_por_outra_sessao_falha_no_tempo()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var antes = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);
        await using (await Fixtura.SegurarNoDestinoAsync(Par, canonica.Tabela, cancelamento))
        {
            // Com um teto: o dialeto que espera a tabela sem prazo falha aqui, e não prende a suíte.
            using var teto = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            teto.CancelAfter(2 * Fixtura.PrazoDaInterrupcao);
            var operacoes = new (string Nome, Func<Task> Acao)[]
            {
                ("a gravação", async () => await (await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = canonica.Tabela, Colunas = [canonica.Chave], Tratadas = [] }, teto.Token)).DisposeAsync()),
                ("o esvaziamento", () => Par.Destino.Gravador.EsvaziarAsync(canonica.Tabela, teto.Token)),
            };
            foreach (var (nome, acao) in operacoes)
            {
                var relogio = Stopwatch.StartNew();
                var erro = await FalhaDoDialetoAsync(acao);
                Assert.True(!teto.IsCancellationRequested || cancelamento.IsCancellationRequested, $"Gravação: {nome} de {canonica.Tabela}, segurada por outra sessão, esperou a tabela sem prazo.");
                Assert.True(erro is not null, $"Gravação: {nome} de {canonica.Tabela}, segurada por outra sessão, não falhou.");
                Assert.True(relogio.Elapsed <= Fixtura.PrazoDaInterrupcao, $"Gravação: {nome} de {canonica.Tabela}, segurada por outra sessão, levou {relogio.Elapsed.TotalSeconds:0.0} s para falhar.");
                Assert.True(erro is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo), $"Gravação: a falha com {canonica.Tabela} segurada ({nome}) saiu como {erro.GetType().Name}, sem um código da faixa dos dialetos.");
            }
        }

        var depois = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);
        Assert.True(depois.SequenceEqual(antes), $"Gravação: a falha com {canonica.Tabela} segurada não deixou a tabela como estava.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_violacao_de_uma_restricao_do_destino_nunca_traz_o_valor()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var unica = Esperado.Gravacao.Unica;
        var sentinela = Esperado.Sentinelas.FirstOrDefault(sentinela => sentinela.Length > 0) ?? "zqGRAVACAOqz";
        var antes = await Fixtura.LerTextoAsync(Par, destino: true, canonica.Tabela, cancelamento);

        // O mesmo valor, com o sentinela, em todas as linhas: a unicidade falha na gravação ou na conclusão.
        var erro = await FalhaDoDialetoAsync(() => CopiarSemConferirAsync(
            canonica.Tabela,
            [canonica.Chave, unica],
            [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
            [new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Texto }],
            lote => Escrever(lote, 1, _ => sentinela + "-u"),
            cancelamento));
        Assert.True(erro is not null, $"Gravação: duas linhas com o mesmo valor em {canonica.Tabela}.{unica}, que é única, foram gravadas.");
        Assert.True(erro is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo), $"Gravação: a violação da unicidade de {canonica.Tabela}.{unica} saiu como {erro.GetType().Name}, sem um código da faixa dos dialetos.");
        Assert.True(!TemSentinela(TextoDaFalha(erro)), $"Gravação: a violação da unicidade de {canonica.Tabela}.{unica} traz o valor gravado.");
        await ConferirDesfeitaAsync(canonica.Tabela, antes, sentinela + "-u", "a gravação que falhou", cancelamento);
    }

    // Tudo o que uma falha mostra: o texto, as internas e os dados de cada uma.
    private static string TextoDaFalha(Exception erro)
    {
        var texto = new System.Text.StringBuilder();
        for (Exception? atual = erro; atual is not null; atual = atual.InnerException)
        {
            texto.Append(atual).Append('\n');
            foreach (System.Collections.DictionaryEntry dado in atual.Data)
            {
                texto.Append(dado.Key).Append('=').Append(dado.Value).Append('\n');
            }
        }

        return texto.ToString();
    }

    // A tabela depois de uma gravação desfeita: como estava, num banco que grava a tabela numa transação só; nos outros,
    // cada linha do que estava ou do que foi gravado (com a marca dada).
    private async Task ConferirDesfeitaAsync(NomeObjeto tabela, IReadOnlyList<string> antes, string? marca, string quando, CancellationToken cancelamento)
    {
        var depois = await Fixtura.LerTextoAsync(Par, destino: true, tabela, cancelamento);
        Assert.True(
            Esperado.Gravacao.Atomica
                ? depois.SequenceEqual(antes)
                : depois.All(linha => antes.Contains(linha) || (marca is not null && linha.Contains(marca, StringComparison.Ordinal))),
            Esperado.Gravacao.Atomica
                ? $"Gravação: depois d{quando}, {tabela} não ficou como estava."
                : $"Gravação: depois d{quando}, {tabela} ficou com linhas que não estavam nem foram gravadas.");
    }

    // Esvazia a tabela canônica do destino e confere que ficou vazia.
    private async Task EsvaziarCanonicaAsync(CancellationToken cancelamento)
    {
        var tabela = Esperado.LeituraDasLinhas.Canonica.Tabela;
        await Par.Destino.Gravador.EsvaziarAsync(tabela, cancelamento);
        var restantes = await Fixtura.LerTextoAsync(Par, destino: true, tabela, cancelamento);
        Assert.True(restantes.Count == 0, $"Gravação: {tabela} esvaziada ficou com {restantes.Count} linhas no destino.");
    }

    // Os valores de uma coluna inteira de uma tabela do destino, lidos pela sessão do destino; a falha do dialeto na leitura é
    // falha da gravação conferida.
    private async Task<IReadOnlyList<long?>> LerInteirosDoDestinoAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        IReadOnlyList<long?>? lidos = null;
        var erro = await FalhaDoDialetoAsync(async () =>
        {
            await using var sessao = await Par.Destino.AbrirLeituraAsync(Pedido(1), cancelamento);
            lidos = await LerInteirosAsync(sessao.Leitores[0], coluna, cancelamento);
        });
        Assert.True(erro is null, $"Gravação: a leitura de {coluna.Tabela} no destino falhou ({erro?.GetType().Name}).");
        return lidos!;
    }

    // Copia a tabela da origem para o destino (CopiarSemConferirAsync); a falha do dialeto na cópia é falha da gravação
    // conferida.
    private async Task CopiarAsync(
        NomeObjeto tabela,
        IReadOnlyList<string> colunas,
        IReadOnlyList<ColunaConvertida> convertidas,
        IReadOnlyList<ColunaConvertida> tratadas,
        Action<LoteLinhas> tratar,
        CancellationToken cancelamento)
    {
        var erro = await FalhaDoDialetoAsync(() => CopiarSemConferirAsync(tabela, colunas, convertidas, tratadas, tratar, cancelamento));
        Assert.True(erro is null, $"Gravação: a cópia de {tabela} falhou ({erro?.GetType().Name}).");
    }

    // Lê a tabela da origem pela sessão e grava no destino, lote a lote, com o tratamento dado; devolve as linhas lidas e as
    // gravadas.
    private async Task<(long Lidas, long Gravadas)> CopiarSemConferirAsync(
        NomeObjeto tabela,
        IReadOnlyList<string> colunas,
        IReadOnlyList<ColunaConvertida> convertidas,
        IReadOnlyList<ColunaConvertida> tratadas,
        Action<LoteLinhas> tratar,
        CancellationToken cancelamento)
    {
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        await using var leitura = await sessao.Leitores[0].AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = tabela, Colunas = colunas, Convertidas = convertidas }, cancelamento);
        await using var gravacao = await Par.Destino.Gravador.AbrirAsync(new PedidoGravacao { Tabela = tabela, Colunas = colunas, Tratadas = tratadas }, cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            tratar(lote);
            await gravacao.GravarAsync(lote, cancelamento);
        }

        return (leitura.Linhas, await gravacao.ConcluirAsync(cancelamento));
    }

    // As convertidas de uma tabela do destino, lidas pela sessão do destino, na ordem do pedido; a falha do dialeto na
    // leitura é falha da gravação conferida.
    private async Task<List<object?[]>> LerDoDestinoAsync(NomeObjeto tabela, IReadOnlyList<string> colunas, IReadOnlyList<TipoColuna> tipos, CancellationToken cancelamento)
    {
        List<object?[]>? lidas = null;
        var erro = await FalhaDoDialetoAsync(async () =>
        {
            await using var sessao = await Par.Destino.AbrirLeituraAsync(Pedido(1), cancelamento);
            lidas = await LerAsync(sessao.Leitores[0], tabela, colunas, [.. tipos.Select((tipo, i) => new ColunaConvertida { Indice = i, Tipo = tipo })], _ => true, cancelamento);
        });
        Assert.True(erro is null, $"Gravação: a leitura de {tabela} gravada no destino falhou ({erro?.GetType().Name}).");
        return lidas!;
    }

    // As chaves da tabela canônica num banco.
    private async Task<IReadOnlyList<long?>> ChavesAsync(DialetoBanco banco, CancellationToken cancelamento)
    {
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        await using var sessao = await banco.AbrirLeituraAsync(Pedido(1), cancelamento);
        return await LerInteirosAsync(sessao.Leitores[0], new ReferenciaColuna(canonica.Tabela, canonica.Chave), cancelamento);
    }

    // As colunas que a cópia lê e grava de uma tabela, como a fixture as declara.
    private List<string> ColunasDe(NomeObjeto tabela)
    {
        Assert.True(Esperado.Gravacao.Colunas.TryGetValue(tabela, out var colunas), $"Gravação: a fixture precisa declarar as colunas de {tabela}.");
        return [.. colunas];
    }

    // Uma segunda tabela que se pode gravar ao lado da canônica, com os valores de uma coluna inteira.
    private ValoresEsperados? OutraTabela() =>
        Esperado.Gravacao.Particionada.Existe ? Esperado.Gravacao.Particionada.Valor
        : Esperado.LeituraDasLinhas.MaeDeHeranca.Existe ? Esperado.LeituraDasLinhas.MaeDeHeranca.Valor
        : null;

    // Escreve numa coluna de texto de cada linha o texto dado pela chave (a coluna 0, inteira).
    private static void Escrever(LoteLinhas lote, int coluna, Func<long?, string> texto)
    {
        for (var linha = 0; linha < lote.Quantidade; linha++)
        {
            lote.TentarLer(linha, 0, out var chave);
            lote.Escrever(linha, coluna, texto((long?)chave));
        }
    }

    // Mantém as convertidas dadas em cada linha.
    private static void Manter(LoteLinhas lote, IReadOnlyList<ColunaConvertida> convertidas)
    {
        for (var linha = 0; linha < lote.Quantidade; linha++)
        {
            foreach (var convertida in convertidas)
            {
                lote.Manter(linha, convertida.Indice);
            }
        }
    }

    // A saída das conexões fechadas é assíncrona no servidor: espera até um prazo, nunca um tempo fixo.
    private async Task<bool> EsperarConexoesDeGravacaoAsync(int esperadas)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var prazo = DateTime.UtcNow + Fixtura.PrazoDasConexoes;
        while (true)
        {
            if (await Fixtura.ContarConexoesDeGravacaoAsync(Par, cancelamento) <= esperadas)
            {
                return true;
            }

            if (DateTime.UtcNow > prazo)
            {
                return false;
            }

            await Task.Delay(100, cancelamento);
        }
    }

    /// <summary>Um lote que não é de dialeto nenhum: o gravador recusa.</summary>
    private sealed class LoteDeFora(PedidoLeituraTabela pedido) : LoteLinhas(pedido, 1)
    {
        protected override bool TentarLerCore(int linha, int coluna, out object? valor)
        {
            valor = null;
            return true;
        }

        protected override void EscreverCore(int linha, int coluna, object? valor)
        {
        }

        protected override void ManterCore(int linha, int coluna)
        {
        }

        protected override void DisposeCore()
        {
        }
    }
}
