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
        var sequences = new SequenceEsquema[] { new() { Nome = Nome("clientes_id_seq"), Colunas = [new ReferenciaColuna(Nome("clientes"), "id")] } };
        return new EsquemaBanco
        {
            Tabelas = tabelas,
            Sequences = tabelas.Any(tabela => tabela.Nome.Equals(Nome("clientes"))) ? sequences : [],
            ObjetosResiduais = [],
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
        ChavesEstrangeiras = [new ChaveEstrangeira { Nome = "fk_pedidos_clientes", Colunas = ["cliente"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["id"], Imposta = true, Validada = true }],
    };

    internal static IdentidadeLida Identidade(string banco) => new()
    {
        Banco = banco,
        Servidor = "servidor-sintetico",
        Linhagem = new Dictionary<string, string> { ["identificador"] = banco == "loja" ? "1" : "2" },
        Transporte = new TransporteConexao { Cifrado = true, ServidorVerificado = true, Local = false },
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
