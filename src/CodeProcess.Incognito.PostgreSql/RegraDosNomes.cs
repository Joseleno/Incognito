using System.Text;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// Como o PostgreSQL decide, num banco, se um nome escrito designa um nome do catálogo. Entre aspas, nada muda. Sem
/// aspas, o servidor dobra A-Z para minúsculas e, num banco de um byte por caractere (inclusive <c>SQL_ASCII</c>), também
/// as letras fora do ASCII, pelo <c>tolower</c> do locale do servidor, que o Incognito não lê: aí a resposta é
/// indeterminada. Um nome maior que o limite do tipo <c>name</c> o servidor corta com um aviso; aqui ele não corresponde,
/// sem emular o corte.
/// </summary>
/// <param name="utf8">Se o banco é UTF-8.</param>
/// <param name="bytesPorCaractere">O máximo de bytes por caractere da codificação do banco (<c>pg_encoding_max_length</c>).</param>
/// <param name="maximoIdentificador">O limite do tipo <c>name</c>, em bytes (<c>max_identifier_length</c>).</param>
internal sealed class RegraDosNomes(bool utf8, int bytesPorCaractere, int maximoIdentificador)
{
    /// <summary>Compara.</summary>
    /// <param name="escrito">O nome como foi escrito, sem as aspas.</param>
    /// <param name="entreAspas">Se foi escrito entre aspas.</param>
    /// <param name="doCatalogo">O nome como o catálogo o guarda.</param>
    /// <returns>A correspondência.</returns>
    internal CorrespondenciaNome Comparar(string escrito, bool entreAspas, string doCatalogo)
    {
        ArgumentException.ThrowIfNullOrEmpty(escrito);
        ArgumentException.ThrowIfNullOrEmpty(doCatalogo);
        if (bytesPorCaractere < 1 || maximoIdentificador < 1)
        {
            // Um servidor real nunca informa isso; sem os números, não há como decidir.
            return CorrespondenciaNome.Indeterminada;
        }

        var ascii = Ascii.IsValid(escrito);
        if (!entreAspas && !ascii && bytesPorCaractere == 1)
        {
            return CorrespondenciaNome.Indeterminada;
        }

        switch (Cabe(escrito, ascii))
        {
            case null:
                return CorrespondenciaNome.Indeterminada;
            case false:
                return CorrespondenciaNome.NaoCorresponde;
        }

        var nome = entreAspas ? escrito : DobrarAscii(escrito);
        return string.Equals(nome, doCatalogo, StringComparison.Ordinal) ? CorrespondenciaNome.Corresponde : CorrespondenciaNome.NaoCorresponde;
    }

    // Se o nome cabe no tipo name, em bytes da codificação do banco. O ASCII ocupa um byte em toda codificação de servidor;
    // numa codificação de vários bytes que não é UTF-8, só se sabe quando cabe com folga ou não cabe de jeito nenhum.
    private bool? Cabe(string nome, bool ascii)
    {
        if (ascii)
        {
            return nome.Length <= maximoIdentificador;
        }

        if (utf8)
        {
            return Encoding.UTF8.GetByteCount(nome) <= maximoIdentificador;
        }

        var caracteres = nome.EnumerateRunes().Count();
        if (caracteres > maximoIdentificador)
        {
            return false;
        }

        return caracteres * bytesPorCaractere <= maximoIdentificador ? true : null;
    }

    private static string DobrarAscii(string nome) =>
        string.Create(nome.Length, nome, static (destino, origem) =>
        {
            for (var indice = 0; indice < origem.Length; indice++)
            {
                destino[indice] = char.IsAsciiLetterUpper(origem[indice]) ? (char)(origem[indice] + ('a' - 'A')) : origem[indice];
            }
        });
}
