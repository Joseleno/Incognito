using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Universo de pseudônimos: o nome canônico do domínio e a versão do transformador (<c>cpf@1</c>). Mesma chave, mesmo
/// ambiente e mesmo domínio dão o mesmo pseudônimo; renomear o domínio ou mudar a versão muda toda a saída do modo
/// <c>reproduzivel</c>.
/// </summary>
/// <remarks>
/// O valor <c>default</c> não é um domínio: <see cref="Nome"/> e <see cref="Versao"/> lançam nele, e quem recebe um
/// domínio o recusa. A igualdade é ordinal e funciona também com o <c>default</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct DominioPseudonimo : IEquatable<DominioPseudonimo>
{
    private const string MensagemNaoInicializado = "Domínio não inicializado (o valor default não é um domínio).";

    private readonly string? _nome;
    private readonly int _versao;

    /// <summary>Cria o domínio.</summary>
    /// <param name="nome">
    /// O nome canônico: letras minúsculas e dígitos, com <c>.</c>, <c>_</c> ou <c>-</c> entre eles, até 64 caracteres.
    /// </param>
    /// <param name="versao">A versão do transformador, a partir de 1.</param>
    /// <exception cref="ArgumentNullException">Nome nulo.</exception>
    /// <exception cref="ArgumentException">Nome fora da regra dos nomes; a mensagem não traz o nome.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Versão menor que 1.</exception>
    public DominioPseudonimo(string nome, int versao)
    {
        Subchaves.ValidarNome(nome, nameof(nome));
        ArgumentOutOfRangeException.ThrowIfLessThan(versao, 1);
        _nome = nome;
        _versao = versao;
    }

    /// <summary>O nome canônico do domínio.</summary>
    /// <exception cref="InvalidOperationException">No valor <c>default</c>.</exception>
    public string Nome => _nome ?? throw new InvalidOperationException(MensagemNaoInicializado);

    /// <summary>A versão do transformador, a partir de 1.</summary>
    /// <exception cref="InvalidOperationException">No valor <c>default</c>.</exception>
    public int Versao => _nome is null ? throw new InvalidOperationException(MensagemNaoInicializado) : _versao;

    /// <summary>Se é um domínio de verdade, e não o valor <c>default</c>.</summary>
    internal bool Inicializado => _nome is not null;

    /// <summary>Lê um domínio no formato <c>nome@versao</c>, como <c>cpf@1</c>.</summary>
    /// <param name="texto">O texto: o nome pela regra dos nomes, <c>@</c> e a versão em decimal, sem sinal nem zeros à esquerda.</param>
    /// <param name="dominio">O domínio lido, ou <c>default</c>.</param>
    /// <returns>Se o texto estava no formato.</returns>
    public static bool TryParse([NotNullWhen(true)] string? texto, out DominioPseudonimo dominio)
    {
        dominio = default;
        if (texto is null)
        {
            return false;
        }

        var arroba = texto.LastIndexOf('@');
        if (arroba <= 0 || !Subchaves.EhNomeValido(texto[..arroba]))
        {
            return false;
        }

        var versao = texto.AsSpan(arroba + 1);
        if (versao.IsEmpty || versao[0] == '0' || versao.ContainsAnyExceptInRange('0', '9')
            || !int.TryParse(versao, NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
        {
            return false;
        }

        dominio = new DominioPseudonimo(texto[..arroba], numero);
        return true;
    }

    /// <summary>Compara dois domínios.</summary>
    /// <param name="esquerda">O primeiro.</param>
    /// <param name="direita">O segundo.</param>
    /// <returns>Se são o mesmo domínio.</returns>
    public static bool operator ==(DominioPseudonimo esquerda, DominioPseudonimo direita) => esquerda.Equals(direita);

    /// <summary>Compara dois domínios.</summary>
    /// <param name="esquerda">O primeiro.</param>
    /// <param name="direita">O segundo.</param>
    /// <returns>Se são domínios diferentes.</returns>
    public static bool operator !=(DominioPseudonimo esquerda, DominioPseudonimo direita) => !esquerda.Equals(direita);

    /// <inheritdoc/>
    public bool Equals(DominioPseudonimo other) => string.Equals(_nome, other._nome, StringComparison.Ordinal) && _versao == other._versao;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DominioPseudonimo outro && Equals(outro);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_nome ?? ""), _versao);

    /// <summary>O domínio no formato <c>nome@versao</c>; vazio no valor <c>default</c>.</summary>
    /// <returns>O texto.</returns>
    public override string ToString() => _nome is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{_nome}@{_versao}");
}
