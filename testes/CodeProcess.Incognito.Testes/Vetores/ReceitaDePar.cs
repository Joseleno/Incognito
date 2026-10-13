namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Como gerar os pares de um ramo: a origem (método do <see cref="GeradorBr"/>, regra de construção, inversa ou
/// literal), as formas, quantas vezes e o resultado que o ramo precisa ter. O gerador procura a semente que satisfaz.
/// </summary>
internal sealed record ReceitaDePar(string Ramo, string Tipo, string? MetodoOuRegra, string[] Formas)
{
    /// <summary>Entrada literal (só bordas sem identificador de ninguém): texto, inteiro ou UTF-16 em hexadecimal.</summary>
    internal object? Literal { get; init; }

    internal int Vezes { get; init; } = 1;

    /// <summary>Desfecho (nome do enum) ou código de erro que o ramo exige; nulo se qualquer um serve.</summary>
    internal string? Esperado { get; init; }

    /// <summary>Entidade do CEP; <c>{n}</c> é trocado pela semente da tentativa.</summary>
    internal string? Entidade { get; init; }

    /// <summary>Condição a mais sobre entrada e resultado, para ramos que dependem da saída.</summary>
    internal Func<object, ResultadoDoPar, bool>? Condicao { get; init; }

    internal static ReceitaDePar Gerador(string ramo, string metodo, params string[] formas) => new(ramo, "gerador-br", metodo, formas);

    internal static ReceitaDePar Construido(string ramo, string regra, params string[] formas) => new(ramo, "construido", regra, formas);

    internal static ReceitaDePar Inversa(string ramo, string regra, params string[] formas) => new(ramo, "inversa", regra, formas);

    internal static ReceitaDePar DeLiteral(string ramo, object literal) => new(ramo, "literal", null, []) { Literal = literal };

    internal ReceitaDePar X(int vezes) => this with { Vezes = vezes };

    internal ReceitaDePar E(string esperado) => this with { Esperado = esperado };

    internal ReceitaDePar Com(string entidade) => this with { Entidade = entidade };

    internal ReceitaDePar Onde(Func<object, ResultadoDoPar, bool> condicao) => this with { Condicao = condicao };
}
