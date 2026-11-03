using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A leitura das linhas pela sessão (<see cref="LeitorDaSessao.AbrirTabelaAsync"/>): o instante, as linhas que a cópia lê, a
/// forma canônica das convertidas pela posição no pedido, o valor sem representação apontado, a leitura interrompida que
/// volta logo e deixa o leitor no instante, o lote que vale depois da leitura, as leituras vivas lado a lado, a tabela
/// mudada depois da abertura (travada ou não) nunca lida em silêncio, a sessão derrubada, a coluna inexistente e a
/// segurança por linha que falha. As regras da base (o leitor ocupado até o descarte, uma chamada por vez, o tipo
/// conferido, a célula escrita) ficam no teste do contrato.
/// </summary>
public abstract partial class ConformidadeDialetoTestes<TFixture>
{
    // O que sai de TentarLer quando o valor não tem representação na forma canônica.
    private static readonly object _semForma = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_linhas_sao_lidas_no_instante()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        var esperados = Esperado.LeituraDasLinhas.ValoresDaComum;
        Assert.True(esperados.Count > 0 && esperados.Max() == Esperado.MaioresValores.Comum.Valor, "Linhas: a fixture precisa declarar os valores da comum com o maior igual ao do maior valor.");
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, comum.Tabela), cancelamento);
        var novo = await Fixtura.AumentarMaiorValorAsync(Par, destino: false, cancelamento);
        if (sessao.Obtida == ConsistenciaObtida.Instantaneo)
        {
            var lidos = await LerInteirosAsync(sessao.Leitores[0], comum, cancelamento);
            Assert.True(
                MesmosValores(lidos, esperados),
                lidos.Contains(novo) ? $"Linhas: a leitura de {comum.Tabela} viu a linha confirmada depois da abertura." : $"Linhas: {comum.Tabela} lida com outros valores em {comum.Coluna}.");
        }

        await using var seguinte = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        Assert.True((await LerInteirosAsync(seguinte.Leitores[0], comum, cancelamento)).Contains(novo), "Linhas: a sessão aberta depois não leu a linha confirmada antes dela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_linhas_sao_as_que_a_copia_le()
    {
        // A particionada com as partições; a mãe sem as herdeiras.
        var cancelamento = TestContext.Current.CancellationToken;
        var leitura = Esperado.LeituraDasLinhas;
        ValoresEsperados[] casos =
        [
            .. leitura.Particionada.Existe ? [leitura.Particionada.Valor] : Array.Empty<ValoresEsperados>(),
            .. leitura.MaeDeHeranca.Existe ? [leitura.MaeDeHeranca.Valor] : Array.Empty<ValoresEsperados>(),
        ];
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, [.. casos.Select(caso => caso.Coluna.Tabela)]), cancelamento);
        foreach (var caso in casos)
        {
            var lidos = await LerInteirosAsync(sessao.Leitores[0], caso.Coluna, cancelamento);
            Assert.True(MesmosValores(lidos, caso.Valores), $"Linhas: {caso.Coluna.Tabela} lida com {lidos.Count} linhas, esperadas {caso.Valores.Count}, ou com outros valores.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_convertidas_chegam_na_forma_canonica()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        Assert.True(canonica.Colunas.Select(coluna => coluna.Tipo).Distinct().Count() == Enum.GetValues<TipoColuna>().Length, "Linhas: a fixture precisa declarar uma coluna de cada tipo canônico.");
        Assert.True(canonica.Linhas.Count > 0 && canonica.Linhas.All(linha => linha.Valores.Count == canonica.Colunas.Count), "Linhas: a fixture precisa declarar linhas com um valor por coluna.");
        Assert.True(canonica.Linhas.Any(linha => linha.Valores.Any(valor => valor is null)), "Linhas: a fixture precisa declarar ao menos um NULL.");

        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        var colunas = canonica.Colunas.ToList();
        var lidas = await LerAsync(
            sessao.Leitores[0],
            canonica.Tabela,
            [canonica.Chave, .. colunas.Select(coluna => coluna.Nome)],
            [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, .. colunas.Select((coluna, i) => new ColunaConvertida { Indice = i + 1, Tipo = coluna.Tipo })],
            linha => canonica.Linhas.Any(esperada => (long?)linha[0] == esperada.Chave),
            cancelamento);
        foreach (var esperada in canonica.Linhas)
        {
            var linha = lidas.Where(lida => (long?)lida[0] == esperada.Chave).ToList();
            Assert.True(linha.Count == 1, $"Linhas: a linha de chave {esperada.Chave} de {canonica.Tabela} lida {linha.Count} vezes.");
            for (var i = 0; i < colunas.Count; i++)
            {
                var lido = linha[0][i + 1];
                var valor = esperada.Valores[i];
                Assert.True(
                    Igual(lido, valor),
                    $"Linhas: {canonica.Tabela}.{colunas[i].Nome} ({colunas[i].Tipo}) na linha de chave {esperada.Chave} lida {(ReferenceEquals(lido, _semForma) ? "sem forma" : lido is null ? "nula" : "com outro valor")}, esperada {(valor is null ? "nula" : "com o valor declarado")}.");
            }
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_convertidas_sao_lidas_pela_posicao_no_pedido()
    {
        // As convertidas fora da ordem das colunas, com colunas opacas antes, entre e depois delas.
        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var colunas = canonica.Colunas.ToList();
        var inteira = colunas.First(coluna => coluna.Tipo == TipoColuna.Inteiro);
        var opacas = colunas.Where(coluna => coluna != inteira).Take(3).ToList();
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        var lidas = await LerAsync(
            sessao.Leitores[0],
            canonica.Tabela,
            [opacas[0].Nome, canonica.Chave, opacas[1].Nome, inteira.Nome, opacas[2].Nome],
            [new ColunaConvertida { Indice = 3, Tipo = inteira.Tipo }, new ColunaConvertida { Indice = 1, Tipo = TipoColuna.Inteiro }],
            _ => true,
            cancelamento);
        foreach (var esperada in canonica.Linhas)
        {
            var linha = lidas.Where(lida => lida[1] is long chave && chave == esperada.Chave).ToList();
            Assert.True(
                linha.Count == 1 && Igual(linha[0][0], esperada.Valores[colunas.IndexOf(inteira)]),
                $"Linhas: com as convertidas fora de ordem e colunas opacas no meio, a linha de chave {esperada.Chave} de {canonica.Tabela} lida {linha.Count} vezes, ou com outro valor em {inteira.Nome}.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_valor_sem_forma_canonica_e_apontado_sem_lancar()
    {
        var semForma = Esperado.LeituraDasLinhas.SemForma;
        if (semForma.Count == 0)
        {
            return;
        }

        var cancelamento = TestContext.Current.CancellationToken;
        var canonica = Esperado.LeituraDasLinhas.Canonica;
        var colunas = semForma.Select(celula => (celula.Coluna, celula.Tipo)).Distinct().ToList();
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        List<object?[]>? lidas = null;
        var erro = await FalhaDoDialetoAsync(async () => lidas = await LerAsync(
            sessao.Leitores[0],
            canonica.Tabela,
            [canonica.Chave, .. colunas.Select(coluna => coluna.Coluna)],
            [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }, .. colunas.Select((coluna, i) => new ColunaConvertida { Indice = i + 1, Tipo = coluna.Tipo })],
            _ => true,
            cancelamento));
        Assert.True(erro is null, $"Linhas: a leitura de {canonica.Tabela} lançou {erro?.GetType().Name} num valor sem representação na forma canônica.");
        foreach (var celula in semForma)
        {
            var linha = lidas!.Where(lida => lida[0] is long chave && chave == celula.Chave).ToList();
            Assert.True(linha.Count == 1, $"Linhas: a linha de chave {celula.Chave} de {canonica.Tabela}, com um valor sem forma, lida {linha.Count} vezes.");
            var lido = linha[0][colunas.IndexOf((celula.Coluna, celula.Tipo)) + 1];
            Assert.True(ReferenceEquals(lido, _semForma), $"Linhas: {canonica.Tabela}.{celula.Coluna} na linha de chave {celula.Chave}, sem representação na forma canônica, foi lida como {(lido is null ? "nula" : "um valor")}.");
        }

        Assert.True(
            canonica.Linhas.All(esperada => lidas!.Any(lida => lida[0] is long chave && chave == esperada.Chave)),
            $"Linhas: com os valores sem forma, faltaram linhas de {canonica.Tabela}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_leitura_interrompida_volta_logo_e_deixa_o_leitor_no_instante()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        var longa = await Fixtura.CriarTabelaLongaAsync(Par, cancelamento);
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, comum.Coluna.Tabela), cancelamento);
        var leitor = sessao.Leitores[0];

        // Um lote lido e a leitura descartada no meio; depois, cancelada no meio das chamadas e descartada.
        foreach (var cancelando in new[] { false, true })
        {
            Exception? erro = null;
            var levou = TimeSpan.Zero;
            var falha = await FalhaDoDialetoAsync(async () =>
            {
                using var parar = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
                var interrompida = await leitor.AbrirTabelaAsync(PedidoDeLinhas(longa.Coluna.Tabela, [longa.Coluna.Coluna], TipoColuna.Inteiro), cancelamento);
                var relogio = new Stopwatch();
                try
                {
                    (await interrompida.LerLoteAsync(parar.Token))?.Dispose();
                    relogio.Start();
                    if (cancelando)
                    {
                        var chamadas = Task.Run(
                            async () =>
                            {
                                while (await interrompida.LerLoteAsync(parar.Token) is { } lote)
                                {
                                    lote.Dispose();
                                }
                            },
                            cancelamento);
                        await parar.CancelAsync();
                        erro = await Record.ExceptionAsync(() => chamadas);
                    }
                }
                finally
                {
                    await interrompida.DisposeAsync();
                    levou = relogio.Elapsed;
                }
            });
            Assert.True(falha is null, $"Linhas: o leitor não leu depois de uma leitura interrompida ({falha?.GetType().Name}).");
            Assert.True(erro is null or OperationCanceledException, $"Linhas: a leitura de {longa.Coluna.Tabela} cancelada no meio lançou {erro?.GetType().Name}, e não OperationCanceledException.");
            Assert.True(levou <= Fixtura.PrazoDaInterrupcao, $"Linhas: a leitura de {longa.Coluna.Tabela} {(cancelando ? "cancelada" : "descartada")} no meio levou {levou.TotalSeconds:0.0} s para voltar, acima de {Fixtura.PrazoDaInterrupcao.TotalSeconds:0.0} s.");
        }

        await Fixtura.AumentarMaiorValorAsync(Par, destino: false, cancelamento);
        var depois = await FalhaDoDialetoAsync(async () =>
        {
            var lidos = await LerInteirosAsync(leitor, comum.Coluna, cancelamento);
            Assert.True(
                sessao.Obtida != ConsistenciaObtida.Instantaneo || MesmosValores(lidos, Esperado.LeituraDasLinhas.ValoresDaComum),
                $"Linhas: depois da leitura interrompida, {comum.Coluna.Tabela} foi lida fora do instante da sessão.");
        });
        Assert.True(depois is null, $"Linhas: o leitor não leu depois de uma leitura interrompida ({depois?.GetType().Name}).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_lote_vale_depois_da_leitura_seguinte_e_dos_descartes()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var longa = await Fixtura.CriarTabelaLongaAsync(Par, cancelamento);
        var tabela = longa.Coluna.Tabela;
        var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        LoteLinhas? primeiro = null;
        try
        {
            await using (var leitura = await sessao.Leitores[0].AbrirTabelaAsync(PedidoDeLinhas(tabela, [longa.Coluna.Coluna], TipoColuna.Inteiro), cancelamento))
            {
                primeiro = await leitura.LerLoteAsync(cancelamento);
                Assert.True(primeiro is { Quantidade: > 0 }, $"Linhas: a leitura de {tabela} não trouxe o primeiro lote.");
                var antes = Inteiros(primeiro!);
                for (var i = 0; i < 2; i++)
                {
                    (await leitura.LerLoteAsync(cancelamento))?.Dispose();
                }

                Assert.True(Inteiros(primeiro!).SequenceEqual(antes), $"Linhas: o primeiro lote de {tabela} mudou depois da leitura dos seguintes.");
                await leitura.DisposeAsync();
                await sessao.DisposeAsync();
                Assert.True(Inteiros(primeiro!).SequenceEqual(antes), $"Linhas: o primeiro lote de {tabela} mudou depois do descarte da leitura e da sessão.");
            }
        }
        finally
        {
            primeiro?.Dispose();
            await sessao.DisposeAsync();
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Duas_leituras_vivas_alternadas_leem_cada_uma_as_suas_linhas()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var longa = await Fixtura.CriarTabelaLongaAsync(Par, cancelamento);
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(2), cancelamento);
        var pedido = PedidoDeLinhas(longa.Coluna.Tabela, [longa.Coluna.Coluna], TipoColuna.Inteiro);
        await using var primeira = await sessao.Leitores[0].AbrirTabelaAsync(pedido, cancelamento);
        await using var segunda = await sessao.Leitores[1].AbrirTabelaAsync(pedido, cancelamento);
        LeituraTabela[] leituras = [primeira, segunda];
        var contadas = new long[2];
        var somas = new long[2];
        var fim = new bool[2];
        while (!fim[0] || !fim[1])
        {
            for (var i = 0; i < 2; i++)
            {
                if (fim[i])
                {
                    continue;
                }

                using var lote = await leituras[i].LerLoteAsync(cancelamento);
                fim[i] = lote is null;
                if (lote is not null)
                {
                    contadas[i] += lote.Quantidade;
                    somas[i] += Inteiros(lote).Sum(valor => valor ?? 0);
                }
            }
        }

        var soma = longa.Linhas * (longa.Linhas + 1) / 2;
        for (var i = 0; i < 2; i++)
        {
            Assert.True(contadas[i] == longa.Linhas && somas[i] == soma, $"Linhas: com duas leituras vivas alternadas, a do leitor {i} trouxe {contadas[i]} de {longa.Linhas} linhas de {longa.Coluna.Tabela}, ou outros valores.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_linhas_da_travada_nunca_sao_lidas_vazias_em_silencio()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, comum.Tabela), cancelamento);
        var esvaziou = await Fixtura.TentarEsvaziarAsync(Par, comum.Tabela, cancelamento);
        IReadOnlyList<long?>? lidos = null;
        var erro = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(sessao.Leitores[0], comum, cancelamento));
        var impede = Esperado.Sessao.Travada == ProtecaoDaTravada.Impede;
        Assert.True(
            erro is not null || !esvaziou || lidos!.Count > 0,
            $"Linhas: a tabela travada {comum.Tabela}, esvaziada com a sessão viva, foi lida vazia em silêncio.");
        Assert.True(
            esvaziou != impede,
            $"Linhas: a tabela travada {comum.Tabela} {(esvaziou ? "foi esvaziada" : "não foi esvaziada")} com a sessão viva, e o banco declara que {(impede ? "impede o esvaziamento" : "deixa esvaziar e falha a leitura")}.");
        Assert.True(
            esvaziou ? erro is not null : erro is null && MesmosValores(lidos!, Esperado.LeituraDasLinhas.ValoresDaComum),
            erro is null ? $"Linhas: a tabela travada {comum.Tabela} foi lida com outros valores." : $"Linhas: a tabela travada {comum.Tabela}, protegida, não foi lida ({erro.GetType().Name}).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_tabela_esvaziada_fora_das_travadas_nunca_e_lida_vazia_em_silencio()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        Assert.True(await Fixtura.TentarEsvaziarAsync(Par, comum.Tabela, cancelamento), $"Linhas: {comum.Tabela}, fora das travadas, não foi esvaziada com a sessão viva (controle).");
        IReadOnlyList<long?>? lidos = null;
        var erro = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(sessao.Leitores[0], comum, cancelamento));
        Assert.True(
            erro is not null || MesmosValores(lidos!, Esperado.LeituraDasLinhas.ValoresDaComum),
            lidos is { Count: 0 } ? $"Linhas: {comum.Tabela}, esvaziada fora das travadas depois da abertura, foi lida vazia em silêncio." : $"Linhas: {comum.Tabela}, esvaziada fora das travadas, foi lida com outros valores.");
        Assert.True(erro is null || (erro is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo)), $"Linhas: a falha da leitura de {comum.Tabela} esvaziada saiu como {erro?.GetType().Name}, sem um código da faixa dos dialetos.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particao_anexada_depois_da_abertura_nunca_entra_nas_linhas()
    {
        var particionada = Esperado.LeituraDasLinhas.Particionada;
        if (!particionada.Existe)
        {
            return;
        }

        var cancelamento = TestContext.Current.CancellationToken;
        var caso = particionada.Valor;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, caso.Coluna.Tabela), cancelamento);
        var anexado = await Fixtura.AnexarParticaoAsync(Par, cancelamento);
        IReadOnlyList<long?>? lidos = null;
        var erro = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(sessao.Leitores[0], caso.Coluna, cancelamento));
        Assert.True(
            erro is not null || MesmosValores(lidos!, caso.Valores),
            lidos?.Contains(anexado) == true ? $"Linhas: a partição anexada depois da abertura entrou na leitura de {caso.Coluna.Tabela}." : $"Linhas: a particionada {caso.Coluna.Tabela} foi lida com outros valores.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_sessao_derrubada_nunca_le_nem_termina_a_leitura_em_silencio()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        var longa = await Fixtura.CriarTabelaLongaAsync(Par, cancelamento);
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(2, comum.Tabela), cancelamento);

        // A leitura aberta antes da queda, com um lote lido: o resto falha, ou chega inteiro.
        long lidas = 0;
        var noMeio = await FalhaDoDialetoAsync(async () =>
        {
            await using var leitura = await sessao.Leitores[1].AbrirTabelaAsync(PedidoDeLinhas(longa.Coluna.Tabela, [longa.Coluna.Coluna], TipoColuna.Inteiro), cancelamento);
            using (var lote = await leitura.LerLoteAsync(cancelamento))
            {
                lidas += lote?.Quantidade ?? 0;
            }

            await Fixtura.DerrubarSessoesAsync(Par, destino: false, cancelamento);
            while (await leitura.LerLoteAsync(cancelamento) is { } lote)
            {
                using (lote)
                {
                    lidas += lote.Quantidade;
                }
            }
        });
        Assert.True(noMeio is not null || lidas == longa.Linhas, $"Linhas: a leitura de {longa.Coluna.Tabela}, com a sessão derrubada no meio, terminou em silêncio com {lidas} de {longa.Linhas} linhas.");
        Assert.True(noMeio is null || !TemSentinela(noMeio.ToString()), "Linhas: a falha da leitura com a sessão derrubada no meio traz um sentinela.");

        // A leitura aberta depois da queda.
        IReadOnlyList<long?>? lidos = null;
        var depois = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(sessao.Leitores[0], comum, cancelamento));
        Assert.True(depois is not null, $"Linhas: o leitor leu {lidos?.Count} linhas de {comum.Tabela} depois de a sessão ser derrubada.");
        Assert.True(!TemSentinela(depois.ToString()), "Linhas: a falha da leitura com a sessão derrubada traz um sentinela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_coluna_inexistente_e_recusada_e_o_leitor_continua()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, comum.Tabela), cancelamento);
        var leitor = sessao.Leitores[0];
        var recusa = await FalhaDoDialetoAsync(() => LerAsync(leitor, comum.Tabela, [comum.Coluna, "coluna_inexistente"], [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }], _ => true, cancelamento));
        Assert.True(
            recusa is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo),
            recusa is null ? $"Linhas: a coluna inexistente de {comum.Tabela} foi lida." : $"Linhas: a recusa da coluna inexistente de {comum.Tabela} saiu como {recusa.GetType().Name}, sem um código da faixa dos dialetos.");
        Assert.True(!TemSentinela(recusa!.ToString()), $"Linhas: a recusa da coluna inexistente de {comum.Tabela} traz um sentinela.");

        IReadOnlyList<long?>? lidos = null;
        var depois = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(leitor, comum, cancelamento));
        Assert.True(depois is null, $"Linhas: o leitor não leu depois da recusa da coluna inexistente ({depois?.GetType().Name}).");
        Assert.True(sessao.Obtida != ConsistenciaObtida.Instantaneo || MesmosValores(lidos!, Esperado.LeituraDasLinhas.ValoresDaComum), $"Linhas: depois da recusa da coluna inexistente, {comum.Tabela} foi lida com outros valores.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_faz_a_leitura_das_linhas_falhar()
    {
        if (!Esperado.Sessao.SegurancaPorLinha.Existe)
        {
            return;
        }

        var cancelamento = TestContext.Current.CancellationToken;
        var coluna = Esperado.Sessao.SegurancaPorLinha.Valor;
        var comum = Esperado.MaioresValores.Comum.Coluna;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        var leitor = sessao.Leitores[0];
        IReadOnlyList<long?>? lidos = null;
        var falha = await FalhaDoDialetoAsync(async () => lidos = await LerInteirosAsync(leitor, coluna, cancelamento));
        Assert.True(falha is not null, $"Linhas: {coluna.Tabela}, com a segurança por linha para o login, foi lida ({lidos?.Count} linhas) em vez de falhar.");
        Assert.True(falha is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo), $"Linhas: a falha da segurança por linha de {coluna.Tabela} saiu como {falha.GetType().Name}, sem um código da faixa dos dialetos.");
        Assert.True(!TemSentinela(falha.ToString()), $"Linhas: a falha da segurança por linha de {coluna.Tabela} traz um sentinela.");

        var depois = await FalhaDoDialetoAsync(async () => Assert.True(
            sessao.Obtida != ConsistenciaObtida.Instantaneo || MesmosValores(await LerInteirosAsync(leitor, comum, cancelamento), Esperado.LeituraDasLinhas.ValoresDaComum),
            $"Linhas: depois de uma leitura que falhou, {comum.Tabela} foi lida com outros valores."));
        Assert.True(depois is null, $"Linhas: o leitor não leu depois de uma leitura das linhas que falhou ({depois?.GetType().Name}).");
    }

    private static PedidoLeituraTabela PedidoDeLinhas(NomeObjeto tabela, IReadOnlyList<string> colunas, TipoColuna tipo) =>
        new() { Tabela = tabela, Colunas = colunas, Convertidas = [new ColunaConvertida { Indice = 0, Tipo = tipo }] };

    private static bool MesmosValores(IReadOnlyList<long?> lidos, IReadOnlyList<long> esperados) =>
        lidos.Count == esperados.Count && lidos.Order().SequenceEqual(esperados.Select(valor => (long?)valor).Order());

    // O decimal compara também a escala: 1234.50 e 1234.5 são formas diferentes.
    private static bool Igual(object? lido, object? esperado) => (lido, esperado) switch
    {
        (null, null) => true,
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (decimal a, decimal b) => a == b && a.Scale == b.Scale,
        (not null, not null) => lido.GetType() == esperado.GetType() && lido.Equals(esperado),
        _ => false,
    };

    // A falha do dialeto numa ação; a falha de uma asserção da própria suíte sobe como está.
    private static async Task<Exception?> FalhaDoDialetoAsync(Func<Task> acao)
    {
        var erro = await Record.ExceptionAsync(acao);
        if (erro is IAssertionException)
        {
            ExceptionDispatchInfo.Throw(erro);
        }

        return erro;
    }

    // Os valores da primeira coluna do pedido (inteira) de cada linha do lote.
    private static long?[] Inteiros(LoteLinhas lote) =>
        [.. Enumerable.Range(0, lote.Quantidade).Select(linha => lote.TentarLer(linha, 0, out var valor) ? (long?)valor : null)];

    // Os valores de uma coluna inteira, de todas as linhas.
    private static async Task<IReadOnlyList<long?>> LerInteirosAsync(LeitorDaSessao leitor, ReferenciaColuna coluna, CancellationToken cancelamento) =>
        [.. (await LerAsync(leitor, coluna.Tabela, [coluna.Coluna], [new ColunaConvertida { Indice = 0, Tipo = TipoColuna.Inteiro }], _ => true, cancelamento)).Select(linha => (long?)linha[0])];

    // Lê a tabela inteira e devolve, das linhas que o filtro aceita (pela primeira convertida), os valores das convertidas
    // na ordem da lista do pedido; o sem forma sai como _semForma. Confere a numeração dos lotes e a contagem da leitura.
    private static async Task<List<object?[]>> LerAsync(
        LeitorDaSessao leitor, NomeObjeto tabela, IReadOnlyList<string> colunas, IReadOnlyList<ColunaConvertida> convertidas, Func<object?[], bool> filtro, CancellationToken cancelamento)
    {
        var linhas = new List<object?[]>();
        long contadas = 0;
        await using var leitura = await leitor.AbrirTabelaAsync(new PedidoLeituraTabela { Tabela = tabela, Colunas = colunas, Convertidas = convertidas }, cancelamento);
        while (await leitura.LerLoteAsync(cancelamento) is { } lote)
        {
            using (lote)
            {
                Assert.True(lote.PrimeiraLinha == contadas + 1, $"Linhas: o lote de {tabela} começa na linha {lote.PrimeiraLinha}, depois de {contadas} lidas.");
                contadas += lote.Quantidade;
                for (var i = 0; i < lote.Quantidade; i++)
                {
                    var linha = new object?[convertidas.Count];
                    for (var c = 0; c < convertidas.Count; c++)
                    {
                        linha[c] = lote.TentarLer(i, convertidas[c].Indice, out var valor) ? valor : _semForma;
                        if (c == 0 && !filtro(linha))
                        {
                            break;
                        }
                    }

                    if (filtro(linha))
                    {
                        linhas.Add(linha);
                    }
                }
            }
        }

        Assert.True(leitura.Linhas == contadas, $"Linhas: a leitura de {tabela} contou {leitura.Linhas} linhas, e os lotes trouxeram {contadas}.");
        Assert.True(await leitura.LerLoteAsync(cancelamento) is null, $"Linhas: a leitura de {tabela} devolveu um lote depois do fim.");
        return linhas;
    }
}
