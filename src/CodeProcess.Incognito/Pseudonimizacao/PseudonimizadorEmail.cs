using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de e-mail: a parte local vira 100 bits do HMAC da <c>K_semente</c> sobre o endereço normalizado
/// (os 20 primeiros caracteres do base32 minúsculo), e o domínio vira <c>example.com</c>, reservado pela RFC 2606 e sem
/// entrega. Mesma chave, domínio, versão e ambiente dão o mesmo pseudônimo. Endereços que continuam diferentes depois
/// da normalização colidem só com probabilidade desprezível (100 bits): a injetividade é probabilística após a
/// normalização, e <c>Fulano@x</c> e <c>fulano@x</c> dão de propósito o mesmo pseudônimo.
/// </summary>
/// <remarks>
/// <para>
/// Normalização antes do HMAC: sem os espaços das pontas e com uma regra própria de minúsculas, congelada para dar o
/// mesmo resultado em toda máquina (o <c>ToLowerInvariant</c> muda com o ICU, o NLS e a globalização invariante): só
/// <c>A</c>-<c>Z</c> e <c>À</c>-<c>Þ</c> (menos o <c>×</c>) somam 32, o que cobre o português; o resto fica como veio.
/// Nenhuma regra de provedor: pontos e <c>+</c> da parte local ficam como vieram.
/// </para>
/// <para>
/// Forma aceita: uma única <c>@</c>, parte local de 1 a 64 caracteres, domínio com ao menos um ponto e sem rótulo
/// vazio, sem espaço, caractere de controle nem surrogate sem par (que viraria U+FFFD e juntaria endereços diferentes),
/// até 254 caracteres. O resto segue <c>entradaMalformada</c>. Espaços à direita (<c>char(n)</c>) voltam como estavam,
/// descontado o que o pseudônimo (sempre 32 caracteres) tiver a mais que a entrada; texto vazio ou só com espaços volta
/// como veio. Nenhuma exceção traz o valor. Pode ser usado por várias threads ao mesmo tempo; o descarte é do dono,
/// depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorEmail : IDisposable
{
    private const string Transformador = "email";
    private const string DominioReservado = "@example.com";
    private const string AlfabetoBase32 = "abcdefghijklmnopqrstuvwxyz234567";

    // 13 bytes dão 20 caracteres de 5 bits: 100 bits (12 bytes dariam só 95).
    private const int BytesDaParteLocal = 13;
    private const int CaracteresDaParteLocal = BytesDaParteLocal * 8 / 5;
    private const int MaximoDaParteLocal = 64;
    private const int MaximoDoEndereco = 254;
    private const string MensagemMalformado =
        "Valor fora da forma de e-mail (uma única @, parte local de 1 a 64 caracteres, domínio com ponto e sem rótulo vazio, sem espaço, caractere de controle nem surrogate sem par, até 254 caracteres); a política entradaMalformada é falhar. O valor não é exibido.";

    private readonly HmacDeSemente _hmac;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de e-mail.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (por padrão, <c>email</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="malformada">Política para valor fora da forma de e-mail.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal PseudonimizadorEmail(ChaveIncognito chave, string dominio, int versao, string ambiente, PoliticaEntradaMalformada malformada)
    {
        Politicas.Validar(malformada, nameof(malformada));
        _hmac = HmacDeSemente.Derivar(chave, Transformador, dominio, versao, ambiente);
        _malformada = malformada;
    }

    /// <summary>Pseudonimiza um e-mail.</summary>
    /// <param name="entrada">O e-mail como veio da coluna.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro ou vazia.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(string entrada, out string? saida)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        var semPreenchimento = entrada.AsSpan().TrimEnd(' ');
        var preenchimento = entrada.AsSpan(semPreenchimento.Length);
        var endereco = semPreenchimento.Trim();
        if (endereco.IsEmpty)
        {
            saida = entrada;
            return Desfecho.Vazio;
        }

        if (!TemFormaDeEmail(endereco))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }

        Span<char> normalizado = stackalloc char[endereco.Length];
        Span<byte> resultado = stackalloc byte[HmacDeSemente.Tamanho];
        bool valido;
        try
        {
            ParaMinusculas(endereco, normalizado);
            valido = _hmac.Calcular([], normalizado, resultado, recusarInvalido: true);
        }
        finally
        {
            normalizado.Clear();
        }

        if (!valido)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }

        Span<char> pseudonimo = stackalloc char[CaracteresDaParteLocal + DominioReservado.Length];
        EscreverBase32(resultado[..BytesDaParteLocal], pseudonimo);
        CryptographicOperations.ZeroMemory(resultado);
        DominioReservado.CopyTo(pseudonimo[CaracteresDaParteLocal..]);

        // O que o pseudônimo tiver a mais que o texto da entrada sai do preenchimento, para não alargar char(n).
        var excesso = Math.Max(0, pseudonimo.Length - semPreenchimento.Length);
        saida = string.Concat(pseudonimo, preenchimento[Math.Min(excesso, preenchimento.Length)..]);
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Descarta a cópia da chave de semente.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _hmac.Dispose();
    }

    private static bool TemFormaDeEmail(ReadOnlySpan<char> endereco)
    {
        var arroba = endereco.IndexOf('@');
        if (endereco.Length > MaximoDoEndereco || arroba is < 1 or > MaximoDaParteLocal || endereco.LastIndexOf('@') != arroba)
        {
            return false;
        }

        foreach (var caractere in endereco)
        {
            if (char.IsWhiteSpace(caractere) || char.IsControl(caractere))
            {
                return false;
            }
        }

        // Domínio com ao menos dois rótulos, nenhum vazio.
        var dominio = endereco[(arroba + 1)..];
        return dominio.Contains('.') && dominio[0] != '.' && dominio[^1] != '.' && !dominio.Contains("..", StringComparison.Ordinal);
    }

    // Regra congelada: A-Z e À-Þ (menos ×, U+00D7) somam 32; nenhuma outra letra muda.
    private static void ParaMinusculas(ReadOnlySpan<char> origem, Span<char> destino)
    {
        for (var i = 0; i < origem.Length; i++)
        {
            var caractere = origem[i];
            destino[i] = caractere is (>= 'A' and <= 'Z') or (>= 'À' and <= 'Þ' and not '×') ? (char)(caractere + 32) : caractere;
        }
    }

    // Base32 da RFC 4648 em minúsculas, sem preenchimento: cada 5 bits viram um caractere.
    private static void EscreverBase32(ReadOnlySpan<byte> bytes, Span<char> destino)
    {
        var acumulado = 0;
        var bits = 0;
        var posicao = 0;
        foreach (var valor in bytes)
        {
            acumulado = ((acumulado << 8) | valor) & 0xFFF;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                destino[posicao++] = AlfabetoBase32[(acumulado >> bits) & 31];
            }
        }
    }
}
