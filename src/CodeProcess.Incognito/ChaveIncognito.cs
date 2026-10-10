using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CodeProcess.Incognito;

/// <summary>
/// Chave secreta do Incognito: 32 bytes aleatórios, escritos como <c>inc1_</c> seguido de 43 caracteres base64url
/// sem padding. Qualquer outro formato é recusado, o que dificulta usar uma senha digitada como chave; a chave de
/// 32 bytes zero também é recusada.
/// </summary>
/// <remarks>
/// O valor fica num <see cref="byte"/>[] fixado na memória, para o coletor de lixo não deixar cópias, e é zerado no
/// <see cref="Dispose"/> (ou na finalização, se ninguém descartar). A zeragem é de melhor esforço: o texto de onde a
/// chave foi lida e as estruturas criptográficas derivadas dela guardam cópias enquanto existem.
/// <see cref="ToString"/> e as mensagens de erro nunca revelam o valor. A instância não é thread-safe para descarte:
/// quem a descarta é o dono, depois de todo uso; um uso que cruze com o descarte lança
/// <see cref="ObjectDisposedException"/> em vez de usar o valor zerado.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class ChaveIncognito : IDisposable
{
    private const string Prefixo = "inc1_";
    private const int TamanhoEmBytes = 32;
    private const int TamanhoBase64 = 43;
    private const int TamanhoTexto = 48;
    private const string CodigoFormatoInvalido = "INC1001";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly byte[] _valor;

    private int _descartada;

    private ChaveIncognito(ReadOnlySpan<byte> valor)
    {
        _valor = GC.AllocateArray<byte>(TamanhoEmBytes, pinned: true);
        valor.CopyTo(_valor);
        try
        {
            ImpressaoDigital = CalcularImpressaoDigital(_valor);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(_valor);
            GC.SuppressFinalize(this);
            throw;
        }
    }

    /// <summary>Zera o valor se a chave for coletada sem ter sido descartada.</summary>
    ~ChaveIncognito()
    {
        CryptographicOperations.ZeroMemory(_valor);
    }

    /// <summary>
    /// Identificação pública da chave: 16 caracteres hexadecimais minúsculos, iguais para a mesma chave em qualquer
    /// ambiente. Não permite recuperar a chave.
    /// </summary>
    public string ImpressaoDigital { get; }

    /// <summary>Sal do HKDF-Extract que dá origem a todas as subchaves.</summary>
    internal static ReadOnlySpan<byte> SalDaDerivacao => "CodeProcess.Incognito/v1"u8;

    private static ReadOnlySpan<byte> PrefixoUtf8 => "inc1_"u8;

    /// <summary>Gera uma chave nova com <see cref="RandomNumberGenerator"/>.</summary>
    /// <returns>A chave gerada.</returns>
    public static ChaveIncognito Gerar()
    {
        Span<byte> valor = stackalloc byte[TamanhoEmBytes];
        try
        {
            RandomNumberGenerator.Fill(valor);
            return new ChaveIncognito(valor);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valor);
        }
    }

    /// <summary>Lê uma chave no formato <c>inc1_</c> seguido de 43 caracteres base64url sem padding.</summary>
    /// <param name="texto">A chave por extenso, sem espaços nem quebras de linha.</param>
    /// <returns>A chave lida.</returns>
    /// <exception cref="IncognitoException">
    /// Com o código <c>INC1001</c>, se o texto não estiver exatamente no formato canônico. A mensagem não traz o texto.
    /// </exception>
    public static ChaveIncognito Ler(ReadOnlySpan<char> texto)
    {
        if (texto.Length != TamanhoTexto)
        {
            throw FormatoInvalido();
        }

        Span<byte> utf8 = stackalloc byte[TamanhoTexto];
        try
        {
            for (var i = 0; i < texto.Length; i++)
            {
                // Antes de truncar para byte: U+0141 viraria 'A'.
                if (!char.IsAscii(texto[i]))
                {
                    throw FormatoInvalido();
                }

                utf8[i] = (byte)texto[i];
            }

            return Ler(utf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    /// <summary>Lê uma chave em UTF-8 no formato <c>inc1_</c> seguido de 43 caracteres base64url sem padding.</summary>
    /// <param name="textoUtf8">A chave por extenso em UTF-8, sem espaços nem quebras de linha.</param>
    /// <returns>A chave lida.</returns>
    /// <exception cref="IncognitoException">
    /// Com o código <c>INC1001</c>, se o texto não estiver exatamente no formato canônico. A mensagem não traz o texto.
    /// </exception>
    public static ChaveIncognito Ler(ReadOnlySpan<byte> textoUtf8)
    {
        // A forma canônica é conferida antes de decodificar. Assim, uma entrada recusada nunca passa pelo
        // decodificador vetorizado, que deixaria um bloco decodificado num registrador, salvo na pilha ao lançar.
        if (textoUtf8.Length != TamanhoTexto
            || !textoUtf8.StartsWith(PrefixoUtf8)
            || !EhBase64UrlCanonico(textoUtf8[PrefixoUtf8.Length..]))
        {
            throw FormatoInvalido();
        }

        Span<byte> valor = stackalloc byte[TamanhoEmBytes];
        try
        {
            // Inalcançável depois da conferência acima; fica como trava antes de criar a chave.
            if (Base64Url.DecodeFromUtf8(textoUtf8[PrefixoUtf8.Length..], valor, out var consumidos, out var decodificados) != OperationStatus.Done
                || consumidos != TamanhoBase64
                || decodificados != TamanhoEmBytes)
            {
                throw FormatoInvalido();
            }

            // 32 bytes zero são uma chave pública conhecida, que nenhum gerador aleatório produz na prática.
            if (valor.IndexOfAnyExcept((byte)0) < 0)
            {
                throw FormatoInvalido();
            }

            return new ChaveIncognito(valor);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valor);
        }
    }

    /// <summary>Escreve a chave no formato <c>inc1_</c>, para gravá-la ao gerar. Nenhum outro uso deve exportá-la.</summary>
    /// <param name="destino">Destino com pelo menos 48 caracteres.</param>
    /// <param name="escritos">Quantidade de caracteres escritos; zero se o destino for curto.</param>
    /// <returns><see langword="true"/> se a chave foi escrita.</returns>
    /// <exception cref="ObjectDisposedException">Se a chave foi descartada antes ou durante a exportação.</exception>
    public bool TentarExportar(Span<char> destino, out int escritos)
    {
        LancarSeDescartada();
        escritos = 0;
        if (destino.Length < TamanhoTexto)
        {
            return false;
        }

        Prefixo.CopyTo(destino);
        var codificou = Base64Url.TryEncodeToChars(_valor, destino[Prefixo.Length..], out var codificados);
        Debug.Assert(codificou && codificados == TamanhoBase64);

        // Um descarte no meio deixaria no destino uma chave total ou parcialmente zerada.
        if (Volatile.Read(ref _descartada) != 0)
        {
            destino[..TamanhoTexto].Clear();
            throw new ObjectDisposedException(nameof(ChaveIncognito));
        }

        escritos = TamanhoTexto;
        return true;
    }

    /// <summary>Zera o valor da chave. Depois disso, só <see cref="ImpressaoDigital"/> e <see cref="ToString"/> funcionam.</summary>
    public void Dispose()
    {
        // Marca antes de zerar: quem estiver usando a chave confere a marca depois do uso e descarta o resultado.
        if (Interlocked.Exchange(ref _descartada, 1) == 0)
        {
            CryptographicOperations.ZeroMemory(_valor);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Máscara fixa, que nunca revela a chave.</summary>
    /// <returns><c>ChaveIncognito(inc1_****)</c>.</returns>
    public override string ToString() => "ChaveIncognito(inc1_****)";

    /// <summary>
    /// PRK = HKDF-Extract(SHA-256, sal, chave), de onde saem todas as subchaves. Se a chave for descartada durante o
    /// cálculo, o resultado é apagado e a chamada lança <see cref="ObjectDisposedException"/>.
    /// </summary>
    /// <param name="prk">Destino de 32 bytes; quem chama zera depois de usar.</param>
    internal void ExtrairPrk(Span<byte> prk)
    {
        LancarSeDescartada();
        HKDF.Extract(HashAlgorithmName.SHA256, _valor, SalDaDerivacao, prk);
        if (Volatile.Read(ref _descartada) != 0)
        {
            CryptographicOperations.ZeroMemory(prk);
            throw new ObjectDisposedException(nameof(ChaveIncognito));
        }
    }

    // K_impressao = HKDF-Expand(PRK, "impressao-digital", 32); a impressão são os 8 primeiros bytes de
    // HMAC-SHA256(K_impressao, "v1") em hexadecimal. Não depende do ambiente.
    private static string CalcularImpressaoDigital(ReadOnlySpan<byte> valor)
    {
        Span<byte> prk = stackalloc byte[32];
        Span<byte> chaveDeImpressao = stackalloc byte[32];
        Span<byte> mac = stackalloc byte[32];
        try
        {
            HKDF.Extract(HashAlgorithmName.SHA256, valor, SalDaDerivacao, prk);
            HKDF.Expand(HashAlgorithmName.SHA256, prk, chaveDeImpressao, "impressao-digital"u8);
            HMACSHA256.HashData(chaveDeImpressao, "v1"u8, mac);
            return Convert.ToHexStringLower(mac[..8]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(chaveDeImpressao);
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    // 43 caracteres do alfabeto base64url, e os 2 bits que sobram no último (32 bytes ocupam 256 dos 258 bits) em zero.
    private static bool EhBase64UrlCanonico(ReadOnlySpan<byte> base64)
    {
        if (base64.Length != TamanhoBase64)
        {
            return false;
        }

        foreach (var caractere in base64)
        {
            if (ValorBase64Url(caractere) < 0)
            {
                return false;
            }
        }

        return (ValorBase64Url(base64[^1]) & 0b11) == 0;
    }

    private static int ValorBase64Url(byte caractere) => caractere switch
    {
        >= (byte)'A' and <= (byte)'Z' => caractere - 'A',
        >= (byte)'a' and <= (byte)'z' => caractere - 'a' + 26,
        >= (byte)'0' and <= (byte)'9' => caractere - '0' + 52,
        (byte)'-' => 62,
        (byte)'_' => 63,
        _ => -1,
    };

    private void LancarSeDescartada() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartada) != 0, this);

    private static IncognitoException FormatoInvalido() => new(
        CodigoFormatoInvalido,
        "Chave do Incognito em formato inválido. O formato é 'inc1_' seguido de 43 caracteres base64url sem padding; gere uma com 'incognito gerar-chave'.");
}
