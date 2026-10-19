namespace CodeProcess.Incognito.Dialetos;

/// <summary>
/// O diagnóstico do contrato de dialeto, que muda até a 1.0: quem implementa ou consome o contrato fora do motor
/// suprime o <c>INCX001</c> no próprio projeto, sabendo que pode ter de acompanhar mudanças.
/// </summary>
internal static class ContratoExperimental
{
    /// <summary>O identificador do diagnóstico.</summary>
    internal const string Diagnostico = "INCX001";
}
