using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de telefone brasileiro: o DDD e a classe ficam, e o número é permutado dentro da classe, com o DDD
/// como ajuste. Celular (<c>9</c> e mais 8 dígitos) usa a variante <c>cel</c> sobre os 8 dígitos depois do 9
/// (10⁴ × 10⁴); celular antigo (8 dígitos, o primeiro de 6 a 9, anterior ao nono dígito) usa a mesma chave sobre
/// (primeiro − 6)·10⁷ mais os outros 7 (4000 × 10⁴, separado do celular atual por a e b); fixo (8 dígitos, o primeiro
/// de 2 a 5) usa a variante <c>fixo</c> (4000 × 10⁴), sem produzir número de serviço. Mesma chave, domínio, versão e
/// ambiente dão o mesmo pseudônimo, e números diferentes da mesma classe e do mesmo DDD nunca colidem.
/// </summary>
/// <remarks>
/// <para>
/// Formas: DDD e número, opcionalmente com o código do país <c>55</c> ou o <c>0</c> de tronco à frente (os dois ficam
/// e não entram no ajuste), ou só o número, permutado sem ajuste (o mesmo número com e sem DDD dá pseudônimos
/// diferentes). O DDD tem dois dígitos de 1 a 9. A formatação é preservada, só os dígitos trocam, mas tem de ser a de
/// um telefone, para nenhum dígito do número ser lido como DDD: parênteses só em volta do DDD (com o 0 de tronco, se
/// houver), nenhum separador dentro do 55 nem do 0 de tronco com o DDD, o número colado ou em grupos de 4 e 4, 5 e 4
/// ou 1, 4 e 4 (com espaço, hífen ou ponto), o <c>+</c> só no início e só com o 55, até 32 caracteres. Ainda assim,
/// <c>5512-3456 1234</c> é lido como 55, DDD 12 e número: a forma é a mesma de <c>55 12 3456-1234</c>.
/// </para>
/// <para>
/// Números de serviço (0800, 0300, 0500 e 0900 com 11 dígitos, com o 55 opcional; fixos que começam com 3003 ou 4004)
/// não são de pessoa e voltam como vieram (<see cref="Desfecho.ServicoPreservado"/>). Espaços à direita voltam como
/// estavam; texto vazio ou só com espaços volta como veio. Em coluna numérica valem as mesmas formas sem formatação e
/// sem o 0 de tronco; um número de 10 dígitos que começa com 800, 300, 500 ou 900 é um 0800 e afins que perdeu o
/// zero. O resto segue <c>entradaMalformada</c>; telefone não tem DV. Nenhuma exceção traz o valor.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorTelefone : IDisposable
{
    private const int MaximoDeDigitos = 13;

    // O formato mais longo, +55 (00) 00000-0000, tem 19 caracteres; o limite também protege o stackalloc.
    private const int MaximoDeCaracteres = 32;
    private const ulong InicioDo3003 = 10_030_000;
    private const ulong InicioDo4004 = 20_040_000;
    private const ulong FaixaDeServico = 10_000;
    private const string MensagemTextoMalformado =
        "Valor fora da forma de telefone (DDD e número, opcionalmente com 55 ou 0 à frente, ou só o número; parênteses só em volta do DDD; número colado ou em grupos de 4 e 4, 5 e 4 ou 1, 4 e 4; + só no início e com o 55; até 32 caracteres); a política entradaMalformada é falhar. O valor não é exibido.";
    private const string MensagemNumeroMalformado =
        "Número fora da forma de telefone; a política entradaMalformada é falhar. O valor não é exibido.";

    // Fixos que começam com 3003 ou 4004, no valor do domínio do fixo ((primeiro dígito − 2)·10⁷ + os outros 7).
    private static readonly ConjuntoExcluido _servicos = ConjuntoExcluido.PorRegra(EhServico, 2 * FaixaDeServico);

    private readonly PermutacaoFeistel _celular;
    private readonly PermutacaoFeistel _celularAntigo;
    private readonly PermutacaoFeistel _fixo;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de telefone.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (por padrão, <c>telefone</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="malformada">Política para valor fora da forma de telefone.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal PseudonimizadorTelefone(ChaveIncognito chave, string dominio, int versao, string ambiente, PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, a permutação criada ficaria sem descarte.
        Politicas.Validar(malformada, nameof(malformada));

        Span<byte> chaves = stackalloc byte[2 * Subchaves.Tamanho];
        var deCelular = chaves[..Subchaves.Tamanho];
        var deFixo = chaves[Subchaves.Tamanho..];
        try
        {
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Celular, ambiente, deCelular);
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.Fixo, ambiente, deFixo);
            _celular = new PermutacaoFeistel(deCelular, 10, 10_000, 10_000);
            try
            {
                _celularAntigo = new PermutacaoFeistel(deCelular, 10, 4_000, 10_000);
                try
                {
                    _fixo = new PermutacaoFeistel(deFixo, 10, 4_000, 10_000);
                }
                catch
                {
                    _celularAntigo.Dispose();
                    throw;
                }
            }
            catch
            {
                _celular.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaves);
        }

        _malformada = malformada;
    }

    private enum Classe
    {
        Celular,
        CelularAntigo,
        Fixo,

        // Fixo com prefixo 3003 ou 4004.
        ServicoFixo,

        // 0800, 0300, 0500 ou 0900 com 11 dígitos.
        ServicoNaoGeografico,
    }

    /// <summary>Política para valor fora da forma, conferida por quem compõe este pseudonimizador (a chave Pix).</summary>
    internal PoliticaEntradaMalformada Malformada => _malformada;

    /// <summary>Pseudonimiza um telefone em texto.</summary>
    /// <param name="entrada">O telefone como veio da coluna.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro, vazia ou de serviço.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
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

        Span<char> digitos = stackalloc char[MaximoDeDigitos];
        Span<int> posicoes = stackalloc int[MaximoDeDigitos];
        var quantidade = corpo.Length <= MaximoDeCaracteres ? SepararDigitos(corpo, digitos, posicoes) : -1;
        if (quantidade < 0 || !Classificar(digitos[..quantidade], out var classe, out var prefixo, out var comDdd))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        // O 0800 e afins volta como veio com qualquer formatação; o resto precisa ter a forma de um telefone.
        var formaValida = classe == Classe.ServicoNaoGeografico
            ? corpo[0] != '+' || prefixo == 2
            : TemFormaDeTelefone(corpo, posicoes[..quantidade], prefixo, comDdd);
        if (!formaValida)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        if (classe is Classe.ServicoFixo or Classe.ServicoNaoGeografico)
        {
            saida = entrada;
            return Desfecho.ServicoPreservado;
        }

        Permutar(digitos[..quantidade], classe, prefixo, comDdd);
        Span<char> formatado = stackalloc char[corpo.Length];
        corpo.CopyTo(formatado);
        for (var i = 0; i < quantidade; i++)
        {
            formatado[posicoes[i]] = digitos[i];
        }

        saida = string.Concat(formatado, preenchimento);
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Pseudonimiza um telefone guardado em coluna numérica.</summary>
    /// <param name="entrada">O telefone como número, sem formatação e sem os zeros à esquerda.</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro ou de serviço.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(long entrada, out long? saida)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        var quantidade = 0;
        for (var restante = entrada; restante > 0; restante /= 10)
        {
            quantidade++;
        }

        if (quantidade is 0 or > MaximoDeDigitos)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemNumeroMalformado, entrada, out saida);
        }

        Span<char> digitos = stackalloc char[quantidade];
        var resto = entrada;
        for (var i = quantidade - 1; i >= 0; i--)
        {
            digitos[i] = (char)('0' + (int)(resto % 10));
            resto /= 10;
        }

        // Um 0800 e afins guardado como número perde o zero; 80, 30, 50 e 90 nunca são DDD aceito.
        if (quantidade == 10 && EhPrefixoNaoGeografico(digitos[..3]))
        {
            saida = entrada;
            return Desfecho.ServicoPreservado;
        }

        if (!Classificar(digitos, out var classe, out var prefixo, out var comDdd))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemNumeroMalformado, entrada, out saida);
        }

        if (classe is Classe.ServicoFixo or Classe.ServicoNaoGeografico)
        {
            saida = entrada;
            return Desfecho.ServicoPreservado;
        }

        Permutar(digitos, classe, prefixo, comDdd);
        long numero = 0;
        foreach (var digito in digitos)
        {
            numero = (numero * 10) + (digito - '0');
        }

        saida = numero;
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Descarta as permutações e as cópias das chaves de domínio.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        try
        {
            _celular.Dispose();
        }
        finally
        {
            try
            {
                _celularAntigo.Dispose();
            }
            finally
            {
                _fixo.Dispose();
            }
        }
    }

    // Separa os dígitos e as posições deles; devolve −1 com caractere fora dos separadores, + fora do início ou mais
    // de 13 dígitos.
    private static int SepararDigitos(ReadOnlySpan<char> corpo, Span<char> digitos, Span<int> posicoes)
    {
        var quantidade = 0;
        for (var i = 0; i < corpo.Length; i++)
        {
            var caractere = corpo[i];
            if (char.IsAsciiDigit(caractere))
            {
                if (quantidade == MaximoDeDigitos)
                {
                    return -1;
                }

                digitos[quantidade] = caractere;
                posicoes[quantidade++] = i;
            }
            else if (caractere is not (' ' or '(' or ')' or '-' or '.') && !(caractere == '+' && i == 0))
            {
                return -1;
            }
        }

        return quantidade;
    }

    // Lê as partes pela quantidade de dígitos. O prefixo é 2 com o 55 do país, 1 com o 0 de tronco e 0 sem nenhum; o
    // DDD, se houver, vem logo depois do prefixo, e o número depois do DDD.
    private static bool Classificar(ReadOnlySpan<char> digitos, out Classe classe, out int prefixo, out bool comDdd)
    {
        classe = Classe.Fixo;
        comDdd = false;
        var quantidade = digitos.Length;
        prefixo = quantidade is 12 or 13 && digitos[0] == '5' && digitos[1] == '5' ? 2
            : quantidade is 11 or 12 && digitos[0] == '0' ? 1
            : 0;

        // 0800 e afins, com o 55 opcional: não se confunde com o 0 de tronco, porque o DDD nunca tem zero.
        var semPais = prefixo == 2 ? digitos[2..] : digitos;
        if (semPais.Length == 11 && semPais[0] == '0' && EhPrefixoNaoGeografico(semPais[1..4]))
        {
            classe = Classe.ServicoNaoGeografico;
            prefixo = quantidade - 11;
            return true;
        }

        var resto = quantidade - prefixo;
        if (resto is 10 or 11)
        {
            if (digitos[prefixo] is < '1' or > '9' || digitos[prefixo + 1] is < '1' or > '9')
            {
                return false;
            }

            comDdd = true;
        }
        else if (resto is not (8 or 9))
        {
            return false;
        }

        var numero = digitos[(prefixo + (comDdd ? 2 : 0))..];
        if (numero.Length == 9)
        {
            classe = Classe.Celular;
            return numero[0] == '9';
        }

        switch (numero[0])
        {
            case >= '2' and <= '5':
                classe = EhServico(ValorDoFixo(numero)) ? Classe.ServicoFixo : Classe.Fixo;
                return true;
            case >= '6' and <= '9':
                classe = Classe.CelularAntigo;
                return true;
            default:
                return false;
        }
    }

    // A formatação tem de ser a de um telefone: sem isso, um ramal ou um grupo fora do lugar faria dígitos do número
    // serem lidos como 55 ou DDD (e saírem em claro). Entre o país e o DDD e entre o DDD e o número, os separadores são
    // livres; dentro das partes, não.
    private static bool TemFormaDeTelefone(ReadOnlySpan<char> corpo, ReadOnlySpan<int> posicoes, int prefixo, bool comDdd)
    {
        var quantidade = posicoes.Length;
        var inicioDoNumero = prefixo + (comDdd ? 2 : 0);
        var tamanhoDoNumero = quantidade - inicioDoNumero;

        // Antes do primeiro dígito só o + (do 55) e o ( (do DDD); depois do último, nada.
        foreach (var caractere in corpo[..posicoes[0]])
        {
            if (caractere is not ('+' or '('))
            {
                return false;
            }
        }

        if (posicoes[^1] != corpo.Length - 1 || (corpo[0] == '+' && prefixo != 2))
        {
            return false;
        }

        // Parênteses: um par, o ( logo antes do DDD (ou do 0 de tronco) e o ) logo depois dele.
        var abre = corpo.IndexOf('(');
        var fecha = corpo.IndexOf(')');
        if (abre >= 0 || fecha >= 0)
        {
            var inicioDoDdd = prefixo == 1 ? 0 : prefixo;
            if (!comDdd
                || abre < 0
                || fecha < 0
                || corpo.LastIndexOf('(') != abre
                || corpo.LastIndexOf(')') != fecha
                || abre != posicoes[inicioDoDdd] - 1
                || fecha != posicoes[inicioDoNumero - 1] + 1)
            {
                return false;
            }
        }

        var separadoAntesDosUltimos4 = posicoes[quantidade - 4] != posicoes[quantidade - 5] + 1;
        for (var i = 1; i < quantidade; i++)
        {
            var separador = corpo[(posicoes[i - 1] + 1)..posicoes[i]];
            if (separador.IsEmpty || (i == 2 && prefixo == 2) || (i == inicioDoNumero && comDdd))
            {
                continue;
            }

            // Dentro do número, um só separador (os parênteses já estão no lugar), antes dos últimos 4 dígitos ou depois
            // do primeiro do celular de 9 (este só com o outro também presente). Dentro do 55, do 0 de tronco com o DDD e
            // do DDD, nenhum: essas posições nunca são as do número.
            var antesDosUltimos4 = i == quantidade - 4;
            var depoisDoPrimeiro = tamanhoDoNumero == 9 && i == inicioDoNumero + 1 && separadoAntesDosUltimos4;
            if (separador.Length != 1 || !(antesDosUltimos4 || depoisDoPrimeiro))
            {
                return false;
            }
        }

        return true;
    }

    // Troca os dígitos do número no lugar; o prefixo e o DDD ficam.
    private void Permutar(Span<char> digitos, Classe classe, int prefixo, bool comDdd)
    {
        Span<byte> ddd = [0, 0];
        if (comDdd)
        {
            ddd[0] = (byte)digitos[prefixo];
            ddd[1] = (byte)digitos[prefixo + 1];
        }

        ReadOnlySpan<byte> ajuste = comDdd ? ddd : [];
        var numero = digitos[(prefixo + (comDdd ? 2 : 0))..];
        switch (classe)
        {
            case Classe.Celular:
                var depoisDo9 = numero[1..];
                Escrever(_celular.Permutar(Ler(depoisDo9), ajuste), depoisDo9);
                break;
            case Classe.CelularAntigo:
                EscreverComPrimeiro(_celularAntigo.Permutar(((ulong)(numero[0] - '6') * 10_000_000) + Ler(numero[1..]), ajuste), '6', numero);
                break;
            default:
                EscreverComPrimeiro(_fixo.Permutar(ValorDoFixo(numero), ajuste, _servicos), '2', numero);
                break;
        }
    }

    private static bool EhPrefixoNaoGeografico(ReadOnlySpan<char> tresDigitos) =>
        tresDigitos is "800" or "300" or "500" or "900";

    // Subtração sem sinal: um valor abaixo do início da faixa dá a volta e fica enorme, fora dela.
    private static bool EhServico(ulong valorDoFixo) =>
        valorDoFixo - InicioDo3003 < FaixaDeServico || valorDoFixo - InicioDo4004 < FaixaDeServico;

    private static ulong ValorDoFixo(ReadOnlySpan<char> numero) => ((ulong)(numero[0] - '2') * 10_000_000) + Ler(numero[1..]);

    // O primeiro dígito volta a partir da base (2 no fixo, 6 no celular antigo) e os outros 7 vêm da permutação.
    private static void EscreverComPrimeiro(ulong valor, char basePrimeiro, Span<char> numero)
    {
        numero[0] = (char)(basePrimeiro + (int)(valor / 10_000_000));
        Escrever(valor % 10_000_000, numero[1..]);
    }

    private static ulong Ler(ReadOnlySpan<char> digitos)
    {
        ulong valor = 0;
        foreach (var digito in digitos)
        {
            valor = (valor * 10) + (ulong)(digito - '0');
        }

        return valor;
    }

    private static void Escrever(ulong valor, Span<char> destino)
    {
        for (var i = destino.Length - 1; i >= 0; i--)
        {
            destino[i] = (char)('0' + (int)(valor % 10));
            valor /= 10;
        }
    }
}
