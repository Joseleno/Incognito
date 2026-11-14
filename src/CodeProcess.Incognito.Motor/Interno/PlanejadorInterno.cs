// O planejador consome o contrato experimental de dialeto que o motor declara, sempre pelo envoltório; nada daqui é
// público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que o dialeto leu de um banco.</summary>
/// <param name="Esquema">O esquema, já conferido.</param>
/// <param name="Identidade">A identidade.</param>
/// <param name="ObjetosDoDiario">
/// No destino, o que o diário do Incognito pode deixar no esquema reservado (o <c>planejar</c> aceita isso no destino); nula
/// na origem.
/// </param>
internal sealed record LeituraDoBanco(EsquemaBanco Esquema, IdentidadeLida Identidade, IReadOnlyList<ObjetoReservado>? ObjetosDoDiario = null);


/// <summary>
/// O <c>planejar</c>: resolve as regras sem o banco, conecta e lê a origem e o destino, em série e sempre pelo envoltório
/// do dialeto, confere a coerência das duas leituras e monta o plano, comparando os nomes pelo dialeto de cada lado com as
/// conexões abertas. Nunca resolve a referência da chave: o <c>planejar</c> roda sem o segredo, e o resolvedor só recebe
/// as conexões.
/// </summary>
internal static class PlanejadorInterno
{
    /// <summary>Planeja.</summary>
    /// <param name="configuracao">A configuração lida.</param>
    /// <param name="registro">O registro de transformadores.</param>
    /// <param name="dialetos">O dialeto de cada provedor.</param>
    /// <param name="resolverConexao">Resolve a referência de uma conexão (origem ou destino) na string de conexão.</param>
    /// <param name="cancelamento">Cancela o planejamento.</param>
    /// <param name="gerarChave">
    /// O gerador da chave aleatória da sessão descartável em que os transformadores são criados; nulo usa
    /// <see cref="ChaveIncognito.Gerar"/> (os testes passam uma chave canário).
    /// </param>
    /// <returns>O plano.</returns>
    /// <exception cref="ConfiguracaoInvalidaException">Regra recusada pelo registro.</exception>
    /// <exception cref="PlanoRecusadoException">
    /// Sem dialeto para o provedor, o dialeto recusou a conexão ou a leitura, ou a configuração e o esquema não permitem
    /// copiar.
    /// </exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou leu um esquema que se contradiz.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<Plano> PlanejarAsync(
        ConfiguracaoIncognito configuracao,
        RegistroTransformadores registro,
        IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos,
        Func<ConexaoBanco, SegredoConexao> resolverConexao,
        CancellationToken cancelamento,
        Func<ChaveIncognito>? gerarChave = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(dialetos);
        ArgumentNullException.ThrowIfNull(resolverConexao);

        // P0: as regras e o hash da configuração, antes do banco.
        var regras = ResolvedorDeRegras.Resolver(configuracao, registro);
        var hash = HashConfiguracao.Calcular(configuracao, regras);

        // P1: o dialeto de cada lado, e as duas leituras em série.
        var erros = new List<ErroPlano>();
        var dialetoDaOrigem = Dialeto(dialetos, configuracao.Origem, LadoBanco.Origem, erros);
        var dialetoDoDestino = Dialeto(dialetos, configuracao.Destino, LadoBanco.Destino, erros);
        if (erros.Count > 0)
        {
            throw new PlanoRecusadoException(erros);
        }

        DialetoProtegido? origem = null;
        DialetoProtegido? destino = null;
        PlanoMontado montado;
        try
        {
            origem = await ConectarAsync(dialetoDaOrigem!, configuracao.Origem, LadoBanco.Origem, resolverConexao, cancelamento).ConfigureAwait(false);
            var leituraDaOrigem = await LerAsync(origem, LadoBanco.Origem, cancelamento).ConfigureAwait(false);
            destino = await ConectarAsync(dialetoDoDestino!, configuracao.Destino, LadoBanco.Destino, resolverConexao, cancelamento).ConfigureAwait(false);
            var leituraDoDestino = await LerAsync(destino, LadoBanco.Destino, cancelamento).ConfigureAwait(false);

            // P2 a P9: a montagem, pelo caminho que o replanejamento da execução também usa; o maior valor de cada coluna do
            // id.permutar, em série, pelo envoltório da origem.
            montado = await MontarAsync(
                configuracao, regras, registro, origem.CompararNome, destino.CompararNome, leituraDaOrigem, leituraDoDestino, origem.LerMaiorValorAsync, gerarChave ?? ChaveIncognito.Gerar, cancelamento)
                .ConfigureAwait(false);
        }
        catch
        {
            // O erro do planejar é o que sai: a falha ao fechar nunca toma o lugar dele.
            await FecharAsync(destino, origem).ConfigureAwait(false);
            throw;
        }

        if (await FecharAsync(destino, origem).ConfigureAwait(false) is { } aoFechar)
        {
            throw aoFechar;
        }

