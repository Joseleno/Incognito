using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// Base das fábricas embutidas, todas na versão 1: parâmetros aceitos com o padrão e a forma canônica, uso do
/// <c>derivarDe</c>, comprimento mínimo da coluna, a identidade das instâncias na sessão e os domínios citados por
/// outras fábricas.
/// </summary>
internal abstract class FabricaEmbutida : FabricaTransformador
{
    private readonly FrozenSet<TipoColuna> _tipos;
    private readonly string _dominioPadrao;

    protected FabricaEmbutida(string nome, string dominioPadrao, Injetividade injetividade, params TipoColuna[] tipos)
    {
        Nome = nome;
        _dominioPadrao = dominioPadrao;
        Injetividade = injetividade;
        _tipos = tipos.ToFrozenSet();
    }

    /// <summary>Se a regra usa <c>derivarDe</c>.</summary>
    protected enum UsoDoDerivarDe
    {
        /// <summary>A regra com <c>derivarDe</c> é recusada.</summary>
        Recusado,

        /// <summary>A coluna sem <c>derivarDe</c> é recusada.</summary>
        Exigido,
    }

    public sealed override string Nome { get; }

    public sealed override int Versao => 1;

    public sealed override Injetividade Injetividade { get; }

    public sealed override IReadOnlySet<TipoColuna> TiposSuportados => _tipos;

    private protected sealed override string DominioPadraoEmbutido => _dominioPadrao;

    /// <summary>Os parâmetros aceitos, com o padrão e a conferência da forma canônica.</summary>
    protected virtual FrozenDictionary<string, ParametroEmbutido> ParametrosAceitos => FrozenDictionary<string, ParametroEmbutido>.Empty;

    /// <summary>Se a regra usa <c>derivarDe</c>.</summary>
    protected virtual UsoDoDerivarDe DerivarDe => UsoDoDerivarDe.Recusado;

    /// <summary>Se o transformador tem DV e usa a política <c>entradaInvalida</c> (CPF e CNPJ); os outros a ignoram.</summary>
    protected virtual bool UsaEntradaInvalida => false;

    public sealed override void Validar(RegraTransformacao regra)
    {
        ArgumentNullException.ThrowIfNull(regra);
        foreach (var (nome, valor) in regra.Parametros.OrderBy(parametro => parametro.Key, StringComparer.Ordinal))
        {
            if (!ParametrosAceitos.TryGetValue(nome, out var parametro))
            {
                throw new RegraInvalidaException($"Parâmetro desconhecido para {Nome}@{Versao} ({Aceitos()}).", nome);
            }

            if (!parametro.Valido(valor))
            {
                throw new RegraInvalidaException($"Parâmetro de {Nome}@{Versao} fora da forma: {parametro.Forma}.", nome);
            }
        }

        if (DerivarDe == UsoDoDerivarDe.Recusado && regra.TemDerivarDe == true)
        {
            throw new RegraInvalidaException($"O transformador {Nome}@{Versao} não usa 'derivarDe'.");
        }

        if (DerivarDe == UsoDoDerivarDe.Exigido && regra.TemDerivarDe == false)
        {
            throw new RegraInvalidaException(
                $"O transformador {Nome}@{Versao} precisa de 'derivarDe': a coluna da mesma tabela de que o valor é derivado (numa coluna de domínio, {{ dominio: nome, derivarDe: coluna }}).");
        }

        ValidarRegra(regra);
    }

    public sealed override IReadOnlyDictionary<string, string> Normalizar(RegraTransformacao regra)
    {
        Validar(regra);
        return ParametrosAceitos.ToFrozenDictionary(
            parametro => parametro.Key,
            parametro => regra.Parametros.TryGetValue(parametro.Key, out var valor) ? parametro.Value.Canonico(valor) : parametro.Value.Padrao,
            StringComparer.Ordinal);
    }

    /// <summary>Confere o que é próprio da fábrica, depois dos parâmetros e do <c>derivarDe</c>.</summary>
    /// <exception cref="RegraInvalidaException">Regra recusada.</exception>
    protected virtual void ValidarRegra(RegraTransformacao regra)
    {
    }

