namespace CodeProcess.Incognito.Criptografia;

/// <summary>
/// Espaços permutados, que separam as chaves de domínio de um mesmo domínio. Os textos entram na derivação e são
/// contrato: um erro de digitação geraria outra permutação, por isso só estes valores são aceitos.
/// </summary>
internal static class Variantes
{
    internal const string Numerico = "num";
    internal const string Alfanumerico = "alfa";
    internal const string Celular = "cel";
    internal const string Fixo = "fixo";
    internal const string Ordem = "ordem";
    internal const string Identificador = "id";
    internal const string NumeroDfe = "nnf";
    internal const string DiaNff = "nff-dia";
    internal const string SequencialNff = "nff-seq";
    internal const string CodigoNumerico8 = "cnf8";
    internal const string CodigoNumerico7 = "cnf7";

    internal static bool EhConhecida(string variante) => variante is
        Numerico or Alfanumerico or Celular or Fixo or Ordem or Identificador
        or NumeroDfe or DiaNff or SequencialNff or CodigoNumerico8 or CodigoNumerico7;
}
