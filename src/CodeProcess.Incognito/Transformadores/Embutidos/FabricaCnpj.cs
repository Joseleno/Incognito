using System.Collections.Frozen;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>br.cnpj@1</c>: CNPJ com a raiz pseudonimizada, no domínio padrão <c>cnpj</c>. Parâmetro <c>permutarOrdem</c>
/// (padrão <c>false</c>), que muda o universo de pseudônimos.
/// </summary>
internal sealed class FabricaCnpj() : FabricaEmbutida("br.cnpj", "cnpj", Injetividade.Garantida, TipoColuna.Texto, TipoColuna.Inteiro)
{
    private const string PermutarOrdem = "permutarOrdem";

    private static readonly FrozenDictionary<string, ParametroEmbutido> _parametros =
        new Dictionary<string, ParametroEmbutido>(StringComparer.Ordinal) { [PermutarOrdem] = ParametroEmbutido.Logico(padrao: false) }
            .ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> _identidade = new[] { PermutarOrdem }.ToFrozenSet(StringComparer.Ordinal);

    public override IReadOnlySet<string> ParametrosDeIdentidade => _identidade;

    protected override FrozenDictionary<string, ParametroEmbutido> ParametrosAceitos => _parametros;

    protected override bool UsaEntradaInvalida => true;

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 14, "o pseudônimo de CNPJ tem 14 caracteres (18 com a máscara)");
        ExigirFaixa(contexto, 99_999_999_999_999, "o pseudônimo de CNPJ tem até 14 dígitos");
        var cnpj = Instancia(contexto.Sessao, contexto.Regra);
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, cnpj.Pseudonimizar, cnpj.Pseudonimizar);
    }

    /// <summary>
    /// A instância de uma regra, pela mesma identidade da coluna: a chave Pix e a chave de DF-e pedem por aqui a do
    /// domínio do documento, e a coluna dele usa a mesma.
    /// </summary>
    internal PseudonimizadorCnpj Instancia(SessaoTransformacao sessao, RegraTransformacao regra)
    {
        var normalizados = Normalizar(regra);
        var permutarOrdem = normalizados[PermutarOrdem] == "true";
        return sessao.Obter(Identidade(regra, normalizados), (chave, ambiente) => new PseudonimizadorCnpj(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, permutarOrdem, regra.EntradaInvalida, regra.EntradaMalformada));
    }
}
