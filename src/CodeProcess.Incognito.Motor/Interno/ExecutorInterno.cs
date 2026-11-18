// O executor consome o contrato experimental de dialeto que o motor declara, sempre pelo envoltório; nada daqui é público.
#pragma warning disable INCX001

using System.Diagnostics.CodeAnalysis;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que quem executa decide: a confirmação, as tabelas ignoradas com linhas, o paralelismo e o token da restauração.</summary>
/// <param name="Confirmar">A confirmação, com o servidor, o banco e os avisos; falso não escreve nada.</param>
/// <param name="AceitarDadosExistentes">Se as tabelas ignoradas do destino podem ter linhas.</param>
/// <param name="Restauracao">
/// O token da restauração na falha e no Ctrl+C, próprio: o primeiro Ctrl+C cancela a execução, um segundo cancela este.
/// </param>
/// <param name="Paralelismo">O paralelismo das opções, que vence o da configuração; nulo usa o dela, ou 4.</param>
/// <param name="Tratamentos">Os tratamentos por tabela (K); nulo usa máx(1, núcleos ÷ P).</param>
/// <param name="GerarChave">A chave aleatória da sessão descartável do replanejamento; nulo gera (os testes passam um canário).</param>
internal sealed record OpcoesDaExecucao(
    Func<ResumoDoDestino, CancellationToken, Task<bool>> Confirmar,
    bool AceitarDadosExistentes,
    CancellationToken Restauracao,
    int? Paralelismo = null,
    int? Tratamentos = null,
    Func<ChaveIncognito>? GerarChave = null)
{
    /// <inheritdoc/>
    public override string ToString() => nameof(OpcoesDaExecucao);
}

/// <summary>
/// O resultado de uma execução (seção 4.5): a confirmação, a consistência obtida e a idade do instante, a contagem de cada
/// tabela, o reparo do começo, a restauração do fim, os avisos e a falha traduzida. Depois do preparo do diário, a execução
/// devolve o resultado, com a falha dentro (D10), também quando a restauração ou o descarte falham; antes, lança. Nunca traz valor de coluna, a chave nem o segredo de
/// conexão; o <c>ToString</c> só conta.
/// </summary>
/// <param name="Negada">Se a confirmação foi negada (nada foi escrito).</param>
/// <param name="Consistencia">A consistência que a leitura obteve; nula quando ela não abriu.</param>
/// <param name="IdadeDoInstante">A idade do instante da leitura, em transações, no fim da cópia; nula sem medida.</param>
/// <param name="Tabelas">A contagem de cada tabela concluída, na ordem em que concluíram.</param>
/// <param name="Reparo">O que o reparo do começo fez; nulo quando não chegou a reparar.</param>
/// <param name="Restauracao">A restauração do fim; nula quando não houve (confirmação negada, ou bloqueio perdido).</param>
/// <param name="Avisos">Os avisos da proteção, do reparo, da restauração, do descarte e o do destino incompleto.</param>
/// <param name="Falha">
/// A falha traduzida: a perda do bloqueio, se houve; senão a primeira que não é cancelamento, ou o cancelamento pedido pelo usuário.
/// </param>
/// <param name="ImpressaoDigital">A impressão digital da chave, só no modo reproduzível.</param>
/// <param name="PicoDeLotes">O maior número de lotes vivos ao mesmo tempo na cópia.</param>
internal sealed record ResultadoDaExecucao(
    bool Negada,
    ConsistenciaObtida? Consistencia,
    long? IdadeDoInstante,
    IReadOnlyList<ContagemDaTabela> Tabelas,
    ResultadoDoReparo? Reparo,
    ResultadoDaRestauracao? Restauracao,
    IReadOnlyList<Aviso> Avisos,
    Exception? Falha,
    string? ImpressaoDigital,
    long PicoDeLotes)
{
    /// <summary>A contagem das tabelas, copiada.</summary>
    public IReadOnlyList<ContagemDaTabela> Tabelas { get; } = [.. Tabelas];

    /// <summary>Os avisos, copiados.</summary>
    public IReadOnlyList<Aviso> Avisos { get; } = [.. Avisos];

    /// <summary>A saída: 130 &gt; 1 &gt; 4 &gt; 0.</summary>
    public int Saida => ClassificadorDeSaida.De(this);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{nameof(ResultadoDaExecucao)} ({(Negada ? "negada" : $"{Tabelas.Count} tabelas, {Avisos.Count} avisos, saída {Saida}")})";
}

