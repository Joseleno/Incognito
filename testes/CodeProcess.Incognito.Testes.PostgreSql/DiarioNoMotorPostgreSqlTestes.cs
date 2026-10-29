using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O diário pelo motor contra o servidor, no banco do diário da suíte (de outro dono, com o login dono só das tabelas): a
/// queda do processo entre as operações do preparo (o servidor encerra as conexões do login, e o bloqueio se solta) é
/// reparada numa conexão nova com o esquema exato, e a execução seguinte passa inteira (C1); a chave da particionada com
/// órfão sai com 4 e continua no diário, também depois do reparo e da execução seguinte (C2).
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class DiarioNoMotorPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private static readonly Guid _marcador = Guid.Parse("0b6f2c1e-5d4a-4e3b-9c8d-7a6b5c4d3e2f");

    private static readonly NomeObjeto[] _triggers = [Loja("disparos"), Loja("pedidos"), Loja("pessoa")];

    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task C1_a_queda_entre_as_operacoes_do_preparo_e_reparada_numa_conexao_nova_com_o_esquema_exato()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarBancoDoDiarioAsync(cancelamento);
        try
        {
            var original = await RetratoAsync(banco);
            var chaves = Chaves();

            // O processo registra, remove três chaves e desliga as triggers de uma árvore, e então some.
            var destino = await ConectarAsync(banco);
            var concessao = (await destino.TentarAdquirirBloqueioAsync(cancelamento))!;
            await destino.CriarDiarioAsync(concessao, cancelamento);
            var execucao = Guid.NewGuid();
            await destino.IniciarExecucaoAsync(concessao, execucao, _marcador, DateTimeOffset.UnixEpoch, cancelamento);
            foreach (var chave in chaves.Take(3))
            {
                await destino.RemoverChaveAsync(concessao, execucao, new NomeObjeto(chave.Tabela.Esquema, chave.Tabela.Tabela), chave.Chave, cancelamento);
            }

            await destino.DesligarTriggersAsync(concessao, execucao, Loja("pedidos"), cancelamento);
            Assert.NotEqual(original, await RetratoAsync(banco));
            await servidor.ExecutarAsync(
                banco,
                $"SELECT pg_catalog.pg_terminate_backend(a.pid, 5000) FROM pg_catalog.pg_stat_activity a WHERE a.datname = pg_catalog.current_database() AND a.usename = '{ServidorPostgreSql.Dono}'",
                cancelamento);
            await Descartar(concessao, destino);

            // Uma instância nova repara, e depois uma execução inteira passa (o executar idempotente).
            await using var novo = await ConectarAsync(banco);
            await using var nova = Assert.IsType<DialetoProtegido.ConcessaoProtegida>(await novo.TentarAdquirirBloqueioAsync(cancelamento));
            var reparo = await ReparadorDoDestino.RepararAsync(novo, nova, _marcador, new OpcoesDoReparo(Validar: true), TimeProvider.System, cancelamento);

            Assert.Equal((0, 1), (ClassificadorDeSaida.De(reparo), reparo.Interrompidas));
            Assert.Equal(original, await RetratoAsync(banco));
            Assert.Empty((await novo.LerDiarioAsync(nova, cancelamento)).Pendentes);

            var seguinte = await ExecutarAsync(novo, nova, chaves);

            Assert.Equal(0, ClassificadorDeSaida.De(seguinte));
            Assert.Equal(original, await RetratoAsync(banco));
            Assert.Empty((await novo.LerDiarioAsync(nova, cancelamento)).Pendentes);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task C2_a_chave_da_particionada_com_orfao_sai_com_4_e_continua_no_diario()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.CriarBancoDoDiarioAsync(cancelamento);
        try
        {
            // A loja 2 some com a chave removida: a linha (2, 1) dos pedidos fica órfã.
            var pedidos = new ChaveRemovida(new NomeTabela("loja", "pedidos"), "fk_pedidos_lojas");
            await using var destino = await ConectarAsync(banco);
            await using var concessao = Assert.IsType<DialetoProtegido.ConcessaoProtegida>(await destino.TentarAdquirirBloqueioAsync(cancelamento));
            await destino.CriarDiarioAsync(concessao, cancelamento);
            var execucao = await DiarioDoDestino.PrepararAsync(destino, concessao, _marcador, [pedidos], [], TimeProvider.System, cancelamento);
            await servidor.ExecutarAsync(banco, "SET session_replication_role = replica; DELETE FROM loja.lojas WHERE id = 2; RESET session_replication_role", cancelamento);

            var restaurada = await DiarioDoDestino.RestaurarAsync(destino, concessao, execucao, TimeProvider.System, cancelamento);
            var reparada = await ReparadorDoDestino.RepararAsync(destino, concessao, _marcador, new OpcoesDoReparo(Validar: true), TimeProvider.System, cancelamento);
            var seguinte = await ExecutarAsync(destino, concessao, [pedidos]);

            // Do 15 ao 17, a validação direta acha o órfão e a chave fica fora; no 18, volta sem validação.
            var esperada = servidor.Versao >= 180000 ? SituacaoEntrada.SemValidacao : SituacaoEntrada.Removida;
            foreach (var resultado in new[] { restaurada, reparada.Restauracao, seguinte })
            {
                var pendente = Assert.Single(resultado.Pendentes);
                Assert.Equal((Loja("pedidos"), "fk_pedidos_lojas", esperada), (pendente.Tabela, pendente.Nome, pendente.Situacao));
                Assert.Equal(4, ClassificadorDeSaida.De(resultado));
            }

            var entrada = Assert.Single((await destino.LerDiarioAsync(concessao, cancelamento)).Pendentes);
            Assert.NotEqual(execucao, entrada.Execucao);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    private static NomeObjeto Loja(string nome) => new("loja", nome);

    // O processo morto não escreve mais; as falhas do descarte das conexões encerradas pelo servidor são esperadas.
    private static async Task Descartar(DialetoProtegido.ConcessaoProtegida concessao, DialetoProtegido destino)
    {
        await concessao.DisposeAsync();
        try
        {
            await destino.DisposeAsync();
        }
        catch (FalhaDialetoException)
        {
        }
    }

    // O executar (INC-038) sem a cópia: reparo sem validar, preparo e restauração.
    private static async Task<ResultadoDaRestauracao> ExecutarAsync(DialetoProtegido destino, DialetoProtegido.ConcessaoProtegida concessao, ChaveRemovida[] chaves)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var reparo = await ReparadorDoDestino.RepararAsync(destino, concessao, _marcador, new OpcoesDoReparo(Validar: false), TimeProvider.System, cancelamento);
        Assert.Empty(reparo.Restauracao.Falhas);
        var execucao = await DiarioDoDestino.PrepararAsync(destino, concessao, _marcador, chaves, chaves.Length > 1 ? _triggers : [], TimeProvider.System, cancelamento);
        return await DiarioDoDestino.RestaurarAsync(destino, concessao, execucao, TimeProvider.System, cancelamento);
    }

    // As chaves do banco do diário; a não imposta só existe no 18.
    private ChaveRemovida[] Chaves() =>
    [
        new(new NomeTabela("loja", "compras"), "fk_compras_lojas"),
        new(new NomeTabela("loja", "pedidos"), "fk_pedidos_lojas"),
        new(new NomeTabela("loja", "vendas"), "fk_vendas_codigo"),
        new(new NomeTabela("loja", "vendas"), "fk_vendas_lojas"),
        new(new NomeTabela("loja", "vendas"), "fk_vendas_par"),
        new(new NomeTabela("loja", "vendas"), "fk_vendas_pendente"),
        .. servidor.Versao >= 180000 ? [new ChaveRemovida(new NomeTabela("loja", "vinculo"), "fk_vinculo_solto")] : Array.Empty<ChaveRemovida>(),
    ];

    // As chaves estrangeiras (definição e estados) e as triggers (estado por tabela física) do esquema loja.
    private async Task<string?> RetratoAsync(string banco)
    {
        var imposta = servidor.Versao >= 180000 ? " || '|' || k.conenforced::text" : "";
        return (string?)await servidor.ConsultarAsync(
            banco,
            $"""
            SELECT pg_catalog.string_agg(r.x, '; ' ORDER BY r.x) FROM (
                SELECT k.conrelid::regclass::text || '.' || k.conname || '=' || pg_catalog.pg_get_constraintdef(k.oid) || '|' || k.convalidated::text{imposta} AS x
                FROM pg_catalog.pg_constraint k
                WHERE k.contype = 'f' AND k.connamespace = 'loja'::regnamespace AND k.conparentid = 0
                UNION ALL
                SELECT t.tgrelid::regclass::text || '.' || t.tgname || '=' || t.tgenabled::text
                FROM pg_catalog.pg_trigger t
                JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                WHERE NOT t.tgisinternal AND c.relnamespace = 'loja'::regnamespace) r
            """,
            TestContext.Current.CancellationToken);
    }

    private Task<DialetoProtegido> ConectarAsync(string banco) =>
        DialetoProtegido.ConectarAsync(
            _provedor,
            "postgresql",
            LadoBanco.Destino,
            new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Dono, servidor.SenhaDono)),
            TestContext.Current.CancellationToken);
}
