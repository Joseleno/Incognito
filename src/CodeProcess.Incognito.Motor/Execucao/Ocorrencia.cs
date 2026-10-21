using System.Globalization;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Execucao;

// O que o Aviso e o ErroPlano têm em comum: os invariantes e o ToString. Os dois ficam tipos separados, porque quem lê um
// plano trata um aviso e uma recusa de formas diferentes.
internal static class Ocorrencia
{
    internal static void Conferir(string codigo, string mensagem, NomeTabela? tabela, string? coluna, string? objeto)
    {
        ArgumentException.ThrowIfNullOrEmpty(codigo);
        ArgumentException.ThrowIfNullOrEmpty(mensagem);
        if (coluna is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(coluna);
            if (tabela is null)
            {
                throw new ArgumentException("A coluna exige a tabela.", nameof(coluna));
            }
        }

        if (objeto is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(objeto);
        }
    }

    // Com posição: "arquivo(linha,coluna): INCnnnn: alvo: mensagem"; sem posição, só "INCnnnn: alvo: mensagem". O alvo é a
    // tabela ou a coluna, escapado; sem tabela, não sai. O arquivo sai com o escape de caminho: o
    // nome pode trazer controles que mudariam a tela.
    internal static string Formatar(string codigo, string mensagem, NomeTabela? tabela, string? coluna, PosicaoConfiguracao? posicao)
    {
        var onde = posicao is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{(posicao.Arquivo is null ? "incognito.yaml" : TextoSeguro.Caminho(posicao.Arquivo))}({posicao.Linha},{posicao.Coluna}): ");
        var alvo = tabela is null ? "" : coluna is null ? $"{tabela}: " : $"{tabela}.{NomeTabela.Parte(coluna)}: ";
        return $"{onde}{codigo}: {alvo}{mensagem}";
    }
}