/// <summary>
/// A execução de um plano (seção 4.3 da proposta; o público é do INC-063): conectar, proteger, reparar, abrir a leitura
/// consistente, replanejar e comparar, criar os transformadores, preparar o diário, copiar, medir a idade do instante e soltar a
/// origem, restaurar em fases e devolver o resultado. Tudo do dialeto pelo envoltório. Na falha depois do preparo, a tabela em
/// curso é desfeita, as outras param, e a restauração religa e recria sem validar (D9), com o token próprio, e encerra como
/// <c>Falhou</c>, com o aviso de destino incompleto (D12); no Ctrl+C, o mesmo, e sai 130, também quando ele chega no meio da
/// restauração do caminho feliz (que é refeita sem validar). A falha da restauração nunca toma o lugar do erro original: vira
/// o aviso de que o <c>reparar</c> conclui. Com o bloqueio perdido (também junto com outra falha), nada mais é escrito, nem o
/// encerramento. O reparo do começo que não restaura alguma entrada recusa antes de qualquer escrita da cópia (saída 1), e a
/// idade do instante que não se mede vira aviso. Os transformadores (e a sessão deles) são descartados antes da restauração,
/// já sem tarefa viva. No fim, sempre e nessa ordem, cada um mesmo que o anterior falhe: os transformadores que sobraram, a
/// chave, a sessão de leitura, a concessão e os dialetos.
/// </summary>
internal static class ExecutorInterno
{
    /// <summary>O paralelismo sem a configuração nem as opções.</summary>
    internal const int ParalelismoPadrao = 4;

    /// <summary>Executa o plano.</summary>
    /// <param name="plano">O plano do <c>planejar</c>.</param>
    /// <param name="registro">O registro de transformadores.</param>
    /// <param name="dialetos">O dialeto de cada provedor.</param>
    /// <param name="resolverConexao">Resolve a referência de uma conexão.</param>
    /// <param name="resolverChave">Resolve a referência da chave numa instância nova (o executor é o dono).</param>
    /// <param name="opcoes">O que quem executa decide.</param>
    /// <param name="relogio">O relógio.</param>
    /// <param name="cancelamento">O token do usuário (o primeiro Ctrl+C).</param>
    /// <returns>O resultado (depois do preparo do diário, também na falha e no cancelamento).</returns>
    /// <exception cref="PlanoRecusadoException">Antes do preparo: o destino não está protegido, ou o banco mudou (saída 2).</exception>
    /// <exception cref="ConfiguracaoInvalidaException">Antes do preparo: a referência da chave não se resolveu (saída 2).</exception>
    /// <exception cref="BloqueioDestinoException">Antes do preparo: outra execução tem o bloqueio, ou ele se perdeu.</exception>
    /// <exception cref="IncognitoException">Antes do preparo: o reparo do começo não restaurou alguma entrada (<c>INC4012</c>, saída 1).</exception>
    /// <exception cref="FalhaDialetoException">Antes do preparo: o dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Antes do preparo: cancelado pelo usuário.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Depois do preparo, o defeito no descarte dos transformadores vira a falha do resultado (D10).")]
    internal static async Task<ResultadoDaExecucao> ExecutarAsync(
        Plano plano,
        RegistroTransformadores registro,
        IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos,
        Func<ConexaoBanco, SegredoConexao> resolverConexao,
        Func<ReferenciaEscrita, ChaveIncognito?> resolverChave,
        OpcoesDaExecucao opcoes,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(plano);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(dialetos);
        ArgumentNullException.ThrowIfNull(resolverConexao);
        ArgumentNullException.ThrowIfNull(resolverChave);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(relogio);

        var recursos = new Recursos();
        try
        {
            return await ExecutarComRecursosAsync(plano, registro, dialetos, resolverConexao, resolverChave, opcoes, relogio, recursos, cancelamento).ConfigureAwait(false);
        }
        finally
        {
            await recursos.DescartarAsync().ConfigureAwait(false);
        }
    }