    /// <summary>
    /// A regra do domínio citado num parâmetro (<c>dominioCpf</c>), da tabela de domínios da sessão: precisa ser do
    /// transformador esperado, na versão 1.
    /// </summary>
    /// <exception cref="RegraInvalidaException">Domínio fora da tabela, de outro transformador ou de outra versão.</exception>
    protected static RegraTransformacao DominioCitado(SessaoTransformacao sessao, IReadOnlyDictionary<string, string> normalizados, string parametro, string transformador)
    {
        if (!sessao.TentarObterDominio(normalizados[parametro], out var definicao))
        {
            throw new RegraInvalidaException($"O domínio citado em '{parametro}' não está na tabela de domínios da sessão.", parametro);
        }

        if (!string.Equals(definicao.Transformador, transformador, StringComparison.Ordinal) || definicao.Regra.Dominio.Versao != 1)
        {
            throw new RegraInvalidaException($"O domínio citado em '{parametro}' precisa ser do {transformador}@1.", parametro);
        }

        return definicao.Regra;
    }

    /// <summary>Recusa a coluna de texto que não comporta o menor pseudônimo.</summary>
    protected static void ExigirComprimento(ContextoCriacao contexto, int minimo, string motivo)
    {
        if (contexto.Tipo == TipoColuna.Texto && contexto.ComprimentoMaximo < minimo)
        {
            throw new RegraInvalidaException($"A coluna comporta menos de {minimo} caracteres: {motivo}.");
        }
    }

    /// <summary>
    /// Tudo o que muda a saída de uma instância: transformador, domínio, versão, parâmetros normalizados e as políticas
    /// que ele usa. Duas colunas com a mesma identidade usam a mesma instância da sessão.
    /// </summary>
    protected string Identidade(RegraTransformacao regra) => Identidade(regra, Normalizar(regra));

    /// <inheritdoc cref="Identidade(RegraTransformacao)"/>
    protected string Identidade(RegraTransformacao regra, IReadOnlyDictionary<string, string> normalizados)
    {
        var invalida = UsaEntradaInvalida ? regra.EntradaInvalida.ToString() : "-";
        var identidade = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{Nome}@{Versao}|{regra.Dominio}|{invalida}|{regra.EntradaMalformada}");
        foreach (var (nome, valor) in normalizados.OrderBy(parametro => parametro.Key, StringComparer.Ordinal))
        {
            identidade.Append('|').Append(nome).Append('=').Append(valor);
        }

        return identidade.ToString();
    }

    /// <summary>A exceção de um valor fora da representação do tipo da coluna, sem o valor.</summary>
    internal static ArgumentException ForaDoTipo(TipoColuna tipo, string parametro) =>
        new($"O valor não está na representação do tipo {tipo}; o valor não é exibido.", parametro);

    private string Aceitos() =>
        ParametrosAceitos.Count == 0 ? "não tem parâmetros" : "aceita " + string.Join(", ", ParametrosAceitos.Keys.Order(StringComparer.Ordinal));
}

/// <summary>
/// Um parâmetro de fábrica embutida: o padrão, a forma aceita, a conferência e a forma canônica de um valor aceito (o
/// próprio valor, quando não há outra).
/// </summary>
internal sealed record ParametroEmbutido(string Padrao, string Forma, Func<string, bool> Valido, Func<string, string>? Normalizacao = null)
{
    /// <summary>Lógico, só <c>true</c> ou <c>false</c>.</summary>
    internal static ParametroEmbutido Logico(bool padrao) =>
        new(padrao ? "true" : "false", "true ou false", valor => valor is "true" or "false");

    /// <summary>O nome de outro domínio da configuração, na regra dos nomes.</summary>
    internal static ParametroEmbutido Dominio(string padrao) =>
        new(padrao, "nome de domínio, com letras minúsculas e dígitos e '.', '_' ou '-' entre eles", Subchaves.EhNomeValido);

    /// <summary>A forma canônica de um valor já aceito.</summary>
    internal string Canonico(string valor) => Normalizacao?.Invoke(valor) ?? valor;
}
