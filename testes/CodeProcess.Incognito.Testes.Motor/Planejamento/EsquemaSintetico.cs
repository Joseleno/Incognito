using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// Esquemas montados à mão para o planejar: um banco pequeno e coerente, em que cada campo nasce no valor otimista, e
/// cada teste troca um só (por <see cref="BancoFalso.Clonar"/>, que copia todo campo do modelo).
/// </summary>
internal static class EsquemaSintetico
{
    internal static readonly ColacaoEsquema Padrao = BancoFalso.Padrao;

    /// <summary>
    /// <c>loja.clientes</c> (id, cpf, nome), com chave primária e índice primário, e <c>loja.pedidos</c> com a chave
    /// estrangeira para clientes; uma sequence do id, as propriedades do banco e as permissões de tudo.
    /// </summary>
    internal static EsquemaBanco Coerente() => ComTabelas(Clientes(), Pedidos());

    internal static EsquemaBanco ComTabelas(params TabelaEsquema[] tabelas)
    {
        // A publicação e a assinatura nascem no valor otimista (fora de qualquer uma) quando o teste não as escreve.
        tabelas = [.. tabelas.Select(tabela => tabela.Publicada is null ? BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Publicada), false)) : tabela)];
        tabelas = [.. tabelas.Select(tabela => tabela.Assinada is null ? BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Assinada), false)) : tabela)];
        var sequences = new SequenceEsquema[] { new() { Nome = Nome("clientes_id_seq"), Colunas = [new ReferenciaColuna(Nome("clientes"), "id")] } };
        return new EsquemaBanco
        {
            Tabelas = tabelas,
            Sequences = tabelas.Any(tabela => tabela.Nome.Equals(Nome("clientes"))) ? sequences : [],
            ObjetosResiduais = [],
            ObjetosNoEsquemaReservado = [],
            TriggersDeEvento = [],
            Propriedades = new PropriedadesBanco
            {
                Versao = new Version(18, 0),
                Codificacao = new CodificacaoTexto { Nome = "UTF8", Definida = true, Unicode = true, BytesPorCaractereMaximo = 4 },
                Colacao = Padrao,
            },
            Acesso = Acesso(tabelas, tabelas.Any(tabela => tabela.Nome.Equals(Nome("clientes"))) ? sequences : [], []),
        };
    }

    internal static AcessoLido Acesso(IEnumerable<TabelaEsquema> tabelas, IEnumerable<SequenceEsquema> sequences, IEnumerable<ObjetoResidual> residuais) => new()
    {
        Banco = new PermissoesBanco { PodeMarcarBanco = true, PodeCriarEsquema = true },
        Tabelas =
        [
            .. tabelas.Select(tabela => new PermissoesTabela
            {
                Tabela = tabela.Nome,
                PodeLer = true,
                PodeInserir = true,
                PodeGravarIdentidade = true,
                PodeEsvaziar = true,
                PodeReferenciar = true,
                PodeAlterarRestricoes = true,
                PodeDesligarTriggers = true,
                LeituraFiltradaPorLinha = false,
                EscritaRestritaPorLinha = false,
            }),
        ],
        Residuais = [.. residuais.Select(residual => new PermissoesResidual { Nome = residual.Nome, Tipo = residual.Tipo, PodeLimpar = true })],
        Sequences = [.. sequences.Select(sequence => new PermissoesSequence { Nome = sequence.Nome, PodeAjustar = true })],
    };

    internal static TabelaEsquema Clientes() => new()
    {
        Nome = Nome("clientes"),
        Colunas = [Inteiro("id", anulavel: false), Texto("cpf", 11), Texto("nome", 100)],
        ChavePrimaria = ["id"],
        IndicesUnicos = [new IndiceUnico { Nome = "pk_clientes", Primario = true, Elementos = [new ElementoIndice { Coluna = "id" }], NulosDistintos = true }],
    };

    internal static TabelaEsquema Pedidos() => new()
    {
        Nome = Nome("pedidos"),
        Colunas = [Inteiro("id", anulavel: false), Inteiro("cliente")],
        ChavePrimaria = ["id"],
        IndicesUnicos = [new IndiceUnico { Nome = "pk_pedidos", Primario = true, Elementos = [new ElementoIndice { Coluna = "id" }], NulosDistintos = true }],
        ChavesEstrangeiras = [new ChaveEstrangeira { Nome = "fk_pedidos_clientes", Colunas = ["cliente"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["id"], Imposta = true, Validada = true, DesligadaSemRestauracao = false }],
    };

    // A origem (loja) sem marcador e o destino com ele, em servidores e linhagens diferentes: nada a avisar.
    internal static IdentidadeLida Identidade(string banco) => new()
    {
        Banco = banco,
        Servidor = banco == "loja" ? "origem-sintetica:5432" : "destino-sintetico:5432",
        Linhagem = new Dictionary<string, string> { ["identificador"] = banco == "loja" ? "1" : "2", ["banco"] = "16384" },
        Transporte = new TransporteConexao { Cifrado = true, ServidorVerificado = true, Local = false },
        Marcador = banco == "loja" ? Ausente : Marcado,
    };

    internal static MarcadorLido Ausente { get; } = new() { Situacao = SituacaoMarcador.Ausente };

    internal static MarcadorLido Marcado { get; } = new()
    {
        Situacao = SituacaoMarcador.Valido,
        Identificador = new Guid("6f1c2a4e-9b3d-4c8a-a1e5-0d7f3b2c9e14"),
        GravadoEm = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
    };

    internal static NomeObjeto Nome(string nome) => new("loja", nome);

    internal static ColunaEsquema Inteiro(string nome, bool anulavel = true) => new()
    {
        Nome = nome,
        TipoNativo = "bigint",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Inteiro,
        Anulavel = anulavel,
        Faixa = new FaixaInteira { Minimo = long.MinValue, Maximo = long.MaxValue },
        RestritaPeloTipo = false,
    };

    internal static ColunaEsquema Texto(string nome, int comprimento) => new()
    {
        Nome = nome,
        TipoNativo = "varchar",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Texto,
        Anulavel = true,
        Colacao = Padrao,
        IgnoraCaixaPeloTipo = false,
        IgnoraEspacoFinalPeloTipo = false,
        Comprimento = new LimiteComprimento { Valor = comprimento, Unidade = UnidadeComprimento.PontosDeCodigo },
        RestritaPeloTipo = false,
    };

    /// <summary>O esquema com a tabela trocada.</summary>
    internal static EsquemaBanco Trocar(EsquemaBanco esquema, string tabela, Func<TabelaEsquema, TabelaEsquema> trocar) =>
        BancoFalso.Clonar(esquema, (nameof(EsquemaBanco.Tabelas), esquema.Tabelas.Select(lida => lida.Nome.Equals(Nome(tabela)) ? trocar(lida) : lida).ToArray()));

    /// <summary>O esquema com a coluna trocada.</summary>
    internal static EsquemaBanco TrocarColuna(EsquemaBanco esquema, string tabela, string coluna, params (string Propriedade, object? Valor)[] trocas) =>
        Trocar(esquema, tabela, lida => BancoFalso.Clonar(lida, (nameof(TabelaEsquema.Colunas), lida.Colunas.Select(antiga => antiga.Nome == coluna ? BancoFalso.Clonar(antiga, trocas) : antiga).ToArray())));
}
