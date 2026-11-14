using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// A chave da execução: no <see cref="ModoTratamento.Efemero"/>, gerada; no <see cref="ModoTratamento.Reproduzivel"/>, do
/// resolvedor, que devolve uma instância nova por chamada. O executor é o dono dela: descarta e zera no fim. Nada a guarda em
/// campo, estático ou singleton.
/// </summary>
internal static class ChaveDaExecucao
{
    /// <summary>Obtém a chave.</summary>
    /// <param name="configuracao">A configuração da carga refeita.</param>
    /// <param name="resolverChave">Resolve a referência como escrita; nulo quando ela não se resolve.</param>
    /// <returns>A chave, de que quem chama é o dono.</returns>
    /// <exception cref="ConfiguracaoInvalidaException">
    /// A referência não se resolveu (<c>INC2012</c>, saída 2, sem o caminho nem a mensagem do resolvedor).
    /// </exception>
    internal static ChaveIncognito Obter(ConfiguracaoDaExecucao configuracao, Func<ReferenciaEscrita, ChaveIncognito?> resolverChave)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(resolverChave);
        if (configuracao.Modo == ModoTratamento.Efemero)
        {
            return ChaveIncognito.Gerar();
        }

        var referencia = configuracao.Chave ?? throw new InvalidOperationException("O modo reproduzível chegou sem a referência da chave.");
        ChaveIncognito? chave;
        try
        {
            chave = resolverChave(referencia);
        }
        catch (ConfiguracaoInvalidaException)
        {
            // A recusa do próprio resolvedor do Incognito já sai sem o caminho; outra IncognitoException sai só com o tipo.
            throw;
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            throw NaoResolvida(referencia, excecao.GetType().Name);
        }

        return chave ?? throw NaoResolvida(referencia, null);
    }

    // Só o tipo da referência e, se houve, o da exceção: o caminho do arquivo e o nome da variável podem dizer demais.
    private static ConfiguracaoInvalidaException NaoResolvida(ReferenciaEscrita referencia, string? tipo)
    {
        var onde = referencia.Tipo == TipoReferenciaSegredo.Arquivo ? "o arquivo" : "a variável de ambiente";
        var porque = tipo is null ? "" : $" ({TextoSeguro.Mensagem(tipo)})";
        return new ConfiguracaoInvalidaException(
            [new ErroConfiguracao(CodigosConfiguracao.Referencia, null, null, null, "/chave", $"A referência da chave não se resolveu{porque}: confira {onde} indicado em 'chave'.")],
            omitidos: false);
    }
}

/// <summary>
/// Os transformadores da execução: a sessão real (a chave da execução e a tabela de domínios) e um transformador por coluna
/// tratada de cada unidade copiada, criado pelo contexto da carga refeita na criação protegida, que confere o nome, a
/// versão e a injetividade registrados. Cada instância serve a todas as threads da coluna (o contrato do transformador é
/// seguro entre threads) e é descartada uma vez só, a repetida também; o descarte vem depois de aguardar todas as tarefas,
/// também no cancelamento. Não é dono da chave.
/// </summary>
internal sealed class TransformadoresDaExecucao
{
    private readonly SessaoTransformacao _sessao;
    private readonly Dictionary<(NomeTabela Tabela, string Coluna), Transformador> _porColuna = [];
    private readonly List<Criado> _distintos = [];
    private readonly HashSet<Transformador> _vistos = new(ReferenceEqualityComparer.Instance);
    private int _descartados;

    private TransformadoresDaExecucao(SessaoTransformacao sessao) => _sessao = sessao;

