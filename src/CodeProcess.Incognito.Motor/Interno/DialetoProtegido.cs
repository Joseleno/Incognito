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
/// inclusive código fora da faixa, vira falha <c>INC3001</c> só com o tipo, a operação e o lado. Nas operações que tocam dado
/// de coluna ou texto de quem administra o banco (o maior valor, a identidade com o marcador, a gravação do marcador, a
/// existência de linhas no destino, o bloqueio e o diário), nem a recusa nem a <see cref="IncognitoException"/> de tipo
/// exato levam a mensagem: só o código. A concessão do bloqueio volta protegida (<see cref="ConcessaoProtegida"/>): a perda
/// sai como <c>INC4001</c>, saída 1.
/// </para>
/// <para>
/// O diário só trabalha com a concessão viva: cada operação recebe a concessão, o dialeto recebe o token do chamador ligado
/// à perda do bloqueio, e a perda sai como <c>INC4001</c>, nunca como cancelamento do chamador. A recusa do dialeto no
/// diário é falha (saída 1), porque ele só trabalha na execução. O que o dialeto devolve do diário é conferido: o que se
/// contradiz é falha, nunca pulado.
/// </para>
/// <para>
/// Da exceção original nunca se lê <see cref="Exception.Data"/>, <see cref="Exception.StackTrace"/>,
/// <see cref="Exception.InnerException"/> nem o <c>ToString</c>: as do driver trazem a string de conexão, o comando ou valores.
/// O nome do provedor nas mensagens é o da configuração, nunca o que o dialeto diz.
/// </para>
/// </remarks>
internal sealed partial class DialetoProtegido : IAsyncDisposable
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

    /// <summary>
    /// Lê a identidade, com o marcador. O banco pode ter no lugar do marcador um comentário de quem o administra, que o
    /// dialeto poderia pôr na exceção: qualquer exceção sai só com o código ou o tipo. O marcador que se contradiz (válido
    /// sem o identificador ou a data, ou o contrário) é falha do dialeto.
    /// </summary>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>A identidade lida.</returns>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a leitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou leu um marcador que se contradiz.</exception>
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
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento, aceitaRecusa: true);
        }

        if (identidade is null)
        {
            throw SemResultado(_provedor, Operacao, _lado);
        }

        return identidade.Marcador is { } marcador && !Coerente(marcador)
            ? throw new FalhaDialetoException(CodigosPlanejar.EsquemaIncoerente, $"O dialeto de {_provedor} leu{NoLado(_lado)} um marcador do Incognito que se contradiz.")
            : identidade;
    }

    /// <summary>
    /// Grava o marcador do Incognito no destino. O banco pode ter um comentário de quem o administra: qualquer exceção sai só
    /// com o código ou o tipo, e a recusa (o banco já tem algo, ou o login não pode gravar) vira erro do plano, saída 2.
    /// </summary>
    /// <param name="identificador">O identificador aleatório.</param>
    /// <param name="gravadoEm">Quando, em UTC e sem fração de segundo.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="InvalidOperationException">O banco é a origem (defeito do motor).</exception>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a gravação.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task GravarMarcadorAsync(Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento)
    {
        var destino = Destino("o marcador só se grava no destino");
        try
        {
            await destino.Marcador.GravarAsync(identificador, gravadoEm, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, "gravar o marcador", _lado, cancelamento, aceitaRecusa: true);
        }
    }

    /// <summary>
    /// Se uma tabela do destino tem linhas. O destino pode guardar dado real: qualquer exceção sai só com o código ou o tipo,
    /// e a recusa (tabela de outra forma, sem leitura, segurança por linha) vira erro do plano, saída 2.
    /// </summary>
    /// <param name="tabela">A tabela, como o catálogo do destino a guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>Se há linha.</returns>
    /// <exception cref="InvalidOperationException">O banco é a origem (defeito do motor).</exception>
    /// <exception cref="PlanoRecusadoException">O dialeto recusou a leitura.</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<bool> TemLinhasAsync(NomeObjeto tabela, CancellationToken cancelamento)
    {
        var destino = Destino("a existência de linhas só se lê no destino");
        try
        {
            return await destino.Dados.TemLinhasAsync(tabela, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, "ler se uma tabela tem linhas", _lado, cancelamento, aceitaRecusa: true);
        }
    }

    /// <summary>
    /// Tenta adquirir o bloqueio exclusivo de execução do destino, sem esperar. Qualquer exceção sai só com o código ou o
    /// tipo, e a recusa também é falha (saída 1).
    /// </summary>
    /// <param name="cancelamento">Cancela a tentativa.</param>
    /// <returns>A concessão protegida, que o chamador descarta; nula quando outra sessão tem o bloqueio.</returns>
    /// <exception cref="InvalidOperationException">O banco é a origem (defeito do motor).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou (uma recusa também é falha aqui).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<ConcessaoProtegida?> TentarAdquirirBloqueioAsync(CancellationToken cancelamento)
    {
        const string Operacao = "adquirir o bloqueio";
        var destino = Destino("o bloqueio só se adquire no destino");
        ConcessaoBloqueio? concessao;
        try
        {
            concessao = await destino.Bloqueio.TentarAdquirirAsync(cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            // Sem a mensagem: o dialeto poderia dizer quem tem o bloqueio (a sessão, o login, o endereço).
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
        }

        return concessao is null ? null : new ConcessaoProtegida(concessao, concessao.Perdido);
    }

    /// <summary>
    /// O que o diário do destino pode deixar no esquema reservado. Cada objeto precisa estar no esquema reservado e aparecer
    /// uma vez só; senão, é falha do dialeto.
    /// </summary>
    /// <returns>Os objetos do diário.</returns>
    /// <exception cref="InvalidOperationException">O banco é a origem (defeito do motor).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, ou devolveu uma lista incoerente.</exception>
    internal IReadOnlyList<ObjetoReservado> ObjetosDoDiario()
    {
        const string Operacao = "dizer os objetos do diário";
        var destino = Destino("o diário só existe no destino");
        IReadOnlyList<ObjetoReservado>? objetos;
        try
        {
            objetos = destino.Diario.ObjetosDoDiario;
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, CancellationToken.None);
        }

        if (objetos is null)
        {
            throw SemResultado(_provedor, Operacao, _lado);
        }

        IReadOnlyList<ObjetoReservado> copia = [.. objetos];
        return copia.All(objeto => objeto is not null && string.Equals(objeto.Nome.Esquema, MontagemDoPlano.Reservado, StringComparison.Ordinal))
            && copia.Select(objeto => objeto.Nome).Distinct().Count() == copia.Count
            ? copia
            : throw Incoerente(Operacao);
    }

    /// <summary>Cria o diário que falta no destino (<c>preparar-destino</c>).</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="cancelamento">Cancela a criação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task CriarDiarioAsync(ConcessaoProtegida concessao, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "criar o diário", (diario, token) => diario.CriarAsync(token), cancelamento);

    /// <summary>
    /// Lê o diário do destino. O estado que se contradiz (situação ou tipo desconhecido, nome fora da chave, entrada
    /// repetida ou de execução não lida, chave antes de trigger, diário que não existe com entradas) é falha do dialeto.
    /// </summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O estado do diário.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, recusou (saída 1) ou leu um diário que se contradiz.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<EstadoDiario> LerDiarioAsync(ConcessaoProtegida concessao, CancellationToken cancelamento)
    {
        const string Operacao = "ler o diário";
        var estado = await NoDiarioAsync(concessao, Operacao, (diario, token) => diario.LerAsync(token), cancelamento).ConfigureAwait(false);
        return estado is null ? throw SemResultado(_provedor, Operacao, _lado)
            : Coerente(estado) ? estado
            : throw Incoerente(Operacao);
    }

    /// <summary>Registra uma execução em andamento no diário do destino.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="marcador">O identificador do marcador do destino.</param>
    /// <param name="iniciadaEm">Quando, em UTC.</param>
    /// <param name="cancelamento">Cancela o registro.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task IniciarExecucaoAsync(ConcessaoProtegida concessao, Guid execucao, Guid marcador, DateTimeOffset iniciadaEm, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "registrar a execução", (diario, token) => diario.IniciarAsync(execucao, marcador, iniciadaEm, token), cancelamento);

    /// <summary>Remove registrando uma chave estrangeira do destino (ou adota a entrada pendente dela).</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="execucao">A execução em andamento.</param>
    /// <param name="tabela">A tabela da chave, como o catálogo do destino a guarda.</param>
    /// <param name="chave">O nome da chave.</param>
    /// <param name="cancelamento">Cancela a remoção.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task RemoverChaveAsync(ConcessaoProtegida concessao, Guid execucao, NomeObjeto tabela, string chave, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "remover uma chave estrangeira", (diario, token) => diario.RemoverChaveAsync(execucao, tabela, chave, token), cancelamento);

    /// <summary>Desliga registrando as triggers de usuário de uma tabela do destino e das partições dela.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="execucao">A execução em andamento.</param>
    /// <param name="tabela">A tabela (numa particionada, a raiz).</param>
    /// <param name="cancelamento">Cancela o desligamento.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task DesligarTriggersAsync(ConcessaoProtegida concessao, Guid execucao, NomeObjeto tabela, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "desligar as triggers", (diario, token) => diario.DesligarTriggersAsync(execucao, tabela, token), cancelamento);

    /// <summary>Religa as triggers de uma entrada do diário no estado original.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="entrada">A entrada das triggers.</param>
    /// <param name="cancelamento">Cancela a restauração.</param>
    /// <returns>As triggers puladas (que não existem mais).</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, recusou (saída 1) ou devolveu uma lista incoerente.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<IReadOnlyList<TriggerDoDiario>> ReligarTriggersAsync(ConcessaoProtegida concessao, EntradaDiario entrada, CancellationToken cancelamento)
    {
        const string Operacao = "religar as triggers";
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.Tipo != TipoEntrada.Triggers)
        {
            throw new InvalidOperationException("Só uma entrada de triggers se religa.");
        }

        var puladas = await NoDiarioAsync(concessao, Operacao, (diario, token) => diario.ReligarTriggersAsync(entrada, token), cancelamento).ConfigureAwait(false);
        if (puladas is null)
        {
            throw SemResultado(_provedor, Operacao, _lado);
        }

        IReadOnlyList<TriggerDoDiario> copia = [.. puladas];
        return copia.All(trigger => trigger is not null) ? copia : throw Incoerente(Operacao);
    }

    /// <summary>Recria uma chave removida do destino.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="entrada">A entrada da chave.</param>
    /// <param name="cancelamento">Cancela a recriação.</param>
    /// <returns>A situação da entrada depois; nula quando o estado original voltou.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, recusou (saída 1) ou devolveu uma situação desconhecida.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task<SituacaoEntrada?> RecriarChaveAsync(ConcessaoProtegida concessao, EntradaDiario entrada, CancellationToken cancelamento) =>
        NaChaveAsync(concessao, entrada, "recriar uma chave estrangeira", (diario, token) => diario.RecriarChaveAsync(entrada, token), cancelamento);

    /// <summary>Valida uma chave recriada sem validação.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="entrada">A entrada da chave.</param>
    /// <param name="cancelamento">Cancela a validação.</param>
    /// <returns>A situação da entrada depois; nula quando validou.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou, recusou (saída 1) ou devolveu uma situação desconhecida.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task<SituacaoEntrada?> ValidarChaveAsync(ConcessaoProtegida concessao, EntradaDiario entrada, CancellationToken cancelamento) =>
        NaChaveAsync(concessao, entrada, "validar uma chave estrangeira", (diario, token) => diario.ValidarChaveAsync(entrada, token), cancelamento);

    /// <summary>Apaga uma entrada do diário sem restaurar (depois da confirmação de quem repara).</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="entrada">A entrada.</param>
    /// <param name="cancelamento">Cancela o descarte.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task DescartarEntradaAsync(ConcessaoProtegida concessao, EntradaDiario entrada, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "descartar uma entrada do diário", (diario, token) => diario.DescartarAsync(entrada, token), cancelamento);

    /// <summary>Grava a situação final de uma execução.</summary>
    /// <param name="concessao">A concessão viva do bloqueio deste destino.</param>
    /// <param name="execucao">A execução.</param>
    /// <param name="situacao">Concluída, falhou ou interrompida.</param>
    /// <param name="encerradaEm">Quando, em UTC.</param>
    /// <param name="cancelamento">Cancela a gravação.</param>
    /// <returns>A tarefa.</returns>
    /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou (saída 1).</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal Task EncerrarExecucaoAsync(ConcessaoProtegida concessao, Guid execucao, SituacaoExecucao situacao, DateTimeOffset encerradaEm, CancellationToken cancelamento) =>
        NoDiarioAsync(concessao, "encerrar a execução", (diario, token) => diario.EncerrarAsync(execucao, situacao, encerradaEm, token), cancelamento);

    /// <summary>
    /// Lê o maior valor de uma coluna inteira da origem. O valor é de coluna tratada: qualquer exceção sai só com o código
    /// ou o tipo, nunca com a mensagem do dialeto, nem a de uma recusa.
    /// </summary>
    /// <param name="coluna">A tabela e a coluna, como o catálogo da origem as guarda.</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>O maior valor; nulo quando nenhuma linha tem valor na coluna.</returns>
    /// <exception cref="InvalidOperationException">O banco é o destino: a leitura é só da origem (defeito do motor).</exception>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou recusou a leitura.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
    internal async Task<long?> LerMaiorValorAsync(ReferenciaColuna coluna, CancellationToken cancelamento)
    {
        const string Operacao = "ler o maior valor de uma coluna";
        if (_banco is not DialetoOrigem origem)
        {
            throw new InvalidOperationException("O maior valor de uma coluna só se lê na origem.");
        }

        try
        {
            return await origem.Dados.LerMaiorValorAsync(coluna, cancelamento).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            throw TraduzirSemMensagem(excecao, _provedor, Operacao, _lado, cancelamento);
        }
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

    /// <summary>Se um nome escrito designa um nome do catálogo, pela regra do banco desta conexão.</summary>
    /// <param name="escrito">O nome como foi escrito, sem as aspas.</param>
    /// <param name="entreAspas">Se foi escrito entre aspas.</param>
    /// <param name="doCatalogo">O nome como o catálogo o guarda.</param>
    /// <returns>A correspondência; um valor fora do enum vira falha do dialeto, nunca um palpite.</returns>
    /// <exception cref="FalhaDialetoException">O dialeto falhou ou devolveu um valor desconhecido.</exception>
    internal CorrespondenciaNome CompararNome(string escrito, bool entreAspas, string doCatalogo)
    {
        const string Operacao = "comparar um nome";
        CorrespondenciaNome correspondencia;
        try
        {
            correspondencia = _banco.Sql.CompararNome(escrito, entreAspas, doCatalogo);
        }
        catch (Exception excecao)
        {
            throw Traduzir(excecao, _provedor, Operacao, _lado, aceitaRecusa: false, CancellationToken.None);
        }

        return Enum.IsDefined(correspondencia)
            ? correspondencia
            : throw new FalhaDialetoException(CodigosPlanejar.FalhaDoDialeto, $"O dialeto de {_provedor} devolveu um resultado desconhecido ao {Operacao}{NoLado(_lado)}.");
    }

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

    /// <summary>
    /// A exceção que sai no lugar da que o dialeto lançou numa operação que lê dado de coluna: como em
    /// <see cref="Traduzir"/>, mas sem nunca ler a mensagem, nem a de uma recusa ou de uma <see cref="IncognitoException"/>
    /// de tipo exato, que o dialeto poderia ter escrito com o valor lido.
    /// </summary>
    /// <param name="excecao">A exceção do dialeto, que não sai daqui.</param>
    /// <param name="provedor">O nome do provedor na configuração.</param>
    /// <param name="operacao">O que o motor pedia, no infinitivo.</param>
    /// <param name="lado">O lado.</param>
    /// <param name="cancelamento">O token do chamador.</param>
    /// <param name="aceitaRecusa">Se a recusa vira erro do plano (saída 2), também sem a mensagem; senão, falha.</param>
    /// <returns>A exceção a lançar.</returns>
    internal static Exception TraduzirSemMensagem(Exception excecao, string provedor, string operacao, LadoBanco lado, CancellationToken cancelamento, bool aceitaRecusa = false)
    {
        if (cancelamento.IsCancellationRequested && SoCancelamento(excecao))
        {
            return new OperationCanceledException(cancelamento);
        }

        if (excecao is not IncognitoException incognito)
        {
            return FalhaSemMensagem(provedor, operacao, lado, excecao);
        }

        var codigo = incognito.Codigo;
        if (!CodigosPlanejar.DaFaixaDosDialetos(codigo))
        {
            return FalhaSemMensagem(provedor, operacao, lado, excecao);
        }

        return aceitaRecusa && excecao is DialetoRecusouException
            ? new PlanoRecusadoException([new ErroPlano(codigo, $"O dialeto de {provedor} recusou {operacao}{NoLado(lado)} com o código {codigo}.")])
            : new FalhaDialetoException(codigo, $"O dialeto de {provedor} falhou ao {operacao}{NoLado(lado)} com o código {codigo}.");
    }

    // O marcador coerente: a situação conhecida, e o identificador e a data, em UTC, só e sempre no válido.
    private static bool Coerente(MarcadorLido marcador) =>
        Enum.IsDefined(marcador.Situacao)
        && (marcador.Situacao == SituacaoMarcador.Valido
            ? marcador.Identificador is { } identificador && identificador != Guid.Empty && marcador.GravadoEm is { Offset.Ticks: 0 }
            : marcador.Identificador is null && marcador.GravadoEm is null);

    // O diário coerente: os enums conhecidos (um tipo novo é falha, nunca pulado), o nome só e sempre na chave, a situação
    // sem validação só na chave, nenhuma entrada repetida nem de execução não lida, as triggers antes das chaves, e nada
    // num diário que não existe.
    private static bool Coerente(EstadoDiario estado)
    {
        IReadOnlyList<ExecucaoLida> execucoes = [.. estado.Execucoes];
        IReadOnlyList<EntradaDiario> pendentes = [.. estado.Pendentes];
        if (!estado.Existe)
        {
            return execucoes.Count == 0 && pendentes.Count == 0;
        }

        var lidas = execucoes.Select(execucao => execucao.Id).ToHashSet();
        var semTriggers = false;
        return lidas.Count == execucoes.Count
            && execucoes.All(execucao => Enum.IsDefined(execucao.Situacao) && execucao.Id != Guid.Empty)
            && pendentes.Select(entrada => (entrada.Tipo, entrada.Tabela, entrada.Nome)).Distinct().Count() == pendentes.Count
            && pendentes.All(entrada =>
            {
                var chave = entrada.Tipo == TipoEntrada.ChaveEstrangeira;
                var naOrdem = chave || !semTriggers;
                semTriggers |= chave;
                return naOrdem
                    && Enum.IsDefined(entrada.Tipo)
                    && Enum.IsDefined(entrada.Situacao)
                    && lidas.Contains(entrada.Execucao)
                    && (chave ? !string.IsNullOrEmpty(entrada.Nome) : entrada.Nome is null && entrada.Situacao == SituacaoEntrada.Removida);
            });
    }

    private DialetoDestino Destino(string motivo) => _banco as DialetoDestino ?? throw new InvalidOperationException($"No envoltório da origem: {motivo}.");

    // Uma operação do diário: só com a concessão viva; o dialeto recebe o token do chamador ligado à perda do bloqueio, e a
    // perda sai como INC4001 (nunca como cancelamento do chamador, saída 130). Nenhuma mensagem do dialeto sai (a validação
    // traz o valor da chave órfã, e a definição guardada é texto do destino), e a recusa é falha, saída 1: o diário só
    // trabalha na execução, depois do plano.
    private async Task<T> NoDiarioAsync<T>(ConcessaoProtegida concessao, string operacao, Func<DiarioExecucao, CancellationToken, Task<T>> chamar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(concessao);
        var destino = Destino("o diário só existe no destino");
        concessao.LancarSePerdida();
        using var ligado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, concessao.Perdido);
        try
        {
            return await chamar(destino.Diario, ligado.Token).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            concessao.LancarSePerdida();
            throw TraduzirSemMensagem(excecao, _provedor, operacao, _lado, cancelamento);
        }
    }

    private async Task NoDiarioAsync(ConcessaoProtegida concessao, string operacao, Func<DiarioExecucao, CancellationToken, Task> chamar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(concessao);
        var destino = Destino("o diário só existe no destino");
        concessao.LancarSePerdida();
        using var ligado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, concessao.Perdido);
        try
        {
            await chamar(destino.Diario, ligado.Token).ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            concessao.LancarSePerdida();
            throw TraduzirSemMensagem(excecao, _provedor, operacao, _lado, cancelamento);
        }
    }

    // Uma operação sobre a entrada de uma chave, que devolve a situação dela depois: nula, removida ou sem validação.
    private async Task<SituacaoEntrada?> NaChaveAsync(
        ConcessaoProtegida concessao, EntradaDiario entrada, string operacao, Func<DiarioExecucao, CancellationToken, Task<SituacaoEntrada?>> chamar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.Tipo != TipoEntrada.ChaveEstrangeira)
        {
            throw new InvalidOperationException("Só uma entrada de chave estrangeira se recria ou se valida.");
        }

        var situacao = await NoDiarioAsync(concessao, operacao, chamar, cancelamento).ConfigureAwait(false);
        return situacao is null || Enum.IsDefined(situacao.Value) ? situacao : throw Incoerente(operacao);
    }

    private FalhaDialetoException Incoerente(string operacao) => new(
        CodigosPlanejar.EsquemaIncoerente,
        $"O dialeto de {_provedor} devolveu um resultado que se contradiz ao {operacao}{NoLado(_lado)}.");

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

    /// <summary>
    /// Uma concessão do bloqueio do destino, chamada só por aqui. A perda (a do dialeto, ou o batimento que falha) sai como
    /// <see cref="BloqueioDestinoException"/> com <c>INC4001</c> (saída 1), nunca como cancelamento do chamador nem como
    /// erro do plano; o <see cref="Perdido"/> daqui é cancelado fora da thread do dialeto (do driver); e o descarte libera
    /// uma vez e nunca lança, para não tomar o lugar do erro de quem o chama.
    /// </summary>
    internal sealed class ConcessaoProtegida : IAsyncDisposable
    {
        private readonly ConcessaoBloqueio _concessao;
        private readonly CancellationToken _doDialeto;
        private readonly CancellationTokenSource _perdido = new();
        private readonly CancellationTokenRegistration _registro;
        private int _liberada;

        internal ConcessaoProtegida(ConcessaoBloqueio concessao, CancellationToken doDialeto)
        {
            _concessao = concessao;
            _doDialeto = doDialeto;
            _registro = doDialeto.UnsafeRegister(static estado => _ = Task.Run(() => Cancelar((CancellationTokenSource)estado!)), _perdido);
        }

        /// <summary>Cancelado quando o bloqueio se perde, numa thread do motor.</summary>
        internal CancellationToken Perdido => _perdido.Token;

        /// <summary>O batimento antes de um passo crítico.</summary>
        /// <param name="cancelamento">O token do chamador (o cancelamento dele não é perda).</param>
        /// <returns>A tarefa.</returns>
        /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
        /// <exception cref="OperationCanceledException">Cancelado pelo chamador.</exception>
        /// <exception cref="ObjectDisposedException">A concessão já foi liberada (defeito do motor).</exception>
        internal async Task ConferirAsync(CancellationToken cancelamento)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _liberada) != 0, this);
            if (_doDialeto.IsCancellationRequested || _perdido.IsCancellationRequested)
            {
                throw Perda();
            }

            try
            {
                await _concessao.ConferirAsync(cancelamento).ConfigureAwait(false);
            }
            catch (Exception excecao) when (cancelamento.IsCancellationRequested && SoCancelamento(excecao))
            {
                throw new OperationCanceledException(cancelamento);
            }
            catch
            {
                Cancelar(_perdido);
                throw Perda();
            }
        }

        /// <summary>Lança a perda se o bloqueio já se perdeu (ou a concessão já foi liberada): antes e depois de cada operação do diário.</summary>
        /// <exception cref="BloqueioDestinoException">O bloqueio se perdeu (<c>INC4001</c>).</exception>
        /// <exception cref="ObjectDisposedException">A concessão já foi liberada (defeito do motor).</exception>
        internal void LancarSePerdida()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _liberada) != 0, this);
            if (_doDialeto.IsCancellationRequested || _perdido.IsCancellationRequested)
            {
                throw Perda();
            }
        }

        /// <summary>Libera o bloqueio, uma vez só; nunca lança.</summary>
        /// <returns>A tarefa do descarte.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "O descarte nunca toma o lugar do erro de quem o chama; a concessão do dialeto já marca a perda.")]
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _liberada, 1) != 0)
            {
                return;
            }

            await _registro.DisposeAsync().ConfigureAwait(false);
            try
            {
                await _concessao.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static BloqueioDestinoException Perda() => new(
            CodigosExecucao.BloqueioPerdido,
            "O bloqueio do destino se perdeu (a conexão do bloqueio caiu, ou o banco não o confirma): a execução para sem escrever mais nada no destino.");

        // Quem registrou no token é o motor; uma falha dele não derruba quem sinaliza.
        private static void Cancelar(CancellationTokenSource perdido)
        {
            try
            {
                perdido.Cancel();
            }
            catch (AggregateException)
            {
            }
        }
    }
}
