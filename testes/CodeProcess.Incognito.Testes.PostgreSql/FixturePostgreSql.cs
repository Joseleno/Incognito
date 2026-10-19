using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A fixture da suíte no PostgreSQL: a origem clonada do banco-modelo, lida pelo leitor (nunca dono), e o destino vazio,
/// do dono sem superusuário. Declara o que o esquema de conformidade tem na versão do servidor.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class FixturePostgreSql(ServidorPostgreSql servidor) : FixtureDialeto
{
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
            if (origem is not null)
            {
                await origem.DisposeAsync();
            }

            await servidor.ApagarAsync(bancoOrigem);
            if (bancoDestino is not null)
            {
                await servidor.ApagarAsync(bancoDestino);
            }

            throw;
        }
    }

    private static EsquemaDeConformidade Declarar(int versao)
    {
        var dezoito = versao >= 180000;
        ColunaGeradaEsperada[] geradas = dezoito
            ? [new(Coluna("calculos", "dobro"), GeracaoColuna.Armazenada), new(Coluna("calculos", "triplo"), GeracaoColuna.Virtual)]
            : [new(Coluna("calculos", "dobro"), GeracaoColuna.Armazenada)];
        return new()
        {
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
                new(Coluna("externa", "linha"), SituacaoTipo.NaoSuportado, null),
                new(Coluna("calculos", "dobro"), SituacaoTipo.SuportadoPorExclusao, null),
            ],
            Particao = Caso.Presente(new Hierarquia(Nome("pedidos"), [Nome("pedidos_1"), Nome("pedidos_2")])),
            Heranca = Caso.Presente(new Hierarquia(Nome("pessoa"), [Nome("funcionario")])),
            SequenceCompartilhada = Caso.Presente(new SequenceEsperada(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")])),
            ColunasGeradas = Caso.Presente<IReadOnlyList<ColunaGeradaEsperada>>(geradas),
            ChaveEstrangeiraComposta = new(Nome("itens"), "fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"]),
            Temporal = Caso.Inexistente<TemporalEsperada>("o PostgreSQL não tem tabela temporal"),
            ObjetosResiduais =
            [
                new(Nome("resumo_vendas"), TipoObjetoResidual.VisaoMaterializada),
                new(new NomeObjeto("pg_catalog", "pg_largeobject"), TipoObjetoResidual.ObjetosGrandes),
            ],
            SegurancaDeLinha = Caso.Presente(Nome("segredos")),
            Mascara = Caso.Inexistente<ReferenciaColuna>("o PostgreSQL não tem máscara dinâmica"),
            NomesExatos = [Nome("Cliente"), Nome("cliente"), Nome("select"), Nome("nota fiscal")],
            Citacoes = [new("Cliente", "\"Cliente\""), new("a\"b", "\"a\"\"b\""), new("nota fiscal", "\"nota fiscal\"")],
            ObjetoDoIncognito = new NomeObjeto("incognito", "execucao"),
        };
    }

    private static ColunaEsperada Suportado(string coluna, TipoColuna? tipo) => new(Coluna("tipos", coluna), SituacaoTipo.Suportado, tipo);

    private static ColunaEsperada ComRessalva(string coluna) => new(Coluna("tipos", coluna), SituacaoTipo.ComRessalva, null);

    private static NomeObjeto Nome(string nome) => new("loja", nome);

    private static ReferenciaColuna Coluna(string tabela, string coluna) => new(Nome(tabela), coluna);
}
