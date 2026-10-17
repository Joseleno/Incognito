namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Se valores diferentes saem diferentes: o <c>planejar</c> só aceita, em coluna de PK, UNIQUE, índice único ou FK,
/// transformador <see cref="Garantida"/> ou <see cref="Probabilistica"/>.
/// </summary>
public enum Injetividade
{
    /// <summary>Valores diferentes podem sair iguais (CEP, texto substituído, datas generalizadas).</summary>
    Nenhuma,

    /// <summary>Colisão com chance desprezível (e-mail, depois da normalização).</summary>
    Probabilistica,

    /// <summary>Nunca colide: é uma bijeção (CPF, CNPJ, telefone).</summary>
    Garantida,
}
