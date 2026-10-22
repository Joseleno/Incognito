using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary><c>br.cpf@1</c>: CPF válido na mesma forma da entrada, no domínio padrão <c>cpf</c>; sem parâmetros.</summary>
internal sealed class FabricaCpf() : FabricaEmbutida("br.cpf", "cpf", Injetividade.Garantida, TipoColuna.Texto, TipoColuna.Inteiro)
{
    protected override bool UsaEntradaInvalida => true;

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 11, "o pseudônimo de CPF tem 11 caracteres (14 com a máscara)");
        ExigirFaixa(contexto, 99_999_999_999, "o pseudônimo de CPF tem até 11 dígitos");
        var cpf = Instancia(contexto.Sessao, contexto.Regra);
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, cpf.Pseudonimizar, cpf.Pseudonimizar);
    }

    /// <summary>
    /// A instância de uma regra, pela mesma identidade da coluna: a chave Pix e a chave de DF-e pedem por aqui a do
    /// domínio do documento, e a coluna dele usa a mesma.
    /// </summary>
    internal PseudonimizadorCpf Instancia(SessaoTransformacao sessao, RegraTransformacao regra) =>
        sessao.Obter(Identidade(regra), (chave, ambiente) => new PseudonimizadorCpf(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaInvalida, regra.EntradaMalformada));
}
