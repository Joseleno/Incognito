namespace CodeProcess.Incognito.Geracao;

/// <summary>Tipo de chave Pix gerada pelo <see cref="GeradorBr"/>.</summary>
internal enum TipoChavePix
{
    /// <summary>CPF com 11 dígitos, sem máscara.</summary>
    Cpf,

    /// <summary>CNPJ com 14 caracteres, sem máscara, numérico ou alfanumérico.</summary>
    Cnpj,

    /// <summary>E-mail em domínio reservado, em minúsculas.</summary>
    Email,

    /// <summary>Celular com <c>+55</c> e DDD.</summary>
    Telefone,

    /// <summary>Chave aleatória: UUID versão 4 em minúsculas.</summary>
    Aleatoria,
}
