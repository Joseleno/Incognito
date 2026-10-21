using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// Junta os erros e os avisos do planejar e os devolve em ordem estável: tabela (ordinal por esquema e nome; sem tabela
/// antes), posição da coluna no catálogo (sem coluna antes), código, objeto e, por último, a posição no YAML. Avisos iguais
/// em código, tabela, coluna, objeto e mensagem saem uma vez só.
/// </summary>
internal sealed class ColetorDoPlano
{
    private readonly List<(ErroPlano Erro, int? Posicao)> _erros = [];
    private readonly List<(Aviso Aviso, int? Posicao)> _avisos = [];

    /// <summary>Se há algum erro.</summary>
    internal bool TemErros => _erros.Count > 0;

    /// <summary>Junta um erro.</summary>
    /// <param name="erro">O erro.</param>
    /// <param name="posicaoDaColuna">A posição da coluna na tabela do catálogo, se o erro é de uma coluna.</param>
    internal void Erro(ErroPlano erro, int? posicaoDaColuna = null) => _erros.Add((erro, posicaoDaColuna));

    /// <summary>Junta um aviso.</summary>
    /// <param name="aviso">O aviso.</param>
    /// <param name="posicaoDaColuna">A posição da coluna na tabela do catálogo, se o aviso é de uma coluna.</param>
    internal void Aviso(Aviso aviso, int? posicaoDaColuna = null) => _avisos.Add((aviso, posicaoDaColuna));

    /// <summary>Lança a recusa com os erros juntados, em ordem, se houver algum.</summary>
    /// <exception cref="PlanoRecusadoException">Há erros.</exception>
    internal void LancarSeHouverErros()
    {
        if (TemErros)
        {
            var ordem = Comparer<(ErroPlano Erro, int? Posicao)>.Create((um, outro) => Comparar(Chave(um.Erro, um.Posicao), Chave(outro.Erro, outro.Posicao)));
            throw new PlanoRecusadoException([.. _erros.Order(ordem).Select(item => item.Erro)]);
        }
    }

    /// <summary>Os avisos, em ordem e sem repetição.</summary>
    /// <returns>Os avisos.</returns>
    internal IReadOnlyList<Aviso> Avisos()
    {
        var ordem = Comparer<(Aviso Aviso, int? Posicao)>.Create((um, outro) => Comparar(Chave(um.Aviso, um.Posicao), Chave(outro.Aviso, outro.Posicao)));
        return [.. _avisos.DistinctBy(item => (item.Aviso.Codigo, item.Aviso.Tabela, item.Aviso.Coluna, item.Aviso.Objeto, item.Aviso.Mensagem)).Order(ordem).Select(item => item.Aviso)];
    }

    private static (NomeTabela? Tabela, int? Coluna, string Codigo, string? Objeto, PosicaoConfiguracao? Posicao) Chave(ErroPlano erro, int? coluna) =>
        (erro.Tabela, coluna, erro.Codigo, erro.Objeto, erro.Posicao);

    private static (NomeTabela? Tabela, int? Coluna, string Codigo, string? Objeto, PosicaoConfiguracao? Posicao) Chave(Aviso aviso, int? coluna) =>
        (aviso.Tabela, coluna, aviso.Codigo, aviso.Objeto, aviso.Posicao);

    private static int Comparar(
        (NomeTabela? Tabela, int? Coluna, string Codigo, string? Objeto, PosicaoConfiguracao? Posicao) um,
        (NomeTabela? Tabela, int? Coluna, string Codigo, string? Objeto, PosicaoConfiguracao? Posicao) outro)
    {
        var comparacao = CompararTabelas(um.Tabela, outro.Tabela);
        if (comparacao == 0)
        {
            comparacao = Nullable.Compare(um.Coluna, outro.Coluna);
        }

        if (comparacao == 0)
        {
            comparacao = string.CompareOrdinal(um.Codigo, outro.Codigo);
        }

        if (comparacao == 0)
        {
            comparacao = string.CompareOrdinal(um.Objeto, outro.Objeto);
        }

        if (comparacao == 0)
        {
            comparacao = Nullable.Compare(um.Posicao?.Linha, outro.Posicao?.Linha);
        }

        return comparacao == 0 ? Nullable.Compare(um.Posicao?.Coluna, outro.Posicao?.Coluna) : comparacao;
    }

    /// <summary>A ordem das tabelas: ordinal por esquema e nome, sem tabela antes.</summary>
    internal static int CompararTabelas(NomeTabela? uma, NomeTabela? outra)
    {
        if (uma is null || outra is null)
        {
            return uma is null ? (outra is null ? 0 : -1) : 1;
        }

        var comparacao = string.CompareOrdinal(uma.Esquema, outra.Esquema);
        return comparacao != 0 ? comparacao : string.CompareOrdinal(uma.Tabela, outra.Tabela);
    }
}
