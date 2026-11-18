// O tratamento lê e escreve o lote do contrato experimental de dialeto, sempre pelo envoltório; nada daqui é público.
#pragma warning disable INCX001

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O tratamento das colunas de uma unidade copiada, lote a lote: por linha, lê todos os originais (as tratadas e as
/// referências de <c>derivarDe</c>) antes de escrever qualquer célula, porque a escrita apaga o original (decisão 102); depois,
/// em cada tratada, o valor sem representação segue a política (D3), o NULL sem <see cref="Transformador.TransformaNulo"/>
/// sai NULL sem chamar, e o resto passa pelo transformador, com o resultado conferido antes de escrever. Seguro entre threads:
/// cada thread trata os seus lotes e conta os desfechos no seu <see cref="Contagem"/>.
/// </summary>
internal sealed partial class TratamentoDoLote
{
    private static readonly int _desfechos = Enum.GetValues<Desfecho>().Max(desfecho => (int)desfecho) + 1;

    private readonly UnidadeDeCopia _unidade;
    private readonly Tratada[] _tratadas;
    private readonly int[] _lidas;
    private readonly CancellationToken _cancelamento;

    /// <summary>Prepara o tratamento de uma unidade.</summary>
    /// <param name="unidade">A unidade copiada.</param>
    /// <param name="transformadores">Os transformadores da execução.</param>
    /// <param name="cancelamento">O token do motor: só o pedido nele sai como cancelamento.</param>
    internal TratamentoDoLote(UnidadeDeCopia unidade, TransformadoresDaExecucao transformadores, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(unidade);
        ArgumentNullException.ThrowIfNull(transformadores);
        _unidade = unidade;
        _cancelamento = cancelamento;
        var posicao = unidade.ColunasLidas.Select((coluna, indice) => (coluna, indice)).ToDictionary(par => par.coluna, par => par.indice, StringComparer.Ordinal);
        _tratadas =
        [
            .. unidade.Tratadas.Select(tratada => new Tratada(
                tratada,
                transformadores.Da(unidade.Nome, tratada.Coluna),
                posicao[tratada.Coluna],
                tratada.Referencia is { } referencia ? posicao[referencia] : null)),
        ];
        Convertidas =
        [
            .. _tratadas.Select(tratada => new ColunaConvertida { Indice = tratada.Indice, Tipo = tratada.Coluna.Contexto.Tipo })
                .Concat(_tratadas.Where(tratada => tratada.Referencia is not null && _tratadas.All(outra => outra.Indice != tratada.Referencia))
                    .Select(tratada => new ColunaConvertida { Indice = tratada.Referencia!.Value, Tipo = tratada.Coluna.Contexto.TipoReferencia ?? throw new InvalidOperationException("Referência de derivarDe sem o tipo no contexto.") }))
                .DistinctBy(convertida => convertida.Indice)
                .OrderBy(convertida => convertida.Indice),
        ];
        _lidas = [.. Convertidas.Select(convertida => convertida.Indice)];
    }

    /// <summary>As colunas lidas na forma canônica: as tratadas e as referências, pela posição nas lidas.</summary>
    internal IReadOnlyList<ColunaConvertida> Convertidas { get; }

    /// <summary>As tratadas, pela posição nas lidas e com o tipo, para o pedido da gravação.</summary>
    internal IReadOnlyList<ColunaConvertida> Tratadas => [.. _tratadas.Select(tratada => new ColunaConvertida { Indice = tratada.Indice, Tipo = tratada.Coluna.Contexto.Tipo })];

    /// <summary>Uma contagem nova, por coluna tratada e desfecho, para uma thread.</summary>
    /// <returns>A contagem zerada.</returns>
    internal Contagem NovaContagem() => new(_tratadas.Length, _desfechos);

    /// <summary>Trata um lote inteiro.</summary>
    /// <param name="lote">O lote, de que quem chama segue o dono.</param>
    /// <param name="contagem">A contagem da thread.</param>
    /// <exception cref="FalhaTransformacaoException">Uma tratada não pôde ser transformada numa linha.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou numa célula.</exception>
    /// <exception cref="OperationCanceledException">O token do motor foi pedido e o transformador parou.</exception>
    internal void Tratar(DialetoProtegido.LoteProtegido lote, Contagem contagem)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(contagem);
        var valores = new object?[_unidade.ColunasLidas.Count];
        var representaveis = new bool[_unidade.ColunasLidas.Count];
        for (var linha = 0; linha < lote.Quantidade; linha++)
        {
            _cancelamento.ThrowIfCancellationRequested();

            // Todos os originais da linha antes de qualquer escrita.
            foreach (var indice in _lidas)
            {
                representaveis[indice] = lote.TentarLer(linha, indice, out valores[indice]);
            }

            for (var numero = 0; numero < _tratadas.Length; numero++)
            {
                var desfecho = TratarCelula(lote, linha, _tratadas[numero], valores, representaveis);
                contagem.Contar(numero, desfecho);
            }
        }
    }

    // Uma célula: a política para o valor sem representação, o NULL sem chamar, ou o transformador com o resultado conferido.
    private Desfecho TratarCelula(DialetoProtegido.LoteProtegido lote, int linha, Tratada tratada, object?[] valores, bool[] representaveis)
    {
        var numeroDaLinha = lote.PrimeiraLinha + linha;
        var coluna = tratada.Coluna;
        if (tratada.Referencia is { } referencia && !representaveis[referencia])
        {
            // Com preservar, a tratada sairia em claro por uma anomalia de outra coluna (D3).
            throw Falha(CodigosExecucao.SemRepresentacao, numeroDaLinha, coluna, $"a coluna {NomeTabela.Parte(coluna.Referencia!)} de 'derivarDe' tem um valor sem representação na forma canônica.");
        }

        if (!representaveis[tratada.Indice])
        {
            var politica = PoliticasQueAnulam.UsaEntradaMalformada(coluna.Fabrica) ? coluna.Regra.EntradaMalformada : PoliticaEntradaMalformada.Falhar;
            switch (politica)
            {
                case PoliticaEntradaMalformada.Anular:
                    lote.Escrever(linha, tratada.Indice, null);
                    return Desfecho.AnuladoMalformado;
                case PoliticaEntradaMalformada.Preservar:
                    lote.Manter(linha, tratada.Indice);
                    return Desfecho.CopiadoEmClaro;
                default:
                    throw Falha(CodigosExecucao.SemRepresentacao, numeroDaLinha, coluna, "o valor lido não tem representação na forma canônica, e a política é falhar.");
            }
        }

        var valor = valores[tratada.Indice];
        var resultado = Transformar(tratada, valor, tratada.Referencia is { } posicao ? valores[posicao] : null, numeroDaLinha);
        if (resultado is null)
        {
            lote.Escrever(linha, tratada.Indice, null);
            return Desfecho.Nulo;
        }

        if (ConferenciaDoResultado.Problema(coluna.Contexto, valor is null, resultado.Value) is { } problema)
        {
            throw Falha(CodigosExecucao.ResultadoInvalido, numeroDaLinha, coluna, $"o transformador {coluna.Fabrica.Nome}@{coluna.Fabrica.Versao} devolveu {problema}.");
        }

        // O copiado em claro fica como veio, byte a byte; o resto vai com o valor conferido.
        if (resultado.Value.Desfecho == Desfecho.CopiadoEmClaro)
        {
            lote.Manter(linha, tratada.Indice);
        }
        else
        {
            lote.Escrever(linha, tratada.Indice, resultado.Value.Valor);
        }

        return resultado.Value.Desfecho;
    }

    // O resultado do transformador, ou nulo quando o NULL sai NULL sem chamar; a exceção de fora sai só com o tipo.
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Relança a falta de memória de um transformador de fora sem a mensagem nem a interna dele.")]
    private ResultadoTransformacao? Transformar(Tratada tratada, object? valor, object? referencia, long linha)
    {
        var coluna = tratada.Coluna;
        try
        {
            if (valor is null && !tratada.Transformador.TransformaNulo)
            {
                return null;
            }

            return tratada.Transformador.Transformar(valor, new ContextoTransformacao(referencia));
        }
        catch (OperationCanceledException) when (_cancelamento.IsCancellationRequested)
        {
            throw new OperationCanceledException(_cancelamento);
        }
        catch (OutOfMemoryException) when (!coluna.Fabrica.Embutida)
        {
            throw new OutOfMemoryException();
        }
        catch (IncognitoException excecao) when (coluna.Fabrica.Embutida && excecao.GetType() == typeof(IncognitoException))
        {
            // As embutidas lançam só a IncognitoException de tipo exato, com mensagens sem valor (INC1003, INC1004).
            throw Falha(CodigosExecucao.TransformadorFalhou, linha, coluna, $"o transformador {coluna.Fabrica.Nome}@{coluna.Fabrica.Versao} falhou ({excecao.Codigo}: {TextoSeguro.Mensagem(excecao.Message)}).");
        }
        catch (Exception excecao)
        {
            // Só o tipo, e o código quando tem a forma INCnnnn: a mensagem, a interna e o Data podem trazer o valor.
            var codigo = excecao is IncognitoException incognito && FormaDoCodigo().IsMatch(incognito.Codigo) ? $", {incognito.Codigo}" : "";
            throw Falha(CodigosExecucao.TransformadorFalhou, linha, coluna, $"o transformador {coluna.Fabrica.Nome}@{coluna.Fabrica.Versao} lançou {TextoSeguro.Nome(excecao.GetType().Name)}{codigo}.");
        }
    }

    private FalhaTransformacaoException Falha(string codigo, long linha, ColunaTratada coluna, string motivo) =>
        new(codigo, _unidade.Nome, coluna.Coluna, linha, motivo);

    [GeneratedRegex("^INC[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDoCodigo();

    /// <summary>A contagem de uma thread, por coluna tratada e desfecho; juntada às outras no fim.</summary>
    internal sealed class Contagem
    {
        private readonly long[][] _porColuna;

        internal Contagem(int colunas, int desfechos) =>
            _porColuna = [.. Enumerable.Range(0, colunas).Select(_ => new long[desfechos])];

        /// <summary>Quantas vezes cada desfecho saiu em cada coluna tratada, na ordem da unidade.</summary>
        internal IReadOnlyList<IReadOnlyList<long>> PorColuna => _porColuna;

        internal void Contar(int coluna, Desfecho desfecho) => _porColuna[coluna][(int)desfecho]++;

        /// <summary>Soma outra contagem nesta.</summary>
        /// <param name="outra">A outra contagem, da mesma unidade.</param>
        internal void Somar(Contagem outra)
        {
            for (var coluna = 0; coluna < _porColuna.Length; coluna++)
            {
                for (var desfecho = 0; desfecho < _porColuna[coluna].Length; desfecho++)
                {
                    _porColuna[coluna][desfecho] += outra._porColuna[coluna][desfecho];
                }
            }
        }
    }

    private sealed record Tratada(ColunaTratada Coluna, Transformador Transformador, int Indice, int? Referencia);
}

/// <summary>
/// A conferência do resultado de um transformador antes de escrever: desfecho conhecido e definido, nulidade pelo desfecho
/// (o enum inteiro coberto), a forma canônica exata do tipo da coluna e o contexto dela (comprimento, faixa inteira,
/// precisão e escala, fração abaixo do microssegundo). O motivo nunca traz o valor.
/// </summary>
internal static class ConferenciaDoResultado
{
    /// <summary>O problema do resultado, ou nulo quando ele pode ser escrito.</summary>
    /// <param name="contexto">O contexto da coluna.</param>
    /// <param name="entradaNula">Se a entrada era NULL.</param>
    /// <param name="resultado">O resultado.</param>
    /// <returns>O motivo, para a mensagem; nulo quando o resultado confere.</returns>
    internal static string? Problema(ContextoDaColuna contexto, bool entradaNula, ResultadoTransformacao resultado)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var desfecho = resultado.Desfecho;
        bool? nulo = desfecho switch
        {
            Desfecho.Nulo or Desfecho.AnuladoDvInvalido or Desfecho.AnuladoMalformado => true,
            Desfecho.Pseudonimizado or Desfecho.DvInvalidoPreservado or Desfecho.PontoFixo or Desfecho.CopiadoEmClaro
                or Desfecho.ServicoPreservado or Desfecho.Vazio or Desfecho.Zerado or Desfecho.Substituido => false,
            _ => null,
        };
        if (nulo is null)
        {
            return "um desfecho indefinido ou desconhecido";
        }

        if (desfecho == Desfecho.Nulo && !entradaNula)
        {
            return "o desfecho Nulo para uma entrada que não é NULL";
        }

        var valor = resultado.Valor;
        if (nulo.Value != valor is null)
        {
            return nulo.Value ? $"um valor com o desfecho {desfecho}" : $"NULL com o desfecho {desfecho}";
        }

        if (valor is null)
        {
            return null;
        }

        if (!FormaCanonica.Aceita(contexto.Tipo, valor))
        {
            return "um valor fora da forma canônica do tipo da coluna";
        }

        return valor switch
        {
            string texto when texto.Length > contexto.ComprimentoMaximo => "um texto acima do comprimento da coluna",
            byte[] bytes when bytes.Length > contexto.ComprimentoMaximo => "um binário acima do comprimento da coluna",
            long inteiro when inteiro < contexto.MenorInteiro || inteiro > contexto.MaiorInteiro => "um inteiro fora da faixa da coluna",
            decimal numero when !CabeNoDecimal(numero, contexto.Precisao, contexto.Escala) => "um decimal acima da precisão ou da escala da coluna",
            DateTime data when data.Ticks % TimeSpan.TicksPerMicrosecond != 0 => "uma data e hora com fração abaixo do microssegundo",
            DateTimeOffset data when data.UtcTicks % TimeSpan.TicksPerMicrosecond != 0 => "uma data e hora com fração abaixo do microssegundo",
            _ => null,
        };
    }

    // numeric(p, s): no máximo s casas e p − s dígitos na parte inteira; sem a escala, p dígitos ao todo. O banco
    // arredondaria em silêncio a casa a mais.
    private static bool CabeNoDecimal(decimal numero, int? precisao, int? escala)
    {
        var casas = numero.Scale;
        if (escala is { } maximo && casas > maximo)
        {
            return false;
        }

        if (precisao is not { } digitos)
        {
            return true;
        }

        var inteiros = DigitosInteiros(decimal.Truncate(Math.Abs(numero)));
        return escala is { } s ? inteiros <= digitos - s : inteiros + casas <= digitos;
    }

    private static int DigitosInteiros(decimal parteInteira)
    {
        var digitos = 0;
        while (parteInteira >= 1)
        {
            parteInteira = decimal.Truncate(parteInteira / 10);
            digitos++;
        }

        return digitos;
    }
}
