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

            // P2: a coerência das duas leituras.
            Conferir(leituraDaOrigem.Esquema, configuracao.Origem, LadoBanco.Origem);
            Conferir(leituraDoDestino.Esquema, configuracao.Destino, LadoBanco.Destino);

            // P3 a P6b: os nomes pelo dialeto de cada lado, com as conexões abertas, e as colunas do id.permutar a ler.
            var montagem = MontagemDoPlano.Preparar(configuracao, regras, registro, origem.CompararNome, destino.CompararNome, leituraDaOrigem, leituraDoDestino);

            // O maior valor de cada uma, em série, pelo envoltório da origem: fica na montagem, que só publica o N.
            foreach (var coluna in montagem.Leituras)
            {
                montagem.Anotar(coluna, await origem.LerMaiorValorAsync(coluna, cancelamento).ConfigureAwait(false));
            }

            // P7 a P9: a criação dos transformadores, as restrições, o acesso e a publicação.
            montado = montagem.Concluir(gerarChave ?? ChaveIncognito.Gerar, cancelamento);
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

        return new Plano(montado.Tabelas, montado.Avisos, hash, montado.Execucao);
    }

    // P11: fecha em ordem inversa, cada um mesmo que o anterior falhe, e devolve a primeira falha.
    private static async Task<FalhaDialetoException?> FecharAsync(params DialetoProtegido?[] bancos)
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

    private static ProvedorDialeto? Dialeto(IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos, ConexaoBanco conexao, LadoBanco lado, List<ErroPlano> erros)
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

    private static Task<DialetoProtegido> ConectarAsync(
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
