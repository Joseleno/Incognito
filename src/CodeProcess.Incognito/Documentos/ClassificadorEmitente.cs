using System.Diagnostics;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// Classifica o emitente da chave de DF-e (CPF ou CNPJ) só por campos que a pseudonimização preserva, para a saída ter
/// sempre a mesma classificação da entrada. O DV nunca decide: todo CPF que começa com "00" forma, com o prefixo
/// <c>000</c>, um CNPJ de DV válido.
/// </summary>
/// <remarks>
/// <para>
/// NF-e e NFC-e, na ordem: letra nas posições 7 a 18 ou AAMM anterior a 1808 (o emitente CPF na chave começou em
/// 08/2018) dá CNPJ; tpEmis 3 com série fora do PAA (970 a 989) e AAMM a partir de 2104 é a Nota Fiscal Fácil,
/// decidida pela posição 30 (1 CNPJ, 2 CPF, outro valor anomalia), e antes disso é anomalia; depois, a série: 000 a 909
/// CNPJ, 910 a 969 CPF e, só na NF-e, 970 a 979 CPF e 980 a 989 CNPJ (PAA); o resto é anomalia. CPF sem <c>000</c>
/// nas posições 7 a 9 é anomalia.
/// </para>
/// <para>
/// CT-e e MDF-e, pela regra dos zeros: nenhuma regra de série separa os dois documentos com segurança (o PAA usa tpEmis
/// 3 também com CNPJ de MEI). Letra nas posições 7 a 18 dá CNPJ; senão, <c>000</c> nas posições 7 a 9 dá CPF; senão,
/// CNPJ com <see cref="ClassificacaoEmitente.ExcluiRaizComZeros"/>, para a saída não ser lida como CPF. A Nota Fiscal
/// Fácil é tpEmis 3 com série fora do PAA e AAMM a partir de 2006: o tpEmis 3 só passou a ser aceito com as NTs da NFF
/// (NT MDF-e 2020.002, homologação em 06/2020; NT CT-e 2020.001). Os outros modelos só admitem CNPJ.
/// </para>
/// </remarks>
internal static class ClassificadorEmitente
{
    private const int PrimeiroAammComCpf = 1808;
    private const int PrimeiroAammDaNff = 2104;
    private const int PrimeiroAammDaNffDeTransporte = 2006;

    /// <summary>Classifica o emitente.</summary>
    /// <param name="chave">
    /// As 44 posições, sem prefixo nem separadores. O cDV não é conferido: uma chave com cDV errado tem a mesma
    /// classificação. Fora da estrutura (tamanho, minúscula, dígito que não é ASCII, letra fora das posições 7 a 18), a
    /// chave é anomalia, nunca CNPJ.
    /// </param>
    /// <returns>A classificação.</returns>
    internal static ClassificacaoEmitente Classificar(ReadOnlySpan<char> chave)
    {
        if (!RegrasChaveDfe.TemEstrutura(chave))
        {
            return ClassificacaoEmitente.DeAnomalia(MotivoAnomaliaDfe.EstruturaInvalida, null);
        }

        if (SoZeros(chave))
        {
            return ClassificacaoEmitente.DeAnomalia(MotivoAnomaliaDfe.ChaveZerada, null);
        }

        if (!LeiautesDfe.TentarObter(chave[20..22], out var leiaute))
        {
            return ClassificacaoEmitente.DeAnomalia(MotivoAnomaliaDfe.ModeloForaDaTabela, null);
        }

        // Com a estrutura conferida, o que não é dígito nas posições 7 a 18 é letra maiúscula.
        var comLetra = !SoDigitos(chave[6..18]);
        return leiaute.Regra switch
        {
            RegraEmitente.SempreCnpj => ClassificacaoEmitente.DeCnpj(leiaute, ehNff: false, excluiRaizComZeros: false),
            RegraEmitente.Zeros => PelosZeros(chave, leiaute, comLetra),
            RegraEmitente.Series or RegraEmitente.SeriesComPaa => PelasSeries(chave, leiaute, comLetra),
            _ => throw new UnreachableException("Leiaute de DF-e sem regra do emitente."),
        };
    }

