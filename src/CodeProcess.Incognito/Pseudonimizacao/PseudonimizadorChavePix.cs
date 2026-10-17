using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de chave Pix: o tipo sai da forma da chave e continua o mesmo. CPF, CNPJ, e-mail e telefone vão aos
/// pseudonimizadores das colunas correspondentes, recebidos prontos, então a chave sai igual ao pseudônimo da coluna
/// (mesmo domínio, versão, ambiente e opções); a chave aleatória sai do HMAC da <c>K_semente</c> sobre os 16 bytes do
/// UUID. Injetividade garantida para CPF, CNPJ (sem distinguir a caixa) e telefone, e probabilística para e-mail (após
/// a normalização) e aleatória: as saídas dos tipos não se cruzam.
/// </summary>
/// <remarks>
/// <para>
/// Classificação sobre o texto sem os espaços à direita (o preenchimento de <c>char(n)</c>), com a entrada inteira
/// repassada ao tipo: <c>@</c> é e-mail; UUID <c>8-4-4-4-12</c> com hexadecimais ASCII em qualquer caixa (só a forma do
/// DICT) é aleatória; 11 dígitos ou a máscara <c>000.000.000-00</c> são sempre CPF, para nenhum telefone sem o
/// <c>+55</c> dividir as saídas de 11 dígitos com o CPF (um telefone assim segue a política de DV do CPF); 12 caracteres
/// alfanuméricos ASCII e 2 dígitos, ou a máscara <c>00.000.000/0000-00</c>, são CNPJ, e com minúsculas só se o DV
/// conferir depois de passá-las a maiúsculas (o DICT não distingue a caixa do CNPJ, mas só guarda chave válida);
/// <c>+</c> seguido de dígito, ou só dígitos e separadores de telefone começando por dígito ou <c>(</c>, é telefone.
/// Nenhum zero é completado: 9 e 10 dígitos (que o CPF completaria) e 12 e 13 (que o CNPJ completaria) vão para o
/// telefone. O resto segue <c>entradaMalformada</c>, a mesma em todos os tipos: o construtor recusa pseudonimizadores
/// com outra política para valor malformado. Texto vazio ou só com espaços volta como veio; os espaços à direita voltam
/// como cada tipo os trata.
/// </para>
/// <para>
/// Chave aleatória: HMAC-SHA256 de <c>"evp" ‖ 0x00 ‖</c> os 16 bytes do UUID na ordem do texto; os 16 primeiros bytes
/// do resultado, com a versão 4 (<c>b[6] = (b[6] &amp; 0x0F) | 0x40</c>) e a variante da RFC 9562
/// (<c>b[8] = (b[8] &amp; 0x3F) | 0x80</c>), saem em minúsculas na forma <c>8-4-4-4-12</c>. Os bytes não vêm do
/// <see cref="Guid"/>, que guarda os três primeiros grupos invertidos e aceita outras formas.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso, e só zera a chave de
/// semente: os pseudonimizadores recebidos continuam com quem os criou e precisam viver até o fim do uso deste.
/// Nenhuma exceção traz o valor.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorChavePix : IDisposable
{
    private const string Transformador = "br.chave-pix";
    private const int TamanhoDoUuid = 36;
    private const int BytesDoUuid = 16;
    private const string MensagemMalformado =
        "Valor fora das formas de chave Pix (CPF, CNPJ, e-mail, telefone ou chave aleatória 8-4-4-4-12); a política entradaMalformada é falhar. O valor não é exibido.";

    private readonly HmacDeSemente _hmac;
    private readonly PseudonimizadorCpf _cpf;
    private readonly PseudonimizadorCnpj _cnpj;
    private readonly PseudonimizadorEmail _email;
    private readonly PseudonimizadorTelefone _telefone;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de chave Pix.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio da chave aleatória (por padrão, <c>chave-pix</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="cpf">O pseudonimizador da coluna de CPF, que continua com quem o criou.</param>
    /// <param name="cnpj">O pseudonimizador da coluna de CNPJ, que continua com quem o criou.</param>
    /// <param name="email">O pseudonimizador da coluna de e-mail, que continua com quem o criou.</param>
    /// <param name="telefone">O pseudonimizador da coluna de telefone, que continua com quem o criou.</param>
    /// <param name="malformada">Política para valor fora das formas de chave Pix, a mesma dos quatro recebidos.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    /// <exception cref="ArgumentNullException">Pseudonimizador nulo.</exception>
    /// <exception cref="ArgumentException">Pseudonimizador com outra política para valor malformado.</exception>
    internal PseudonimizadorChavePix(
        ChaveIncognito chave,
        string dominio,
        int versao,
        string ambiente,
        PseudonimizadorCpf cpf,
        PseudonimizadorCnpj cnpj,
        PseudonimizadorEmail email,
        PseudonimizadorTelefone telefone,
        PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, a chave de semente ficaria sem descarte.
        Politicas.Validar(malformada, nameof(malformada));
        ArgumentNullException.ThrowIfNull(cpf);
        ArgumentNullException.ThrowIfNull(cnpj);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(telefone);
        ExigirMesmaPolitica(cpf.Malformada, malformada, nameof(cpf));
        ExigirMesmaPolitica(cnpj.Malformada, malformada, nameof(cnpj));
        ExigirMesmaPolitica(email.Malformada, malformada, nameof(email));
        ExigirMesmaPolitica(telefone.Malformada, malformada, nameof(telefone));

        _hmac = HmacDeSemente.Derivar(chave, Transformador, dominio, versao, ambiente);
        _cpf = cpf;
        _cnpj = cnpj;
        _email = email;
        _telefone = telefone;
        _malformada = malformada;
    }

    private enum Tipo
    {
        Nenhum,
        Cpf,
        Cnpj,
        Email,
        Telefone,
        Aleatoria,
    }

    // Rótulo da chave aleatória (EVP, no DICT) antes dos 16 bytes na mensagem do HMAC.
    private static ReadOnlySpan<byte> RotuloDaAleatoria => "evp\0"u8;

    /// <summary>Pseudonimiza uma chave Pix.</summary>
    /// <param name="entrada">A chave como veio da coluna.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro, vazia, ponto fixo ou de serviço.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado) ou <c>INC1004</c> (DV errado), pelas políticas.</exception>
    /// <exception cref="ObjectDisposedException">Este ou o pseudonimizador do tipo já descartado.</exception>
    internal Desfecho Pseudonimizar(string entrada, out string? saida)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        var corpo = entrada.AsSpan().TrimEnd(' ');
        if (corpo.IsEmpty)
        {
            saida = entrada;
            return Desfecho.Vazio;
        }

        switch (Classificar(corpo))
        {
            case Tipo.Email:
                return _email.Pseudonimizar(entrada, out saida);
            case Tipo.Telefone:
                return _telefone.Pseudonimizar(entrada, out saida);
            case Tipo.Cpf:
                return _cpf.Pseudonimizar(entrada, out saida);
            case Tipo.Cnpj:
                return PseudonimizarCnpj(entrada, corpo, out saida);
            case Tipo.Aleatoria:
                saida = PseudonimizarAleatoria(corpo, entrada.AsSpan(corpo.Length));
                return Desfecho.Pseudonimizado;
            default:
                return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }
    }

    /// <summary>Descarta a cópia da chave de semente; os pseudonimizadores recebidos ficam como estão.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _hmac.Dispose();
    }

    // Com outra política, um valor sem a forma do tipo teria outro destino que um valor sem tipo nenhum.
    private static void ExigirMesmaPolitica(PoliticaEntradaMalformada doTipo, PoliticaEntradaMalformada daChavePix, string parametro)
    {
        if (doTipo != daChavePix)
        {
            throw new ArgumentException("O pseudonimizador recebido tem outra política entradaMalformada que a chave Pix.", parametro);
        }
    }

    private static Tipo Classificar(ReadOnlySpan<char> corpo)
    {
        if (corpo.Contains('@'))
        {
            return Tipo.Email;
        }

        if (EhUuid(corpo))
        {
            return Tipo.Aleatoria;
        }

        if ((corpo.Length == 11 && SoDigitos(corpo)) || EhCpfComMascara(corpo))
        {
            return Tipo.Cpf;
        }

        if (EhCnpj(corpo))
        {
            return TemMinuscula(corpo) && !DvDeCnpjEmMinusculasConfere(corpo) ? Tipo.Nenhum : Tipo.Cnpj;
        }

        return EhTelefone(corpo) ? Tipo.Telefone : Tipo.Nenhum;
    }

    private static bool EhUuid(ReadOnlySpan<char> corpo)
    {
        if (corpo.Length != TamanhoDoUuid)
        {
            return false;
        }

        for (var i = 0; i < corpo.Length; i++)
        {
            var valido = i is 8 or 13 or 18 or 23 ? corpo[i] == '-' : char.IsAsciiHexDigit(corpo[i]);
            if (!valido)
            {
                return false;
            }
        }

        return true;
    }

    private static bool EhCpfComMascara(ReadOnlySpan<char> corpo) =>
        corpo.Length == 14
        && corpo[3] == '.'
        && corpo[7] == '.'
        && corpo[11] == '-'
        && SoDigitos(corpo[..3])
        && SoDigitos(corpo[4..7])
        && SoDigitos(corpo[8..11])
        && SoDigitos(corpo[12..]);

    // Raiz e ordem com letras de qualquer caixa e DV com dígitos, com ou sem a máscara.
    private static bool EhCnpj(ReadOnlySpan<char> corpo) => corpo.Length switch
    {
        14 => SoAlfanumericos(corpo[..12]) && SoDigitos(corpo[12..]),
        18 => corpo[2] == '.'
            && corpo[6] == '.'
            && corpo[10] == '/'
            && corpo[15] == '-'
            && SoAlfanumericos(corpo[..2])
            && SoAlfanumericos(corpo[3..6])
            && SoAlfanumericos(corpo[7..10])
            && SoAlfanumericos(corpo[11..15])
            && SoDigitos(corpo[16..]),
        _ => false,
    };

    private static bool SoAlfanumericos(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (!char.IsAsciiLetterOrDigit(caractere))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TemMinuscula(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (char.IsAsciiLetterLower(caractere))
            {
                return true;
            }
        }

        return false;
    }

    // Um CNPJ com minúsculas só é CNPJ com o DV certo depois de passá-las a maiúsculas: sem isso, um texto qualquer de 14
    // caracteres seria tratado como CNPJ de DV errado.
    private static bool DvDeCnpjEmMinusculasConfere(ReadOnlySpan<char> corpo)
    {
        Span<char> baseCnpj = stackalloc char[12];
        if (corpo.Length == 14)
        {
            corpo[..12].CopyTo(baseCnpj);
        }
        else
        {
            corpo[..2].CopyTo(baseCnpj);
            corpo[3..6].CopyTo(baseCnpj[2..]);
            corpo[7..10].CopyTo(baseCnpj[5..]);
            corpo[11..15].CopyTo(baseCnpj[8..]);
        }

        for (var i = 0; i < baseCnpj.Length; i++)
        {
            baseCnpj[i] = char.IsAsciiLetterLower(baseCnpj[i]) ? (char)(baseCnpj[i] - 32) : baseCnpj[i];
        }

        var confere = DigitosVerificadores.Cnpj(baseCnpj) == ((corpo[^2] - '0') * 10) + (corpo[^1] - '0');
        baseCnpj.Clear();
        return confere;
    }

    // O + só seguido de dígito; sem ele, só dígitos e separadores de telefone, começando por dígito ou pelo ( do DDD.
    private static bool EhTelefone(ReadOnlySpan<char> corpo)
    {
        if (corpo[0] == '+')
        {
            return corpo.Length > 1 && char.IsAsciiDigit(corpo[1]);
        }

        if (!char.IsAsciiDigit(corpo[0]) && corpo[0] != '(')
        {
            return false;
        }

        foreach (var caractere in corpo)
        {
            if (!char.IsAsciiDigit(caractere) && caractere is not (' ' or '(' or ')' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    // As minúsculas passam a maiúsculas antes do CNPJ, numa segunda string: o único caminho com duas alocações, e uma
    // cópia do valor que, como a própria entrada, não pode ser zerada. O ponto fixo volta com a caixa da entrada.
    private Desfecho PseudonimizarCnpj(string entrada, ReadOnlySpan<char> corpo, out string? saida)
    {
        if (!TemMinuscula(corpo))
        {
            return _cnpj.Pseudonimizar(entrada, out saida);
        }

        var maiusculas = string.Create(entrada.Length, entrada, static (destino, origem) =>
        {
            for (var i = 0; i < origem.Length; i++)
            {
                destino[i] = char.IsAsciiLetterLower(origem[i]) ? (char)(origem[i] - 32) : origem[i];
            }
        });
        var desfecho = _cnpj.Pseudonimizar(maiusculas, out saida);
        if (desfecho == Desfecho.PontoFixo)
        {
            saida = entrada;
        }

        return desfecho;
    }

    private string PseudonimizarAleatoria(ReadOnlySpan<char> uuid, ReadOnlySpan<char> preenchimento)
    {
        Span<char> hexadecimais = stackalloc char[2 * BytesDoUuid];
        Span<byte> mensagem = stackalloc byte[RotuloDaAleatoria.Length + BytesDoUuid];
        Span<byte> resultado = stackalloc byte[HmacDeSemente.Tamanho];
        try
        {
            uuid[..8].CopyTo(hexadecimais);
            uuid[9..13].CopyTo(hexadecimais[8..]);
            uuid[14..18].CopyTo(hexadecimais[12..]);
            uuid[19..23].CopyTo(hexadecimais[16..]);
            uuid[24..].CopyTo(hexadecimais[20..]);
            RotuloDaAleatoria.CopyTo(mensagem);
            if (Convert.FromHexString(hexadecimais, mensagem[RotuloDaAleatoria.Length..], out _, out _) != OperationStatus.Done)
            {
                throw new UnreachableException("O UUID já foi conferido.");
            }

            _hmac.Calcular(mensagem, resultado);

            resultado[6] = (byte)((resultado[6] & 0x0F) | 0x40);
            resultado[8] = (byte)((resultado[8] & 0x3F) | 0x80);
            Span<char> formatado = stackalloc char[TamanhoDoUuid];
            EscreverHexadecimais(resultado[..4], formatado);
            formatado[8] = '-';
            EscreverHexadecimais(resultado[4..6], formatado[9..]);
            formatado[13] = '-';
            EscreverHexadecimais(resultado[6..8], formatado[14..]);
            formatado[18] = '-';
            EscreverHexadecimais(resultado[8..10], formatado[19..]);
            formatado[23] = '-';
            EscreverHexadecimais(resultado[10..BytesDoUuid], formatado[24..]);
            return string.Concat(formatado, preenchimento);
        }
        finally
        {
            hexadecimais.Clear();
            CryptographicOperations.ZeroMemory(mensagem);
            CryptographicOperations.ZeroMemory(resultado);
        }
    }

    private static void EscreverHexadecimais(ReadOnlySpan<byte> bytes, Span<char> destino)
    {
        if (!Convert.TryToHexStringLower(bytes, destino, out _))
        {
            throw new UnreachableException("O destino tem o tamanho dos hexadecimais.");
        }
    }
}
