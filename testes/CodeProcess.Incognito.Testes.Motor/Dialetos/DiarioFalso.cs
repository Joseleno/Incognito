using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>Uma chave estrangeira do banco do diário falso: a definição opaca e o estado.</summary>
/// <param name="Definicao">A definição.</param>
/// <param name="Validada">Se foi validada.</param>
/// <param name="Imposta">Se é imposta.</param>
internal sealed record ChaveFalsa(string Definicao, bool Validada, bool Imposta);

/// <summary>Uma entrada do diário falso: a chave guardada, ou o estado das triggers de cada tabela física.</summary>
internal sealed class EntradaFalsa
{
    internal required Guid Execucao { get; set; }

    internal required SituacaoEntrada Situacao { get; set; }

    internal ChaveFalsa? Chave { get; init; }

    internal Dictionary<(NomeObjeto Tabela, string Nome), char>? Triggers { get; init; }
}

/// <summary>
/// O banco do diário falso, dividido pelas instâncias conectadas a ele: as chaves e as triggers dos casos, o diário, e as
/// falhas e adulterações que a fixture injeta.
/// </summary>
internal sealed class EstadoDoDiarioFalso
{
    /// <summary>A trava de todas as operações.</summary>
    internal Lock Trava { get; } = new();

    internal bool Criado { get; set; }

    internal Dictionary<(NomeObjeto Tabela, string Nome), ChaveFalsa> Chaves { get; } = new()
    {
        [(BancoFalso.Nome("vendas"), "fk_vendas_lojas")] = new("chave (loja) de loja.lojas (id) atualizar anula, apagar propaga, adiavel", true, true),
        [(BancoFalso.Nome("vendas"), "fk_vendas_par")] = new("chave (a, b) de loja.lojas (a, b) completa", true, true),
        [(BancoFalso.Nome("vendas"), "fk_vendas_codigo")] = new("chave (codigo) de loja.lojas (codigo)", true, true),
        [(BancoFalso.Nome("vendas"), "fk_vendas_pendente")] = new("chave (loja) de loja.lojas (id)", false, true),
        [(BancoFalso.Nome("pedidos"), "fk_pedidos_lojas")] = new("chave (loja) de loja.lojas (id)", true, true),
        [(BancoFalso.Nome("vinculo"), "fk_vinculo_solto")] = new("chave (loja) de loja.lojas (id)", false, false),
        [(BancoFalso.Nome("compras"), "fk_compras_lojas")] = new("chave (loja) de loja.lojas (id) apagar restringe", true, true),
    };

    /// <summary>As triggers de usuário por tabela física, com o estado (O, D, R ou A).</summary>
    internal Dictionary<(NomeObjeto Tabela, string Nome), char> Triggers { get; } = new()
    {
        [(BancoFalso.Nome("disparos"), "tg_ligada")] = 'O',
        [(BancoFalso.Nome("disparos"), "tg_desligada")] = 'D',
        [(BancoFalso.Nome("disparos"), "tg_replica")] = 'R',
        [(BancoFalso.Nome("disparos"), "tg_sempre")] = 'A',
        [(BancoFalso.Nome("pedidos"), "tg_pedidos")] = 'O',
        [(BancoFalso.Nome("pedidos_1"), "tg_pedidos")] = 'O',
        [(BancoFalso.Nome("pedidos_1"), "tg_pedidos_1_propria")] = 'O',
        [(BancoFalso.Nome("pedidos_2"), "tg_pedidos")] = 'D',
        [(BancoFalso.Nome("pessoa"), "tg_pessoa")] = 'O',
        [(BancoFalso.Nome("cliente"), "tg_cliente")] = 'O',
    };

    /// <summary>As tabelas físicas de cada árvore (a herdeira de herança clássica é árvore própria).</summary>
    internal Dictionary<NomeObjeto, NomeObjeto[]> Arvores { get; } = new()
    {
        [BancoFalso.Nome("disparos")] = [BancoFalso.Nome("disparos")],
        [BancoFalso.Nome("pedidos")] = [BancoFalso.Nome("pedidos"), BancoFalso.Nome("pedidos_1"), BancoFalso.Nome("pedidos_2")],
        [BancoFalso.Nome("pessoa")] = [BancoFalso.Nome("pessoa")],
        [BancoFalso.Nome("cliente")] = [BancoFalso.Nome("cliente")],
    };

    internal HashSet<(NomeObjeto Tabela, string Nome)> ComOrfao { get; } = [];