    private static async Task<ResultadoDaExecucao> ExecutarComRecursosAsync(
        Plano plano,
        RegistroTransformadores registro,
        IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos,
        Func<ConexaoBanco, SegredoConexao> resolverConexao,
        Func<ReferenciaEscrita, ChaveIncognito?> resolverChave,
        OpcoesDaExecucao opcoes,
        TimeProvider relogio,
        Recursos recursos,
        CancellationToken cancelamento)
    {
        // 1. Conectar, pelo envoltório.
        var (daOrigem, doDestino) = Replanejador.Conexoes(plano);
        var erros = new List<ErroPlano>();
        var dialetoDaOrigem = PlanejadorInterno.Dialeto(dialetos, daOrigem, LadoBanco.Origem, erros);
        var dialetoDoDestino = PlanejadorInterno.Dialeto(dialetos, doDestino, LadoBanco.Destino, erros);
        if (erros.Count > 0)
        {
            throw new PlanoRecusadoException(erros);
        }

        var origem = recursos.Origem = await PlanejadorInterno.ConectarAsync(dialetoDaOrigem!, daOrigem, LadoBanco.Origem, resolverConexao, cancelamento).ConfigureAwait(false);
        var destino = recursos.Destino = await PlanejadorInterno.ConectarAsync(dialetoDoDestino!, doDestino, LadoBanco.Destino, resolverConexao, cancelamento).ConfigureAwait(false);

        // 2. Proteção: as ignoradas da carga original (desvio declarado, item 32: as unidades do refeito são as mesmas).
        var original = plano.Execucao;
        var ignoradas = original.Unidades.Where(unidade => unidade.Acao == AcaoTabela.Ignorar && unidade.NoDestino is not null).Select(unidade => Objeto(unidade.NoDestino!)).ToArray();
        var protecao = await ProtecaoDoDestino.ConferirAsync(origem, destino, original.Configuracao.Destino.BancoEsperado, ignoradas, opcoes.AceitarDadosExistentes, opcoes.Confirmar, cancelamento)
            .ConfigureAwait(false);
        var avisos = new List<Aviso>(protecao.Avisos);
        if (protecao.Concessao is not { } concessao)
        {
            return new ResultadoDaExecucao(true, null, null, [], null, null, avisos, null, null, 0);
        }

        recursos.Concessao = concessao;

        // 3. Reparo do que uma execução interrompida deixou, sem validar.
        var reparo = await ReparadorDoDestino.RepararAsync(destino, concessao, protecao.Marcador, new OpcoesDoReparo(Validar: false), relogio, cancelamento).ConfigureAwait(false);
        avisos.AddRange(reparo.Restauracao.Ocorrencias());
        if (reparo.Restauracao.Falhas.Count > 0)
        {
            // A cópia não começa sobre um destino que o reparo não fechou (uma trigger que não religou ficaria desligada).
            throw new IncognitoException(
                CodigosExecucao.ReparoIncompleto,
                "O reparo do que uma execução interrompida deixou no destino não terminou: nada da cópia foi escrito; rode o reparar e execute de novo.");
        }

        // 4. Leitura consistente: P leitores, as copiadas travadas.
        var paralelismo = Paralelismo(original, opcoes);
        var leitura = await LeituraConsistente.AbrirAsync(origem, original, paralelismo, cancelamento).ConfigureAwait(false);
        recursos.Leitura = leitura.Sessao;

        // 5 e 6. Replanejamento e comparação: dali em diante, só a carga refeita.
        var leituraDaOrigem = new LeituraDoBanco(leitura.Esquema, await origem.LerIdentidadeAsync(cancelamento).ConfigureAwait(false));
        var leituraDoDestino = new LeituraDoBanco(
            await destino.LerEsquemaAsync(cancelamento).ConfigureAwait(false), await destino.LerIdentidadeAsync(cancelamento).ConfigureAwait(false), destino.ObjetosDoDiario());
        var carga = await Replanejador.ReplanejarAsync(
            plano, registro, leituraDaOrigem, leituraDoDestino, origem.CompararNome, destino.CompararNome, leitura.Sessao.Leitores[0].LerMaiorValorAsync, opcoes.GerarChave ?? ChaveIncognito.Gerar, cancelamento)
            .ConfigureAwait(false);

        // 7. A chave e os transformadores, criados com ela e conferidos.
        var chave = recursos.Chave = ChaveDaExecucao.Obter(carga.Configuracao, resolverChave);
        var impressao = carga.Configuracao.Modo == ModoTratamento.Reproduzivel ? chave.ImpressaoDigital : null;
        recursos.Transformadores = TransformadoresDaExecucao.Criar(carga, chave, cancelamento);

        // 8. O preparo do diário: uma falha aqui deixa o diário para o reparo da próxima execução. Dali em diante, sempre o resultado.
        var execucao = await DiarioDoDestino.PrepararAsync(destino, concessao, protecao.Marcador, carga.ChavesRemovidas, carga.TriggersDesligadas, relogio, cancelamento).ConfigureAwait(false);

        var contador = new ContadorDoPipeline();
        var (tabelas, falha) = await CopiarAsync(carga, leitura.Sessao, destino, recursos.Transformadores, concessao, paralelismo, opcoes, contador, relogio, recursos, cancelamento)
            .ConfigureAwait(false);

        // 10. A idade do instante, e a origem solta (as travas saem com a sessão).
        long? idade = null;
        if (falha is null)
        {
            try
            {
                idade = await leitura.Sessao.LerIdadeAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                // A idade é medida, não cópia: a falha dela vira aviso; o Ctrl+C e a perda do bloqueio continuam sendo a falha.
                var classificada = Classificar(excecao, concessao, cancelamento);
                if (classificada is OperationCanceledException or BloqueioDestinoException)
                {
                    falha = classificada;
                }
                else
                {
                    avisos.Add(new Aviso(CodigosExecucao.IdadeNaoMedida, $"A idade do instante da leitura não foi medida ({TextoSeguro.Nome(classificada.GetType().Name)}); a cópia não muda."));
                }
            }
        }

        await recursos.SoltarLeituraAsync().ConfigureAwait(false);

        // Os transformadores (e a sessão deles), descartados antes da restauração (nenhuma tarefa vive depois da cópia): o defeito
        // no descarte de uma embutida vira a falha e segue o caminho dela (D10).
        try
        {
            avisos.AddRange(recursos.DescartarTransformadores());
        }
        catch (Exception excecao) when (falha is null)
        {
            falha = excecao;
        }
        catch (Exception excecao)
        {
            avisos.Add(new Aviso(
                CodigosExecucao.DescarteDoTransformador,
                $"O descarte dos transformadores falhou ({TextoSeguro.Nome(excecao.GetType().Name)}) depois de outra falha; a saída é a dela."));
        }

        // 11. A restauração em fases; com o bloqueio perdido (também junto com outra falha), nada mais é escrito.
        if (concessao.EstaPerdida && falha is not BloqueioDestinoException)
        {
            falha = Perda(concessao);
        }

        ResultadoDaRestauracao? restauracao = null;
        if (falha is not BloqueioDestinoException)
        {
            Aviso? incompleta;
            (restauracao, falha, incompleta) = await RestaurarAsync(destino, concessao, execucao, falha, opcoes, relogio, cancelamento).ConfigureAwait(false);
            avisos.AddRange(restauracao.Ocorrencias());
            if (incompleta is not null)
            {
                avisos.Add(incompleta);
            }
        }

        if (falha is not null)
        {
            avisos.Add(new Aviso(CodigosExecucao.DestinoIncompleto, "A execução não terminou: o destino está incompleto e não deve ser usado; corrija a causa e execute de novo."));
        }

        return new ResultadoDaExecucao(false, leitura.Sessao.Obtida, idade, tabelas, reparo, restauracao, avisos, falha, impressao, contador.Pico);
    }

