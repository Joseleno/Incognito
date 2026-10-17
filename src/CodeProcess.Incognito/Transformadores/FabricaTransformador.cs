using System.Collections.Frozen;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Cria os transformadores de um nome e uma versão (<c>br.cpf@1</c>): confere a regra uma vez, antes do banco
/// (<see cref="Validar"/>), e cria o transformador de cada coluna conhecendo o tipo, o comprimento e a colação
/// (<see cref="Criar"/>). Classe abstrata para poder ganhar membros virtuais sem quebrar quem estende.
/// </summary>
/// <remarks>
/// <para>
/// Uma fábrica própria não recebe nada derivado da chave: um transformador próprio não é pseudonimização com chave.
/// Nunca use hash sem chave (SHA-256 de um CPF, por exemplo) para tratar dado pessoal: quem conhece a forma do dado
/// refaz a tabela inteira por força bruta.
/// </para>
/// <para>Os membros são lidos uma vez, no registro: precisam devolver sempre o mesmo valor.</para>
/// <para>
/// A mensagem de uma <see cref="RegraInvalidaException"/> lançada em <see cref="Validar"/> ou <see cref="Criar"/> é
/// mostrada no erro de configuração: ela fala da regra, e um parâmetro nunca é segredo nem dado de coluna.
/// </para>
/// </remarks>
public abstract class FabricaTransformador
{
    /// <summary>O nome: letras minúsculas e dígitos, com <c>.</c>, <c>_</c> ou <c>-</c> entre eles, até 64 caracteres.</summary>
    public abstract string Nome { get; }

    /// <summary>A versão, a partir de 1; o registro é por nome e versão.</summary>
    public abstract int Versao { get; }

    /// <summary>A injetividade dos transformadores criados.</summary>
    public abstract Injetividade Injetividade { get; }

    /// <summary>Os tipos de coluna que os transformadores aceitam.</summary>
    public abstract IReadOnlySet<TipoColuna> TiposSuportados { get; }

    /// <summary>
    /// O domínio de uma coluna com o transformador escrito direto, sem <c>dominio</c>: o nome da fábrica, salvo nas
    /// embutidas que têm o domínio da fachada (<c>cpf</c> no <c>br.cpf</c>).
    /// </summary>
    public string DominioPadrao => DominioPadraoEmbutido ?? Nome;

    /// <summary>
    /// Os parâmetros que mudam o universo de pseudônimos (<c>permutarOrdem</c> no <c>br.cnpj</c>): uma coluna sem
    /// <c>dominio</c> que os mude usaria o domínio padrão com outro universo, e a configuração é recusada. Padrão: nenhum.
    /// </summary>
    public virtual IReadOnlySet<string> ParametrosDeIdentidade => FrozenSet<string>.Empty;

    // Só as embutidas sobrescrevem: o domínio padrão de uma fábrica de fora é o nome dela, que não colide com os da fachada.
    private protected virtual string? DominioPadraoEmbutido => null;

    /// <summary>
    /// Confere a regra antes do banco: parâmetros, <c>derivarDe</c> e <c>justificativa</c>. O padrão recusa qualquer
    /// parâmetro e o <c>derivarDe</c>.
    /// </summary>
    /// <param name="regra">A regra.</param>
    /// <exception cref="ArgumentNullException">Regra nula.</exception>
    /// <exception cref="RegraInvalidaException">Regra recusada.</exception>
    public virtual void Validar(RegraTransformacao regra)
    {
        ArgumentNullException.ThrowIfNull(regra);
        if (regra.Parametros.Count > 0)
        {
            throw new RegraInvalidaException(
                $"O transformador {Nome}@{Versao} não tem parâmetros.", regra.Parametros.Keys.Order(StringComparer.Ordinal).First());
        }

        if (regra.TemDerivarDe == true)
        {
            throw new RegraInvalidaException($"O transformador {Nome}@{Versao} não usa 'derivarDe'.");
        }
    }

    /// <summary>
    /// Os parâmetros na forma canônica, com os padrões preenchidos: duas regras com a mesma forma normalizada são o
    /// mesmo universo. O padrão confere a regra e devolve os parâmetros como vieram; uma fábrica com
    /// <see cref="ParametrosDeIdentidade"/> sobrescreve para preencher o padrão deles (senão o valor padrão escrito e o
    /// omitido parecem universos diferentes).
    /// </summary>
    /// <param name="regra">A regra.</param>
    /// <returns>Os parâmetros normalizados.</returns>
    /// <exception cref="ArgumentNullException">Regra nula.</exception>
    /// <exception cref="RegraInvalidaException">Regra recusada.</exception>
    public virtual IReadOnlyDictionary<string, string> Normalizar(RegraTransformacao regra)
    {
        Validar(regra);
        return regra.Parametros;
    }

    /// <summary>
    /// Cria o transformador de uma coluna: confere a regra, a versão do domínio e o tipo da coluna, chama
    /// <see cref="CriarCore"/> e confere que o transformador criado tem o nome e a versão da fábrica.
    /// </summary>
    /// <param name="contexto">A regra e a coluna.</param>
    /// <returns>O transformador.</returns>
    /// <exception cref="ArgumentNullException">Contexto nulo.</exception>
    /// <exception cref="RegraInvalidaException">Regra recusada ou coluna que o transformador não atende.</exception>
    /// <exception cref="InvalidOperationException">
    /// O contexto já foi usado por outra fábrica, ou a fábrica criou um transformador de outro nome, versão ou
    /// injetividade.
    /// </exception>
    public Transformador Criar(ContextoCriacao contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (!contexto.Reservar(this))
        {
            throw new InvalidOperationException("O contexto de criação já foi usado por outra fábrica: cada fábrica recebe o seu.");
        }

        Validar(contexto.Regra);
        if (contexto.Regra.Dominio.Versao != Versao)
        {
            throw new RegraInvalidaException($"O domínio {contexto.Regra.Dominio} é de outra versão que o transformador {Nome}@{Versao}.");
        }

        if (!TiposSuportados.Contains(contexto.Tipo))
        {
            throw new RegraInvalidaException($"O transformador {Nome}@{Versao} não aceita coluna do tipo {contexto.Tipo}.");
        }

        var transformador = CriarCore(contexto) ?? throw new InvalidOperationException($"A fábrica {Nome}@{Versao} não criou o transformador.");
        if (!string.Equals(transformador.Nome, Nome, StringComparison.Ordinal) || transformador.Versao != Versao
            || transformador.Injetividade != Injetividade)
        {
            throw new InvalidOperationException($"A fábrica {Nome}@{Versao} criou um transformador de outro nome, versão ou injetividade.");
        }

        return transformador;
    }

    /// <summary>Cria o transformador, com a regra, a versão e o tipo já conferidos por <see cref="Criar"/>.</summary>
    /// <param name="contexto">A regra e a coluna.</param>
    /// <returns>O transformador.</returns>
    /// <exception cref="RegraInvalidaException">Coluna que o transformador não atende (comprimento, colação).</exception>
    protected abstract Transformador CriarCore(ContextoCriacao contexto);
}