        return new Plano(montado.Tabelas, montado.Avisos, hash, montado.Execucao, configuracao);
    }

    /// <summary>
    /// A montagem do plano a partir das duas leituras, o caminho único do <c>planejar</c> e do replanejamento da execução:
    /// a coerência de cada leitura, os nomes, a propagação, o maior valor de cada coluna do <c>id.permutar</c> (lido em série
    /// por quem chama), a criação dos transformadores numa sessão descartável, as restrições, o acesso e a publicação.
    /// </summary>
    /// <param name="configuracao">A configuração.</param>
    /// <param name="regras">As regras resolvidas.</param>
    /// <param name="registro">O registro de transformadores.</param>
    /// <param name="daOrigem">A comparação de nomes do banco da origem, com a conexão aberta.</param>
    /// <param name="doDestino">A comparação de nomes do banco do destino, com a conexão aberta.</param>
    /// <param name="origem">A leitura da origem.</param>
    /// <param name="destino">A leitura do destino.</param>
    /// <param name="lerMaiorValor">
    /// Lê o maior valor de uma coluna da origem: no <c>planejar</c>, pela conexão de controle; na execução, por um leitor da
    /// sessão, no instante dela.
    /// </param>
    /// <param name="gerarChave">O gerador da chave aleatória da sessão descartável.</param>
    /// <param name="cancelamento">Cancela a montagem.</param>
    /// <returns>O plano montado.</returns>
    /// <exception cref="PlanoRecusadoException">A configuração e o esquema não permitem copiar.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou leu um esquema que se contradiz.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<PlanoMontado> MontarAsync(
        ConfiguracaoIncognito configuracao,
        ResolucaoDeRegras regras,
        RegistroTransformadores registro,
        ComparadorDeNomes daOrigem,
        ComparadorDeNomes doDestino,
        LeituraDoBanco origem,
        LeituraDoBanco destino,
        Func<ReferenciaColuna, CancellationToken, Task<long?>> lerMaiorValor,
        Func<ChaveIncognito> gerarChave,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(lerMaiorValor);
        Conferir(origem.Esquema, configuracao.Origem, LadoBanco.Origem);
        Conferir(destino.Esquema, configuracao.Destino, LadoBanco.Destino);

        // P3 a P6b: os nomes pelo dialeto de cada lado e as colunas do id.permutar a ler; o maior valor fica na montagem,
        // que só publica o N.
        var montagem = MontagemDoPlano.Preparar(configuracao, regras, registro, daOrigem, doDestino, origem, destino);
        foreach (var coluna in montagem.Leituras)
        {
            montagem.Anotar(coluna, await lerMaiorValor(coluna, cancelamento).ConfigureAwait(false));
        }

        // P7 a P9: a criação dos transformadores, as restrições, o acesso e a publicação.
        return montagem.Concluir(gerarChave, cancelamento);
    }

    // P11: fecha em ordem inversa, cada um mesmo que o anterior falhe, e devolve a primeira falha.
    internal static async Task<FalhaDialetoException?> FecharAsync(params DialetoProtegido?[] bancos)
    {
        FalhaDialetoException? primeira = null;
        foreach (var banco in bancos.OfType<DialetoProtegido>())
        {
            try
            {
                await banco.DisposeAsync().ConfigureAwait(false);
            }
            catch (FalhaDialetoException falha)
            {
                primeira ??= falha;
            }
        }

        return primeira;
    }

    internal static ProvedorDialeto? Dialeto(IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos, ConexaoBanco conexao, LadoBanco lado, List<ErroPlano> erros)
    {
        var provedor = ValidadorConfiguracao.Nome(conexao.Provedor);
        if (!dialetos.TryGetValue(conexao.Provedor, out var dialeto) || dialeto is null)
        {
            erros.Add(new ErroPlano(CodigosPlanejar.DialetoAusente, $"Não há dialeto para o provedor {provedor} {Do(lado)}: instale e registre o pacote do dialeto."));
            return null;
        }

        if (!DialetoProtegido.ChamaSe(dialeto, provedor))
        {
            erros.Add(new ErroPlano(CodigosPlanejar.DialetoAusente, $"O dialeto registrado para o provedor {provedor} {Do(lado)} é de outro provedor."));
            return null;
        }

        return dialeto;
    }

    internal static Task<DialetoProtegido> ConectarAsync(
        ProvedorDialeto dialeto, ConexaoBanco conexao, LadoBanco lado, Func<ConexaoBanco, SegredoConexao> resolverConexao, CancellationToken cancelamento)
    {
        var segredo = resolverConexao(conexao) ?? throw new InvalidOperationException("O resolvedor de conexões não devolveu a conexão.");
        return DialetoProtegido.ConectarAsync(dialeto, ValidadorConfiguracao.Nome(conexao.Provedor), lado, segredo, cancelamento);
    }

    private static async Task<LeituraDoBanco> LerAsync(DialetoProtegido banco, LadoBanco lado, CancellationToken cancelamento)
    {
        var esquema = await banco.LerEsquemaAsync(cancelamento).ConfigureAwait(false);
        var identidade = await banco.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
        return new LeituraDoBanco(esquema, identidade, lado == LadoBanco.Destino ? banco.ObjetosDoDiario() : null);
    }

    private static void Conferir(EsquemaBanco esquema, ConexaoBanco conexao, LadoBanco lado)
    {
        var problemas = ConsistenciaDoEsquema.Problemas(esquema);
        if (problemas.Count == 0)
        {
            return;
        }

        var mais = problemas.Count > 1 ? $" (e mais {problemas.Count - 1})" : "";
        throw new FalhaDialetoException(
            CodigosPlanejar.EsquemaIncoerente,
            $"O dialeto de {ValidadorConfiguracao.Nome(conexao.Provedor)} leu {Do(lado)} um esquema que se contradiz: {problemas[0]}{mais}.");
    }

    private static string Do(LadoBanco lado) => lado == LadoBanco.Origem ? "da origem" : "do destino";
}
