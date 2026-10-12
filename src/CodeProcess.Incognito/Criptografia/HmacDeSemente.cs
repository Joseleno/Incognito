using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;

namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// HMAC-SHA256 com a <c>K_semente</c> de um transformador, para os valores que não são permutados (e-mail, CEP, nome,
/// datas): a mesma mensagem dá sempre o mesmo resultado, e sem a chave nada se recupera.
/// </summary>
/// <remarks>
/// A chave fica numa cópia em <c>byte[]</c> fixado, zerada no descarte e na finalização. Thread-safe; o descarte é do
/// dono, depois de todo uso. Um descarte que cruze com o cálculo lança <see cref="ObjectDisposedException"/> ou termina
/// com a chave ainda íntegra; nunca entrega resultado de chave zerada.
/// </remarks>
internal sealed class HmacDeSemente : IDisposable
{
    /// <summary>Tamanho do resultado, em bytes.</summary>
    internal const int Tamanho = 32;

    private const int TamanhoNaPilha = 512;

    private readonly byte[] _chave;
    private int _descartada;

    /// <summary>Guarda uma cópia da chave de semente.</summary>
    /// <param name="chaveDeSemente">Os 32 bytes de <c>K_semente</c>.</param>
    internal HmacDeSemente(ReadOnlySpan<byte> chaveDeSemente)
    {
        if (chaveDeSemente.Length != Subchaves.Tamanho)
        {
            throw new ArgumentException("A chave de semente precisa ter 32 bytes.", nameof(chaveDeSemente));
        }

        _chave = GC.AllocateArray<byte>(Subchaves.Tamanho, pinned: true);
        chaveDeSemente.CopyTo(_chave);
    }

    /// <summary>Zera a cópia da chave se a instância for coletada sem ter sido descartada.</summary>
    ~HmacDeSemente()
    {
        CryptographicOperations.ZeroMemory(_chave);
    }

    /// <summary>Deriva a <c>K_semente</c> de um transformador e guarda a cópia; a derivada temporária é zerada.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="transformador">Nome do transformador (<c>email</c>, <c>br.cep</c>).</param>
    /// <param name="dominio">Nome canônico do domínio.</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <returns>O HMAC da semente.</returns>
    internal static HmacDeSemente Derivar(ChaveIncognito chave, string transformador, string dominio, int versao, string ambiente)
    {
        Span<byte> chaveDeSemente = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarSemente(chave, transformador, dominio, versao, ambiente, chaveDeSemente);
            return new HmacDeSemente(chaveDeSemente);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeSemente);
        }
    }

    /// <summary>Calcula o HMAC da mensagem.</summary>
    /// <param name="mensagem">A mensagem.</param>
    /// <param name="destino">Onde escrever os 32 bytes do resultado.</param>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal void Calcular(ReadOnlySpan<byte> mensagem, Span<byte> destino)
    {
        if (destino.Length != Tamanho)
        {
            throw new ArgumentException("O destino precisa ter 32 bytes.", nameof(destino));
        }

        HMACSHA256.HashData(_chave, mensagem, destino);

        // O descarte marca antes de zerar: se a marca não está aqui depois do cálculo, a chave estava íntegra durante
        // todo ele; se está, o resultado é descartado.
        Interlocked.MemoryBarrier();
        if (Volatile.Read(ref _descartada) != 0)
        {
            CryptographicOperations.ZeroMemory(destino);
            throw new ObjectDisposedException(nameof(HmacDeSemente));
        }
    }

    /// <summary>
    /// Calcula o HMAC de <c>prefixo ‖ UTF-8(texto)</c>. Os bytes do texto ficam na pilha (ou num buffer emprestado, se
    /// forem muitos) e são zerados depois, porque são dado pessoal.
    /// </summary>
    /// <param name="prefixo">Bytes antes do texto (vazio se não houver).</param>
    /// <param name="texto">O texto.</param>
    /// <param name="destino">Onde escrever os 32 bytes do resultado.</param>
    /// <param name="recusarInvalido">
    /// Com <see langword="true"/>, UTF-16 inválido (surrogate sem par) não é calculado; com <see langword="false"/>, cada
    /// sequência inválida vira U+FFFD.
    /// </param>
    /// <returns><see langword="false"/> se o texto tem UTF-16 inválido e <paramref name="recusarInvalido"/> é verdadeiro.</returns>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal bool Calcular(ReadOnlySpan<byte> prefixo, ReadOnlySpan<char> texto, Span<byte> destino, bool recusarInvalido)
    {
        var maximo = prefixo.Length + Encoding.UTF8.GetMaxByteCount(texto.Length);
        var emprestado = maximo > TamanhoNaPilha ? ArrayPool<byte>.Shared.Rent(maximo) : null;
        Span<byte> mensagem = emprestado is null ? stackalloc byte[TamanhoNaPilha] : emprestado.AsSpan(0, maximo);
        try
        {
            prefixo.CopyTo(mensagem);
            var situacao = Utf8.FromUtf16(texto, mensagem[prefixo.Length..], out _, out var escritos, replaceInvalidSequences: !recusarInvalido);
            if (situacao != OperationStatus.Done)
            {
                return false;
            }

            Calcular(mensagem[..(prefixo.Length + escritos)], destino);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mensagem);
            if (emprestado is not null)
            {
                ArrayPool<byte>.Shared.Return(emprestado);
            }
        }
    }

    /// <summary>Zera a cópia da chave.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _descartada, 1) != 0)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_chave);
        GC.SuppressFinalize(this);
    }
}
