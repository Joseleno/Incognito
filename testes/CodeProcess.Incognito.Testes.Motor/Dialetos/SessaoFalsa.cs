using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// Os dados do banco falso que mudam durante o teste (o maior valor aumentado, as tabelas criadas, esvaziadas e travadas, a
/// partição anexada) e as conexões das sessões de leitura. Dividido pelas instâncias conectadas ao banco.
/// </summary>
internal sealed class DadosDoBancoFalso
{
    private readonly Lock _trava = new();
    private readonly Dictionary<ReferenciaColuna, long?> _maiores = [];
    private readonly HashSet<NomeObjeto> _esvaziadas = [];
    private readonly List<NomeObjeto> _criadas = [];
    private readonly Dictionary<NomeObjeto, int> _travas = [];
    private long? _anexado;
    private int _conexoes;
    private int _geracao;

    /// <summary>A particionada do banco falso, onde a partição é anexada.</summary>
    internal static readonly NomeObjeto Particionada = BancoFalso.Nome("pedidos");

    /// <summary>O valor da linha da partição anexada.</summary>
    internal const long ValorAnexado = 999;

    /// <summary>Os dados num momento: o instante de uma sessão.</summary>
    internal sealed record Retrato(IReadOnlyDictionary<ReferenciaColuna, long?> Maiores, IReadOnlySet<NomeObjeto> Esvaziadas, long? Anexado);

    internal int Conexoes
    {
        get
        {
            lock (_trava)
            {
                return _conexoes;
            }
        }
    }

    internal int Geracao
    {
        get
        {
            lock (_trava)
            {
                return _geracao;
            }
        }
    }

    internal IReadOnlyList<NomeObjeto> Criadas
    {
        get
        {
            lock (_trava)
            {
                return [.. _criadas];
            }
        }
    }

    internal Retrato Agora()
    {
        lock (_trava)
        {
            return new Retrato(new Dictionary<ReferenciaColuna, long?>(_maiores), new HashSet<NomeObjeto>(_esvaziadas), _anexado);
        }
    }

    /// <summary>Se o teste mudou a coluna (o maior valor, a tabela esvaziada, a partição anexada).</summary>
    internal static bool Mudou(Retrato retrato, ReferenciaColuna coluna) =>
        retrato.Maiores.ContainsKey(coluna) || retrato.Esvaziadas.Contains(coluna.Tabela) || (coluna.Tabela.Equals(Particionada) && retrato.Anexado is not null);

    /// <summary>O maior valor num retrato, como a cópia lê: a tabela esvaziada não tem; a partição anexada conta pelo pai.</summary>
    internal static long? Maior(Retrato retrato, ReferenciaColuna coluna, Defeito defeito)
    {
        if (retrato.Esvaziadas.Contains(coluna.Tabela))
        {
            return null;
        }

        var maior = retrato.Maiores.TryGetValue(coluna, out var definido) ? definido : BancoFalso.MaiorValor(coluna, defeito);
        return coluna.Tabela.Equals(Particionada) && coluna.Coluna == "id" && retrato.Anexado is { } anexado ? Math.Max(maior ?? anexado, anexado) : maior;
    }

    internal void DefinirMaior(ReferenciaColuna coluna, long valor)
    {
        lock (_trava)
        {
            _maiores[coluna] = valor;
        }
    }

    internal void Criar(NomeObjeto tabela)
    {
        lock (_trava)
        {
            _criadas.Add(tabela);
        }
    }

    internal void Anexar()
    {
        lock (_trava)
        {
            _anexado = ValorAnexado;
        }
    }

    /// <summary>Esvazia a tabela, se nenhuma sessão a trava.</summary>
    internal bool TentarEsvaziar(NomeObjeto tabela)
    {
        lock (_trava)
        {
            if (_travas.GetValueOrDefault(tabela) > 0)
            {
                return false;
            }

            _esvaziadas.Add(tabela);
            return true;
        }
    }

    internal void Travar(IEnumerable<NomeObjeto> tabelas, int sentido)
    {
        lock (_trava)
        {
            foreach (var tabela in tabelas)
            {
                _travas[tabela] = _travas.GetValueOrDefault(tabela) + sentido;
            }
        }
    }

    internal void Contar(int conexoes)
    {
        lock (_trava)
        {
            _conexoes += conexoes;
        }
    }

