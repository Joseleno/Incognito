using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Gerador detalhado de chaves de DF-e para os testes de propriedade e os vetores: a chave sai na forma pedida pela
/// <see cref="EspecificacaoChaveDfe"/>, mesmo quando a combinação é anômala pela classificação do emitente (ela não é
/// conferida), com o resto sorteado. Só dos testes, fora do pacote e do contrato de sequência do
/// <see cref="GeradorBr"/>, mas a proveniência do <c>br.chave-dfe@1</c> refaz as entradas por ele: a ordem dos
/// sorteios, os nomes dos enumerados da especificação (lidos pela regra) e a lista <see cref="CodigosProibidos"/> ficam
/// congelados. Campo novo só com padrão que não muda os sorteios; se a sequência precisar mudar, fica uma cópia desta. Nenhum emitente forma um CNPJ de raiz <c>00000000</c> com ordem de estabelecimento (todos
/// cadastrados): a raiz zero só sai com a ordem <c>0000</c>, e o CPF começando com <c>00000</c>, que lido como CNPJ
/// teria essa raiz, sai sempre com o DV deslocado.
/// </summary>
internal static class GeradorChaveDfe
{
    /// <summary>
    /// Os 20 códigos numéricos proibidos (regra B03-10), na ordem em que o gerador sorteia: cópia congelada para a
    /// sequência não depender da ordem da lista do produto.
    /// </summary>
    internal static readonly int[] CodigosProibidos =
    [
        0, 11_111_111, 22_222_222, 33_333_333, 44_444_444, 55_555_555, 66_666_666, 77_777_777, 88_888_888, 99_999_999,
        12_345_678, 23_456_789, 34_567_890, 45_678_901, 56_789_012, 67_890_123, 78_901_234, 89_012_345, 90_123_456, 1_234_567,
    ];

    /// <summary>Se o código está entre os 20 proibidos.</summary>
    internal static bool EhCodigoProibido(long codigo) => Array.IndexOf(CodigosProibidos, (int)Math.Clamp(codigo, -1, int.MaxValue)) >= 0;

