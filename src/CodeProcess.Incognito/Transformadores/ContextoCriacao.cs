namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// O que a fábrica sabe para criar o transformador de uma coluna: a regra da configuração, o tipo, o comprimento, a faixa,
/// a precisão e a colação da coluna e o tipo da coluna de <c>derivarDe</c>. O motor monta um a cada criação, pela coluna
/// do destino; o construtor é público para testes de fábricas próprias.
/// </summary>
/// <remarks>
/// Um contexto serve a uma fábrica só: a primeira que o usa no <see cref="FabricaTransformador.Criar"/> fica com ele, e
/// outra fábrica que o receba é recusada. Assim uma fábrica de fora não repassa o contexto a uma embutida para obter um
/// transformador com a chave da execução.
/// </remarks>
public sealed class ContextoCriacao
{
    private readonly int? _comprimentoMaximo;
    private readonly int? _precisao;
    private readonly TipoColuna? _tipoReferencia;
    private FabricaTransformador? _fabrica;

    /// <summary>Cria o contexto.</summary>
    /// <param name="regra">A regra da configuração.</param>
    /// <param name="tipo">O tipo da coluna.</param>
    /// <param name="sessao">A sessão da execução, dona das instâncias que os transformadores embutidos usam.</param>
    /// <exception cref="ArgumentNullException">Regra ou sessão nulas.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Tipo fora do enum.</exception>
    public ContextoCriacao(RegraTransformacao regra, TipoColuna tipo, SessaoTransformacao sessao)
    {
        ArgumentNullException.ThrowIfNull(regra);
        ArgumentNullException.ThrowIfNull(sessao);
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de coluna desconhecido.");
        }

        Regra = regra;
        Tipo = tipo;
        Sessao = sessao;
    }

    /// <summary>A regra da configuração.</summary>
    public RegraTransformacao Regra { get; }

    /// <summary>O tipo da coluna, que fixa a representação do valor (<see cref="TipoColuna"/>).</summary>
    public TipoColuna Tipo { get; }

    /// <summary>
    /// O comprimento máximo da coluna em unidades UTF-16 que cabem com certeza (o <c>planejar</c> converte do limite que
    /// o dialeto lê, na unidade do banco, de forma conservadora: o <c>varchar</c> do SQL Server conta bytes); numa coluna
    /// <see cref="TipoColuna.Binario"/>, em bytes. Nulo quando não há limite.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Na atribuição: zero ou negativo.</exception>
    public int? ComprimentoMaximo
    {
        get => _comprimentoMaximo;
        init => _comprimentoMaximo = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "O comprimento máximo precisa ser positivo.");
    }

    /// <summary>
    /// O menor valor que a coluna aceita, numa coluna <see cref="TipoColuna.Inteiro"/> (a faixa do tipo, não a de um
    /// <c>CHECK</c>); nulo quando não informado. O <see cref="FabricaTransformador.Criar"/> recusa o contexto com o
    /// menor valor igual ou acima do maior.
    /// </summary>
    public long? MenorInteiro { get; init; }

    /// <summary>
    /// O maior valor que a coluna aceita, numa coluna <see cref="TipoColuna.Inteiro"/>; nulo quando não informado. As
    /// fábricas embutidas que aceitam <see cref="TipoColuna.Inteiro"/> (<c>br.cpf</c>, <c>br.cnpj</c>, <c>br.cep</c> e
    /// <c>br.telefone</c>) o exigem: sem ele, ou com ele abaixo do maior pseudônimo, o <c>Criar</c> lança
    /// <see cref="RegraInvalidaException"/>.
    /// </summary>
    public long? MaiorInteiro { get; init; }

    /// <summary>
    /// A precisão de uma coluna <see cref="TipoColuna.Decimal"/> (dígitos ao todo); nula quando não informada.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Na atribuição: zero ou negativa.</exception>
    public int? Precisao
    {
        get => _precisao;
        init => _precisao = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "A precisão precisa ser positiva.");
    }

    /// <summary>
    /// A escala de uma coluna <see cref="TipoColuna.Decimal"/> (dígitos depois da vírgula); nula quando não informada.
    /// Pode ser negativa ou passar da precisão (o PostgreSQL aceita <c>numeric(5,-2)</c> e <c>numeric(2,5)</c>): a fábrica
    /// decide se atende. O <see cref="FabricaTransformador.Criar"/> recusa a escala sem a precisão.
    /// </summary>
    public int? Escala { get; init; }

    /// <summary>
    /// A colação da coluna, como o banco a nomeia; nula quando não se aplica. Só o nome: ele não descreve como a colação
    /// compara.
    /// </summary>
    public string? Colacao { get; init; }

    /// <summary>O tipo da coluna de <c>derivarDe</c>; nulo quando a regra não tem <c>derivarDe</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Na atribuição: valor fora do enum.</exception>
    public TipoColuna? TipoReferencia
    {
        get => _tipoReferencia;
        init => _tipoReferencia = value is null || Enum.IsDefined(value.Value) ? value : throw new ArgumentOutOfRangeException(nameof(value), "Tipo de coluna desconhecido.");
    }

    /// <summary>O ambiente das derivações.</summary>
    public string Ambiente => Sessao.Ambiente;

    // Só o núcleo a enxerga: uma fábrica de fora não descarta a sessão nem cria instâncias por ela.
    internal SessaoTransformacao Sessao { get; }

    /// <summary>Reserva o contexto para a fábrica; falso se outra já o usou.</summary>
    internal bool Reservar(FabricaTransformador fabrica)
    {
        var dona = Interlocked.CompareExchange(ref _fabrica, fabrica, null);
        return dona is null || ReferenceEquals(dona, fabrica);
    }
}
