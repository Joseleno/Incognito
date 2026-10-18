using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// A sessão de uma execução: a chave, o ambiente, a tabela de domínios da configuração e as instâncias derivadas que os
/// transformadores embutidos usam, uma por identidade (domínio, versão, parâmetros e políticas), compartilhadas entre as
/// colunas do mesmo universo. Nunca expõe a chave nem uma subchave, e não guarda pseudônimos.
/// </summary>
/// <remarks>
/// <para>
/// Tão sensível quanto a chave: os transformadores criados com ela pseudonimizam qualquer valor, e quem os chama monta a
/// tabela inteira e reverte os pseudônimos. Nunca a exponha a quem recebe os dados.
/// </para>
/// <para>
/// A chave continua de quem a criou e precisa viver enquanto a sessão for usada. O <see cref="Dispose"/> vem depois de
/// todo uso dos transformadores criados com ela (inclusive em cancelamento); é idempotente e descarta as instâncias na
/// ordem inversa da criação.
/// </para>
/// </remarks>
[DebuggerDisplay("SessaoTransformacao({Ambiente})")]
public sealed class SessaoTransformacao : IDisposable
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly ChaveIncognito _chave;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly Dictionary<string, IDisposable> _instancias = new(StringComparer.Ordinal);

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly List<IDisposable> _ordem = [];

    private readonly Lock _trava = new();
    private readonly IReadOnlyCollection<DefinicaoDominio> _dominios = ReadOnlyCollection<DefinicaoDominio>.Empty;
    private readonly FrozenDictionary<string, DefinicaoDominio> _porNome = FrozenDictionary<string, DefinicaoDominio>.Empty;
    private bool _descartada;

    /// <summary>Abre a sessão.</summary>
    /// <param name="chave">A chave do Incognito, que continua de quem a criou.</param>
    /// <param name="ambiente">
    /// O ambiente da configuração: letras minúsculas e dígitos, com <c>.</c>, <c>_</c> ou <c>-</c> entre eles, até 64
    /// caracteres.
    /// </param>
    /// <exception cref="ArgumentNullException">Chave ou ambiente nulos.</exception>
    /// <exception cref="ArgumentException">Ambiente fora da regra dos nomes; a mensagem não traz o valor.</exception>
    /// <exception cref="ObjectDisposedException">Chave já descartada.</exception>
    public SessaoTransformacao(ChaveIncognito chave, string ambiente)
    {
        ArgumentNullException.ThrowIfNull(chave);
        Subchaves.ValidarNome(ambiente, nameof(ambiente));
        chave.LancarSeDescartada();
        _chave = chave;
        Ambiente = ambiente;
    }

    /// <summary>O ambiente das derivações.</summary>
    public string Ambiente { get; }

    /// <summary>
    /// Os domínios da configuração, um por nome: os declarados, os implícitos das colunas e os que a chave Pix e a chave
    /// de DF-e citam (<c>dominioCpf</c>…), com as políticas de cada domínio. A chave Pix e a chave de DF-e procuram aqui
    /// o domínio de cada documento, para usar a mesma instância da coluna dele. Padrão: nenhum.
    /// </summary>
    /// <exception cref="ArgumentNullException">Na atribuição: nula, ou com um item nulo.</exception>
    /// <exception cref="ArgumentException">Na atribuição: nome de domínio repetido.</exception>
    public IReadOnlyCollection<DefinicaoDominio> Dominios
    {
        get => _dominios;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            var porNome = new Dictionary<string, DefinicaoDominio>(StringComparer.Ordinal);
            var naOrdem = new List<DefinicaoDominio>();
            foreach (var definicao in value)
            {
                ArgumentNullException.ThrowIfNull(definicao, nameof(value));
                if (!porNome.TryAdd(definicao.Nome, definicao))
                {
                    throw new ArgumentException("Nome de domínio repetido: um nome de domínio tem uma definição só.", nameof(value));
                }

                naOrdem.Add(definicao);
            }

            _dominios = naOrdem.AsReadOnly();
            _porNome = porNome.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Descarta as instâncias derivadas, na ordem inversa da criação, depois de todo uso; idempotente. A
    /// <see cref="ChaveIncognito"/> continua de quem a criou.
    /// </summary>
    public void Dispose()
    {
        // Sob a trava: uma segunda chamada espera a primeira terminar, e nenhuma criação cruza o descarte.
        ExceptionDispatchInfo? primeiro = null;
        lock (_trava)
        {
            if (_descartada)
            {
                return;
            }

            _descartada = true;

            // Um descarte que falhe não impede os seguintes; o primeiro erro sobe no fim.
            for (var i = _ordem.Count - 1; i >= 0; i--)
            {
                try
                {
                    _ordem[i].Dispose();
                }
                catch (Exception excecao)
                {
                    primeiro ??= ExceptionDispatchInfo.Capture(excecao);
                }
            }
        }

        primeiro?.Throw();
    }

    /// <summary>Quantas instâncias a sessão criou (para os testes do compartilhamento).</summary>
    internal int Instancias
    {
        get
        {
            lock (_trava)
            {
                return _ordem.Count;
            }
        }
    }

    /// <summary>A definição de um domínio da tabela, pelo nome.</summary>
    internal bool TentarObterDominio(string nome, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DefinicaoDominio? definicao) =>
        _porNome.TryGetValue(nome, out definicao);

    /// <summary>
    /// A instância de uma identidade: a já criada, ou uma nova, guardada para as próximas colunas do mesmo universo.
    /// </summary>
    /// <param name="identidade">Tudo o que muda a saída: transformador, domínio, versão, parâmetros e políticas.</param>
    /// <param name="criar">Cria a instância com a chave e o ambiente; a sessão passa a ser dona dela.</param>
    /// <returns>A instância.</returns>
    /// <exception cref="ObjectDisposedException">Sessão ou chave já descartadas.</exception>
    internal T Obter<T>(string identidade, Func<ChaveIncognito, string, T> criar)
        where T : class, IDisposable
    {
        lock (_trava)
        {
            ObjectDisposedException.ThrowIf(_descartada, this);
            _chave.LancarSeDescartada();
            if (_instancias.TryGetValue(identidade, out var existente))
            {
                return (T)existente;
            }

            var nova = criar(_chave, Ambiente);
            _instancias.Add(identidade, nova);
            _ordem.Add(nova);
            return nova;
        }
    }
}
