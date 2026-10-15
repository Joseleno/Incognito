using System.Diagnostics;

namespace CodeProcess.Incognito.Normas;

/// <summary>Uma referência normativa considerada pelo projeto e a data da última revisão dela.</summary>
/// <remarks>
/// Citar uma referência não indica conformidade com ela nem constitui aconselhamento jurídico. Nenhum transformador lê o
/// catálogo: rever uma referência nunca muda a saída. Quando uma mudança normativa pede outra saída, nasce uma nova
/// versão do transformador.
/// </remarks>
[DebuggerDisplay("{Identificador,nq} ({RevisadaEm})")]
public sealed class ReferenciaNormativa
{
    internal ReferenciaNormativa(
        string identificador,
        string titulo,
        string emissor,
        string? dispositivos,
        SituacaoNormativa situacao,
        string observacao,
        string motivo,
        Uri fonte,
        DateOnly revisadaEm)
    {
        Identificador = identificador;
        Titulo = titulo;
        Emissor = emissor;
        Dispositivos = dispositivos;
        Situacao = situacao;
        Observacao = observacao;
        Motivo = motivo;
        Fonte = fonte;
        RevisadaEm = revisadaEm;
    }

    /// <summary>
    /// Identificador em minúsculas com hífens (por exemplo, <c>lgpd</c>): não muda entre versões do pacote, e o de uma
    /// referência retirada não é reaproveitado.
    /// </summary>
    public string Identificador { get; }

    /// <summary>A citação da referência, com número, data ou versão quando o documento os tem.</summary>
    public string Titulo { get; }

    /// <summary>
    /// Onde conferir a referência: o documento ou a página oficial que o publica. Quando ela não pôde ser aberta na
    /// revisão, partes da referência foram confirmadas em fonte secundária. Para exibir, use <see cref="Uri.AbsoluteUri"/>.
    /// </summary>
    public Uri Fonte { get; }

    /// <summary>A data (civil, de Brasília, sem hora) da última revisão da referência pelo projeto.</summary>
    public DateOnly RevisadaEm { get; }

    /// <summary>Quem emitiu o documento, pelo nome da época dele.</summary>
    internal string Emissor { get; }

    /// <summary>Os artigos, itens ou controles que o projeto usa, quando não é o documento inteiro.</summary>
    internal string? Dispositivos { get; }

    /// <summary>A situação na data da revisão.</summary>
    internal SituacaoNormativa Situacao { get; }

    /// <summary>O que a revisão viu: alterações, versões, e se a confirmação foi só em fonte secundária.</summary>
    internal string Observacao { get; }

    /// <summary>Por que o projeto considera a referência.</summary>
    internal string Motivo { get; }
}