    // 9. A cópia: P trabalhadores, cada um com o seu leitor, pegam as unidades na ordem do plano; a primeira falha que não é
    // cancelamento vence e para as outras; a perda do bloqueio cancela tudo.
    private static async Task<(IReadOnlyList<ContagemDaTabela> Tabelas, Exception? Falha)> CopiarAsync(
        DadosDeExecucao carga,
        DialetoProtegido.SessaoProtegida sessao,
        DialetoProtegido destino,
        TransformadoresDaExecucao transformadores,
        DialetoProtegido.ConcessaoProtegida concessao,
        int paralelismo,
        OpcoesDaExecucao opcoes,
        ContadorDoPipeline contador,
        TimeProvider relogio,
        Recursos recursos,
        CancellationToken cancelamento)
    {
        var unidades = carga.Unidades.Where(unidade => unidade.Acao is AcaoTabela.Copiar or AcaoTabela.Esvaziar).ToArray();
        var tratamentos = Math.Max(1, opcoes.Tratamentos ?? Environment.ProcessorCount / paralelismo);
        var copia = recursos.Copia = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, concessao.Perdido);
        var tabelas = new List<ContagemDaTabela>();
        Exception? primeira = null;
        var proxima = -1;

        async Task TrabalharAsync(DialetoProtegido.LeitorProtegido leitor)
        {
            try
            {
                for (var indice = Interlocked.Increment(ref proxima); indice < unidades.Length; indice = Interlocked.Increment(ref proxima))
                {
                    copia.Token.ThrowIfCancellationRequested();
                    var contagem = await CopiaDaTabela.CopiarAsync(unidades[indice], leitor, destino, transformadores, tratamentos, concessao.ConferirAsync, contador, relogio, copia.Token)
                        .ConfigureAwait(false);
                    lock (tabelas)
                    {
                        tabelas.Add(contagem);
                    }
                }
            }
            catch (Exception excecao)
            {
                var classificada = Classificar(excecao, concessao, cancelamento);
                if (classificada is not OperationCanceledException && Interlocked.CompareExchange(ref primeira, classificada, null) is null)
                {
                    await copia.CancelAsync().ConfigureAwait(false);
                }
            }
        }

