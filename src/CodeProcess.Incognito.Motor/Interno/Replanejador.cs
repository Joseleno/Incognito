// O replanejador lê o esquema relido dos dois bancos, tipos do contrato experimental de dialeto que o motor declara; nada
// daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O replanejamento da execução (passos 5 e 6): a montagem roda de novo, pelo mesmo caminho do <c>planejar</c>, com a
/// configuração de que o plano saiu, o registro, a origem relida no instante da leitura e o destino relido depois do reparo,
/// e a carga refeita é comparada campo a campo com a original. Divergência é recusa com 2 (<c>INC3005</c>), antes de
/// qualquer escrita da cópia; sem ela, a carga refeita é a que a execução usa dali em diante. É o único leitor de
/// <see cref="Plano.FonteDoPlano"/> (guarda de arquitetura).
/// </summary>
internal static class Replanejador
{
    /// <summary>Replaneja e compara.</summary>
    /// <param name="plano">O plano do <c>planejar</c>.</param>
    /// <param name="registro">O registro de transformadores.</param>
    /// <param name="origem">A origem relida no instante da sessão de leitura.</param>
    /// <param name="destino">O destino relido pela conexão de controle, depois do reparo.</param>
    /// <param name="daOrigem">A comparação de nomes do banco da origem, com a conexão aberta.</param>
    /// <param name="doDestino">A comparação de nomes do banco do destino, com a conexão aberta.</param>
    /// <param name="lerMaiorValor">Lê o maior valor de uma coluna da origem por um leitor da sessão, no instante dela.</param>
    /// <param name="gerarChave">O gerador da chave aleatória da sessão descartável da montagem.</param>
    /// <param name="cancelamento">Cancela o replanejamento.</param>
    /// <returns>A carga refeita, igual à original.</returns>
    /// <exception cref="PlanoRecusadoException">
    /// O banco mudou desde o <c>planejar</c> (<c>INC3005</c> primeiro; com os erros da montagem, quando ela recusou).
    /// </exception>
    /// <exception cref="ConfiguracaoInvalidaException">O registro recusou uma regra que o <c>planejar</c> aceitou.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou leu um esquema que se contradiz.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<DadosDeExecucao> ReplanejarAsync(
        Plano plano,
        RegistroTransformadores registro,
        LeituraDoBanco origem,
        LeituraDoBanco destino,
        ComparadorDeNomes daOrigem,
        ComparadorDeNomes doDestino,
        Func<ReferenciaColuna, CancellationToken, Task<long?>> lerMaiorValor,
        Func<ChaveIncognito> gerarChave,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(plano);
        ArgumentNullException.ThrowIfNull(registro);
        var configuracao = plano.FonteDoPlano;
        var regras = ResolvedorDeRegras.Resolver(configuracao, registro);

        // O hash sai das regras resolvidas: só muda se o registro mudou entre o planejar e a execução, e aí não foi o banco
        // (conferido antes da montagem, que recusaria com a causa errada).
        if (!string.Equals(HashConfiguracao.Calcular(configuracao, regras), plano.HashConfiguracao, StringComparison.Ordinal))
        {
            throw new PlanoRecusadoException([new ErroPlano(
                CodigosPlanejar.BancoMudou,
                "A configuração ou o registro de transformadores mudou desde o planejar: planeje de novo antes de executar.")]);
        }

        PlanoMontado refeito;
        try
        {
            refeito = await PlanejadorInterno.MontarAsync(configuracao, regras, registro, daOrigem, doDestino, origem, destino, lerMaiorValor, gerarChave, cancelamento)
                .ConfigureAwait(false);
        }
        catch (PlanoRecusadoException recusa)
        {
            // O banco mudou a ponto de a montagem recusar: os erros dela seguem, depois do que diz por quê.
            throw new PlanoRecusadoException([Mudou("o banco"), .. recusa.Erros]);
        }

        var divergencia = ComparadorDaCarga.Divergencia(plano.Execucao, refeito.Execucao);
        return divergencia is null ? refeito.Execucao : throw new PlanoRecusadoException([Mudou(divergencia)]);
    }

    /// <summary>
    /// As conexões da configuração de que o plano saiu: a execução conecta antes de replanejar, e só as conexões saem daqui
    /// (a regra escrita nunca).
    /// </summary>
    /// <param name="plano">O plano.</param>
    /// <returns>A origem e o destino, como escritos.</returns>
    internal static (ConexaoBanco Origem, ConexaoBanco Destino) Conexoes(Plano plano)
    {
        ArgumentNullException.ThrowIfNull(plano);
        return (plano.FonteDoPlano.Origem, plano.FonteDoPlano.Destino);
    }

    // A mensagem é constante: só o lugar que divergiu, com os nomes escapados, nunca o N nem trechos do esquema.
    private static ErroPlano Mudou(string local) =>
        new(CodigosPlanejar.BancoMudou, $"O banco mudou desde o planejar ({local} não é mais o que o plano viu): planeje de novo antes de executar.");
}