    internal List<(Guid Id, Guid Marcador, DateTimeOffset Inicio, SituacaoExecucao Situacao)> Execucoes { get; } = [];

    internal Dictionary<(TipoEntrada Tipo, NomeObjeto Tabela, string? Nome), EntradaFalsa> Entradas { get; } = [];

    internal bool FalharNoFim { get; set; }

    internal HashSet<NomeObjeto> Seguradas { get; } = [];

    internal HashSet<Adulteracao> Adulteracoes { get; } = [];
}

/// <summary>O diário falso, com os defeitos de cada regra do contrato.</summary>
internal sealed class DiarioFalso(EstadoDoDiarioFalso estado, BloqueioFalso bloqueio, Defeito defeito) : DiarioExecucao
{
    /// <summary>O que o diário falso cria no esquema reservado.</summary>
    internal static readonly ObjetoReservado[] Criados =
    [
        new() { Nome = new NomeObjeto("incognito", "execucao"), Tipo = TipoObjetoReservado.Tabela },
        new() { Nome = new NomeObjeto("incognito", "restricao_removida"), Tipo = TipoObjetoReservado.Tabela },
    ];

    // Sem persistir: a criação fica só nesta instância.
    private bool _criadoAqui;

    public override IReadOnlyList<ObjetoReservado> ObjetosDoDiario => defeito == Defeito.ObjetosDoDiarioAMais
        ? [.. Criados, new() { Nome = new NomeObjeto("incognito", "indice_extra"), Tipo = TipoObjetoReservado.Indice }]
        : Criados;

    public override Task CriarAsync(CancellationToken cancelamento) => Fazer(exigeDiario: false, () =>
    {
        if (defeito == Defeito.DiarioNaoPersiste)
        {
            _criadoAqui = true;
        }
        else
        {
            estado.Criado = true;
        }
    });

    public override Task<EstadoDiario> LerAsync(CancellationToken cancelamento) => Fazer(exigeDiario: false, () =>
    {
        if (!Criado)
        {
            return new EstadoDiario { Existe = false };
        }

        var referidas = estado.Entradas.Values.Select(entrada => entrada.Execucao).ToHashSet();
        return new EstadoDiario
        {
            Existe = true,
            Execucoes =
            [
                .. estado.Execucoes
                    .Where(execucao => execucao.Situacao == SituacaoExecucao.EmAndamento || referidas.Contains(execucao.Id))
                    .Select(execucao => new ExecucaoLida { Id = execucao.Id, Marcador = execucao.Marcador, IniciadaEm = execucao.Inicio, Situacao = execucao.Situacao }),
            ],
            Pendentes =
            [
                .. estado.Entradas
                    .OrderBy(entrada => entrada.Key.Tipo == TipoEntrada.ChaveEstrangeira)
                    .ThenBy(entrada => entrada.Key.Tabela.Esquema, StringComparer.Ordinal)
                    .ThenBy(entrada => entrada.Key.Tabela.Nome, StringComparer.Ordinal)
                    .ThenBy(entrada => entrada.Key.Nome, StringComparer.Ordinal)
                    .Select(entrada => new EntradaDiario
                    {
                        Execucao = entrada.Value.Execucao,
                        Tipo = entrada.Key.Tipo,
                        Tabela = entrada.Key.Tabela,
                        Nome = entrada.Key.Nome,
                        Situacao = entrada.Value.Situacao,
                    }),
            ],
        };
    });

