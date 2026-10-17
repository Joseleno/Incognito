using System.Diagnostics;
using System.Runtime.CompilerServices;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito;

/// <summary>
/// Pseudonimização direta de documentos brasileiros, sem banco: CPF, CNPJ, telefone, e-mail, chave de acesso de DF-e e
/// chave Pix, nos domínios padrão (<c>cpf</c>, <c>cnpj</c>, <c>telefone</c>, <c>email</c>, <c>chave-pix</c> e, no
/// número e no código da chave de DF-e, <c>dfe-numero</c>), na versão 1 em toda a série 1.x. Mesma chave, mesmo
/// ambiente e mesmo valor dão o mesmo pseudônimo em qualquer processo, igual ao de uma configuração com esses domínios
/// e esse <c>ambiente</c>; valores diferentes nunca colidem (CPF, CNPJ, telefone e chave de DF-e) ou colidem com chance
/// desprezível (e-mail e chave Pix aleatória).
/// </summary>
/// <remarks>
/// <para>
/// O resultado continua sendo dado pessoal: é pseudonimização, e quem tem a chave reverte todos os valores. Guarde a
/// chave separada, nunca no ambiente de destino, e prefira uma chave por ambiente. A própria instância é tão sensível
/// quanto a chave: quem pode chamá-la monta a tabela de todos os CPFs e reverte qualquer pseudônimo. Nunca a exponha
/// (como serviço, endpoint ou fila) a quem recebe os dados pseudonimizados. Ela guarda as chaves derivadas até o
/// <see cref="Dispose"/>; descarte-a assim que terminar.
/// </para>
/// <para>
/// A chave continua de quem a criou e precisa viver enquanto esta instância for usada: com ela descartada, os métodos
/// lançam <see cref="ObjectDisposedException"/>. O ambiente é obrigatório: com a mesma chave, ambientes diferentes dão
/// pseudônimos sem relação.
/// </para>
/// <para>
/// Formas e políticas fixas: cada método aceita as formas do tipo e devolve o pseudônimo na mesma forma, com os espaços
/// à direita preservados (CPF e CNPJ sem os zeros à esquerda voltam completos, com 11 e 14 caracteres, descontados os
/// zeros acrescentados dos espaços). Valor fora da forma lança <see cref="IncognitoException"/> com o código
/// <c>INC1003</c>, sem o valor na mensagem (não registre a entrada ao tratar a exceção). DV errado é mantido pelo
/// deslocamento: o pseudônimo continua inválido. Texto vazio ou só com espaços, CPF de base repdígito, CNPJ de raiz zero,
/// número de serviço do telefone, telefone zerado (como <c>(11) 0000-0000</c>) e a chave de 44 zeros voltam como
/// vieram. Nunca devolve nulo. Um erro interno (o
/// limite defensivo da permutação, <c>INC1002</c>, ou um invariante quebrado) só acontece com chance desprezível.
/// </para>
/// <para>
/// Válido não quer dizer fictício: o CPF, o CNPJ (inclusive de microempreendedor, que é pessoa natural), o telefone e a
/// chave de DF-e gerados podem pertencer a terceiros reais. Não envie mensagens a eles nem os consulte em bureaus ou nos
/// portais das SEFAZ.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O <see cref="Dispose"/> vem depois de todo uso; é idempotente, e
/// depois dele os métodos lançam <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("Pseudonimizador({Ambiente})")]
public sealed class Pseudonimizador : IDisposable
{
    private const int Versao = 1;
    private const string MensagemCpf =
        "Valor fora da forma de CPF (11 dígitos, com ou sem a máscara 000.000.000-00, ou 9 ou 10 dígitos sem os zeros à esquerda). O valor não é exibido.";
    private const string MensagemCnpj =
        "Valor fora da forma de CNPJ (14 caracteres, com ou sem a máscara 00.000.000/0000-00, ou 12 ou 13 dígitos sem os zeros à esquerda; raiz e ordem com dígitos ou letras maiúsculas, DV com dígitos). O valor não é exibido.";
    private const string MensagemTelefone =
        "Valor fora da forma de telefone (com ou sem +55 e DDD; número colado ou em grupos de 4+4, 5+4 ou 1+4+4). O valor não é exibido.";
    private const string MensagemEmail =
        "Valor fora da forma de e-mail (uma única @, parte local de 1 a 64 caracteres, domínio com ponto, até 254 caracteres). O valor não é exibido.";
    private const string MensagemChaveDfe =
        "Valor fora da forma de chave de acesso de DF-e (44 posições, com o prefixo do Id do modelo ou em 11 grupos de 4), de modelo fora dos nove ou sem emitente classificável pelas regras de série, tipo de emissão e data. O valor não é exibido.";
    private const string MensagemChavePix =
        "Valor fora das formas de chave Pix (CPF, CNPJ, e-mail, telefone com +55 ou chave aleatória 8-4-4-4-12). O valor não é exibido.";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly ChaveIncognito _chave;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorCpf _cpf;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorCnpj _cnpj;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorTelefone _telefone;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorEmail _email;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorChavePix _chavePix;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PseudonimizadorChaveDfe _chaveDfe;

