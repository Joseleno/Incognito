using System.Globalization;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// Um erro do <c>incognito.yaml</c>: código estável, onde está (arquivo, linha, coluna e o caminho até o campo) e o que se
/// esperava. A mensagem nunca traz o valor escrito no campo, só o nome do campo e a regra.
/// </summary>
/// <remarks>
/// Linha e coluna começam em 1; a coluna conta unidades UTF-16 (um emoji conta 2, um tab conta 1), como o VS Code e o
/// SARIF. CR, LF e CRLF são uma quebra de linha cada.
/// </remarks>
public sealed class ErroConfiguracao
{
    internal ErroConfiguracao(string codigo, string? arquivo, int? linha, int? coluna, string local, string mensagem)
    {
        Codigo = codigo;
        Arquivo = arquivo;
        Linha = linha;
        Coluna = coluna;
        Local = local;
        Mensagem = mensagem;
    }

    /// <summary>Código estável do erro, na faixa <c>INC2xxx</c>.</summary>
    public string Codigo { get; }

    /// <summary>O arquivo, como foi pedido a <see cref="ConfiguracaoIncognito.CarregarYaml"/>; nulo quando lido de texto.</summary>
    public string? Arquivo { get; }

    /// <summary>Linha, a partir de 1; nula quando o erro é do arquivo inteiro (vazio, grande demais, ilegível).</summary>
    public int? Linha { get; }

    /// <summary>Coluna, a partir de 1, em unidades UTF-16; nula junto com a <see cref="Linha"/>.</summary>
    public int? Coluna { get; }

    /// <summary>
    /// Caminho até o campo, em JSON Pointer (RFC 6901), como <c>/tabelas/public.clientes/colunas/cpf</c>; vazio para o
    /// documento inteiro.
    /// </summary>
    public string Local { get; }

    /// <summary>O que está errado e o que se esperava, sem o valor escrito.</summary>
    public string Mensagem { get; }

    /// <summary>No formato <c>arquivo(linha,coluna): INC2xxx: mensagem</c>.</summary>
    public override string ToString()
    {
        var posicao = Linha is { } linha && Coluna is { } coluna
            ? string.Create(CultureInfo.InvariantCulture, $"({linha},{coluna})")
            : "";
        return $"{Arquivo ?? "incognito.yaml"}{posicao}: {Codigo}: {Mensagem}";
    }
}
