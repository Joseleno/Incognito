using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// O gravador do destino falso: a gravação junta as linhas e só as põe nos dados do banco na conclusão (a tabela gravada
/// numa transação só; com o defeito do banco não atômico, a cada lote); a tabela segurada falha; a conexão de cada chamada é
/// contada. Com os defeitos da gravação.
/// </summary>
internal sealed class GravadorFalso(DadosDoBancoFalso dados, Defeito defeito) : GravadorEmLote
{
    private readonly List<GravacaoFalsa> _vivas = [];

    // As linhas de todas as gravações, com o defeito do estado comum.
    private readonly List<IReadOnlyDictionary<string, object?>> _comum = [];

    /// <summary>Fecha as gravações vivas (o descarte do dialeto).</summary>
    internal async ValueTask FecharAsync()
    {
        GravacaoFalsa[] vivas;
        lock (_vivas)
        {
            vivas = [.. _vivas];
        }

        foreach (var gravacao in vivas)
        {
            await gravacao.DisposeAsync();
        }
    }

    protected override async Task EsvaziarAsyncCore(NomeObjeto tabela, CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();
        await ConferirAsync(tabela, []);
        dados.ContarGravacao(1);
        try
        {
            var naoEsvazia = (defeito == Defeito.GravacaoSoDaRaiz && tabela.Equals(DadosDoBancoFalso.Particionada))
                || (defeito == Defeito.EsvaziamentoNaoEsvazia && !tabela.Equals(DadosDoBancoFalso.Particionada));
            if (!naoEsvazia)
            {
                dados.Gravar(tabela, []);
            }

            EsvaziarHerdeiras(dados, tabela, defeito);
        }
        finally
        {
            if (defeito is not (Defeito.ConexaoDaGravacaoAberta or Defeito.EsvaziamentoDeixaConexao))
            {
                dados.ContarGravacao(-1);
            }
        }
    }

    protected override async Task<GravacaoTabela> AbrirAsyncCore(PedidoGravacao pedido, CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();
        await ConferirAsync(pedido.Tabela, pedido.Colunas);
        if (defeito != Defeito.GravacaoSemConexaoPropria)
        {
            dados.ContarGravacao(1);
        }

        List<IReadOnlyDictionary<string, object?>> linhas = [];
        if (defeito == Defeito.GravacoesComEstadoComum)
        {
            linhas = _comum;
        }

        // O banco não atômico confirma o esvaziamento na abertura, e cada lote na hora.
        if (defeito is Defeito.GravacaoNaoAtomica or Defeito.GravacaoNaoAtomicaInventa)
        {
            dados.Gravar(pedido.Tabela, []);
        }

        var gravacao = new GravacaoFalsa(dados, defeito, linhas, viva =>
        {
            lock (_vivas)
            {
                _vivas.Remove(viva);
            }
        });
        lock (_vivas)
        {
            _vivas.Add(gravacao);
        }

        return gravacao;
    }

    /// <summary>Com o defeito, a mãe esvaziada leva as herdeiras junto.</summary>
    internal static void EsvaziarHerdeiras(DadosDoBancoFalso dados, NomeObjeto tabela, Defeito defeito)
    {
        if (defeito == Defeito.GravacaoComHerdeiras && tabela.Nome == "pessoa")
        {
            dados.Gravar(BancoFalso.Nome("funcionario"), []);
        }
    }

    // A tabela existe, tem as colunas e ninguém a segura (ou o defeito ignora quem segura, demora ou falha sem código).
    private async Task ConferirAsync(NomeObjeto tabela, IReadOnlyList<string> colunas)
    {
        _ = LinhasFalsas.De(tabela, dados.Agora(), Defeito.Nenhum);
        if (colunas.FirstOrDefault(coluna => !LinhasFalsas.Colunas(tabela).Contains(coluna)) is { } inexistente)
        {
            throw new DialetoRecusouException("INC3399", $"A coluna {inexistente} não existe em {tabela}.");
        }

        if (!dados.Travada(tabela) || defeito == Defeito.TabelaSeguradaIgnorada)
        {
            return;
        }

        if (defeito == Defeito.TabelaSeguradaDemora)
        {
            await Task.Delay(FixtureFalsa.Prazo + TimeSpan.FromSeconds(2));
        }

        throw defeito == Defeito.TabelaSeguradaSemCodigo
            ? new InvalidOperationException("A tabela estava em uso.")
            : new IncognitoException("INC3399", $"A tabela {tabela} estava em uso por outra sessão e não foi liberada a tempo.");
    }
}

/// <summary>A gravação do falso: as linhas ficam com ela até a conclusão, que as põe nos dados; o descarte sem ela as esquece.</summary>
internal sealed class GravacaoFalsa(DadosDoBancoFalso dados, Defeito defeito, List<IReadOnlyDictionary<string, object?>> linhas, Action<GravacaoFalsa> aoDescartar) : GravacaoTabela
{
    private bool _concluida;
    private bool _recebeu;
    private bool _concluirTentado;

    private bool NaoAtomica => defeito is Defeito.GravacaoNaoAtomica or Defeito.GravacaoNaoAtomicaInventa;

