namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>Como um valor foi tratado; o relatório conta cada desfecho.</summary>
internal enum Desfecho
{
    /// <summary>Trocado pelo pseudônimo.</summary>
    Pseudonimizado,

    /// <summary>DV errado: base pseudonimizada e deslocamento do DV mantido.</summary>
    DvInvalidoPreservado,

    /// <summary>
    /// Base excluída da permutação (repdígito do CPF, raiz zero do CNPJ): base e DV ficam como vieram, com os zeros à
    /// esquerda completados. A regra vale com qualquer DV; um DV errado só chega aqui com a política de preservar. No
    /// <c>id.permutar</c>, são o zero e os negativos, que ficam fora do domínio e voltam como vieram. Na chave de DF-e,
    /// quando emitente, número e código ficam todos como vieram (as ocorrências dizem por quê).
    /// </summary>
    PontoFixo,

    /// <summary>DV errado gravado como nulo pela política de entrada inválida.</summary>
    AnuladoDvInvalido,

    /// <summary>Malformado e gravado como nulo pela política de entrada malformada.</summary>
    AnuladoMalformado,

    /// <summary>Malformado e copiado em claro pela política de entrada malformada.</summary>
    CopiadoEmClaro,

    /// <summary>Número de serviço do telefone (0800 e afins, 3003, 4004): não é de pessoa e volta como veio.</summary>
    ServicoPreservado,

    /// <summary>Texto vazio ou só com espaços (<c>char(n)</c> em branco): não há dado a tratar e ele volta como veio.</summary>
    Vazio,

    /// <summary>
    /// Valor de preenchimento só com zeros, sem dado pessoal: a chave de DF-e com as 44 posições em zero, o CEP só com
    /// zeros e o telefone só com zeros. Volta como veio, com prefixo, máscara, separadores e preenchimento, antes da
    /// política de entrada malformada.
    /// </summary>
    Zerado,
}
