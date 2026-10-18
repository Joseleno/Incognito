using System.Collections.ObjectModel;
using CodeProcess.Incognito.Transformadores.Embutidos;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>As fábricas dos transformadores que vêm com o Incognito, todas na versão 1.</summary>
public static class TransformadoresEmbutidos
{
    /// <summary>
    /// As fábricas embutidas: <c>br.cpf</c>, <c>br.cnpj</c>, <c>br.telefone</c>, <c>email</c>, <c>br.cep</c>,
    /// <c>br.chave-pix</c>, <c>br.chave-dfe</c> e <c>texto.substituir</c>. A lista não muda depois de criada.
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
    ]);
}
