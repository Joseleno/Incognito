using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A comparação de nomes do PostgreSQL contra o próprio servidor: cada nome escrito vira tabela, e o nome que o catálogo
/// guarda é o que a comparação diz que corresponde. Num banco de um byte por caractere, a dobra de uma letra fora do ASCII
/// depende do locale, e a resposta é indeterminada.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class NomesPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_comparacao_preve_o_nome_que_o_servidor_guarda()
    {
        // Um esquema por caso: a única tabela dele tem o nome que o servidor guardou para o que foi escrito.
        (string Escrito, bool EntreAspas)[] casos =
        [
            ("Cliente", false), ("cliente", false), ("Cliente", true), ("Ação", false), ("AÇÃO", false), ("nota fiscal", true),
            (new string('ç', 31) + "a", true),
        ];
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            var criar = string.Concat(casos.Select((caso, indice) =>
                $"CREATE SCHEMA c{indice}; CREATE TABLE c{indice}.{(caso.EntreAspas ? "\"" + caso.Escrito + "\"" : caso.Escrito)} (id integer); "));
            await servidor.ExecutarAsync(banco, criar + $"CREATE SCHEMA longo; CREATE TABLE longo.{new string('a', 64)} (id integer);", cancelamento);
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(servidor.StringDeConexaoDoAdministrador(banco)), cancelamento);
            var tabelas = (await origem.Esquema.LerAsync(cancelamento)).Tabelas;

            // Cada nome escrito contra o que o servidor guardou para todos os casos: corresponde se e só se o servidor guardou
            // o mesmo nome (as negativas também vêm do servidor, não de uma lista escrita à mão).
            var guardados = casos.Select((_, indice) => Assert.Single(tabelas, tabela => tabela.Nome.Esquema == $"c{indice}").Nome.Nome).ToArray();
            for (var indice = 0; indice < casos.Length; indice++)
            {
                var (escrito, entreAspas) = casos[indice];
                for (var outro = 0; outro < casos.Length; outro++)
                {
                    var esperada = guardados[indice] == guardados[outro] ? CorrespondenciaNome.Corresponde : CorrespondenciaNome.NaoCorresponde;
                    Assert.Equal(esperada, origem.Sql.CompararNome(escrito, entreAspas, guardados[outro]));
                }
            }

            // O servidor corta o nome longo em 63 bytes; a comparação não emula o corte e não o iguala a nada.
            var cortado = Assert.Single(tabelas, tabela => tabela.Nome.Esquema == "longo").Nome.Nome;
            Assert.Equal(new string('a', 63), cortado);
            Assert.Equal(CorrespondenciaNome.NaoCorresponde, origem.Sql.CompararNome(new string('a', 64), false, cortado));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("ISO_8859_5")]
    [InlineData("SQL_ASCII")]
    public async Task Num_banco_de_um_byte_a_letra_fora_do_ASCII_sem_aspas_e_indeterminada(string codificacao)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = $"inc_nomes_{Guid.NewGuid():N}";
        await servidor.ExecutarAsync("postgres", $"CREATE DATABASE {banco} TEMPLATE template0 ENCODING '{codificacao}' LC_COLLATE 'C' LC_CTYPE 'C'", cancelamento);
        try
        {
            await using var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao(servidor.StringDeConexaoDoAdministrador(banco)), cancelamento);

            Assert.Equal(CorrespondenciaNome.Indeterminada, origem.Sql.CompararNome("ЖУК", false, "жук"));
            Assert.Equal(CorrespondenciaNome.Indeterminada, origem.Sql.CompararNome("Ação", false, "ação"));
            Assert.Equal(CorrespondenciaNome.Corresponde, origem.Sql.CompararNome("Cliente", false, "cliente"));
            Assert.Equal(CorrespondenciaNome.Corresponde, origem.Sql.CompararNome("ЖУК", true, "ЖУК"));
            Assert.Equal(CorrespondenciaNome.NaoCorresponde, origem.Sql.CompararNome("ЖУК", true, "жук"));

            // O limite do name em bytes da codificação do banco: 40 letras são 40 bytes aqui (seriam 80 em UTF-8).
            Assert.Equal(CorrespondenciaNome.Corresponde, origem.Sql.CompararNome(new string('Ж', 40), true, new string('Ж', 40)));

            // Sem o esquema do diário, a lista vem vazia, e não nula (nula recusaria todo plano).
            var esquema = await origem.Esquema.LerAsync(cancelamento);
            Assert.NotNull(esquema.ObjetosNoEsquemaReservado);
            Assert.Empty(esquema.ObjetosNoEsquemaReservado);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(true, 4, "ç", 31, CorrespondenciaNome.Corresponde)]
    [InlineData(true, 4, "ç", 32, CorrespondenciaNome.NaoCorresponde)]
    [InlineData(false, 1, "ç", 63, CorrespondenciaNome.Corresponde)]
    [InlineData(false, 1, "ç", 64, CorrespondenciaNome.NaoCorresponde)]
    [InlineData(false, 3, "ç", 21, CorrespondenciaNome.Corresponde)]
    [InlineData(false, 3, "ç", 22, CorrespondenciaNome.Indeterminada)]
    [InlineData(false, 3, "ç", 63, CorrespondenciaNome.Indeterminada)]
    [InlineData(false, 3, "ç", 64, CorrespondenciaNome.NaoCorresponde)]
    [InlineData(false, 3, "a", 63, CorrespondenciaNome.Corresponde)]
    [InlineData(false, 3, "a", 64, CorrespondenciaNome.NaoCorresponde)]
    [InlineData(false, 0, "ç", 1, CorrespondenciaNome.Indeterminada)]
    public void O_limite_do_name_e_contado_em_bytes_da_codificacao_do_banco(bool utf8, int bytesPorCaractere, string letra, int repeticoes, CorrespondenciaNome esperada)
    {
        // Numa codificação de vários bytes que não é UTF-8, só se sabe quando cabe com folga ou não cabe de jeito nenhum.
        var nome = string.Concat(Enumerable.Repeat(letra, repeticoes));

        Assert.Equal(esperada, new RegraDosNomes(utf8, bytesPorCaractere, 63).Comparar(nome, entreAspas: true, nome));
    }
}
