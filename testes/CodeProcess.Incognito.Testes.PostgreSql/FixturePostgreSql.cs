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

    private readonly ProvedorPostgreSql _provedor = new();

    /// <inheritdoc/>
    public override EsquemaDeConformidade Esperado { get; } = Declarar(servidor.Versao);

    /// <inheritdoc/>
    public override async ValueTask<ParDeBancos> CriarParAsync(CancellationToken cancelamento)
    {
        var bancoOrigem = await servidor.ClonarModeloAsync(cancelamento);
        string? bancoDestino = null;
        DialetoOrigem? origem = null;
        try
        {
            bancoDestino = await servidor.CriarDestinoAsync(cancelamento);
            origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(bancoOrigem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            var destino = await _provedor.ConectarDestinoAsync(
                new SegredoConexao(servidor.StringDeConexao(bancoDestino, ServidorPostgreSql.Dono, servidor.SenhaDono)), cancelamento);
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
        ];
        return new()
        {
            Inventario = Inventariar(versao),
            Tipos =
            [
                Suportado("pequeno", TipoColuna.Inteiro),
                Suportado("inteiro", TipoColuna.Inteiro),
                Suportado("id", TipoColuna.Inteiro),
                Suportado("valor", TipoColuna.Decimal),
                Suportado("texto", TipoColuna.Texto),
                Suportado("curto", TipoColuna.Texto),
                Suportado("fixo", TipoColuna.Texto),
                Suportado("ativo", TipoColuna.Booleano),
                Suportado("dia", TipoColuna.Data),
                Suportado("momento", TipoColuna.DataHora),
                Suportado("instante", TipoColuna.DataHoraComFuso),
                Suportado("identificador", TipoColuna.Uuid),
                Suportado("conteudo", TipoColuna.Binario),
                Suportado("email", TipoColuna.Texto),
                Suportado("hora", null),
                Suportado("hora_com_fuso", null),
                Suportado("duracao", null),
                ComRessalva("emails"),
                ComRessalva("dados"),
                ComRessalva("documento"),
                ComRessalva("marcacao"),
                ComRessalva("preco"),
                ComRessalva("situacao"),
                ComRessalva("codigo"),
                ComRessalva("faixa"),
                ComRessalva("lista"),
                ComRessalva("real"),
                ComRessalva("dupla"),
                ComRessalva("endereco"),
                new(Coluna("tipos", "anexo"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("tipos", "referencia"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("objetos", "codigo"), SituacaoTipo.ComRessalva, null),
                new(Coluna("objetos", "referencia"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("objetos", "referencias"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("objetos", "anexos"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("objetos", "lista"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("imitacoes", "parecido"), SituacaoTipo.ComRessalva, null),
                new(Coluna("imitacoes", "adotado"), SituacaoTipo.ComRessalva, null),
                new(Coluna("externa", "linha"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("externa", "tamanho"), SituacaoTipo.SuportadoPorExclusao, null),
                new(Coluna("leituras_arquivo", "valor"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("calculos", "dobro"), SituacaoTipo.SuportadoPorExclusao, null),

                // Domínios fora da tabela de tipos, colunas de partição e de herdeira e o esquema que começa com "pg".
                new(Coluna("protocolos", "numero"), SituacaoTipo.ComRessalva, null),
                new(Coluna("protocolos", "derivado"), SituacaoTipo.ComRessalva, null),
                new(Coluna("com_dominio", "valor"), SituacaoTipo.ComRessalva, null),
                new(Coluna("pedidos_9", "numero"), SituacaoTipo.Suportado, TipoColuna.Inteiro),
                new(Coluna("eventos_1", "id"), SituacaoTipo.Suportado, TipoColuna.Inteiro),
                new(Coluna("sem_a_primeira", "um"), SituacaoTipo.Suportado, TipoColuna.Texto),
                new(new ReferenciaColuna(new NomeObjeto("pgdados", "clientes"), "id"), SituacaoTipo.Suportado, TipoColuna.Inteiro),
            ],
            Particao = Caso.Presente(new Hierarquia(Nome("pedidos"), [Nome("pedidos_1"), Nome("pedidos_2"), Nome("pedidos_9")])),
            Heranca = Caso.Presente(new Hierarquia(Nome("pessoa"), [Nome("funcionario")])),
            SequenceCompartilhada = Caso.Presente(new SequenceEsperada(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")])),
            ColunasGeradas = Caso.Presente<IReadOnlyList<ColunaGeradaEsperada>>(geradas),
            ChaveEstrangeiraComposta = new(Nome("itens"), "fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"]),
            Temporal = Caso.Inexistente<TemporalEsperada>("o PostgreSQL não tem tabela temporal"),
            ObjetosResiduais =
            [
                new(Nome("resumo_vendas"), TipoObjetoResidual.VisaoMaterializada),
                new(Nome("resumo_alheio"), TipoObjetoResidual.VisaoMaterializada),
                new(new NomeObjeto("pg_catalog", "pg_largeobject"), TipoObjetoResidual.ObjetosGrandes),
            ],
            SegurancaDeLinha = Caso.Presente(Nome("segredos")),
            Mascara = Caso.Inexistente<ReferenciaColuna>("o PostgreSQL não tem máscara dinâmica"),
            NomesExatos = [Nome("Cliente"), Nome("cliente"), Nome("select"), Nome("nota fiscal")],
            Citacoes = [new("Cliente", "\"Cliente\""), new("a\"b", "\"a\"\"b\""), new("nota fiscal", "\"nota fiscal\"")],
            ObjetoDoIncognito = new NomeObjeto("incognito", "execucao"),
        };
    }

    // Escrito à mão a partir do esquema-de-conformidade.sql, nunca copiado da saída do leitor. O tipo nativo é o do
    // format_type com o caminho de busca só no pg_catalog (os tipos de extensão e de domínio saem com o esquema).
    private static Inventario Inventariar(int versao)
    {
        var dezessete = versao >= 170000;
        var dezoito = versao >= 180000;
        ColunaInventariada[] pedido = [Col("loja", Inteiro, nula: false), Col("id", Grande, nula: false), Col("numero", Grande)];
        ColunaInventariada[] venda = [Col("ano", Inteiro, nula: false), Col("mes", Inteiro, nula: false)];
        ColunaInventariada[] agenda = [Col("dia", "date", nula: false), Col("sala", Texto), Col("nota", Texto), Col("turno", Texto)];
        ColunaInventariada[] leitura = [Col("origem", Texto, nula: false), Col("valor", Texto)];
        TabelaInventariada[] tabelas =
        [
            new()
            {
                Nome = Nome("tipos"),
                Colunas =
                [
                    Col("id", Grande, nula: false),
                    Col("pequeno", "smallint"),
                    Col("inteiro", Inteiro),
                    Col("valor", "numeric"),
                    Col("texto", Texto, nula: false),
                    Col("curto", "character varying"),
                    Col("fixo", "character"),
                    Col("ativo", "boolean"),
                    Col("dia", "date"),
                    Col("momento", "timestamp without time zone"),
                    Col("instante", "timestamp with time zone"),
                    Col("identificador", "uuid"),
                    Col("conteudo", "bytea"),
                    Col("hora", "time without time zone"),
                    Col("hora_com_fuso", "time with time zone"),
                    Col("duracao", "interval"),
                    Col("email", "public.citext"),
                    Col("emails", "public.citext[]"),
                    Col("dados", "jsonb"),
                    Col("documento", "json"),
                    Col("marcacao", "xml"),
                    Col("preco", "money"),
                    Col("situacao", "loja.situacao"),
                    Col("codigo", "loja.codigo", tipoBase: Texto),
                    Col("faixa", "int4range"),
                    Col("lista", "integer[]"),
                    Col("real", "real"),
                    Col("dupla", "double precision"),
                    Col("endereco", "inet"),
                    Col("anexo", "public.lo", tipoBase: "oid"),
                    Col("referencia", "oid"),
                    Col("observacao", Texto),
                ],
                ChavePrimaria = ["id"],
            },
            new()
            {
                Nome = Nome("objetos"),
                Colunas =
                [
                    Col("id", Grande, nula: false),
                    Col("codigo", "loja.codigo_curto", tipoBase: Texto),
                    Col("referencia", "loja.ref_objeto", tipoBase: "oid"),
                    Col("referencias", "oid[]"),
                    Col("anexos", "public.lo[]"),
                    Col("lista", "loja.lista_lo", tipoBase: "public.lo[]"),
                ],
                ChavePrimaria = ["id"],
            },
            new() { Nome = Nome("imitacoes"), Colunas = [Col("parecido", "loja.citext", tipoBase: Texto), Col("adotado", "loja.texto_adotado", tipoBase: Texto)] },
            new() { Nome = Nome("da_extensao"), Extensao = "citext", Colunas = [Col("id", Inteiro)] },
            new() { Nome = Nome("externa"), Estrangeira = true, Colunas = [Col("linha", Texto), Col("tamanho", Inteiro, geracao: GeracaoColuna.Armazenada)] },
            new()
            {
                Nome = Nome("calculos"),
                Colunas =
                [
                    Col("id", Grande, nula: false),
                    Col("valor", Inteiro, nula: false),
                    Col("dobro", Inteiro, geracao: GeracaoColuna.Armazenada),
                    .. dezoito ? [Col("triplo", Inteiro, geracao: GeracaoColuna.Virtual)] : Array.Empty<ColunaInventariada>(),
                ],
                ChavePrimaria = ["id"],
            },
            new()
            {
                Nome = Nome("contadores"),
                Colunas =
                [
                    Col("sempre", Grande, nula: false, identidade: IdentidadeColuna.Sempre),
                    Col("por_padrao", Grande, nula: false, identidade: IdentidadeColuna.PorPadrao),
                    Col("serie", Inteiro, nula: false),
                ],
                ChavePrimaria = ["sempre"],
            },
            new()
            {
                Nome = Nome("eventos"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["tipo"]),
                Colunas = [Col("id", Grande, nula: false, identidade: IdentidadeColuna.Sempre), Col("tipo", Inteiro, nula: false)],
            },
            new()
            {
                Nome = Nome("eventos_1"),
                Tipo = TipoTabela.Particao,
                Pai = Nome("eventos"),
                Colunas =
                [
                    Col("id", Grande, nula: false, identidade: dezessete ? IdentidadeColuna.Sempre : IdentidadeColuna.Nenhuma),
                    Col("tipo", Inteiro, nula: false),
                ],
            },
            new() { Nome = Nome("lojas"), Colunas = [Col("id", Inteiro, nula: false)], ChavePrimaria = ["id"] },
            new()
            {
                Nome = Nome("pedidos"),
                Tipo = TipoTabela.Particionada,
                ChaveParticao = new(["loja"]),
                Colunas = pedido,
                ChavePrimaria = ["loja", "id"],
                ChavesEstrangeiras = [new("pedidos_loja_fkey", ["loja"], Nome("lojas"), ["id"])],
            },
            Particao("pedidos_1", "pedidos", pedido, ["loja", "id"]),
            Particao("pedidos_2", "pedidos", pedido, ["loja", "id"]),
            Particao("pedidos_9", "pedidos", [Col("loja", Inteiro, nula: false), Col("id", Grande, nula: false), Col("numero", Grande, nula: false)], ["loja", "id"]),
            new() { Nome = Nome("regioes"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["nome"]), Colunas = [Col("nome", Texto, nula: false)] },
            Particao("regioes_sentinela", "regioes", [Col("nome", Texto, nula: false)]),
            new()
            {
                Nome = Nome("itens"),
                Colunas = [Col("pedido", Grande, nula: false), Col("loja", Inteiro, nula: false), Col("numero", Grande), Col("produto", Texto)],
                ChavesEstrangeiras = [new("fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"])],
            },
            new() { Nome = Nome("pessoa"), Colunas = [Col("id", Grande), Col("apelido", Texto)] },
            new() { Nome = Nome("funcionario"), Tipo = TipoTabela.Herdeira, Pai = Nome("pessoa"), Colunas = [Col("id", Grande), Col("apelido", Texto), Col("matricula", Inteiro)] },
            new() { Nome = Nome("Cliente"), Colunas = [Col("id", Grande, nula: false)], ChavePrimaria = ["id"] },
            new() { Nome = Nome("cliente"), Colunas = [Col("id", Grande, nula: false)], ChavePrimaria = ["id"] },
            new() { Nome = Nome("select"), Colunas = [Col("from", Texto), Col("coluna com espaço", Texto), Col("a\"b", Texto), Col("valor €", Texto)] },
            new() { Nome = Nome("nota fiscal"), Colunas = [Col("id", Grande)] },
            new() { Nome = Nome("chave_invertida"), Colunas = [Col("a", Inteiro, nula: false), Col("b", Inteiro, nula: false)], ChavePrimaria = ["b", "a"] },
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
                Colunas = [Col("id", Grande), Col("dados", Texto)],
            },
            new() { Nome = Nome("sem_colunas"), Tipo = TipoTabela.Particionada, ChaveParticao = new([], ColunasIncompletas: true), Colunas = [] },
            new() { Nome = Nome("leituras"), Tipo = TipoTabela.Particionada, ChaveParticao = new(["origem"]), Colunas = leitura },
            new() { Nome = Nome("leituras_arquivo"), Tipo = TipoTabela.Particao, Pai = Nome("leituras"), Estrangeira = true, Colunas = leitura },
            new() { Nome = Nome("contato"), Colunas = [Col("canal", Texto)] },
            new() { Nome = Nome("endereco"), Colunas = [Col("cidade", Texto)] },
            new() { Nome = Nome("documento"), Colunas = [Col("numero", Texto)] },
            new()
            {
                Nome = Nome("ficha"),
                Tipo = TipoTabela.Herdeira,
                Pai = Nome("contato"),
                OutrosPais = [Nome("endereco"), Nome("documento")],
                Colunas = [Col("canal", Texto), Col("cidade", Texto), Col("numero", Texto)],
            },
            new() { Nome = Nome("mae_um"), Colunas = [Col("um", Texto)] },
            new() { Nome = Nome("mae_dois"), Colunas = [Col("dois", Texto)] },
            new() { Nome = Nome("mae_tres"), Colunas = [Col("tres", Texto)] },
            new()
            {
                Nome = Nome("sem_a_primeira"),
                Tipo = TipoTabela.Herdeira,
                Pai = Nome("mae_dois"),
                OutrosPais = [Nome("mae_tres")],
                Colunas = [Col("um", Texto), Col("dois", Texto), Col("tres", Texto)],
            },
            new() { Nome = Nome("com_dominio"), Colunas = [Col("valor", "loja.obrigatorio", tipoBase: Texto)] },
            new()
            {
                Nome = Nome("protocolos"),
                Colunas =
                [
                    Col("numero", "loja.protocolo", tipoBase: Grande),
                    Col("proprio", "loja.protocolo", tipoBase: Grande),
                    Col("base", "loja.numero_base", tipoBase: Grande),
                    Col("derivado", "loja.numero_derivado", tipoBase: Grande),
                ],
            },
            .. dezoito ? [new TabelaInventariada { Nome = Nome("pendente"), Colunas = [Col("valor", Texto)] }] : Array.Empty<TabelaInventariada>(),
            new() { Nome = Nome("segredos"), SegurancaDeLinha = true, Colunas = [Col("id", Grande, nula: false), Col("conteudo", Texto)], ChavePrimaria = ["id"] },
            new() { Nome = Nome("reservas"), Colunas = [Col("sala", Inteiro), Col("periodo", "int4range"), Col("nota", Texto)] },
            new() { Nome = Nome("alheia"), Colunas = [Col("id", Grande, nula: false)], ChavePrimaria = ["id"] },
            new() { Nome = new NomeObjeto("pgdados", "clientes"), Colunas = [Col("id", Grande, nula: false)], ChavePrimaria = ["id"] },
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
        ];
        return new(tabelas, sequences);
    }

    private static TabelaInventariada Particao(string nome, string pai, ColunaInventariada[] colunas, string[]? chavePrimaria = null) =>
        new() { Nome = Nome(nome), Tipo = TipoTabela.Particao, Pai = Nome(pai), Colunas = colunas, ChavePrimaria = chavePrimaria ?? [] };

    private static ColunaInventariada Col(
        string nome, string tipo, bool nula = true, IdentidadeColuna identidade = IdentidadeColuna.Nenhuma,
        GeracaoColuna geracao = GeracaoColuna.Nenhuma, string? tipoBase = null) =>
        new() { Nome = nome, TipoNativo = tipo, Anulavel = nula, Identidade = identidade, Geracao = geracao, TipoBase = tipoBase };

    private static ColunaEsperada Suportado(string coluna, TipoColuna? tipo) => new(Coluna("tipos", coluna), SituacaoTipo.Suportado, tipo);

    private static ColunaEsperada ComRessalva(string coluna) => new(Coluna("tipos", coluna), SituacaoTipo.ComRessalva, null);

    private static NomeObjeto Nome(string nome) => new("loja", nome);

    private static ReferenciaColuna Coluna(string tabela, string coluna) => new(Nome(tabela), coluna);
}
