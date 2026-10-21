using CodeProcess.Incognito.Configuracao;

namespace CodeProcess.Incognito.Execucao;

/// <summary>Uma unidade de cópia no plano: a tabela, o que a execução faz com ela e com cada coluna.</summary>
public sealed class MapaTabela
{
    internal MapaTabela(NomeTabela nome, AcaoTabela acao, bool acaoEscrita, IEnumerable<MapaColuna> colunas, PosicaoConfiguracao? posicao)
    {
        ArgumentNullException.ThrowIfNull(nome);
        ArgumentNullException.ThrowIfNull(colunas);
        Nome = nome;
        Acao = acao;
        AcaoEscrita = acaoEscrita;
        Colunas = [.. colunas];
        Posicao = posicao;
        foreach (var coluna in Colunas)
        {
            ArgumentNullException.ThrowIfNull(coluna, nameof(colunas));
        }
    }

    /// <summary>O nome da tabela.</summary>
    public NomeTabela Nome { get; }

    /// <summary>O que a execução faz com a tabela no destino.</summary>
    public AcaoTabela Acao { get; }

    /// <summary>
    /// Se a ação foi escrita no <c>incognito.yaml</c>; falso quando é a padrão (<see cref="AcaoTabela.Copiar"/>) ou a que o
    /// plano decidiu sozinho.
    /// </summary>
    public bool AcaoEscrita { get; }

    /// <summary>Todas as colunas da tabela na origem, na ordem do catálogo; vazia na tabela que só existe no destino.</summary>
    public IReadOnlyList<MapaColuna> Colunas { get; }

    /// <summary>Onde, no <c>incognito.yaml</c>, a tabela foi escrita; nula quando não foi.</summary>
    public PosicaoConfiguracao? Posicao { get; }
}
