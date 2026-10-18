using System.Collections.Frozen;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// As fábricas de transformadores por nome e versão: as embutidas e as acrescentadas. Os metadados de cada fábrica são
/// copiados no registro (uma fábrica não muda de nome, versão, tipos ou domínio padrão depois). Interno até o
/// <c>AddIncognito</c> expor o acréscimo de fábricas.
/// </summary>
/// <remarks>
/// Não é seguro acrescentar fábricas enquanto outra thread lê o registro: monte-o antes do <c>planejar</c>.
/// </remarks>
internal sealed class RegistroTransformadores
{
    // Reservados para as embutidas, as de hoje e as futuras: uma fábrica de fora nunca colide com uma do Incognito. A
    // documentação recomenda um prefixo da organização (empresa.algo).
    private static readonly string[] _prefixosReservados = ["br.", "id.", "texto.", "data.", "numero.", "json.", "incognito.", "email.", "fixo.", "nulo."];
    // Também os domínios padrão das embutidas que ainda vão chegar (chave Pix e número de DF-e): o domínio padrão de uma
    // fábrica de fora é o nome dela, e ele nunca pode colidir depois com o de uma embutida.
    private static readonly FrozenSet<string> _nomesReservados =
        new[] { "email", "fixo", "nulo", "chave-pix", "dfe-numero" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly Dictionary<(string Nome, int Versao), FabricaRegistrada> _fabricas = [];

    /// <summary>Cria o registro com as fábricas embutidas.</summary>
    internal RegistroTransformadores()
    {
        foreach (var fabrica in TransformadoresEmbutidos.Fabricas)
        {
            Registrar(fabrica, fabrica.Nome);
        }
    }

    /// <summary>Acrescenta uma fábrica de fora do Incognito.</summary>
    /// <exception cref="ArgumentNullException">Fábrica nula.</exception>
    /// <exception cref="ArgumentException">
    /// Nome ou domínio padrão fora da regra dos nomes, versão menor que 1, nome reservado, nome e versão já registrados ou
    /// domínio padrão de outra fábrica.
    /// </exception>
    internal void Adicionar(FabricaTransformador fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        // Lido uma vez só: a conferência dos reservados e o registro usam o mesmo nome, já validado.
        var nome = fabrica.Nome;
        ArgumentNullException.ThrowIfNull(nome, nameof(fabrica));
        _ = new DominioPseudonimo(nome, 1);
        if (_nomesReservados.Contains(nome) || _prefixosReservados.Any(prefixo => nome.StartsWith(prefixo, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"O nome {nome} é reservado aos transformadores do Incognito; use um prefixo da organização (empresa.algo).", nameof(fabrica));
        }

        Registrar(fabrica, nome);
    }

    /// <summary>A fábrica de um nome e uma versão.</summary>
    internal bool TentarObter(string nome, int versao, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FabricaRegistrada? fabrica) =>
        _fabricas.TryGetValue((nome, versao), out fabrica);

    /// <summary>A maior versão registrada de um nome, ou nulo se nenhuma.</summary>
    internal int? VersaoMaisRecente(string nome)
    {
        var versoes = _fabricas.Keys.Where(chave => chave.Nome == nome).Select(chave => chave.Versao).ToList();
        return versoes.Count == 0 ? null : versoes.Max();
    }

    /// <summary>As fábricas cujo domínio padrão é este nome (o mesmo nome em todas as versões).</summary>
    internal IEnumerable<FabricaRegistrada> ComDominioPadrao(string dominio) =>
        _fabricas.Values.Where(fabrica => fabrica.DominioPadrao == dominio).OrderBy(fabrica => fabrica.Versao);

    private void Registrar(FabricaTransformador fabrica, string nome)
    {
        // Lidos uma vez: a fábrica não troca de nome, versão ou domínio depois do registro. O domínio padrão de uma
        // fábrica de fora é o nome (o da embutida vem dela).
        var versao = fabrica.Versao;
        var dominioPadrao = TransformadoresEmbutidos.Fabricas.Contains(fabrica) ? fabrica.DominioPadrao : nome;
        var injetividade = fabrica.Injetividade;
        var tipos = (fabrica.TiposSuportados ?? throw new ArgumentException($"A fábrica {nome}@{versao} não declara os tipos suportados.", nameof(fabrica))).ToFrozenSet();
        var identidade = (fabrica.ParametrosDeIdentidade ?? throw new ArgumentException($"A fábrica {nome}@{versao} não declara os parâmetros de identidade.", nameof(fabrica))).ToFrozenSet(StringComparer.Ordinal);
        var citados = fabrica.ParametrosDeDominio.ToFrozenDictionary(StringComparer.Ordinal);
        _ = new DominioPseudonimo(nome, versao);
        _ = new DominioPseudonimo(dominioPadrao, versao);
        if (!Enum.IsDefined(injetividade))
        {
            throw new ArgumentException($"A fábrica {nome}@{versao} declara uma injetividade desconhecida.", nameof(fabrica));
        }

        if (tipos.Count == 0 || tipos.Any(tipo => !Enum.IsDefined(tipo)))
        {
            throw new ArgumentException($"A fábrica {nome}@{versao} declara tipos suportados vazios ou desconhecidos.", nameof(fabrica));
        }

        if (_fabricas.ContainsKey((nome, versao)))
        {
            throw new ArgumentException($"Já há uma fábrica {nome}@{versao}.", nameof(fabrica));
        }

        if (_fabricas.Values.Any(outra => outra.DominioPadrao == dominioPadrao && outra.Nome != nome))
        {
            throw new ArgumentException($"O domínio padrão {dominioPadrao} já é de outra fábrica.", nameof(fabrica));
        }

        _fabricas.Add((nome, versao), new FabricaRegistrada(fabrica, nome, versao, injetividade, tipos, dominioPadrao, identidade, citados));
    }
}

/// <summary>Uma fábrica registrada, com os metadados lidos uma vez no registro.</summary>
internal sealed record FabricaRegistrada(
    FabricaTransformador Fabrica,
    string Nome,
    int Versao,
    Injetividade Injetividade,
    FrozenSet<TipoColuna> TiposSuportados,
    string DominioPadrao,
    FrozenSet<string> ParametrosDeIdentidade,
    FrozenDictionary<string, string> ParametrosDeDominio)
{
    /// <summary>
    /// Cria o transformador de uma coluna conferindo os metadados registrados, não os que a fábrica devolve agora: o tipo
    /// da coluna, e o nome, a versão e a injetividade do transformador criado.
    /// </summary>
    /// <exception cref="RegraInvalidaException">Regra recusada ou coluna que o transformador não atende.</exception>
    /// <exception cref="InvalidOperationException">A fábrica mudou depois do registro.</exception>
    internal Transformador Criar(ContextoCriacao contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (!TiposSuportados.Contains(contexto.Tipo))
        {
            throw new RegraInvalidaException($"O transformador {Nome}@{Versao} não aceita coluna do tipo {contexto.Tipo}.");
        }

        var transformador = Fabrica.Criar(contexto);
        if (!string.Equals(transformador.Nome, Nome, StringComparison.Ordinal) || transformador.Versao != Versao || transformador.Injetividade != Injetividade)
        {
            throw new InvalidOperationException($"A fábrica {Nome}@{Versao} criou um transformador diferente do que registrou.");
        }

        return transformador;
    }
}
