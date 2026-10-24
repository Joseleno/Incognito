using System.Collections.ObjectModel;
using CodeProcess.Incognito.Transformadores.Embutidos;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>As fábricas dos transformadores que vêm com o Incognito, todas na versão 1.</summary>
public static class TransformadoresEmbutidos
{
    /// <summary>
    /// As fábricas embutidas: <c>br.cpf</c>, <c>br.cnpj</c>, <c>br.telefone</c>, <c>email</c>, <c>br.cep</c>,
    /// <c>br.chave-pix</c>, <c>br.chave-dfe</c>, <c>texto.substituir</c> e <c>id.permutar</c>. A lista não muda depois
    /// de criada. As que aceitam coluna inteira (<c>br.cpf</c>, <c>br.cnpj</c>, <c>br.cep</c>, <c>br.telefone</c> e
    /// <c>id.permutar</c>) exigem <see cref="ContextoCriacao.MaiorInteiro"/> acima do maior pseudônimo; sem ele, o
    /// <c>Criar</c> lança <see cref="RegraInvalidaException"/>. O <c>id.permutar</c> exige também o
    /// <see cref="ContextoCriacao.TamanhoDominio"/>.
    /// </summary>
    public static IReadOnlyList<FabricaTransformador> Fabricas { get; } = new ReadOnlyCollection<FabricaTransformador>(
    [
        new FabricaCpf(),
        new FabricaCnpj(),
        new FabricaTelefone(),
        new FabricaEmail(),
        new FabricaCep(),
        new FabricaChavePix(),
        new FabricaChaveDfe(),
        new FabricaTextoSubstituir(),
        new FabricaIdentificador(),
    ]);
}
