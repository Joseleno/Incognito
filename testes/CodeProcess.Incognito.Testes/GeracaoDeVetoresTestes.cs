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
                // O vetor de volume nunca é regravado: só é criado.
                using var novo = new FileStream(Path.Combine(pasta, arquivo), FileMode.CreateNew, FileAccess.Write);
                novo.Write(VolumeDeVetores.Escrever(chave, textoDaChave));
                continue;
            }

            var receita = ReceitasDeVetores.Todas.SingleOrDefault(r => r.Arquivo == arquivo)
                ?? throw new InvalidOperationException($"Sem receita para '{arquivo}'.");
            var acrescentados = GeradorDeVetores.Gerar(receita, pasta, chave, textoDaChave);
            TestContext.Current.TestOutputHelper?.WriteLine($"{arquivo}: {acrescentados} caso(s) acrescentado(s).");
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
