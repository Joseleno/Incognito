using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A fixture da suíte no PostgreSQL: a origem clonada do banco-modelo da origem, lida pelo leitor (nunca dono), e o destino
/// clonado do banco-modelo do destino, com o mesmo esquema e lido pelo dono sem superusuário. Declara o que o esquema de
/// conformidade tem na versão do servidor.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class FixturePostgreSql(ServidorPostgreSql servidor) : FixtureDialeto
{
    private const string Texto = "text";
    private const string Inteiro = "integer";
    private const string Grande = "bigint";
    private const string Variavel = "character varying";

    // A colação padrão dos bancos da fixture (o modelo vem do template1 da imagem, em libc), declarada uma vez e nunca
    // deduzida do tipo nativo; e a "C" do catálogo, a do tipo name.
    private static readonly ColacaoEsperada _padrao = ColacaoEsperada.Determinista("default", null);
    private static readonly ColacaoEsperada _c = ColacaoEsperada.Determinista("C", "pg_catalog");

    // As faixas dos inteiros pela documentação do PostgreSQL, aplicadas pelo tipo nativo; domínio e array declaram à mão.
    private static readonly Dictionary<string, FaixaEsperada> _faixas = new(StringComparer.Ordinal)
    {
        ["smallint"] = FaixaEsperada.Pequeno,
        [Inteiro] = FaixaEsperada.Inteiro,
        [Grande] = FaixaEsperada.Grande,
    };

    private readonly ProvedorPostgreSql _provedor = new();

    /// <inheritdoc/>
    public override EsquemaDeConformidade Esperado { get; } = Declarar(servidor.Versao);

    /// <inheritdoc/>
    public override ValueTask<ParDeBancos> CriarParAsync(CancellationToken cancelamento) =>
        CriarAsync(
            banco => servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor),
            banco => servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono),
            cancelamento);

    /// <inheritdoc/>
    public override ValueTask<ParDeBancos> CriarParPrivilegiadoAsync(CancellationToken cancelamento) =>
        CriarAsync(servidor.StringDeConexaoDoAdministrador, servidor.StringDeConexaoDoAdministrador, cancelamento);

    /// <inheritdoc/>
    public override ProvedorDialeto Provedor => _provedor;

    /// <inheritdoc/>
    // Dois servidores: recusado antes de abrir (INC3102), com o sentinela na senha e numa opção.
    public override SegredoConexao ConexaoRecusada(string sentinela) =>
        new($"Host=127.0.0.1,127.0.0.2;Database=loja_{sentinela};Username=u;Password={sentinela};Application Name={sentinela}");

    /// <inheritdoc/>
    public override Task<DialetoDestino> ConectarOutroDestinoAsync(ParDeBancos par, CancellationToken cancelamento) =>
        _provedor.ConectarDestinoAsync(new SegredoConexao(servidor.StringDeConexao(par.BancoDestino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);

    /// <inheritdoc/>
    public override Task ComentarAsync(
        ParDeBancos par, bool destino, ComentarioDoBanco comentario, string sentinela, Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento)
    {
        var banco = destino ? par.BancoDestino : par.BancoOrigem;
        var texto = comentario switch
        {
            ComentarioDoBanco.Alheio => "Comentário de quem administra o banco: " + sentinela,
            ComentarioDoBanco.Invalido => FormatoDoMarcador.Prefixo + sentinela,
            _ => FormatoDoMarcador.Texto(identificador, gravadoEm),
        };
        return servidor.ExecutarAsync(banco, $"COMMENT ON DATABASE \"{banco}\" IS '{texto.Replace("'", "''", StringComparison.Ordinal)}'", cancelamento);
    }

    /// <inheritdoc/>
    // Espera o fim da sessão (o segundo argumento): sem isso, o batimento seguinte ainda acharia o lock.
    public override Task DerrubarBloqueioAsync(ParDeBancos par, CancellationToken cancelamento) =>
        servidor.ExecutarAsync(
            par.BancoDestino,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"SELECT pg_catalog.pg_terminate_backend(l.pid, 5000) FROM pg_catalog.pg_locks l WHERE l.locktype = 'advisory' AND l.classid = {BloqueioPostgreSql.Classe} AND l.objid = {BloqueioPostgreSql.Execucao} AND l.objsubid = 2 AND l.database = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database())"),
            cancelamento);

    private async ValueTask<ParDeBancos> CriarAsync(Func<string, string> conexaoDaOrigem, Func<string, string> conexaoDoDestino, CancellationToken cancelamento)
    {
        var bancoOrigem = await servidor.ClonarModeloAsync(cancelamento);
        string? bancoDestino = null;
        DialetoOrigem? origem = null;
        try
        {
            bancoDestino = await servidor.CriarDestinoAsync(cancelamento);

            // Cada lado assina a publicação do outro que lista a partição eventos_1 (a assinatura não passa ao clone).
            await servidor.AssinarAsync(bancoOrigem, bancoDestino, "publicacao_sem_carga", cancelamento);
            await servidor.AssinarAsync(bancoDestino, bancoOrigem, "publicacao_sem_carga", cancelamento);
            origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(conexaoDaOrigem(bancoOrigem)), cancelamento);
            var destino = await _provedor.ConectarDestinoAsync(new SegredoConexao(conexaoDoDestino(bancoDestino)), cancelamento);
            var apagar = bancoDestino;
            return new ParDeBancos(origem, destino, bancoOrigem, bancoDestino, async () =>
            {
                try
                {
                    await servidor.ApagarAsync(bancoOrigem);
                }
                finally
                {
                    await servidor.ApagarAsync(apagar);
                }
            });
        }
        catch
        {
            // O destino é apagado mesmo que a limpeza da origem falhe.
            try
            {
                if (origem is not null)
                {
                    await origem.DisposeAsync();
                }

                await servidor.ApagarAsync(bancoOrigem);
            }
            finally
            {
                if (bancoDestino is not null)
                {
                    await servidor.ApagarAsync(bancoDestino);
                }
            }

            throw;
        }
    }

    private static EsquemaDeConformidade Declarar(int versao)
    {
        var dezoito = versao >= 180000;
        ColunaGeradaEsperada[] geradas =
        [
            new(Coluna("calculos", "dobro"), GeracaoColuna.Armazenada),
            .. dezoito ? [new ColunaGeradaEsperada(Coluna("calculos", "triplo"), GeracaoColuna.Virtual)] : Array.Empty<ColunaGeradaEsperada>(),
            new(Coluna("externa", "tamanho"), GeracaoColuna.Armazenada),
            new(Coluna("textos", "email_min"), GeracaoColuna.Armazenada),
            new(Coluna("textos", "junto"), GeracaoColuna.Armazenada),
            new(Coluna("numeros", "constante"), GeracaoColuna.Armazenada),
            new(Coluna("indexada", "minusculo"), GeracaoColuna.Armazenada),
            new(Coluna("alheia_colunas", "dobro"), GeracaoColuna.Armazenada),
        ];
        return new()
        {
            Inventario = Inventariar(versao),
            Inexistentes = new Dictionary<Enum, string> { [UnidadeComprimento.UnidadesUtf16] = "o PostgreSQL não mede em UTF-16" },

            // A versão é a do servidor, lida pelo administrador: o Dependabot sobe a secundária das imagens.
            Propriedades = Caso.Presente(new PropriedadesEsperadas(new Version(versao / 10000, versao % 10000), new CodificacaoEsperada("UTF8", true, true, 4), _padrao)),
            Sentinelas = [ServidorPostgreSql.Sentinela, ServidorPostgreSql.SentinelaCurto],
            Particao = Caso.Presente(new Hierarquia(Nome("pedidos"), [Nome("pedidos_1"), Nome("pedidos_2"), Nome("pedidos_9")])),
            Heranca = Caso.Presente(new Hierarquia(Nome("pessoa"), [Nome("funcionario")])),
            SequenceCompartilhada = Caso.Presente(new SequenceEsperada(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")])),
            ColunasGeradas = Caso.Presente<IReadOnlyList<ColunaGeradaEsperada>>(geradas),
            ChaveEstrangeiraComposta = new(Nome("itens"), "fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"]),
            Temporal = Caso.Inexistente<TemporalEsperada>("o PostgreSQL não tem tabela temporal"),
            SegurancaDeLinha = Caso.Presente<IReadOnlyList<NomeObjeto>>([Nome("forcada"), Nome("repartida_1"), Nome("segredos")]),
            Publicacao = Caso.Presente<IReadOnlyList<NomeObjeto>>([Nome("pedidos_2"), Nome("tipos")]),
            Assinatura = Caso.Presente<IReadOnlyList<NomeObjeto>>([Nome("eventos_1")]),
            Mascara = Caso.Inexistente<ReferenciaColuna>("o PostgreSQL não tem máscara dinâmica"),
            NomesExatos = [Nome("Cliente"), Nome("cliente"), Nome("select"), Nome("nota fiscal")],
            Citacoes = [new("Cliente", "\"Cliente\""), new("a\"b", "\"a\"\"b\""), new("nota fiscal", "\"nota fiscal\"")],

            // Banco UTF-8: sem aspas, só A-Z dobra; o limite do name é de 63 bytes (ç ocupa 2).
            ComparacoesDeNome =
            [
                new("Cliente", false, "cliente", CorrespondenciaNome.Corresponde),
                new("Cliente", false, "Cliente", CorrespondenciaNome.NaoCorresponde),
                new("Cliente", true, "Cliente", CorrespondenciaNome.Corresponde),
                new("Cliente", true, "cliente", CorrespondenciaNome.NaoCorresponde),
                new("Ação", false, "ação", CorrespondenciaNome.Corresponde),
                new("AÇÃO", false, "aÇÃo", CorrespondenciaNome.Corresponde),
                new("AÇÃO", false, "ação", CorrespondenciaNome.NaoCorresponde),
                new(new string('ç', 31) + "a", true, new string('ç', 31) + "a", CorrespondenciaNome.Corresponde),
                new(new string('ç', 32), true, new string('ç', 32), CorrespondenciaNome.NaoCorresponde),
                new(new string('a', 64), false, new string('a', 64), CorrespondenciaNome.NaoCorresponde),
            ],
            ObjetoDoIncognito = new NomeObjeto("incognito", "execucao"),
            Acesso = Caso.Presente(new AcessoEsperado(AcessoDoLeitor(), AcessoDoDono(versao))),

            // Pelo loopback com o TLS só preferido, num servidor sem TLS: em claro.
            Transporte = Caso.Presente(new TransporteEsperado(Cifrado: false, ServidorVerificado: false, Local: true)),

            // As linhas do fim do script: a particionada pela raiz (o 75 está em pedidos_2) e a mãe sem a herdeira (que tem 60).
            MaioresValores = new()
            {
                Comum = new(Coluna("lojas", "id"), 91),
                SemValor = new(Coluna("cliente", "id"), null),
                SoNegativos = new(Coluna("Cliente", "id"), -2),
                Particionada = Caso.Presente(new MaiorValorEsperado(Coluna("pedidos", "id"), 75)),
                MaeDeHeranca = Caso.Presente(new MaiorValorEsperado(Coluna("pessoa", "id"), 12)),
                ColunaDeTexto = Coluna("tipos", "texto"),
            },

            // As linhas do fim do script, lidas pelo dono no destino: a mãe de herança múltipla só tem a linha da herdeira
            // (loja.ficha), e a loja.forcada força a segurança por linha também para o dono.
            Linhas = new()
            {
                ComLinha = Nome("lojas"),
                Vazia = Nome("cliente"),
                Particionada = Caso.Presente(Nome("pedidos")),
                MaeComLinhaSoNaHerdeira = Caso.Presente(Nome("contato")),
                ComSegurancaPorLinha = Caso.Presente(Nome("forcada")),
            },
        };
    }

    // O leitor da origem lê tudo do esquema loja, menos uma coluna de uma tabela e a tabela sem colunas; não lê fora dele (sem
    // USAGE ou sem SELECT) e
    // tem a leitura filtrada onde há segurança por linha. Não grava, não limpa, não ajusta e não marca o banco.
    private static LadoDoAcesso AcessoDoLeitor()
    {
        var porLinha = Leitor(podeLer: true) with { LeituraFiltradaPorLinha = true, EscritaRestritaPorLinha = true };
        return new()
        {
            Banco = new(PodeMarcarBanco: false, PodeCriarEsquema: false),
            Tabelas = Leitor(podeLer: true),
            ExcecoesDasTabelas = new Dictionary<NomeObjeto, PermissoesTabelaEsperadas>
            {
                [Nome("parcial")] = Leitor(podeLer: false) with { ColunasSemLeitura = ["nota"] },
                [Nome("vazia")] = Leitor(podeLer: false),
                [new NomeObjeto("restrito", "fechada")] = Leitor(podeLer: false),
                [new NomeObjeto("alheio", "do_dono")] = Leitor(podeLer: false) with { ColunasSemLeitura = ["id"] },
                [new NomeObjeto("pgdados", "clientes")] = Leitor(podeLer: false) with { ColunasSemLeitura = ["id"] },
                [Nome("segredos")] = porLinha,
                [Nome("forcada")] = porLinha,
                [Nome("repartida_1")] = porLinha,
            },
            PodeLimpar = false,
            PodeAjustar = false,
        };
    }

    // O dono do destino pode tudo no que é dele, fora a segurança por linha forçada; nas tabelas do terceiro papel e na da
    // extensão (que continua do administrador), só o que lhe foi concedido; e nada na tabela dele num esquema sem USAGE.
    private static LadoDoAcesso AcessoDoDono(int versao)
    {
        var nada = new PermissoesTabelaEsperadas(false, false, false, false, false, false, false, false, false);
        return new()
        {
            Banco = new(PodeMarcarBanco: true, PodeCriarEsquema: true),
            Tabelas = new(true, true, true, true, true, true, true, false, false),
            ExcecoesDasTabelas = new Dictionary<NomeObjeto, PermissoesTabelaEsperadas>
            {
                [Nome("alheia")] = nada with { PodeEsvaziar = true, PodeReferenciar = true, ColunasSemLeitura = ["id"] },
                [Nome("alheia_colunas")] = nada with { PodeInserir = true, PodeGravarIdentidade = true, PodeReferenciar = true, ColunasSemLeitura = ["id", "nota", "dobro"] },
                [Nome("alheia_insercao")] = nada with { PodeInserir = true, PodeGravarIdentidade = true, ColunasSemLeitura = ["id"] },
                [Nome("vazia_alheia")] = nada with { PodeInserir = true, PodeGravarIdentidade = true, PodeReferenciar = true },
                [new NomeObjeto("alheio", "do_dono")] = nada,
                [Nome("da_extensao")] = nada with { ColunasSemLeitura = ["id"] },
                [Nome("forcada")] = new(true, true, true, true, true, true, true, true, true),
            },
            PodeLimpar = true,
            ExcecoesDosResiduais = new Dictionary<NomeObjeto, bool?>
            {
                [Nome("resumo_alheio")] = false,
                [Nome("resumo_mantido")] = versao >= 170000,
                [new NomeObjeto("pg_catalog", "pg_largeobject")] = false,
            },
            PodeAjustar = true,
            ExcecoesDasSequences = new Dictionary<NomeObjeto, bool?> { [Nome("seq_alheia")] = false, [new NomeObjeto("alheio", "seq_do_dono")] = false },
        };
    }

    // As permissões de quem só lê (ou nem isso).
    private static PermissoesTabelaEsperadas Leitor(bool podeLer) => new(podeLer, false, false, false, false, false, false, false, false);

    // Escrito à mão a partir do esquema-de-conformidade.sql, nunca copiado da saída do leitor: a classe de cada coluna pela
    // matriz de tipos da especificação, a semântica pelo DDL. O tipo nativo é o do format_type com o caminho de busca só no
    // pg_catalog (os tipos de extensão e de domínio saem com o esquema).
    private static Inventario Inventariar(int versao)
    {
        var dezesseis = versao >= 160000;
        var dezessete = versao >= 170000;
        var dezoito = versao >= 180000;
        var suportadoInteiro = Classe.Suportado(TipoColuna.Inteiro);
        var suportadoTexto = Classe.Suportado(TipoColuna.Texto);
        ColunaInventariada[] pedido = [Col("loja", Inteiro, suportadoInteiro, nula: false), Col("id", Grande, suportadoInteiro, nula: false), Col("numero", Grande, suportadoInteiro)];
        ColunaInventariada[] venda = [Col("ano", Inteiro, suportadoInteiro, nula: false), Col("mes", Inteiro, suportadoInteiro, nula: false)];
        ColunaInventariada[] agenda =
        [
            Col("dia", "date", Classe.Suportado(TipoColuna.Data), nula: false),
            ColTexto("sala", Texto, suportadoTexto),
            ColTexto("nota", Texto, suportadoTexto),
            ColTexto("turno", Texto, suportadoTexto),
        ];
        ColunaInventariada[] leitura = [ColTexto("origem", Texto, suportadoTexto, nula: false), ColTexto("valor", Texto, suportadoTexto)];
        ColunaInventariada[] leituraArquivo = [ColTexto("origem", Texto, Classe.NaoSuportado, nula: false), ColTexto("valor", Texto, Classe.NaoSuportado)];
        TabelaInventariada[] tabelas =
        [
            new()
            {
                Nome = Nome("tipos"),
                Publicada = true,
                Colunas =
                [
                    Col("id", Grande, suportadoInteiro, nula: false),
                    Col("pequeno", "smallint", suportadoInteiro),
                    Col("inteiro", Inteiro, suportadoInteiro),
                    Col("valor", "numeric", Classe.Suportado(TipoColuna.Decimal), precisao: 12, escala: 2),
                    ColTexto("texto", Texto, suportadoTexto, nula: false),
                    ColTexto("curto", Variavel, suportadoTexto, comprimento: 20),
                    ColTexto("fixo", "character", suportadoTexto, comprimento: 3, espacoFinal: true),
                    Col("ativo", "boolean", Classe.Suportado(TipoColuna.Booleano)),
                    Col("dia", "date", Classe.Suportado(TipoColuna.Data)),
                    Col("momento", "timestamp without time zone", Classe.Suportado(TipoColuna.DataHora)),
                    Col("instante", "timestamp with time zone", Classe.Suportado(TipoColuna.DataHoraComFuso)),
                    Col("identificador", "uuid", Classe.Suportado(TipoColuna.Uuid)),
                    Col("conteudo", "bytea", Classe.Suportado(TipoColuna.Binario), comprimento: new(null, UnidadeComprimento.Bytes)),
                    Col("hora", "time without time zone", Classe.SemTipo),
                    Col("hora_com_fuso", "time with time zone", Classe.SemTipo),
                    Col("duracao", "interval", Classe.SemTipo),
                    ColTexto("email", "public.citext", suportadoTexto, caixa: true),
                    ColTexto("emails", "public.citext[]", Classe.Ressalva, caixa: true),
                    Col("dados", "jsonb", Classe.Ressalva),
                    Col("documento", "json", Classe.Ressalva),
                    Col("marcacao", "xml", Classe.Ressalva),
                    Col("preco", "money", Classe.Ressalva),
                    Col("situacao", "loja.situacao", Classe.Ressalva),
                    ColTexto("codigo", "loja.codigo", Classe.Ressalva, tipoBase: Texto, restrita: true),
                    Col("faixa", "int4range", Classe.Ressalva),
                    Col("lista", "integer[]", Classe.Ressalva, faixa: FaixaEsperada.Inteiro),
                    Col("real", "real", Classe.Ressalva),
                    Col("dupla", "double precision", Classe.Ressalva),
                    Col("endereco", "inet", Classe.Ressalva),
                    Col("anexo", "public.lo", Classe.NaoSuportado, tipoBase: "oid"),
                    Col("referencia", "oid", Classe.NaoSuportado),
                    ColTexto("observacao", Texto, suportadoTexto),
                ],
                ChavePrimaria = ["id"],
                IndicesUnicos = [Indice("ix_tipos_curto", [Elemento("curto")], predicado: new(["id", "curto"])), Primario("tipos_pkey", "id")],
                Checks = [new("ck_tipos_texto", new(["texto"]))],
            },
            new()
            {
                Nome = Nome("objetos"),
                Colunas =
                [
                    Col("id", Grande, suportadoInteiro, nula: false),
                    ColTexto("codigo", "loja.codigo_curto", Classe.Ressalva, tipoBase: Texto, restrita: true),
                    Col("referencia", "loja.ref_objeto", Classe.NaoSuportado, tipoBase: "oid"),
                    Col("referencias", "oid[]", Classe.NaoSuportado),
                    Col("anexos", "public.lo[]", Classe.NaoSuportado),
                    Col("lista", "loja.lista_lo", Classe.NaoSuportado, tipoBase: "public.lo[]"),
                ],
                ChavePrimaria = ["id"],
                IndicesUnicos = [Primario("objetos_pkey", "id")],
            },
            new()
            {
                Nome = Nome("imitacoes"),
                Colunas = [ColTexto("parecido", "loja.citext", Classe.Ressalva, tipoBase: Texto), ColTexto("adotado", "loja.texto_adotado", Classe.Ressalva, tipoBase: Texto)],
            },
            new() { Nome = Nome("da_extensao"), Extensao = "citext", Colunas = [Col("id", Inteiro, suportadoInteiro)] },
            new()
            {
                Nome = Nome("externa"),
                Estrangeira = true,
                Colunas =
                [
                    ColTexto("linha", Texto, Classe.NaoSuportado),
                    Col("tamanho", Inteiro, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["linha"])),
                ],
            },
            new()
            {
                Nome = Nome("calculos"),
                Colunas =
                [
                    Col("id", Grande, suportadoInteiro, nula: false),
                    Col("valor", Inteiro, suportadoInteiro, nula: false),
                    Col("dobro", Inteiro, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["valor"])),
                    .. dezoito ? [Col("triplo", Inteiro, Classe.Exclusao, geracao: GeracaoColuna.Virtual, referencias: new(["valor"]))] : Array.Empty<ColunaInventariada>(),
                ],
                ChavePrimaria = ["id"],
                IndicesUnicos = [Primario("calculos_pkey", "id")],
            },
            new()
            {
                Nome = Nome("contadores"),
                Colunas =
                [
                    Col("sempre", Grande, suportadoInteiro, nula: false, identidade: IdentidadeColuna.Sempre),
                    Col("por_padrao", Grande, suportadoInteiro, nula: false, identidade: IdentidadeColuna.PorPadrao),
                    Col("serie", Inteiro, suportadoInteiro, nula: false),
                ],
                ChavePrimaria = ["sempre"],
                IndicesUnicos = [Primario("contadores_pkey", "sempre")],
            },
            new()
            {
                Nome = Nome("eventos"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["tipo"]),
                Colunas = [Col("id", Grande, suportadoInteiro, nula: false, identidade: IdentidadeColuna.Sempre), Col("tipo", Inteiro, suportadoInteiro, nula: false)],
            },
            new()
            {
                Nome = Nome("eventos_1"),
                Tipo = TipoTabela.Particao,
                Pai = Nome("eventos"),
                Assinada = true,
                Colunas =
                [
                    Col("id", Grande, suportadoInteiro, nula: false, identidade: dezessete ? IdentidadeColuna.Sempre : IdentidadeColuna.Nenhuma),
                    Col("tipo", Inteiro, suportadoInteiro, nula: false),
                ],
            },
            new() { Nome = Nome("lojas"), Colunas = [Col("id", Inteiro, suportadoInteiro, nula: false)], ChavePrimaria = ["id"], IndicesUnicos = [Primario("lojas_pkey", "id")] },
            new()
            {
                Nome = Nome("pedidos"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["loja"]),
                Colunas = pedido,
                ChavePrimaria = ["loja", "id"],
                ChavesEstrangeiras = [new("pedidos_loja_fkey", ["loja"], Nome("lojas"), ["id"])],
                IndicesUnicos = [Primario("pedidos_pkey", "loja", "id")],
            },
            Particao("pedidos_1", "pedidos", pedido, ["loja", "id"]),
            Particao("pedidos_2", "pedidos", pedido, ["loja", "id"], publicada: true),
            Particao(
                "pedidos_9",
                "pedidos",
                [Col("loja", Inteiro, suportadoInteiro, nula: false), Col("id", Grande, suportadoInteiro, nula: false), Col("numero", Grande, suportadoInteiro, nula: false)],
                ["loja", "id"]),
            new() { Nome = Nome("regioes"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["nome"]), Colunas = [ColTexto("nome", Texto, suportadoTexto, nula: false)] },
            Particao("regioes_sentinela", "regioes", [ColTexto("nome", Texto, suportadoTexto, nula: false)]),
            new()
            {
                Nome = Nome("itens"),
                Colunas =
                [
                    Col("pedido", Grande, suportadoInteiro, nula: false),
                    Col("loja", Inteiro, suportadoInteiro, nula: false),
                    Col("numero", Grande, suportadoInteiro),
                    ColTexto("produto", Texto, suportadoTexto),
                ],
                ChavesEstrangeiras = [new("fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"])],
            },
            new() { Nome = Nome("pessoa"), Colunas = [Col("id", Grande, suportadoInteiro), ColTexto("apelido", Texto, suportadoTexto)], Checks = [new("ck_pessoa_id", new(["id"]))] },
            new()
            {
                Nome = Nome("funcionario"),
                Tipo = TipoTabela.Herdeira,
                Pai = Nome("pessoa"),
                Colunas = [Col("id", Grande, suportadoInteiro), ColTexto("apelido", Texto, suportadoTexto), Col("matricula", Inteiro, suportadoInteiro)],

                // O CHECK herdado vale na herdeira, que é copiada sozinha.
                Checks = [new("ck_pessoa_id", new(["id"]))],
            },
            new() { Nome = Nome("Cliente"), Colunas = [Col("id", Grande, suportadoInteiro, nula: false)], ChavePrimaria = ["id"], IndicesUnicos = [Primario("Cliente_pkey", "id")] },
            new() { Nome = Nome("cliente"), Colunas = [Col("id", Grande, suportadoInteiro, nula: false)], ChavePrimaria = ["id"], IndicesUnicos = [Primario("cliente_pkey", "id")] },
            new()
            {
                Nome = Nome("select"),
                Colunas =
                [
                    ColTexto("from", Texto, suportadoTexto),
                    ColTexto("coluna com espaço", Texto, suportadoTexto),
                    ColTexto("a\"b", Texto, suportadoTexto),
                    ColTexto("valor €", Texto, suportadoTexto),
                ],
            },
            new() { Nome = Nome("nota fiscal"), Colunas = [Col("id", Grande, suportadoInteiro)] },
            new()
            {
                Nome = Nome("chave_invertida"),
                Colunas = [Col("a", Inteiro, suportadoInteiro, nula: false), Col("b", Inteiro, suportadoInteiro, nula: false)],
                ChavePrimaria = ["b", "a"],
                IndicesUnicos = [Primario("chave_invertida_pkey", "b", "a")],
            },
            new() { Nome = Nome("vendas"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["ano"]), Colunas = venda },
            new() { Nome = Nome("vendas_2026"), Tipo = TipoTabela.Particao, Pai = Nome("vendas"), ChaveParticao = new(["mes"]), Colunas = venda },
            Particao("vendas_2026_01", "vendas_2026", venda),
            new() { Nome = Nome("agenda"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["sala", "dia", "nota", "turno"]), Colunas = agenda },
            Particao("agenda_toda", "agenda", agenda),
            new()
            {
                Nome = Nome("arquivo_morto"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new([], ColunasIncompletas: true),
                Colunas = [Col("id", Grande, suportadoInteiro), ColTexto("dados", Texto, suportadoTexto)],
            },
            new()
            {
                Nome = Nome("sem_colunas"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new([], ColunasIncompletas: true),
                Colunas = [],
                Checks = [new("ck_sem_colunas_linha", new([], Incompletas: true))],
            },
            new() { Nome = Nome("leituras"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["origem"]), Colunas = leitura },
            new() { Nome = Nome("leituras_arquivo"), Tipo = TipoTabela.Particao, Pai = Nome("leituras"), Estrangeira = true, Colunas = leituraArquivo },
            new() { Nome = Nome("contato"), Colunas = [ColTexto("canal", Texto, suportadoTexto)] },
            new() { Nome = Nome("endereco"), Colunas = [ColTexto("cidade", Texto, suportadoTexto)] },
            new() { Nome = Nome("documento"), Colunas = [ColTexto("numero", Texto, suportadoTexto)] },
            new()
            {
                Nome = Nome("ficha"),
                Tipo = TipoTabela.Herdeira,
                Pai = Nome("contato"),
                OutrosPais = [Nome("endereco"), Nome("documento")],
                Colunas = [ColTexto("canal", Texto, suportadoTexto), ColTexto("cidade", Texto, suportadoTexto), ColTexto("numero", Texto, suportadoTexto)],
            },
            new() { Nome = Nome("mae_um"), Colunas = [ColTexto("um", Texto, suportadoTexto)] },
            new() { Nome = Nome("mae_dois"), Colunas = [ColTexto("dois", Texto, suportadoTexto)] },
            new() { Nome = Nome("mae_tres"), Colunas = [ColTexto("tres", Texto, suportadoTexto)] },
            new()
            {
                Nome = Nome("sem_a_primeira"),
                Tipo = TipoTabela.Herdeira,
                Pai = Nome("mae_dois"),
                OutrosPais = [Nome("mae_tres")],
                Colunas = [ColTexto("um", Texto, suportadoTexto), ColTexto("dois", Texto, suportadoTexto), ColTexto("tres", Texto, suportadoTexto)],
            },
            new() { Nome = Nome("com_dominio"), Colunas = [ColTexto("valor", "loja.obrigatorio", Classe.Ressalva, tipoBase: Texto)] },
            new()
            {
                Nome = Nome("protocolos"),
                Colunas =
                [
                    Col("numero", "loja.protocolo", Classe.Ressalva, tipoBase: Grande, faixa: FaixaEsperada.Grande),
                    Col("proprio", "loja.protocolo", Classe.Ressalva, tipoBase: Grande, faixa: FaixaEsperada.Grande),
                    Col("base", "loja.numero_base", Classe.Ressalva, tipoBase: Grande, faixa: FaixaEsperada.Grande),
                    Col("derivado", "loja.numero_derivado", Classe.Ressalva, tipoBase: Grande, faixa: FaixaEsperada.Grande),
                ],
            },
            .. dezoito ? [new TabelaInventariada { Nome = Nome("pendente"), Colunas = [ColTexto("valor", Texto, suportadoTexto)] }] : Array.Empty<TabelaInventariada>(),
            new()
            {
                Nome = Nome("segredos"),
                SegurancaDeLinha = true,
                Colunas = [Col("id", Grande, suportadoInteiro, nula: false), ColTexto("conteudo", Texto, suportadoTexto)],
                ChavePrimaria = ["id"],
                IndicesUnicos = [Primario("segredos_pkey", "id")],
                Triggers = [new("tg_segredos")],
            },
            new()
            {
                Nome = Nome("reservas"),
                Colunas = [Col("sala", Inteiro, suportadoInteiro), Col("periodo", "int4range", Classe.Ressalva), ColTexto("nota", Texto, suportadoTexto)],

                // A coluna só incluída fica de fora; a do predicado entra.
                Exclusoes = [new("ex_reservas", new(["periodo", "nota"])), new("ex_reservas_expressao", new(["nota"])), new("ex_reservas_sala", new(["sala", "periodo"]))],
            },
            new()
            {
                Nome = Nome("textos"),
                Colunas =
                [
                    ColTexto("limitado", Variavel, suportadoTexto, comprimento: 10),
                    ColTexto("livre", Variavel, suportadoTexto),
                    ColTexto("fixo", "character", suportadoTexto, comprimento: 3, espacoFinal: true),
                    ColTexto("fixos", "character[]", Classe.Ressalva, comprimento: 3, espacoFinal: true),
                    ColTexto("um", "character", suportadoTexto, comprimento: 1, espacoFinal: true),
                    ColTexto("listas", "character varying[]", Classe.Ressalva, comprimento: 10),
                    ColTexto("do_dominio", "loja.d_varchar", Classe.Ressalva, tipoBase: Variavel, comprimento: 12),
                    ColTexto("do_dominio_de_dominio", "loja.d_d_varchar", Classe.Ressalva, tipoBase: Variavel, comprimento: 12),
                    ColTexto("dominio_de_lista", "loja.d_lista", Classe.Ressalva, tipoBase: "character varying[]", comprimento: 5),
                    ColTexto("lista_de_dominio", "loja.d_varchar[]", Classe.Ressalva, comprimento: 12),
                    ColTexto("nome_do_catalogo", "name", Classe.Ressalva, colacao: _c, limite: new(63, UnidadeComprimento.Bytes)),
                    ColTexto("caractere", "\"char\"", Classe.Ressalva, semColacao: true, limite: new(1, UnidadeComprimento.Bytes)),
                    ColTexto("binaria", Texto, suportadoTexto, colacao: _c),
                    ColTexto("nivel2", Texto, suportadoTexto, colacao: Medida("ci_nivel2", "Idioma\"X", caixa: true)),
                    ColTexto("sem_acento", Texto, suportadoTexto, colacao: Medida("sem_acento", "Idioma\"X", caixa: true, acento: true)),
                    ColTexto("pontuacao", Texto, suportadoTexto, colacao: Medida("pontuacao", "Idioma\"X", pontuacao: true)),
                    ColTexto("hostil", Texto, suportadoTexto, colacao: Medida("Caixa\"Alta.ç;--'", "Idioma\"X", caixa: true)),
                    ColTexto("sentinela", Texto, suportadoTexto, colacao: Medida("colacao_sentinela", "loja", caixa: true)),
                    ColTexto("caixa_do_dominio", "loja.d_citext", Classe.Ressalva, tipoBase: "public.citext", caixa: true),
                    ColTexto("fixo_do_dominio", "loja.d_fixo", Classe.Ressalva, tipoBase: "character", comprimento: 2, espacoFinal: true),
                    ColTexto("codigos", "loja.codigo_curto[]", Classe.Ressalva, restrita: true),
                    ColTexto("email", Texto, suportadoTexto),
                    ColTexto("email_min", Texto, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["email"])),
                    ColTexto("junto", Texto, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["limitado", "livre"])),
                    .. dezesseis
                        ? [ColTexto("com_regras", Texto, suportadoTexto, colacao: new ColacaoEsperada("colacao_regras", "loja", false, true, null, null, null))]
                        : Array.Empty<ColunaInventariada>(),
                ],
            },
            new()
            {
                Nome = Nome("numeros"),
                Colunas =
                [
                    Col("centenas", "numeric", Classe.Suportado(TipoColuna.Decimal), precisao: 5, escala: -2),
                    Col("livre", "numeric", Classe.Suportado(TipoColuna.Decimal)),
                    Col("positivo", "loja.numero", Classe.Ressalva, tipoBase: Inteiro, faixa: FaixaEsperada.Inteiro, restrita: true),
                    Col("positivo_derivado", "loja.numero_livre", Classe.Ressalva, tipoBase: Inteiro, faixa: FaixaEsperada.Inteiro, restrita: true),
                    ColTexto("documento", "loja.d_d_cpf", Classe.Ressalva, tipoBase: Variavel, comprimento: 11, restrita: true),
                    Col("valor_do_dominio", "loja.d_valor", Classe.Ressalva, tipoBase: "numeric", precisao: 8, escala: 3),
                    Col("constante", Inteiro, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new([])),
                ],
            },
            .. ObjetosDaTabela(versao),
            new() { Nome = Nome("alheia"), Colunas = [Col("id", Grande, suportadoInteiro, nula: false)], ChavePrimaria = ["id"], IndicesUnicos = [Primario("alheia_pkey", "id")] },

            // As permissões: as tabelas de cada caso de leitor e de dono.
            new() { Nome = new NomeObjeto("restrito", "fechada"), Colunas = [Col("id", Grande, suportadoInteiro)] },
            new() { Nome = Nome("parcial"), Colunas = [Col("id", Grande, suportadoInteiro), ColTexto("nota", Texto, suportadoTexto)] },
            new() { Nome = Nome("forcada"), SegurancaDeLinha = true, Colunas = [Col("id", Grande, suportadoInteiro)] },
            new() { Nome = Nome("repartida"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["id"]), Colunas = [Col("id", Inteiro, suportadoInteiro)] },
            new() { Nome = Nome("repartida_1"), Tipo = TipoTabela.Particao, Pai = Nome("repartida"), SegurancaDeLinha = true, Colunas = [Col("id", Inteiro, suportadoInteiro)] },
            new() { Nome = new NomeObjeto("alheio", "do_dono"), Colunas = [Col("id", Grande, suportadoInteiro)] },
            new() { Nome = Nome("alheia_insercao"), Colunas = [Col("id", Grande, suportadoInteiro)] },
            new() { Nome = Nome("vazia"), Colunas = [] },
            new() { Nome = Nome("vazia_alheia"), Colunas = [] },
            new()
            {
                Nome = Nome("alheia_colunas"),
                Colunas = [Col("id", Grande, suportadoInteiro), ColTexto("nota", Texto, suportadoTexto), Col("dobro", Grande, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["id"]))],
            },
            new()
            {
                Nome = new NomeObjeto("pgdados", "clientes"),
                Colunas = [Col("id", Grande, suportadoInteiro, nula: false)],
                ChavePrimaria = ["id"],
                IndicesUnicos = [Primario("clientes_pkey", "id")],
            },
        ];
        SequenceEsperada[] sequences =
        [
            new(Nome("contadores_sempre_seq"), [Coluna("contadores", "sempre")]),
            new(Nome("contadores_por_padrao_seq"), [Coluna("contadores", "por_padrao")]),
            new(Nome("contadores_serie_seq"), [Coluna("contadores", "serie")]),
            new(Nome("eventos_id_seq"), [Coluna("eventos", "id")]),
            new(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")]),
            new(Nome("pedidos_9_numero_seq"), [Coluna("pedidos_9", "numero")]),
            new(Nome("seq_avulsa"), []),
            new(Nome("seq_protocolo"), [Coluna("protocolos", "base"), Coluna("protocolos", "numero")]),
            new(Nome("seq_alheia"), []),
            new(new NomeObjeto("alheio", "seq_do_dono"), []),
        ];
        ResidualInventariado[] residuais =
        [
            Visao("resumo_vendas"),
            Visao("resumo_alheio"),
            Visao("resumo_mantido"),
            Visao("mv_base"),
            Visao("mv_derivada") with { DependeDe = [Nome("mv_base"), Nome("resumo_vendas")] },
            Visao("mv_vazia", populada: false),
            Visao("mv_por_funcao", incompletas: true),
            Visao("mv_por_operador", incompletas: true),
            Visao("mv_de_volta", incompletas: true),
            Visao("mv_ciclo_a", incompletas: true),
            Visao("mv_ciclo_b", incompletas: true),
            new(new NomeObjeto("pg_catalog", "pg_largeobject"), TipoObjetoResidual.ObjetosGrandes),
        ];

        // O esquema do diário: a tabela e o índice da chave dela, a particionada e o índice dela, a foreign table, a
        // materialized view, a sequence e a view.
        NomeObjeto[] reservado =
        [
            new("incognito", "execucao"), new("incognito", "execucao_pkey"), new("incognito", "lotes"), new("incognito", "lotes_id"),
            new("incognito", "remota"), new("incognito", "resumo"), new("incognito", "seq_interna"), new("incognito", "vista"),
        ];
        return new(tabelas, sequences, residuais, reservado);
    }

    // As tabelas dos objetos da tabela: índices únicos, CHECKs, exclusões, triggers e o estado das chaves estrangeiras.
    private static TabelaInventariada[] ObjetosDaTabela(int versao)
    {
        var dezessete = versao >= 170000;
        var dezoito = versao >= 180000;
        var suportadoInteiro = Classe.Suportado(TipoColuna.Inteiro);
        var suportadoTexto = Classe.Suportado(TipoColuna.Texto);
        ColunaInventariada[] lote = [Col("id", Inteiro, suportadoInteiro), Col("parte", Inteiro, suportadoInteiro)];
        ColunaInventariada[] remessa = [Col("loja", Inteiro, suportadoInteiro), Col("parte", Inteiro, suportadoInteiro, nula: false)];
        ColunaInventariada[] sala = [Col("sala", Inteiro, suportadoInteiro), Col("periodo", "int4range", Classe.Ressalva)];
        return
        [
            new()
            {
                Nome = Nome("indexada"),
                Colunas =
                [
                    Col("id", Grande, suportadoInteiro, nula: false),
                    Col("a", Inteiro, suportadoInteiro),
                    Col("b", Inteiro, suportadoInteiro),
                    ColTexto("g", Texto, suportadoTexto),
                    ColTexto("apelido", Texto, suportadoTexto),
                    ColTexto("email", Texto, suportadoTexto),
                    Col("ini", Inteiro, suportadoInteiro),
                    Col("fim", Inteiro, suportadoInteiro),
                    ColTexto("minusculo", Texto, Classe.Exclusao, geracao: GeracaoColuna.Armazenada, referencias: new(["email"])),
                ],
                ChavePrimaria = ["id"],
                IndicesUnicos =
                [
                    Primario("indexada_pkey", "id"),
                    Indice("ix_indexada_apelido", [Elemento("apelido", Medida("ci_nivel2", "Idioma\"X", caixa: true))]),
                    Indice("ix_indexada_colacoes", [Elemento("g", Medida("Caixa\"Alta.ç;--'", "Idioma\"X", caixa: true)), Elemento("apelido", Medida("colacao_sentinela", "loja", caixa: true))]),

                    // Expressões e predicado: as colunas citadas, na ordem da tabela; a expressão de texto tem a colação padrão.
                    Indice("ix_indexada_expressoes", [Elemento(null), Elemento(null, colacao: null)], expressoes: new(["email", "ini", "fim"]), predicado: new(["id", "b"])),
                    Indice("ix_indexada_linha", [Elemento(null)], expressoes: new([], Incompletas: true)),
                    Indice("ix_indexada_minusculo", [Elemento("minusculo")]),
                    Indice("ix_indexada_misto", [Elemento("g"), Elemento(null, colacao: null)], expressoes: new(["ini"])),
                    Indice("uq_indexada_ab", [Elemento("a", colacao: null), Elemento("b", colacao: null)], nulosDistintos: false),

                    // A restrição única sobre texto tem a colação da coluna.
                    Indice("uq_indexada_email", [Elemento("email")]),
                ],
                Checks =
                [
                    new("ck_indexada_constante", new([])),
                    new("ck_indexada_faixa", new(["ini", "fim"])),
                    .. dezoito ? [new CheckInventariado("ck_indexada_nao_imposto", new(["b"]), Imposta: false)] : Array.Empty<CheckInventariado>(),
                    new("ck_indexada_nao_validado", new(["a"])),
                ],
            },
            new() { Nome = Nome("checada"), Colunas = [Col("id", Inteiro, suportadoInteiro), ColTexto("nota", Texto, suportadoTexto)], Checks = [new("ck_checada_linha", new([], Incompletas: true))] },
            new()
            {
                Nome = Nome("lotes"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["parte"]),
                Colunas = lote,

                // O índice só do pai (inválido) é listado como os outros.
                IndicesUnicos = [Indice("ix_lotes_so_pai", [Elemento("id", colacao: null), Elemento("parte", colacao: null)])],
                Checks = [new("ck_lotes_parte", new(["parte"]))],
                Triggers = [new("tg_lotes")],
            },
            new()
            {
                Nome = Nome("lotes_1"),
                Tipo = TipoTabela.Particao,
                Pai = Nome("lotes"),
                Colunas = lote,
                IndicesUnicos = [Indice("ix_lotes_1_proprio", [Elemento("id", colacao: null)])],
                Checks = [new("ck_lotes_1_local", new(["id"]))],
                Triggers = [new("tg_lotes", Herdada: true), new("tg_lotes_1_proprio")],
            },
            .. dezessete
                ?
                [
                    new TabelaInventariada { Nome = Nome("salas"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["sala"]), Colunas = sala, Exclusoes = [new("ex_salas", new(["sala", "periodo"]))] },
                    new TabelaInventariada { Nome = Nome("salas_1"), Tipo = TipoTabela.Particao, Pai = Nome("salas"), Colunas = sala },
                ]
                : Array.Empty<TabelaInventariada>(),

            // A chave primária com WITHOUT OVERLAPS é exclusão, sem índice único.
            .. dezoito
                ?
                [
                    new TabelaInventariada
                    {
                        Nome = Nome("periodos"),
                        Colunas = [Col("sala", Inteiro, suportadoInteiro, nula: false), Col("periodo", "int4range", Classe.Ressalva, nula: false)],
                        ChavePrimaria = ["sala", "periodo"],
                        Exclusoes = [new("periodos_pkey", new(["sala", "periodo"]))],
                    },
                ]
                : Array.Empty<TabelaInventariada>(),
            new()
            {
                Nome = Nome("disparos"),
                Colunas = [Col("id", Inteiro, suportadoInteiro), ColTexto("valor", Texto, suportadoTexto)],
                Triggers = [new("tg_desligada", Ligada: false), new("tg_esvaziar"), new("tg_ligada"), new("tg_replica", Ligada: false), new("tg_sempre")],
            },
            Vinculo("vinculo_desligado", new("fk_desligado", ["loja"], Nome("lojas"), ["id"], Imposta: false)),
            Vinculo("vinculo_replica", new("fk_replica", ["loja"], Nome("lojas"), ["id"], Imposta: false)),
            Vinculo("vinculo_pendente", new("fk_pendente", ["loja"], Nome("lojas"), ["id"], Validada: false)),
            .. dezoito ? [Vinculo("vinculo_solto", new("fk_solto", ["loja"], Nome("lojas"), ["id"], Imposta: false, Validada: false))] : Array.Empty<TabelaInventariada>(),

            // A integridade desligada só numa partição desliga a chave do pai; as cópias nas partições não são listadas.
            new()
            {
                Nome = Nome("remessas"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["parte"]),
                Colunas = remessa,
                ChavesEstrangeiras = [new("fk_remessas", ["loja"], Nome("lojas"), ["id"], Imposta: false)],
            },
            Particao("remessas_1", "remessas", remessa),
            Particao("remessas_2", "remessas", remessa),
            new() { Nome = Nome("duplicadas"), Colunas = [Col("v", Inteiro, suportadoInteiro)], IndicesUnicos = [Indice("ix_duplicadas_v", [Elemento("v", colacao: null)])] },
        ];
    }

    private static TabelaInventariada Vinculo(string nome, ChaveEstrangeiraInventariada chave) =>
        new() { Nome = Nome(nome), Colunas = [Col("loja", Inteiro, Classe.Suportado(TipoColuna.Inteiro))], ChavesEstrangeiras = [chave] };

    // O índice da chave primária, sobre colunas sem colação.
    private static IndiceInventariado Primario(string nome, params string[] colunas) =>
        new(nome, [.. colunas.Select(coluna => Elemento(coluna, colacao: null))], Primario: true);

    private static IndiceInventariado Indice(string nome, ElementoEsperado[] elementos, ReferenciasEsperadas? expressoes = null, ReferenciasEsperadas? predicado = null, bool nulosDistintos = true) =>
        new(nome, elementos, Expressoes: expressoes, Predicado: predicado, NulosDistintos: nulosDistintos);

    // Um elemento de texto com a colação padrão, salvo declarada (nula num elemento sem colação).
    private static ElementoEsperado Elemento(string? coluna) => new(coluna, _padrao);

    private static ElementoEsperado Elemento(string? coluna, ColacaoEsperada? colacao) => new(coluna, colacao);

    // Uma materialized view populada, sem dependência de outra nem de função, salvo declaradas.
    private static ResidualInventariado Visao(string nome, bool populada = true, bool incompletas = false) =>
        new(Nome(nome), TipoObjetoResidual.VisaoMaterializada, incompletas, populada);

    private static TabelaInventariada Particao(string nome, string pai, ColunaInventariada[] colunas, string[]? chavePrimaria = null, bool publicada = false) =>
        new() { Nome = Nome(nome), Tipo = TipoTabela.Particao, Pai = Nome(pai), Colunas = colunas, ChavePrimaria = chavePrimaria ?? [], Publicada = publicada };

    // Uma coluna fora de texto: a faixa pelo tipo nativo, se for inteiro; nenhuma restrição do tipo, salvo declarada.
    private static ColunaInventariada Col(
        string nome, string tipo, Classe classe, bool nula = true, IdentidadeColuna identidade = IdentidadeColuna.Nenhuma,
        GeracaoColuna geracao = GeracaoColuna.Nenhuma, string? tipoBase = null, FaixaEsperada? faixa = null, int? precisao = null, int? escala = null,
        LimiteEsperado? comprimento = null, ReferenciasEsperadas? referencias = null, bool restrita = false) => new()
        {
            Nome = nome,
            TipoNativo = tipo,
            Situacao = classe.Situacao,
            Tipo = classe.Tipo,
            Anulavel = nula,
            Identidade = identidade,
            Geracao = geracao,
            TipoBase = tipoBase,
            Faixa = faixa ?? _faixas.GetValueOrDefault(tipo),
            Precisao = precisao,
            Escala = escala,
            Comprimento = comprimento,
            ReferenciasDaGeracao = referencias,
            RestritaPeloTipo = restrita,
        };

    // Uma coluna de texto pelo fim da cadeia: a colação padrão, as marcas do tipo falsas e o limite em caracteres, salvo
    // declarados.
    private static ColunaInventariada ColTexto(
        string nome, string tipo, Classe classe, bool nula = true, string? tipoBase = null, int? comprimento = null, LimiteEsperado? limite = null,
        bool caixa = false, bool espacoFinal = false, bool restrita = false, GeracaoColuna geracao = GeracaoColuna.Nenhuma,
        ReferenciasEsperadas? referencias = null, ColacaoEsperada? colacao = null, bool semColacao = false) => new()
        {
            Nome = nome,
            TipoNativo = tipo,
            Situacao = classe.Situacao,
            Tipo = classe.Tipo,
            Anulavel = nula,
            Geracao = geracao,
            TipoBase = tipoBase,
            Colacao = semColacao ? null : colacao ?? _padrao,
            IgnoraCaixaPeloTipo = caixa,
            IgnoraEspacoFinalPeloTipo = espacoFinal,
            Comprimento = limite ?? new(comprimento, UnidadeComprimento.PontosDeCodigo),
            ReferenciasDaGeracao = referencias,
            RestritaPeloTipo = restrita,
        };

    // Uma colação não determinística medida: o que a medida acha, pela documentação do ICU para o locale do DDL.
    private static ColacaoEsperada Medida(string nome, string esquema, bool caixa = false, bool acento = false, bool pontuacao = false) =>
        new(nome, esquema, false, false, caixa, acento, pontuacao);

    private static NomeObjeto Nome(string nome) => new("loja", nome);

    private static ReferenciaColuna Coluna(string tabela, string coluna) => new(Nome(tabela), coluna);

    // A classe de uma coluna na matriz de tipos: a situação e o tipo canônico.
    private readonly record struct Classe(SituacaoTipo Situacao, TipoColuna? Tipo)
    {
        internal static Classe SemTipo => new(SituacaoTipo.Suportado, null);

        internal static Classe Ressalva => new(SituacaoTipo.ComRessalva, null);

        internal static Classe Exclusao => new(SituacaoTipo.SuportadoPorExclusao, null);

        internal static Classe NaoSuportado => new(SituacaoTipo.NaoSuportado, null);

        internal static Classe Suportado(TipoColuna tipo) => new(SituacaoTipo.Suportado, tipo);
    }
}