    public override Task IniciarAsync(Guid execucao, Guid marcador, DateTimeOffset iniciadaEm, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        if (estado.Execucoes.Any(lida => lida.Situacao == SituacaoExecucao.EmAndamento) && defeito != Defeito.ExecucaoDupla)
        {
            throw Recusa("Há execução em andamento.");
        }

        estado.Execucoes.Add((execucao, marcador, iniciadaEm, SituacaoExecucao.EmAndamento));
    });

    public override Task RemoverChaveAsync(Guid execucao, NomeObjeto tabela, string chave, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        ExigirEmAndamento(execucao);
        Segurada(tabela);
        var chaveDoBanco = estado.Chaves.GetValueOrDefault((tabela, chave));
        var entrada = estado.Entradas.GetValueOrDefault((TipoEntrada.ChaveEstrangeira, tabela, chave));
        if (entrada is not null)
        {
            if (defeito == Defeito.NaoAdota)
            {
                throw Recusa("A chave já está no diário.");
            }

            if (chaveDoBanco is not null && chaveDoBanco.Definicao != entrada.Chave!.Definicao)
            {
                throw Recusa("Outra definição.");
            }

            FalharSeInjetado(() => { });
            estado.Chaves.Remove((tabela, chave));
            (entrada.Execucao, entrada.Situacao) = (execucao, SituacaoEntrada.Removida);
            return;
        }

        if (chaveDoBanco is null)
        {
            throw Recusa("A chave não existe.");
        }

        // Com a falha injetada no fim do comando, nada fica; no defeito, a chave sai sem o registro.
        FalharSeInjetado(() =>
        {
            if (defeito == Defeito.RemocaoSemRegistro)
            {
                estado.Chaves.Remove((tabela, chave));
            }
        });
        estado.Entradas[(TipoEntrada.ChaveEstrangeira, tabela, chave)] = new() { Execucao = execucao, Situacao = SituacaoEntrada.Removida, Chave = chaveDoBanco };
        estado.Chaves.Remove((tabela, chave));
    });

    public override Task DesligarTriggersAsync(Guid execucao, NomeObjeto tabela, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        ExigirEmAndamento(execucao);
        Segurada(tabela);
        if (estado.Entradas.ContainsKey((TipoEntrada.Triggers, tabela, null)))
        {
            throw Recusa("A tabela já tem as triggers no diário.");
        }

        var fisicas = estado.Arvores.GetValueOrDefault(tabela) ?? throw Recusa("A tabela não existe.");
        var guardadas = estado.Triggers.Where(trigger => fisicas.Contains(trigger.Key.Tabela)).ToDictionary();
        FalharSeInjetado(() => { });
        estado.Entradas[(TipoEntrada.Triggers, tabela, null)] = new() { Execucao = execucao, Situacao = SituacaoEntrada.Removida, Triggers = guardadas };
        foreach (var trigger in guardadas.Keys)
        {
            estado.Triggers[trigger] = 'D';
        }
    });

    public override Task<IReadOnlyList<TriggerDoDiario>> ReligarTriggersAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer<IReadOnlyList<TriggerDoDiario>>(exigeDiario: true, () =>
    {
        if (!estado.Entradas.Remove((TipoEntrada.Triggers, entrada.Tabela, null), out var guardada))
        {
            return [];
        }

        var puladas = new List<TriggerDoDiario>();
        foreach (var ((tabela, nome), estadoOriginal) in guardada.Triggers!)
        {
            if (!estado.Triggers.ContainsKey((tabela, nome)))
            {
                puladas.Add(new() { Tabela = tabela, Nome = nome });
                continue;
            }

            // No defeito, a restauração desce da raiz: a cópia desligada numa partição volta ligada.
            var raiz = guardada.Triggers.GetValueOrDefault((entrada.Tabela, nome), estadoOriginal);
            estado.Triggers[(tabela, nome)] = defeito == Defeito.TriggersPelaRaiz ? raiz : estadoOriginal;
        }

        return puladas;
    });

    public override Task<SituacaoEntrada?> RecriarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        var chave = (entrada.Tabela, entrada.Nome!);
        if (estado.Entradas.GetValueOrDefault((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome)) is not { Chave: { } guardada } registro)
        {
            return default(SituacaoEntrada?);
        }

        if (estado.Chaves.TryGetValue(chave, out var existente))
        {
            if (existente.Definicao != guardada.Definicao && defeito != Defeito.SobrescreveOutraDefinicao)
            {
                throw Recusa("Outra definição.");
            }

            // No defeito, a chave que já voltou dá a entrada por restaurada, sem validar.
            if (defeito == Defeito.RecriacaoNaoIdempotente)
            {
                estado.Entradas.Remove((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome));
                return null;
            }

            if (existente.Definicao == guardada.Definicao && existente.Validada == guardada.Validada)
            {
                estado.Entradas.Remove((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome));
                return null;
            }

            if (existente.Definicao == guardada.Definicao)
            {
                registro.Situacao = SituacaoEntrada.SemValidacao;
                return SituacaoEntrada.SemValidacao;
            }
        }

        FalharSeInjetado(() => { });
        var semValidar = guardada.Validada;
        estado.Chaves[chave] = guardada with { Validada = defeito == Defeito.RecriaValidada || (guardada.Validada && !semValidar) };
        if (semValidar && defeito != Defeito.RecriaSemSituacao)
        {
            registro.Situacao = SituacaoEntrada.SemValidacao;
            return SituacaoEntrada.SemValidacao;
        }

        estado.Entradas.Remove((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome));
        return (SituacaoEntrada?)null;
    });

    public override Task<SituacaoEntrada?> ValidarChaveAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        var chave = (entrada.Tabela, entrada.Nome!);
        if (estado.Entradas.GetValueOrDefault((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome)) is not { } registro)
        {
            return default(SituacaoEntrada?);
        }

        if (!estado.Chaves.TryGetValue(chave, out var existente))
        {
            registro.Situacao = SituacaoEntrada.Removida;
            return SituacaoEntrada.Removida;
        }

        if (estado.ComOrfao.Contains(chave) && defeito != Defeito.OrfaoValida)
        {
            return SituacaoEntrada.SemValidacao;
        }

        estado.Chaves[chave] = existente with { Validada = true };
        estado.Entradas.Remove((TipoEntrada.ChaveEstrangeira, entrada.Tabela, entrada.Nome));
        return (SituacaoEntrada?)null;
    });

    public override Task DescartarAsync(EntradaDiario entrada, CancellationToken cancelamento) => Fazer(exigeDiario: true, () =>
    {
        estado.Entradas.Remove((entrada.Tipo, entrada.Tabela, entrada.Tipo == TipoEntrada.Triggers ? null : entrada.Nome));
    });

    public override Task EncerrarAsync(Guid execucao, SituacaoExecucao situacao, DateTimeOffset encerradaEm, CancellationToken cancelamento)
    {
        if (situacao == SituacaoExecucao.EmAndamento)
        {
            throw new ArgumentOutOfRangeException(nameof(situacao));
        }

        return Fazer(exigeDiario: true, () =>
        {
            var indice = estado.Execucoes.FindIndex(lida => lida.Id == execucao && lida.Situacao == SituacaoExecucao.EmAndamento);
            if (indice < 0)
            {
                throw Recusa("A execução não está em andamento.");
            }

            estado.Execucoes[indice] = estado.Execucoes[indice] with { Situacao = situacao };
        });
    }

    private bool Criado => estado.Criado || _criadoAqui;

    private static DialetoRecusouException Recusa(string mensagem) => new("INC3399", "Diário do falso: " + mensagem);

    // Uma operação do diário: só com a concessão viva (que se perde se o banco não a reconhece mais), e, quando precisa
    // dele, com o diário criado e não adulterado.
    private Task<T> Fazer<T>(bool exigeDiario, Func<T> operacao)
    {
        try
        {
            lock (estado.Trava)
            {
                if (defeito != Defeito.DiarioSemConcessao)
                {
                    var concessao = bloqueio.Viva ?? throw new InvalidOperationException("O diário só trabalha com a concessão viva.");
                    if (!concessao.Viva)
                    {
                        concessao.MarcarPerda();
                        throw new IncognitoException("INC3399", "O bloqueio se perdeu.");
                    }
                }

                if (Criado && estado.Adulteracoes.Count > 0 && defeito != Defeito.AdulteracaoAceita)
                {
                    throw Recusa("O diário não é o esperado.");
                }

                if (exigeDiario && !Criado)
                {
                    throw Recusa("O diário não existe.");
                }

                return Task.FromResult(operacao());
            }
        }
        catch (Exception excecao)
        {
            return Task.FromException<T>(excecao);
        }
    }

    private Task<bool> Fazer(bool exigeDiario, Action operacao) => Fazer(exigeDiario, () =>
    {
        operacao();
        return true;
    });

    private void ExigirEmAndamento(Guid execucao)
    {
        if (!estado.Execucoes.Any(lida => lida.Id == execucao && lida.Situacao == SituacaoExecucao.EmAndamento))
        {
            throw Recusa("A execução não está em andamento.");
        }
    }

    private void Segurada(NomeObjeto tabela)
    {
        if (estado.Seguradas.Contains(tabela))
        {
            throw new IncognitoException("INC3399", "Outra sessão segurava a tabela.");
        }
    }

    // A alteração que falha no fim do comando: nada da operação fica (o que o defeito faz antes, fica).
    private void FalharSeInjetado(Action antes)
    {
        if (estado.FalharNoFim)
        {
            antes();
            throw new IncognitoException("INC3399", "A alteração falhou no fim do comando.");
        }
    }
}

/// <summary>O leitor do banco do diário falso: só o esquema reservado, com o que o diário criou.</summary>
internal sealed class LeitorDoDiarioFalso(EstadoDoDiarioFalso estado) : LeitorEsquema
{
    public override Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        lock (estado.Trava)
        {
            return Task.FromResult(new EsquemaBanco { Tabelas = [], ObjetosNoEsquemaReservado = estado.Criado ? DiarioFalso.Criados : [], TriggersDeEvento = [] });
        }
    }
}
