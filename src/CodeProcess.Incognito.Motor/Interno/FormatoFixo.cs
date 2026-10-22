using System.Collections.Frozen;
using CodeProcess.Incognito.Configuracao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// As embutidas cuja saída continua distinta numa colação de comparação numérica (<c>kn</c>) com as três marcas falsas,
/// que iguala dois textos quando eles só diferem nos zeros à esquerda de um trecho de dígitos: o pseudônimo é função do
/// valor numérico da entrada, com os trechos de dígitos em comprimento e posição fixos e sem encostar numa letra, ou o
/// alfabeto da saída nunca começa um trecho de dígitos por <c>0</c>. Só relaxa: numa colação não determinística medida
/// com as três marcas falsas, o transformador listado vale com a injetividade caindo para
/// <see cref="Transformadores.Injetividade.Probabilistica"/>; o resto é recusado. Presa por um teste de propriedade sobre o
/// modelo da igualdade <c>kn</c> e por um fato contra o servidor.
/// </summary>
/// <remarks>
/// Ficam fora o <c>br.cnpj@1</c> (a raiz alfanumérica põe letra ao lado de dígito), a <c>br.chave-dfe@1</c> (o emitente
/// com CNPJ alfanumérico faz o mesmo dentro da chave), a <c>br.chave-pix@1</c> (a chave aleatória é hexadecimal, com
/// <c>0</c> ao lado de letra) e o <c>br.telefone@1</c> (o número de serviço volta como veio, com o zero à esquerda, e
/// <c>0900123456</c> se iguala ao pseudônimo <c>900123456</c> de um celular sem DDD).
/// </remarks>
internal static class FormatoFixo
{
    /// <summary>Os transformadores da tabela, por <c>nome@versao</c>.</summary>
    internal static readonly FrozenSet<string> Transformadores =
        new[] { "br.cpf@1", "email@1" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Se a fábrica é uma embutida da tabela.</summary>
    /// <param name="fabrica">A fábrica registrada.</param>
    /// <returns>Se está na tabela.</returns>
    internal static bool Contem(FabricaRegistrada fabrica) => fabrica.Embutida && Transformadores.Contains($"{fabrica.Nome}@{fabrica.Versao}");
}