    /// <summary>Cria a sessão e os transformadores; na falha, descarta o que criou, e o erro da criação é o que sai.</summary>
    /// <param name="carga">A carga refeita.</param>
    /// <param name="chave">A chave da execução (o chamador é o dono).</param>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>Os transformadores.</returns>
    /// <exception cref="PlanoRecusadoException">
    /// Uma fábrica recusou a coluna ou falhou, ou criou um transformador diferente do registrado (saída 2, antes da cópia).
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal static TransformadoresDaExecucao Criar(DadosDeExecucao carga, ChaveIncognito chave, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(carga);
        ArgumentNullException.ThrowIfNull(chave);
        var criados = new TransformadoresDaExecucao(new SessaoTransformacao(chave, carga.Configuracao.Ambiente) { Dominios = carga.Dominios });
        try
        {
            var erros = new List<ErroPlano>();
            foreach (var unidade in carga.Unidades.Where(unidade => unidade.Acao == AcaoTabela.Copiar))
            {
                foreach (var tratada in unidade.Tratadas)
                {
                    var (criado, erro) = CriacaoProtegida.Criar(tratada.Fabrica, tratada.Regra, tratada.Contexto, criados._sessao, cancelamento);
                    if (erro is { } falha)
                    {
                        // Na execução, a fábrica de fora já recebeu a sessão com a chave real: a recusa dela sai só com o tipo.
                        var mensagem = falha.Codigo == CodigosPlanejar.CriacaoRecusada && !tratada.Fabrica.Embutida
                            ? $"O transformador {tratada.Fabrica.Nome}@{tratada.Fabrica.Versao} recusou a coluna na execução ({nameof(RegraInvalidaException)}): planeje de novo antes de executar."
                            : falha.Mensagem;
                        erros.Add(new ErroPlano(falha.Codigo, mensagem, unidade.Nome, tratada.Coluna));
                        continue;
                    }

                    criados.Guardar(unidade.Nome, tratada, criado!);
                }
            }

            return erros.Count == 0 ? criados : throw new PlanoRecusadoException(erros);
        }
        catch
        {
            criados.DescartarNaFalha();
            throw;
        }
    }

    /// <summary>O transformador de uma coluna tratada de uma unidade copiada.</summary>
    /// <param name="tabela">A unidade, pelo nome da carga.</param>
    /// <param name="coluna">A coluna, pelo nome no catálogo da origem.</param>
    /// <returns>O transformador.</returns>
    internal Transformador Da(NomeTabela tabela, string coluna) =>
        Volatile.Read(ref _descartados) == 0
            ? _porColuna[(tabela, coluna)]
            : throw new ObjectDisposedException(nameof(TransformadoresDaExecucao));

    /// <summary>
    /// Descarta cada instância uma vez, em ordem de criação, e depois a sessão; só a primeira chamada descarta. A falha no
    /// descarte de um transformador de fora vira aviso só com o tipo, sem tomar o lugar de nada; a de uma embutida é defeito e
    /// sobe depois de descartar o resto.
    /// </summary>
    /// <returns>Os avisos.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A falha de descarte de uma fábrica de fora vira aviso só com o tipo; nada mais é engolido.")]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Relança a falta de memória de um transformador de fora sem a mensagem nem a interna dele.")]
    internal IReadOnlyList<Aviso> Descartar()
    {
        if (Interlocked.Exchange(ref _descartados, 1) != 0)
        {
            return [];
        }

        var avisos = new List<Aviso>();
        Exception? defeito = null;
        foreach (var criado in _distintos)
        {
            try
            {
                (criado.Transformador as IDisposable)?.Dispose();
            }
            catch (OutOfMemoryException) when (!criado.Fabrica.Embutida)
            {
                // A falta de memória não é aviso: sobe nova, sem a mensagem de fora, depois de descartar o resto.
                defeito ??= new OutOfMemoryException();
            }
            catch (Exception excecao) when (!criado.Fabrica.Embutida)
            {
                avisos.Add(new Aviso(
                    CodigosExecucao.DescarteDoTransformador,
                    $"O descarte do transformador {criado.Fabrica.Nome}@{criado.Fabrica.Versao} falhou ({TextoSeguro.Mensagem(excecao.GetType().Name)}); a cópia não muda.",
                    criado.Tabela,
                    criado.Coluna));
            }
            catch (Exception excecao)
            {
                defeito ??= excecao;
            }
        }

        try
        {
            _sessao.Dispose();
        }
        catch (Exception excecao)
        {
            defeito ??= excecao;
        }

        if (defeito is not null)
        {
            ExceptionDispatchInfo.Throw(defeito);
        }

        return avisos;
    }

    private void Guardar(NomeTabela tabela, ColunaTratada tratada, Transformador transformador)
    {
        _porColuna.Add((tabela, tratada.Coluna), transformador);
        if (_vistos.Add(transformador))
        {
            _distintos.Add(new Criado(transformador, tratada.Fabrica, tabela, tratada.Coluna));
        }
    }

    // Na falha da criação, o erro dela é o que sai: o descarte nunca toma o lugar.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O erro da criação é o que sai; a falha do descarte cede a ele.")]
    private void DescartarNaFalha()
    {
        try
        {
            Descartar();
        }
        catch
        {
        }
    }

    private sealed record Criado(Transformador Transformador, FabricaRegistrada Fabrica, NomeTabela Tabela, string Coluna);
}
