using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O <c>planejar</c> ponta a ponta contra o servidor, com o dialeto PostgreSQL: o maior valor da chave primária é lido
/// de verdade, o N sai dele, e o valor lido nunca aparece na recusa.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class PlanejarPostgreSqlTestes(ServidorPostgreSql servidor)
{
    // O maior valor gravado: cabe no integer, faz o N ser 10^10 e não é parte de N nem de N − 1.
    private const long Sentinela = 731_826_459;

    private const string Esquema = """
        CREATE SCHEMA permuta;
        CREATE TABLE permuta.pedidos (id integer PRIMARY KEY);
        CREATE TABLE permuta.itens (id bigint PRIMARY KEY, pedido integer REFERENCES permuta.pedidos (id));

        """;

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Integer_com_maior_valor_alto_sai_com_codigo_2_e_o_maior_nunca_aparece()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.ClonarModeloAsync(cancelamento);
        var destino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            var valores = string.Create(CultureInfo.InvariantCulture, $"INSERT INTO permuta.pedidos (id) VALUES (3), ({Sentinela}), (-8); INSERT INTO permuta.itens (id, pedido) VALUES (1, {Sentinela});");
            await servidor.ExecutarAsync(
                origem,
                Esquema + $"GRANT USAGE ON SCHEMA permuta TO {ServidorPostgreSql.Leitor}; GRANT SELECT ON ALL TABLES IN SCHEMA permuta TO {ServidorPostgreSql.Leitor};" + valores,
                cancelamento);
            await servidor.ExecutarAsync(
                destino,
                Esquema + $"ALTER SCHEMA permuta OWNER TO {ServidorPostgreSql.Dono}; ALTER TABLE permuta.pedidos OWNER TO {ServidorPostgreSql.Dono}; ALTER TABLE permuta.itens OWNER TO {ServidorPostgreSql.Dono};",
                cancelamento);
            var configuracao = ConfiguracaoIncognito.LerYaml("""
                versao: 1
                origem:
                  provedor: postgresql
                  conexao: ambiente:INCOGNITO_ORIGEM
                destino:
                  provedor: postgresql
                  conexao: ambiente:INCOGNITO_DESTINO
                  bancoEsperado: loja_homolog
                modo: reproduzivel
                chave: ambiente:INCOGNITO_CHAVE
                ambiente: homologacao
                tabelas:
                  permuta.pedidos:
                    colunas:
                      id: { transformador: id.permutar@1, dominio: pedido-id }

                """);

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PlanejadorInterno.PlanejarAsync(
                configuracao,
                new RegistroTransformadores(),
                new Dictionary<ProvedorBanco, ProvedorDialeto> { [ProvedorBanco.PostgreSql] = new ProvedorPostgreSql() },
                conexao => new SegredoConexao(ReferenceEquals(conexao, configuracao.Origem)
                    ? servidor.StringDeConexao(origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)
                    : servidor.StringDeConexao(destino, ServidorPostgreSql.Dono, servidor.SenhaDono)),
                cancelamento));

            // A PK e a FK, ambas integer, não comportam 9999999999 (N = 10^10, lido do maior valor).
            var naoCabe = recusa.Erros.Where(erro => erro.Codigo == "INC3090").Select(erro => $"{erro.Tabela}.{erro.Coluna}").Order(StringComparer.Ordinal);
            Assert.Equal(["permuta.itens.pedido", "permuta.pedidos.id"], naoCabe);
            Assert.All(recusa.Erros.Where(erro => erro.Codigo == "INC3090"), erro => Assert.Contains("N = 10^10", erro.Mensagem, StringComparison.Ordinal));

            string[] formas =
            [
                Sentinela.ToString(CultureInfo.InvariantCulture),
                Sentinela.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")),
                Sentinela.ToString("N0", CultureInfo.InvariantCulture),
                Sentinela.ToString("X", CultureInfo.InvariantCulture),
            ];
            var textos = recusa.Erros.Select(erro => erro.ToString()).Append(recusa.ToString()).ToList();
            foreach (var forma in formas)
            {
                Assert.All(textos, texto => Assert.DoesNotContain(forma, texto, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }
}
