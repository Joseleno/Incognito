namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// Onde, no <c>incognito.yaml</c>, está o que deu origem a um item do plano: arquivo, linha, coluna e o caminho até o
/// campo. Linha e coluna como em <see cref="Configuracao.ErroConfiguracao"/>.
/// </summary>
public sealed class PosicaoConfiguracao
{
    internal PosicaoConfiguracao(string? arquivo, int linha, int coluna, string local)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(linha, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(coluna, 1);
        ArgumentNullException.ThrowIfNull(local);
        Arquivo = arquivo;
        Linha = linha;
        Coluna = coluna;
        Local = local;
    }

    /// <summary>O arquivo, como foi pedido a <see cref="Configuracao.ConfiguracaoIncognito.CarregarYaml"/>; nulo quando lido de texto.</summary>
    public string? Arquivo { get; }

    /// <summary>Linha, a partir de 1.</summary>
    public int Linha { get; }

    /// <summary>Coluna, a partir de 1, em unidades UTF-16.</summary>
    public int Coluna { get; }

    /// <summary>
    /// Caminho até o campo, em JSON Pointer (RFC 6901), como <c>/tabelas/public.clientes/colunas/cpf</c>; vazio para o
    /// documento inteiro.
    /// </summary>
    public string Local { get; }
}
