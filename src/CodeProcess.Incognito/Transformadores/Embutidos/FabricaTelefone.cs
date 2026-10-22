using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>br.telefone@1</c>: telefone com o mesmo DDD e a mesma classe, no domínio padrão <c>telefone</c>; sem parâmetros.
/// A política de DV não se aplica.
/// </summary>
internal sealed class FabricaTelefone() : FabricaEmbutida("br.telefone", "telefone", Injetividade.Garantida, TipoColuna.Texto, TipoColuna.Inteiro)
{
    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirFaixa(contexto, 9_999_999_999_999, "o pseudônimo de telefone tem até 13 dígitos, com o código do país");
        var telefone = Instancia(contexto.Sessao, contexto.Regra);
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, telefone.Pseudonimizar, telefone.Pseudonimizar);
    }

    /// <summary>
    /// A instância de uma regra, pela mesma identidade da coluna: a chave Pix e a chave de DF-e pedem por aqui a do
    /// domínio do documento, e a coluna dele usa a mesma.
    /// </summary>
    internal PseudonimizadorTelefone Instancia(SessaoTransformacao sessao, RegraTransformacao regra) =>
        sessao.Obter(Identidade(regra), (chave, ambiente) => new PseudonimizadorTelefone(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaMalformada));
}
