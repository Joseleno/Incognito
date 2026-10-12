namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// Permutação de cadeias de dígitos e letras maiúsculas particionada pela presença de letra: só com dígitos, a cadeia
/// é permutada em base 10; com ao menos uma letra, em base 36 (<c>0-9</c> → 0 a 9, <c>A-Z</c> → 10 a 35, alfabeto
/// completo), evitando as cadeias só com dígitos. As duas partes juntas são uma bijeção sobre as 36ⁿ cadeias, e a
/// presença de letra nunca muda: uma cadeia numérica não colide com uma alfanumérica.
/// </summary>
/// <remarks>
/// <para>
/// A codificação em base 36 serve só para permutar. O valor ASCII − 48, usado no DV do CNPJ, não entra aqui: com ele,
/// <c>0000000Z</c> e <c>00000016</c> valeriam ambos 42.
/// </para>
/// <para>
/// As cadeias que começam com um prefixo de zeros dado (a raiz zero do CNPJ, com qualquer ordem) ficam fora das duas
/// partes: voltam como vieram (ponto fixo) e nunca são produzidas. As duas permutações têm radix, a e b diferentes,
/// que entram na função de rodada, então podem usar a mesma chave de domínio. Thread-safe; o descarte é do dono,
/// depois de todo uso.
/// </para>
/// <para>
/// Sob pedido, a parte só com dígitos também evita as cadeias que começam com um prefixo menor de zeros (a raiz
/// <c>000xxxxx</c> do CNPJ na chave de CT-e e MDF-e, que seria lida como CPF): o conjunto excluído cresce e contém o
/// fixo, então a saída difere da permutação sem o pedido só quando ela cairia nesse prefixo. Uma entrada com o prefixo
/// volta como veio. A parte com letra não muda.
/// </para>
/// </remarks>
internal sealed class PermutacaoParticionada : IDisposable
{
    private const int TamanhoMinimo = 2;

    // Cada metade da parte alfanumérica precisa caber num lado da permutação Feistel (até 2³² − 1): 36⁶ cabe, 36⁷ não.
    private const int TamanhoMaximo = 12;

    private readonly int _tamanho;
    private readonly ulong _limiteNumericoFixo;
    private readonly ulong _limiteAlfanumericoFixo;
    private readonly ulong _limiteNumericoExcluido;
    private readonly ConjuntoExcluido _excluidosNumericos;
    private readonly ConjuntoExcluido? _excluidosNumericosComZeros;
    private readonly ConjuntoExcluido _excluidosAlfanumericos;
    private readonly PermutacaoFeistel _numerica;
    private readonly PermutacaoFeistel _alfanumerica;

    /// <summary>Prepara a permutação de cadeias de um tamanho.</summary>
    /// <param name="chaveNumerica">Chave de domínio da parte só com dígitos.</param>
    /// <param name="chaveAlfanumerica">Chave de domínio da parte com letra.</param>
    /// <param name="tamanho">Tamanho das cadeias, de 2 a 12.</param>
    /// <param name="zerosFixos">
    /// Quantos zeros no início fazem a cadeia ficar fixa, de 0 (nenhuma) ao tamanho (só a cadeia toda de zeros).
    /// </param>
    /// <param name="zerosExcluidos">
    /// Quantos zeros no início excluem a cadeia só com dígitos da saída quando pedido: 0 se a permutação nunca exclui;
    /// senão, de 1 a <paramref name="zerosFixos"/> (ou ao tamanho, sem zeros fixos), para o conjunto conter o dos fixos.
    /// </param>
    internal PermutacaoParticionada(
        ReadOnlySpan<byte> chaveNumerica, ReadOnlySpan<byte> chaveAlfanumerica, int tamanho, int zerosFixos, int zerosExcluidos = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanho, TamanhoMinimo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tamanho, TamanhoMaximo);
        ArgumentOutOfRangeException.ThrowIfNegative(zerosFixos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zerosFixos, tamanho);
        ArgumentOutOfRangeException.ThrowIfNegative(zerosExcluidos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zerosExcluidos, zerosFixos == 0 ? tamanho : zerosFixos);

