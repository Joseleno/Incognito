using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// A regra efetiva de uma coluna: a que a execução aplica, depois de resolver domínios, versões e políticas e de propagar
/// pelas chaves estrangeiras e pela herança. Pode diferir do que o <c>incognito.yaml</c> escreveu.
/// </summary>
public sealed class RegraColuna
{
    internal RegraColuna(
        TipoRegra tipo,
        string? transformador,
        int? versao,
        DominioPseudonimo? dominio,
        Injetividade? injetividade,
        PoliticaEntradaInvalida? entradaInvalida,
        PoliticaEntradaMalformada? entradaMalformada,
        string? derivarDe,
        OrigemRegra origem,
        PropagacaoRegra? propagacao,
        NomeColuna? propagadaDe,
        PosicaoConfiguracao? posicao,
        long? tamanhoDominio = null)
    {
        var transformar = tipo == TipoRegra.Transformar;
        if (transformar != (transformador is not null) || transformar != versao.HasValue || transformar != injetividade.HasValue)
        {
            throw new ArgumentException("Transformador, versão e injetividade existem só, e sempre, na regra que transforma.", nameof(tipo));
        }

        if (dominio.HasValue && (!transformar || dominio.Value == default))
        {
            throw new ArgumentException("Só a regra que transforma tem domínio, e o default não é um domínio.", nameof(dominio));
        }

        if (propagacao.HasValue != (propagadaDe is not null))
        {
            throw new ArgumentException("A propagação e a coluna de onde ela veio existem juntas.", nameof(propagacao));
        }

        if (tamanhoDominio.HasValue && (!dominio.HasValue || tamanhoDominio <= 0))
        {
            throw new ArgumentException("Só a regra que transforma com domínio tem o tamanho do domínio, e ele é positivo.", nameof(tamanhoDominio));
        }

        if (transformador is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(transformador);
        }

        if (versao is { } numero)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(numero, 1, nameof(versao));
        }

        if (derivarDe is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(derivarDe);
        }

        Tipo = tipo;
        Transformador = transformador;
        Versao = versao;
        Dominio = dominio;
        Injetividade = injetividade;
        EntradaInvalida = entradaInvalida;
        EntradaMalformada = entradaMalformada;
        DerivarDe = derivarDe;
        Origem = origem;
        Propagacao = propagacao;
        PropagadaDe = propagadaDe;
        Posicao = posicao;
        TamanhoDominio = tamanhoDominio;
    }

    /// <summary>Se a coluna é mantida ou transformada.</summary>
    public TipoRegra Tipo { get; }

    /// <summary>O nome do transformador, como <c>br.cpf</c>; nulo em <see cref="TipoRegra.Manter"/>.</summary>
    public string? Transformador { get; }

    /// <summary>A versão do transformador, a partir de 1; nula em <see cref="TipoRegra.Manter"/>.</summary>
    public int? Versao { get; }

    /// <summary>
    /// O domínio de pseudônimos resolvido, com a versão; nulo em <see cref="TipoRegra.Manter"/> e no transformador que não
    /// usa domínio.
    /// </summary>
    public DominioPseudonimo? Dominio { get; }

    /// <summary>
    /// A injetividade efetiva: a que o plano aceitou para esta coluna, que pode ser mais fraca que a registrada pelo
    /// transformador (por exemplo, numa colação que não distingue valores que ele distingue); nula em
    /// <see cref="TipoRegra.Manter"/>.
    /// </summary>
    public Injetividade? Injetividade { get; }

    /// <summary>O que fazer com a entrada que tem o formato certo e o dígito verificador errado; nulo quando não se aplica.</summary>
    public PoliticaEntradaInvalida? EntradaInvalida { get; }

    /// <summary>O que fazer com a entrada fora do formato; nulo quando não se aplica.</summary>
    public PoliticaEntradaMalformada? EntradaMalformada { get; }

    /// <summary>
    /// A coluna da mesma tabela de onde o transformador tira a entidade (<c>derivarDe</c>), pelo nome no catálogo da
    /// origem; nula quando não há. Texto do catálogo, sem escape.
    /// </summary>
    public string? DerivarDe { get; }

    /// <summary>De onde veio a regra; numa regra propagada, de onde veio a regra da coluna de origem.</summary>
    public OrigemRegra Origem { get; }

    /// <summary>Por onde a regra chegou a esta coluna; nula quando a regra foi escrita para ela.</summary>
    public PropagacaoRegra? Propagacao { get; }

    /// <summary>
    /// A coluna com regra escrita mais próxima de onde esta se propagou (no empate, a primeira em ordem ordinal); nula
    /// junto com <see cref="Propagacao"/>.
    /// </summary>
    public NomeColuna? PropagadaDe { get; }

    /// <summary>Onde, no <c>incognito.yaml</c>, a regra foi escrita (na propagada, a da coluna de origem); nula quando não foi.</summary>
    public PosicaoConfiguracao? Posicao { get; }

    /// <summary>
    /// O tamanho do domínio (N) numa coluna do <c>id.permutar</c>: o pseudônimo vai de 1 a N − 1. Nulo nas outras regras.
    /// Vem dos dados da origem no <c>planejar</c> (a menor potência de 10, a partir de 10⁴, acima de dez vezes o maior valor
    /// da chave primária) e pode mudar entre execuções; o maior valor lido nunca é publicado.
    /// </summary>
    public long? TamanhoDominio { get; }
}