    protected override async Task GravarAsyncCore(LoteLinhas lote, CancellationToken cancelamento)
    {
        // A tabela longa grava devagar, respondendo ao cancelamento (o defeito o ignora): o cancelamento chega no meio.
        if (Pedido.Tabela.Nome == "longa")
        {
            if (defeito == Defeito.GravacaoIgnoraCancelamento)
            {
                await Task.Delay(FixtureFalsa.Prazo + TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancelamento);
            }
        }

        cancelamento.ThrowIfCancellationRequested();
        if (lote is not LoteLinhasFalso falso)
        {
            if (defeito == Defeito.LoteDeForaComOutraExcecao)
            {
                throw new NotSupportedException("O lote não é do dialeto falso.");
            }

            if (defeito != Defeito.LoteDeForaAceito)
            {
                throw new InvalidOperationException("O lote não é do dialeto falso.");
            }

            linhas.Add(Pedido.Colunas.ToDictionary(coluna => coluna, object? (_) => LinhasFalsas.Opaco, StringComparer.Ordinal));
            return;
        }

        _recebeu = true;
        for (var linha = 0; linha < lote.Quantidade; linha++)
        {
            linhas.Add(Gravada(falso, linha));
        }

        if (NaoAtomica)
        {
            dados.Gravar(Pedido.Tabela, [.. linhas]);
        }
    }

    protected override Task ConcluirAsyncCore(CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();
        _concluirTentado = true;

        // A unicidade do texto da tabela canônica, sem o valor na mensagem (salvo os defeitos).
        if (Pedido.Tabela.Nome == "tipos"
            && linhas.Select(linha => linha.GetValueOrDefault("texto")).OfType<string>().GroupBy(texto => texto, StringComparer.Ordinal).FirstOrDefault(grupo => grupo.Count() > 1) is { } repetido)
        {
            var violacao = new IncognitoException("INC3399", "Uma linha gravada violou a unicidade de texto." + (defeito == Defeito.SentinelaNaViolacao ? " Valor: " + repetido.Key : ""));
            if (defeito == Defeito.ViolacaoComValorNoData)
            {
                violacao.Data["valor"] = repetido.Key;
            }

            throw violacao;
        }

        if (!(defeito == Defeito.GravacaoSoDaRaiz && Pedido.Tabela.Equals(DadosDoBancoFalso.Particionada)))
        {
            dados.Gravar(Pedido.Tabela, defeito == Defeito.GravacaoNaoEsvazia ? [.. LinhasFalsas.De(Pedido.Tabela, dados.Agora(), Defeito.Nenhum), .. linhas] : [.. linhas]);
        }

        GravadorFalso.EsvaziarHerdeiras(dados, Pedido.Tabela, defeito);
        _concluida = true;
        return Task.CompletedTask;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (!_concluida)
        {
            if (defeito == Defeito.DescarteDaGravacaoConfirma)
            {
                dados.Gravar(Pedido.Tabela, [.. linhas]);
            }

            if (defeito == Defeito.GravacaoNaoAtomicaInventa)
            {
                dados.Gravar(Pedido.Tabela, [.. linhas, Pedido.Colunas.ToDictionary(coluna => coluna, object? (_) => "inventada", StringComparer.Ordinal)]);
            }

            // Só a gravação que recebeu lotes e não tentou concluir: o fato que mede o descarte.
            if (defeito == Defeito.DescarteDaGravacaoDemora && _recebeu && !_concluirTentado)
            {
                await Task.Delay(FixtureFalsa.Prazo + TimeSpan.FromSeconds(2));
            }
        }

        if (defeito != Defeito.GravacoesComEstadoComum)
        {
            linhas.Clear();
        }

        if (defeito is not (Defeito.ConexaoDaGravacaoAberta or Defeito.GravacaoSemConexaoPropria))
        {
            dados.ContarGravacao(-1);
        }

        aoDescartar(this);
    }

    // A linha como fica gravada: a célula escrita (ou mantida) com o que o lote guardou, as outras como vieram; com os
    // defeitos de quem ignora a escrita, toma a escrita pela ordem da lista ou troca o opaco.
    private Dictionary<string, object?> Gravada(LoteLinhasFalso falso, int linha)
    {
        var gravada = new Dictionary<string, object?>(StringComparer.Ordinal);
        var comEscrita = Enumerable.Range(0, Pedido.Colunas.Count).Any(coluna => falso.Escritos.ContainsKey((linha, coluna)));
        for (var coluna = 0; coluna < Pedido.Colunas.Count; coluna++)
        {
            var fonte = coluna;
            if (defeito == Defeito.EscritaPelaPosicao && falso.Escritos.ContainsKey((linha, coluna)))
            {
                var indices = falso.Indices;
                fonte = indices.Order().ElementAt(indices.IndexOf(coluna));
            }

            var original = falso.Original(linha, coluna);
            var valor = falso.Escritos.TryGetValue((linha, fonte), out var escrito) && defeito != Defeito.GravacaoIgnoraEscrita ? escrito : original;
            var opaco = !falso.Escritos.ContainsKey((linha, coluna));
            if (opaco && valor is string texto
                && (defeito == Defeito.GravacaoTrocaOpaco || (defeito == Defeito.TrocaOpacoNaLinhaEscrita && comEscrita)))
            {
                valor = texto + "x";
            }

            gravada[Pedido.Colunas[coluna]] = valor;
        }

        return gravada;
    }
}
