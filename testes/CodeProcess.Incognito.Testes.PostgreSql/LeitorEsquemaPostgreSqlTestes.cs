using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a leitura do esquema do PostgreSQL faz além do que a suíte confere, com bancos montados no meio do teste: a recusa
/// da partição com detach pendente, a codificação de um banco sem os caracteres da medida de colação ou sem codificação
/// declarada, e a colação que o leitor não pode medir. O resto (esquemas lidos, colunas, chaves, sequences, partição,
/// herança e a semântica das colunas) a suíte confere pelo inventário.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class LeitorEsquemaPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

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

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Banco_sem_codificacao_declarada_mede_o_texto_em_bytes()
    {
        // No SQL_ASCII o banco guarda bytes, e o Incognito grava UTF-8 cru: até 4 bytes por caractere.
        var colunas = await LerBancoAvulsoAsync(
            "ENCODING 'SQL_ASCII' LC_COLLATE 'C' LC_CTYPE 'C'",
            "CREATE TABLE public.t (curto varchar(3), livre text, nome name)",
            TestContext.Current.CancellationToken);

        var codificacao = colunas.Propriedades!.Codificacao;
        Assert.Equal(("SQL_ASCII", false, false, 4), (codificacao.Nome, codificacao.Definida, codificacao.Unicode, codificacao.BytesPorCaractereMaximo));
        var tabela = Assert.Single(colunas.Tabelas);
        Assert.Equal(
            [(3, UnidadeComprimento.Bytes), (null, UnidadeComprimento.Bytes), (63, UnidadeComprimento.Bytes)],
            tabela.Colunas.Select(coluna => (coluna.Comprimento!.Valor, coluna.Comprimento.Unidade)));
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Banco_com_regras_na_colacao_padrao_tem_a_padrao_marcada_com_regras()
    {
        // Regras ICU na colação do banco só existem do 16 em diante; a padrão continua determinística, e as marcas, falsas.
        Assert.SkipWhen(servidor.Versao < 160000, "Regras de colação só existem do PostgreSQL 16 em diante.");
        var esquema = await LerBancoAvulsoAsync(
            "ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C' LOCALE_PROVIDER icu ICU_LOCALE 'und' ICU_RULES '&a < b'",
            "CREATE TABLE public.t (v text)",
            TestContext.Current.CancellationToken);

        var padrao = esquema.Propriedades!.Colacao;
        Assert.Equal(("default", true, true), (padrao.Nome, padrao.Deterministica, padrao.TemRegras));
        Assert.Equal<bool?[]>([false, false, false], [padrao.IgnoraCaixa, padrao.IgnoraAcento, padrao.IgnoraPontuacao]);
        Assert.Same(padrao, Assert.Single(Assert.Single(esquema.Tabelas).Colunas).Colacao);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Colacao_num_banco_sem_os_caracteres_da_medida_fica_sem_marcas_e_a_leitura_nao_falha()
    {
        // O acento da medida não existe no ISO_8859_5: medida, a conversão abortaria a transação da leitura.
        var esquema = await LerBancoAvulsoAsync(
            "ENCODING 'ISO_8859_5' LC_COLLATE 'C' LC_CTYPE 'C'",
            "CREATE COLLATION public.ci (provider = icu, locale = 'und-u-ks-level2', deterministic = false); CREATE TABLE public.t (v text COLLATE public.ci)",
            TestContext.Current.CancellationToken);

        var codificacao = esquema.Propriedades!.Codificacao;
        Assert.Equal(("ISO_8859_5", true, false, 1), (codificacao.Nome, codificacao.Definida, codificacao.Unicode, codificacao.BytesPorCaractereMaximo));
        var colacao = Assert.Single(Assert.Single(esquema.Tabelas).Colunas).Colacao!;
        Assert.Equal(("ci", "public", false, false), (colacao.Nome, colacao.Esquema, colacao.Deterministica, colacao.TemRegras));
        Assert.Equal<bool?[]>([null, null, null], [colacao.IgnoraCaixa, colacao.IgnoraAcento, colacao.IgnoraPontuacao]);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Colacao_num_esquema_sem_USAGE_para_o_leitor_fica_sem_marcas_na_origem()
    {
        // Sem USAGE no esquema da colação, o COLLATE da medida falharia e abortaria a transação; o administrador, que tem,
        // mede.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                banco,
                "CREATE SCHEMA fechado; CREATE COLLATION fechado.ci (provider = icu, locale = 'und-u-ks-level2', deterministic = false); "
                + "CREATE TABLE loja.fechada (v text COLLATE fechado.ci); GRANT SELECT ON loja.fechada TO incognito_leitor;",
                cancelamento);

            var doLeitor = await ColacaoDeAsync(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor), cancelamento);
            var doAdministrador = await ColacaoDeAsync(servidor.StringDeConexaoDoAdministrador(banco), cancelamento);

            Assert.Equal<bool?[]>([null, null, null], [doLeitor.IgnoraCaixa, doLeitor.IgnoraAcento, doLeitor.IgnoraPontuacao]);
            Assert.Equal<bool?[]>([true, false, false], [doAdministrador.IgnoraCaixa, doAdministrador.IgnoraAcento, doAdministrador.IgnoraPontuacao]);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }

        async Task<ColacaoEsquema> ColacaoDeAsync(string conexao, CancellationToken cancelamento)
        {
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(conexao), cancelamento);
            var tabela = (await origem.Esquema.LerAsync(cancelamento)).Tabelas.Single(lida => lida.Nome.Equals(new NomeObjeto("loja", "fechada")));
            return Assert.Single(tabela.Colunas).Colacao!;
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_recusa_da_medida_da_colacao_nao_traz_nada_do_erro_do_banco()
    {
        // A mensagem, o detalhe, o contexto e a consulta interna do erro podem trazer valores; o nome da colação vai citado e
        // com os caracteres de controle escapados.
        var erro = new PostgresException(
            messageText: ServidorPostgreSql.Sentinela + "-mensagem",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: PostgresErrorCodes.InvalidTextRepresentation,
            detail: ServidorPostgreSql.Sentinela + "-detalhe",
            internalQuery: ServidorPostgreSql.Sentinela + "-consulta",
            where: ServidorPostgreSql.Sentinela + "-contexto");
        var nome = "Caixa\"Alta" + (char)10 + "INC0000";

        var recusa = await Assert.ThrowsAsync<IncognitoException>(() => LeitorEsquemaPostgreSql.SemErroDoBancoAsync<bool>("Idioma\"X", nome, () => throw erro));

        Assert.Equal("INC3106", recusa.Codigo);
        Assert.Null(recusa.InnerException);
        Assert.Contains("\"Idioma\"\"X\".\"Caixa\"\"Alta", recusa.Message, StringComparison.Ordinal);
        Assert.DoesNotContain((char)10, recusa.Message);
        Assert.DoesNotContain(ServidorPostgreSql.Sentinela, recusa.ToString(), StringComparison.Ordinal);
    }

    // Um banco criado do template0, com o esquema dado, lido pelo administrador (o leitor não tem nada nele) e apagado.
    private async Task<EsquemaBanco> LerBancoAvulsoAsync(string opcoes, string esquema, CancellationToken cancelamento)
    {
        var banco = $"inc_avulso_{Guid.NewGuid():N}";
        await servidor.ExecutarAsync("postgres", $"CREATE DATABASE {banco} TEMPLATE template0 {opcoes}", cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, esquema, cancelamento);
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(servidor.StringDeConexaoDoAdministrador(banco)), cancelamento);
            return await origem.Esquema.LerAsync(cancelamento);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
