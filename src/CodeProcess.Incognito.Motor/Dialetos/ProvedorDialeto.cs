using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// Um banco suportado (<c>postgresql</c>, <c>sqlserver</c>): o nome usado na configuração e a conexão à origem e ao
/// destino. Classe abstrata para poder ganhar membros virtuais sem quebrar quem estende.
/// </summary>
/// <remarks>
/// Contrato experimental (<c>INCX001</c>): muda até a 1.0. Um dialeto de fora implementa as partes que o motor usa e
/// prova o comportamento herdando a suíte de conformidade.
/// </remarks>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class ProvedorDialeto
{
    /// <summary>O nome do provedor na configuração (<c>provedor: postgresql</c>).</summary>
    public abstract string Nome { get; }

    /// <summary>
    /// Conecta à origem: abre a conexão de controle e confere que ela responde. O que o dialeto faz na origem é só
    /// leitura.
    /// </summary>
    /// <param name="conexao">A string de conexão.</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>O dialeto ligado à origem, que o chamador descarta. Se a conexão falhar ou for cancelada, o dialeto fecha o que já abriu.</returns>
    public abstract Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento);

    /// <summary>Conecta ao destino: abre a conexão de controle e confere que ela responde.</summary>
    /// <param name="conexao">A string de conexão.</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>O dialeto ligado ao destino, que o chamador descarta. Se a conexão falhar ou for cancelada, o dialeto fecha o que já abriu.</returns>
    public abstract Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento);
}
