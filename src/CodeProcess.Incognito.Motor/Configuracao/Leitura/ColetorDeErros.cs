namespace CodeProcess.Incognito.Configuracao.Leitura;

/// <summary>Junta os erros de uma leitura, até 100, e monta a exceção ordenada pela posição no arquivo.</summary>
internal sealed class ColetorDeErros(string? arquivo)
{
    /// <summary>Máximo de erros guardados; os seguintes só marcam que houve mais.</summary>
    internal const int Maximo = 100;

    /// <summary>Espaço à parte para o aviso de segredo por extenso, que sai mesmo com o máximo de outros erros.</summary>
    internal const int MaximoDeSegredos = 20;

    private readonly List<ErroConfiguracao> _erros = [];
    private readonly List<ErroConfiguracao> _segredos = [];
    private bool _omitidos;

    internal bool Vazio => _erros.Count == 0 && _segredos.Count == 0;

    /// <summary>Já guardou o máximo: quem procura erros em volume pode parar.</summary>
    internal bool Cheio => _erros.Count >= Maximo;

    /// <summary>Já guardou o máximo de avisos de segredo.</summary>
    internal bool SegredosCheios => _segredos.Count >= MaximoDeSegredos;

    internal void Adicionar(string codigo, Posicao? posicao, string local, string mensagem)
    {
        if (codigo == CodigosConfiguracao.SegredoPorExtenso)
        {
            if (!SegredosCheios)
            {
                _segredos.Add(new ErroConfiguracao(codigo, arquivo, posicao?.Linha, posicao?.Coluna, local, mensagem));
            }

            return;
        }

        if (Cheio)
        {
            _omitidos = true;
            return;
        }

        _erros.Add(new ErroConfiguracao(codigo, arquivo, posicao?.Linha, posicao?.Coluna, local, mensagem));
    }

    internal ConfiguracaoInvalidaException Excecao()
    {
        var ordenados = _segredos.Concat(_erros)
            .OrderBy(erro => erro.Linha ?? int.MaxValue)
            .ThenBy(erro => erro.Coluna ?? int.MaxValue)
            .ToArray();
        return new ConfiguracaoInvalidaException(ordenados, _omitidos);
    }
}

/// <summary>Linha e coluna no texto, a partir de 1; a coluna em unidades UTF-16.</summary>
internal readonly record struct Posicao(int Linha, int Coluna);
