using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>A ordem estável dos erros e dos avisos e a fusão dos avisos repetidos.</summary>
public sealed class ColetorDoPlanoTestes
{
    private static readonly NomeTabela _alfa = new("loja", "alfa");
    private static readonly NomeTabela _beta = new("loja", "beta");

    [Fact]
    public void Os_erros_saem_por_tabela_coluna_codigo_objeto_e_posicao()
    {
        // Juntados de trás para a frente: cada critério desempata o anterior.
        var esperados = new (ErroPlano Erro, int? Coluna)[]
        {
            (new ErroPlano("INC3010", "sem tabela, linha 2", posicao: new PosicaoConfiguracao(null, 2, 1, "")), null),
            (new ErroPlano("INC3010", "sem tabela, linha 5", posicao: new PosicaoConfiguracao(null, 5, 1, "")), null),
            (new ErroPlano("INC3010", "sem tabela, linha 5, coluna 9", posicao: new PosicaoConfiguracao(null, 5, 9, "")), null),
            (new ErroPlano("INC3073", "objeto a", objeto: "a"), null),
            (new ErroPlano("INC3073", "objeto b", objeto: "b"), null),
            (new ErroPlano("INC3020", "alfa, sem coluna", _alfa), null),
            (new ErroPlano("INC3011", "alfa, coluna 0", _alfa, "x"), 0),
            (new ErroPlano("INC3022", "alfa, coluna 0, outro código", _alfa, "x"), 0),
            (new ErroPlano("INC3011", "alfa, coluna 3", _alfa, "y"), 3),
            (new ErroPlano("INC3010", "beta", _beta), null),
        };
        var coletor = new ColetorDoPlano();
        foreach (var (erro, coluna) in esperados.Reverse())
        {
            coletor.Erro(erro, coluna);
        }

        var recusa = Assert.Throws<PlanoRecusadoException>(coletor.LancarSeHouverErros);

        Assert.Equal(esperados.Select(par => par.Erro.Mensagem), recusa.Erros.Select(erro => erro.Mensagem));
    }

    [Fact]
    public void Avisos_iguais_saem_uma_vez_e_os_que_so_diferem_na_mensagem_ficam()
    {
        var coletor = new ColetorDoPlano();
        coletor.Aviso(new Aviso("INC3523", "O CHECK x só existe na origem.", _alfa, objeto: "x"));
        coletor.Aviso(new Aviso("INC3523", "O CHECK x só existe na origem.", _alfa, objeto: "x"));
        coletor.Aviso(new Aviso("INC3523", "O índice único x só existe na origem.", _alfa, objeto: "x"));
        coletor.LancarSeHouverErros();

        Assert.False(coletor.TemErros);
        Assert.Equal(["O CHECK x só existe na origem.", "O índice único x só existe na origem."], coletor.Avisos().Select(aviso => aviso.Mensagem));
    }

    [Fact]
    public void A_recusa_confere_a_lista_de_erros()
    {
        Assert.Throws<ArgumentNullException>(() => new PlanoRecusadoException(null!));
        Assert.Throws<ArgumentNullException>(() => new PlanoRecusadoException([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanoRecusadoException([]));
    }
}
