using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito;

/// <summary>
/// Geração de documentos brasileiros válidos para dados de teste: CPF, CNPJ (numérico ou alfanumérico) e chave de acesso
/// de DF-e, que passam no <see cref="ValidadorBr"/>. Não é criptográfico e não pseudonimiza nada.
/// </summary>
/// <remarks>
/// <para>
/// Válido não quer dizer fictício: um documento gerado pode pertencer a alguém, porque nenhum órgão reserva faixa para
/// testes. Não use o que for gerado para consultar cadastros de terceiros nem para enviar mensagens.
/// </para>
/// <para>
/// <see cref="GeradorBr(long)"/> é determinístico: a mesma semente dá a mesma sequência dentro da mesma versão maior
/// do pacote, a partir da 1.0.0 (antes dela, a sequência pode mudar entre versões candidatas), em qualquer plataforma. O sorteio é o xoshiro256** com o estado expandido da semente pelo SplitMix64, e
/// os inteiros saem sem viés pelo método de Lemire. <see cref="Padrao"/> sorteia de uma sequência por thread, semeada
/// pelo <see cref="RandomNumberGenerator"/>: não é reproduzível, e cada execução dá outra sequência.
/// </para>
/// <para>
/// CPF: base de 9 dígitos sem os 10 repdígitos. CNPJ: ordem <c>0001</c> (a matriz); raiz numérica sem
/// <c>00000000</c>; raiz alfanumérica com dígitos e letras maiúsculas e ao menos uma letra. Chave de acesso de DF-e:
/// emissão normal com emitente CNPJ de matriz (ver <see cref="ChaveDfe(int, bool)"/>). Cada dígito ou caractere é um
/// sorteio; uma base recusada é sorteada inteira de novo; <c>formatado</c> só muda a apresentação.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo; a sequência só é determinística com uma. Prefira argumentos
/// nomeados (<c>Cnpj(alfanumerico: true)</c>): uma opção nova entra por sobrecarga nova.
/// </para>
/// </remarks>
public sealed class GeradorBr
{
    /// <summary>O alfabeto do CNPJ alfanumérico, na ordem dos sorteios.</summary>
    internal const string Alfanumericos = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string CaracteresDoEmail = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string DominioReservado = "@example.org";
    private const int TamanhoDaParteLocal = 12;
    private const int UltimoAammDaChave = 2609;
    private const int PrimeiroAammDaChave = 2001;
    private const int PrimeiroAammDoCnpjAlfanumerico = 2607;
    private const string MensagemModeloDesconhecido = "Modelo de DF-e sem chave de 44 posições; use 55, 57, 58, 62, 63, 64, 65, 66 ou 67.";

    // Os 27 códigos de UF do IBGE, as duas primeiras posições da chave. A ordem e o conteúdo fazem parte da sequência:
    // mudar a tabela muda as chaves geradas por semente, só numa versão maior, com registro no CHANGELOG.
    private static readonly byte[] _codigosDeUf =
    [
        11, 12, 13, 14, 15, 16, 17,
        21, 22, 23, 24, 25, 26, 27, 28, 29,
        31, 32, 33, 35,
        41, 42, 43,
        50, 51, 52, 53,
    ];

