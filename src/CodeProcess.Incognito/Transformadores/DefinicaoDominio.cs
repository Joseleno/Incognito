using CodeProcess.Incognito.Criptografia;

namespace CodeProcess.Incognito.Transformadores;

/// <summary>
/// Um domínio da configuração, para as fábricas que usam outros domínios (a chave Pix e a chave de DF-e): o
/// transformador e a regra do domínio, com os parâmetros e as políticas do próprio domínio (as declaradas nele, ou as
/// globais), nunca as de uma coluna.
/// </summary>
/// <remarks>Imutável. Nada aqui é dado de coluna.</remarks>
public sealed class DefinicaoDominio
{
    /// <summary>Cria a definição.</summary>
    /// <param name="transformador">
    /// O nome do transformador do domínio (<c>br.cpf</c>), sem a versão, que é a do domínio.
    /// </param>
    /// <param name="regra">A regra do domínio: nome e versão, parâmetros e políticas.</param>
    /// <exception cref="ArgumentNullException">Transformador ou regra nulos.</exception>
    /// <exception cref="ArgumentException">Transformador fora da regra dos nomes; a mensagem não traz o valor.</exception>
    public DefinicaoDominio(string transformador, RegraTransformacao regra)
    {
        Subchaves.ValidarNome(transformador, nameof(transformador));
        ArgumentNullException.ThrowIfNull(regra);
        Transformador = transformador;
        Regra = regra;
    }

    /// <summary>O nome do domínio.</summary>
    public string Nome => Regra.Dominio.Nome;

    /// <summary>O nome do transformador do domínio.</summary>
    public string Transformador { get; }

    /// <summary>A regra do domínio.</summary>
    public RegraTransformacao Regra { get; }
}
