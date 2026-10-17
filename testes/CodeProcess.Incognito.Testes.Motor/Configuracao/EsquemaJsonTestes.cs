using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeProcess.Incognito.Configuracao.Leitura;
using Corvus.Json;
using JsonSchema = Corvus.Json.Validator.JsonSchema;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// O JSON Schema publicado (esquema/incognito.schema.json, para o autocompletar dos editores) e o validador concordam:
/// o que um aceita o outro aceita, salvo as regras que só o validador consegue conferir, listadas aqui. O YAML vira
/// JSON pelos mesmos tipos do core schema do YAML 1.2 que o validador usa.
/// </summary>
public sealed class EsquemaJsonTestes
{
    private static readonly Lazy<JsonSchema> _esquema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "esquema", "incognito.schema.json")),
            "https://raw.githubusercontent.com/Joseleno/Incognito/main/esquema/incognito.schema.json"));

    public static TheoryData<string> Validos() =>
    [
        Yaml.Minimo,
        Yaml.Exemplo,
        Yaml.Trocando("modo: reproduzivel", "modo: efemero").Replace("chave: ambiente:INCOGNITO_CHAVE\n", "", StringComparison.Ordinal),
        Yaml.Com("tabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n  public.itens:\n    colunas:\n      pedido_id: pedido_id\n      cpf: { dominio: pedido_id, entradaMalformada: anular }\n"),
        Yaml.Com("tabelas:\n  '\"Vendas\".\"Clientes\"': { acao: ignorar }\n  dbo.pedidos: { acao: copiar }\n"),
        Yaml.Com("dominios:\n  chave-dfe: { transformador: br.chave-dfe, modelos: [55, 65], permutarOrdem: true }\n"),
    ];

    public static TheoryData<string> InvalidosNosDois() =>
    [
        Yaml.Com("tamanhoDoLote: 10\n"),
        Yaml.Trocando("ambiente: homologacao", null),
        Yaml.Trocando("modo: reproduzivel", "modo: reprodutivel"),
        Yaml.Trocando("modo: reproduzivel", "modo: efemero"),
        Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", null),
        Yaml.Trocando("  bancoEsperado: loja_homolog", null),
        Yaml.Trocando("  bancoEsperado: loja_homolog", "  bancoEsperado: \" \""),
        Yaml.Trocando("  conexao: ambiente:INCOGNITO_ORIGEM", "  conexao: Host=db;Database=loja"),
        Yaml.Trocando("ambiente: homologacao", "ambiente: Homologacao"),
        Yaml.Trocando("ambiente: homologacao", "ambiente: 2024"),
        Yaml.Trocando("versao: 1", "versao: \"1\""),
        Yaml.Trocando("versao: 1", "versao: 2"),
        Yaml.Com("politica:\n  suspeitaSemRegra: copiar\n"),
        Yaml.Com("politica:\n  paralelismo: 65\n"),
        Yaml.Com("dominios:\n  CPF: { transformador: br.cpf@1 }\n"),
        Yaml.Com("dominios:\n  manter: { transformador: br.cpf@1 }\n"),
        Yaml.Com("dominios:\n  cpf: { transformador: br.cpf@0 }\n"),
        Yaml.Com("dominios:\n  cpf: { transformador: br.cpf@1, derivarDe: nome }\n"),
        Yaml.Com("tabelas:\n  clientes: { acao: esvaziar }\n"),
        Yaml.Com("tabelas:\n  public.clientes: { acao: esvaziar, colunas: { cpf: manter } }\n"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      cpf: { acao: manter, transformador: br.cpf@1 }\n"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      laudo: { acao: manter, classe: sensivel }\n"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1, dominio: outro }\n"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      cpf: { classe: sensivel }\n"),
        Yaml.Com("dominios:\n  chave-dfe: { transformador: br.chave-dfe@1, modelos: [55, \"57,65\"] }\n"),
        Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: \"arquivo:/x \""),
    ];

    // O que o esquema não expressa: referências entre partes do arquivo, segredo por extenso, caminhos e caracteres.
    public static TheoryData<string> InvalidosSoNoValidador() =>
    [
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      cpf: rg\n"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      a: { transformador: br.cep@1, derivarDe: b }\n      b: { transformador: br.cep@1, derivarDe: a }\n"),
        Yaml.Com("dominios:\n  pedido_id: { transformador: br.cpf@1 }\ntabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n"),
        Yaml.Trocando("  conexao: ambiente:INCOGNITO_DESTINO", "  conexao: ambiente:INCOGNITO_ORIGEM"),
        Yaml.Minimo.Replace("destino:\n  provedor: postgresql", "destino:\n  provedor: sqlserver", StringComparison.Ordinal),
        Yaml.Com("# Password=senha-antiga\n"),
        Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: arquivo:segredos/incognito.chave"),
        Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", @"chave: arquivo:\\servidor\share\incognito.chave"),
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      '\"ca" + (char)0x200D + "fe\"': manter\n"),
        Yaml.Com("tabelas:\n  public.1clientes: { acao: esvaziar }\n"),
        // O draft-07 compara números pelo valor: 1.0 vale 1 e 4.0 é inteiro.
        Yaml.Trocando("versao: 1", "versao: 1.0"),
        Yaml.Com("politica:\n  paralelismo: 4.0\n"),
        // O esquema limita o transformador inteiro (nome, '@' e versão), não o nome sozinho.
        Yaml.Com("dominios:\n  cpf: { transformador: " + new string('a', 65) + " }\n"),
    ];

    // Campo opcional escrito como nulo: o validador o trata como ausente; o esquema, mais estrito, o recusa (o editor
    // aponta, o Incognito aceita).
    public static TheoryData<string> InvalidosSoNoEsquema() =>
    [
        Yaml.Com("politica:\n  consistencia: ~\n"),
        Yaml.Com("tabelas:\n  public.clientes: { acao: ~ }\n"),
        // Justificativa em branco numa coluna não sensível: o validador a trata como ausente.
        Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      c: { acao: manter, justificativa: \" \" }\n"),
    ];

    [Theory]
    [MemberData(nameof(InvalidosSoNoEsquema))]
    public void Invalido_so_no_esquema(string texto)
    {
        Yaml.Ler(texto);
        Assert.False(ValidoNoEsquema(texto));
    }

    [Theory]
    [MemberData(nameof(Validos))]
    public void Valido_no_validador_e_no_esquema(string texto)
    {
        Yaml.Ler(texto);
        Assert.True(ValidoNoEsquema(texto));
    }

    [Theory]
    [MemberData(nameof(InvalidosNosDois))]
    public void Invalido_no_validador_e_no_esquema(string texto)
    {
        Assert.NotEmpty(Yaml.Erros(texto));
        Assert.False(ValidoNoEsquema(texto));
    }

    [Theory]
    [MemberData(nameof(InvalidosSoNoValidador))]
    public void Invalido_so_no_validador(string texto)
    {
        Assert.NotEmpty(Yaml.Erros(texto));
        Assert.True(ValidoNoEsquema(texto));
    }

    private static bool ValidoNoEsquema(string texto)
    {
        var erros = new ColetorDeErros(null);
        var raiz = LeitorYaml.Ler(PassadaDeCaracteres.Conferir(texto, erros), erros);
        Assert.NotNull(raiz);

        using var documento = JsonDocument.Parse(ParaJson(raiz));
        return _esquema.Value.Validate(documento.RootElement, ValidationLevel.Flag).IsValid;
    }

    private static string ParaJson(NoYaml raiz)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria))
        {
            Escrever(raiz, escritor);
        }

        return Encoding.UTF8.GetString(memoria.ToArray());
    }

    private static void Escrever(NoYaml no, Utf8JsonWriter escritor)
    {
        switch (no)
        {
            case MapaYaml mapa:
                escritor.WriteStartObject();
                foreach (var (chave, valor) in mapa.Entradas)
                {
                    escritor.WritePropertyName(chave.Valor);
                    Escrever(valor, escritor);
                }

                escritor.WriteEndObject();
                break;
            case ListaYaml lista:
                escritor.WriteStartArray();
                foreach (var item in lista.Itens)
                {
                    Escrever(item, escritor);
                }

                escritor.WriteEndArray();
                break;
            case EscalarYaml escalar:
                switch (escalar.Tipo)
                {
                    case TipoEscalar.Nulo:
                        escritor.WriteNullValue();
                        break;
                    case TipoEscalar.Booleano:
                        escritor.WriteBooleanValue(escalar.Valor.StartsWith('t') || escalar.Valor.StartsWith('T'));
                        break;
                    case TipoEscalar.Inteiro or TipoEscalar.Decimal when decimal.TryParse(escalar.Valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero):
                        escritor.WriteNumberValue(numero);
                        break;
                    case TipoEscalar.Inteiro or TipoEscalar.Decimal:
                        escritor.WriteRawValue("0.5");
                        break;
                    default:
                        escritor.WriteStringValue(escalar.Valor);
                        break;
                }

                break;
        }
    }
}
