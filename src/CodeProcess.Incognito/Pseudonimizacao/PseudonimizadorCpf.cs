using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de CPF: a base de 9 dígitos é permutada no domínio (variante <c>num</c>, 10⁴ × 10⁵, sem os 10
/// repdígitos) e os DVs são recalculados. Mesma chave, domínio, versão e ambiente dão o mesmo pseudônimo; a
/// permutação é uma bijeção, então CPFs diferentes nunca colidem.
/// </summary>
/// <remarks>
/// <para>
/// Formas aceitas: 11 dígitos, a máscara <c>000.000.000-00</c> e, em texto só com dígitos, 9 ou 10 dígitos (CPF que
/// perdeu os zeros à esquerda), completados com zeros e devolvidos com 11. Espaços à direita (preenchimento de
/// <c>char(n)</c>) voltam como estavam, descontados os zeros acrescentados, para o valor não ficar mais largo que a
/// coluna. Texto vazio ou só com espaços volta como veio (<see cref="Desfecho.Vazio"/>). Em coluna numérica, todo
/// valor de 0 a 99.999.999.999 é um CPF com zeros à esquerda.
/// </para>
/// <para>
/// DV errado com <see cref="PoliticaEntradaInvalida.Preservar"/>: <c>δ = (DV_entrada − DV_correto(base)) mod 100</c> e a
/// saída é <c>P(base) ‖ ((DV_correto(P(base)) + δ) mod 100)</c>, injetiva e ainda inválida. A política de DV vem antes
/// do ponto fixo: base repdígito com DV certo (pelo módulo 11) é ponto fixo em toda política; com DV errado, segue a
/// política, e preservar a mantém como veio. Nenhuma exceção traz o valor.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorCpf : IDisposable
{
    private const ulong TamanhoA = 10_000;
    private const ulong TamanhoB = 100_000;
    private const long MaiorCpf = 99_999_999_999;
    private const string MensagemTextoMalformado =
        "Valor fora da forma de CPF (11 dígitos, máscara 000.000.000-00 ou 9 ou 10 dígitos sem os zeros à esquerda); a política entradaMalformada é falhar. O valor não é exibido.";
    private const string MensagemNumeroMalformado =
        "Número fora da faixa de CPF (0 a 99.999.999.999); a política entradaMalformada é falhar. O valor não é exibido.";

    private static readonly ConjuntoExcluido _repdigitos = ConjuntoExcluido.DeValores(
        0, 111_111_111, 222_222_222, 333_333_333, 444_444_444, 555_555_555, 666_666_666, 777_777_777, 888_888_888, 999_999_999);

    private readonly PermutacaoFeistel _permutacao;
    private readonly PoliticaEntradaInvalida _invalida;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de CPF.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (por padrão, <c>cpf</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="invalida">Política para DV errado.</param>
    /// <param name="malformada">Política para valor fora da forma de CPF.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal PseudonimizadorCpf(
        ChaveIncognito chave,
        string dominio,
        int versao,
        string ambiente,
        PoliticaEntradaInvalida invalida,
        PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, a permutação criada ficaria sem descarte.
        Politicas.Validar(invalida, nameof(invalida));
        Politicas.Validar(malformada, nameof(malformada));

        Span<byte> chaveDeDominio = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Numerico, ambiente, chaveDeDominio);
            _permutacao = new PermutacaoFeistel(chaveDeDominio, 10, TamanhoA, TamanhoB);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeDominio);
        }

        _invalida = invalida;
        _malformada = malformada;
    }

    /// <summary>Política para valor fora da forma, conferida por quem compõe este pseudonimizador (a chave Pix).</summary>
    internal PoliticaEntradaMalformada Malformada => _malformada;

    /// <summary>Pseudonimiza um CPF em texto.</summary>
    /// <param name="entrada">O CPF como veio da coluna.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro ou vazia.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado) ou <c>INC1004</c> (DV errado), pelas políticas.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(string entrada, out string? saida)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        var corpo = entrada.AsSpan().TrimEnd(' ');
        var preenchimento = entrada.AsSpan(corpo.Length);
        if (corpo.IsEmpty)
        {
            saida = entrada;
            return Desfecho.Vazio;
        }

        Span<char> digitos = stackalloc char[11];
        bool comMascara;
        if (corpo.Length == 14 && corpo[3] == '.' && corpo[7] == '.' && corpo[11] == '-')
        {
            corpo[..3].CopyTo(digitos);
            corpo[4..7].CopyTo(digitos[3..]);
            corpo[8..11].CopyTo(digitos[6..]);
            corpo[12..].CopyTo(digitos[9..]);
            comMascara = true;
        }
        else if (corpo.Length is >= 9 and <= 11)
        {
            var zeros = 11 - corpo.Length;
            digitos[..zeros].Fill('0');
            corpo.CopyTo(digitos[zeros..]);
            preenchimento = preenchimento[Math.Min(zeros, preenchimento.Length)..];
            comMascara = false;
        }
        else
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        if (!SoDigitos(digitos))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        var desfecho = Transformar(Numero(digitos[..9]), (int)Numero(digitos[9..]), out var baseSaida, out var dvSaida);
        if (desfecho == Desfecho.AnuladoDvInvalido)
        {
            saida = null;
            return desfecho;
        }

        Span<char> formatado = stackalloc char[14];
        saida = string.Concat(formatado[..Formatar(baseSaida, dvSaida, comMascara, formatado)], preenchimento);
        return desfecho;
    }

    /// <summary>Pseudonimiza um CPF guardado em coluna numérica.</summary>
    /// <param name="entrada">O CPF como número, sem os zeros à esquerda.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado) ou <c>INC1004</c> (DV errado), pelas políticas.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(long entrada, out long? saida)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        if (entrada is < 0 or > MaiorCpf)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemNumeroMalformado, entrada, out saida);
        }

        var desfecho = Transformar((ulong)(entrada / 100), (int)(entrada % 100), out var baseSaida, out var dvSaida);
        saida = desfecho == Desfecho.AnuladoDvInvalido ? null : ((long)baseSaida * 100) + dvSaida;
        return desfecho;
    }

    /// <summary>Descarta a permutação e a cópia da chave de domínio.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _permutacao.Dispose();
    }

    private Desfecho Transformar(ulong baseEntrada, int dvEntrada, out ulong baseSaida, out int dvSaida)
    {
        var dvCorreto = Dv(baseEntrada);
        var deslocamento = (dvEntrada - dvCorreto + 100) % 100;
        if (deslocamento != 0 && !Politicas.SeguirComDvInvalido(_invalida, "CPF"))
        {
            baseSaida = 0;
            dvSaida = 0;
            return Desfecho.AnuladoDvInvalido;
        }

        if (_repdigitos.Contem(baseEntrada))
        {
            baseSaida = baseEntrada;
            dvSaida = dvEntrada;
            return Desfecho.PontoFixo;
        }

        baseSaida = _permutacao.Permutar(baseEntrada, [], _repdigitos);
        dvSaida = (Dv(baseSaida) + deslocamento) % 100;
        return deslocamento == 0 ? Desfecho.Pseudonimizado : Desfecho.DvInvalidoPreservado;
    }

    // Escreve da direita para a esquerda: DVs, hífen e a base, com os pontos da máscara a cada três dígitos.
    private static int Formatar(ulong baseCpf, int dv, bool comMascara, Span<char> destino)
    {
        var tamanho = comMascara ? 14 : 11;
        var posicao = tamanho;
        destino[--posicao] = (char)('0' + (dv % 10));
        destino[--posicao] = (char)('0' + (dv / 10));
        if (comMascara)
        {
            destino[--posicao] = '-';
        }

        for (var i = 0; i < 9; i++)
        {
            if (comMascara && i is 3 or 6)
            {
                destino[--posicao] = '.';
            }

            destino[--posicao] = (char)('0' + (int)(baseCpf % 10));
            baseCpf /= 10;
        }

        return tamanho;
    }

    private static int Dv(ulong baseCpf)
    {
        Span<char> digitos = stackalloc char[9];
        for (var i = 8; i >= 0; i--)
        {
            digitos[i] = (char)('0' + (int)(baseCpf % 10));
            baseCpf /= 10;
        }

        return DigitosVerificadores.Cpf(digitos);
    }

    private static ulong Numero(ReadOnlySpan<char> digitos)
    {
        ulong numero = 0;
        foreach (var digito in digitos)
        {
            numero = (numero * 10) + (ulong)(digito - '0');
        }

        return numero;
    }
}