    private static ClassificacaoEmitente PelosZeros(ReadOnlySpan<char> chave, LeiauteDfe leiaute, bool comLetra)
    {
        var ehNff = TipoEmissao3(chave) && !EhSerieDoPaa(Serie(chave)) && Valor(chave[2..6]) >= PrimeiroAammDaNffDeTransporte;
        if (comLetra)
        {
            // A presença de letra é preservada (a raiz alfanumérica sai sempre com letra): as posições 7 a 9 não decidem.
            return ClassificacaoEmitente.DeCnpj(leiaute, ehNff, excluiRaizComZeros: false);
        }

        return TemZerosDoCpf(chave)
            ? ClassificacaoEmitente.DeCpf(leiaute, ehNff)
            : ClassificacaoEmitente.DeCnpj(leiaute, ehNff, excluiRaizComZeros: true);
    }

    private static ClassificacaoEmitente PelasSeries(ReadOnlySpan<char> chave, LeiauteDfe leiaute, bool comLetra)
    {
        var aamm = Valor(chave[2..6]);
        var serie = Serie(chave);
        var nffPossivel = TipoEmissao3(chave) && !EhSerieDoPaa(serie);
        var ehNff = nffPossivel && aamm >= PrimeiroAammDaNff;
        if (comLetra || aamm < PrimeiroAammComCpf)
        {
            return ClassificacaoEmitente.DeCnpj(leiaute, ehNff, excluiRaizComZeros: false);
        }

        // O tpEmis 3 do SCAN (desativado até 09/2014) já caiu no AAMM anterior a 1808.
        if (nffPossivel && !ehNff)
        {
            return ClassificacaoEmitente.DeAnomalia(MotivoAnomaliaDfe.TipoEmissao3ForaDoPeriodo, leiaute);
        }

        var tipo = ehNff ? TipoPelaNff(chave) : TipoPelaSerie(serie, leiaute.Regra == RegraEmitente.SeriesComPaa);
        if (tipo is null)
        {
            var motivo = ehNff ? MotivoAnomaliaDfe.NffSemTipoDeEmissor : MotivoAnomaliaDfe.SerieSemEmissao;
            return ClassificacaoEmitente.DeAnomalia(motivo, leiaute);
        }

        if (tipo == TipoEmitente.Cnpj)
        {
            return ClassificacaoEmitente.DeCnpj(leiaute, ehNff, excluiRaizComZeros: false);
        }

        return TemZerosDoCpf(chave)
            ? ClassificacaoEmitente.DeCpf(leiaute, ehNff)
            : ClassificacaoEmitente.DeAnomalia(MotivoAnomaliaDfe.CpfSemZerosAEsquerda, leiaute);
    }

    // Posição 30 da NFF da NF-e e da NFC-e: o tipo do emissor.
    private static TipoEmitente? TipoPelaNff(ReadOnlySpan<char> chave) => chave[29] switch
    {
        '1' => TipoEmitente.Cnpj,
        '2' => TipoEmitente.Cpf,
        _ => null,
    };

    private static TipoEmitente? TipoPelaSerie(int serie, bool comPaa) => serie switch
    {
        <= 909 => TipoEmitente.Cnpj,
        <= 969 => TipoEmitente.Cpf,
        <= 979 when comPaa => TipoEmitente.Cpf,
        <= 989 when comPaa => TipoEmitente.Cnpj,
        _ => null,
    };

    // O PAA usa tpEmis 3 com numeração sequencial nessas séries: o número não traz data. A série da NFF é dispositivo ou
    // indicador mais o ano, que não chega a 97x ou 98x.
    private static bool EhSerieDoPaa(int serie) => serie is >= 970 and <= 989;

    private static bool TipoEmissao3(ReadOnlySpan<char> chave) => chave[34] == '3';

    private static int Serie(ReadOnlySpan<char> chave) => Valor(chave[22..25]);

    private static bool TemZerosDoCpf(ReadOnlySpan<char> chave) => chave[6..9] is "000";
}
