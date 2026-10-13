namespace CodeProcess.Incognito.Testes;

/// <summary>
/// O emitente de uma chave do <see cref="GeradorChaveDfe"/>, escrito nas posições 7 a 20. Os nomes são lidos pelas regras
/// dos vetores fixos: não renomear.
/// </summary>
internal enum EmitenteGerado
{
    /// <summary>CNPJ de matriz de raiz numérica, como o do <see cref="GeradorBr.Cnpj(bool, bool)"/>.</summary>
    CnpjNumerico,

    /// <summary>CNPJ de matriz de raiz alfanumérica (ao menos uma letra na raiz).</summary>
    CnpjAlfanumerico,

    /// <summary>
    /// CNPJ de matriz de raiz alfanumérica que começa com <c>000</c> (ao menos uma letra nas outras 5 posições da raiz):
    /// na regra dos zeros, a letra decide CNPJ antes dos zeros.
    /// </summary>
    CnpjAlfanumericoComZeros,

    /// <summary>CNPJ de raiz numérica e ordem com letra (filial nova de raiz antiga).</summary>
    CnpjNumericoComOrdemAlfanumerica,

    /// <summary>CNPJ de raiz e ordem alfanuméricas.</summary>
    CnpjAlfanumericoComOrdemAlfanumerica,

    /// <summary>CNPJ de raiz numérica e ordem numérica sorteada diferente de <c>0001</c> (filial).</summary>
    CnpjFilial,

    /// <summary>CNPJ de raiz numérica <c>000xxxxx</c> e ordem numérica.</summary>
    CnpjRaizComZeros,

    /// <summary>CNPJ de raiz numérica <c>000xxxxx</c> e ordem com letra.</summary>
    CnpjRaizComZerosEOrdemAlfanumerica,

    /// <summary>
    /// CNPJ de raiz <c>00000000</c> com a ordem <c>0000</c>, que não é de estabelecimento (a raiz zero é cadastrada em
    /// todas as ordens de estabelecimento).
    /// </summary>
    CnpjRaizZero,

    /// <summary><c>000</c> e um CPF de base sem repdígito.</summary>
    Cpf,

    /// <summary>
    /// <c>000</c> e um CPF de base começando com <c>00</c>, mas não com <c>00000</c> (os 14 caracteres também são um CNPJ
    /// válido).
    /// </summary>
    CpfComZeros,

    /// <summary>
    /// <c>000</c> e um CPF de base começando com <c>00000</c> (como CNPJ, raiz zero), sempre com o DV deslocado: com o DV
    /// certo, seria um CNPJ de raiz zero com ordem de estabelecimento, cadastrado.
    /// </summary>
    CpfComCincoZeros,

    /// <summary><c>000</c> e um CPF de base repdígito.</summary>
    CpfRepdigito,
}

/// <summary>
/// O número (posições 26 a 34) de uma chave do <see cref="GeradorChaveDfe"/>. Os nomes são lidos pelas regras dos vetores
/// fixos: não renomear.
/// </summary>
internal enum NumeroGerado
{
    /// <summary>De 1 a 999.999.999.</summary>
    Qualquer,

    /// <summary>O número 0.</summary>
    Zero,

    /// <summary>
    /// Partes da NFF com o mês do AAMM, dia válido nele e sequencial diferente de 0; com o mês do AAMM fora de 01 a 12, a
    /// data sai inválida.
    /// </summary>
    Nff,

    /// <summary>Partes da NFF com o mês do número fora de 01 a 12 ou o dia fora do mês.</summary>
    NffDataInvalida,

    /// <summary>Partes da NFF com dia válido e sequencial 0.</summary>
    NffSequencialZero,
}

/// <summary>
/// O código numérico de uma chave do <see cref="GeradorChaveDfe"/>. Os nomes são lidos pelas regras dos vetores fixos: não
/// renomear.
/// </summary>
internal enum CodigoGerado
{
    /// <summary>Sorteado (8 dígitos; na NF3e e na NFCom, 7 depois do <c>nSiteAutoriz</c>).</summary>
    Qualquer,

    /// <summary>Um dos 20 proibidos (só no código de 8 dígitos; no de 7, sorteado).</summary>
    Proibido,

    /// <summary>
    /// Igual ao número de entrada, que então fica abaixo de 10⁸ (só no código de 8 dígitos e com
    /// <see cref="NumeroGerado.Qualquer"/>; outra combinação é recusada).
    /// </summary>
    IgualAoNumero,
}

/// <summary>
/// O que a chave do <see cref="GeradorChaveDfe"/> tem nas posições que decidem a classificação e os caminhos do
/// <c>br.chave-dfe</c>; o resto é sorteado.
/// </summary>
/// <param name="Aamm">O AAMM, de 0000 a 9999 (o mês pode estar fora de 01 a 12).</param>
/// <param name="Modelo">O código do modelo, de 00 a 99.</param>
/// <param name="Serie">A série, de 000 a 999.</param>
/// <param name="TipoEmissao">O tpEmis, um dígito.</param>
/// <param name="Emitente">O emitente.</param>
/// <param name="Numero">O número.</param>
/// <param name="Codigo">O código numérico.</param>
/// <param name="TipoDoEmissorNff">A posição 30 nas partes da NFF da NF-e e da NFC-e.</param>
/// <param name="DeslocamentoCdv">Quanto somar ao cDV certo, de 0 a 9.</param>
/// <param name="DeslocamentoDvEmitente">Quanto somar ao DV certo do emitente, de 0 a 99.</param>
/// <param name="SiteAutorizador">A posição 36 na NF3e e na NFCom.</param>
/// <param name="Dia">
/// O dia nas partes da NFF (<see cref="NumeroGerado.Nff"/> e <see cref="NumeroGerado.NffSequencialZero"/>), de 0 a 99,
/// mesmo fora do mês; nulo para sortear um dia do mês do AAMM (mesmo com outro <paramref name="MesDoNumero"/>).
/// </param>
/// <param name="MesDoNumero">
/// O mês nas partes da NFF, de 0 a 99, mesmo diferente do mês do AAMM; nulo para o mês do AAMM.
/// </param>
/// <param name="ValorDoCodigo">
/// O código numérico (8 dígitos, ou 7 na NF3e e na NFCom), com <see cref="CodigoGerado.Qualquer"/>; nulo para sortear.
/// </param>
internal readonly record struct EspecificacaoChaveDfe(
    int Aamm,
    int Modelo,
    int Serie,
    char TipoEmissao,
    EmitenteGerado Emitente,
    NumeroGerado Numero,
    CodigoGerado Codigo,
    char TipoDoEmissorNff = '1',
    int DeslocamentoCdv = 0,
    int DeslocamentoDvEmitente = 0,
    char SiteAutorizador = '0',
    int? Dia = null,
    int? MesDoNumero = null,
    long? ValorDoCodigo = null);