    private int _descartado;

    /// <summary>Prepara a pseudonimização com os domínios padrão.</summary>
    /// <param name="chave">A chave do Incognito, que continua de quem a criou e precisa viver enquanto esta instância for usada.</param>
    /// <param name="ambiente">
    /// O ambiente de destino (por exemplo, <c>homologacao</c>): letras minúsculas e dígitos, com <c>.</c>, <c>_</c> ou
    /// <c>-</c> entre eles, até 64 caracteres. É o mesmo <c>ambiente</c> da configuração do motor.
    /// </param>
    /// <exception cref="ArgumentNullException">Chave ou ambiente nulos.</exception>
    /// <exception cref="ArgumentException">Ambiente fora da regra dos nomes; a mensagem não traz o valor.</exception>
    /// <exception cref="ObjectDisposedException">Chave já descartada.</exception>
    public Pseudonimizador(ChaveIncognito chave, string ambiente)
    {
        // Fora do try: uma falha de argumento não deriva nada.
        ArgumentNullException.ThrowIfNull(chave);
        Subchaves.ValidarNome(ambiente, nameof(ambiente));

        PseudonimizadorCpf? cpf = null;
        PseudonimizadorCnpj? cnpj = null;
        PseudonimizadorTelefone? telefone = null;
        PseudonimizadorEmail? email = null;
        PseudonimizadorChavePix? chavePix = null;
        try
        {
            cpf = new PseudonimizadorCpf(chave, "cpf", Versao, ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            cnpj = new PseudonimizadorCnpj(chave, "cnpj", Versao, ambiente, permutarOrdem: false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            telefone = new PseudonimizadorTelefone(chave, "telefone", Versao, ambiente, PoliticaEntradaMalformada.Falhar);
            email = new PseudonimizadorEmail(chave, "email", Versao, ambiente, PoliticaEntradaMalformada.Falhar);
            chavePix = new PseudonimizadorChavePix(chave, "chave-pix", Versao, ambiente, cpf, cnpj, email, telefone, PoliticaEntradaMalformada.Falhar);
            _chaveDfe = new PseudonimizadorChaveDfe(chave, Versao, ambiente, cpf, cnpj, modelos: null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        }
        catch
        {
            chavePix?.Dispose();
            email?.Dispose();
            telefone?.Dispose();
            cnpj?.Dispose();
            cpf?.Dispose();
            throw;
        }

        _chave = chave;
        _cpf = cpf;
        _cnpj = cnpj;
        _telefone = telefone;
        _email = email;
        _chavePix = chavePix;
        Ambiente = ambiente;
    }

    /// <summary>O ambiente das derivações.</summary>
    public string Ambiente { get; }

    /// <summary>Pseudonimiza um CPF no domínio <c>cpf@1</c>.</summary>
    /// <param name="cpf">O CPF: 11 dígitos, com ou sem a máscara <c>000.000.000-00</c>, ou 9 ou 10 dígitos sem os zeros à esquerda.</param>
    /// <returns>O pseudônimo, um CPF válido (ou inválido, se o DV veio errado) na mesma forma, com 11 dígitos.</returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora da forma de CPF.</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string Cpf(string cpf)
    {
        Conferir(cpf);
        try
        {
            _cpf.Pseudonimizar(cpf, out var saida);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemCpf);
        }
    }

    /// <summary>Pseudonimiza um CNPJ, numérico ou alfanumérico, no domínio <c>cnpj@1</c>, com a ordem preservada.</summary>
    /// <param name="cnpj">O CNPJ: 14 caracteres, com ou sem a máscara <c>00.000.000/0000-00</c>, ou 12 ou 13 dígitos sem os zeros à esquerda.</param>
    /// <returns>O pseudônimo, com a raiz trocada e a ordem preservada (matriz e filiais continuam juntas), na mesma forma, com 14 caracteres.</returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora da forma de CNPJ (inclusive letra minúscula).</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string Cnpj(string cnpj)
    {
        Conferir(cnpj);
        try
        {
            _cnpj.Pseudonimizar(cnpj, out var saida);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemCnpj);
        }
    }

    /// <summary>Pseudonimiza um telefone no domínio <c>telefone@1</c>, com o mesmo DDD e a mesma classe (celular ou fixo).</summary>
    /// <param name="telefone">O telefone, com ou sem <c>+55</c>, DDD e separadores.</param>
    /// <returns>
    /// O pseudônimo com a mesma formatação; os números de serviço (0800, 0300, 0303, 0500, 0900, 3003 e 4004) e o
    /// telefone zerado voltam como vieram.
    /// </returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora da forma de telefone.</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string Telefone(string telefone)
    {
        Conferir(telefone);
        try
        {
            _telefone.Pseudonimizar(telefone, out var saida);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemTelefone);
        }
    }

