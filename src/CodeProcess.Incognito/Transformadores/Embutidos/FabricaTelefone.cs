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
        var regra = contexto.Regra;
        var telefone = contexto.Sessao.Obter(Identidade(contexto), (chave, ambiente) => new PseudonimizadorTelefone(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaMalformada));
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, telefone.Pseudonimizar, telefone.Pseudonimizar);
    }
}
