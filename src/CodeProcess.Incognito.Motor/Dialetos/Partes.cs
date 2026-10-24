using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

/// <summary>A leitura do esquema de um banco, sem alterá-lo.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorEsquema
{
    /// <summary>
    /// Lê o esquema: tabelas, colunas, chaves, sequences e objetos residuais, as tabelas, as sequences e os objetos
    /// residuais em ordem ordinal de esquema e nome (o plano e o relatório saem iguais entre leituras), as colunas na
    /// ordem do catálogo, e as permissões do login que lê. As tabelas, as sequences e os objetos residuais do esquema
    /// <c>incognito</c> (diário do Incognito) ficam fora das listas, e os objetos dele vão para
    /// <see cref="EsquemaBanco.ObjetosNoEsquemaReservado"/>. Não escreve no banco.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema lido.</returns>
    /// <remarks>
    /// Um dialeto que não vê o catálogo inteiro (por falta de privilégio do login) lança: nunca devolve um esquema parcial,
    /// que deixaria tabelas, restrições ou objetos residuais sem conferência.
    /// </remarks>
    public abstract Task<EsquemaBanco> LerAsync(CancellationToken cancelamento);
}

/// <summary>
/// A leitura de dados da origem que o <c>planejar</c> precisa: só agregados de uma coluna, nunca uma linha. O valor lido é
/// de coluna tratada: nenhuma exceção, mensagem ou registro do dialeto o traz, e o motor traduz qualquer exceção desta parte
/// só com o código.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public abstract class LeitorDados
{
    /// <summary>
    /// O maior valor não nulo de uma coluna <c>Inteiro</c> do esquema lido, nas linhas que a cópia lê da tabela: numa
    /// particionada, as de todas as partições; nas outras, só as da própria tabela, sem as herdeiras de herança clássica.
    /// Lê na conexão de controle, numa transação só de leitura, e não escreve no banco. A segurança por linha nunca filtra
    /// a leitura em silêncio: o dialeto falha se ela valer para o login.
    /// </summary>
    /// <param name="coluna">A tabela e a coluna, como o catálogo as guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O maior valor; nulo quando nenhuma linha tem valor na coluna.</returns>
    public abstract Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento);
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

/// <summary>Se um nome escrito designa um nome do catálogo (<see cref="DialetoSql.CompararNome"/>).</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum CorrespondenciaNome
{
    /// <summary>Não designa.</summary>
    NaoCorresponde = 0,

    /// <summary>Designa.</summary>
    Corresponde = 1,

    /// <summary>O dialeto não sabe decidir sem adivinhar (por exemplo, a dobra de caixa depende do locale do servidor).</summary>
    Indeterminada = 2,
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

    /// <summary>
    /// Se um nome escrito na configuração designa um nome do catálogo, pela regra do banco desta conexão: a dobra de caixa
    /// do nome sem aspas e a igualdade dos identificadores dependem do banco (a codificação no PostgreSQL, a colação do
    /// catálogo no SQL Server). Vale só para o banco da conexão de onde veio este <see cref="DialetoSql"/>. O motor também a
    /// usa com o nome do catálogo do outro lado, entre aspas, para parear a origem com o destino, e com nomes fixos que podem
    /// não existir no catálogo (os esquemas reservados): <paramref name="doCatalogo"/> não precisa existir.
    /// </summary>
    /// <param name="escrito">O nome como foi escrito, sem as aspas.</param>
    /// <param name="entreAspas">Se foi escrito entre aspas (delimitado, na sintaxe do banco).</param>
    /// <param name="doCatalogo">O nome como o catálogo o guarda.</param>
    /// <returns>
    /// Se corresponde, se não corresponde, ou <see cref="CorrespondenciaNome.Indeterminada"/> quando o dialeto não sabe
    /// decidir sem adivinhar (o motor recusa o plano, nunca escolhe).
    /// </returns>
    public abstract CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo);

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
