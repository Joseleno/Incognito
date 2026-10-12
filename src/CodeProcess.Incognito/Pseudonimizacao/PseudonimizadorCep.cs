using System.Buffers.Binary;
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

        if (!Caracteres.SoDigitos(digitos) || !Escolher(Caracteres.Valor(digitos), entidade, out var cep))
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

    // Acha a UF do CEP e escolhe um CEP nas faixas de saída dela pela entidade; falso se o CEP não está em faixa nenhuma.
    private bool Escolher(int cepDeEntrada, string entidade, out int cep)
    {
        cep = 0;
        var uf = FaixasDeCep.UfDe(cepDeEntrada, FaixasDeCep.DeSaida);
        if (uf < 0)
        {
            uf = FaixasDeCep.UfDe(cepDeEntrada, FaixasDeCep.SoDeEntrada);
        }

        if (uf < 0)
        {
            return false;
        }

        cep = FaixasDeCep.CepNaPosicao(uf, Semente(uf, entidade) % FaixasDeCep.CepsPorUf[uf]);
        return true;
    }

    private ulong Semente(int uf, string entidade)
    {
        var sigla = FaixasDeCep.Ufs[uf];
        ReadOnlySpan<byte> prefixo = [(byte)sigla[0], (byte)sigla[1], 0];
        Span<byte> resultado = stackalloc byte[HmacDeSemente.Tamanho];
        _hmac.Calcular(prefixo, entidade.AsSpan().Trim(), resultado, recusarInvalido: false);
        return BinaryPrimitives.ReadUInt64BigEndian(resultado);
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
}
