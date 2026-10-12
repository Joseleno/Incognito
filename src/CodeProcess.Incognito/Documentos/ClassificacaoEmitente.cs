using System.Diagnostics;

namespace CodeProcess.Incognito.Documentos;

/// <summary>
/// O documento do emitente de uma chave de DF-e, ou por que ela não tem um. O valor padrão é anomalia
/// (<see cref="MotivoAnomaliaDfe.NaoClassificada"/>): nada não inicializado é tratado como CNPJ. Quem consome confere
/// <see cref="EhAnomalia"/> e, nele, <see cref="EhChaveZerada"/> antes da política de entrada malformada.
/// </summary>
internal readonly struct ClassificacaoEmitente
{
    private ClassificacaoEmitente(
        TipoEmitente tipo, MotivoAnomaliaDfe anomalia, LeiauteDfe? leiaute, bool ehNff, bool excluiRaizComZeros)
    {
        Tipo = tipo;
        Anomalia = anomalia;
        Leiaute = leiaute;
        EhNff = ehNff;
        ExcluiRaizComZeros = excluiRaizComZeros;
    }

    /// <summary>O documento do emitente; fora de <see cref="TipoEmitente"/> se for anomalia.</summary>
    internal TipoEmitente Tipo { get; }

    /// <summary>Por que não há emitente; <see cref="MotivoAnomaliaDfe.Nenhum"/> se classificada.</summary>
    internal MotivoAnomaliaDfe Anomalia { get; }

    /// <summary>
    /// O leiaute do modelo; nulo só se a chave é zerada, está fora da estrutura ou tem modelo fora da tabela.
    /// </summary>
    internal LeiauteDfe? Leiaute { get; }

    /// <summary>
    /// Se o número tem as partes da Nota Fiscal Fácil (<see cref="LeiauteDfe.Nff"/>); falso na anomalia.
    /// </summary>
    internal bool EhNff { get; }

    /// <summary>
    /// Se a pseudonimização do CNPJ precisa excluir da saída as raízes <c>000xxxxx</c> (regra dos zeros): sem isso, a
    /// saída poderia ser lida como CPF.
    /// </summary>
    internal bool ExcluiRaizComZeros { get; }

    /// <summary>Se a chave não tem emitente classificado (inclusive a zerada e o valor padrão).</summary>
    internal bool EhAnomalia => Tipo is not (TipoEmitente.Cnpj or TipoEmitente.Cpf);

    /// <summary>Se é a chave de 44 zeros, que volta como veio em vez de seguir a política de entrada malformada.</summary>
    internal bool EhChaveZerada => Anomalia == MotivoAnomaliaDfe.ChaveZerada;

    /// <summary>Emitente CNPJ.</summary>
    /// <param name="leiaute">O leiaute do modelo.</param>
    /// <param name="ehNff">Se o número tem as partes da Nota Fiscal Fácil.</param>
    /// <param name="excluiRaizComZeros">Se a saída precisa evitar as raízes <c>000xxxxx</c>.</param>
    /// <returns>A classificação.</returns>
    internal static ClassificacaoEmitente DeCnpj(LeiauteDfe leiaute, bool ehNff, bool excluiRaizComZeros)
    {
        Debug.Assert(!ehNff || leiaute.Nff is not null, "NFF num modelo sem as partes da NFF.");
        return new(TipoEmitente.Cnpj, MotivoAnomaliaDfe.Nenhum, leiaute, ehNff, excluiRaizComZeros);
    }

    /// <summary>Emitente CPF.</summary>
    /// <param name="leiaute">O leiaute do modelo.</param>
    /// <param name="ehNff">Se o número tem as partes da Nota Fiscal Fácil.</param>
    /// <returns>A classificação.</returns>
    internal static ClassificacaoEmitente DeCpf(LeiauteDfe leiaute, bool ehNff)
    {
        Debug.Assert(!ehNff || leiaute.Nff is not null, "NFF num modelo sem as partes da NFF.");
        return new(TipoEmitente.Cpf, MotivoAnomaliaDfe.Nenhum, leiaute, ehNff, excluiRaizComZeros: false);
    }

    /// <summary>Chave sem emitente classificado.</summary>
    /// <param name="motivo">Por quê; nunca <see cref="MotivoAnomaliaDfe.Nenhum"/> nem o valor padrão.</param>
    /// <param name="leiaute">O leiaute do modelo, se ele está na tabela.</param>
    /// <returns>A classificação.</returns>
    internal static ClassificacaoEmitente DeAnomalia(MotivoAnomaliaDfe motivo, LeiauteDfe? leiaute)
    {
        Debug.Assert(motivo is not (MotivoAnomaliaDfe.Nenhum or MotivoAnomaliaDfe.NaoClassificada), "Anomalia sem motivo.");
        return new(default, motivo, leiaute, ehNff: false, excluiRaizComZeros: false);
    }
}
