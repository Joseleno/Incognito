using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Dialetos;

// O que o planejar usa para decidir se um pseudônimo cabe na coluna e se a unicidade continua valendo: colação,
// comprimento, faixa, codificação e as colunas que uma expressão cita. Nenhum campo traz texto opaco do banco (expressão,
// locale, regras, default): só nomes de catálogo, números e marcas.

/// <summary>
/// As colunas que uma expressão, um predicado ou uma restrição citam, sem dizer o que a expressão faz com elas.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ReferenciasDaExpressao
{
    private readonly IReadOnlyList<string> _colunas = [];

    /// <summary>
    /// As colunas citadas, sem repetição, na ordem das colunas da tabela, sem as de sistema. Vazia: a expressão não cita
    /// coluna (uma constante) ou só cita o que <see cref="Incompletas"/> marca.
    /// </summary>
    /// <exception cref="ArgumentException">Nome vazio ou repetido.</exception>
    public required IReadOnlyList<string> Colunas
    {
        get => _colunas;
        init
        {
            var copia = Lista.CopiarNomes(value, nameof(Colunas));
            if (copia.Distinct(StringComparer.Ordinal).Count() != copia.Count)
            {
                throw new ArgumentException("A lista tem nome repetido.", nameof(Colunas));
            }

            _colunas = copia;
        }
    }

    /// <summary>
    /// Se a expressão cita a linha inteira ou algo que o dialeto não decompõe: trate como se citasse qualquer coluna. Uma
    /// função que leia outra tabela continua invisível, e nada aqui diz o que a expressão faz com as colunas (um
    /// <c>lower</c> iguala saídas que só diferem na caixa).
    /// </summary>
    public required bool Incompletas { get; init; }
}

/// <summary>
/// Uma colação: como o banco compara textos. Só o nome e o comportamento medido, nunca o locale nem as regras, que são
/// texto livre gravável por quem cria a colação.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class ColacaoEsquema
{
    private readonly string _nome = null!;
    private readonly string? _esquema;

    /// <summary>
    /// O nome no catálogo (<c>collname</c>; a padrão do banco é <c>default</c>). A padrão aparece <c>default</c> nos dois
    /// lados mesmo com locales diferentes: compare o comportamento, nunca o nome.
    /// </summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>O esquema da colação; nulo onde ela não tem esquema (a padrão do banco; no SQL Server, sempre).</summary>
    public string? Esquema { get => _esquema; init => _esquema = value is null ? null : Lista.Texto(value, nameof(Esquema)); }

    /// <summary>
    /// Se compara byte a byte. <c>false</c> basta para dizer que a colação pode
    /// igualar valores distintos além do que as marcas <see cref="IgnoraCaixa"/>, <see cref="IgnoraAcento"/> e
    /// <see cref="IgnoraPontuacao"/> medem: as três <c>false</c> numa colação não determinística não a tornam
    /// determinística (uma ordenação numérica iguala <c>012</c> a <c>12</c>, e a normalização Unicode iguala formas
    /// compostas e decompostas).
    /// </summary>
    public required bool Deterministica { get; init; }

    /// <summary>
    /// Se a colação tem regras de ordenação do usuário (ICU). <c>true</c> força o pior caso: as regras podem igualar
    /// quaisquer caracteres. <c>false</c> onde o banco não tem o recurso.
    /// </summary>
    public required bool TemRegras { get; init; }

    /// <summary>
    /// Se iguala textos que só diferem na caixa, medido no servidor com textos sintéticos. Nulo quando não foi possível
    /// medir (regras, codificação do banco sem os caracteres da medida, login sem acesso ao esquema da colação): trate
    /// como <c>true</c>. Numa colação determinística, <c>false</c>.
    /// </summary>
    public bool? IgnoraCaixa { get; init; }

    /// <summary>Se iguala textos que só diferem no acento; nulo e determinística como em <see cref="IgnoraCaixa"/>.</summary>
    public bool? IgnoraAcento { get; init; }

    /// <summary>Se iguala textos que só diferem na pontuação; nulo e determinística como em <see cref="IgnoraCaixa"/>.</summary>
    public bool? IgnoraPontuacao { get; init; }
}

