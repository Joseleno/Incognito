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
        var regra = contexto.Regra;
        var normalizados = Normalizar(regra);
        var permutarOrdem = normalizados[PermutarOrdem] == "true";
        var cnpj = contexto.Sessao.Obter(Identidade(contexto, normalizados), (chave, ambiente) => new PseudonimizadorCnpj(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, permutarOrdem, regra.EntradaInvalida, regra.EntradaMalformada));
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, cnpj.Pseudonimizar, cnpj.Pseudonimizar);
    }
}
