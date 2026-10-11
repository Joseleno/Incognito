namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>Como um valor foi tratado; o relatório conta cada desfecho.</summary>
internal enum Desfecho
{
    /// <summary>Trocado pelo pseudônimo.</summary>
    Pseudonimizado,

    /// <summary>DV errado: base pseudonimizada e deslocamento do DV mantido.</summary>
    DvInvalidoPreservado,

    /// <summary>
    /// Base excluída da permutação (repdígito): base e DV ficam como vieram, com os zeros à esquerda completados. A
    /// regra vale com qualquer DV; um DV errado só chega aqui com a política de preservar.
    /// </summary>
    PontoFixo,

    /// <summary>DV errado gravado como nulo pela política de entrada inválida.</summary>
    AnuladoDvInvalido,

    /// <summary>Malformado e gravado como nulo pela política de entrada malformada.</summary>
    AnuladoMalformado,

    /// <summary>Malformado e copiado em claro pela política de entrada malformada.</summary>
    CopiadoEmClaro,

    /// <summary>Texto vazio ou só com espaços (<c>char(n)</c> em branco): não há dado a tratar e ele volta como veio.</summary>
    Vazio,
}