        // Com o prefixo de zeros, o valor da cadeia é menor que radix^(tamanho − zeros); sem prefixo, nada é fixo.
        _tamanho = tamanho;
        _limiteNumericoFixo = zerosFixos == 0 ? 0 : Potencia(10, tamanho - zerosFixos);
        _limiteAlfanumericoFixo = zerosFixos == 0 ? 0 : Potencia(36, tamanho - zerosFixos);
        var limiteNumerico = _limiteNumericoFixo;
        var limiteAlfanumerico = _limiteAlfanumericoFixo;
        _excluidosNumericos = zerosFixos == 0
            ? ConjuntoExcluido.Vazio
            : ConjuntoExcluido.PorRegra(valor => valor < limiteNumerico, limiteNumerico);

        // Com menos zeros, o conjunto contém o dos fixos; no máximo um décimo do domínio.
        var limiteExcluido = zerosExcluidos == 0 ? 0 : Potencia(10, tamanho - zerosExcluidos);
        _limiteNumericoExcluido = limiteExcluido;
        _excluidosNumericosComZeros = zerosExcluidos == 0
            ? null
            : ConjuntoExcluido.PorRegra(valor => valor < limiteExcluido, limiteExcluido);

        // As cadeias fixas só com dígitos já estão entre as só com dígitos: somam-se só as que têm letra.
        _excluidosAlfanumericos = ConjuntoExcluido.PorRegra(
            valor => valor < limiteAlfanumerico || SoDigitosEmBase36(valor, tamanho),
            Potencia(10, tamanho) + limiteAlfanumerico - limiteNumerico);

