using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// Permutação Feistel modular sobre Z_a × Z_b (N = a·b), no estilo do FF1 sem binarizar. Não é criptografia
/// padronizada: é a bijeção determinística que troca um valor por outro do mesmo domínio sem colisão.
/// </summary>
/// <remarks>
/// <para>
/// Com <c>L = x div b</c> e <c>R = x mod b</c>, cada rodada i calcula <c>C = (L + F_i(R)) mod m_i</c>, com
/// <c>m_i</c> = a nas rodadas pares e b nas ímpares, e passa a <c>L = R</c>, <c>R = C</c>. A saída é <c>L·b + R</c>.
/// A função de rodada é
/// <c>F_i(v) = U64BE(HMAC-SHA256(K_dominio, "R" ‖ u8(i) ‖ u32be(radix) ‖ u64be(a) ‖ u64be(b) ‖ u16be(len(ajuste)) ‖ ajuste ‖ u64be(v))[0..8]) mod m_i</c>.
/// O número de rodadas é par: com a ≠ b e número ímpar, a saída cairia fora do domínio. Ele não entra na função de
/// rodada, então uma chave de domínio é usada sempre com o mesmo número de rodadas (cada variante tem a sua chave).
/// </para>
/// <para>
/// Thread-safe: cada thread usa a sua instância de HMAC, preparada uma vez com a chave. O descarte não é: quem
/// descarta é o dono, depois de todo uso. Um descarte que cruze com o uso lança <see cref="ObjectDisposedException"/>
/// ou termina o cálculo com a chave ainda íntegra; nunca entrega resultado de chave zerada.
/// </para>
/// </remarks>
internal sealed class PermutacaoFeistel : IDisposable
{
    /// <summary>Rodadas usadas pelos transformadores.</summary>
    internal const int RodadasPadrao = 12;

    private const int RodadasMinimas = 10;
    private const int RodadasMaximas = 254;
    private const ulong LimiteDePassos = 64;
    private const int TamanhoDoCabecalho = 24;
    private const int TamanhoNaPilha = 256;
    private const int TamanhoDoMac = 32;
    private const string CodigoLimiteExcedido = "INC1002";

    private readonly byte[] _chave;
    private readonly uint _radix;
    private readonly ulong _a;
    private readonly ulong _b;
    private readonly int _rodadas;
    private readonly ThreadLocal<IncrementalHash> _hmacDaThread;
    private int _descartada;