/// <summary>A unidade em que o banco mede o comprimento de um texto.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public enum UnidadeComprimento
{
    /// <summary>
    /// Caracteres (pontos de código Unicode; numa codificação que não é Unicode, os caracteres daquela codificação).
    /// </summary>
    PontosDeCodigo,

    /// <summary>Unidades de 16 bits do UTF-16 (os tipos <c>n*</c> do SQL Server); o PostgreSQL não mede assim.</summary>
    UnidadesUtf16,

    /// <summary>Bytes (binário, texto sem codificação declarada, tipos de tamanho fixo do catálogo).</summary>
    Bytes,
}

/// <summary>O comprimento máximo que o tipo da coluna declara, na unidade do banco.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class LimiteComprimento
{
    private readonly int? _valor;
    private readonly UnidadeComprimento _unidade;

    /// <summary>
    /// O limite declarado; nulo se o tipo não declara limite (<c>text</c>, <c>varchar</c> sem tamanho, <c>(max)</c>). Num
    /// array, vale para cada elemento. Converter para <c>ContextoCriacao.ComprimentoMaximo</c> é do <c>planejar</c>, de
    /// forma conservadora.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Zero ou negativo.</exception>
    public required int? Valor
    {
        get => _valor;
        init => _valor = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Valor), "O limite precisa ser positivo.");
    }

    /// <summary>A unidade em que o banco mede o limite.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Valor fora do enum.</exception>
    public required UnidadeComprimento Unidade
    {
        get => _unidade;
        init => _unidade = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Unidade), "Unidade de comprimento desconhecida.");
    }
}

/// <summary>
/// A faixa de um tipo inteiro (não a de um <c>CHECK</c>), saturada nos limites de <see cref="long"/>. Num array, vale
/// para cada elemento.
/// </summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class FaixaInteira
{
    /// <summary>O menor valor que o tipo aceita.</summary>
    public required long Minimo { get; init; }

    /// <summary>O maior valor que o tipo aceita.</summary>
    public required long Maximo { get; init; }
}

/// <summary>A codificação dos textos do banco.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class CodificacaoTexto
{
    private readonly string _nome = null!;
    private readonly int _bytesPorCaractereMaximo;

    /// <summary>O nome, só para mensagem (vem de um conjunto fechado do banco).</summary>
    public required string Nome { get => _nome; init => _nome = Lista.Texto(value, nameof(Nome)); }

    /// <summary>
    /// <c>false</c> quando o banco guarda bytes sem codificação declarada (<c>SQL_ASCII</c> no PostgreSQL): o limite de
    /// comprimento fica em <see cref="UnidadeComprimento.Bytes"/>.
    /// </summary>
    public required bool Definida { get; init; }

    /// <summary>
    /// Se a codificação tem o repertório Unicode completo (UTF-8 ou UTF-16): de uma origem Unicode para um destino sem
    /// ele, o texto copiado como veio pode não caber.
    /// </summary>
    public required bool Unicode { get; init; }

    /// <summary>
    /// Quantos bytes um caractere ocupa no máximo, de 1 a 4. Sem codificação declarada, 4: o Incognito grava UTF-8 cru.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Fora de 1 a 4.</exception>
    public required int BytesPorCaractereMaximo
    {
        get => _bytesPorCaractereMaximo;
        init => _bytesPorCaractereMaximo = value is >= 1 and <= 4 ? value : throw new ArgumentOutOfRangeException(nameof(BytesPorCaractereMaximo), "Bytes por caractere fora de 1 a 4.");
    }
}

/// <summary>O que vale para o banco inteiro: versão do servidor, codificação e colação padrão.</summary>
[Experimental(ContratoExperimental.Diagnostico)]
public sealed class PropriedadesBanco
{
    private readonly Version _versao = null!;
    private readonly CodificacaoTexto _codificacao = null!;
    private readonly ColacaoEsquema _colacao = null!;

    /// <summary>
    /// A versão do servidor até a parte que o dialeto usa (PostgreSQL: principal e secundária; SQL Server: até a
    /// compilação). O <c>planejar</c> compara só a principal, e só entre lados do mesmo dialeto: versões de dialetos
    /// diferentes não se comparam.
    /// </summary>
    public required Version Versao { get => _versao; init => _versao = value ?? throw new ArgumentNullException(nameof(Versao)); }

    /// <summary>A codificação dos textos.</summary>
    public required CodificacaoTexto Codificacao { get => _codificacao; init => _codificacao = value ?? throw new ArgumentNullException(nameof(Codificacao)); }

    /// <summary>A colação padrão do banco.</summary>
    public required ColacaoEsquema Colacao { get => _colacao; init => _colacao = value ?? throw new ArgumentNullException(nameof(Colacao)); }
}
