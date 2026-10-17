using System.Text;
using CodeProcess.Incognito.Configuracao;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>Leitura do incognito.yaml de disco: codificação, tamanho e o caminho relativo do <c>arquivo:</c>.</summary>
public sealed class ArquivoDeConfiguracaoTestes : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("incognito-config-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Arquivo_relativo_vale_a_partir_da_pasta_do_yaml()
    {
        var caminho = Gravar("incognito.yaml", Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: arquivo:segredos/incognito.chave"));

        var chave = ConfiguracaoIncognito.CarregarYaml(caminho).Chave!;

        Assert.Equal((TipoReferenciaSegredo.Arquivo, Path.Combine(_pasta, "segredos", "incognito.chave")), (chave.Tipo, chave.Alvo));
    }

    [Fact]
    public void Erro_traz_o_arquivo_como_foi_pedido()
    {
        var caminho = Gravar("incognito.yaml", Yaml.Trocando("versao: 1", "versao: 2"));

        var erro = Assert.Single(Excecao(caminho).Erros);

        Assert.Equal(caminho, erro.Arquivo);
        Assert.Equal($"{caminho}(1,9): INC2009: Versão do arquivo não suportada: esta versão do Incognito lê 'versao: 1'.", erro.ToString());
    }

    [Fact]
    public void BOM_UTF8_e_aceito()
    {
        var caminho = Path.Combine(_pasta, "incognito.yaml");
        File.WriteAllText(caminho, Yaml.Minimo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal("homologacao", ConfiguracaoIncognito.CarregarYaml(caminho).Ambiente);
    }

    [Fact]
    public void Arquivo_ausente_ou_pasta_e_recusado_sem_a_mensagem_do_sistema()
    {
        foreach (var caminho in new[] { Path.Combine(_pasta, "nao-existe.yaml"), _pasta })
        {
            var erro = Assert.Single(Excecao(caminho).Erros);
            Assert.Equal(("INC2001", (int?)null), (erro.Codigo, erro.Linha));
            Assert.Equal("Não foi possível ler o arquivo (não existe, é uma pasta ou não há permissão).", erro.Mensagem);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x76, 0x00 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x76 })]
    [InlineData(new byte[] { 0x76, 0x3A, 0x20, 0xC3, 0x28 })]
    [InlineData(new byte[] { 0x76, 0x3A, 0x20, 0xED, 0xA0, 0x80 })]
    public void Arquivo_fora_de_UTF8_e_recusado(byte[] conteudo)
    {
        var caminho = Path.Combine(_pasta, "incognito.yaml");
        File.WriteAllBytes(caminho, conteudo);

        Assert.Equal("INC2001", Assert.Single(Excecao(caminho).Erros).Codigo);
    }

    [Fact]
    public void Arquivo_acima_de_16_MiB_e_recusado()
    {
        var caminho = Path.Combine(_pasta, "incognito.yaml");
        File.WriteAllBytes(caminho, Encoding.UTF8.GetBytes(Yaml.Minimo + "#" + new string('x', ConfiguracaoIncognito.TamanhoMaximo)));

        Assert.Equal("O arquivo passa de 16 MiB.", Assert.Single(Excecao(caminho).Erros).Mensagem);
        Assert.Equal("O texto passa de 16 MiB.", Assert.Single(Yaml.Erros(File.ReadAllText(caminho))).Mensagem);
    }

    private string Gravar(string nome, string conteudo)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    private static ConfiguracaoInvalidaException Excecao(string caminho)
    {
        try
        {
            ConfiguracaoIncognito.CarregarYaml(caminho);
        }
        catch (ConfiguracaoInvalidaException excecao)
        {
            return excecao;
        }

        throw new XunitException("O arquivo deveria ter sido recusado.");
    }
}
