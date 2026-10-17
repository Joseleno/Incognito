namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// A configuração repete a regra dos nomes do núcleo só para apontar linha e coluna; as duas precisam aceitar e recusar
/// os mesmos nomes. O núcleo é conferido pela API pública (o ambiente da fachada), sem acesso aos internos dele.
/// </summary>
public sealed class RegraDosNomesTestes
{
    // Fora da regra (INC2010) ou com caractere invisível, recusado antes (INC2002).
    private static readonly string[] _codigosDeNomeRecusado = ["INC2010", "INC2002"];

    public static TheoryData<string> Nomes() =>
    [
        "homologacao", "a", "0", "a.b", "a_b", "a-b", "a1.b2-c3_d4", new string('a', 64),
        new string('a', 65), "Homologacao", "homologação", "a..b", "a.", ".a", "_a", "a_", "a b", "a@b", "a/b",
        "a" + (char)0x200B + "b", "caf" + 'e' + (char)0x0301, "a" + (char)0x00A0 + "b", "ａ",
    ];

    [Theory]
    [MemberData(nameof(Nomes))]
    public void Configuracao_e_nucleo_concordam_no_ambiente(string nome)
    {
        bool aceitoPeloNucleo;
        using (var chave = ChaveIncognito.Gerar())
        {
            try
            {
                using var pseudonimizador = new Pseudonimizador(chave, nome);
                aceitoPeloNucleo = true;
            }
            catch (ArgumentException)
            {
                aceitoPeloNucleo = false;
            }
        }

        var texto = Yaml.Trocando("ambiente: homologacao", "ambiente: \"" + nome + "\"");
        bool aceitoPelaConfiguracao;
        try
        {
            aceitoPelaConfiguracao = Yaml.Ler(texto).Ambiente == nome;
        }
        catch (CodeProcess.Incognito.Configuracao.ConfiguracaoInvalidaException excecao)
        {
            Assert.All(excecao.Erros, erro => Assert.Contains(erro.Codigo, _codigosDeNomeRecusado));
            aceitoPelaConfiguracao = false;
        }

        Assert.Equal(aceitoPeloNucleo, aceitoPelaConfiguracao);
    }
}