    /// <summary>A queda das conexões das sessões: as abertas antes não leem mais, e as travas delas se soltam.</summary>
    internal void Derrubar()
    {
        lock (_trava)
        {
            _geracao++;
            _travas.Clear();
        }
    }
}

/// <summary>
/// A sessão de leitura do falso: o instante é o retrato dos dados e o esquema lido na abertura; as travadas ficam travadas
/// até o descarte. Com os defeitos de quem lê fora do instante, não trava, filtra, não fecha.
/// </summary>
internal sealed class SessaoFalsa : SessaoLeitura
{
    private readonly DadosDoBancoFalso _dados;
    private readonly IReadOnlyList<NomeObjeto> _travadas;
    private readonly LeitorEsquema _leitor;
    private readonly EsquemaBanco _esquema;
    private readonly Defeito _defeito;
    private readonly int _conexoes;
    private readonly Action<SessaoFalsa> _aoDescartar;

    private SessaoFalsa(IReadOnlyList<LeitorDaSessaoFalso> leitores, DadosDoBancoFalso dados, IReadOnlyList<NomeObjeto> travadas, LeitorEsquema leitor, EsquemaBanco esquema, Defeito defeito, int conexoes, Action<SessaoFalsa> aoDescartar)
        : base(leitores)
    {
        _dados = dados;
        _travadas = travadas;
        _leitor = leitor;
        _esquema = esquema;
        _defeito = defeito;
        _conexoes = conexoes;
        _aoDescartar = aoDescartar;
    }

    public override ConsistenciaObtida Obtida => _defeito == Defeito.ConsistenciaTrocada ? ConsistenciaObtida.PorTabela : ConsistenciaObtida.Instantaneo;

    internal static async Task<SessaoLeitura> AbrirAsync(
        DadosDoBancoFalso dados, PedidoLeitura pedido, bool completo, Defeito defeito, LeitorEsquema leitor, Action<SessaoFalsa> aoAbrir, Action<SessaoFalsa> aoDescartar, CancellationToken cancelamento)
    {
        var tabelas = BancoFalso.Tabelas(completo).Select(tabela => tabela.Nome).ToHashSet();
        if (pedido.Travadas.FirstOrDefault(travada => !tabelas.Contains(travada)) is { } inexistente && defeito != Defeito.TravadaInexistenteAceita)
        {
            throw new DialetoRecusouException("INC3399", $"A tabela {inexistente} não existe para travar" + (defeito == Defeito.SentinelaNaRecusaDaTravada ? ": " + BancoFalso.Sentinela : "."));
        }

        if (pedido.Modo == ModoConsistencia.Exigir && defeito == Defeito.ExigirRecusado)
        {
            throw new DialetoRecusouException("INC3399", "O banco falso não dá o instante único.");
        }

        IReadOnlyList<NomeObjeto> travadas = defeito == Defeito.TravaAusente ? [] : pedido.Travadas;
        dados.Travar(travadas, +1);
        var esquema = await leitor.LerAsync(cancelamento);
        var retrato = dados.Agora();
        var geracao = dados.Geracao;
        var conexoes = defeito == Defeito.LeitoresNumaConexaoSo ? 1 : pedido.Leitores + 1;
        dados.Contar(conexoes);
        var leitores = Enumerable.Range(0, pedido.Leitores).Select(_ => new LeitorDaSessaoFalso(dados, retrato, geracao, defeito)).ToArray();
        var sessao = new SessaoFalsa(leitores, dados, travadas, leitor, esquema, defeito, conexoes, aoDescartar);
        aoAbrir(sessao);
        return sessao;
    }

    protected override async Task<EsquemaBanco> LerEsquemaAsyncCore(CancellationToken cancelamento) => _defeito switch
    {
        Defeito.EsquemaForaDoInstante => await _leitor.LerAsync(cancelamento),
        Defeito.EsquemaRelidoDiferente => BancoFalso.Clonar(_esquema, (nameof(EsquemaBanco.Tabelas), _esquema.Tabelas.Skip(1).ToArray())),
        _ => _esquema,
    };

    protected override Task<long?> LerIdadeAsyncCore(CancellationToken cancelamento) => Task.FromResult<long?>(_defeito == Defeito.IdadeNula ? null : 0);

