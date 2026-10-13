using System.Text.Json;
using CodeProcess.Incognito.Testes.Vetores;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Gerador dos vetores fixos, pulado salvo com <c>INCOGNITO_GERAR_VETORES</c> com os arquivos separados por vírgula
/// (<c>br.cpf@1.json</c>), e recusado no CI. Grava em <c>testes/vetores/</c> do repositório; arquivo existente só ganha
/// casos novos, depois de reconferido. A contagem de casos acrescentados sai com <c>--output Detailed</c>. Depois de
/// gerar: rodar a referência em Python (<c>py -3 -I testes/referencia/conferir_vetores.py</c> no Windows) e só então
/// fazer o commit.
/// </summary>
public sealed class GeracaoDeVetoresTestes
{
    private const string Variavel = "INCOGNITO_GERAR_VETORES";

    [Fact]
    public void Gerar_os_vetores_pedidos()
    {
        var pedidos = Environment.GetEnvironmentVariable(Variavel);
        if (string.IsNullOrWhiteSpace(pedidos))
        {
            Assert.Skip($"Só roda com {Variavel}.");
        }

        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Os vetores nunca são gerados no CI.");
        var pasta = Path.Combine(RaizDoRepositorio(), "testes", "vetores");
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        var textoDaChave = ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto");
        using var chave = ChaveIncognito.Ler(textoDaChave);
        foreach (var arquivo in pedidos.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (arquivo == VolumeDeVetores.Arquivo)
            {
                GerarVolume(Path.Combine(pasta, arquivo), chave, textoDaChave);
                continue;
            }

            var receita = ReceitasDeVetores.Todas.SingleOrDefault(r => r.Arquivo == arquivo)
                ?? throw new InvalidOperationException($"Sem receita para '{arquivo}'.");
            var acrescentados = GeradorDeVetores.Gerar(receita, pasta, chave, textoDaChave);
            TestContext.Current.TestOutputHelper?.WriteLine($"{arquivo}: {acrescentados} caso(s) acrescentado(s).");
        }
    }

    // O vetor de volume só ganha casos: o cabeçalho e os casos que já estão no arquivo precisam sair iguais, comparados
    // como JSON (como a guarda de crescimento do CI); o diff do arquivo mostra se algum byte deles mudou.
    private static void GerarVolume(string caminho, ChaveIncognito chave, string textoDaChave)
    {
        var conteudo = VolumeDeVetores.Escrever(chave, textoDaChave);
        if (File.Exists(caminho))
        {
            using var antigo = JsonDocument.Parse(File.ReadAllBytes(caminho), EsquemaDeVetores.Opcoes);
            using var novo = JsonDocument.Parse(conteudo);
            var casosAntigos = antigo.RootElement.GetProperty("casos").EnumerateArray().ToList();
            var casosNovos = novo.RootElement.GetProperty("casos").EnumerateArray().ToList();
            var iguais = antigo.RootElement.GetProperty("descricao").GetString() == novo.RootElement.GetProperty("descricao").GetString()
                && antigo.RootElement.GetProperty("chave").GetString() == novo.RootElement.GetProperty("chave").GetString()
                && casosAntigos.Count <= casosNovos.Count
                && casosAntigos.Zip(casosNovos).All(par => JsonElement.DeepEquals(par.First, par.Second));
            Assert.True(iguais, $"{VolumeDeVetores.Arquivo}: um caso existente mudaria.");
        }

        var temporario = caminho + ".novo";
        try
        {
            using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write))
            {
                arquivo.Write(conteudo);
            }

            File.Move(temporario, caminho, overwrite: true);
        }
        finally
        {
            File.Delete(temporario);
        }
    }

    private static string RaizDoRepositorio()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "Incognito.slnx")) && File.Exists(Path.Combine(pasta.FullName, "global.json")))
            {
                return pasta.FullName;
            }
        }

        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
