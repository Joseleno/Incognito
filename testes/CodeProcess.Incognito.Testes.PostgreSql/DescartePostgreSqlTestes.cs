using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O descarte depois de uma abertura que falhou, sem servidor: provocar a falha no descarte do Npgsql não é viável, e por
/// isso a abertura descarta por um ajudante, provado aqui com descartáveis falsos.
/// </summary>
public sealed class DescartePostgreSqlTestes
{
    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_falha_de_um_descarte_nao_toma_o_lugar_da_falha_nem_impede_os_seguintes()
    {
        var primeiro = new Descartavel(falha: true);
        var segundo = new Descartavel(falha: true);
        var terceiro = new Descartavel(falha: false);
        var falha = new InvalidOperationException("falha da abertura");

        var excecao = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            try
            {
                throw falha;
            }
            catch
            {
                await Descarte.DepoisDaFalhaAsync(primeiro, null, segundo, terceiro);
                throw;
            }
        });

        Assert.Same(falha, excecao);
        Assert.Equal([1, 1, 1], [primeiro.Descartes, segundo.Descartes, terceiro.Descartes]);
    }

    private sealed class Descartavel(bool falha) : IAsyncDisposable
    {
        internal int Descartes { get; private set; }

        public ValueTask DisposeAsync()
        {
            Descartes++;
            return falha ? ValueTask.FromException(new IOException("falha do descarte")) : ValueTask.CompletedTask;
        }
    }
}
