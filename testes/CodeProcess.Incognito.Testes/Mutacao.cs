namespace CodeProcess.Incognito.Testes;

/// <summary>Os testes pesados sob a mutação noturna (<c>noturno.yml</c>), que roda com <c>INCOGNITO_MUTACAO=1</c>.</summary>
/// <remarks>
/// O Stryker captura a cobertura teste a teste, com o código instrumentado (cerca de 10 vezes mais lento): um teste que
/// passa de ~30 s assim quebra a captura dele e a de quase todos os seguintes, e o noturno falha. Por isso, rodando a
/// suíte com <c>INCOGNITO_MUTACAO=1</c> (sem o Stryker), nenhum teste deve passar de ~1 s: o pesado usa um tamanho menor
/// (<see cref="Tamanho"/>), escolhido para que o conjunto dos casos continue cobrindo as mesmas linhas; o estatístico
/// pesado, cujos valores críticos dependem do tamanho da amostra, é pulado com <see cref="Motivo"/>; o de alocação, com
/// <see cref="MotivoAlocacao"/>; o que lê o IL do núcleo, com <see cref="MotivoIl"/>. Fora da mutação, nada muda, e no CI
/// a variável só é aceita no workflow <c>noturno</c> (<see cref="MutacaoTestes"/>).
/// </remarks>
internal static class Mutacao
{
    /// <summary>Motivo do teste pesado pulado sob a mutação.</summary>
    public const string Motivo = "Pesado para a mutação noturna (INCOGNITO_MUTACAO=1).";

    /// <summary>Motivo do teste de alocação pulado sob a mutação, em que o código instrumentado aloca por conta própria.</summary>
    public const string MotivoAlocacao = "Mede alocação, e sob a mutação noturna (INCOGNITO_MUTACAO=1) o código instrumentado aloca.";

    /// <summary>
    /// Motivo do teste que lê o IL do núcleo, pulado sob a mutação: o Stryker reescreve o IL (inclusive o inicializador
    /// estático dos dados, embrulhado para rastrear o valor), e o teste falharia na dll mutada, matando mutantes à toa.
    /// </summary>
    public const string MotivoIl = "Lê o IL do núcleo, que o Stryker reescreve sob a mutação noturna (INCOGNITO_MUTACAO=1).";

    /// <summary>Se a suíte roda sob a mutação.</summary>
    public static bool Ativa { get; } = Environment.GetEnvironmentVariable("INCOGNITO_MUTACAO") == "1";

    /// <summary>O tamanho de um teste pesado: o normal, ou o reduzido sob a mutação.</summary>
    /// <param name="normal">Tamanho fora da mutação.</param>
    /// <param name="sobMutacao">Tamanho sob a mutação.</param>
    /// <returns>O tamanho a usar.</returns>
    public static int Tamanho(int normal, int sobMutacao) => Ativa ? sobMutacao : normal;
}
