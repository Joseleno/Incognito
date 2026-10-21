using System.Text.Json;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O hash da configuração: o vetor fixo (a referência em Python confere o formato a partir do modelo; aqui, o YAML até o
/// hash), o que não muda o hash (comentários, quebras de linha, justificativa, ordem, a pasta do arquivo, a grafia
/// equivalente) e o que muda (cada campo que entra).
/// </summary>
public sealed class HashConfiguracaoTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "inc-hash-" + Guid.NewGuid().ToString("N"));

    public HashConfiguracaoTestes() => Directory.CreateDirectory(_pasta);

    public static TheoryData<string> Casos => [.. Vetor().RootElement.GetProperty("casos").EnumerateArray().Select(caso => caso.GetProperty("nome").GetString()!)];

    // Cada troca muda um campo que entra no hash, no YAML do caso com tabelas.
    public static TheoryData<string, string> Trocas => new()
    {
        { "  conexao: ambiente:INCOGNITO_ORIGEM", "  conexao: ambiente:INCOGNITO_ORIGEM_2" },
        { "  conexao: ambiente:INCOGNITO_ORIGEM", "  conexao: arquivo:INCOGNITO_ORIGEM" },
        { "  bancoEsperado: loja_homolog", "  bancoEsperado: loja_homolog_2" },
        { "  provedor: postgresql", "  provedor: sqlserver" },
        { "modo: reproduzivel\nchave: arquivo:segredos/chave.txt", "modo: efemero" },
        { "chave: arquivo:segredos/chave.txt", "chave: arquivo:segredos/outra.txt" },
        { "chave: arquivo:segredos/chave.txt", "chave: ambiente:INCOGNITO_CHAVE" },
        { "ambiente: homologacao", "ambiente: testes" },
        { "dominios:", "politica:\n  entradaInvalida: anular\ndominios:" },
        { "dominios:", "politica:\n  entradaMalformada: preservar\ndominios:" },
        { "dominios:", "politica:\n  consistencia: exigir\ndominios:" },
        { "dominios:", "politica:\n  paralelismo: 2\ndominios:" },
        { "br.cnpj@1, permutarOrdem: true }", "br.cnpj@1, permutarOrdem: false }" },
        { "br.cpf@1, entradaMalformada: anular }", "br.cpf@1 }" },
        { "  '\"Vendas\".\"Itens\"': { acao: esvaziar }", "  '\"Vendas\".\"Itens\"': { acao: ignorar }" },
        { "  '\"Vendas\".\"Itens\"': { acao: esvaziar }", "  '\"Vendas\".\"Itens\"': {}" },
        { "  '\"Vendas\".\"Itens\"': { acao: esvaziar }", "  '\"Vendas\".itens': { acao: esvaziar }" },
        { "  '\"Vendas\".\"Itens\"': { acao: esvaziar }", "" },
        { "      cidade: manter\n", "" },
        { "      '\"Telefone\"': { transformador: br.telefone }", "      Telefone: { transformador: br.telefone }" },
        { "      nome: { acao: manter, classe: sensivel, justificativa: \"o nome do caso\" }", "      nome: manter" },
        { "derivarDe: cidade }", "derivarDe: endereço }" },
        { "      cnpj: empresas", "      cnpj: documentos" },
        { "{ transformador: br.cnpj@1, entradaInvalida: falhar }", "{ transformador: br.cnpj@1, entradaInvalida: anular }" },
        { "      cpf: documentos", "      cpf: { transformador: br.cpf@1 }" },
    };

    // Cada troca não muda nada que entra: a escrita muda, o modelo resolvido não.
    public static TheoryData<string, string> Equivalentes => new()
    {
        { "  conexao: ambiente:INCOGNITO_ORIGEM", "  conexao: ambiente:INCOGNITO_ORIGEM # a origem\n# um comentário com um segredo esquecido" },
        { "\"o nome do caso\"", "\"outra justificativa\"" },
        { "{ transformador: br.telefone }", "{ transformador: br.telefone@1 }" },
        { "{ transformador: br.cnpj@1, entradaInvalida: falhar }", "{ transformador: br.cnpj@1, entradaInvalida: falhar, entradaMalformada: falhar }" },
        { "{ transformador: br.cpf@1, entradaMalformada: anular }", "{ entradaMalformada: anular, transformador: br.cpf@1 }" },
        { "      cidade: manter\n      endereço: manter\n", "      endereço: manter\n      cidade: manter\n" },
        { "dominios:", "politica:\n  entradaInvalida: preservar\n  entradaMalformada: falhar\ndominios:" },
        { "chave: arquivo:segredos/chave.txt", "chave: arquivo:segredos\\chave.txt" },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void O_hash_do_YAML_e_o_do_vetor(string nome)
    {
        var caso = Vetor().RootElement.GetProperty("casos").EnumerateArray().Single(item => item.GetProperty("nome").GetString() == nome);

        Assert.Equal(caso.GetProperty("hash").GetString(), Hash(caso.GetProperty("yaml").GetString()!));
    }

    [Fact]
    public void O_prefixo_do_vetor_e_o_do_formato()
    {
        var bytes = HashConfiguracao.Serializar(Yaml.Ler(Yaml.Minimo), ResolvedorDeRegras.Resolver(Yaml.Ler(Yaml.Minimo), new RegistroTransformadores()));

        Assert.Equal("incognito/hash-configuracao/v1", Vetor().RootElement.GetProperty("prefixo").GetString());
        Assert.Equal("incognito/hash-configuracao/v1\0"u8.ToArray(), bytes.Take(31));
    }

    [Theory]
    [MemberData(nameof(Trocas))]
    public void Cada_campo_que_entra_muda_o_hash(string original, string troca)
    {
        var base_ = Tabelas();
        Assert.Contains(original, base_, StringComparison.Ordinal);

        Assert.NotEqual(Hash(base_), Hash(base_.Replace(original, troca, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Equivalentes))]
    public void A_escrita_equivalente_nao_muda_o_hash(string original, string troca)
    {
        var base_ = Tabelas();
        Assert.Contains(original, base_, StringComparison.Ordinal);

        Assert.Equal(Hash(base_), Hash(base_.Replace(original, troca, StringComparison.Ordinal)));
    }

    [Fact]
    public void Quebras_de_linha_e_a_pasta_do_arquivo_nao_mudam_o_hash()
    {
        // O mesmo arquivo: relativo, de duas pastas diferentes, resolve dois caminhos absolutos diferentes; entra o escrito.
        var base_ = Tabelas();
        var outra = Path.Combine(_pasta, "outra", "pasta");
        Directory.CreateDirectory(outra);

        Assert.Equal(Hash(base_), Hash(base_.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Equal(Hash(base_), Hash(base_, outra));
    }

    [Fact]
    public async Task O_plano_publica_o_hash_da_configuracao()
    {
        var yaml = Yaml.Minimo;
        var plano = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(), yaml: yaml);

        var configuracao = Yaml.Ler(yaml);
        Assert.Equal(HashConfiguracao.Calcular(configuracao, ResolvedorDeRegras.Resolver(configuracao, new RegistroTransformadores())), plano.HashConfiguracao);
        Assert.Matches("^sha256:[0-9a-f]{64}$", plano.HashConfiguracao);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static JsonDocument Vetor() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vetores", "hash-configuracao@1.json")));

    private static string Tabelas() =>
        Vetor().RootElement.GetProperty("casos").EnumerateArray().Single(caso => caso.GetProperty("nome").GetString() == "3-tabelas").GetProperty("yaml").GetString()!;

    // Pelo disco: o caminho relativo do arquivo: só se resolve com a pasta do incognito.yaml.
    private string Hash(string yaml, string? pasta = null)
    {
        var arquivo = Path.Combine(pasta ?? _pasta, "incognito.yaml");
        File.WriteAllText(arquivo, yaml);
        var configuracao = ConfiguracaoIncognito.CarregarYaml(arquivo);
        return HashConfiguracao.Calcular(configuracao, ResolvedorDeRegras.Resolver(configuracao, new RegistroTransformadores()));
    }
}
