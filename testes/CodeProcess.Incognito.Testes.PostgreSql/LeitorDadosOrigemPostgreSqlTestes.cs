using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que a leitura do maior valor do PostgreSQL faz além do que a suíte confere: a segurança por linha faz a leitura falhar,
/// nunca devolver o maior das linhas visíveis, e o domínio sobre um inteiro conta como inteiro.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class LeitorDadosOrigemPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_faz_a_leitura_falhar_e_nunca_filtrar()
    {
        // O leitor da origem só vê a linha de 5: a de 907 fica escondida pela política. Filtrada, a leitura daria 5 e o N
        // sairia pequeno demais; com row_security desligada, o servidor recusa a consulta.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, "INSERT INTO loja.segredos (id, conteudo) VALUES (5, 'visivel'), (907, 'zqSENTINELAqz');", cancelamento);
            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);

            var excecao = await Record.ExceptionAsync(() => origem.Dados.LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "segredos"), "id"), cancelamento));

            Assert.NotNull(excecao);
            Assert.DoesNotContain("907", excecao.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_dominio_sobre_um_inteiro_e_lido_e_o_numeric_e_recusado()
    {
        // loja.numeros.positivo é de um domínio sobre integer; loja.tipos.valor é numeric, que o leitor classifica como
        // decimal: a recusa vem antes da consulta, sem o valor.
        var cancelamento = TestContext.Current.CancellationToken;
        var banco = await servidor.ClonarModeloAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(banco, "INSERT INTO loja.numeros (positivo) VALUES (3), (44); INSERT INTO loja.tipos (id, valor) VALUES (3, 8123.45);", cancelamento);
            await using var origem = await _provedor.ConectarOrigemAsync(
                new SegredoConexao(servidor.StringDeConexao(banco, ServidorPostgreSql.Leitor, servidor.SenhaLeitor)), cancelamento);

            Assert.Equal(44L, await origem.Dados.LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "numeros"), "positivo"), cancelamento));
            var recusa = await Assert.ThrowsAsync<DialetoRecusouException>(
                () => origem.Dados.LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "tipos"), "valor"), cancelamento));
            Assert.Equal("INC3108", recusa.Codigo);
            Assert.DoesNotContain("8123", recusa.ToString(), StringComparison.Ordinal);
            var inexistente = await Assert.ThrowsAsync<DialetoRecusouException>(
                () => origem.Dados.LerMaiorValorAsync(new ReferenciaColuna(new NomeObjeto("loja", "nao_existe"), "id"), cancelamento));
            Assert.Equal("INC3108", inexistente.Codigo);
        }
        finally
        {
            await servidor.ApagarAsync(banco);
        }
    }
}
