using System.Collections.Frozen;
using System.Globalization;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>br.chave-dfe@1</c>: chave de acesso de DF-e de 44 posições, sempre no domínio <c>dfe-numero</c> (o do número e
/// do código, comum a todos os DF-e). O emitente vai às instâncias dos domínios citados em <c>dominioCpf</c> e
/// <c>dominioCnpj</c> (padrão: <c>cpf</c> e <c>cnpj</c>), as mesmas das colunas desses documentos; <c>modelos</c>
/// (padrão: os nove) limita os modelos aceitos pela coluna. A política de DV vale para o cDV; o DV do emitente é sempre
/// mantido pelo deslocamento.
/// </summary>
internal sealed class FabricaChaveDfe() : FabricaEmbutida(NomeDoTransformador, PseudonimizadorNumeroDfe.NomeDoDominio, Injetividade.Garantida, TipoColuna.Texto)
{
    internal const string NomeDoTransformador = "br.chave-dfe";

    private const string Modelos = "modelos";
    private const string DominioCpf = "dominioCpf";
    private const string DominioCnpj = "dominioCnpj";

    private static readonly FabricaCpf _cpf = new();
    private static readonly FabricaCnpj _cnpj = new();

    // Em ordem crescente: a forma normalizada da lista.
    private static readonly int[] _osNove = [.. Enum.GetValues<ModeloDfe>().Select(modelo => (int)modelo).Order()];

    private static readonly FrozenDictionary<string, ParametroEmbutido> _parametros = new Dictionary<string, ParametroEmbutido>(StringComparer.Ordinal)
    {
        [Modelos] = new(
            Texto(_osNove),
            $"códigos de modelo em decimal, separados por vírgula e sem repetir, entre {string.Join(", ", _osNove)}",
            valor => TentarLerModelos(valor, out _),
            valor => TentarLerModelos(valor, out var modelos) ? Texto(modelos.Order()) : valor),
        [DominioCpf] = ParametroEmbutido.Dominio(_cpf.DominioPadrao),
        [DominioCnpj] = ParametroEmbutido.Dominio(_cnpj.DominioPadrao),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> _citados = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DominioCpf] = _cpf.Nome,
        [DominioCnpj] = _cnpj.Nome,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    protected override FrozenDictionary<string, ParametroEmbutido> ParametrosAceitos => _parametros;

    protected override bool UsaEntradaInvalida => true;

    private protected override IReadOnlyDictionary<string, string> ParametrosDeDominioEmbutido => _citados;

    protected override void ValidarRegra(RegraTransformacao regra)
    {
        // Os números e códigos de todos os DF-e ficam num domínio só; outro nome cairia no mesmo universo em silêncio.
        if (!string.Equals(regra.Dominio.Nome, DominioPadrao, StringComparison.Ordinal))
        {
            throw new RegraInvalidaException(
                $"O {Nome}@{Versao} fica sempre no domínio {DominioPadrao}; para levar o emitente a outro domínio, use 'dominioCpf' e 'dominioCnpj'.");
        }
    }

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 44, "o pseudônimo de chave de DF-e tem 44 caracteres (mais o prefixo do Id ou os espaços dos grupos, como vieram)");
        var sessao = contexto.Sessao;
        var regra = contexto.Regra;
        var normalizados = Normalizar(regra);

        // O emitente usa a regra do domínio dele, com as políticas do domínio e não as da chave (o DV do emitente é mantido
        // pelo deslocamento): assim a instância é a mesma da coluna do documento. Os dois domínios conferidos antes de
        // criar qualquer instância.
        var deCpf = DominioCitado(sessao, normalizados, DominioCpf, _cpf.Nome);
        var deCnpj = DominioCitado(sessao, normalizados, DominioCnpj, _cnpj.Nome);
        _ = TentarLerModelos(normalizados[Modelos], out var modelos);

        var cpf = _cpf.Instancia(sessao, deCpf);
        var cnpj = _cnpj.Instancia(sessao, deCnpj);
        var chaveDfe = sessao.Obter(Identidade(regra, normalizados), (chave, ambiente) => new PseudonimizadorChaveDfe(
            chave, regra.Dominio.Versao, ambiente, cpf, cnpj, modelos, regra.EntradaInvalida, regra.EntradaMalformada));
        return new TransformadorChaveDfe(chaveDfe);
    }

    // Códigos dos nove modelos em decimal sem zeros à esquerda nem sinal, separados só por vírgula, sem repetir. O
    // NumberStyles.None aceita só dígitos (nem sinal, nem espaço).
    private static bool TentarLerModelos(string valor, out List<int> modelos)
    {
        modelos = [];
        foreach (var parte in valor.Split(','))
        {
            if (parte.StartsWith('0') || !int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var codigo)
                || !_osNove.Contains(codigo) || modelos.Contains(codigo))
            {
                return false;
            }

            modelos.Add(codigo);
        }

        return true;
    }

    private static string Texto(IEnumerable<int> modelos) => string.Join(",", modelos.Select(modelo => modelo.ToString(CultureInfo.InvariantCulture)));
}
