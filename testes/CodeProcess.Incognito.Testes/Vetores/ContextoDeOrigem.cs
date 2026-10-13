using System.Security.Cryptography;
using System.Text.Json;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>O que uma origem por inversa precisa do caso: chave, domínio, versão, ambiente e opções.</summary>
internal sealed record ContextoDeOrigem(ChaveIncognito Chave, string Dominio, int Versao, string Ambiente, bool PermutarOrdem, long N)
{
    internal static ContextoDeOrigem Do(ChaveIncognito chave, int versao, JsonElement caso) => new(
        chave,
        caso.GetProperty("dominio").GetString()!,
        versao,
        caso.GetProperty("ambiente").GetString()!,
        caso.TryGetProperty("permutarOrdem", out var ordem) && ordem.GetBoolean(),
        caso.TryGetProperty("n", out var n) ? n.GetInt64() : 0);

    /// <summary>A permutação do domínio, sem exclusões, com a chave derivada como o pseudonimizador deriva.</summary>
    internal PermutacaoFeistel Feistel(string variante, uint radix, ulong a, ulong b)
    {
        Span<byte> chaveDeDominio = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarChaveDeDominio(Chave, Dominio, Versao, variante, Ambiente, chaveDeDominio);
            return new PermutacaoFeistel(chaveDeDominio, radix, a, b);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeDominio);
        }
    }
}
