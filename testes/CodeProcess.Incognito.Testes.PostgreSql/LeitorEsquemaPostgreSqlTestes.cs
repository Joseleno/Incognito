using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a leitura do esquema do PostgreSQL faz além do que a suíte confere hoje: esquemas lidos, chave primária,
/// identity, anulável, colunas, tipo nativo, partição em dois níveis, herança múltipla, FKs de particionada e partição
/// com detach pendente.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class LeitorEsquemaPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task So_os_esquemas_do_usuario_sao_lidos_mesmo_sem_privilegio_e_os_que_comecam_com_pg()
    {
        // Fora: pg_catalog, information_schema, pg_toast e o incognito. Dentro: pgdados, em que o leitor não tem privilégio
        // nenhum (o catálogo o mostra assim mesmo), e que só começa com pg (o prefixo do sistema é pg_).
        var esquema = await LerAsync();

        Assert.Equal(["loja", "pgdados"], esquema.Tabelas.Select(tabela => tabela.Nome.Esquema).Distinct());
        Assert.DoesNotContain(esquema.Sequences, sequence => sequence.Nome.Esquema != "loja");
        Assert.DoesNotContain(esquema.ObjetosResiduais, objeto => objeto.Nome.Esquema is not ("loja" or "pg_catalog"));
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_primaria_sai_na_ordem_da_chave()
    {
        var tabelas = Indexar(await LerAsync());

        Assert.Equal(["id"], tabelas[Loja("tipos")].ChavePrimaria);
        Assert.Equal(["loja", "id"], tabelas[Loja("pedidos")].ChavePrimaria);
        Assert.Equal(["b", "a"], tabelas[Loja("chave_invertida")].ChavePrimaria);
        Assert.Empty(tabelas[Loja("pessoa")].ChavePrimaria);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_identity_e_o_serial_sao_lidos_com_as_sequences_deles()
    {
        var esquema = await LerAsync();
        var contadores = Indexar(esquema)[Loja("contadores")].Colunas.ToDictionary(coluna => coluna.Nome);

        Assert.Equal(IdentidadeColuna.Sempre, contadores["sempre"].Identidade);
        Assert.Equal(IdentidadeColuna.PorPadrao, contadores["por_padrao"].Identidade);
        Assert.Equal(IdentidadeColuna.Nenhuma, contadores["serie"].Identidade);
        foreach (var coluna in new[] { "sempre", "por_padrao", "serie" })
        {
            var referencia = new ReferenciaColuna(Loja("contadores"), coluna);
            var sequence = Assert.Single(esquema.Sequences, lida => lida.Colunas.Contains(referencia));
            Assert.Equal([referencia], sequence.Colunas);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Anulavel_vem_do_not_null_validado_da_coluna()
    {
        var tabelas = Indexar(await LerAsync());

        Assert.False(Coluna(tabelas, "tipos", "texto").Anulavel);
        Assert.False(Coluna(tabelas, "tipos", "id").Anulavel);
        Assert.True(Coluna(tabelas, "tipos", "curto").Anulavel);

        // O NOT NULL do domínio não conta: o banco aceita NULL numa coluna dele em casos documentados.
        Assert.True(Coluna(tabelas, "com_dominio", "valor").Anulavel);
        if (servidor.Versao >= 180000)
        {
            Assert.True(Coluna(tabelas, "pendente", "valor").Anulavel);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_colunas_saem_na_ordem_do_catalogo_sem_as_apagadas_e_com_o_tipo_sem_modificador()
    {
        var tabelas = Indexar(await LerAsync());

        Assert.Equal(["a", "b"], tabelas[Loja("chave_invertida")].Colunas.Select(coluna => coluna.Nome));
        Assert.Equal(["id", "pequeno", "inteiro", "valor"], tabelas[Loja("tipos")].Colunas.Take(4).Select(coluna => coluna.Nome));
        Assert.Equal("character varying", Coluna(tabelas, "tipos", "curto").TipoNativo);
        Assert.Equal("loja.codigo", Coluna(tabelas, "tipos", "codigo").TipoNativo);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particao_intermediaria_e_particao_e_a_heranca_multipla_aponta_a_primeira_mae()
    {
        var esquema = await LerAsync();
        var tabelas = Indexar(esquema);

        Assert.Equal((TipoTabela.Particionada, (NomeObjeto?)null), (tabelas[Loja("vendas")].Tipo, tabelas[Loja("vendas")].Pai));
        Assert.Equal((TipoTabela.Particao, Loja("vendas")), (tabelas[Loja("vendas_2026")].Tipo, tabelas[Loja("vendas_2026")].Pai));
        Assert.Equal((TipoTabela.Particao, Loja("vendas_2026")), (tabelas[Loja("vendas_2026_01")].Tipo, tabelas[Loja("vendas_2026_01")].Pai));
        Assert.Single(esquema.Tabelas, tabela => tabela.Nome.Equals(Loja("ficha")));
        Assert.Equal((TipoTabela.Herdeira, Loja("contato")), (tabelas[Loja("ficha")].Tipo, tabelas[Loja("ficha")].Pai));
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_chaves_estrangeiras_de_tabela_particionada_aparecem_uma_vez_so_pela_particionada()
    {
        // Uma FK que referencia uma particionada ganha uma restrição por partição do lado referenciado, e uma FK declarada
        // na particionada ganha uma em cada partição: o leitor devolve só as originais.
        var tabelas = Indexar(await LerAsync());

        Assert.Equal(["fk_itens_pedidos"], tabelas[Loja("itens")].ChavesEstrangeiras.Select(chave => chave.Nome));
        var dePedidos = Assert.Single(tabelas[Loja("pedidos")].ChavesEstrangeiras);
        Assert.Equal(Loja("lojas"), dePedidos.Referenciada);
        Assert.Empty(tabelas[Loja("pedidos_1")].ChavesEstrangeiras);
        Assert.Empty(tabelas[Loja("pedidos_2")].ChavesEstrangeiras);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Particao_com_detach_concurrently_interrompido_e_recusada_com_os_nomes_citados()
    {
        // A leitura pelo pai já não enxerga as linhas da partição pendente: copiada pelo pai, elas sumiriam. O nome com
        // maiúscula mostra que o comando sugerido cita os nomes.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, """CREATE TABLE loja.p (id integer) PARTITION BY LIST (id); CREATE TABLE loja."P1" PARTITION OF loja.p FOR VALUES IN (1);""", cancelamento);

            // Um snapshot antigo segura o DETACH CONCURRENTLY, que é cancelado no meio e deixa a partição pendente.
            await using (var antiga = await servidor.AbrirComoAdministradorAsync(banco, cancelamento))
            {
                await using (var comando = new NpgsqlCommand("BEGIN ISOLATION LEVEL REPEATABLE READ; SELECT count(*) FROM loja.p;", antiga))
                {
                    await comando.ExecuteNonQueryAsync(cancelamento);
                }

                // Comandos separados: o DETACH CONCURRENTLY não roda dentro de um bloco de transação.
                await using var outra = await servidor.AbrirComoAdministradorAsync(banco, cancelamento);
                await using (var limite = new NpgsqlCommand("SET statement_timeout = '1s'", outra))
                {
                    await limite.ExecuteNonQueryAsync(cancelamento);
                }

                await using var detach = new NpgsqlCommand("""ALTER TABLE loja.p DETACH PARTITION loja."P1" CONCURRENTLY""", outra);
                var erro = await Assert.ThrowsAsync<PostgresException>(() => detach.ExecuteNonQueryAsync(cancelamento));
                Assert.Equal(PostgresErrorCodes.QueryCanceled, erro.SqlState);
            }

            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            var recusa = await Assert.ThrowsAsync<IncognitoException>(() => origem.Esquema.LerAsync(cancelamento));

            Assert.Equal("INC3105", recusa.Codigo);
            Assert.Contains("""ALTER TABLE "loja"."p" DETACH PARTITION "loja"."P1" FINALIZE""", recusa.Message, StringComparison.Ordinal);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    private static NomeObjeto Loja(string nome) => new("loja", nome);

    private static Dictionary<NomeObjeto, TabelaEsquema> Indexar(EsquemaBanco esquema) => esquema.Tabelas.ToDictionary(tabela => tabela.Nome);

    private static ColunaEsquema Coluna(Dictionary<NomeObjeto, TabelaEsquema> tabelas, string tabela, string coluna) =>
        tabelas[Loja(tabela)].Colunas.Single(lida => lida.Nome == coluna);

    // Lê o esquema de um clone do modelo, pelo leitor.
    private async Task<EsquemaBanco> LerAsync()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);
            return await origem.Esquema.LerAsync(cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
