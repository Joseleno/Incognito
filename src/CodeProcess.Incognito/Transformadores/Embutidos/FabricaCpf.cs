using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary><c>br.cpf@1</c>: CPF válido na mesma forma da entrada, no domínio padrão <c>cpf</c>; sem parâmetros.</summary>
internal sealed class FabricaCpf() : FabricaEmbutida("br.cpf", "cpf", Injetividade.Garantida, TipoColuna.Texto, TipoColuna.Inteiro)
{
    protected override bool UsaEntradaInvalida => true;

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 11, "o pseudônimo de CPF tem 11 caracteres (14 com a máscara)");
        var regra = contexto.Regra;
        var cpf = contexto.Sessao.Obter(Identidade(contexto), (chave, ambiente) => new PseudonimizadorCpf(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaInvalida, regra.EntradaMalformada));
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, cpf.Pseudonimizar, cpf.Pseudonimizar);
    }
}
