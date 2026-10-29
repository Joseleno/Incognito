using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A sessão de leitura (<see cref="SessaoLeitura"/>): o mesmo instante em todos os leitores e no esquema relido, as tabelas
/// travadas nunca lidas vazias em silêncio, a leitura que falha sem filtrar nem inutilizar o leitor, e as conexões fechadas
/// no descarte. As regras da base (descarte único, reentrada, leitores fixos) ficam no teste do contrato.
/// </summary>
public abstract partial class ConformidadeDialetoTestes<TFixture>
{
    private const int LeitoresDaSuite = 3;

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_sessao_le_o_mesmo_instante_em_todos_os_leitores()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(LeitoresDaSuite, comum.Coluna.Tabela), cancelamento);
        Assert.True(sessao.Obtida == Esperado.Sessao.Consistencia, $"Sessão: consistência obtida {sessao.Obtida}, declarada {Esperado.Sessao.Consistencia}.");
        Assert.True(sessao.Leitores.Count == LeitoresDaSuite, $"Sessão: {sessao.Leitores.Count} leitores, pedidos {LeitoresDaSuite}.");

        // Confirmados depois da abertura: uma linha que aumenta o maior valor e uma tabela nova; fora da sessão, os dois aparecem.
        var novo = await Fixtura.AumentarMaiorValorAsync(Par, destino: false, cancelamento);
        var criada = await Fixtura.CriarTabelaAsync(Par, cancelamento);
        Assert.True(await Par.Origem.Dados.LerMaiorValorAsync(comum.Coluna, cancelamento) == novo, "Sessão: a linha confirmada depois da abertura não chegou ao banco (controle).");
        Assert.True((await LerOrigemAsync()).Tabelas.Any(tabela => tabela.Nome.Equals(criada)), "Sessão: a tabela criada depois da abertura não chegou ao catálogo (controle).");
        if (sessao.Obtida != ConsistenciaObtida.Instantaneo)
        {
            return;
        }

        for (var i = 0; i < sessao.Leitores.Count; i++)
        {
            var lido = await sessao.Leitores[i].LerMaiorValorAsync(comum.Coluna, cancelamento);
            Assert.True(lido == comum.Valor, $"Sessão: o leitor {i} viu a linha confirmada depois da abertura (não está no instante da sessão).");
        }

        var relido = await sessao.LerEsquemaAsync(cancelamento);
        Assert.True(!relido.Tabelas.Any(tabela => tabela.Nome.Equals(criada)), "Sessão: o esquema relido viu a tabela criada depois da abertura.");

        await using var seguinte = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        Assert.True(await seguinte.Leitores[0].LerMaiorValorAsync(comum.Coluna, cancelamento) == novo, "Sessão: a sessão aberta depois não viu a linha confirmada antes dela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_maior_valor_na_sessao_e_lido_como_a_copia_le()
    {
        // Os mesmos casos da leitura fora de sessão, pelos leitores, com as tabelas travadas: a particionada com as
        // partições, a mãe sem as herdeiras.
        var cancelamento = TestContext.Current.CancellationToken;
        var maiores = Esperado.MaioresValores;
        MaiorValorEsperado[] casos =
        [
            maiores.Comum,
            maiores.SemValor,
            maiores.SoNegativos,
            .. maiores.Particionada.Existe ? [maiores.Particionada.Valor] : Array.Empty<MaiorValorEsperado>(),
            .. maiores.MaeDeHeranca.Existe ? [maiores.MaeDeHeranca.Valor] : Array.Empty<MaiorValorEsperado>(),
        ];
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(2, [.. casos.Select(caso => caso.Coluna.Tabela).Distinct()]), cancelamento);
        for (var i = 0; i < casos.Length; i++)
        {
            var caso = casos[i];
            var lido = await sessao.Leitores[i % 2].LerMaiorValorAsync(caso.Coluna, cancelamento);
            Assert.True(lido == caso.Valor, $"Sessão: {caso.Coluna} lido na sessão {(lido is null ? "nulo" : "com outro valor")}, diferente do declarado.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_sessao_do_destino_le_o_mesmo_instante()
    {
        // O destino é lido sem travas (o esvaziamento do próprio Incognito esperaria por elas).
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        await using var sessao = await Par.Destino.AbrirLeituraAsync(Pedido(2), cancelamento);
        Assert.True(sessao.Obtida == Esperado.Sessao.Consistencia, $"Sessão: no destino, consistência obtida {sessao.Obtida}, declarada {Esperado.Sessao.Consistencia}.");

        var novo = await Fixtura.AumentarMaiorValorAsync(Par, destino: true, cancelamento);
        if (sessao.Obtida == ConsistenciaObtida.Instantaneo)
        {
            for (var i = 0; i < sessao.Leitores.Count; i++)
            {
                var lido = await sessao.Leitores[i].LerMaiorValorAsync(comum.Coluna, cancelamento);
                Assert.True(lido == comum.Valor, $"Sessão: no destino, o leitor {i} viu a linha confirmada depois da abertura (não está no instante da sessão).");
            }
        }

        await using var seguinte = await Par.Destino.AbrirLeituraAsync(Pedido(1), cancelamento);
        Assert.True(await seguinte.Leitores[0].LerMaiorValorAsync(comum.Coluna, cancelamento) == novo, "Sessão: no destino, a sessão aberta depois não viu a linha confirmada antes dela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_modo_exigir_abre_no_instante_unico_ou_e_recusado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var pedido = new PedidoLeitura { Leitores = 1, Modo = ModoConsistencia.Exigir };
        if (Esperado.Sessao.Consistencia == ConsistenciaObtida.Instantaneo)
        {
            var erro = await Record.ExceptionAsync(async () =>
            {
                await using var sessao = await Par.Origem.AbrirLeituraAsync(pedido, cancelamento);
                Assert.True(sessao.Obtida == ConsistenciaObtida.Instantaneo, $"Sessão: o modo exigir abriu com a consistência {sessao.Obtida}.");
            });
            Assert.True(erro is null or Xunit.Sdk.IAssertionException, $"Sessão: o modo exigir foi recusado num banco de instante único ({erro?.GetType().Name}).");
            if (erro is not null)
            {
                throw erro;
            }
        }
        else
        {
            var erro = await Record.ExceptionAsync(async () => await (await Par.Origem.AbrirLeituraAsync(pedido, cancelamento)).DisposeAsync());
            Assert.True(erro is DialetoRecusouException, $"Sessão: o modo exigir num banco sem instante único {(erro is null ? "abriu" : $"lançou {erro.GetType().Name}")}, e não foi recusado.");
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_travadas_nunca_sao_lidas_vazias_em_silencio()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        var tabela = comum.Coluna.Tabela;
        var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(2, tabela), cancelamento);
        try
        {
            // O coordenador trabalha antes: a releitura do esquema e a idade não soltam as travas.
            await sessao.LerEsquemaAsync(cancelamento);
            await sessao.LerIdadeAsync(cancelamento);
            var esvaziou = await Fixtura.TentarEsvaziarAsync(Par, tabela, cancelamento);
            long? lido = null;
            var erro = await Record.ExceptionAsync(async () => lido = await sessao.Leitores[1].LerMaiorValorAsync(comum.Coluna, cancelamento));
            Assert.True(
                erro is not null || lido == comum.Valor,
                esvaziou ? $"Sessão: a tabela travada {tabela}, esvaziada com a sessão viva, foi lida vazia em silêncio." : $"Sessão: a tabela travada {tabela} foi lida com outro valor.");
            var impede = Esperado.Sessao.Travada == ProtecaoDaTravada.Impede;
            Assert.True(
                esvaziou != impede,
                $"Sessão: a tabela travada {tabela} {(esvaziou ? "foi esvaziada" : "não foi esvaziada")} com a sessão viva, e o banco declara que {(impede ? "impede o esvaziamento" : "deixa esvaziar e falha a leitura")}.");
        }
        finally
        {
            await sessao.DisposeAsync();
        }

        Assert.True(await Fixtura.TentarEsvaziarAsync(Par, tabela, cancelamento), $"Sessão: a tabela {tabela} continuou travada depois do descarte da sessão.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particao_anexada_depois_da_abertura_nunca_entra_na_leitura()
    {
        var particionada = Esperado.MaioresValores.Particionada;
        if (!particionada.Existe)
        {
            return;
        }

        var cancelamento = TestContext.Current.CancellationToken;
        var caso = particionada.Valor;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, caso.Coluna.Tabela), cancelamento);
        var anexado = await Fixtura.AnexarParticaoAsync(Par, cancelamento);
        Assert.True(await Par.Origem.Dados.LerMaiorValorAsync(caso.Coluna, cancelamento) == anexado, "Sessão: a partição anexada não chegou ao banco (controle).");

        long? lido = null;
        var erro = await Record.ExceptionAsync(async () => lido = await sessao.Leitores[0].LerMaiorValorAsync(caso.Coluna, cancelamento));
        Assert.True(
            erro is not null || lido == caso.Valor,
            lido == anexado ? $"Sessão: a partição anexada depois da abertura entrou na leitura de {caso.Coluna.Tabela}." : $"Sessão: a particionada {caso.Coluna.Tabela} foi lida com outro valor.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_leitura_que_falha_nao_filtra_nem_inutiliza_o_leitor()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var maiores = Esperado.MaioresValores;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        var leitor = sessao.Leitores[0];

        // A coluna de texto é recusada sem trazer o texto das linhas.
        var recusa = await Record.ExceptionAsync(() => leitor.LerMaiorValorAsync(maiores.ColunaDeTexto, cancelamento));
        Assert.True(recusa is not null, $"Sessão: a coluna de texto {maiores.ColunaDeTexto} foi lida como inteira.");
        Assert.True(!TemSentinela(recusa.ToString()), $"Sessão: a recusa da coluna de texto {maiores.ColunaDeTexto} traz o valor das linhas.");

        // A segurança por linha para o login faz a leitura falhar, com um código do dialeto, nunca filtrar.
        if (Esperado.Sessao.SegurancaPorLinha.Existe)
        {
            var coluna = Esperado.Sessao.SegurancaPorLinha.Valor;
            var falha = await Record.ExceptionAsync(() => leitor.LerMaiorValorAsync(coluna, cancelamento));
            Assert.True(falha is not null, $"Sessão: {coluna.Tabela}, com a segurança por linha para o login, foi lida em vez de falhar.");
            Assert.True(falha is IncognitoException { Codigo: var codigo } && DaFaixaDosDialetos(codigo), $"Sessão: a falha da segurança por linha de {coluna.Tabela} saiu como {falha.GetType().Name}, sem um código da faixa dos dialetos.");
            Assert.True(!TemSentinela(falha.ToString()), $"Sessão: a falha da segurança por linha de {coluna.Tabela} traz um sentinela.");
        }

        var depois = await Record.ExceptionAsync(async () => Assert.True(
            await leitor.LerMaiorValorAsync(maiores.Comum.Coluna, cancelamento) == maiores.Comum.Valor,
            $"Sessão: depois de uma leitura que falhou, {maiores.Comum.Coluna} foi lida com outro valor."));
        Assert.True(depois is null or Xunit.Sdk.IAssertionException, $"Sessão: o leitor não leu depois de uma leitura que falhou ({depois?.GetType().Name}).");
        if (depois is not null)
        {
            throw depois;
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_descarte_fecha_as_conexoes_da_sessao()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        foreach (var (destino, dialeto) in new (bool, DialetoBanco)[] { (false, Par.Origem), (true, Par.Destino) })
        {
            var lado = destino ? "destino" : "origem";
            var antes = await Fixtura.ContarConexoesDeLeituraAsync(Par, destino, cancelamento);
            var sessao = await dialeto.AbrirLeituraAsync(Pedido(LeitoresDaSuite), cancelamento);
            var durante = await Fixtura.ContarConexoesDeLeituraAsync(Par, destino, cancelamento);
            await sessao.DisposeAsync();
            Assert.True(durante >= antes + LeitoresDaSuite, $"Sessão: {lado}: {durante - antes} conexões de leitura com a sessão viva, esperadas ao menos {LeitoresDaSuite} (uma por leitor).");
            Assert.True(await EsperarConexoesAsync(destino, antes), $"Sessão: {lado}: o descarte da sessão deixou conexões de leitura abertas.");
        }

        // Descartar o dialeto com a sessão viva a fecha.
        var base0 = await Fixtura.ContarConexoesDeLeituraAsync(Par, destino: false, cancelamento);
        await Par.Origem.AbrirLeituraAsync(Pedido(2), cancelamento);
        await Par.Origem.DisposeAsync();
        Assert.True(await EsperarConexoesAsync(destino: false, base0), "Sessão: descartar a origem com a sessão viva deixou conexões de leitura abertas.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_sessao_derrubada_falha_e_o_descarte_nao_lanca()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(2, comum.Coluna.Tabela), cancelamento);
        await Fixtura.DerrubarSessoesAsync(Par, destino: false, cancelamento);

        var erro = await Record.ExceptionAsync(() => sessao.Leitores[0].LerMaiorValorAsync(comum.Coluna, cancelamento));
        Assert.True(erro is not null, "Sessão: o leitor leu depois de a sessão ser derrubada.");
        Assert.True(!TemSentinela(erro.ToString()), "Sessão: a falha da sessão derrubada traz um sentinela.");
        var descarte = await Record.ExceptionAsync(async () => await sessao.DisposeAsync());
        Assert.True(descarte is null, $"Sessão: o descarte da sessão derrubada lançou {descarte?.GetType().Name}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_idade_do_instante_e_lida()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1), cancelamento);
        var idade = await sessao.LerIdadeAsync(cancelamento);
        Assert.True(
            Esperado.Sessao.MedeIdade ? idade is >= 0 : idade is null,
            $"Sessão: a idade do instante lida como {(idade is null ? "nula" : "um número")}, num banco que {(Esperado.Sessao.MedeIdade ? "a mede" : "não a mede")}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_travada_inexistente_e_recusada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var inexistente = new NomeObjeto(Esperado.MaioresValores.Comum.Coluna.Tabela.Esquema, "travada_inexistente");
        var erro = await Record.ExceptionAsync(async () => await (await Par.Origem.AbrirLeituraAsync(Pedido(1, inexistente), cancelamento)).DisposeAsync());
        var recusa = erro as DialetoRecusouException;
        Assert.True(recusa is not null, $"Sessão: a travada inexistente {(erro is null ? "foi aceita" : $"saiu como {erro.GetType().Name}")}, e não DialetoRecusouException.");
        Assert.True(DaFaixaDosDialetos(recusa.Codigo), "Sessão: a recusa da travada inexistente tem um código fora da faixa dos dialetos.");
        Assert.True(!TemSentinela(recusa.ToString()), "Sessão: a recusa da travada inexistente traz um sentinela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_relido_na_sessao_e_o_do_dialeto()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var comum = Esperado.MaioresValores.Comum;
        await using var sessao = await Par.Origem.AbrirLeituraAsync(Pedido(1, comum.Coluna.Tabela), cancelamento);
        var relido = DespejoDoModelo.TextoSemAcesso(await sessao.LerEsquemaAsync(cancelamento));
        var direto = DespejoDoModelo.TextoSemAcesso(await LerOrigemAsync());
        Assert.True(relido == direto, "Sessão: o esquema relido na sessão difere do lido pelo dialeto (fora as permissões do login).");
        Assert.True(DespejoDoModelo.TextoSemAcesso(await sessao.LerEsquemaAsync(cancelamento)) == relido, "Sessão: duas releituras do esquema na sessão diferem.");
        Assert.True(await sessao.Leitores[0].LerMaiorValorAsync(comum.Coluna, cancelamento) == comum.Valor, $"Sessão: depois das releituras do esquema, {comum.Coluna} foi lida com outro valor.");
    }

    private static PedidoLeitura Pedido(int leitores, params NomeObjeto[] travadas) =>
        new() { Leitores = leitores, Modo = ModoConsistencia.MelhorEsforco, Travadas = travadas };

    private static bool DaFaixaDosDialetos(string? codigo) =>
        codigo is { Length: 7 } && codigo.StartsWith("INC3", StringComparison.Ordinal) && codigo[4] is >= '1' and <= '4' && char.IsAsciiDigit(codigo[5]) && char.IsAsciiDigit(codigo[6]);

    private bool TemSentinela(string texto) => Esperado.Sentinelas.Any(sentinela => sentinela.Length > 0 && texto.Contains(sentinela, StringComparison.Ordinal));

    // A saída das conexões fechadas é assíncrona no servidor: espera até um prazo, nunca um tempo fixo.
    private async Task<bool> EsperarConexoesAsync(bool destino, int esperadas)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var prazo = DateTime.UtcNow + Fixtura.PrazoDasConexoes;
        while (true)
        {
            if (await Fixtura.ContarConexoesDeLeituraAsync(Par, destino, cancelamento) <= esperadas)
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
}
