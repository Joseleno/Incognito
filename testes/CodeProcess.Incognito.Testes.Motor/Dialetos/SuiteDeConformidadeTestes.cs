using System.Reflection;
using CodeProcess.Incognito.Testes.Conformidade;
using Xunit.Sdk;
using Xunit.v3;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// A suíte de conformidade de dialeto roda contra um dialeto falso, por reflexão (a classe concreta é interna, e o
/// xUnit não a descobre): o falso certo passa em tudo e cada defeito derruba exatamente os testes que precisam pegá-lo,
/// por asserção e com a mensagem do defeito. Assim a suíte é provada em todo pull request, sem banco.
/// </summary>
public sealed class SuiteDeConformidadeTestes
{
    // Os testes que cada defeito derruba, com um trecho da mensagem esperada, e em qual variante do banco falso
    // (completo ou sem os recursos opcionais).
    private static readonly Dictionary<Defeito, (bool Completo, (string Teste, string Trecho)[] Falhas)> _mapa = new()
    {
        [Defeito.CasoSemDeclaracao] = (true, [(nameof(Suite.Todo_caso_do_catalogo_tem_declaracao), "Declaração: casos sem declaração na fixture: NomesExatos"), (nameof(Suite.Os_nomes_sao_lidos_como_o_catalogo_os_guarda), "Nomes: a fixture precisa declarar")]),
        [Defeito.TiposSemCobertura] = (true, [(nameof(Suite.Os_tipos_da_matriz_sao_classificados), "Tipos: a matriz declarada precisa cobrir")]),
        [Defeito.TipoMalClassificado] = (true, [(nameof(Suite.Os_tipos_da_matriz_sao_classificados), "Tipos: loja.tipos.dados (jsonb) lida como Suportado")]),
        [Defeito.TipoCanonicoForaDoSuportado] = (true, [(nameof(Suite.O_esquema_lido_e_consistente), "coluna loja.calculos.dobro com tipo canônico fora de 'suportado'")]),
        [Defeito.ParticaoSemPai] = (true, [(nameof(Suite.A_particionada_e_as_particoes_apontam_o_pai), "Partição: filhas de loja.pedidos"), (nameof(Suite.O_esquema_lido_e_consistente), "pai de loja.pedidos_2026")]),
        [Defeito.HerancaSemMae] = (true, [(nameof(Suite.As_herdeiras_apontam_a_mae), "Herança: filhas de loja.pessoa"), (nameof(Suite.O_esquema_lido_e_consistente), "pai de loja.funcionario")]),
        [Defeito.SequenceComUmaColuna] = (true, [(nameof(Suite.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada: loja.seq_numero lida com")]),
        [Defeito.SequenceComColunaRepetida] = (true, [(nameof(Suite.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada: loja.seq_numero lida com"), (nameof(Suite.O_esquema_lido_e_consistente), "repetida na sequence loja.seq_numero")]),
        [Defeito.GeradaNaoMarcada] = (true, [(nameof(Suite.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: lidas []")]),
        [Defeito.GeradaNaCopia] = (true, [(nameof(Suite.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: sem a situação 'suportado por exclusão': loja.calculos.dobro")]),
        [Defeito.ChaveForaDeOrdem] = (true, [(nameof(Suite.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: fk_itens_pedidos lida como (pedido, loja)")]),
        [Defeito.ChaveDesbalanceada] = (true, [(nameof(Suite.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: fk_itens_pedidos lida como"), (nameof(Suite.O_esquema_lido_e_consistente), "com 2 colunas e 1 referenciadas")]),
        [Defeito.TabelaDuplicada] = (true, [(nameof(Suite.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: tabela loja.itens lida 2 vezes"), (nameof(Suite.O_esquema_lido_e_consistente), "tabela loja.itens repetida"), (nameof(Suite.O_esquema_vem_em_ordem_de_esquema_e_nome), "Ordem: tabelas")]),
        [Defeito.TemporalSemHistorico] = (true, [(nameof(Suite.A_temporal_aponta_o_historico), "Temporal: loja.contratos lida como Temporal com histórico ,"), (nameof(Suite.O_esquema_lido_e_consistente), "histórico de loja.contratos (Temporal) ausente")]),
        [Defeito.ResiduoAusente] = (true, [(nameof(Suite.Os_objetos_residuais_sao_inventariados), "Objetos residuais: lidos []")]),
        [Defeito.SegurancaDeLinhaAusente] = (true, [(nameof(Suite.A_seguranca_por_linha_e_apontada), "Segurança por linha: lida em []")]),
        [Defeito.MascaraAusente] = (true, [(nameof(Suite.A_mascara_e_apontada), "Máscara: lida em []")]),
        [Defeito.NomeNaCaixaErrada] = (true, [(nameof(Suite.Os_nomes_sao_lidos_como_o_catalogo_os_guarda), "Nomes: não lidos exatamente como o catálogo os guarda: loja.Cliente")]),
        [Defeito.CitacaoSemEscape] = (true, [(nameof(Suite.Os_identificadores_sao_citados_como_o_banco_espera), "no origem")]),
        [Defeito.CitacaoErradaNoDestino] = (true, [(nameof(Suite.Os_identificadores_sao_citados_como_o_banco_espera), "no destino")]),
        [Defeito.EsquemaDoIncognitoLido] = (true, [(nameof(Suite.O_esquema_do_Incognito_fica_fora_da_leitura), "Esquema incognito: objetos lidos")]),
        [Defeito.IncognitoForaDoEsquema] = (true, [(nameof(Suite.O_esquema_do_Incognito_fica_fora_da_leitura), "Esquema incognito: o objeto declarado (loja.execucao)")]),
        [Defeito.TabelasForaDeOrdem] = (true, [(nameof(Suite.O_esquema_vem_em_ordem_de_esquema_e_nome), "Ordem: tabelas")]),
        [Defeito.LeituraInstavel] = (true, [(nameof(Suite.A_leitura_do_esquema_e_estavel), "Leitura estável:")]),
        [Defeito.IdentidadeInstavel] = (true, [(nameof(Suite.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: duas leituras seguidas")]),
        [Defeito.BancoErrado] = (true, [(nameof(Suite.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: banco da origem lido como outro-banco")]),
        [Defeito.LinhagemVazia] = (true, [(nameof(Suite.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: linhagem vazia")]),

        // Um banco sem os recursos opcionais cujo leitor os devolve mesmo assim: inexistente é conferido, não pulado.
        [Defeito.InventaRecursos] = (false,
        [
            (nameof(Suite.A_particionada_e_as_particoes_apontam_o_pai), "Partição lida num banco que a declara inexistente"),
            (nameof(Suite.As_herdeiras_apontam_a_mae), "Herança lida num banco que a declara inexistente"),
            (nameof(Suite.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada lida num banco que a declara inexistente"),
            (nameof(Suite.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: lidas [loja.calculos.dobro"),
            (nameof(Suite.A_temporal_aponta_o_historico), "Temporal lida num banco que a declara inexistente"),
            (nameof(Suite.Os_objetos_residuais_sao_inventariados), "Objetos residuais: lidos [loja.resumo_vendas"),
            (nameof(Suite.A_seguranca_por_linha_e_apontada), "Segurança por linha: lida em [loja.segredos]"),
            (nameof(Suite.A_mascara_e_apontada), "Máscara: lida em [loja.contatos.email]"),
        ]),
    };

    public static TheoryData<Defeito> Defeitos => [.. _mapa.Keys];

    [Fact]
    public void A_suite_so_tem_fatos_sem_parametros_nem_pulo_e_todos_na_categoria_de_banco()
    {
        var metodos = typeof(ConformidadeDialetoTestes<>).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(metodo => metodo.GetCustomAttributes().Any(atributo => atributo is IFactAttribute or ITraitAttribute or IDataAttribute))
            .ToArray();

        Assert.NotEmpty(metodos);
        Assert.All(metodos, metodo =>
        {
            var fato = Assert.IsType<FactAttribute>(Assert.Single(metodo.GetCustomAttributes().OfType<IFactAttribute>()), exactMatch: true);
            Assert.True(fato.Skip is null && fato.SkipWhen is null && fato.SkipUnless is null && !fato.Explicit && fato.Timeout == 0, $"{metodo.Name} pula, é explícito ou tem tempo-limite.");
            Assert.True(metodo.IsPublic && !metodo.IsStatic && metodo.GetParameters().Length == 0, $"{metodo.Name} não é um fato público sem parâmetros.");
            Assert.True(metodo.ReturnType == typeof(void) || metodo.ReturnType == typeof(Task), $"{metodo.Name} devolve {metodo.ReturnType.Name}.");
            Assert.Contains(
                metodo.GetCustomAttributes<TraitAttribute>(),
                trait => trait.Name == CategoriaDaSuite.Nome && trait.Value == CategoriaDaSuite.Valor);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task O_dialeto_falso_certo_passa_em_todos_os_testes(bool completo)
    {
        var falhas = await RodarAsync(new FixtureFalsa(completo, Defeito.Nenhum));

        Assert.True(falhas.Count == 0, string.Join("\n", falhas.Select(falha => $"{falha.Key}: {falha.Value.Message}")));
    }

    [Theory]
    [MemberData(nameof(Defeitos))]
    public async Task Cada_defeito_derruba_exatamente_os_testes_dele_por_assercao(Defeito defeito)
    {
        var (completo, esperadas) = _mapa[defeito];

        var falhas = await RodarAsync(new FixtureFalsa(completo, defeito));

        Assert.Equal(esperadas.Select(falha => falha.Teste).Order(StringComparer.Ordinal), falhas.Keys.Order(StringComparer.Ordinal));
        Assert.All(esperadas, esperada =>
        {
            var excecao = falhas[esperada.Teste];
            Assert.True(excecao is IAssertionException, $"{esperada.Teste} falhou por {excecao.GetType().Name}, não por asserção.");
            Assert.True(excecao.Message.Contains(esperada.Trecho, StringComparison.Ordinal), $"{esperada.Teste} falhou com outra mensagem: {excecao.Message}");
        });
    }

    [Fact]
    public void Todo_teste_da_suite_e_derrubado_por_algum_defeito_e_todo_defeito_esta_no_mapa()
    {
        Assert.Equal(Fatos().Order(StringComparer.Ordinal), _mapa.Values.SelectMany(valor => valor.Falhas.Select(falha => falha.Teste)).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(Enum.GetValues<Defeito>().Where(defeito => defeito != Defeito.Nenhum).Order(), _mapa.Keys.Order());
    }

    private static string[] Fatos() =>
    [
        .. typeof(ConformidadeDialetoTestes<>).GetMethods()
            .Where(metodo => metodo.GetCustomAttribute<FactAttribute>() is not null)
            .Select(metodo => metodo.Name),
    ];

    // Roda cada fato da suíte com o ciclo de vida do xUnit (construtor, InitializeAsync, fato, DisposeAsync) e junta as
    // falhas por nome do teste.
    private static async Task<Dictionary<string, Exception>> RodarAsync(FixtureFalsa fixture)
    {
        var falhas = new Dictionary<string, Exception>(StringComparer.Ordinal);
        foreach (var fato in Fatos())
        {
            var suite = new Suite(fixture);
            await suite.InitializeAsync();
            try
            {
                if (typeof(Suite).GetMethod(fato)!.Invoke(suite, null) is Task tarefa)
                {
                    await tarefa;
                }
            }
            catch (TargetInvocationException excecao)
            {
                falhas.Add(fato, excecao.InnerException!);
            }
            catch (Exception excecao)
            {
                falhas.Add(fato, excecao);
            }
            finally
            {
                await suite.DisposeAsync();
            }
        }

        return falhas;
    }

    // Interna: o xUnit só descobre classes públicas, então os fatos herdados não rodam sozinhos aqui.
    internal sealed class Suite(FixtureFalsa fixture) : ConformidadeDialetoTestes<FixtureFalsa>(fixture);
}
