using System.Security.Cryptography;
using System.Text.Json;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// O que uma origem por inversa precisa do caso: chave, domínio, versão, ambiente, opções e, na chave de DF-e, as colunas
/// do emitente.
/// </summary>
internal sealed record ContextoDeOrigem(ChaveIncognito Chave, string Dominio, int Versao, string Ambiente, bool PermutarOrdem, long N)
{
    /// <summary>As colunas de CPF e de CNPJ do emitente, só nos casos da chave de DF-e (os que têm <c>modelos</c>).</summary>
    internal ColunasDoEmitente? Emitente { get; init; }

    internal static ContextoDeOrigem Do(ChaveIncognito chave, int versao, JsonElement caso)
    {
        var contexto = new ContextoDeOrigem(
            chave,
            caso.GetProperty("dominio").GetString()!,
            versao,
            caso.GetProperty("ambiente").GetString()!,
            caso.TryGetProperty("permutarOrdem", out var ordem) && ordem.GetBoolean(),
            caso.TryGetProperty("n", out var n) ? n.GetInt64() : 0);
        if (!caso.TryGetProperty("modelos", out _))
        {
            return contexto;
        }

        var cpf = caso.GetProperty("componentes").GetProperty("cpf");
        var cnpj = caso.GetProperty("componentes").GetProperty("cnpj");
        return contexto with
        {
            Emitente = new(
                (cpf.GetProperty("dominio").GetString()!, cpf.GetProperty("versao").GetInt32()),
                (cnpj.GetProperty("dominio").GetString()!, cnpj.GetProperty("versao").GetInt32(), cnpj.GetProperty("permutarOrdem").GetBoolean())),
        };
    }

    /// <summary>A permutação do domínio, sem exclusões, com a chave derivada como o pseudonimizador deriva.</summary>
    internal PermutacaoFeistel Feistel(string variante, uint radix, ulong a, ulong b) => Feistel(Dominio, Versao, variante, radix, a, b);

    /// <summary>A permutação de outro domínio (o de uma coluna do emitente), com o número de rodadas pedido.</summary>
    internal PermutacaoFeistel Feistel(string dominio, int versao, string variante, uint radix, ulong a, ulong b, int rodadas = PermutacaoFeistel.RodadasPadrao)
    {
        Span<byte> chaveDeDominio = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarChaveDeDominio(Chave, dominio, versao, variante, Ambiente, chaveDeDominio);
            return new PermutacaoFeistel(chaveDeDominio, radix, a, b, rodadas);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeDominio);
        }
    }

    /// <summary>
    /// As 44 posições pseudonimizadas da chave de DF-e com as colunas do emitente do caso, para as origens que partem do
    /// emitente, do número ou do dia já pseudonimizados. As políticas são as do executor; a chave de partida sempre sai
    /// pseudonimizada (o gerador não produz chave vazia nem zerada), e outra coisa é erro de programação.
    /// </summary>
    internal string PseudonimizarChaveDfe(string chave)
    {
        var emitente = Emitente ?? throw new InvalidOperationException("Caso sem as colunas do emitente da chave de DF-e.");
        using var cpf = new PseudonimizadorCpf(
            Chave, emitente.Cpf.Dominio, emitente.Cpf.Versao, Ambiente, ColunasDoEmitente.InvalidaIgnorada, ColunasDoEmitente.MalformadaIgnorada);
        using var cnpj = new PseudonimizadorCnpj(
            Chave, emitente.Cnpj.Dominio, emitente.Cnpj.Versao, Ambiente, emitente.Cnpj.PermutarOrdem, ColunasDoEmitente.InvalidaIgnorada, ColunasDoEmitente.MalformadaIgnorada);
        using var chaveDfe = new PseudonimizadorChaveDfe(
            Chave, Versao, Ambiente, cpf, cnpj, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var desfecho = chaveDfe.Pseudonimizar(chave, out var saida, out _);
        return desfecho is Desfecho.Pseudonimizado or Desfecho.DvInvalidoPreservado or Desfecho.PontoFixo
            ? saida!
            : throw new InvalidOperationException("A chave de partida não saiu pseudonimizada.");
    }
}

/// <summary>Domínio e versão das colunas de CPF e de CNPJ do emitente da chave de DF-e, e a ordem do CNPJ.</summary>
internal sealed record ColunasDoEmitente((string Dominio, int Versao) Cpf, (string Dominio, int Versao, bool PermutarOrdem) Cnpj)
{
    // Dentro da chave, as políticas das colunas não valem (as da chave decidem): estas só completam o construtor.
    internal const PoliticaEntradaInvalida InvalidaIgnorada = PoliticaEntradaInvalida.Preservar;
    internal const PoliticaEntradaMalformada MalformadaIgnorada = PoliticaEntradaMalformada.Falhar;
}
