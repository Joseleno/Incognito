// O motor consome o contrato experimental de dialeto que ele mesmo declara: o envoltório é o único tipo que chama os pontos
// de extensão (guarda de arquitetura), e nada dele é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>O lado de um banco no plano.</summary>
internal enum LadoBanco
{
    /// <summary>A origem, só lida.</summary>
    Origem,

    /// <summary>O destino, esvaziado e gravado.</summary>
    Destino,
}

/// <summary>
/// Um banco conectado, chamado só por aqui: toda chamada ao dialeto (conectar, ler, citar, descartar) fica dentro de um
/// <c>try</c>, e o que o dialeto lança sai traduzido, nunca a exceção original.
/// </summary>
/// <remarks>
/// <para>
/// A tradução: cancelamento pedido pelo chamador sai como cancelamento novo, sem a interna (o driver põe ali o erro do
/// servidor); a recusa do dialeto (<see cref="DialetoRecusouException"/> com código da faixa) vira erro do plano, saída 2; a
/// <see cref="IncognitoException"/> de tipo exato com código da faixa vira falha do dialeto com o código e a mensagem
/// escapada e cortada; uma subclasse, só com o código (a mensagem é virtual e rodaria código do dialeto); e qualquer outra,
/// inclusive código fora da faixa, vira falha <c>INC3001</c> só com o tipo, a operação e o lado.
/// </para>
/// <para>
/// Da exceção original nunca se lê <see cref="Exception.Data"/>, <see cref="Exception.StackTrace"/>,
/// <see cref="Exception.InnerException"/> nem o <c>ToString</c>: as do driver trazem a string de conexão, o comando ou valores.
/// O nome do provedor nas mensagens é o da configuração, nunca o que o dialeto diz.
/// </para>
/// </remarks>
internal sealed class DialetoProtegido : IAsyncDisposable
{
    private readonly DialetoBanco _banco;
    private readonly string _provedor;
    private readonly LadoBanco _lado;
    private int _descartado;

    private DialetoProtegido(DialetoBanco banco, string provedor, LadoBanco lado)
    {
        _banco = banco;
        _provedor = provedor;
        _lado = lado;
    }

