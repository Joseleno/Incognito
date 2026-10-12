using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Transformador <c>id.permutar</c>: permuta identificadores inteiros positivos em <c>[1, N)</c>, com N = 10^d de 10⁴ a
/// 10¹⁸, sobre Z_a × Z_b com a = 10^⌈d/2⌉ e b = 10^⌊d/2⌋ (variante <c>id</c>, o zero excluído da saída). Mesma chave,
/// domínio, versão, ambiente e N dão o mesmo pseudônimo; a permutação é uma bijeção, então identificadores diferentes
/// nunca colidem e as FKs do mesmo domínio continuam apontando para a mesma linha.
/// </summary>
/// <remarks>
/// <para>
/// O ajuste é N em decimal ASCII. Quem escolhe N é o motor (a menor potência de 10 maior que 10 × o maior valor da PK
/// na origem); quando N muda, o pseudônimo muda.
/// </para>
/// <para>
/// Zero e negativos ficam fora do domínio e voltam como vieram (<see cref="Desfecho.PontoFixo"/>), o que mantém a
/// bijeção. Um identificador a partir de N segue a política de entrada malformada; copiado em claro, o mapeamento
/// continua injetivo, porque toda saída da permutação fica abaixo de N. Nenhuma exceção traz o valor.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorIdentificador : IDisposable
{
    private const int DigitosMinimos = 4;
    private const int DigitosMaximos = 18;
    private const string MensagemMalformado =
        "Identificador maior ou igual a N, fora do domínio da permutação; a política entradaMalformada é falhar. O valor não é exibido.";

    private static readonly ConjuntoExcluido _zero = ConjuntoExcluido.DeValores(0);

    private readonly PermutacaoFeistel _permutacao;
    private readonly byte[] _ajuste;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a permutação dos identificadores de um domínio.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio, declarado na coluna de PK.</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="tamanho">N, uma potência de 10 de 10⁴ a 10¹⁸.</param>
    /// <param name="malformada">Política para identificador a partir de N.</param>
    /// <exception cref="ArgumentOutOfRangeException">N fora das potências aceitas ou política fora das definidas.</exception>
    internal PseudonimizadorIdentificador(
        ChaveIncognito chave,
        string dominio,
        int versao,
        string ambiente,
        long tamanho,
        PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, a permutação criada ficaria sem descarte.
        Politicas.Validar(malformada, nameof(malformada));
        var digitos = Digitos(tamanho);

        Span<byte> chaveDeDominio = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Identificador, ambiente, chaveDeDominio);
            _permutacao = new PermutacaoFeistel(
                chaveDeDominio, 10, Potencia((digitos + 1) / 2), Potencia(digitos / 2));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeDominio);
        }

        _ajuste = Encoding.ASCII.GetBytes(tamanho.ToString(CultureInfo.InvariantCulture));
        _malformada = malformada;
        Tamanho = tamanho;
    }

    /// <summary>N, o limite (exclusivo) do domínio permutado, que vai para o relatório.</summary>
    internal long Tamanho { get; }

    /// <summary>Pseudonimiza um identificador.</summary>
    /// <param name="entrada">O identificador como veio da coluna.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se zero, negativa ou copiada em claro.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (identificador a partir de N), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(long entrada, out long? saida)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        if (entrada <= 0)
        {
            saida = entrada;
            return Desfecho.PontoFixo;
        }

        if (entrada >= Tamanho)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }

        saida = (long)_permutacao.Permutar((ulong)entrada, _ajuste, _zero);
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Descarta a permutação e a cópia da chave de domínio.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _permutacao.Dispose();
    }

    // O expoente d de N = 10^d, recusando o que não for potência de 10 de 10⁴ a 10¹⁸ (10¹⁹ já não cabe em long).
    private static int Digitos(long tamanho)
    {
        var potencia = 1L;
        for (var digitos = 1; digitos <= DigitosMaximos; digitos++)
        {
            potencia *= 10;
            if (potencia == tamanho && digitos >= DigitosMinimos)
            {
                return digitos;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(tamanho), "N precisa ser uma potência de 10 de 10⁴ a 10¹⁸.");
    }

    private static ulong Potencia(int expoente)
    {
        ulong valor = 1;
        for (var i = 0; i < expoente; i++)
        {
            valor *= 10;
        }

        return valor;
    }
}
