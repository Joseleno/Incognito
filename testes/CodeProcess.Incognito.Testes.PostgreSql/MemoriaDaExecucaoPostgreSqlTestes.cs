using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// C1, memória estável: 200 mil linhas largas (cerca de 200 MB) passam com P = 1 e K = 1 sem que o pico de lotes vivos
/// passe do que os canais comportam, e a memória gerenciada fica muito abaixo do tamanho da tabela. Numa coleção sem
/// paralelismo, para que outro teste não pese na medida.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
[Collection(nameof(MemoriaDaExecucaoPostgreSqlTestes))]
public sealed class MemoriaDaExecucaoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const int Linhas = 200_000;

    // Bem abaixo dos cerca de 200 MB da tabela, que a cópia guardando tudo passaria.
    private const long TetoDaMemoria = 128L * 1024 * 1024;

    private const string Esquema = "CREATE SCHEMA larga; CREATE TABLE larga.linhas (id bigint PRIMARY KEY, nome text NOT NULL, texto text NOT NULL);";

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task C1_duzentas_mil_linhas_largas_com_o_pico_de_lotes_limitado_e_a_memoria_estavel()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.CriarVazioAsync("origem", null, cancelamento);
        var destino = await servidor.CriarVazioAsync("destino", ServidorPostgreSql.Dono, cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                origem,
                Esquema + $"INSERT INTO larga.linhas SELECT g, 'Pessoa ' || g, repeat(md5(g::text), 32) FROM generate_series(1, {Linhas}) g; "
                + $"GRANT USAGE ON SCHEMA larga TO {ServidorPostgreSql.Leitor}; GRANT SELECT ON ALL TABLES IN SCHEMA larga TO {ServidorPostgreSql.Leitor};",
                cancelamento);
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
                        await Task.Delay(10, CancellationToken.None);
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

            // Lido e esperando o canal 1, dois no canal 1, um tratando, dois no canal 2 e um gravando.
            Assert.InRange(resultado.PicoDeLotes, 1, 1 + CopiaDaTabela.Capacidade + 1 + CopiaDaTabela.Capacidade + 1);
            Assert.True(pico < TetoDaMemoria, $"pico de {pico / (1024 * 1024)} MB, lotes {resultado.PicoDeLotes}, acima do teto de {TetoDaMemoria / (1024 * 1024)} MB");
            Assert.Equal((long)Linhas, await servidor.ConsultarAsync(destino, "SELECT count(*) FROM larga.linhas", cancelamento));
            TestContext.Current.SendDiagnosticMessage($"C1: pico de lotes {resultado.PicoDeLotes}, memória gerenciada {pico / (1024 * 1024)} MB acima da base, {tabela.Duracao.TotalSeconds:F1} s");
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

/// <summary>A coleção da medida de memória, que não roda em paralelo com nenhuma outra.</summary>
[CollectionDefinition(nameof(MemoriaDaExecucaoPostgreSqlTestes), DisableParallelization = true)]
public sealed class ColecaoDaMemoriaDaExecucao;
