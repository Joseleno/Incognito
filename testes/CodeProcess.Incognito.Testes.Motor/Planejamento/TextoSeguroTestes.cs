using System.Text;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CsCheck;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>O escape do texto de fora do motor, a faixa dos códigos dos dialetos e o código de saída pelo tipo.</summary>
public sealed class TextoSeguroTestes
{
    [Theory]
    [InlineData("cliente", "cliente")]
    [InlineData("nota fiscal", "nota fiscal")]
    [InlineData("ação", "ação")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a\tb", "a\\u0009b")]
    [InlineData("a\nb\r", "a\\u000Ab\\u000D")]
    [InlineData("\u001b[2J", "\\u001B[2J")]
    [InlineData("\u0085\u2028\u2029", "\\u0085\\u2028\\u2029")]
    [InlineData("a\u202Eb\u2066c\u200Ed\u061C", "a\\u202Eb\\u2066c\\u200Ed\\u061C")]
    [InlineData("a\u200Bb\u00ADc\uFEFF", "a\\u200Bb\\u00ADc\\uFEFF")]
    [InlineData("a\uE000", "a\\uE000")]
    [InlineData("😀", "😀")]
    [InlineData("a\uDB40\uDC41", "a\\uDB40\\uDC41")]
    public void O_escape_troca_o_que_a_leitura_do_yaml_recusa_e_a_barra(string texto, string escapado) =>
        Assert.Equal(escapado, TextoSeguro.Nome(texto));

    [Fact]
    public void O_surrogate_sem_par_e_escapado()
    {
        // Fora dos dados da teoria: o surrogate sem par não sobrevive à serialização dos casos.
        Assert.Equal("a\\uD800b", TextoSeguro.Nome("a\uD800b"));
        Assert.Equal("a\\uDC00", TextoSeguro.Nome("a\uDC00"));
    }

    [Fact]
    public void O_escape_e_injetivo_e_nao_deixa_caractere_recusado()
    {
        // Sem escapar a barra, um nome com o texto \u000A seria igual a uma quebra escapada.
        Assert.NotEqual(TextoSeguro.Nome("\\u000A"), TextoSeguro.Nome("\n"));

        var caracteres = Gen.Char[(char)0, (char)0xFFFF];
        var textos = Gen.OneOf(caracteres.Array[0, 12].Select(unidades => new string(unidades)), Gen.Const("\\u000A"), Gen.Const("\\"));
        Gen.Select(textos, textos).Sample((um, outro) => um == outro || TextoSeguro.Nome(um) != TextoSeguro.Nome(outro), iter: 20000);
        textos.Sample(texto => SemRecusado(TextoSeguro.Nome(texto)), iter: 20000);

        // A ida e a volta: desfazer o escape devolve o texto, o que prova a injetividade de fato.
        textos.Sample(texto => Desescapar(TextoSeguro.Nome(texto)) == texto, iter: 20000);
    }

    [Fact]
    public void O_arquivo_sai_escapado_nos_tres_ToString_com_a_barra_como_esta()
    {
        // Um nome de arquivo com ESC e U+202E mudaria o que o terminal mostra; o separador do Windows fica como está.
        var arquivo = "C:\\dados\\a" + (char)0x1B + "[2J" + (char)0x202E + "b.yaml";
        const string Escapado = "C:\\dados\\a\\u001B[2J\\u202Eb.yaml";
        var posicao = new PosicaoConfiguracao(arquivo, 3, 5, "/tabelas");

        Assert.Equal(Escapado, TextoSeguro.Caminho(arquivo));
        Assert.Equal(Escapado + "(3,5): INC3071: m", new ErroPlano("INC3071", "m", posicao: posicao).ToString());
        Assert.Equal(Escapado + "(3,5): INC3572: m", new Aviso("INC3572", "m", posicao: posicao).ToString());
        Assert.Equal(Escapado + "(3,5): INC2009: m", new ErroConfiguracao("INC2009", arquivo, 3, 5, "/modo", "m").ToString());
        Assert.Equal(Escapado + ": INC2001: m", new ErroConfiguracao("INC2001", arquivo, null, null, "", "m").ToString());
        Assert.Equal((arquivo, arquivo), (posicao.Arquivo, new ErroConfiguracao("INC2001", arquivo, null, null, "", "m").Arquivo));

        var caracteres = Gen.Char[(char)0, (char)0xFFFF];
        caracteres.Array[0, 12].Select(unidades => new string(unidades)).Sample(texto => SemRecusado(TextoSeguro.Caminho(texto)), iter: 20000);
    }

    // Desfaz o escape: a barra dobrada e o \uXXXX.
    private static string Desescapar(string escapado)
    {
        var saida = new StringBuilder();
        for (var indice = 0; indice < escapado.Length; indice++)
        {
            if (escapado[indice] != '\\')
            {
                saida.Append(escapado[indice]);
            }
            else if (escapado[indice + 1] == '\\')
            {
                saida.Append('\\');
                indice++;
            }
            else
            {
                saida.Append((char)Convert.ToInt32(escapado.Substring(indice + 2, 4), 16));
                indice += 5;
            }
        }

        return saida.ToString();
    }

    [Fact]
    public void A_mensagem_e_cortada_sem_partir_par_nem_escape()
    {
        Assert.Equal(new string('x', 500), TextoSeguro.Mensagem(new string('x', 500)));
        Assert.Equal(new string('x', 500) + "…", TextoSeguro.Mensagem(new string('x', 501)));

        // O escape de 6 unidades e o par surrogate perto do limite saem inteiros ou não saem.
        var comEscape = TextoSeguro.Mensagem(new string('x', 497) + "\n" + "fim");
        Assert.Equal(new string('x', 497) + "…", comEscape);
        var comPar = TextoSeguro.Mensagem(new string('x', 499) + "😀" + "fim");
        Assert.Equal(new string('x', 499) + "…", comPar);

        Gen.String[0, 700].Sample(
            texto =>
            {
                var cortada = TextoSeguro.Mensagem(texto);
                return cortada.Length <= TextoSeguro.MaximoDaMensagem + 1 && SemRecusado(cortada) && TextoSeguro.Nome(texto).StartsWith(cortada.TrimEnd('…'), StringComparison.Ordinal);
            },
            iter: 5000);
    }

    [Theory]
    [InlineData("INC3100", true)]
    [InlineData("INC3150", true)]
    [InlineData("INC3299", true)]
    [InlineData("INC3499", true)]
    [InlineData("INC3001", false)]
    [InlineData("INC3510", false)]
    [InlineData("INC3500", false)]
    [InlineData("INC2001", false)]
    [InlineData("INC1005", false)]
    [InlineData("INC31", false)]
    [InlineData("INC310", false)]
    [InlineData("INC31000", false)]
    [InlineData("INC31x0", false)]
    [InlineData("INC31١٢", false)]
    [InlineData("inc3101", false)]
    [InlineData(" INC3101", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_faixa_dos_dialetos_e_conferida_caractere_a_caractere(string? codigo, bool daFaixa) =>
        Assert.Equal(daFaixa, CodigosPlanejar.DaFaixaDosDialetos(codigo));

    [Fact]
    public void O_codigo_de_saida_sai_do_tipo_da_excecao()
    {
        var configuracao = Assert.Throws<ConfiguracaoInvalidaException>(() => ConfiguracaoIncognito.LerYaml("versao: 1\n"));

        Assert.Equal(2, ClassificadorDeSaida.De(configuracao));
        Assert.Equal(2, ClassificadorDeSaida.De(new PlanoRecusadoException([new ErroPlano(CodigosPlanejar.DialetoAusente, "sem dialeto")])));
        Assert.Equal(2, ClassificadorDeSaida.De(new PlanoRecusadoException([new ErroPlano("INC3102", "recusa do dialeto")])));
        Assert.Equal(1, ClassificadorDeSaida.De(new FalhaDialetoException(CodigosPlanejar.FalhaDoDialeto, "falha")));
        Assert.Equal(1, ClassificadorDeSaida.De(new FalhaDialetoException(CodigosPlanejar.EsquemaIncoerente, "incoerente")));

        // Nunca pela faixa do código: um INC2xxx de outro tipo não é configuração, e um INC30xx de outro tipo não é plano.
        Assert.Equal(1, ClassificadorDeSaida.De(new IncognitoException("INC2001", "outro tipo")));
        Assert.Equal(1, ClassificadorDeSaida.De(new IncognitoException(CodigosPlanejar.Agregado, "outro tipo")));
        Assert.Equal(130, ClassificadorDeSaida.De(new OperationCanceledException()));
        Assert.Equal(130, ClassificadorDeSaida.De(new TaskCanceledException()));
        Assert.Equal(1, ClassificadorDeSaida.De(new InvalidOperationException()));
    }

    [Fact]
    public void O_plano_recusado_guarda_ate_100_erros_e_diz_quantos_ficaram_de_fora()
    {
        var erros = Enumerable.Range(1, 101).Select(indice => new ErroPlano(CodigosPlanejar.DialetoAusente, $"erro {indice}")).ToArray();

        var muitos = new PlanoRecusadoException(erros);
        var um = new PlanoRecusadoException(erros[..1]);

        Assert.Equal(100, muitos.Erros.Count);
        Assert.StartsWith("Plano recusado (mais de 100 erros; 100 estão em Erros). Primeiro: INC3003: erro 1", muitos.Message, StringComparison.Ordinal);
        Assert.StartsWith("Plano recusado (1 erro).", um.Message, StringComparison.Ordinal);
        Assert.Equal("INC3000", muitos.Codigo);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanoRecusadoException([]));
    }

    private static bool SemRecusado(string texto)
    {
        for (var indice = 0; indice < texto.Length; indice++)
        {
            if (char.IsSurrogate(texto[indice]))
            {
                if (!char.IsHighSurrogate(texto[indice]) || indice + 1 >= texto.Length || !char.IsLowSurrogate(texto[indice + 1])
                    || PassadaDeCaracteres.Motivo(new Rune(texto[indice], texto[indice + 1])) is not null)
                {
                    return false;
                }

                indice++;
            }
            else if (texto[indice] == '\t' || PassadaDeCaracteres.Motivo(new Rune(texto[indice])) is not null)
            {
                return false;
            }
        }

        return true;
    }
}
