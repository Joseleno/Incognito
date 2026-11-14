// O envoltório da cópia chama o contrato experimental de dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using System.Diagnostics;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// A cópia pelo envoltório: a leitura das linhas de uma tabela num leitor da sessão, as chamadas por célula do lote e a
/// gravação no destino. Tudo toca dado de coluna: qualquer exceção sai só com o código ou o tipo
/// (<see cref="TraduzirSemMensagem"/>), e a recusa é falha (saída 1), porque a cópia só existe na execução.
/// </summary>
internal sealed partial class DialetoProtegido
{
    /// <summary>Esvazia uma tabela do destino (a unidade só esvaziada), numa conexão própria do gravador.</summary>
    /// <param name="tabela">A tabela, como o catálogo do destino a guarda.</param>
    /// <param name="cancelamento">Cancela o esvaziamento.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">Chamado na origem (defeito do motor).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task EsvaziarAsync(NomeObjeto tabela, CancellationToken cancelamento)
    {
        const string Operacao = "esvaziar uma tabela";
        var gravador = Gravador(Operacao);
        try
        {
            await gravador.EsvaziarAsync(tabela, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
        }
    }

    /// <summary>Abre a gravação de uma tabela do destino, numa conexão própria do gravador.</summary>
    /// <param name="pedido">A tabela, as colunas e as tratadas.</param>
    /// <param name="cancelamento">Cancela a abertura.</param>
    /// <returns>A gravação protegida, que o chamador descarta.</returns>
    /// <exception cref="InvalidOperationException">Chamado na origem (defeito do motor).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<GravacaoProtegida> AbrirGravacaoAsync(PedidoGravacao pedido, CancellationToken cancelamento)
    {
        const string Operacao = "abrir a gravação de uma tabela";
        ArgumentNullException.ThrowIfNull(pedido);
        var gravador = Gravador(Operacao);
        GravacaoTabela? gravacao;
        try
        {
            gravacao = await gravador.AbrirAsync(pedido, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
        }

        return new GravacaoProtegida(gravacao ?? throw SemResultado(_provedor, Operacao, _lado), _provedor, _lado);
    }

    private GravadorEmLote Gravador(string operacao)
    {
        if (_banco is not DialetoDestino destino)
        {
            throw new InvalidOperationException("A gravação só existe no destino.");
        }

        GravadorEmLote? gravador;
        try
        {
            gravador = destino.Gravador;
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, operacao, _lado, CancellationToken.None);
        }

        return gravador ?? throw SemResultado(_provedor, operacao, _lado);
    }

    internal sealed partial class LeitorProtegido
    {
        /// <summary>Começa a ler as linhas de uma tabela no instante da sessão; o leitor fica ocupado até o descarte da leitura.</summary>
        /// <param name="pedido">A tabela, as colunas e as convertidas.</param>
        /// <param name="cancelamento">Cancela a abertura.</param>
        /// <returns>A leitura protegida, que o chamador descarta.</returns>
        /// <exception cref="InvalidOperationException">O leitor já está numa leitura (defeito do motor).</exception>
        /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<LeituraProtegida> AbrirTabelaAsync(PedidoLeituraTabela pedido, CancellationToken cancelamento)
        {
            const string Operacao = "abrir a leitura de uma tabela";
            ArgumentNullException.ThrowIfNull(pedido);
            if (Interlocked.Exchange(ref _ocupado, 1) != 0)
            {
                throw new InvalidOperationException("O leitor da sessão já está numa leitura.");
            }

            LeituraTabela? leitura;
            try
            {
                leitura = await _leitor.AbrirTabelaAsync(pedido, cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                Volatile.Write(ref _ocupado, 0);
                throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
            }

            if (leitura is null)
            {
                Volatile.Write(ref _ocupado, 0);
                throw SemResultado(_provedor, Operacao, _lado);
            }

            return new LeituraProtegida(leitura, _provedor, _lado, () => Volatile.Write(ref _ocupado, 0));
        }
    }

    /// <summary>A leitura das linhas de uma tabela, protegida; o descarte libera o leitor.</summary>
    internal sealed class LeituraProtegida : IAsyncDisposable
    {
        private readonly LeituraTabela _leitura;
        private readonly string _provedor;
        private readonly LadoBanco _lado;
        private readonly Action _liberar;
        private int _descartada;

        internal LeituraProtegida(LeituraTabela leitura, string provedor, LadoBanco lado, Action liberar)
        {
            _leitura = leitura;
            _provedor = provedor;
            _lado = lado;
            _liberar = liberar;
        }

        /// <summary>O próximo lote, ou nulo no fim (o dialeto já conferiu que a sessão seguiu intacta).</summary>
        /// <param name="cancelamento">Cancela a leitura.</param>
        /// <returns>O lote protegido, de que o chamador é o dono; nulo no fim.</returns>
        /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<LoteProtegido?> LerLoteAsync(CancellationToken cancelamento)
        {
            const string Operacao = "ler as linhas de uma tabela";
            LoteLinhas? lote;
            try
            {
                lote = await _leitura.LerLoteAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
            }

            return lote is null ? null : new LoteProtegido(lote, _provedor, _lado);
        }

        /// <summary>Descarta a leitura, uma vez só, e libera o leitor; nunca lança.</summary>
        /// <returns>A tarefa do descarte.</returns>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _descartada, 1) != 0)
            {
                return;
            }

            try
            {
                await _leitura.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _liberar();
            }
        }

        /// <inheritdoc/>
        public override string ToString() => nameof(LeituraProtegida);
    }

    /// <summary>
    /// Um lote lido, protegido: cada chamada por célula traduz a exceção do dialeto só com o código ou o tipo. Um lote numa
    /// thread de cada vez; o dono descarta, ou passa à gravação, que passa a ser a dona.
    /// </summary>
    internal sealed class LoteProtegido : IDisposable
    {
        private readonly string _provedor;
        private readonly LadoBanco _lado;

        internal LoteProtegido(LoteLinhas lote, string provedor, LadoBanco lado)
        {
            Lote = lote;
            _provedor = provedor;
            _lado = lado;
        }

        /// <summary>Quantas linhas o lote tem.</summary>
        internal int Quantidade => Lote.Quantidade;

        /// <summary>O número, na leitura (a partir de 1), da primeira linha do lote.</summary>
        internal long PrimeiraLinha => Lote.PrimeiraLinha;

        /// <summary>O lote do dialeto, só para a gravação protegida.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal LoteLinhas Lote { get; }

        /// <summary>Lê, na forma canônica, o valor original de uma coluna convertida.</summary>
        /// <param name="linha">A linha no lote.</param>
        /// <param name="coluna">A posição da coluna no pedido.</param>
        /// <param name="valor">O valor, ou nulo.</param>
        /// <returns>Falso quando o valor não tem representação na forma canônica.</returns>
        /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
        internal bool TentarLer(int linha, int coluna, out object? valor)
        {
            try
            {
                return Lote.TentarLer(linha, coluna, out valor);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "ler uma célula do lote", _lado, CancellationToken.None);
            }
        }

        /// <summary>Escreve o valor tratado numa coluna convertida.</summary>
        /// <param name="linha">A linha no lote.</param>
        /// <param name="coluna">A posição da coluna no pedido.</param>
        /// <param name="valor">O valor, já conferido na forma canônica.</param>
        /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
        internal void Escrever(int linha, int coluna, object? valor)
        {
            try
            {
                Lote.Escrever(linha, coluna, valor);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "escrever uma célula do lote", _lado, CancellationToken.None);
            }
        }

