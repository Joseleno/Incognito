using System.Buffers.Binary;
using System.Diagnostics;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização de CEP: a UF sai da faixa do CEP de entrada, e o CEP de saída é escolhido nas faixas dessa UF pela
/// entidade (o valor original da coluna de <c>derivarDe</c>, como o CPF da pessoa), não pelo próprio CEP. A mesma
/// entidade na mesma UF dá sempre o mesmo CEP, então endereços da mesma pessoa não se espalham; o CEP original não
/// entra na escolha além da UF. Não é injetivo.
/// </summary>
/// <remarks>
/// <para>
/// Escolha: os 8 primeiros bytes do HMAC da <c>K_semente</c> sobre <c>UF ‖ 0x00 ‖ entidade</c> (a sigla da UF em
/// ASCII; a entidade sem os espaços das pontas, em UTF-8, com surrogate sem par trocado por U+FFFD), como número
/// big-endian, módulo a quantidade de CEPs nas faixas da UF, contados na ordem das faixas. A UF na mensagem faz cada
/// estado escolher de forma independente: a mesma pessoa em duas UFs não sai com os mesmos dígitos finais. Entidade
/// vazia (o Motor passa vazio para NULL) dá um CEP fixo por UF. Máscara ou outra grafia da entidade é outra entidade.
/// </para>
/// <para>
/// Faixas por UF dos Correios; a faixa antiga de RO (<c>78900-000</c> a <c>78999-999</c>) é aceita como entrada de RO,
/// mas a saída sai só das faixas atuais. Um CEP fora de toda faixa (como <c>00000-000</c>) segue <c>entradaMalformada</c>.
/// Formas: 8 dígitos, <c>00000-000</c> e <c>00.000-000</c>, com a máscara preservada; em texto só com dígitos, 7 dígitos
/// (CEP que perdeu o zero à esquerda) completados e devolvidos com 8, com o zero acrescentado saindo do preenchimento
/// de <c>char(n)</c>; em coluna numérica, de 0 a 99.999.999. Espaços à direita voltam como estavam; texto vazio ou só
/// com espaços volta como veio. Nenhuma exceção traz o valor. Pode ser usado por várias threads ao mesmo tempo; o
/// descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorCep : IDisposable
{
    private const string Transformador = "br.cep";
    private const long MaiorCep = 99_999_999;
    private const string MensagemTextoMalformado =
        "Valor fora da forma de CEP (8 dígitos, máscara 00000-000 ou 00.000-000, ou 7 dígitos sem o zero à esquerda) ou fora das faixas das UFs; a política entradaMalformada é falhar. O valor não é exibido.";
    private const string MensagemNumeroMalformado =
        "Número fora das faixas de CEP das UFs; a política entradaMalformada é falhar. O valor não é exibido.";

    // Siglas na ordem dos índices usados nas faixas.
    private static readonly string[] _ufs =
        ["SP", "RJ", "ES", "MG", "BA", "SE", "PE", "AL", "PB", "RN", "CE", "PI", "MA", "PA", "AP", "AM", "RR", "AC", "DF", "GO", "RO", "TO", "MT", "MS", "PR", "SC", "RS"];

    // Faixas de CEP por UF dos Correios, em ordem crescente; o terceiro campo é o índice da UF em _ufs.
    private static readonly Faixa[] _faixas =
    [
        new(1_000_000, 19_999_999, 0),
        new(20_000_000, 28_999_999, 1),
        new(29_000_000, 29_999_999, 2),
        new(30_000_000, 39_999_999, 3),
        new(40_000_000, 48_999_999, 4),
        new(49_000_000, 49_999_999, 5),
        new(50_000_000, 56_999_999, 6),
        new(57_000_000, 57_999_999, 7),
        new(58_000_000, 58_999_999, 8),
        new(59_000_000, 59_999_999, 9),
        new(60_000_000, 63_999_999, 10),
        new(64_000_000, 64_999_999, 11),
        new(65_000_000, 65_999_999, 12),
        new(66_000_000, 68_899_999, 13),
        new(68_900_000, 68_999_999, 14),
        new(69_000_000, 69_299_999, 15),
        new(69_300_000, 69_399_999, 16),
        new(69_400_000, 69_899_999, 15),
        new(69_900_000, 69_999_999, 17),
        new(70_000_000, 72_799_999, 18),
        new(72_800_000, 72_999_999, 19),
        new(73_000_000, 73_699_999, 18),
        new(73_700_000, 76_799_999, 19),
        new(76_800_000, 76_999_999, 20),
        new(77_000_000, 77_999_999, 21),
        new(78_000_000, 78_899_999, 22),
        new(79_000_000, 79_999_999, 23),
        new(80_000_000, 87_999_999, 24),
        new(88_000_000, 89_999_999, 25),
        new(90_000_000, 99_999_999, 26),
    ];

    // Faixas que só decidem a UF da entrada e nunca são escolhidas para a saída: a 78900-000 a 78999-999, de RO em
    // tabelas antigas e fora da atual dos Correios, ainda pode estar em bases antigas.
    private static readonly Faixa[] _faixasSoDeEntrada =
    [
        new(78_900_000, 78_999_999, 20),
    ];

    // Quantidade de CEPs de cada UF, somada uma vez a partir das faixas.
    private static readonly ulong[] _cepsPorUf = SomarPorUf();

    private readonly HmacDeSemente _hmac;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização de um domínio de CEP.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (por padrão, <c>cep</c>).</param>
    /// <param name="versao">Versão do transformador, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    /// <param name="malformada">Política para valor fora da forma de CEP.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    internal PseudonimizadorCep(ChaveIncognito chave, string dominio, int versao, string ambiente, PoliticaEntradaMalformada malformada)
    {
        Politicas.Validar(malformada, nameof(malformada));
        _hmac = HmacDeSemente.Derivar(chave, Transformador, dominio, versao, ambiente);
        _malformada = malformada;
    }

    private enum Mascara
    {
        Nenhuma,
        Hifen,
        PontoEHifen,
    }

    /// <summary>Pseudonimiza um CEP em texto.</summary>
    /// <param name="entrada">O CEP como veio da coluna.</param>
    /// <param name="entidade">O valor original da coluna de <c>derivarDe</c> na mesma linha (vazio se for NULL).</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro ou vazia.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(string entrada, string entidade, out string? saida)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ArgumentNullException.ThrowIfNull(entidade);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        var corpo = entrada.AsSpan().TrimEnd(' ');
        var preenchimento = entrada.AsSpan(corpo.Length);
        if (corpo.IsEmpty)
        {
            saida = entrada;
            return Desfecho.Vazio;
        }

        Span<char> digitos = stackalloc char[8];
        Mascara mascara;
        if (corpo.Length == 9 && corpo[5] == '-')
        {
            corpo[..5].CopyTo(digitos);
            corpo[6..].CopyTo(digitos[5..]);
            mascara = Mascara.Hifen;
        }
        else if (corpo.Length == 10 && corpo[2] == '.' && corpo[6] == '-')
        {
            corpo[..2].CopyTo(digitos);
            corpo[3..6].CopyTo(digitos[2..]);
            corpo[7..].CopyTo(digitos[5..]);
            mascara = Mascara.PontoEHifen;
        }
        else if (corpo.Length is 7 or 8)
        {
            var zeros = 8 - corpo.Length;
            digitos[..zeros].Fill('0');
            corpo.CopyTo(digitos[zeros..]);
            preenchimento = preenchimento[Math.Min(zeros, preenchimento.Length)..];
            mascara = Mascara.Nenhuma;
        }
        else
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        if (!Caracteres.SoDigitos(digitos) || !Escolher(Ler(digitos), entidade, out var cep))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemTextoMalformado, entrada, out saida);
        }

        Span<char> formatado = stackalloc char[10];
        var tamanho = Formatar(cep, mascara, formatado);
        saida = string.Concat(formatado[..tamanho], preenchimento);
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Pseudonimiza um CEP guardado em coluna numérica.</summary>
    /// <param name="entrada">O CEP como número, sem o zero à esquerda.</param>
    /// <param name="entidade">O valor original da coluna de <c>derivarDe</c> na mesma linha (vazio se for NULL).</param>
    /// <param name="saida">O pseudônimo; nulo se anulado; a própria entrada se copiada em claro.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal Desfecho Pseudonimizar(long entrada, string entidade, out long? saida)
    {
        ArgumentNullException.ThrowIfNull(entidade);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        if (entrada is < 0 or > MaiorCep || !Escolher((int)entrada, entidade, out var cep))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemNumeroMalformado, entrada, out saida);
        }

        saida = cep;
        return Desfecho.Pseudonimizado;
    }

    /// <summary>Descarta a cópia da chave de semente.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _hmac.Dispose();
    }

    private static ulong[] SomarPorUf()
    {
        var quantidades = new ulong[_ufs.Length];
        foreach (var faixa in _faixas)
        {
            quantidades[faixa.Uf] += faixa.Tamanho;
        }

        return quantidades;
    }

    // Acha a UF do CEP e escolhe um CEP nas faixas de saída dela pela entidade; falso se o CEP não está em faixa nenhuma.
    private bool Escolher(int cepDeEntrada, string entidade, out int cep)
    {
        cep = 0;
        var uf = UfDe(cepDeEntrada, _faixas);
        if (uf < 0)
        {
            uf = UfDe(cepDeEntrada, _faixasSoDeEntrada);
        }

        if (uf < 0)
        {
            return false;
        }

        var indice = Semente(uf, entidade) % _cepsPorUf[uf];
        foreach (var faixa in _faixas)
        {
            if (faixa.Uf != uf)
            {
                continue;
            }

            if (indice < faixa.Tamanho)
            {
                cep = faixa.Inicio + (int)indice;
                return true;
            }

            indice -= faixa.Tamanho;
        }

        throw new UnreachableException("A escolha saiu das faixas da UF.");
    }

    private static int UfDe(int cep, Faixa[] faixas)
    {
        foreach (var faixa in faixas)
        {
            if (cep >= faixa.Inicio && cep <= faixa.Fim)
            {
                return faixa.Uf;
            }
        }

        return -1;
    }

    private ulong Semente(int uf, string entidade)
    {
        var sigla = _ufs[uf];
        ReadOnlySpan<byte> prefixo = [(byte)sigla[0], (byte)sigla[1], 0];
        Span<byte> resultado = stackalloc byte[HmacDeSemente.Tamanho];
        _hmac.Calcular(prefixo, entidade.AsSpan().Trim(), resultado, recusarInvalido: false);
        return BinaryPrimitives.ReadUInt64BigEndian(resultado);
    }

    private static int Ler(ReadOnlySpan<char> digitos)
    {
        var valor = 0;
        foreach (var digito in digitos)
        {
            valor = (valor * 10) + (digito - '0');
        }

        return valor;
    }

    private static int Formatar(int cep, Mascara mascara, Span<char> destino)
    {
        Span<char> digitos = stackalloc char[8];
        for (var i = 7; i >= 0; i--)
        {
            digitos[i] = (char)('0' + (cep % 10));
            cep /= 10;
        }

        switch (mascara)
        {
            case Mascara.Hifen:
                digitos[..5].CopyTo(destino);
                destino[5] = '-';
                digitos[5..].CopyTo(destino[6..]);
                return 9;
            case Mascara.PontoEHifen:
                digitos[..2].CopyTo(destino);
                destino[2] = '.';
                digitos[2..5].CopyTo(destino[3..]);
                destino[6] = '-';
                digitos[5..].CopyTo(destino[7..]);
                return 10;
            default:
                digitos.CopyTo(destino);
                return 8;
        }
    }

    private readonly struct Faixa(int inicio, int fim, int uf)
    {
        internal int Inicio { get; } = inicio;

        internal int Fim { get; } = fim;

        internal int Uf { get; } = uf;

        internal ulong Tamanho => (ulong)(Fim - Inicio + 1);
    }
}