        await Task.WhenAll(sessao.Leitores.Take(Math.Min(paralelismo, sessao.Leitores.Count)).Select(leitor => Task.Run(() => TrabalharAsync(leitor), CancellationToken.None))).ConfigureAwait(false);
        var falha = (concessao.EstaPerdida ? Perda(concessao) : null)
            ?? Volatile.Read(ref primeira)
            ?? (cancelamento.IsCancellationRequested ? new OperationCanceledException(cancelamento) : (Exception?)null);
        lock (tabelas)
        {
            return (tabelas.ToArray(), falha);
        }
    }

    // Sucesso: religar, (INC-039), recriar e validar, (INC-040), encerrar concluída. Falha ou Ctrl+C, também no meio da
    // restauração do caminho feliz: religar e recriar sem validar, com o token próprio, e encerrar como falhou (D9). A falha da
    // restauração nunca toma o lugar da original: vira o aviso de que o reparar conclui.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A falha da restauração vira a falha (no caminho feliz) ou um aviso só com o tipo; nunca some.")]
    private static async Task<(ResultadoDaRestauracao Restauracao, Exception? Falha, Aviso? Incompleta)> RestaurarAsync(
        DialetoProtegido destino,
        DialetoProtegido.ConcessaoProtegida concessao,
        Guid execucao,
        Exception? falha,
        OpcoesDaExecucao opcoes,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        var restauracao = ResultadoDaRestauracao.Vazio;
        if (falha is null)
        {
            try
            {
                restauracao = await DiarioDoDestino.ReligarAsync(destino, concessao, execucao, cancelamento).ConfigureAwait(false);

                // Gancho do INC-039: as sequences e o REFRESH das materialized views.
                restauracao = restauracao.Mais(await DiarioDoDestino.RecriarAsync(destino, concessao, execucao, validar: true, cancelamento).ConfigureAwait(false));

                // Gancho do INC-040: as validações da cópia.
                await DiarioDoDestino.EncerrarAsync(destino, concessao, execucao, SituacaoExecucao.Concluida, relogio, cancelamento).ConfigureAwait(false);
                return (restauracao, null, null);
            }
            catch (Exception excecao)
            {
                falha = Classificar(excecao, concessao, cancelamento);
                if (falha is BloqueioDestinoException)
                {
                    return (restauracao, falha, null);
                }
            }
        }

        try
        {
            // A segunda volta não acha mais o que a primeira já devolveu: os avisos da primeira seguem junto.
            var refeita = new ResultadoDaRestauracao([], [], restauracao.Avisos);
            refeita = refeita.Mais(await DiarioDoDestino.ReligarAsync(destino, concessao, execucao, opcoes.Restauracao).ConfigureAwait(false));
            refeita = refeita.Mais(await DiarioDoDestino.RecriarAsync(destino, concessao, execucao, validar: false, opcoes.Restauracao).ConfigureAwait(false));
            await DiarioDoDestino.EncerrarAsync(destino, concessao, execucao, SituacaoExecucao.Falhou, relogio, opcoes.Restauracao).ConfigureAwait(false);
            return (refeita, falha, null);
        }
        catch (Exception excecao)
        {
            var classificada = Classificar(excecao, concessao, opcoes.Restauracao);
            if (classificada is BloqueioDestinoException)
            {
                // A perda do bloqueio vence a falha original: nada mais é escrito.
                return (restauracao, classificada, null);
            }

            return (restauracao, falha, new Aviso(
                CodigosExecucao.RestauracaoIncompleta,
                $"A restauração do destino não terminou ({TextoSeguro.Nome(classificada.GetType().Name)}): rode o reparar antes de usar o destino."));
        }
    }

    // A falha como sai: a perda do bloqueio como INC4001; o cancelamento só quando o usuário pediu; o resto como veio
    // (já traduzido pelo envoltório).
    private static Exception Classificar(Exception excecao, DialetoProtegido.ConcessaoProtegida concessao, CancellationToken cancelamento) =>
        excecao switch
        {
            BloqueioDestinoException => excecao,
            _ when concessao.EstaPerdida => Perda(concessao),
            OperationCanceledException when cancelamento.IsCancellationRequested => new OperationCanceledException(cancelamento),
            _ => excecao,
        };

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Só monta a exceção da perda, que a concessão lança.")]
    private static BloqueioDestinoException Perda(DialetoProtegido.ConcessaoProtegida concessao)
    {
        try
        {
            concessao.LancarSePerdida();
        }
        catch (BloqueioDestinoException perda)
        {
            return perda;
        }

        throw new InvalidOperationException("A perda do bloqueio foi pedida com o bloqueio vivo.");
    }

    // P = o paralelismo das opções, ou o da configuração, ou 4, limitado às unidades tocadas e ao máximo da leitura, no mínimo 1.
    private static int Paralelismo(DadosDeExecucao carga, OpcoesDaExecucao opcoes)
    {
        var tocadas = carga.Unidades.Count(unidade => unidade.Acao is AcaoTabela.Copiar or AcaoTabela.Esvaziar);
        var pedido = opcoes.Paralelismo ?? carga.Configuracao.Paralelismo ?? ParalelismoPadrao;
        return Math.Clamp(Math.Min(pedido, tocadas), 1, PedidoLeitura.MaximoDeLeitores);
    }

    private static NomeObjeto Objeto(NomeTabela nome) => new(nome.Esquema, nome.Tabela);

    // O que a execução segura, descartado no fim nessa ordem, cada um mesmo que o anterior falhe.
    private sealed class Recursos
    {
        internal DialetoProtegido? Origem { get; set; }

        internal DialetoProtegido? Destino { get; set; }

        internal DialetoProtegido.ConcessaoProtegida? Concessao { get; set; }

        internal DialetoProtegido.SessaoProtegida? Leitura { get; set; }

        internal ChaveIncognito? Chave { get; set; }

        internal TransformadoresDaExecucao? Transformadores { get; set; }

        internal CancellationTokenSource? Copia { get; set; }

        // Os transformadores (e a sessão deles) uma vez só: no fim do caminho feliz, com os avisos, ou no descarte.
        internal IReadOnlyList<Aviso> DescartarTransformadores()
        {
            var transformadores = Transformadores;
            Transformadores = null;
            return transformadores?.Descartar() ?? [];
        }

        internal async Task SoltarLeituraAsync()
        {
            var leitura = Leitura;
            Leitura = null;
            if (leitura is not null)
            {
                await leitura.DisposeAsync().ConfigureAwait(false);
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cada descarte roda mesmo que o anterior falhe, e nenhum toma o lugar do erro da execução.")]
        internal async Task DescartarAsync()
        {
            try
            {
                DescartarTransformadores();
            }
            catch
            {
            }

            try
            {
                Chave?.Dispose();
            }
            catch
            {
            }

            try
            {
                await SoltarLeituraAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            if (Concessao is { } concessao)
            {
                await concessao.DisposeAsync().ConfigureAwait(false);
            }

            // O token da cópia cancelado antes de descartar o destino (registro do PR 2).
            if (Copia is { } copia)
            {
                try
                {
                    await copia.CancelAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                copia.Dispose();
            }

            await PlanejadorInterno.FecharAsync(Destino, Origem).ConfigureAwait(false);
        }
    }
}
