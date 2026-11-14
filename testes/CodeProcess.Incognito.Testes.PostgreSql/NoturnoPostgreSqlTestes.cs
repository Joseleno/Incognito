using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// Os 10 milhões de linhas, só no job noturno (categoria própria, filtrada no CI de todo pull request e no release): linhas
/// estreitas por <c>generate_series</c>, uma em cada cem com um texto de cerca de 1 KB, copiadas com P = 1 e K = 1 sem que o
/// pico de lotes passe do que os canais comportam nem a memória gerenciada passe do teto do C1. Só no PostgreSQL 18, para não
/// dobrar o tempo do noturno; na coleção sem paralelismo da medida de memória.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
[Collection(nameof(MemoriaDaExecucaoPostgreSqlTestes))]
public sealed class NoturnoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    /// <summary>A categoria do job noturno (o <c>ci.yml</c> e o <c>release.yml</c> a filtram; a guarda dos projetos de teste confere).</summary>
    internal const string Noturno = "Noturno";

    private const int Linhas = 10_000_000;

    private const long TetoDaMemoria = 128L * 1024 * 1024;

    private const string Esquema = "CREATE SCHEMA larga; CREATE TABLE larga.linhas (id bigint PRIMARY KEY, nome text NOT NULL, texto text);";

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [Trait(CategoriaDaSuite.Nome, Noturno)]
    public async Task Dez_milhoes_de_linhas_com_o_pico_de_lotes_limitado_e_a_memoria_estavel()
    {
        Assert.SkipUnless(servidor.Versao >= 180000, "Os 10 milhões rodam só no PostgreSQL 18.");
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.CriarVazioAsync("origem", null, cancelamento);
        var destino = await servidor.CriarVazioAsync("destino", ServidorPostgreSql.Dono, cancelamento);
        try
        {
            await using (var conexao = await servidor.AbrirComoAdministradorAsync(origem, cancelamento))
            {
                await using var comando = new NpgsqlCommand(
                    Esquema + $"INSERT INTO larga.linhas SELECT g, 'Pessoa ' || g, CASE WHEN g % 100 = 0 THEN repeat(md5(g::text), 32) END FROM generate_series(1, {Linhas}) g; "
                    + $"GRANT USAGE ON SCHEMA larga TO {ServidorPostgreSql.Leitor}; GRANT SELECT ON ALL TABLES IN SCHEMA larga TO {ServidorPostgreSql.Leitor};",
                    conexao)
                {
                    CommandTimeout = 0,
                };
                await comando.ExecuteNonQueryAsync(cancelamento);
            }

            await servidor.ExecutarAsync(destino, Esquema, cancelamento);
            await ExecucaoPostgreSqlTestes.PrepararDestinoAsync(servidor, origem, destino, "larga", cancelamento);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var inicial = GC.GetTotalMemory(forceFullCollection: true);
            var pico = 0L;
            using var fim = new CancellationTokenSource();
            var amostras = Task.Run(
                async () =>
                {
                    while (!fim.IsCancellationRequested)
                    {
                        pico = Math.Max(pico, GC.GetTotalMemory(forceFullCollection: false) - inicial);
                        await Task.Delay(50, CancellationToken.None);
                    }
                },
                CancellationToken.None);

            var resultado = await ExecucaoPostgreSqlTestes.ExecutarAsync(
                servidor, origem, destino, Configuracao(destino), new RegistroTransformadores(), _ => ChaveIncognito.Gerar(), paralelismo: 1, tratamentos: 1, cancelamento);
            await fim.CancelAsync();
            await amostras;

            Assert.Equal(0, resultado.Saida);
            var tabela = Assert.Single(resultado.Tabelas);
            Assert.Equal((Linhas, (long)Linhas), (tabela.Lidas, tabela.Gravadas));
            Assert.InRange(resultado.PicoDeLotes, 1, 1 + CopiaDaTabela.Capacidade + 1 + CopiaDaTabela.Capacidade + 1);
            Assert.True(pico < TetoDaMemoria, $"pico de {pico / (1024 * 1024)} MB acima do teto de {TetoDaMemoria / (1024 * 1024)} MB");
            Assert.Equal((long)Linhas, await servidor.ConsultarAsync(destino, "SELECT count(*) FROM larga.linhas", cancelamento));
            Assert.Equal((long)(Linhas / 100), await servidor.ConsultarAsync(destino, "SELECT count(*) FROM larga.linhas WHERE texto IS NOT NULL", cancelamento));
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    private static string Configuracao(string destino) => $"""
        versao: 1
        origem:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_ORIGEM
        destino:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_DESTINO
          bancoEsperado: {destino}
        modo: efemero
        ambiente: homologacao
        tabelas:
          larga.linhas:
            colunas:
              nome: {"{"} transformador: texto.substituir@1 {"}"}

        """;
}
