using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O modelo da igualdade numérica (<c>kn</c>) que o planejar usa na tabela de formato fixo, preso ao servidor: pares
/// conhecidos, uma amostra de saídas reais das embutidas da tabela e o par de chaves de DF-e que a tira da tabela,
/// comparados por uma colação ICU com a ordenação numérica.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class IgualdadeNumericaPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private const string Colacao = "CREATE COLLATION numerica (provider = icu, locale = 'und-u-kn-true', deterministic = false)";

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_servidor_iguala_os_pares_como_o_modelo()
    {
        (string Um, string Outro)[] pares =
        [
            ("ab01c2", "ab1c02"), ("a2b", "a02b"), ("a23b4", "a2b34"), ("00012345678", "12345678"), ("000", "0"),
            ("12.3B5.678/01A2-XX", "12.3B5.678/1A02-XX"), ("123.456.789-00", "12345678900"), ("ab01c2ff", "ab1c02ff"),
            ("0900123456", "900123456"),
        ];
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, Colacao, cancelamento);
            await using var conexao = await servidor.AbrirComoAdministradorAsync(banco, cancelamento);
            foreach (var (um, outro) in pares)
            {
                Assert.Equal(IgualdadeNumerica.Iguais(um, outro), await IguaisNoServidorAsync(conexao, um, outro, cancelamento));
            }
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_servidor_nunca_iguala_saidas_distintas_da_tabela_e_iguala_as_chaves_de_DFe_que_o_modelo_iguala()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var amostra = IgualdadeNumerica.SaidasDaTabela(150, 34044);
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, Colacao, cancelamento);
            await using var conexao = await servidor.AbrirComoAdministradorAsync(banco, cancelamento);

            // As saídas da tabela (CPF e e-mail): o servidor vê todas distintas, como o modelo.
            var daTabela = amostra.Where(par => par.Forma.StartsWith("cpf", StringComparison.Ordinal) || par.Forma == "email").Select(par => par.Saida).Distinct(StringComparer.Ordinal).ToArray();
            await using var comando = new NpgsqlCommand("SELECT count(DISTINCT valor COLLATE numerica) FROM unnest($1::text[]) AS valor", conexao) { Parameters = { new() { Value = daTabela } } };
            Assert.Equal(daTabela.Length, Convert.ToInt32(await comando.ExecuteScalarAsync(cancelamento), System.Globalization.CultureInfo.InvariantCulture));

            // Uma chave de DF-e real com emitente alfanumérico e a mesma com um zero antes do trecho de dígitos que segue uma
            // letra: distintas, e iguais pela comparação numérica, no modelo e no servidor.
            var chave = amostra.First(par => par.Forma == "dfe-alfanumerica" && TrechoAposLetra(par.Saida) >= 0).Saida;
            var indice = TrechoAposLetra(chave);
            var variante = chave[..indice] + "0" + chave[indice..];
            Assert.True(IgualdadeNumerica.Iguais(chave, variante));
            Assert.True(await IguaisNoServidorAsync(conexao, chave, variante, cancelamento));
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    private static async Task<bool> IguaisNoServidorAsync(NpgsqlConnection conexao, string um, string outro, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand("SELECT ($1 COLLATE numerica) = $2", conexao) { Parameters = { new() { Value = um }, new() { Value = outro } } };
        return (bool)(await comando.ExecuteScalarAsync(cancelamento))!;
    }

    // A posição do primeiro dígito que segue uma letra; -1 se não há.
    private static int TrechoAposLetra(string texto) =>
        Enumerable.Range(1, texto.Length - 1).FirstOrDefault(posicao => char.IsAsciiDigit(texto[posicao]) && char.IsLetter(texto[posicao - 1]), -1);
}
