using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>br.cep@1</c>: CEP na faixa da mesma UF, derivado da entidade, no domínio padrão <c>cep</c>; sem parâmetros e
/// com <c>derivarDe</c> obrigatório, apontando uma coluna de texto ou inteira.
/// </summary>
internal sealed class FabricaCep() : FabricaEmbutida(NomeDoTransformador, "cep", Injetividade.Nenhuma, TipoColuna.Texto, TipoColuna.Inteiro)
{
    internal const string NomeDoTransformador = "br.cep";

    protected override UsoDoDerivarDe DerivarDe => UsoDoDerivarDe.Exigido;

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        if (contexto.TipoReferencia is not { } tipoReferencia || tipoReferencia is not (TipoColuna.Texto or TipoColuna.Inteiro))
        {
            throw new RegraInvalidaException($"O {Nome}@{Versao} deriva o CEP de uma coluna de texto ou inteira ('derivarDe').");
        }

        ExigirComprimento(contexto, 8, "o pseudônimo de CEP tem 8 caracteres (9 ou 10 com a máscara)");
        var regra = contexto.Regra;
        var cep = contexto.Sessao.Obter(Identidade(regra), (chave, ambiente) => new PseudonimizadorCep(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, regra.EntradaMalformada));
        return new TransformadorCep(cep, contexto.Tipo, tipoReferencia);
    }
}
