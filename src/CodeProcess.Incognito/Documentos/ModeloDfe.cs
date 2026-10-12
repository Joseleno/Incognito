namespace CodeProcess.Incognito.Documentos;

/// <summary>Modelos de DF-e com chave de acesso de 44 posições; o valor é o código das posições 21–22.</summary>
internal enum ModeloDfe : byte
{
    /// <summary>NF-e.</summary>
    Nfe = 55,

    /// <summary>CT-e e CT-e Simplificado.</summary>
    Cte = 57,

    /// <summary>MDF-e.</summary>
    Mdfe = 58,

    /// <summary>NFCom.</summary>
    Nfcom = 62,

    /// <summary>BP-e, BP-e TM e BP-e TA.</summary>
    Bpe = 63,

    /// <summary>GTV-e.</summary>
    Gtve = 64,

    /// <summary>NFC-e.</summary>
    Nfce = 65,

    /// <summary>NF3e.</summary>
    Nf3e = 66,

    /// <summary>CT-e OS.</summary>
    CteOs = 67,
}