    /// <summary>Se o dialeto se chama como o provedor da configuração.</summary>
    /// <param name="dialeto">O dialeto registrado para o provedor.</param>
    /// <param name="provedor">O nome do provedor na configuração (<c>postgresql</c>).</param>
    /// <returns>Se o nome confere.</returns>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao dizer o nome.</exception>
    internal static bool ChamaSe(ProvedorDialeto dialeto, string provedor)
    {
        ArgumentNullException.ThrowIfNull(dialeto);
        try
        {
            return string.Equals(dialeto.Nome, provedor, StringComparison.Ordinal);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, provedor, "dizer o nome", lado: null, aceitaRecusa: false, CancellationToken.None);
        }
    }

    /// <summary>Conecta ao banco do lado pedido.</summary>
    /// <param name="dialeto">O dialeto do provedor.</param>
    /// <param name="provedor">O nome do provedor na configuração.</param>
    /// <param name="lado">Origem ou destino.</param>
    /// <param name="conexao">A string de conexão resolvida.</param>
    /// <param name="cancelamento">Cancela a conexão.</param>
    /// <returns>O banco conectado, que o chamador descarta.</returns>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a conexão.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static async Task<DialetoProtegido> ConectarAsync(
        ProvedorDialeto dialeto, string provedor, LadoBanco lado, SegredoConexao conexao, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dialeto);
        ArgumentNullException.ThrowIfNull(conexao);
        DialetoBanco? banco;
        try
        {
            banco = lado == LadoBanco.Origem
                ? await dialeto.ConectarOrigemAsync(conexao, cancelamento).ConfigureAwait(false)
                : await dialeto.ConectarDestinoAsync(conexao, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, provedor, "conectar", lado, aceitaRecusa: true, cancelamento);
        }

        return banco is null
            ? throw SemResultado(provedor, "conectar", lado)
            : new DialetoProtegido(banco, provedor, lado);
    }

    /// <summary>Lê o esquema.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O esquema lido.</returns>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a leitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<EsquemaBanco> LerEsquemaAsync(CancellationToken cancelamento)
    {
        const string Operacao = "ler o esquema";
        EsquemaBanco? esquema;
        try
        {
            esquema = await _banco.Esquema.LerAsync(cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: true, cancelamento);
        }

        return esquema ?? throw SemResultado(_provedor, Operacao, _lado);
    }

    /// <summary>Lê a identidade.</summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A identidade lida.</returns>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a leitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<IdentidadeLida> LerIdentidadeAsync(CancellationToken cancelamento)
    {
        const string Operacao = "ler a identidade";
        IdentidadeLida? identidade;
        try
        {
            identidade = await _banco.Identidade.LerAsync(cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: true, cancelamento);
        }

        return identidade ?? throw SemResultado(_provedor, Operacao, _lado);
    }

    /// <summary>Cita um identificador na sintaxe do banco.</summary>
    /// <param name="identificador">O identificador, como o catálogo o guarda.</param>
    /// <returns>O identificador citado.</returns>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    internal string Citar(string identificador) => Citar(sql => sql.Citar(identificador));

    /// <summary>Cita o esquema e o nome.</summary>
    /// <param name="nome">O nome do objeto.</param>
    /// <returns>O nome citado.</returns>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    internal string Citar(NomeObjeto nome) => Citar(sql => sql.Citar(nome));

    /// <summary>
    /// Fecha a conexão: só a primeira chamada descarta. Qualquer falha, inclusive uma recusa ou uma
    /// <see cref="IncognitoException"/> do dialeto, sai como falha do dialeto só com o tipo; quem chama decide se ela toma
    /// o lugar de um erro anterior (nunca deve).
    /// </summary>
    /// <returns>A tarefa do descarte.</returns>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ao fechar.</exception>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _descartado, 1) != 0)
        {
            return;
        }

        try
        {
            await _banco.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw FalhaSemMensagem(_provedor, "fechar a conexão", _lado, excecao);
        }
    }

    /// <summary>A exceção que sai no lugar da que o dialeto lançou.</summary>
    /// <param name="excecao">A exceção do dialeto, que não sai daqui.</param>
    /// <param name="provedor">O nome do provedor na configuração.</param>
    /// <param name="operacao">O que o motor pedia, no infinitivo.</param>
    /// <param name="lado">O lado, ou nulo quando a chamada não é de um lado.</param>
    /// <param name="aceitaRecusa">
    /// Se a operação pode recusar a configuração ou o ambiente (conectar e ler); nas outras, a recusa vira falha do dialeto.
    /// </param>
    /// <param name="cancelamento">O token do chamador.</param>
    /// <returns>A exceção a lançar.</returns>
    internal static Exception Traduzir(Exception excecao, string provedor, string operacao, LadoBanco? lado, bool aceitaRecusa, CancellationToken cancelamento)
    {
        // Pelo pedido do chamador, e não pela igualdade do token: um dialeto pode cancelar por um token vinculado.
        if (cancelamento.IsCancellationRequested && SoCancelamento(excecao))
        {
            return new OperationCanceledException(cancelamento);
        }

        if (excecao is not IncognitoException incognito)
        {
            return FalhaSemMensagem(provedor, operacao, lado, excecao);
        }

        // Lidos uma vez só: o código não é virtual, e a mensagem só é lida nos tipos em que não roda código do dialeto.
        var codigo = incognito.Codigo;
        if (!CodigosPlanejar.DaFaixaDosDialetos(codigo))
        {
            return FalhaSemMensagem(provedor, operacao, lado, excecao);
        }

        if (excecao is DialetoRecusouException recusa)
        {
            var mensagem = TextoSeguro.Mensagem(recusa.Message);
            return aceitaRecusa ? new PlanoRecusadoException([new ErroPlano(codigo, mensagem)]) : new FalhaDialetoException(codigo, mensagem);
        }

        return excecao.GetType() == typeof(IncognitoException)
            ? new FalhaDialetoException(codigo, TextoSeguro.Mensagem(excecao.Message))
            : new FalhaDialetoException(codigo, $"O dialeto de {provedor} falhou ao {operacao}{NoLado(lado)} com o código {codigo}.");
    }

    private string Citar(Func<DialetoSql, string> citar)
    {
        string? citado;
        try
        {
            citado = citar(_banco.Sql);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, _provedor, "citar um nome", _lado, aceitaRecusa: false, CancellationToken.None);
        }

        return citado ?? throw SemResultado(_provedor, "citar um nome", _lado);
    }

    // Percorre as internas à mão: o Flatten lê a Message, que é virtual, e rodaria código de uma agregada do dialeto.
    private static bool SoCancelamento(Exception excecao)
    {
        var pendentes = new Stack<Exception>([excecao]);
        var alguma = false;
        while (pendentes.TryPop(out var atual))
        {
            if (atual is AggregateException agregada)
            {
                foreach (var interna in agregada.InnerExceptions)
                {
                    pendentes.Push(interna);
                }
            }
            else if (atual is OperationCanceledException)
            {
                alguma = true;
            }
            else
            {
                return false;
            }
        }

        return alguma;
    }

    private static FalhaDialetoException FalhaSemMensagem(string provedor, string operacao, LadoBanco? lado, Exception excecao) => new(
        CodigosPlanejar.FalhaDoDialeto,
        $"O dialeto de {provedor} falhou ao {operacao}{NoLado(lado)} ({TextoSeguro.Nome(excecao.GetType().FullName ?? excecao.GetType().Name)}).");

    private static FalhaDialetoException SemResultado(string provedor, string operacao, LadoBanco lado) => new(
        CodigosPlanejar.FalhaDoDialeto,
        $"O dialeto de {provedor} não devolveu nada ao {operacao}{NoLado(lado)}.");

    private static string NoLado(LadoBanco? lado) => lado switch
    {
        LadoBanco.Origem => " na origem",
        LadoBanco.Destino => " no destino",
        _ => "",
    };
}
