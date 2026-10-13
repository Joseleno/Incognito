using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Pseudonimizacao;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// O que não pode alocar (validação, classificação, número e código da chave de DF-e) e o que só aloca a saída (chave
/// Pix aleatória, chave de DF-e), medido na thread do teste. Roda sozinho, depois das outras classes: com a suíte em
/// paralelo, a medida de uma thread chegou a somar de 0,7 a 8 KB que não eram do código medido (uma rodada em dez,
/// mesmo com a compilação em camadas desligada), e nada disso aparece com os testes sozinhos.
/// </summary>
[Collection(nameof(AlocacaoTestes))]
public sealed class AlocacaoTestes : IDisposable
{
    private readonly ChaveIncognito _chave = ChaveDoVetor();

    public void Dispose() => _chave.Dispose();

    [Fact]
    public void Validacao_com_span_nao_aloca()
    {
        var sorteio = new Random(1312);
        var cpf = MascararCpf(CpfValido(sorteio)).ToCharArray();
        var cnpj = MascararCnpj(CnpjValido(sorteio, alfanumerico: true)).ToCharArray();
        var chave = ChaveValida(sorteio, emitenteAlfanumerico: true).ToCharArray();
        var aceitos = Validar(cpf, cnpj, chave, 100);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        aceitos += Validar(cpf, cnpj, chave, 10_000);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
        Assert.Equal(3 * 10_100, aceitos);
    }

    [Fact]
    public void Classificar_o_emitente_nao_aloca()
    {
        var chaves = Chaves(2605);
        var cpfs = Classificar(chaves, 10);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        cpfs += Classificar(chaves, 1_000);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
        Assert.True(cpfs > 0, "Nenhuma chave da amostra foi classificada como CPF.");
    }

    [Fact]
    public void Numero_e_codigo_da_chave_de_DF_e_nao_alocam()
    {
        using var numeroDfe = new PseudonimizadorNumeroDfe(_chave, PseudonimizadorNumeroDfe.NomeDoDominio, 1, "homologacao");
        var chaves = Chaves(2719);
        var copia = new char[RegrasChaveDfe.Tamanho];
        PseudonimizarNumeros(numeroDfe, chaves, copia, 2);

        var antes = GC.GetAllocatedBytesForCurrentThread();
        PseudonimizarNumeros(numeroDfe, chaves, copia, 100);
        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        Assert.Equal(0L, alocados);
    }

    [Fact]
    public void Chave_Pix_aleatoria_aloca_so_a_saida()
    {
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        using var email = new PseudonimizadorEmail(_chave, "email", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        using var telefone = new PseudonimizadorTelefone(_chave, "telefone", 1, "homologacao", PoliticaEntradaMalformada.Falhar);
        using var pix = new PseudonimizadorChavePix(_chave, "chave-pix", 1, "homologacao", cpf, cnpj, email, telefone, PoliticaEntradaMalformada.Falhar);
        var uuid = new GeradorBr(1816).ChavePix(TipoChavePix.Aleatoria);
        for (var vez = 0; vez < 200; vez++)
        {
            pix.Pseudonimizar(uuid, out _);
        }

        var antes = GC.GetAllocatedBytesForCurrentThread();
        for (var vez = 0; vez < 1_000; vez++)
        {
            pix.Pseudonimizar(uuid, out _);
        }

        var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

        // Uma string de 36 caracteres ocupa 96 bytes em 64 bits.
        Assert.Equal(1_000 * 96L, alocados);
    }

    [Fact]
    public void Chave_de_DF_e_aloca_so_a_saida_em_cada_forma()
    {
        // Por chamada, exatamente a string da saída (cabeçalho, tamanho e caracteres, alinhados a 8 bytes): pura, com o
        // prefixo do Id, em grupos de 4 e com preenchimento.
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = new PseudonimizadorChaveDfe(_chave, 1, "homologacao", cpf, cnpj, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var gerador = new GeradorBr(2827);
        var formas = Enumerable.Range(0, 200)
            .Select(i => gerador.ChaveDfe(i % 2 == 0 ? 55 : 57, alfanumerico: i % 3 == 0))
            .SelectMany(chave => new[] { chave, (chave[20..22] == "55" ? "NFe" : "CTe") + chave, EmGrupos(chave), chave + "    " })
            .ToArray();
        foreach (var forma in formas)
        {
            chaveDfe.Pseudonimizar(forma, out _, out _);
        }

        foreach (var forma in formas)
        {
            var antes = GC.GetAllocatedBytesForCurrentThread();
            chaveDfe.Pseudonimizar(forma, out _, out _);
            var alocados = GC.GetAllocatedBytesForCurrentThread() - antes;

            Assert.Equal((22 + (2 * forma.Length) + 7) / 8 * 8, alocados);
        }
    }

    // Chaves classificadas dos nove modelos, com e sem NFF e com emitente CPF e CNPJ.
    private static char[][] Chaves(long semente)
    {
        var fonte = SequenciaXoshiro.DaSemente(semente);
        int[] modelos = [55, 65, 57, 58, 62, 63, 64, 66, 67];
        var chaves = new List<char[]>();
        for (var i = 0; chaves.Count < 64; i++)
        {
            var modelo = modelos[i % modelos.Length];
            var cpf = modelo is 57 or 58 && i % 2 == 0;
            var nff = modelo is 55 or 65 or 57 or 58 && i % 3 == 0;
            var especificacao = new EspecificacaoChaveDfe(
                2601,
                modelo,
                nff ? 26 : 1,
                nff ? '3' : '1',
                cpf ? EmitenteGerado.Cpf : EmitenteGerado.CnpjAlfanumerico,
                nff ? NumeroGerado.Nff : NumeroGerado.Qualquer,
                CodigoGerado.Qualquer);
            var chave = GeradorChaveDfe.Gerar(fonte, especificacao);
            if (!ClassificadorEmitente.Classificar(chave).EhAnomalia)
            {
                chaves.Add(chave.ToCharArray());
            }
        }

        return [.. chaves];
    }

    private static int Validar(char[] cpf, char[] cnpj, char[] chave, int vezes)
    {
        var aceitos = 0;
        for (var vez = 0; vez < vezes; vez++)
        {
            aceitos += (ValidadorBr.Cpf(cpf.AsSpan()) ? 1 : 0) + (ValidadorBr.Cnpj(cnpj.AsSpan()) ? 1 : 0) + (ValidadorBr.ChaveDfe(chave.AsSpan()) ? 1 : 0);
        }

        return aceitos;
    }

    private static int Classificar(char[][] chaves, int vezes)
    {
        var cpfs = 0;
        for (var vez = 0; vez < vezes; vez++)
        {
            foreach (var chave in chaves)
            {
                cpfs += ClassificadorEmitente.Classificar(chave).Tipo == TipoEmitente.Cpf ? 1 : 0;
            }
        }

        return cpfs;
    }

    private static void PseudonimizarNumeros(PseudonimizadorNumeroDfe numeroDfe, char[][] chaves, char[] copia, int vezes)
    {
        for (var vez = 0; vez < vezes; vez++)
        {
            foreach (var chave in chaves)
            {
                chave.CopyTo(copia, 0);
                numeroDfe.Pseudonimizar(copia);
            }
        }
    }

    private static string EmGrupos(string chave) => string.Join(' ', Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }
}

[CollectionDefinition(nameof(AlocacaoTestes), DisableParallelization = true)]
public sealed class ColecaoDeAlocacao;