        var maior = (tamanho + 1) / 2;
        var menor = tamanho / 2;
        _numerica = new PermutacaoFeistel(chaveNumerica, 10, Potencia(10, maior), Potencia(10, menor));
        try
        {
            _alfanumerica = new PermutacaoFeistel(chaveAlfanumerica, 36, Potencia(36, maior), Potencia(36, menor));
        }
        catch
        {
            _numerica.Dispose();
            throw;
        }
    }

    /// <summary>Permuta uma cadeia; entrada e saída podem ser o mesmo trecho de memória.</summary>
    /// <param name="entrada">A cadeia, só com dígitos e letras maiúsculas ASCII, com o tamanho da permutação.</param>
    /// <param name="saida">Onde escrever a cadeia permutada, com o mesmo tamanho.</param>
    /// <returns>
    /// <see langword="true"/> se a cadeia começa com o prefixo de zeros fixo e voltou como veio (ponto fixo);
    /// <see langword="false"/> se foi permutada.
    /// </returns>
    /// <exception cref="ArgumentException">Tamanho diferente ou caractere fora de <c>0-9</c> e <c>A-Z</c>; a mensagem não traz o valor.</exception>
    internal bool Permutar(ReadOnlySpan<char> entrada, Span<char> saida) =>
        Aplicar(entrada, saida, excluirZeros: false, inversa: false);

    /// <summary>
    /// Como <see cref="Permutar(ReadOnlySpan{char}, Span{char})"/>, evitando também, na parte só com dígitos, as
    /// cadeias com o prefixo de zeros excluídos; uma entrada com o prefixo volta como veio.
    /// </summary>
    /// <param name="entrada">A cadeia, só com dígitos e letras maiúsculas ASCII, com o tamanho da permutação.</param>
    /// <param name="saida">Onde escrever a cadeia permutada, com o mesmo tamanho.</param>
    /// <param name="excluirZeros">Se a parte só com dígitos evita o prefixo de zeros excluídos.</param>
    /// <returns><see langword="true"/> se a cadeia voltou como veio (ponto fixo).</returns>
    /// <exception cref="ArgumentException">Tamanho diferente ou caractere fora de <c>0-9</c> e <c>A-Z</c>; a mensagem não traz o valor.</exception>
    /// <exception cref="InvalidOperationException">Exclusão pedida a uma permutação criada sem zeros excluídos.</exception>
    internal bool Permutar(ReadOnlySpan<char> entrada, Span<char> saida, bool excluirZeros) =>
        Aplicar(entrada, saida, excluirZeros, inversa: false);

    /// <summary>Desfaz <see cref="Permutar(ReadOnlySpan{char}, Span{char}, bool)"/>. Uso interno e de testes.</summary>
    /// <param name="entrada">A cadeia permutada.</param>
    /// <param name="saida">Onde escrever a cadeia original, com o mesmo tamanho.</param>
    /// <param name="excluirZeros">O mesmo pedido usado ao permutar.</param>
    /// <returns><see langword="true"/> se a cadeia era ponto fixo.</returns>
    /// <exception cref="ArgumentException">Tamanho diferente ou caractere fora de <c>0-9</c> e <c>A-Z</c>; a mensagem não traz o valor.</exception>
    /// <exception cref="InvalidOperationException">Exclusão pedida a uma permutação criada sem zeros excluídos.</exception>
    internal bool Inverter(ReadOnlySpan<char> entrada, Span<char> saida, bool excluirZeros) =>
        Aplicar(entrada, saida, excluirZeros, inversa: true);

    /// <summary>Descarta as duas permutações e as cópias das chaves.</summary>
    public void Dispose()
    {
        try
        {
            _numerica.Dispose();
        }
        finally
        {
            _alfanumerica.Dispose();
        }
    }

    // A saída de uma cadeia não fixa nunca cai abaixo do limite, então a mesma conferência vale nos dois sentidos.
    private bool Aplicar(ReadOnlySpan<char> entrada, Span<char> saida, bool excluirZeros, bool inversa)
    {
        if (excluirZeros && _excluidosNumericosComZeros is null)
        {
            throw new InvalidOperationException("A permutação foi criada sem zeros excluídos.");
        }

        if (entrada.Length != _tamanho)
        {
            throw new ArgumentException("A cadeia não tem o tamanho da permutação.", nameof(entrada));
        }

        if (saida.Length != _tamanho)
        {
            throw new ArgumentException("O destino não tem o tamanho da permutação.", nameof(saida));
        }

        var temLetra = TemLetra(entrada);
        var radix = temLetra ? 36u : 10u;
        var valor = Ler(entrada, radix);
        var limite = temLetra ? _limiteAlfanumericoFixo : excluirZeros ? _limiteNumericoExcluido : _limiteNumericoFixo;
        if (valor < limite)
        {
            entrada.CopyTo(saida);
            return true;
        }

        var (permutacao, excluidos) = temLetra
            ? (_alfanumerica, _excluidosAlfanumericos)
            : (_numerica, excluirZeros ? _excluidosNumericosComZeros! : _excluidosNumericos);
        var permutado = inversa ? permutacao.Inverter(valor, [], excluidos) : permutacao.Permutar(valor, [], excluidos);
        Escrever(permutado, radix, saida);
        return false;
    }

    private static bool TemLetra(ReadOnlySpan<char> entrada)
    {
        var temLetra = false;
        foreach (var caractere in entrada)
        {
            if (char.IsAsciiLetterUpper(caractere))
            {
                temLetra = true;
            }
            else if (!char.IsAsciiDigit(caractere))
            {
                throw new ArgumentException("A cadeia tem caractere fora de 0-9 e A-Z.", nameof(entrada));
            }
        }

        return temLetra;
    }

    private static ulong Ler(ReadOnlySpan<char> cadeia, uint radix)
    {
        ulong valor = 0;
        foreach (var caractere in cadeia)
        {
            var simbolo = char.IsAsciiDigit(caractere) ? caractere - '0' : caractere - 'A' + 10;
            valor = (valor * radix) + (ulong)simbolo;
        }

        return valor;
    }

    private static void Escrever(ulong valor, uint radix, Span<char> destino)
    {
        for (var i = destino.Length - 1; i >= 0; i--)
        {
            var simbolo = (int)(valor % radix);
            destino[i] = (char)(simbolo < 10 ? '0' + simbolo : 'A' + simbolo - 10);
            valor /= radix;
        }
    }

    private static bool SoDigitosEmBase36(ulong valor, int tamanho)
    {
        for (var i = 0; i < tamanho; i++)
        {
            if (valor % 36 >= 10)
            {
                return false;
            }

            valor /= 36;
        }

        return true;
    }

    private static ulong Potencia(ulong baseNumerica, int expoente)
    {
        ulong resultado = 1;
        for (var i = 0; i < expoente; i++)
        {
            resultado *= baseNumerica;
        }

        return resultado;
    }
}
