using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O executor (seção 4.3): a ordem dos passos, a recusa antes de qualquer escrita da cópia (C4, com o diário vazio e com a
/// pendência de uma execução interrompida), a trigger que a execução interrompida deixou desligada (D5), as chaves adotadas,
/// a chave do resolvedor e o descarte uma vez (C3), a falha no meio, o Ctrl+C e a perda do bloqueio, e o resultado depois do
/// preparo (D10).
/// </summary>
public sealed class ExecutorInternoTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    private static readonly Guid _anterior = new("0b5f2d8e-3c41-4a97-9e6d-7a1c2b3d4e5f");

    // Todo membro de escrita do dialeto: o do diário e o do gravador (o falso do gravador já é conferido pelos pedidos).
    private static readonly Operacao[] _escritas =
    [
        Operacao.IniciarExecucao, Operacao.RemoverChave, Operacao.DesligarTriggers, Operacao.ReligarTriggers, Operacao.RecriarChave,
        Operacao.ValidarChave, Operacao.DescartarEntrada, Operacao.EncerrarExecucao, Operacao.GravarMarcador, Operacao.CriarDiario,
    ];

    // As do reparo de uma execução interrompida: religar, recriar sem validar e marcar como interrompida.
    private static readonly Operacao[] _doReparo = [Operacao.ReligarTriggers, Operacao.RecriarChave, Operacao.EncerrarExecucao];

    [Fact]
    public async Task Caminho_feliz_na_ordem_dos_passos_com_o_resultado_e_a_restauracao_validando()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(0, resultado.Saida);
        Assert.Null(resultado.Falha);
        Assert.False(resultado.Negada);
        Assert.Equal((ConsistenciaObtida.Instantaneo, (long?)7), (resultado.Consistencia, resultado.IdadeDoInstante));
        Assert.Equal(["clientes", "pedidos"], resultado.Tabelas.Select(tabela => tabela.Nome.Tabela).Order(StringComparer.Ordinal));
        Assert.All(cenario.Gravador.Confirmadas["clientes"], linha => Assert.Equal("TRATADO", linha[1]));
        Assert.Equal(3, cenario.Gravador.Confirmadas["pedidos"].Count);
        Assert.NotNull(resultado.ImpressaoDigital);
        Assert.DoesNotContain(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.DestinoIncompleto);

        // Proteção, reparo, leitura, preparo do diário, cópia, restauração (com a validação) e encerramento, nessa ordem.
        var chamadas = cenario.Provedor.Chamadas;
        Assert.True(Antes(chamadas, "AdquirirBloqueio:destino", "IniciarExecucao:destino"));
        Assert.True(Antes(chamadas, "IniciarExecucao:destino", "RemoverChave:destino"));
        Assert.True(Antes(chamadas, "RemoverChave:destino", "ValidarChave:destino"));
        Assert.True(Antes(chamadas, "ValidarChave:destino", "EncerrarExecucao:destino"));
        cenario.Antes("RemoverChave", "abrir:clientes");
        cenario.Antes("abrir:clientes", "ValidarChave");
        Assert.Single(cenario.EscritasDoDiario(), chamada => chamada == "RemoverChave:destino");
        Assert.Equal(SituacaoExecucao.Concluida, cenario.Encerrada);
        Assert.Equal((3, 3), (cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes));
        Assert.Contains("LiberarBloqueio:destino", chamadas);
    }

    [Fact]
    public async Task Confirmacao_negada_nao_escreve_nada()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();

        var resultado = await cenario.ExecutarAsync(confirmar: false);

        Assert.True(resultado.Negada);
        Assert.Equal(0, resultado.Saida);
        Assert.Empty(cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
    }

    [Fact]
    public async Task Banco_mudado_com_o_diario_vazio_recusa_com_2_sem_nenhum_membro_de_escrita()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            DestinoNaExecucao = Encolhido(),
            Lancar = (operacao, _) => _escritas.Contains(operacao) ? new InvalidOperationException("escrita: " + operacao) : null,
        });

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => cenario.ExecutarAsync());

        Assert.Equal("INC3005", recusa.Erros[0].Codigo);
        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.Empty(cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
        Assert.Equal(2, cenario.Fabrica.Criacoes);
        Assert.Equal(cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes);
        Assert.Contains("LiberarBloqueio:destino", cenario.Provedor.Chamadas);
    }

    [Fact]
    public async Task Banco_mudado_com_pendencia_recusa_com_2_e_so_o_reparo_escreve()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            DestinoNaExecucao = Encolhido(),
            // A chave já recriada sem validação fica como está: o reparo do executar não valida (D9).
            DiarioAntes = Interrompida(
                SituacaoExecucao.EmAndamento,
                Pendente(TipoEntrada.Triggers, "clientes", null),
                Pendente(TipoEntrada.ChaveEstrangeira, "pedidos", "fk_pedidos_clientes"),
                Pendente(TipoEntrada.ChaveEstrangeira, "pedidos", "fk_antiga", SituacaoEntrada.SemValidacao)),
            Lancar = (operacao, _) => _escritas.Contains(operacao) && !_doReparo.Contains(operacao) ? new InvalidOperationException("escrita: " + operacao) : null,
        });

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => cenario.ExecutarAsync());

        Assert.Equal("INC3005", recusa.Erros[0].Codigo);
        Assert.Equal(["ReligarTriggers:destino", "RecriarChave:destino", "EncerrarExecucao:destino"], cenario.EscritasDoDiario());
        Assert.Equal(SituacaoExecucao.Interrompida, cenario.Encerrada);
        cenario.NenhumaEscritaDaCopia();
    }

    [Fact]
    public async Task Trigger_que_a_execucao_interrompida_deixou_desligada_volta_no_reparo_e_e_desligada_de_novo_antes_da_carga()
    {
        // O planejar viu a trigger desligada (a execução interrompida a desligou) e não a pôs na lista; o reparo a religa, e o
        // replanejamento, com o destino relido, a desliga de novo antes de abrir a tabela (D5): sem isso, a trigger
        // AFTER TRUNCATE dispararia na carga.
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            DestinoNoPlanejar = ComTrigger(ligada: false),
            DestinoNaExecucao = ComTrigger(ligada: true),
            DiarioAntes = Interrompida(SituacaoExecucao.EmAndamento, Pendente(TipoEntrada.Triggers, "clientes", null)),
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(0, resultado.Saida);
        Assert.DoesNotContain(new NomeObjeto("loja", "clientes"), cenario.Plano.Execucao.TriggersDesligadas);
        cenario.Antes("ReligarTriggers", "IniciarExecucao");
        cenario.Antes("IniciarExecucao", "DesligarTriggers");
        cenario.Antes("DesligarTriggers", "abrir:clientes");
        Assert.Single(cenario.EscritasDoDiario(), chamada => chamada == "DesligarTriggers:destino");
        lock (cenario.Provedor.Chamadas)
        {
            Assert.True(cenario.Provedor.Chamadas.LastIndexOf("LerEsquema:destino") > cenario.Provedor.Chamadas.IndexOf("ReligarTriggers:destino"), "o destino é relido depois do reparo");
        }
    }

    [Fact]
    public async Task A_chave_que_o_diario_ainda_guarda_e_adotada_e_removida_antes_da_carga()
    {
        // O reparo recria a chave removida da execução anterior sem validar; ela continua pendente e a execução nova a remove
        // junto com as da carga.
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            DiarioAntes = Interrompida(SituacaoExecucao.Interrompida, Pendente(TipoEntrada.ChaveEstrangeira, "pedidos", "fk_antiga")),
            Antigas = [Pendente(TipoEntrada.ChaveEstrangeira, "pedidos", "fk_antiga", SituacaoEntrada.SemValidacao)],
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(0, resultado.Saida);
        Assert.Equal(["RecriarChave:destino", "IniciarExecucao:destino", "RemoverChave:destino", "RemoverChave:destino"], cenario.EscritasDoDiario().Take(4));
        cenario.Antes("RecriarChave", "IniciarExecucao");
        cenario.Antes("RemoverChave", "abrir:pedidos");
    }

    [Fact]
    public async Task A_tabela_ignorada_com_linhas_no_destino_recusa_na_protecao_sem_escrever()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Tabelas = OpcoesDoCenario.ComFuncao + "  loja.pedidos: { acao: ignorar }\n",
            Linhas = tabela => tabela.Equals(Nome("pedidos")),
        });

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => cenario.ExecutarAsync());

        Assert.Equal(("INC3604", "loja.pedidos"), (Assert.Single(recusa.Erros).Codigo, recusa.Erros[0].Tabela?.ToString()));
        Assert.Empty(cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
    }

    [Fact]
    public async Task No_efemero_a_chave_e_gerada_sem_o_resolvedor_e_o_resultado_nao_tem_impressao_digital()
    {
        var efemero = Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", null).Replace("modo: reproduzivel", "modo: efemero", StringComparison.Ordinal);
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario { Base = efemero });

        var resultado = await cenario.ExecutarAsync(resolverChave: _ => throw new InvalidOperationException("o efêmero não resolve a chave"));

        Assert.Equal(0, resultado.Saida);
        Assert.Null(resultado.ImpressaoDigital);
    }

    [Fact]
    public async Task A_falha_no_descarte_de_um_transformador_de_fora_vira_aviso_no_resultado_sem_mudar_a_saida()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();
        var descartes = 0;

        // O primeiro descarte depois do planejar é o do replanejamento; o segundo, o da execução.
        cenario.Fabrica.AoDescartar = () =>
        {
            if (Interlocked.Increment(ref descartes) == 2)
            {
                throw new InvalidOperationException(Sentinela);
            }
        };

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(0, resultado.Saida);
        var aviso = Assert.Single(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.DescarteDoTransformador);
        Assert.DoesNotContain(Sentinela, aviso.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, descartes);
    }

    [Fact]
    public async Task Ctrl_C_durante_a_validacao_do_caminho_feliz_restaura_de_novo_sem_validar_com_o_token_proprio()
    {
        using var usuario = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            NoDiario = async operacao =>
            {
                if (operacao == Operacao.ValidarChave)
                {
                    await usuario.CancelAsync();
                }
            },
        });

        var resultado = await cenario.ExecutarAsync(cancelamento: usuario.Token);

        Assert.IsType<OperationCanceledException>(resultado.Falha);
        Assert.Equal(130, resultado.Saida);
        Assert.Equal(SituacaoExecucao.Falhou, cenario.Encerrada);
        Assert.NotNull(resultado.Restauracao);
        // A segunda volta não valida de novo e encerra uma vez, como falhou.
        Assert.Single(cenario.EscritasDoDiario(), chamada => chamada == "ValidarChave:destino");
        Assert.Single(cenario.EscritasDoDiario(), chamada => chamada == "EncerrarExecucao:destino");
        Assert.Contains(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.DestinoIncompleto);
    }

    [Fact]
    public async Task Com_o_token_proprio_ja_cancelado_a_restauracao_para_e_o_aviso_manda_reparar()
    {
        using var usuario = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var restauracao = new CancellationTokenSource();
        await restauracao.CancelAsync();
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    usuario.Cancel();
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
        });

        var resultado = await cenario.ExecutarAsync(cancelamento: usuario.Token, restauracao: restauracao.Token);

        Assert.Equal(130, resultado.Saida);
        Assert.Null(cenario.Encerrada);
        var aviso = Assert.Single(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.RestauracaoIncompleta);
        Assert.Contains("reparar", aviso.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bloqueio_perdido_junto_com_outra_falha_sai_como_perda_e_nada_mais_e_escrito()
    {
        CenarioDaExecucao? cenario = null;
        cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    cenario!.Provedor.Concessoes.Single().Perder();
                    throw new FormatException(Sentinela);
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.IsType<BloqueioDestinoException>(resultado.Falha);
        Assert.Null(resultado.Restauracao);
        Assert.Null(cenario.Encerrada);
        Assert.DoesNotContain("ReligarTriggers:destino", cenario.Provedor.Chamadas);
    }

    [Fact]
    public async Task Bloqueio_perdido_depois_da_copia_que_falhou_sai_como_perda_sem_restaurar()
    {
        // A perda só é percebida no descarte da sessão de leitura, depois de a cópia registrar a outra falha.
        CenarioDaExecucao? cenario = null;
        var falhou = false;
        cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    falhou = true;
                    throw new FormatException(Sentinela);
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
            AoSoltarSessao = () =>
            {
                if (falhou)
                {
                    cenario!.Provedor.Concessoes.Single().Perder();
                }
            },
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.IsType<BloqueioDestinoException>(resultado.Falha);
        Assert.Null(resultado.Restauracao);
        Assert.Null(cenario.Encerrada);
        Assert.DoesNotContain(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.RestauracaoIncompleta);
    }

    [Fact]
    public async Task Bloqueio_perdido_na_restauracao_depois_de_uma_falha_sai_como_perda()
    {
        CenarioDaExecucao? cenario = null;
        var falhou = false;
        cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    falhou = true;
                    throw new FormatException(Sentinela);
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
            NoDiario = operacao =>
            {
                if (falhou && operacao == Operacao.LerDiario)
                {
                    cenario!.Provedor.Concessoes.Single().Perder();
                }

                return Task.CompletedTask;
            },
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.IsType<BloqueioDestinoException>(resultado.Falha);
        Assert.Null(cenario.Encerrada);
        Assert.Equal(1, resultado.Saida);
    }

    [Fact]
    public async Task Reparo_do_comeco_que_nao_restaura_recusa_com_1_antes_de_qualquer_escrita_da_copia()
    {
        // A trigger que a execução interrompida desligou não religa: a cópia não começa sobre um destino que o reparo não fechou.
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            DiarioAntes = Interrompida(SituacaoExecucao.EmAndamento, Pendente(TipoEntrada.Triggers, "clientes", null)),
            Lancar = (operacao, _) => operacao == Operacao.ReligarTriggers ? new ExcecaoDoDriver() : null,
        });

        var falha = await Assert.ThrowsAsync<IncognitoException>(() => cenario.ExecutarAsync());

        Assert.Equal((CodigosExecucao.ReparoIncompleto, 1), (falha.Codigo, ClassificadorDeSaida.De(falha)));
        Assert.Contains("reparar", falha.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IniciarExecucao:destino", cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
        Assert.Contains("LiberarBloqueio:destino", cenario.Provedor.Chamadas);
        DialetoProtegidoTestes.SemVazamento(falha);
    }

    [Fact]
    public async Task A_falha_na_medida_da_idade_do_instante_vira_aviso_e_a_execucao_conclui()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario { NaIdade = new ExcecaoDoDriver() });

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(0, resultado.Saida);
        Assert.Null(resultado.IdadeDoInstante);
        Assert.Equal(SituacaoExecucao.Concluida, cenario.Encerrada);
        Assert.Contains("ValidarChave:destino", cenario.Provedor.Chamadas);
        var aviso = Assert.Single(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.IdadeNaoMedida);
        Assert.Contains("(FalhaDialetoException)", aviso.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, aviso.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_transformadores_sao_descartados_antes_da_restauracao()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();
        cenario.Fabrica.AoDescartar = () => cenario.Linha.Enqueue("descarte");

        var resultado = await cenario.ExecutarAsync();

        var linha = cenario.Linha.ToList();
        Assert.Equal(0, resultado.Saida);
        Assert.True(linha.LastIndexOf("descarte") < linha.IndexOf("ValidarChave"), string.Join(" | ", linha));
    }

    [Fact]
    public async Task Fabrica_que_lanca_so_na_execucao_recusa_com_2_antes_do_diario_e_solta_tudo()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();
        cenario.Fabrica.NaCriacao = numero => numero == 3 ? new InvalidOperationException(Sentinela) : null;

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => cenario.ExecutarAsync());

        Assert.Equal((CodigosPlanejar.FalhaDaFabrica, 2), (Assert.Single(recusa.Erros).Codigo, ClassificadorDeSaida.De(recusa)));
        Assert.Empty(cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
        Assert.Contains("LiberarBloqueio:destino", cenario.Provedor.Chamadas);
        DialetoProtegidoTestes.SemVazamento(recusa);
    }

    [Fact]
    public async Task O_replanejamento_usa_o_esquema_relido_no_instante_da_leitura()
    {
        // A origem fora do instante continua igual; só a sessão vê a coluna encolhida.
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario { EsquemaDaSessao = Encolhido() });

        var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => cenario.ExecutarAsync());

        Assert.Equal(2, ClassificadorDeSaida.De(recusa));
        Assert.Empty(cenario.EscritasDoDiario());
        cenario.NenhumaEscritaDaCopia();
    }

    [Fact]
    public async Task Tratamentos_abaixo_de_1_nas_opcoes_valem_1()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();

        var resultado = await cenario.ExecutarAsync(tratamentos: 0);

        Assert.Equal(0, resultado.Saida);
        Assert.All(cenario.Gravador.Confirmadas["clientes"], linha => Assert.Equal("TRATADO", linha[1]));
    }

    [Fact]
    public async Task A_chave_vem_do_resolvedor_uma_vez_e_a_que_nao_se_resolve_sai_com_2_sem_escrever()
    {
        var cenario = await CenarioDaExecucao.CriarAsync();
        var pedidas = 0;

        var resultado = await cenario.ExecutarAsync(resolverChave: _ =>
        {
            pedidas++;
            return ChaveIncognito.Gerar();
        });
        var semChave = await CenarioDaExecucao.CriarAsync();
        var recusa = await Assert.ThrowsAsync<ConfiguracaoInvalidaException>(() => semChave.ExecutarAsync(resolverChave: _ => null));

        Assert.Equal(1, pedidas);
        Assert.Equal(0, resultado.Saida);
        Assert.Equal("INC2012", recusa.Erros[0].Codigo);
        semChave.NenhumaEscritaDaCopia();
    }

    [Fact]
    public async Task Falha_no_meio_devolve_o_resultado_com_a_falha_restaura_sem_validar_e_encerra_como_falhou()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor => CenarioDaExecucao.LinhaDe(valor) == 2 ? throw new FormatException(Sentinela) : new ResultadoTransformacao("TRATADO", Desfecho.Substituido),
        });

        var resultado = await cenario.ExecutarAsync();

        var falha = Assert.IsType<FalhaTransformacaoException>(resultado.Falha);
        Assert.Equal(("INC4007", 2L), (falha.Codigo, falha.Linha));
        Assert.Equal(1, resultado.Saida);
        Assert.False(cenario.Gravador.Confirmadas.ContainsKey("clientes"));
        Assert.DoesNotContain("ValidarChave:destino", cenario.Provedor.Chamadas);
        Assert.Equal(SituacaoExecucao.Falhou, cenario.Encerrada);
        Assert.Contains(resultado.Avisos, aviso => aviso.Codigo == CodigosExecucao.DestinoIncompleto);
        Assert.Equal((3, 3), (cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes));
        Assert.DoesNotContain(Sentinela, resultado.ToString(), StringComparison.Ordinal);
        Assert.All(resultado.Avisos, aviso => Assert.DoesNotContain(Sentinela, aviso.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_no_ConcluirAsync_desfaz_a_tabela_e_restaura_sem_validar()
    {
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario { NoConcluir = new ExcecaoDoDriver() });

        var resultado = await cenario.ExecutarAsync();

        Assert.IsType<FalhaDialetoException>(resultado.Falha);
        Assert.Equal(1, resultado.Saida);
        Assert.Empty(cenario.Gravador.Confirmadas);
        Assert.DoesNotContain("ValidarChave:destino", cenario.Provedor.Chamadas);
        Assert.Equal(SituacaoExecucao.Falhou, cenario.Encerrada);
    }

    [Fact]
    public async Task Ctrl_C_sai_130_restaura_com_o_token_proprio_e_encerra_como_falhou()
    {
        using var usuario = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    usuario.Cancel();
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
        });

        var resultado = await cenario.ExecutarAsync(cancelamento: usuario.Token);

        Assert.IsType<OperationCanceledException>(resultado.Falha);
        Assert.Equal(130, resultado.Saida);
        Assert.Equal(SituacaoExecucao.Falhou, cenario.Encerrada);
        Assert.DoesNotContain("ValidarChave:destino", cenario.Provedor.Chamadas);

        // A tabela em curso é desfeita; a do outro trabalhador pode ter concluído antes.
        Assert.False(cenario.Gravador.Confirmadas.ContainsKey("clientes"));
        Assert.Equal((3, 3), (cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes));
    }

    [Fact]
    public async Task Bloqueio_perdido_no_meio_para_tudo_e_nada_mais_e_escrito_nem_o_encerramento()
    {
        CenarioDaExecucao? cenario = null;
        string[]? naPerda = null;
        cenario = await CenarioDaExecucao.CriarAsync(new OpcoesDoCenario
        {
            Funcao = valor =>
            {
                if (CenarioDaExecucao.LinhaDe(valor) == 2)
                {
                    naPerda = cenario!.EscritasDoDiario();
                    cenario.Provedor.Concessoes.Single().Perder();
                }

                return new ResultadoTransformacao("TRATADO", Desfecho.Substituido);
            },
        });

        var resultado = await cenario.ExecutarAsync();

        Assert.Equal(naPerda, cenario.EscritasDoDiario());
        var perda = Assert.IsType<BloqueioDestinoException>(resultado.Falha);
        Assert.Equal(("INC4001", 1), (perda.Codigo, resultado.Saida));
        Assert.Null(resultado.Restauracao);
        Assert.Null(cenario.Encerrada);
        Assert.DoesNotContain("ReligarTriggers:destino", cenario.Provedor.Chamadas);
        Assert.False(cenario.Gravador.Confirmadas.ContainsKey("clientes"));
        Assert.Equal((3, 3), (cenario.Fabrica.Criacoes, cenario.Fabrica.Descartes));
    }

    private static bool Antes(List<string> chamadas, string primeira, string depois)
    {
        lock (chamadas)
        {
            var indice = chamadas.IndexOf(primeira);
            var outro = chamadas.IndexOf(depois);
            Assert.True(indice >= 0 && outro > indice, $"{primeira} antes de {depois}: {string.Join(" | ", chamadas)}");
            return true;
        }
    }

    private static EsquemaBanco Encolhido() =>
        TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 37, Unidade = UnidadeComprimento.PontosDeCodigo }));

    private static EsquemaBanco ComTrigger(bool ligada) =>
        Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Triggers), new TriggerEsquema[] { new() { Nome = "tg_auditoria", Ligada = ligada, Herdada = false } })));

    // O diário com uma execução anterior deste destino e as entradas que ela deixou.
    private static EstadoDiario Interrompida(SituacaoExecucao situacao, params EntradaDiario[] pendentes) => new()
    {
        Existe = true,
        Execucoes = [new ExecucaoLida { Id = _anterior, Marcador = Marcado.Identificador!.Value, IniciadaEm = DateTimeOffset.UnixEpoch, Situacao = situacao }],
        Pendentes = pendentes,
    };

    private static EntradaDiario Pendente(TipoEntrada tipo, string tabela, string? nome, SituacaoEntrada situacao = SituacaoEntrada.Removida) =>
        new() { Execucao = _anterior, Tipo = tipo, Tabela = Nome(tabela), Nome = nome, Situacao = situacao };
}