    // Os 67 DDDs em uso no plano de numeração da Anatel. A ordem e o conteúdo fazem parte da sequência: mudar a tabela
    // muda os telefones gerados por semente e precisa de registro no CHANGELOG.
    private static readonly byte[] _ddds =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 24, 27, 28,
        31, 32, 33, 34, 35, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48, 49,
        51, 53, 54, 55,
        61, 62, 63, 64, 65, 66, 67, 68, 69,
        71, 73, 74, 75, 77, 79,
        81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 92, 93, 94, 95, 96, 97, 98, 99,
    ];

    [ThreadStatic]
    private static SequenciaXoshiro? _daThread;

    private readonly FonteDeSorteio? _fonte;
    private readonly Lock _trava = new();

    /// <summary>Cria um gerador determinístico.</summary>
    /// <param name="semente">
    /// A semente; a mesma semente dá a mesma sequência dentro da mesma versão maior, a partir da 1.0.0.
    /// </param>
    public GeradorBr(long semente)
        : this(SequenciaXoshiro.DaSemente(semente))
    {
    }

    /// <summary>Cria um gerador sobre uma fonte dada.</summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    internal GeradorBr(FonteDeSorteio fonte)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        _fonte = fonte;
    }

    // O Padrao: sem fonte própria, cada thread sorteia da sua, semeada pelo RandomNumberGenerator.
    private GeradorBr()
    {
    }

    /// <summary>Gerador compartilhado, com uma sequência aleatória por thread; não é reproduzível.</summary>
    public static GeradorBr Padrao { get; } = new();

    /// <summary>Gera um CPF válido.</summary>
    /// <param name="formatado">Com a máscara <c>000.000.000-00</c>.</param>
    /// <returns>O CPF, que passa no <see cref="ValidadorBr.Cpf(string?)"/>.</returns>
    public string Cpf(bool formatado = false) =>
        Sortear(formatado, static (fonte, formatado) => GerarCpf(fonte, formatado));

    /// <summary>Gera um CNPJ válido de matriz (ordem <c>0001</c>).</summary>
    /// <param name="alfanumerico">Com raiz alfanumérica (dígitos e letras maiúsculas, ao menos uma letra).</param>
    /// <param name="formatado">Com a máscara <c>00.000.000/0000-00</c>.</param>
    /// <returns>O CNPJ, que passa no <see cref="ValidadorBr.Cnpj(string?)"/>.</returns>
    public string Cnpj(bool alfanumerico = false, bool formatado = false) =>
        Sortear((alfanumerico, formatado), static (fonte, opcoes) => GerarCnpj(fonte, opcoes.alfanumerico, opcoes.formatado));

    /// <summary>
    /// Gera uma chave de acesso de DF-e de 44 posições que passa no <see cref="ValidadorBr.ChaveDfe(string?)"/>: emissão
    /// normal (tpEmis <c>1</c>), emitente CNPJ de matriz, série de 000 a 889, número de 1 a 999.999.999 e código numérico
    /// fora dos 20 proibidos e diferente do número. O emitente CPF não é gerado nesta versão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sorteios, nesta ordem, que fazem parte da sequência: cUF (um, entre os 27 códigos do IBGE, sem coerência com o
    /// emitente); AAMM (um, o índice do mês numa janela fixa, porque a sequência não depende do relógio: de 01/2020 a
    /// 09/2026, GTV-e a partir de 09/2020, NFCom a partir de 01/2022 e, com CNPJ alfanumérico, de 07/2026 a 09/2026);
    /// emitente (os do <see cref="Cnpj(bool, bool)"/>, sorteado de novo no CT-e e no MDF-e se a raiz numérica começar
    /// com <c>000</c>, que a chave lê como CPF); série (um, de 000 a 889); número (um, de 1 a 999.999.999); código
    /// numérico (um sorteio por dígito, os 8 de novo se proibido ou igual ao número; na NF3e e na NFCom,
    /// <c>nSiteAutoriz</c> 0 e 7 dígitos). Mudar a ordem, a tabela de UF ou a janela muda a sequência: só numa versão
    /// maior, com registro no CHANGELOG; dentro dela, outra janela entra por sobrecarga nova.
    /// </para>
    /// <para>
    /// Válida não quer dizer fictícia: é improvável que a chave corresponda a um documento autorizado, mas o emitente pode
    /// ser um CNPJ real (as raízes baixas são densas). Não consulte a chave nos portais das SEFAZ.
    /// </para>
    /// </remarks>
    /// <param name="modelo">O código do modelo: 55 (NF-e), 57 (CT-e), 58 (MDF-e), 62 (NFCom), 63 (BP-e), 64 (GTV-e), 65 (NFC-e), 66 (NF3e) ou 67 (CT-e OS).</param>
    /// <param name="alfanumerico">Com o CNPJ do emitente de raiz alfanumérica.</param>
    /// <returns>As 44 posições, sem prefixo nem separadores.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Modelo fora dos nove; nada é sorteado.</exception>
    public string ChaveDfe(int modelo, bool alfanumerico = false)
    {
        if (modelo is < 0 or > byte.MaxValue || !Enum.IsDefined((ModeloDfe)modelo))
        {
            throw new ArgumentOutOfRangeException(nameof(modelo), MensagemModeloDesconhecido);
        }

        return Sortear((modelo: (ModeloDfe)modelo, alfanumerico), static (fonte, opcoes) => GerarChaveDfe(fonte, opcoes.modelo, opcoes.alfanumerico));
    }

    /// <summary>
    /// Gera um telefone com DDD em uso: celular (<c>9</c> e mais 8 dígitos) ou fixo (8 dígitos, o primeiro de 2 a 5,
    /// sem os prefixos de serviço 3003 e 4004). Sorteios: o DDD (um, entre os 67) e os dígitos do número; um fixo com
    /// prefixo de serviço sorteia o número inteiro de novo, sem o DDD.
    /// </summary>
    /// <param name="celular">Celular; senão, fixo.</param>
    /// <param name="comPais">Com <c>+55</c> à frente.</param>
    /// <param name="formatado">Com o DDD entre parênteses e hífen antes dos últimos 4 dígitos.</param>
    /// <returns>O telefone.</returns>
    internal string Telefone(bool celular, bool comPais = false, bool formatado = false) =>
        Sortear((celular, comPais, formatado), static (fonte, opcoes) => GerarTelefone(fonte, opcoes.celular, opcoes.comPais, opcoes.formatado));

    /// <summary>
    /// Gera um CEP nas faixas de saída de uma UF. Sorteios: a UF (um, se não for dada) e a posição do CEP nas faixas
    /// dela (um). As faixas fazem parte da sequência.
    /// </summary>
    /// <param name="uf">A sigla da UF, em maiúsculas; nula sorteia uma das 27 com a mesma chance.</param>
    /// <param name="formatado">Com a máscara <c>00000-000</c>.</param>
    /// <returns>O CEP.</returns>
    /// <exception cref="ArgumentException">Sigla de UF desconhecida.</exception>
    internal string Cep(string? uf = null, bool formatado = false)
    {
        var indice = -1;
        if (uf is not null)
        {
            indice = FaixasDeCep.Ufs.IndexOf(uf);
            if (indice < 0)
            {
                throw new ArgumentException("Sigla de UF desconhecida.", nameof(uf));
            }
        }

        return Sortear((indice, formatado), static (fonte, opcoes) => GerarCep(fonte, opcoes.indice, opcoes.formatado));
    }

    /// <summary>Gera um e-mail em <c>example.org</c>, reservado pela RFC 2606, com parte local de 12 caracteres.</summary>
    /// <returns>O e-mail.</returns>
    internal string Email() => Sortear(0, static (fonte, _) => GerarEmail(fonte));

    /// <summary>
    /// Gera uma chave Pix na forma do DICT. Sorteios: os do tipo (o CNPJ sorteia antes, num sorteio, se é alfanumérico;
    /// a aleatória usa dois números de 64 bits inteiros).
    /// </summary>
    /// <param name="tipo">O tipo da chave.</param>
    /// <returns>A chave.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Tipo fora dos definidos.</exception>
    internal string ChavePix(TipoChavePix tipo)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de chave Pix desconhecido.");
        }

        return Sortear(tipo, static (fonte, tipo) => GerarChavePix(fonte, tipo));
    }

    private static SequenciaXoshiro NovaDaThread()
    {
        Span<byte> semente = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(semente);
        return SequenciaXoshiro.DaSemente(BinaryPrimitives.ReadInt64LittleEndian(semente));
    }

    /// <summary>Escreve um CPF válido: base sem os 10 repdígitos, sorteada de novo se for um.</summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    /// <param name="cpf">Os 11 caracteres de destino.</param>
    internal static void EscreverCpf(FonteDeSorteio fonte, Span<char> cpf)
    {
        do
        {
            SortearDigitos(fonte, cpf[..9]);
        }
        while (TodosIguais(cpf[..9]));

        EscreverDv(DigitosVerificadores.Cpf(cpf[..9]), cpf[9..]);
    }

    /// <summary>
    /// Escreve um CNPJ válido de matriz (ordem <c>0001</c>): raiz numérica sem <c>00000000</c>, ou alfanumérica com ao
    /// menos uma letra, sorteada de novo se não for.
    /// </summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    /// <param name="alfanumerico">Com raiz alfanumérica.</param>
    /// <param name="cnpj">Os 14 caracteres de destino.</param>
    internal static void EscreverCnpj(FonteDeSorteio fonte, bool alfanumerico, Span<char> cnpj)
    {
        var raiz = cnpj[..8];
        if (alfanumerico)
        {
            do
            {
                for (var i = 0; i < raiz.Length; i++)
                {
                    raiz[i] = Alfanumericos[(int)fonte.Uniforme((ulong)Alfanumericos.Length)];
                }
            }
            while (!TemLetra(raiz));
        }
        else
        {
            do
            {
                SortearDigitos(fonte, raiz);
            }
            while (raiz.SequenceEqual("00000000"));
        }

        "0001".CopyTo(cnpj[8..]);
        EscreverDv(DigitosVerificadores.Cnpj(cnpj[..12]), cnpj[12..]);
    }

    /// <summary>Sorteia dígitos ASCII, um sorteio por dígito.</summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    /// <param name="destino">Onde escrever.</param>
    internal static void SortearDigitos(FonteDeSorteio fonte, Span<char> destino)
    {
        for (var i = 0; i < destino.Length; i++)
        {
            destino[i] = (char)('0' + (int)fonte.Uniforme(10));
        }
    }

    private static string GerarCpf(FonteDeSorteio fonte, bool formatado)
    {
        Span<char> cpf = stackalloc char[11];
        EscreverCpf(fonte, cpf);
        if (!formatado)
        {
            return new string(cpf);
        }

        Span<char> mascara = stackalloc char[14];
        cpf[..3].CopyTo(mascara);
        mascara[3] = '.';
        cpf[3..6].CopyTo(mascara[4..]);
        mascara[7] = '.';
        cpf[6..9].CopyTo(mascara[8..]);
        mascara[11] = '-';
        cpf[9..].CopyTo(mascara[12..]);
        return new string(mascara);
    }

    private static string GerarCnpj(FonteDeSorteio fonte, bool alfanumerico, bool formatado)
    {
        Span<char> cnpj = stackalloc char[14];
        EscreverCnpj(fonte, alfanumerico, cnpj);
        if (!formatado)
        {
            return new string(cnpj);
        }

        Span<char> mascara = stackalloc char[18];
        cnpj[..2].CopyTo(mascara);
        mascara[2] = '.';
        cnpj[2..5].CopyTo(mascara[3..]);
        mascara[6] = '.';
        cnpj[5..8].CopyTo(mascara[7..]);
        mascara[10] = '/';
        cnpj[8..12].CopyTo(mascara[11..]);
        mascara[15] = '-';
        cnpj[12..].CopyTo(mascara[16..]);
        return new string(mascara);
    }

    private static string GerarChaveDfe(FonteDeSorteio fonte, ModeloDfe modelo, bool alfanumerico)
    {
        Span<char> chave = stackalloc char[RegrasChaveDfe.Tamanho];
        EscreverNumero(SortearUf(fonte), chave[..2]);

        var primeiro = alfanumerico ? PrimeiroAammDoCnpjAlfanumerico : modelo switch
        {
            ModeloDfe.Gtve => 2009,
            ModeloDfe.Nfcom => 2201,
            _ => PrimeiroAammDaChave,
        };
        var mes = Meses(primeiro) + (int)fonte.Uniforme((ulong)(Meses(UltimoAammDaChave) - Meses(primeiro) + 1));
        EscreverNumero(((mes / 12) * 100) + (mes % 12) + 1, chave[2..6]);

        var emitente = chave[6..20];
        var transporte = modelo is ModeloDfe.Cte or ModeloDfe.Mdfe;
        do
        {
            EscreverCnpj(fonte, alfanumerico, emitente);
        }
        while (transporte && !alfanumerico && emitente.StartsWith("000"));

        EscreverNumero((int)modelo, chave[20..22]);
        EscreverNumero((int)fonte.Uniforme(890), chave[22..25]);
        var numero = (long)fonte.Uniforme(999_999_999) + 1;
        EscreverNumero(numero, chave[25..34]);
        chave[34] = '1';
        if (modelo is ModeloDfe.Nf3e or ModeloDfe.Nfcom)
        {
            chave[35] = '0';
            SortearDigitos(fonte, chave[36..43]);
        }
        else
        {
            int codigo;
            do
            {
                SortearDigitos(fonte, chave[35..43]);
                codigo = Caracteres.Valor(chave[35..43]);
            }
            while (RegrasChaveDfe.EhCodigoNumericoProibido(codigo) || codigo == numero);
        }

        chave[43] = (char)('0' + DigitosVerificadores.ChaveDfe(chave[..43]));
        return new string(chave);
    }

    /// <summary>Um dos 27 códigos de UF do IBGE, num sorteio.</summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    /// <returns>O código.</returns>
    internal static int SortearUf(FonteDeSorteio fonte) => _codigosDeUf[(int)fonte.Uniforme((ulong)_codigosDeUf.Length)];

    // Meses desde 01/2000, para sortear o AAMM por índice.
    private static int Meses(int aamm) => ((aamm / 100) * 12) + (aamm % 100) - 1;

    /// <summary>Escreve um número com zeros à esquerda, ocupando o destino todo.</summary>
    /// <param name="valor">O número, não negativo.</param>
    /// <param name="destino">Onde escrever.</param>
    internal static void EscreverNumero(long valor, Span<char> destino)
    {
        for (var i = destino.Length - 1; i >= 0; i--)
        {
            destino[i] = (char)('0' + (int)(valor % 10));
            valor /= 10;
        }
    }

    private static string GerarTelefone(FonteDeSorteio fonte, bool celular, bool comPais, bool formatado)
    {
        Span<char> numero = stackalloc char[celular ? 9 : 8];
        var ddd = _ddds[(int)fonte.Uniforme((ulong)_ddds.Length)];
        if (celular)
        {
            numero[0] = '9';
            SortearDigitos(fonte, numero[1..]);
        }
        else
        {
            do
            {
                numero[0] = (char)('2' + (int)fonte.Uniforme(4));
                SortearDigitos(fonte, numero[1..]);
            }
            while (numero.StartsWith("3003") || numero.StartsWith("4004"));
        }

        // O mais longo, +55 (00) 00000-0000, tem 19 caracteres.
        Span<char> telefone = stackalloc char[19];
        var tamanho = 0;
        if (comPais)
        {
            (formatado ? "+55 " : "+55").CopyTo(telefone);
            tamanho = formatado ? 4 : 3;
        }

        if (formatado)
        {
            telefone[tamanho++] = '(';
        }

        telefone[tamanho++] = (char)('0' + (ddd / 10));
        telefone[tamanho++] = (char)('0' + (ddd % 10));
        if (formatado)
        {
            ") ".CopyTo(telefone[tamanho..]);
            tamanho += 2;
            numero[..^4].CopyTo(telefone[tamanho..]);
            tamanho += numero.Length - 4;
            telefone[tamanho++] = '-';
            numero[^4..].CopyTo(telefone[tamanho..]);
            tamanho += 4;
        }
        else
        {
            numero.CopyTo(telefone[tamanho..]);
            tamanho += numero.Length;
        }

        return new string(telefone[..tamanho]);
    }

    private static string GerarCep(FonteDeSorteio fonte, int indiceDaUf, bool formatado)
    {
        var uf = indiceDaUf >= 0 ? indiceDaUf : (int)fonte.Uniforme((ulong)FaixasDeCep.Ufs.Length);
        var cep = FaixasDeCep.CepNaPosicao(uf, fonte.Uniforme(FaixasDeCep.CepsPorUf[uf]));
        Span<char> digitos = stackalloc char[8];
        for (var i = digitos.Length - 1; i >= 0; i--)
        {
            digitos[i] = (char)('0' + (cep % 10));
            cep /= 10;
        }

        if (!formatado)
        {
            return new string(digitos);
        }

        Span<char> mascara = stackalloc char[9];
        digitos[..5].CopyTo(mascara);
        mascara[5] = '-';
        digitos[5..].CopyTo(mascara[6..]);
        return new string(mascara);
    }

    private static string GerarEmail(FonteDeSorteio fonte)
    {
        Span<char> email = stackalloc char[TamanhoDaParteLocal + DominioReservado.Length];
        for (var i = 0; i < TamanhoDaParteLocal; i++)
        {
            email[i] = CaracteresDoEmail[(int)fonte.Uniforme((ulong)CaracteresDoEmail.Length)];
        }

        DominioReservado.CopyTo(email[TamanhoDaParteLocal..]);
        return new string(email);
    }

    private static string GerarChavePix(FonteDeSorteio fonte, TipoChavePix tipo) => tipo switch
    {
        TipoChavePix.Cpf => GerarCpf(fonte, formatado: false),
        TipoChavePix.Cnpj => GerarCnpj(fonte, alfanumerico: fonte.Uniforme(2) == 1, formatado: false),
        TipoChavePix.Email => GerarEmail(fonte),
        TipoChavePix.Telefone => GerarTelefone(fonte, celular: true, comPais: true, formatado: false),
        TipoChavePix.Aleatoria => GerarUuid(fonte),
        _ => throw new UnreachableException("Tipo de chave Pix já conferido."),
    };

    // UUID versão 4 com a variante da RFC 9562, em minúsculas, a partir de dois números de 64 bits em big-endian.
    private static string GerarUuid(FonteDeSorteio fonte)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, fonte.Proximo());
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], fonte.Proximo());
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Span<char> uuid = stackalloc char[36];
        var escritos = 0;
        foreach (var (inicio, tamanho) in (ReadOnlySpan<(int, int)>)[(0, 4), (4, 2), (6, 2), (8, 2), (10, 6)])
        {
            if (escritos > 0)
            {
                uuid[escritos++] = '-';
            }

            if (!Convert.TryToHexStringLower(bytes.Slice(inicio, tamanho), uuid[escritos..], out var hexadecimais))
            {
                throw new UnreachableException("O destino tem o tamanho dos hexadecimais.");
            }

            escritos += hexadecimais;
        }

        return new string(uuid);
    }

    /// <summary>Se há ao menos uma letra maiúscula ASCII.</summary>
    /// <param name="texto">O texto.</param>
    /// <returns><see langword="true"/> se há letra.</returns>
    internal static bool TemLetra(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (char.IsAsciiLetterUpper(caractere))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TodosIguais(ReadOnlySpan<char> texto)
    {
        foreach (var caractere in texto)
        {
            if (caractere != texto[0])
            {
                return false;
            }
        }

        return true;
    }

    private static void EscreverDv(int dv, Span<char> destino)
    {
        destino[0] = (char)('0' + (dv / 10));
        destino[1] = (char)('0' + (dv % 10));
    }

    private string Sortear<T>(T opcoes, Func<FonteDeSorteio, T, string> gerar)
    {
        if (_fonte is null)
        {
            return gerar(_daThread ??= NovaDaThread(), opcoes);
        }

        lock (_trava)
        {
            return gerar(_fonte, opcoes);
        }
    }
}
