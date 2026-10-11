using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de CNPJ, numérico ou alfanumérico: a raiz (8 caracteres) é permutada e a ordem é preservada
/// literalmente, para matriz e filiais continuarem juntas; os DVs são recalculados. A variante sai da raiz: raiz só
/// com dígitos usa <c>num</c> (10⁴ × 10⁴, sem a raiz zero); raiz com letra usa <c>alfa</c> (36⁴ × 36⁴ em base 36,
/// sem as raízes só com dígitos). A presença de letra na raiz não muda, e CNPJs diferentes nunca colidem.
/// </summary>
/// <remarks>
/// <para>
/// Com <c>permutarOrdem</c>, os 12 caracteres são permutados juntos (variante <c>ordem</c>, a mesma chave nas duas
/// partes), particionados pela presença de letra nos 12: só com dígitos em 10⁶ × 10⁶, com letra em 36⁶ × 36⁶ sem as
/// cadeias só com dígitos. O que não muda é a presença de letra nos 12, não na raiz. Matriz e filiais deixam de ficar
/// juntas.
/// </para>
/// <para>
/// Formas aceitas: 14 caracteres, a máscara <c>00.000.000/0000-00</c> e, em texto só com dígitos, 12 ou 13 dígitos
/// (CNPJ que perdeu os zeros à esquerda), completados com zeros e devolvidos com 14. Raiz e ordem com dígitos ou
/// letras maiúsculas; DV só com dígitos; letra minúscula é malformada. Espaços à direita (<c>char(n)</c>) voltam como
/// estavam, descontados os zeros acrescentados. Texto vazio ou só com espaços volta como veio. Em coluna numérica, todo
/// valor de 0 a 99.999.999.999.999 é um CNPJ numérico com zeros à esquerda.
/// </para>
/// <para>
/// DV errado com <see cref="PoliticaEntradaInvalida.Preservar"/>: o deslocamento <c>δ</c> mod 100 é mantido, como no
/// CPF. A política de DV vem antes do ponto fixo: CNPJ de raiz zero (com qualquer ordem, com ou sem letra, nos dois
/// modos) e DV certo continua igual em toda política e é contado como ponto fixo; com DV errado, segue a política, e
/// preservar o mantém como veio. Nenhuma saída tem raiz zero, e as outras raízes repdígito são permutadas. Nenhuma
/// exceção traz o valor.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorCnpj : IDisposable
{
    private const int TamanhoDaRaiz = 8;
    private const int TamanhoDaBase = 12;
    private const long MaiorCnpj = 99_999_999_999_999;
    private const string MensagemTextoMalformado =
        "Valor fora da forma de CNPJ (14 caracteres, máscara 00.000.000/0000-00 ou 12 ou 13 dígitos sem os zeros à esquerda; raiz e ordem com dígitos ou letras maiúsculas, DV com dígitos); a política entradaMalformada é falhar. O valor não é exibido.";
    private const string MensagemNumeroMalformado =
        "Número fora da faixa de CNPJ (0 a 99.999.999.999.999); a política entradaMalformada é falhar. O valor não é exibido.";

    private readonly PermutacaoParticionada _permutacao;
    private readonly int _permutados;
    private readonly PoliticaEntradaInvalida _invalida;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de CNPJ.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (por padrão, <c>cnpj</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="permutarOrdem">Permutar os 12 caracteres em vez de só a raiz.</param>
    /// <param name="invalida">Política para DV errado.</param>
    /// <param name="malformada">Política para valor fora da forma de CNPJ.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal PseudonimizadorCnpj(
        ChaveIncognito chave,
        string dominio,
        int versao,
        string ambiente,
        bool permutarOrdem,
        PoliticaEntradaInvalida invalida,
        PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, a permutação criada ficaria sem descarte.
        Politicas.Validar(invalida, nameof(invalida));
        Politicas.Validar(malformada, nameof(malformada));

        Span<byte> chaves = stackalloc byte[2 * Subchaves.Tamanho];
        var numerica = chaves[..Subchaves.Tamanho];
        var alfanumerica = chaves[Subchaves.Tamanho..];
        try
        {
            if (permutarOrdem)
            {
                Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Ordem, ambiente, numerica);
                _permutacao = new PermutacaoParticionada(numerica, numerica, TamanhoDaBase, TamanhoDaRaiz);
            }
            else
            {
                Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Numerico, ambiente, numerica);
                Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Alfanumerico, ambiente, alfanumerica);
                _permutacao = new PermutacaoParticionada(numerica, alfanumerica, TamanhoDaRaiz, TamanhoDaRaiz);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaves);
        }

        _permutados = permutarOrdem ? TamanhoDaBase : TamanhoDaRaiz;
        _invalida = invalida;
        _malformada = malformada;
    }

    /// <summary>Pseudonimiza um CNPJ em texto.</summary>
    /// <param name="entrada">O CNPJ como veio da coluna.</param>
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

        Span<char> caracteres = stackalloc char[14];
        bool comMascara;
        if (corpo.Length == 18 && corpo[2] == '.' && corpo[6] == '.' && corpo[10] == '/' && corpo[15] == '-')
        {
            corpo[..2].CopyTo(caracteres);
            corpo[3..6].CopyTo(caracteres[2..]);
            corpo[7..10].CopyTo(caracteres[5..]);
            corpo[11..15].CopyTo(caracteres[8..]);
            corpo[16..].CopyTo(caracteres[12..]);
            comMascara = true;
        }
        else if (corpo.Length == 14)
        {
            corpo.CopyTo(caracteres);
            comMascara = false;
        }
        else if (corpo.Length is 12 or 13 && SoDigitos(corpo))
        {
            var zeros = 14 - corpo.Length;
            caracteres[..zeros].Fill('0');
            corpo.CopyTo(caracteres[zeros..]);
            preenchimento = preenchimento[Math.Min(zeros, preenchimento.Length)..];
            comMascara = false;
        }
        else
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        if (!SoDigitosEMaiusculas(caracteres[..TamanhoDaBase]) || !SoDigitos(caracteres[TamanhoDaBase..]))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        var desfecho = Transformar(caracteres);
        if (desfecho == Desfecho.AnuladoDvInvalido)
        {
            saida = null;
            return desfecho;
        }

        Span<char> formatado = stackalloc char[18];
        saida = string.Concat(formatado[..Formatar(caracteres, comMascara, formatado)], preenchimento);
        return desfecho;
    }

    /// <summary>Pseudonimiza um CNPJ numérico guardado em coluna numérica.</summary>
    /// <param name="entrada">O CNPJ como número, sem os zeros à esquerda.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado) ou <c>INC1004</c> (DV errado), pelas políticas.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(long entrada, out long? saida)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        if (entrada is < 0 or > MaiorCnpj)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemNumeroMalformado, entrada, out saida);
        }

        Span<char> caracteres = stackalloc char[14];
        var restante = entrada;
        for (var i = 13; i >= 0; i--)
        {
            caracteres[i] = (char)('0' + (int)(restante % 10));
            restante /= 10;
        }

        var desfecho = Transformar(caracteres);
        saida = desfecho == Desfecho.AnuladoDvInvalido ? null : Numero(caracteres);
        return desfecho;
    }

    /// <summary>Descarta as permutações e as cópias das chaves de domínio.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _permutacao.Dispose();
    }

    // Troca os 14 caracteres no lugar: raiz (ou raiz e ordem) permutada e DV recalculado com o mesmo deslocamento.
    private Desfecho Transformar(Span<char> caracteres)
    {
        var dvEntrada = ((caracteres[12] - '0') * 10) + (caracteres[13] - '0');
        var deslocamento = (dvEntrada - DigitosVerificadores.Cnpj(caracteres[..TamanhoDaBase]) + 100) % 100;
        if (deslocamento != 0 && !Politicas.SeguirComDvInvalido(_invalida, "CNPJ"))
        {
            return Desfecho.AnuladoDvInvalido;
        }

        var permutados = caracteres[.._permutados];
        if (_permutacao.Permutar(permutados, permutados))
        {
            return Desfecho.PontoFixo;
        }

        var dvSaida = (DigitosVerificadores.Cnpj(caracteres[..TamanhoDaBase]) + deslocamento) % 100;
        caracteres[12] = (char)('0' + (dvSaida / 10));
        caracteres[13] = (char)('0' + (dvSaida % 10));
        return deslocamento == 0 ? Desfecho.Pseudonimizado : Desfecho.DvInvalidoPreservado;
    }

    private static int Formatar(ReadOnlySpan<char> caracteres, bool comMascara, Span<char> destino)
    {
        if (!comMascara)
        {
            caracteres.CopyTo(destino);
            return 14;
        }

        caracteres[..2].CopyTo(destino);
        destino[2] = '.';
        caracteres[2..5].CopyTo(destino[3..]);
        destino[6] = '.';
        caracteres[5..8].CopyTo(destino[7..]);
        destino[10] = '/';
        caracteres[8..12].CopyTo(destino[11..]);
        destino[15] = '-';
        caracteres[12..].CopyTo(destino[16..]);
        return 18;
    }

    private static long Numero(ReadOnlySpan<char> digitos)
    {
        long numero = 0;
        foreach (var digito in digitos)
        {
            numero = (numero * 10) + (digito - '0');
        }

        return numero;
    }
}