    /// <summary>Pseudonimiza um e-mail no domínio <c>email@1</c>.</summary>
    /// <param name="email">O e-mail; maiúsculas e minúsculas dão o mesmo pseudônimo.</param>
    /// <returns>Um endereço em <c>example.com</c>, que não recebe mensagens, sempre com 32 caracteres.</returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora da forma de e-mail.</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string Email(string email)
    {
        Conferir(email);
        try
        {
            _email.Pseudonimizar(email, out var saida);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemEmail);
        }
    }

    /// <summary>
    /// Pseudonimiza uma chave de acesso de DF-e de 44 posições (NF-e, NFC-e, CT-e, CT-e OS, GTV-e, MDF-e, BP-e, NF3e e
    /// NFCom): o emitente vai aos domínios <c>cpf</c> ou <c>cnpj</c>, o número e o código ao domínio <c>dfe-numero</c>,
    /// e cUF, AAMM, modelo, série e tpEmis ficam.
    /// </summary>
    /// <remarks>
    /// O emitente sai igual a <see cref="Cpf"/> ou <see cref="Cnpj"/>, salvo no CT-e e no MDF-e, em que um CNPJ de raiz
    /// <c>000xxxxx</c> é lido como CPF e cerca de 0,1% dos CNPJs saem diferentes, para a saída não ser lida como CPF.
    /// Podem ficar como vieram: o número 0, o sequencial 0 e o dia de data inválida da Nota Fiscal Fácil, o código
    /// numérico proibido e, quando tudo isso coincide, a chave inteira.
    /// </remarks>
    /// <param name="chaveDeAcesso">As 44 posições, com o prefixo do <c>Id</c> do modelo ou em 11 grupos de 4.</param>
    /// <returns>A chave pseudonimizada na mesma forma, com o cDV recalculado (errado, se veio errado).</returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora da forma, de outro modelo ou sem emitente classificável.</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string ChaveDfe(string chaveDeAcesso)
    {
        Conferir(chaveDeAcesso);
        try
        {
            _chaveDfe.Pseudonimizar(chaveDeAcesso, out var saida, out _);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemChaveDfe);
        }
    }

    /// <summary>
    /// Pseudonimiza uma chave Pix do mesmo tipo: CPF, CNPJ, e-mail e telefone saem iguais aos métodos correspondentes; a
    /// chave aleatória sai outra chave aleatória no domínio <c>chave-pix@1</c>.
    /// </summary>
    /// <remarks>
    /// A igualdade com os outros métodos vale nas formas do DICT: o telefone com <c>+55</c>, e 11 dígitos sempre como CPF
    /// (um telefone de 11 dígitos sem <c>+55</c> é lido como CPF).
    /// </remarks>
    /// <param name="chavePix">A chave Pix, na forma do DICT.</param>
    /// <returns>A chave pseudonimizada do mesmo tipo.</returns>
    /// <exception cref="ArgumentNullException">Valor nulo.</exception>
    /// <exception cref="IncognitoException"><c>INC1003</c>: fora das formas de chave Pix.</exception>
    /// <exception cref="ObjectDisposedException">Esta instância ou a chave já descartada.</exception>
    public string ChavePix(string chavePix)
    {
        Conferir(chavePix);
        try
        {
            _chavePix.Pseudonimizar(chavePix, out var saida);
            return Saida(saida);
        }
        catch (IncognitoException excecao) when (excecao.Codigo == Politicas.CodigoMalformado)
        {
            throw new IncognitoException(Politicas.CodigoMalformado, MensagemChavePix);
        }
    }

    /// <summary>
    /// Descarta as chaves derivadas, depois de todo uso; idempotente. A <see cref="ChaveIncognito"/> continua de quem a
    /// criou.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _descartado, 1) != 0)
        {
            return;
        }

        // As composições antes das instâncias que elas usam; um descarte que falhe não impede os seguintes.
        try
        {
            _chaveDfe.Dispose();
        }
        finally
        {
            try
            {
                _chavePix.Dispose();
            }
            finally
            {
                try
                {
                    _email.Dispose();
                }
                finally
                {
                    try
                    {
                        _telefone.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            _cnpj.Dispose();
                        }
                        finally
                        {
                            _cpf.Dispose();
                        }
                    }
                }
            }
        }
    }

    // Com as políticas fixas (malformado falha, DV errado é mantido), nenhum caminho anula o valor.
    private static string Saida(string? saida) => saida ?? throw new UnreachableException("A fachada anulou um valor.");

    private void Conferir(string valor, [CallerArgumentExpression(nameof(valor))] string parametro = "")
    {
        ArgumentNullException.ThrowIfNull(valor, parametro);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        _chave.LancarSeDescartada();
    }
}
