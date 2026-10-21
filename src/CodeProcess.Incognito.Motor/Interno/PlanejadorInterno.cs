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
internal sealed record LeituraDoBanco(EsquemaBanco Esquema, IdentidadeLida Identidade);

/// <summary>As regras resolvidas e as leituras dos dois bancos, que o plano vai conferir.</summary>
/// <param name="Regras">As regras da configuração resolvidas contra o registro.</param>
/// <param name="Origem">A leitura da origem.</param>
/// <param name="Destino">A leitura do destino.</param>
internal sealed record LeiturasDoPlano(ResolucaoDeRegras Regras, LeituraDoBanco Origem, LeituraDoBanco Destino);

/// <summary>
/// O <c>planejar</c>: resolve as regras sem o banco, conecta e lê a origem e o destino, em série e sempre pelo envoltório
/// do dialeto, e confere a coerência das duas leituras. Nunca resolve a referência da chave: o <c>planejar</c> roda sem o
/// segredo, e o resolvedor só recebe as conexões.
/// </summary>
internal static class PlanejadorInterno
{
    /// <summary>Planeja.</summary>
    /// <param name="configuracao">A configuração lida.</param>
    /// <param name="registro">O registro de transformadores.</param>
    /// <param name="dialetos">O dialeto de cada provedor.</param>
    /// <param name="resolverConexao">Resolve a referência de uma conexão (origem ou destino) na string de conexão.</param>
    /// <param name="cancelamento">Cancela o planejamento.</param>
    /// <returns>As regras e as leituras conferidas.</returns>
    /// <exception cref="ConfiguracaoInvalidaException">Regra recusada pelo registro.</exception>
    /// <exception cref="PlanoRecusadoException">Sem dialeto para o provedor, ou o dialeto recusou a conexão ou a leitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou leu um esquema que se contradiz.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<LeiturasDoPlano> PlanejarAsync(
        ConfiguracaoIncognito configuracao,
        RegistroTransformadores registro,
        IReadOnlyDictionary<ProvedorBanco, ProvedorDialeto> dialetos,
        Func<ConexaoBanco, SegredoConexao> resolverConexao,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(dialetos);
        ArgumentNullException.ThrowIfNull(resolverConexao);

        // P0: as regras, antes do banco.
        var regras = ResolvedorDeRegras.Resolver(configuracao, registro);

        // P1: o dialeto de cada lado, e as duas leituras em série.
        var erros = new List<ErroPlano>();
        var dialetoDaOrigem = Dialeto(dialetos, configuracao.Origem, LadoBanco.Origem, erros);
        var dialetoDoDestino = Dialeto(dialetos, configuracao.Destino, LadoBanco.Destino, erros);
        if (erros.Count > 0)
        {
            throw new PlanoRecusadoException(erros);
        }

        var origem = await LerAsync(dialetoDaOrigem!, configuracao.Origem, LadoBanco.Origem, resolverConexao, cancelamento).ConfigureAwait(false);
        var destino = await LerAsync(dialetoDoDestino!, configuracao.Destino, LadoBanco.Destino, resolverConexao, cancelamento).ConfigureAwait(false);

        // P2: a coerência das duas leituras.
        Conferir(origem.Esquema, configuracao.Origem, LadoBanco.Origem);
        Conferir(destino.Esquema, configuracao.Destino, LadoBanco.Destino);
        return new LeiturasDoPlano(regras, origem, destino);
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

    // Conecta, lê o esquema e a identidade e fecha. A falha ao fechar só sai quando não houve erro antes: nunca toma o
    // lugar do primeiro.
    private static async Task<LeituraDoBanco> LerAsync(
        ProvedorDialeto dialeto, ConexaoBanco conexao, LadoBanco lado, Func<ConexaoBanco, SegredoConexao> resolverConexao, CancellationToken cancelamento)
    {
        var provedor = ValidadorConfiguracao.Nome(conexao.Provedor);
        var segredo = resolverConexao(conexao) ?? throw new InvalidOperationException("O resolvedor de conexões não devolveu a conexão.");
        var banco = await DialetoProtegido.ConectarAsync(dialeto, provedor, lado, segredo, cancelamento).ConfigureAwait(false);
        var falhou = true;
        try
        {
            var esquema = await banco.LerEsquemaAsync(cancelamento).ConfigureAwait(false);
            var identidade = await banco.LerIdentidadeAsync(cancelamento).ConfigureAwait(false);
            falhou = false;
            return new LeituraDoBanco(esquema, identidade);
        }
        finally
        {
            try
            {
                await banco.DisposeAsync().ConfigureAwait(false);
            }
            catch (FalhaDialetoException) when (falhou)
            {
                // O erro da leitura é o que sai.
            }
        }
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