    protected override ValueTask DisposeAsyncCore()
    {
        if (_defeito != Defeito.TravaNaoSolta)
        {
            _dados.Travar(_travadas, -1);
        }

        if (_defeito != Defeito.DescarteNaoFecha)
        {
            _dados.Contar(-_conexoes);
        }

        _aoDescartar(this);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Um leitor da sessão do falso: lê o retrato, com a guarda contra o que mudou fora do instante.</summary>
internal sealed class LeitorDaSessaoFalso(DadosDoBancoFalso dados, DadosDoBancoFalso.Retrato retrato, int geracao, Defeito defeito) : LeitorDaSessao
{
    private bool _morto;

    protected override Task<long?> LerMaiorValorAsyncCore(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        try
        {
            return Task.FromResult(Ler(coluna));
        }
        catch (Exception erro)
        {
            _morto = defeito == Defeito.LeitorMortoDepoisDaFalha;
            return Task.FromException<long?>(erro);
        }
    }

    private long? Ler(ReferenciaColuna coluna)
    {
        if (_morto || (dados.Geracao != geracao && defeito != Defeito.SessaoDerrubadaLe))
        {
            throw new IncognitoException("INC3399", "A conexão do leitor caiu.");
        }

        switch (coluna.Tabela.Nome)
        {
            case "tipos":
                throw new DialetoRecusouException("INC3399", "O maior valor só se lê de coluna inteira.");
            case "segredos" when defeito == Defeito.SessaoFiltrada:
                return 7;
            case "segredos":
                throw new IncognitoException("INC3399", "A segurança por linha vale para o login: a leitura falhou em vez de filtrar.");
        }

        var agora = dados.Agora();
        if (defeito == Defeito.LeitorForaDoInstante)
        {
            return DadosDoBancoFalso.Maior(agora, coluna, Defeito.Nenhum);
        }

        // A guarda: a tabela esvaziada ou com partição anexada depois do instante não é lida (sem a trava, a leitura do
        // instante a veria vazia, e com a partição nova).
        var esvaziadaDepois = agora.Esvaziadas.Contains(coluna.Tabela) && !retrato.Esvaziadas.Contains(coluna.Tabela);
        var anexadaDepois = coluna.Tabela.Equals(DadosDoBancoFalso.Particionada) && agora.Anexado != retrato.Anexado;
        if (esvaziadaDepois && defeito == Defeito.TravaAusente)
        {
            return null;
        }

        if (anexadaDepois && defeito == Defeito.ParticaoAnexadaAceita)
        {
            return DadosDoBancoFalso.Maior(agora, coluna, Defeito.Nenhum);
        }

        if (esvaziadaDepois || anexadaDepois)
        {
            throw new IncognitoException("INC3399", "A tabela mudou depois do início da leitura.");
        }

        return defeito == Defeito.SessaoSoDaRaiz && coluna.Tabela.Equals(DadosDoBancoFalso.Particionada) ? null : DadosDoBancoFalso.Maior(retrato, coluna, Defeito.Nenhum);
    }
}

/// <summary>O leitor do esquema com as tabelas que a fixture criou depois (o controle do fato do instante).</summary>
internal sealed class LeitorComCriadas(LeitorEsquema leitor, DadosDoBancoFalso dados) : LeitorEsquema
{
    public override async Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        var lido = await leitor.LerAsync(cancelamento);
        var criadas = dados.Criadas;
        return criadas.Count == 0
            ? lido
            : BancoFalso.Clonar(lido, (nameof(EsquemaBanco.Tabelas), lido.Tabelas.Concat(criadas.Select(nome => new TabelaEsquema { Nome = nome, Colunas = [] })).ToArray()));
    }
}

/// <summary>As sessões vivas de um dialeto falso, que o descarte dele fecha.</summary>
internal sealed class SessoesFalsas(Defeito defeito)
{
    private readonly List<SessaoFalsa> _vivas = [];

    internal void Abriu(SessaoFalsa sessao)
    {
        lock (_vivas)
        {
            _vivas.Add(sessao);
        }
    }

    internal void Fechou(SessaoFalsa sessao)
    {
        lock (_vivas)
        {
            _vivas.Remove(sessao);
        }
    }

    internal async ValueTask FecharAsync()
    {
        if (defeito == Defeito.DialetoNaoFechaSessao)
        {
            return;
        }

        SessaoFalsa[] vivas;
        lock (_vivas)
        {
            vivas = [.. _vivas];
        }

        foreach (var sessao in vivas)
        {
            await sessao.DisposeAsync();
        }
    }
}
