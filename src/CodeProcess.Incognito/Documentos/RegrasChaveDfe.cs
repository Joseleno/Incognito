namespace CodeProcess.Incognito.Documentos;

/// <summary>Regras da chave de acesso de DF-e usadas pela validação e pela pseudonimização.</summary>
internal static class RegrasChaveDfe
{
    /// <summary>
    /// Os 20 códigos numéricos de 8 dígitos proibidos pela regra B03-10 (rejeição 897, NT 2019.001 v1.70): os 10
    /// repdígitos e as 10 sequências crescentes. A regra é da NF-e (a NT retirou o modelo 65), mas o projeto exclui
    /// esses códigos da saída em todo modelo com código numérico de 8 dígitos, porque isso só restringe a saída; NF3e e
    /// NFCom têm código de 7 dígitos e ficam de fora. Chaves anteriores à regra podem trazê-los: a validação não os
    /// recusa, só a pseudonimização deixa de produzi-los.
    /// </summary>
    internal static ReadOnlySpan<int> CodigosNumericosProibidos =>
    [
        0, 11_111_111, 22_222_222, 33_333_333, 44_444_444, 55_555_555, 66_666_666, 77_777_777, 88_888_888, 99_999_999,
        12_345_678, 23_456_789, 34_567_890, 45_678_901, 56_789_012, 67_890_123, 78_901_234, 89_012_345, 90_123_456, 1_234_567,
    ];

    /// <summary>Se o código numérico de 8 dígitos está entre os proibidos.</summary>
    /// <param name="codigo">O código, de 0 a 99.999.999.</param>
    /// <returns><see langword="true"/> se a regra B03-10 o proíbe.</returns>
    internal static bool EhCodigoNumericoProibido(int codigo) => CodigosNumericosProibidos.Contains(codigo);
}
