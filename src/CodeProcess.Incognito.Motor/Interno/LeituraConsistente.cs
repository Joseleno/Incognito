// A leitura consistente monta o pedido de leitura e devolve o esquema relido, tipos do contrato experimental de dialeto que
// o motor declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>A sessão de leitura da origem aberta pela carga do plano, com o esquema relido no instante dela.</summary>
/// <param name="Sessao">A sessão, que o chamador descarta.</param>
/// <param name="Esquema">O esquema da origem relido no instante da sessão, para o executor replanejar e comparar.</param>
internal sealed record LeituraAberta(DialetoProtegido.SessaoProtegida Sessao, EsquemaBanco Esquema)
{
    /// <inheritdoc/>
    public override string ToString() => nameof(LeituraAberta);
}

/// <summary>
/// A abertura da leitura consistente da origem pela carga do plano: as tabelas copiadas ficam travadas, pelo nome do
/// catálogo da origem (as partições vêm com a particionada); o modo é o da configuração; e o esquema é relido no instante da
/// sessão. O número de leitores é de quem executa (a precedência com as opções fica com a execução).
/// </summary>
internal static class LeituraConsistente
{
    /// <summary>Abre a sessão e relê o esquema; se a releitura falhar, descarta a sessão.</summary>
    /// <param name="origem">A origem conectada.</param>
    /// <param name="dados">A carga do plano.</param>
    /// <param name="leitores">Quantos leitores, de 1 a <see cref="PedidoLeitura.MaximoDeLeitores"/>.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A sessão e o esquema relido.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Leitores fora da faixa.</exception>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a abertura ou a releitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<LeituraAberta> AbrirAsync(DialetoProtegido origem, DadosDeExecucao dados, int leitores, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(origem);
        var sessao = await origem.AbrirLeituraAsync(Pedido(dados, leitores), cancelamento).ConfigureAwait(false);
        try
        {
            return new LeituraAberta(sessao, await sessao.LerEsquemaAsync(cancelamento).ConfigureAwait(false));
        }
        catch
        {
            await sessao.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>O pedido de leitura da carga: as copiadas travadas, na ordem do plano, e o modo da configuração.</summary>
    /// <param name="dados">A carga do plano.</param>
    /// <param name="leitores">Quantos leitores.</param>
    /// <returns>O pedido.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Leitores fora da faixa.</exception>
    internal static PedidoLeitura Pedido(DadosDeExecucao dados, int leitores)
    {
        ArgumentNullException.ThrowIfNull(dados);
        return new PedidoLeitura
        {
            Leitores = leitores,
            Modo = dados.Configuracao.Consistencia == Consistencia.Exigir ? ModoConsistencia.Exigir : ModoConsistencia.MelhorEsforco,
            Travadas = [.. dados.Unidades.Where(unidade => unidade.Acao == AcaoTabela.Copiar).Select(unidade => new NomeObjeto(unidade.Nome.Esquema, unidade.Nome.Tabela))],
        };
    }
}
