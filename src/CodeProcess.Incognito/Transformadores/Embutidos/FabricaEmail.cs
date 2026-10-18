using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>email@1</c>: endereço em <c>example.com</c>, sempre com 32 caracteres, no domínio padrão <c>email</c>; sem
/// parâmetros. A política de DV não se aplica.
/// </summary>
internal sealed class FabricaEmail() : FabricaEmbutida("email", "email", Injetividade.Probabilistica, TipoColuna.Texto)
{
    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 32, "o pseudônimo de e-mail tem 32 caracteres");
        var regra = contexto.Regra;
        var email = contexto.Sessao.Obter(Identidade(contexto), (chave, ambiente) => new PseudonimizadorEmail(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaMalformada));
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, email.Pseudonimizar, null);
    }
}