    /// <summary>Gera uma chave de 44 posições na forma pedida.</summary>
    /// <param name="fonte">A fonte dos sorteios.</param>
    /// <param name="especificacao">O que a chave tem.</param>
    /// <returns>As 44 posições.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Campo fora da faixa ou tipo desconhecido.</exception>
    /// <exception cref="ArgumentException">Código igual ao número sem número sorteado inteiro.</exception>
    internal static string Gerar(FonteDeSorteio fonte, in EspecificacaoChaveDfe especificacao)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        Validar(especificacao);
        Span<char> chave = stackalloc char[RegrasChaveDfe.Tamanho];
        GeradorBr.EscreverNumero(GeradorBr.SortearUf(fonte), chave[..2]);
        GeradorBr.EscreverNumero(especificacao.Aamm, chave[2..6]);
        EscreverEmitente(fonte, especificacao.Emitente, especificacao.DeslocamentoDvEmitente, chave[6..20]);
        GeradorBr.EscreverNumero(especificacao.Modelo, chave[20..22]);
        GeradorBr.EscreverNumero(especificacao.Serie, chave[22..25]);
        var numero = EscreverNumeroDoDfe(fonte, especificacao, chave[25..34]);
        chave[34] = especificacao.TipoEmissao;
        EscreverCodigo(fonte, especificacao, numero, chave[35..43]);
        chave[43] = (char)('0' + ((DigitosVerificadores.ChaveDfe(chave[..43]) + especificacao.DeslocamentoCdv) % 10));
        return new string(chave);
    }

    private static void Validar(in EspecificacaoChaveDfe especificacao)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(especificacao.Aamm, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(especificacao.Aamm, 9_999, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfNegative(especificacao.Modelo, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(especificacao.Modelo, 99, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfNegative(especificacao.Serie, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(especificacao.Serie, 999, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfNegative(especificacao.DeslocamentoCdv, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(especificacao.DeslocamentoCdv, 9, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfNegative(especificacao.DeslocamentoDvEmitente, nameof(especificacao));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(especificacao.DeslocamentoDvEmitente, 99, nameof(especificacao));
        if (!char.IsAsciiDigit(especificacao.TipoEmissao) || !char.IsAsciiDigit(especificacao.TipoDoEmissorNff)
            || !char.IsAsciiDigit(especificacao.SiteAutorizador))
        {
            throw new ArgumentOutOfRangeException(nameof(especificacao), "tpEmis, tipo do emissor e nSiteAutoriz são um dígito.");
        }

        if (!Enum.IsDefined(especificacao.Emitente) || !Enum.IsDefined(especificacao.Numero) || !Enum.IsDefined(especificacao.Codigo))
        {
            throw new ArgumentOutOfRangeException(nameof(especificacao), "Tipo de emitente, de número ou de código desconhecido.");
        }

        if (especificacao.Codigo == CodigoGerado.IgualAoNumero && especificacao.Numero != NumeroGerado.Qualquer)
        {
            throw new ArgumentException("O código igual ao número pede o número sorteado inteiro.", nameof(especificacao));
        }

        if ((especificacao.Dia is not null || especificacao.MesDoNumero is not null)
            && especificacao.Numero is not (NumeroGerado.Nff or NumeroGerado.NffSequencialZero))
        {
            throw new ArgumentException("Dia e mês do número só nas partes da NFF com data pedida.", nameof(especificacao));
        }

        if (especificacao.Dia is < 0 or > 99 || especificacao.MesDoNumero is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(especificacao), "Dia e mês do número têm 2 dígitos.");
        }

        if (especificacao.ValorDoCodigo is { } codigo
            && (especificacao.Codigo != CodigoGerado.Qualquer || codigo < 0 || codigo > (especificacao.Modelo is 62 or 66 ? 9_999_999 : 99_999_999)))
        {
            throw new ArgumentException("O código pedido exige o código sorteado e cabe nos dígitos do modelo.", nameof(especificacao));
        }
    }

    private static void EscreverEmitente(FonteDeSorteio fonte, EmitenteGerado emitente, int deslocamentoDv, Span<char> destino)
    {
        switch (emitente)
        {
            case EmitenteGerado.CnpjNumerico:
            case EmitenteGerado.CnpjAlfanumerico:
                GeradorBr.EscreverCnpj(fonte, emitente == EmitenteGerado.CnpjAlfanumerico, destino);
                break;
            case EmitenteGerado.Cpf:
                "000".CopyTo(destino);
                GeradorBr.EscreverCpf(fonte, destino[3..]);
                break;
            case EmitenteGerado.CpfComZeros:
            case EmitenteGerado.CpfComCincoZeros:
            case EmitenteGerado.CpfRepdigito:
                EscreverCpfEspecial(fonte, emitente, destino);
                break;
            default:
                EscreverCnpjEspecial(fonte, emitente, destino);
                break;
        }

        // O CPF começando com 00000 é, lido como CNPJ, a raiz zero com uma ordem de estabelecimento: com o DV deslocado,
        // não é um CNPJ válido.
        if (emitente == EmitenteGerado.CpfComCincoZeros && deslocamentoDv == 0)
        {
            deslocamentoDv = 1;
        }

        if (deslocamentoDv != 0)
        {
            var dv = (((destino[12] - '0') * 10) + (destino[13] - '0') + deslocamentoDv) % 100;
            GeradorBr.EscreverNumero(dv, destino[12..]);
        }
    }

    private static void EscreverCnpjEspecial(FonteDeSorteio fonte, EmitenteGerado emitente, Span<char> cnpj)
    {
        var raiz = cnpj[..8];
        var ordem = cnpj[8..12];
        do
        {
            switch (emitente)
            {
                case EmitenteGerado.CnpjRaizComZeros:
                case EmitenteGerado.CnpjRaizComZerosEOrdemAlfanumerica:
                    "000".CopyTo(raiz);
                    GeradorBr.SortearDigitos(fonte, raiz[3..]);
                    break;
                case EmitenteGerado.CnpjAlfanumericoComZeros:
                    "000".CopyTo(raiz);
                    SortearAlfanumericos(fonte, raiz[3..]);
                    break;
                case EmitenteGerado.CnpjRaizZero:
                    "00000000".CopyTo(raiz);
                    break;
                case EmitenteGerado.CnpjAlfanumericoComOrdemAlfanumerica:
                    SortearAlfanumericos(fonte, raiz);
                    break;
                default:
                    GeradorBr.SortearDigitos(fonte, raiz);
                    break;
            }

            switch (emitente)
            {
                case EmitenteGerado.CnpjNumericoComOrdemAlfanumerica:
                case EmitenteGerado.CnpjRaizComZerosEOrdemAlfanumerica:
                    ordem[0] = (char)('A' + (int)fonte.Uniforme(26));
                    GeradorBr.SortearDigitos(fonte, ordem[1..]);
                    break;
                case EmitenteGerado.CnpjAlfanumericoComOrdemAlfanumerica:
                    SortearAlfanumericos(fonte, ordem);
                    break;
                case EmitenteGerado.CnpjRaizZero:
                    "0000".CopyTo(ordem);
                    break;
                case EmitenteGerado.CnpjAlfanumericoComZeros:
                    "0001".CopyTo(ordem);
                    break;
                default:
                    GeradorBr.SortearDigitos(fonte, ordem);
                    break;
            }
        }
        while (!Coerente(emitente, raiz, ordem));

        GeradorBr.EscreverNumero(DigitosVerificadores.Cnpj(cnpj[..12]), cnpj[12..]);
    }

    // A raiz zero só no tipo dela (com a ordem 0000); filial nunca com a ordem 0001; raiz alfanumérica com letra.
    private static bool Coerente(EmitenteGerado emitente, ReadOnlySpan<char> raiz, ReadOnlySpan<char> ordem) => emitente switch
    {
        EmitenteGerado.CnpjRaizZero => true,
        EmitenteGerado.CnpjAlfanumericoComOrdemAlfanumerica or EmitenteGerado.CnpjAlfanumericoComZeros => GeradorBr.TemLetra(raiz),
        EmitenteGerado.CnpjFilial => !raiz.SequenceEqual("00000000") && !ordem.SequenceEqual("0001"),
        _ => !raiz.SequenceEqual("00000000"),
    };

    private static void EscreverCpfEspecial(FonteDeSorteio fonte, EmitenteGerado emitente, Span<char> destino)
    {
        "000".CopyTo(destino);
        var cpf = destino[3..];
        var baseCpf = cpf[..9];
        if (emitente == EmitenteGerado.CpfRepdigito)
        {
            baseCpf.Fill((char)('0' + (int)fonte.Uniforme(10)));
        }
        else
        {
            // Começando com 00 (mas não com 00000, o outro tipo) ou com 00000; nunca a base toda em zero.
            var zeros = emitente == EmitenteGerado.CpfComCincoZeros ? 5 : 2;
            baseCpf[..zeros].Fill('0');
            do
            {
                GeradorBr.SortearDigitos(fonte, baseCpf[zeros..]);
            }
            while (Caracteres.SoZeros(baseCpf) || (zeros == 2 && baseCpf.StartsWith("00000")));
        }

        GeradorBr.EscreverNumero(DigitosVerificadores.Cpf(baseCpf), cpf[9..]);
    }

    private static long EscreverNumeroDoDfe(FonteDeSorteio fonte, in EspecificacaoChaveDfe especificacao, Span<char> destino)
    {
        switch (especificacao.Numero)
        {
            case NumeroGerado.Zero:
                destino.Fill('0');
                return 0;
            case NumeroGerado.Qualquer:
                // Igual ao código, o número precisa caber nos 8 dígitos dele.
                var limite = especificacao.Codigo == CodigoGerado.IgualAoNumero ? 99_999_999UL : 999_999_999UL;
                var numero = (long)fonte.Uniforme(limite) + 1;
                GeradorBr.EscreverNumero(numero, destino);
                return numero;
            default:
                EscreverNff(fonte, especificacao, destino);
                return Caracteres.Valor(destino);
        }
    }

    // NF-e e NFC-e (e qualquer outro modelo, na forma pedida): dia, mês, tipo, sequencial de 4; CT-e e MDF-e: mês, dia,
    // sequencial de 5. O mês é o do AAMM (ou o pedido em MesDoNumero); fora de 01 a 12, ele vai assim para o número e a
    // data sai inválida. O dia é sorteado no mês do AAMM, ou o pedido em Dia.
    private static void EscreverNff(FonteDeSorteio fonte, in EspecificacaoChaveDfe especificacao, Span<char> destino)
    {
        var transporte = especificacao.Modelo is 57 or 58;
        var mes = especificacao.Aamm % 100;
        var ano = especificacao.Aamm / 100;
        var dias = mes is >= 1 and <= 12 ? DateTime.DaysInMonth(2000 + ano, mes) : 31;
        // Sorteia sempre e só então usa o pedido: a sequência não muda com os campos opcionais.
        var diaSorteado = 1 + (int)fonte.Uniforme((ulong)dias);
        var dia = especificacao.Dia ?? diaSorteado;
        mes = especificacao.MesDoNumero ?? mes;
        if (especificacao.Numero == NumeroGerado.NffDataInvalida)
        {
            switch (fonte.Uniforme(4))
            {
                case 0:
                    mes = 0;
                    break;
                case 1:
                    mes = 13;
                    break;
                case 2:
                    dia = 0;
                    break;
                default:
                    dia = dias + 1 + (int)fonte.Uniforme((ulong)(35 - dias));
                    break;
            }
        }

        var tamanhoDoSequencial = transporte ? 5 : 4;
        var sequencial = especificacao.Numero == NumeroGerado.NffSequencialZero
            ? 0
            : 1 + (long)fonte.Uniforme(transporte ? 99_999UL : 9_999UL);
        if (transporte)
        {
            GeradorBr.EscreverNumero(mes, destino[..2]);
            GeradorBr.EscreverNumero(dia, destino[2..4]);
        }
        else
        {
            GeradorBr.EscreverNumero(dia, destino[..2]);
            GeradorBr.EscreverNumero(mes, destino[2..4]);
            destino[4] = especificacao.TipoDoEmissorNff;
        }

        GeradorBr.EscreverNumero(sequencial, destino[^tamanhoDoSequencial..]);
    }

    private static void EscreverCodigo(FonteDeSorteio fonte, in EspecificacaoChaveDfe especificacao, long numero, Span<char> destino)
    {
        if (especificacao.Modelo is 62 or 66)
        {
            destino[0] = especificacao.SiteAutorizador;
            GeradorBr.SortearDigitos(fonte, destino[1..]);

            // Sorteado antes do pedido, para a sequência não mudar com o campo opcional.
            if (especificacao.ValorDoCodigo is { } codigoDe7)
            {
                GeradorBr.EscreverNumero(codigoDe7, destino[1..]);
            }

            return;
        }

        switch (especificacao.Codigo)
        {
            case CodigoGerado.Proibido:
                GeradorBr.EscreverNumero(CodigosProibidos[(int)fonte.Uniforme((ulong)CodigosProibidos.Length)], destino);
                break;
            case CodigoGerado.IgualAoNumero:
                GeradorBr.EscreverNumero(numero, destino);
                break;
            default:
                GeradorBr.SortearDigitos(fonte, destino);

                // Sorteado antes do pedido, para a sequência não mudar com o campo opcional.
                if (especificacao.ValorDoCodigo is { } codigo)
                {
                    GeradorBr.EscreverNumero(codigo, destino);
                }

                break;
        }
    }

    private static void SortearAlfanumericos(FonteDeSorteio fonte, Span<char> destino)
    {
        for (var i = 0; i < destino.Length; i++)
        {
            destino[i] = GeradorBr.Alfanumericos[(int)fonte.Uniforme((ulong)GeradorBr.Alfanumericos.Length)];
        }
    }
}
