using System.Diagnostics.CodeAnalysis;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O contexto de criação de uma coluna, como o <c>planejar</c> o montou a partir da coluna do destino: o executor recria o
/// transformador com ele e a chave real. Nunca guarda a sessão nem a regra.
/// </summary>
/// <param name="Tipo">O tipo do destino.</param>
/// <param name="ComprimentoMaximo">O comprimento já convertido (em texto, unidades UTF-16; em binário, bytes); nulo sem limite.</param>
/// <param name="MenorInteiro">O menor valor da faixa, numa coluna inteira.</param>
/// <param name="MaiorInteiro">O maior valor da faixa, numa coluna inteira.</param>
/// <param name="Precisao">A precisão, numa coluna decimal.</param>
/// <param name="Escala">A escala, numa coluna decimal.</param>
/// <param name="Colacao">O nome da colação do destino.</param>
/// <param name="TipoReferencia">O tipo da coluna de <c>derivarDe</c> na origem.</param>
/// <param name="TamanhoDominio">O N do domínio, só numa coluna do <c>id.permutar</c> embutido; o maior valor lido nunca entra.</param>
internal sealed record ContextoDaColuna(
    TipoColuna Tipo, int? ComprimentoMaximo, long? MenorInteiro, long? MaiorInteiro, int? Precisao, int? Escala, string? Colacao, TipoColuna? TipoReferencia, long? TamanhoDominio = null)
{
    /// <summary>Um contexto novo, para uma criação só (um contexto serve a uma fábrica).</summary>
    /// <param name="regra">A regra conferida.</param>
    /// <param name="sessao">A sessão da criação.</param>
    /// <returns>O contexto.</returns>
    /// <exception cref="InvalidOperationException">O contexto se contradiz: defeito do motor, nunca da fábrica.</exception>
    internal ContextoCriacao Novo(RegraTransformacao regra, SessaoTransformacao sessao)
    {
        // Conferido aqui, antes de qualquer fábrica: a recusa do núcleo para o contexto contraditório cairia na conta dela.
        if (MenorInteiro >= MaiorInteiro || (Escala is not null && Precisao is null) || Precisao <= 0 || ComprimentoMaximo <= 0 || TamanhoDominio <= 0)
        {
            throw new InvalidOperationException("O planejar montou um contexto de criação que se contradiz.");
        }

        return new(regra, Tipo, sessao)
        {
            ComprimentoMaximo = ComprimentoMaximo,
            MenorInteiro = MenorInteiro,
            MaiorInteiro = MaiorInteiro,
            Precisao = Precisao,
            Escala = Escala,
            Colacao = Colacao,
            TipoReferencia = TipoReferencia,
            TamanhoDominio = TamanhoDominio,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ContextoDaColuna)} ({Tipo})";
}

/// <summary>
/// A criação de validação do <c>planejar</c>: uma sessão descartável, com chave aleatória e a tabela de domínios, em que
/// cada coluna tratada tem o transformador criado, lido e descartado. O executor recria tudo com a chave real. O que uma
/// fábrica de fora lança vira erro do plano só com o tipo; a recusa (<see cref="RegraInvalidaException"/>) sai com a
/// mensagem escapada; o cancelamento pedido e a falta de memória sobem novos, sem a mensagem nem a interna de fora; e a
/// exceção de uma embutida é defeito e sobe.
/// </summary>
internal sealed class CriacaoNoPlano : IDisposable
{
    private readonly ChaveIncognito _chave;
    private readonly SessaoTransformacao _sessao;
    private readonly CancellationToken _cancelamento;

    /// <summary>Abre a sessão descartável.</summary>
    /// <param name="gerarChave">O gerador da chave aleatória (nos testes, uma chave canário).</param>
    /// <param name="ambiente">O ambiente da configuração.</param>
    /// <param name="dominios">A tabela de domínios: sem ela, a chave Pix e a de DF-e recusariam a regra.</param>
    /// <param name="cancelamento">Cancela o planejamento.</param>
    internal CriacaoNoPlano(Func<ChaveIncognito> gerarChave, string ambiente, IReadOnlyList<DefinicaoDominio> dominios, CancellationToken cancelamento)
    {
        _chave = gerarChave() ?? throw new InvalidOperationException("O gerador de chave não devolveu a chave.");
        try
        {
            _sessao = new SessaoTransformacao(_chave, ambiente) { Dominios = dominios };
        }
        catch
        {
            _chave.Dispose();
            throw;
        }

        _cancelamento = cancelamento;
    }

    /// <summary>Cria, lê e descarta o transformador de uma coluna.</summary>
    /// <param name="fabrica">A fábrica registrada.</param>
    /// <param name="regra">A regra conferida.</param>
    /// <param name="contexto">O contexto da coluna.</param>
    /// <returns>O código e a mensagem do erro; nulo quando o transformador foi criado e descartado.</returns>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    /// <exception cref="OutOfMemoryException">Falta de memória numa fábrica de fora, sem a mensagem dela.</exception>
    internal (string Codigo, string Mensagem)? Criar(FabricaRegistrada fabrica, RegraTransformacao regra, ContextoDaColuna contexto)
    {
        _cancelamento.ThrowIfCancellationRequested();
        var deFora = !fabrica.Embutida;
        var novo = contexto.Novo(regra, _sessao);
        Transformador criado;
        try
        {
            criado = fabrica.Criar(novo);
        }
        catch (RegraInvalidaException recusa)
        {
            return (CodigosPlanejar.CriacaoRecusada, $"O transformador {fabrica.Nome}@{fabrica.Versao} recusou a coluna: {TextoSeguro.Mensagem(recusa.Message)}");
        }
        catch (OperationCanceledException) when (_cancelamento.IsCancellationRequested)
        {
            throw new OperationCanceledException(_cancelamento);
        }
        catch (OutOfMemoryException) when (deFora)
        {
            throw SemMensagem();
        }
        catch (Exception excecao) when (deFora)
        {
            return Falha(fabrica, "criar", excecao);
        }

        // Descartado uma vez, aqui: o planejar só precisa saber que a criação vinga.
        try
        {
            (criado as IDisposable)?.Dispose();
        }
        catch (OperationCanceledException) when (_cancelamento.IsCancellationRequested)
        {
            throw new OperationCanceledException(_cancelamento);
        }
        catch (OutOfMemoryException) when (deFora)
        {
            throw SemMensagem();
        }
        catch (Exception excecao) when (deFora)
        {
            return Falha(fabrica, "descartar", excecao);
        }

        return null;
    }

    /// <summary>Descarta a sessão e depois a chave, que é zerada.</summary>
    public void Dispose()
    {
        try
        {
            _sessao.Dispose();
        }
        finally
        {
            _chave.Dispose();
        }
    }

    private static (string Codigo, string Mensagem) Falha(FabricaRegistrada fabrica, string operacao, Exception excecao) =>
        (CodigosPlanejar.FalhaDaFabrica, $"A fábrica {fabrica.Nome}@{fabrica.Versao} falhou ao {operacao} o transformador da coluna ({TextoSeguro.Mensagem(excecao.GetType().Name)}): corrija a fábrica ou fale com quem a mantém.");

    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Relança a falta de memória de uma fábrica de fora sem a mensagem nem a interna dela.")]
    private static OutOfMemoryException SemMensagem() => new();
}