    /// <summary>Prepara a permutação de um domínio.</summary>
    /// <param name="chaveDeDominio">Os 32 bytes de <c>K_dominio</c>; a permutação guarda uma cópia.</param>
    /// <param name="radix">Base de numeração dos valores (10 ou 36), que entra na função de rodada.</param>
    /// <param name="a">Tamanho do lado esquerdo, de 2 a 2³² − 1.</param>
    /// <param name="b">Tamanho do lado direito, de 2 a 2³² − 1, no máximo radix vezes o outro lado.</param>
    /// <param name="rodadas">Número par de rodadas, de 10 a 254.</param>
    internal PermutacaoFeistel(ReadOnlySpan<byte> chaveDeDominio, uint radix, ulong a, ulong b, int rodadas = RodadasPadrao)
    {
        if (chaveDeDominio.Length != Subchaves.Tamanho)
        {
            throw new ArgumentException("A chave de domínio precisa ter 32 bytes.", nameof(chaveDeDominio));
        }

        if (radix < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), "O radix precisa ser pelo menos 2.");
        }

        ValidarLado(a, nameof(a));
        ValidarLado(b, nameof(b));

        // Com um lado muito menor, a metade grande só recebe deslocamentos de um conjunto pequeno (com a = 2 e
        // b = 10⁴, 64 valores): um par conhecido entregaria os outros. O FF1 limita a proporção ao radix.
        if (Math.Max(a, b) > radix * Math.Min(a, b))
        {
            throw new ArgumentException("Um lado da permutação passa de radix vezes o outro.", nameof(b));
        }

        if (rodadas % 2 != 0)
        {
            throw new ArgumentException(
                "O número de rodadas precisa ser par: com a ≠ b e número ímpar, a saída cai fora do domínio.", nameof(rodadas));
        }

        if (rodadas is < RodadasMinimas or > RodadasMaximas)
        {
            throw new ArgumentOutOfRangeException(nameof(rodadas), "O número de rodadas precisa estar entre 10 e 254.");
        }

        _radix = radix;
        _a = a;
        _b = b;
        _rodadas = rodadas;
        _chave = GC.AllocateArray<byte>(Subchaves.Tamanho, pinned: true);
        chaveDeDominio.CopyTo(_chave);
        _hmacDaThread = new ThreadLocal<IncrementalHash>(CriarHmac, trackAllValues: true);
    }

    /// <summary>Zera a cópia da chave se a permutação for coletada sem ter sido descartada.</summary>
    ~PermutacaoFeistel()
    {
        CryptographicOperations.ZeroMemory(_chave);
    }

    /// <summary>N = a·b, a quantidade de valores do domínio [0, N).</summary>
    internal ulong Tamanho => _a * _b;

    /// <summary>Permuta um valor do domínio.</summary>
    /// <param name="x">Valor em [0, N).</param>
    /// <param name="ajuste">Ajuste (tweak) da permutação, até 65.535 bytes.</param>
    /// <returns>O valor permutado, em [0, N).</returns>
    internal ulong Permutar(ulong x, ReadOnlySpan<byte> ajuste)
    {
        Validar(x, nameof(x), ajuste);
        return Aplicar(x, ajuste, inversa: false);
    }

    /// <summary>Desfaz <see cref="Permutar(ulong, ReadOnlySpan{byte})"/>. Uso interno e de testes.</summary>
    /// <param name="y">Valor permutado em [0, N).</param>
    /// <param name="ajuste">O mesmo ajuste usado ao permutar.</param>
    /// <returns>O valor original.</returns>
    internal ulong Inverter(ulong y, ReadOnlySpan<byte> ajuste)
    {
        Validar(y, nameof(y), ajuste);
        return Aplicar(y, ajuste, inversa: true);
    }

    /// <summary>
    /// Permuta evitando os valores excluídos (cycle-walking): enquanto a saída estiver excluída, permuta de novo.
    /// Um valor que já está excluído volta como ponto fixo, o que mantém a bijeção; quem chama identifica o caso
    /// por <see cref="ConjuntoExcluido.Contem"/> e o conta no relatório.
    /// </summary>
    /// <param name="x">Valor em [0, N).</param>
    /// <param name="ajuste">Ajuste (tweak) da permutação, até 65.535 bytes.</param>
    /// <param name="excluidos">Valores proibidos na saída, no máximo metade do domínio.</param>
    /// <returns>O valor permutado, fora dos excluídos (ou o próprio valor, se ele estiver excluído).</returns>
    /// <exception cref="IncognitoException">Com o código <c>INC1002</c>, se o limite defensivo de passos estourar.</exception>
    internal ulong Permutar(ulong x, ReadOnlySpan<byte> ajuste, ConjuntoExcluido excluidos) =>
        Caminhar(x, nameof(x), ajuste, excluidos, inversa: false);

    /// <summary>Desfaz <see cref="Permutar(ulong, ReadOnlySpan{byte}, ConjuntoExcluido)"/>. Uso interno e de testes.</summary>
    /// <param name="y">Valor permutado em [0, N).</param>
    /// <param name="ajuste">O mesmo ajuste usado ao permutar.</param>
    /// <param name="excluidos">Os mesmos excluídos usados ao permutar.</param>
    /// <returns>O valor original.</returns>
    /// <exception cref="IncognitoException">Com o código <c>INC1002</c>, se o limite defensivo de passos estourar.</exception>
    internal ulong Inverter(ulong y, ReadOnlySpan<byte> ajuste, ConjuntoExcluido excluidos) =>
        Caminhar(y, nameof(y), ajuste, excluidos, inversa: true);

    /// <summary>Zera a cópia da chave e descarta as instâncias de HMAC.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _descartada, 1) != 0)
        {
            return;
        }

        // A chave é zerada primeiro: uma instância de HMAC criada depois disso confere a marca e é recusada.
        CryptographicOperations.ZeroMemory(_chave);
        GC.SuppressFinalize(this);
        try
        {
            foreach (var hmac in _hmacDaThread.Values)
            {
                hmac.Dispose();
            }
        }
        finally
        {
            _hmacDaThread.Dispose();
        }
    }

    // A órbita de x volta a x, que não está em E, então no máximo |E| passos seguidos caem em E: com E pequeno o
    // limite de |E| + 1 nunca estoura. Com E grande o limite é 64, e estourar exige 64 saídas seguidas em E, chance
    // da ordem de 2⁻⁶⁴ com metade do domínio excluída: a exceção é só defensiva.
    private ulong Caminhar(ulong valor, string parametro, ReadOnlySpan<byte> ajuste, ConjuntoExcluido excluidos, bool inversa)
    {
        ArgumentNullException.ThrowIfNull(excluidos);
        Validar(valor, parametro, ajuste);
        if (excluidos.Quantidade > Tamanho / 2)
        {
            throw new ArgumentException("Os valores excluídos passam de metade do domínio.", nameof(excluidos));
        }

        if (excluidos.Contem(valor))
        {
            return valor;
        }

        var limite = Math.Min(excluidos.Quantidade + 1, LimiteDePassos);
        var atual = valor;
        for (ulong passo = 0; passo < limite; passo++)
        {
            atual = Aplicar(atual, ajuste, inversa);
            if (!excluidos.Contem(atual))
            {
                return atual;
            }
        }

        throw new IncognitoException(
            CodigoLimiteExcedido,
            limite < LimiteDePassos
                ? "A permutação excedeu o limite de passos ao evitar os valores excluídos: o conjunto exclui mais valores do que declara. Nenhum valor foi produzido."
                : "A permutação excedeu o limite de passos ao evitar os valores excluídos. Nenhum valor foi produzido.");
    }

    private ulong Aplicar(ulong valor, ReadOnlySpan<byte> ajuste, bool inversa)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartada) != 0, this);
        var hmac = _hmacDaThread.Value!;

        var tamanhoDaMensagem = TamanhoDoCabecalho + ajuste.Length + sizeof(ulong);
        byte[]? alugada = null;
        Span<byte> mensagem = tamanhoDaMensagem <= TamanhoNaPilha
            ? stackalloc byte[TamanhoNaPilha]
            : alugada = ArrayPool<byte>.Shared.Rent(tamanhoDaMensagem);
        mensagem = mensagem[..tamanhoDaMensagem];
        Span<byte> mac = stackalloc byte[TamanhoDoMac];
        try
        {
            mensagem[0] = (byte)'R';
            BinaryPrimitives.WriteUInt32BigEndian(mensagem[2..], _radix);
            BinaryPrimitives.WriteUInt64BigEndian(mensagem[6..], _a);
            BinaryPrimitives.WriteUInt64BigEndian(mensagem[14..], _b);
            BinaryPrimitives.WriteUInt16BigEndian(mensagem[22..], (ushort)ajuste.Length);
            ajuste.CopyTo(mensagem[TamanhoDoCabecalho..]);

            var esquerda = valor / _b;
            var direita = valor % _b;
            if (!inversa)
            {
                for (var rodada = 0; rodada < _rodadas; rodada++)
                {
                    var modulo = Modulo(rodada);
                    var c = (esquerda + Rodada(hmac, mensagem, mac, rodada, direita, modulo)) % modulo;
                    esquerda = direita;
                    direita = c;
                }
            }
            else
            {
                for (var rodada = _rodadas - 1; rodada >= 0; rodada--)
                {
                    var modulo = Modulo(rodada);
                    var c = direita;
                    direita = esquerda;
                    esquerda = (c + modulo - Rodada(hmac, mensagem, mac, rodada, direita, modulo)) % modulo;
                }
            }

            return (esquerda * _b) + direita;
        }
        finally
        {
            mensagem.Clear();
            mac.Clear();
            if (alugada is not null)
            {
                ArrayPool<byte>.Shared.Return(alugada);
            }
        }
    }

    private ulong Modulo(int rodada) => rodada % 2 == 0 ? _a : _b;

    private static ulong Rodada(IncrementalHash hmac, Span<byte> mensagem, Span<byte> mac, int rodada, ulong v, ulong modulo)
    {
        mensagem[1] = (byte)rodada;
        BinaryPrimitives.WriteUInt64BigEndian(mensagem[^sizeof(ulong)..], v);
        hmac.AppendData(mensagem);
        hmac.GetHashAndReset(mac);
        return BinaryPrimitives.ReadUInt64BigEndian(mac) % modulo;
    }

    private static void ValidarLado(ulong lado, string parametro)
    {
        if (lado is < 2 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parametro, "Cada lado da permutação precisa ter de 2 a 2³² − 1 valores.");
        }
    }

    private void Validar(ulong valor, string parametro, ReadOnlySpan<byte> ajuste)
    {
        // Sem o valor na exceção: ele é dado pessoal.
        if (valor >= Tamanho)
        {
            throw new ArgumentOutOfRangeException(parametro, "O valor está fora do domínio da permutação.");
        }

        if (ajuste.Length > ushort.MaxValue)
        {
            throw new ArgumentException("O ajuste da permutação passa de 65.535 bytes.", nameof(ajuste));
        }
    }

    // A barreira e a reconferência recusam uma instância criada a partir da chave já zerada. Uma instância criada
    // durante o descarte pode ficar fora da lista que ele percorre e só é liberada pelo coletor: por isso o descarte
    // vem depois de todo uso.
    private IncrementalHash CriarHmac()
    {
        var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _chave);
        Interlocked.MemoryBarrier();
        if (Volatile.Read(ref _descartada) != 0)
        {
            hmac.Dispose();
            throw new ObjectDisposedException(nameof(PermutacaoFeistel));
        }

        return hmac;
    }
}
