using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>A leitura do esquema de um banco, sem alterá-lo.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorEsquema
{
    /// <summary>
    /// Lê o esquema: tabelas, colunas, chaves, sequences e objetos residuais, as tabelas, as sequences e os objetos
    /// residuais em ordem ordinal de esquema e nome (o plano e o relatório saem iguais entre leituras), as colunas na
    /// ordem do catálogo, e as permissões do login que lê. O esquema <c>incognito</c> (diário do Incognito) nunca entra.
    /// Não escreve no banco.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema lido.</returns>
    /// <remarks>
    /// Um dialeto que não vê o catálogo inteiro (por falta de privilégio do login) lança: nunca devolve um esquema parcial,
    /// que deixaria tabelas, restrições ou objetos residuais sem conferência.
    /// </remarks>
    public abstract Task<EsquemaBanco> LerAsync(CancellationToken cancelamento);
}

/// <summary>O banco, o servidor, a linhagem e o transporte da conexão.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class IdentidadeBanco
{
    /// <summary>Lê a identidade, sem escrever no banco.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A identidade lida.</returns>
    public abstract Task<IdentidadeLida> LerAsync(CancellationToken cancelamento);
}

/// <summary>A sintaxe do banco.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class DialetoSql
{
    /// <summary>
    /// Cita um identificador como o catálogo o guarda (<c>"Cliente"</c> no PostgreSQL, <c>[Cliente]</c> no SQL
    /// Server), escapando o delimitador.
    /// </summary>
    /// <param name="identificador">O identificador, como o catálogo o guarda.</param>
    /// <returns>O identificador citado.</returns>
    public abstract string Citar(string identificador);

    /// <summary>Cita o esquema e o nome, separados por ponto.</summary>
    /// <param name="nome">O nome do objeto.</param>
    /// <returns>O nome citado.</returns>
    /// <exception cref="ArgumentNullException">Nome nulo.</exception>
    public string Citar(NomeObjeto nome)
    {
        ArgumentNullException.ThrowIfNull(nome);
        return Citar(nome.Esquema) + "." + Citar(nome.Nome);
    }
}
