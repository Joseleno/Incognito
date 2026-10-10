using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// Subchaves derivadas da <see cref="ChaveIncognito"/> por HKDF-SHA256, todas a partir do mesmo PRK
/// (<see cref="ChaveIncognito.ExtrairPrk"/>), com o ambiente em toda derivação de valor. A subchave da impressão
/// digital é derivada pela própria chave, sem ambiente.
/// </summary>
/// <remarks>
/// O <c>info</c> do HKDF é o rótulo seguido dos campos em ASCII, separados por 0x00. Domínio, transformador e
/// ambiente seguem a regra dos nomes (letras minúsculas e dígitos, com <c>.</c>, <c>_</c> ou <c>-</c> entre eles, até
/// 64 caracteres) e a variante é uma das <see cref="Variantes"/>. Assim nenhum campo contém 0x00 nem <c>@</c>, e
/// dois conjuntos de campos diferentes nunca dão o mesmo <c>info</c>. A regra também recusa, em vez de derivar outra
/// chave em silêncio, grafias que parecem iguais: maiúsculas, acentos em formas Unicode diferentes, espaços e
/// caracteres invisíveis.
/// </remarks>
internal static class Subchaves
{
    /// <summary>Tamanho de toda subchave, em bytes.</summary>
    internal const int Tamanho = 32;

    /// <summary>Tamanho máximo de domínio, transformador e ambiente.</summary>
    internal const int TamanhoMaximoDoNome = 64;

    private const string MensagemNomeInvalido =
        "Nome inválido na derivação: use letras minúsculas e dígitos, com '.', '_' ou '-' entre eles, até 64 caracteres.";

    /// <summary>
    /// <c>K_dominio = Expand(PRK, "feistel" ‖ 0x00 ‖ dominio@versao ‖ 0x00 ‖ variante ‖ 0x00 ‖ ambiente, 32)</c>,
    /// a chave da permutação de um domínio num espaço permutado.
    /// </summary>
    /// <param name="chave">Chave do Incognito, não descartada.</param>
    /// <param name="dominio">Nome canônico do domínio.</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="variante">Espaço permutado, uma das <see cref="Variantes"/>.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="destino">
    /// Destino de 32 bytes; quem chama zera depois de usar. Fica intocado se um argumento for recusado e é zerado se
    /// a chave estiver descartada.
    /// </param>
    internal static void DerivarChaveDeDominio(
        ChaveIncognito chave, string dominio, int versao, string variante, string ambiente, Span<byte> destino)
    {
        ArgumentNullException.ThrowIfNull(chave);
        var dominioComVersao = DominioComVersao(dominio, versao);
        ArgumentNullException.ThrowIfNull(variante);
        if (!Variantes.EhConhecida(variante))
        {
            throw new ArgumentException("Variante desconhecida na derivação.", nameof(variante));
        }

        ValidarNome(ambiente, nameof(ambiente));
        ValidarDestino(destino);

        Derivar(chave, "feistel", [dominioComVersao, variante, ambiente], destino);
    }

    /// <summary>
    /// <c>K_semente = Expand(PRK, "semente" ‖ 0x00 ‖ transformador ‖ 0x00 ‖ dominio@versao ‖ 0x00 ‖ ambiente, 32)</c>,
    /// a semente dos transformadores sem injetividade (nome, CEP, datas).
    /// </summary>
    /// <param name="chave">Chave do Incognito, não descartada.</param>
    /// <param name="transformador">Nome do transformador, sem versão (<c>br.nome-completo</c>).</param>
    /// <param name="dominio">Nome canônico do domínio.</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="destino">
    /// Destino de 32 bytes; quem chama zera depois de usar. Fica intocado se um argumento for recusado e é zerado se
    /// a chave estiver descartada.
    /// </param>
    internal static void DerivarSemente(
        ChaveIncognito chave, string transformador, string dominio, int versao, string ambiente, Span<byte> destino)
    {
        ArgumentNullException.ThrowIfNull(chave);
        ValidarNome(transformador, nameof(transformador));
        var dominioComVersao = DominioComVersao(dominio, versao);
        ValidarNome(ambiente, nameof(ambiente));
        ValidarDestino(destino);

        Derivar(chave, "semente", [transformador, dominioComVersao, ambiente], destino);
    }

    private static void Derivar(ChaveIncognito chave, string rotulo, ReadOnlySpan<string> campos, Span<byte> destino)
    {
        // O info não é segredo: nomes de domínio, transformador e ambiente.
        var informacao = Informacao(rotulo, campos);
        Span<byte> prk = stackalloc byte[Tamanho];
        try
        {
            chave.ExtrairPrk(prk);
            HKDF.Expand(HashAlgorithmName.SHA256, prk, destino, informacao);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(destino);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }

    // Todos os campos já foram validados como ASCII sem 0x00.
    private static byte[] Informacao(string rotulo, ReadOnlySpan<string> campos)
    {
        var tamanho = rotulo.Length;
        foreach (var campo in campos)
        {
            tamanho += 1 + campo.Length;
        }

        var informacao = new byte[tamanho];
        var escritos = Encoding.ASCII.GetBytes(rotulo, informacao);
        foreach (var campo in campos)
        {
            informacao[escritos++] = 0x00;
            escritos += Encoding.ASCII.GetBytes(campo, informacao.AsSpan(escritos));
        }

        if (escritos != tamanho)
        {
            throw new UnreachableException("O info da derivação foi montado incompleto.");
        }

        return informacao;
    }

    private static string DominioComVersao(string dominio, int versao)
    {
        ValidarNome(dominio, nameof(dominio));
        ArgumentOutOfRangeException.ThrowIfLessThan(versao, 1);
        return string.Create(CultureInfo.InvariantCulture, $"{dominio}@{versao}");
    }

    private static void ValidarNome(string nome, string parametro)
    {
        ArgumentNullException.ThrowIfNull(nome, parametro);
        if (!EhNomeValido(nome))
        {
            throw new ArgumentException(MensagemNomeInvalido, parametro);
        }
    }

    // [a-z0-9]+ ([._-] [a-z0-9]+)*, até 64 caracteres. O nome vazio cai no retorno final, como se terminasse em separador.
    private static bool EhNomeValido(string nome)
    {
        if (nome.Length > TamanhoMaximoDoNome)
        {
            return false;
        }

        var anteriorEraSeparador = true;
        foreach (var caractere in nome)
        {
            if (char.IsAsciiLetterLower(caractere) || char.IsAsciiDigit(caractere))
            {
                anteriorEraSeparador = false;
            }
            else if ((caractere is '.' or '_' or '-') && !anteriorEraSeparador)
            {
                anteriorEraSeparador = true;
            }
            else
            {
                return false;
            }
        }

        return !anteriorEraSeparador;
    }

    private static void ValidarDestino(Span<byte> destino)
    {
        if (destino.Length != Tamanho)
        {
            throw new ArgumentException("O destino da subchave precisa ter 32 bytes.", nameof(destino));
        }
    }
}
