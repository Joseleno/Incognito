// O envoltório da sessão de leitura chama o contrato experimental de dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

internal sealed partial class DialetoProtegido
{
    /// <summary>
    /// Abre a sessão de leitura consistente. A recusa (o modo exigir que o banco não cumpre, a travada que não existe ou que
    /// o login não lê) vira erro do plano, saída 2, com a mensagem escapada (só nomes do catálogo pedidos); o resto é falha.
    /// O que o dialeto devolve é conferido (a consistência conhecida, a que o modo pede, um leitor por conexão pedida), e a
    /// sessão incoerente é descartada antes da falha.
    /// </summary>
    /// <param name="pedido">Os leitores, o modo e as travadas.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A sessão protegida, que o chamador descarta.</returns>
    /// <exception cref="InvalidOperationException">Travadas no destino (defeito do motor).</exception>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a abertura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou devolveu uma sessão incoerente.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<SessaoProtegida> AbrirLeituraAsync(PedidoLeitura pedido, CancellationToken cancelamento)
    {
        const string Operacao = "abrir a leitura";
        ArgumentNullException.ThrowIfNull(pedido);
        if (_lado == LadoBanco.Destino && pedido.Travadas.Count > 0)
        {
            throw new InvalidOperationException("A sessão de leitura do destino não trava tabelas.");
        }

        SessaoLeitura? sessao;
        try
        {
            sessao = await _banco.AbrirLeituraAsync(pedido, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: true, cancelamento);
        }

        if (sessao is null)
        {
            throw SemResultado(_provedor, Operacao, _lado);
        }

        ConsistenciaObtida obtida;
        try
        {
            obtida = sessao.Obtida;
        }
        catch (Exception excecao)
        {
            await sessao.DisposeAsync().ConfigureAwait(false);
            throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: false, cancelamento);
        }

        // Os leitores são fixos, distintos e não nulos pela base; a quantidade e a consistência, só o dialeto garante.
        var coerente = obtida is ConsistenciaObtida.Instantaneo or ConsistenciaObtida.PorTabela
            && (pedido.Modo != ModoConsistencia.Exigir || obtida == ConsistenciaObtida.Instantaneo)
            && sessao.Leitores.Count == pedido.Leitores;
        if (!coerente)
        {
            // O descarte da sessão é da base: uma vez só, e nunca lança.
            await sessao.DisposeAsync().ConfigureAwait(false);
            throw Incoerente(Operacao);
        }

        return new SessaoProtegida(sessao, obtida, _provedor, _lado);
    }

    /// <summary>
    /// Uma sessão de leitura, chamada só por aqui. A releitura do esquema traduz como a leitura do esquema (a recusa é erro
    /// do plano); a idade, como falha; os leitores leem coluna, e qualquer exceção deles sai só com o código ou o tipo. O
    /// descarte é o da base: fecha uma vez e nunca lança.
    /// </summary>
    internal sealed class SessaoProtegida : IAsyncDisposable
    {
        private readonly SessaoLeitura _sessao;
        private readonly string _provedor;
        private readonly LadoBanco _lado;

        internal SessaoProtegida(SessaoLeitura sessao, ConsistenciaObtida obtida, string provedor, LadoBanco lado)
        {
            _sessao = sessao;
            _provedor = provedor;
            _lado = lado;
            Obtida = obtida;
            Leitores = [.. sessao.Leitores.Select(leitor => new LeitorProtegido(leitor, provedor, lado))];
        }

        /// <summary>A consistência obtida, lida e conferida na abertura.</summary>
        internal ConsistenciaObtida Obtida { get; }

        /// <summary>Os leitores protegidos, um por conexão.</summary>
        internal IReadOnlyList<LeitorProtegido> Leitores { get; }

        /// <summary>Relê o esquema no instante da sessão.</summary>
        /// <param name="cancelamento">Cancela a leitura.</param>
        /// <returns>O esquema relido.</returns>
        /// <exception cref="PlanoRecusadoException">O dialeto recusou a leitura.</exception>
        /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<EsquemaBanco> LerEsquemaAsync(CancellationToken cancelamento)
        {
            const string Operacao = "reler o esquema na leitura";
            EsquemaBanco? esquema;
            try
            {
                esquema = await _sessao.LerEsquemaAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: true, cancelamento);
            }

            return esquema ?? throw SemResultado(_provedor, Operacao, _lado);
        }

        /// <summary>A idade do instante da sessão.</summary>
        /// <param name="cancelamento">Cancela a leitura.</param>
        /// <returns>A idade, em transações; nula quando o banco não a mede.</returns>
        /// <exception cref="FalhaDialetoException">O dialeto falhou, ou devolveu uma idade negativa.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<long?> LerIdadeAsync(CancellationToken cancelamento)
        {
            const string Operacao = "ler a idade da leitura";
            long? idade;
            try
            {
                idade = await _sessao.LerIdadeAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: false, cancelamento);
            }

            return idade is null or >= 0
                ? idade
                : throw new FalhaDialetoException(CodigosPlanejar.EsquemaIncoerente, $"O dialeto de {_provedor} devolveu um resultado que se contradiz ao {Operacao}{NoLado(_lado)}.");
        }

        /// <summary>Fecha a sessão, uma vez só; nunca lança (a base garante as duas).</summary>
        /// <returns>A tarefa do descarte.</returns>
        public ValueTask DisposeAsync() => _sessao.DisposeAsync();
    }

    /// <summary>
    /// Um leitor da sessão, chamado só por aqui. Lê coluna (também no destino, que pode guardar dado real): qualquer exceção
    /// sai só com o código ou o tipo, e a recusa é falha (saída 1), porque a sessão só existe na execução, depois do plano. O
    /// mesmo leitor nunca é chamado em paralelo consigo mesmo: o motor que o fizesse tem defeito.
    /// </summary>
    internal sealed class LeitorProtegido
    {
        private readonly LeitorDaSessao _leitor;
        private readonly string _provedor;
        private readonly LadoBanco _lado;
        private int _ocupado;

        internal LeitorProtegido(LeitorDaSessao leitor, string provedor, LadoBanco lado)
        {
            _leitor = leitor;
            _provedor = provedor;
            _lado = lado;
        }

        /// <summary>O maior valor de uma coluna inteira, no instante da sessão.</summary>
        /// <param name="coluna">A tabela e a coluna, como o catálogo do lado as guarda.</param>
        /// <param name="cancelamento">Cancela a leitura.</param>
        /// <returns>O maior valor; nulo quando nenhuma linha tem valor na coluna.</returns>
        /// <exception cref="InvalidOperationException">O leitor já está numa leitura (defeito do motor).</exception>
        /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou a leitura.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
        {
            ArgumentNullException.ThrowIfNull(coluna);
            if (Interlocked.Exchange(ref _ocupado, 1) != 0)
            {
                throw new InvalidOperationException("O leitor da sessão já está numa leitura.");
            }

            try
            {
                return await _leitor.LerMaiorValorAsync(coluna, cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "ler o maior valor de uma coluna na leitura", _lado, cancelamento);
            }
            finally
            {
                Volatile.Write(ref _ocupado, 0);
            }
        }
    }
}