        /// <summary>Mantém o valor original de uma coluna convertida.</summary>
        /// <param name="linha">A linha no lote.</param>
        /// <param name="coluna">A posição da coluna no pedido.</param>
        /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
        internal void Manter(int linha, int coluna)
        {
            try
            {
                Lote.Manter(linha, coluna);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "manter uma célula do lote", _lado, CancellationToken.None);
            }
        }

        /// <summary>Descarta o lote; nunca lança (a base garante).</summary>
        public void Dispose() => Lote.Dispose();

        /// <inheritdoc/>
        public override string ToString() => nameof(LoteProtegido);
    }

    /// <summary>A gravação de uma tabela do destino, protegida.</summary>
    internal sealed class GravacaoProtegida : IAsyncDisposable
    {
        private readonly GravacaoTabela _gravacao;
        private readonly string _provedor;
        private readonly LadoBanco _lado;

        internal GravacaoProtegida(GravacaoTabela gravacao, string provedor, LadoBanco lado)
        {
            _gravacao = gravacao;
            _provedor = provedor;
            _lado = lado;
        }

        /// <summary>Grava um lote, que passa a ser da gravação (descartado por ela, também na falha).</summary>
        /// <param name="lote">O lote.</param>
        /// <param name="cancelamento">Cancela a gravação do lote.</param>
        /// <returns>A tarefa.</returns>
        /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task GravarAsync(LoteProtegido lote, CancellationToken cancelamento)
        {
            ArgumentNullException.ThrowIfNull(lote);
            try
            {
                await _gravacao.GravarAsync(lote.Lote, cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "gravar as linhas de uma tabela", _lado, cancelamento);
            }
        }

        /// <summary>Conclui a gravação, que confirma.</summary>
        /// <param name="cancelamento">Cancela a conclusão.</param>
        /// <returns>Quantas linhas a tabela recebeu.</returns>
        /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou.</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        internal async Task<long> ConcluirAsync(CancellationToken cancelamento)
        {
            try
            {
                return await _gravacao.ConcluirAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao)
            {
                throw TraduzirSemMensagem(excecao, _provedor, "concluir a gravação de uma tabela", _lado, cancelamento);
            }
        }

        /// <summary>Descarta a gravação (sem concluir, desfaz); nunca lança (a base garante).</summary>
        /// <returns>A tarefa do descarte.</returns>
        public ValueTask DisposeAsync() => _gravacao.DisposeAsync();

        /// <inheritdoc/>
        public override string ToString() => nameof(GravacaoProtegida);
    }
}
