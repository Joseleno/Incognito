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
    // (completo ou sem os recursos opcionais). Os defeitos que mudam uma tabela ou sequence declarada também derrubam o
    // inventário: a sobreposição é esperada e fica escrita aqui.
    private static readonly Dictionary<Defeito, (bool Completo, (string Teste, string Trecho)[] Falhas)> _mapa = new()
    {
        [Defeito.CasoSemDeclaracao] = (true, [(nameof(SuiteFalsa.Todo_caso_do_catalogo_tem_declaracao), "Declaração: casos sem declaração na fixture: NomesExatos"), (nameof(SuiteFalsa.Os_nomes_sao_lidos_como_o_catalogo_os_guarda), "Nomes: a fixture precisa declarar")]),
        [Defeito.TipoMalClassificado] = (true, [Consistencia("coluna loja.tipos.dados de texto sem colação"), Inventario("loja.tipos", "coluna dados: situação lida Suportado, esperada ComRessalva")]),
        [Defeito.TipoCanonicoForaDoSuportado] = (true, [Consistencia("coluna loja.calculos.dobro com tipo canônico fora de 'suportado'"), Inventario("loja.calculos", "coluna dobro: tipo canônico lido Inteiro, esperado nenhum")]),
        [Defeito.ParticaoSemPai] = (true, [(nameof(SuiteFalsa.A_particionada_e_as_particoes_apontam_o_pai), "Partição: filhas de loja.pedidos"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "pai de loja.pedidos_2026"), Inventario("loja.pedidos_2026", "pai lido nenhum, esperado loja.pedidos")]),
        [Defeito.HerancaSemMae] = (true, [(nameof(SuiteFalsa.As_herdeiras_apontam_a_mae), "Herança: filhas de loja.pessoa"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "pai de loja.funcionario"), Inventario("loja.funcionario", "pai lido nenhum, esperado loja.pessoa")]),
        [Defeito.SequenceComUmaColuna] = (true, [(nameof(SuiteFalsa.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada: loja.seq_numero lida com"), Inventario("loja.seq_numero", "sequence usada por [loja.pedidos.numero], esperada por [loja.itens.numero, loja.pedidos.numero]")]),
        [Defeito.SequenceComColunaRepetida] = (true, [(nameof(SuiteFalsa.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada: loja.seq_numero lida com"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "repetida na sequence loja.seq_numero"), Inventario("loja.seq_numero", "sequence usada por [loja.itens.numero, loja.pedidos.numero, loja.pedidos.numero]")]),
        [Defeito.GeradaNaoMarcada] = (true, [(nameof(SuiteFalsa.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: lidas []"), Consistencia("coluna loja.calculos.dobro com referências da geração ausentes ou sobrando"), Inventario("loja.calculos", "coluna dobro: geração lida Nenhuma, esperada Armazenada")]),
        [Defeito.GeradaNaCopia] = (true, [(nameof(SuiteFalsa.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: sem a situação 'suportado por exclusão': loja.calculos.dobro"), Inventario("loja.calculos", "coluna dobro: situação lida Suportado, esperada SuportadoPorExclusao")]),
        [Defeito.ChaveForaDeOrdem] = (true, [(nameof(SuiteFalsa.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: fk_itens_pedidos lida como (pedido, loja)"), Inventario("loja.itens", "chave estrangeira fk_itens_pedidos: colunas lidas [pedido, loja], esperadas [loja, pedido]")]),
        [Defeito.ChaveDesbalanceada] = (true, [(nameof(SuiteFalsa.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: fk_itens_pedidos lida como"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "com 2 colunas e 1 referenciadas"), Inventario("loja.itens", "chave estrangeira fk_itens_pedidos: colunas referenciadas lidas [loja], esperadas [loja, id]")]),
        [Defeito.TabelaDuplicada] = (true, [(nameof(SuiteFalsa.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: tabela loja.itens lida 2 vezes"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "tabela loja.itens repetida"), (nameof(SuiteFalsa.O_esquema_vem_em_ordem_de_esquema_e_nome), "Ordem: tabelas"), Inventario("loja.itens", "tabela lida 2 vezes")]),
        [Defeito.TemporalSemHistorico] = (true, [(nameof(SuiteFalsa.A_temporal_aponta_o_historico), "Temporal: loja.contratos lida como Temporal com histórico ,"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "histórico de loja.contratos (Temporal) ausente"), Inventario("loja.contratos", "histórico lido nenhum, esperado loja.contratos_historico")]),
        [Defeito.ResiduoAusente] = (true, [(nameof(SuiteFalsa.Os_objetos_residuais_sao_inventariados), "Objetos residuais: lidos []"), Inventario("residual loja.resumo_vendas", "residual a menos")]),
        [Defeito.SegurancaDeLinhaAusente] = (true, [(nameof(SuiteFalsa.A_seguranca_por_linha_e_apontada), "Segurança por linha: lida em []"), Inventario("loja.segredos", "segurança por linha lida False, esperada True")]),
        [Defeito.MascaraAusente] = (true, [(nameof(SuiteFalsa.A_mascara_e_apontada), "Máscara: lida em []"), Inventario("loja.contatos", "coluna email: mascarada lida False, esperada True")]),
        [Defeito.NomeNaCaixaErrada] = (true, [(nameof(SuiteFalsa.Os_nomes_sao_lidos_como_o_catalogo_os_guarda), "Nomes: não lidos exatamente como o catálogo os guarda: loja.Cliente"), Inventario("loja.CLIENTE", "tabela a mais; origem: loja.Cliente: tabela a menos")]),
        [Defeito.CitacaoSemEscape] = (true, [(nameof(SuiteFalsa.Os_identificadores_sao_citados_como_o_banco_espera), "no origem")]),
        [Defeito.CitacaoErradaNoDestino] = (true, [(nameof(SuiteFalsa.Os_identificadores_sao_citados_como_o_banco_espera), "no destino")]),
        [Defeito.EsquemaDoIncognitoLido] = (true, [(nameof(SuiteFalsa.O_esquema_do_Incognito_fica_fora_da_leitura), "Esquema incognito: objetos lidos"), Inventario("incognito.execucao", "tabela a mais")]),
        [Defeito.IncognitoForaDoEsquema] = (true, [(nameof(SuiteFalsa.O_esquema_do_Incognito_fica_fora_da_leitura), "Esquema incognito: o objeto declarado (loja.execucao)")]),
        [Defeito.TabelasForaDeOrdem] = (true, [(nameof(SuiteFalsa.O_esquema_vem_em_ordem_de_esquema_e_nome), "Ordem: tabelas")]),
        [Defeito.LeituraInstavel] = (true, [(nameof(SuiteFalsa.A_leitura_do_esquema_e_estavel), "Leitura estável:")]),
        [Defeito.IdentidadeInstavel] = (true, [(nameof(SuiteFalsa.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: duas leituras seguidas")]),
        [Defeito.BancoErrado] = (true, [(nameof(SuiteFalsa.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: banco da origem lido como outro-banco")]),
        [Defeito.LinhagemVazia] = (true, [(nameof(SuiteFalsa.A_identidade_aponta_o_banco_e_e_estavel), "Identidade: linhagem vazia")]),

        // Um banco sem os recursos opcionais cujo leitor os devolve mesmo assim: inexistente é conferido, não pulado.
        [Defeito.InventaRecursos] = (false,
        [
            (nameof(SuiteFalsa.A_particionada_e_as_particoes_apontam_o_pai), "Partição lida num banco que a declara inexistente"),
            (nameof(SuiteFalsa.As_herdeiras_apontam_a_mae), "Herança lida num banco que a declara inexistente"),
            (nameof(SuiteFalsa.A_sequence_compartilhada_e_vista_por_todas_as_colunas), "Sequence compartilhada lida num banco que a declara inexistente"),
            (nameof(SuiteFalsa.As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia), "Colunas geradas: lidas [loja.calculos.dobro"),
            (nameof(SuiteFalsa.A_temporal_aponta_o_historico), "Temporal lida num banco que a declara inexistente"),
            (nameof(SuiteFalsa.Os_objetos_residuais_sao_inventariados), "Objetos residuais: lidos [loja.mv_base VisaoMaterializada"),
            (nameof(SuiteFalsa.A_seguranca_por_linha_e_apontada), "Segurança por linha: lida em [loja.segredos]"),
            (nameof(SuiteFalsa.A_mascara_e_apontada), "Máscara: lida em [loja.contatos.email]"),
            (nameof(SuiteFalsa.As_propriedades_do_banco_sao_lidas), "Propriedades do banco: origem: lidas versão 18.6"),
            Inventario("loja.calculos", "tabela a mais"),
        ]),

        [Defeito.InventarioVazio] = (true, [(nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "Inventário: a fixture precisa declarar as tabelas"), (nameof(SuiteFalsa.A_matriz_de_tipos_e_coberta), "Matriz: valores sem coluna no inventário nem motivo de ausência: SituacaoTipo.Suportado")]),
        [Defeito.TabelaOmitida] = (true, [Inventario("loja.da_extensao", "tabela a menos")]),
        [Defeito.TabelaOmitidaNoDestino] = (true, [(nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "Inventário: destino: loja.da_extensao: tabela a menos.")]),
        [Defeito.ColunaAMais] = (true, [Inventario("loja.endereco", "coluna bairro a mais")]),
        [Defeito.ColunaAMenos] = (true, [Inventario("loja.ficha", "coluna cidade a menos")]),
        [Defeito.ColunasForaDeOrdem] = (true, [Inventario("loja.ficha", "colunas lidas [numero, cidade, canal], esperadas [canal, cidade, numero]")]),
        [Defeito.PropriedadeDeColunaTrocada] = (true, [Inventario("loja.contato", "coluna canal: anulável lida False, esperada True")]),
        [Defeito.ChaveEstrangeiraRepetida] = (true, [(nameof(SuiteFalsa.A_chave_estrangeira_composta_mantem_a_ordem_da_chave), "Chave estrangeira composta: fk_itens_pedidos lida 2 vezes"), (nameof(SuiteFalsa.O_esquema_lido_e_consistente), "chave fk_itens_pedidos de loja.itens repetida"), Inventario("loja.itens", "chave estrangeira fk_itens_pedidos lida 2 vezes")]),
        [Defeito.ChaveEstrangeiraComReferenciadaErrada] = (true, [Inventario("loja.ficha", "chave estrangeira fk_ficha_contato: referenciada lida loja.ficha, esperada loja.contato")]),
        [Defeito.ChavePrimariaForaDeOrdem] = (true, [Consistencia("índice primário pk_pedidos de loja.pedidos diferente da chave primária"), Inventario("loja.pedidos", "chave primária lida [id, loja], esperada [loja, id]")]),
        [Defeito.TipoDeTabelaTrocado] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "pai de loja.contadores (Herdeira) ausente"), Inventario("loja.contadores", "tipo lido Herdeira, esperado Comum")]),
        [Defeito.SequenceOmitida] = (true, [Inventario("loja.seq_avulsa", "sequence a menos")]),
        [Defeito.SituacaoTrocadaNoDestino] = (true, [(nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "Inventário: destino: loja.tipos: coluna dados: situação lida NaoSuportado, esperada ComRessalva.")]),
        [Defeito.SituacaoTrocadaForaDaMatriz] = (true, [Inventario("loja.canonicos", "coluna dia: situação lida ComRessalva, esperada Suportado")]),
        [Defeito.TipoCanonicoTrocadoForaDaMatriz] = (true, [Inventario("loja.canonicos", "coluna momento: tipo canônico lido DataHoraComFuso, esperado DataHora")]),
        [Defeito.OutroPaiForaDoEsquema] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "outros pais de loja.ficha (Herdeira)"), Inventario("loja.ficha", "outros pais lidos [loja.endereco_antigo], esperados [loja.endereco, loja.telefone]")]),
        [Defeito.ChaveDeParticaoAusente] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "chave de partição de loja.pedidos (Particionada) ausente ou sobrando"), Inventario("loja.pedidos", "chave de partição lida nenhum, esperada (loja, id)")]),
        [Defeito.ChaveDeParticaoComColunaInexistente] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "chave de partição de loja.pedidos com coluna que não existe"), Inventario("loja.pedidos", "chave de partição lida (filial), esperada (loja, id)")]),
        [Defeito.ChaveDeParticaoVaziaCompleta] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "chave de partição de loja.pedidos sem coluna e marcada completa"), Inventario("loja.pedidos", "chave de partição lida (), esperada (loja, id)")]),
        [Defeito.MaeDeTipoErrado] = (true, [(nameof(SuiteFalsa.O_esquema_lido_e_consistente), "pai de loja.ficha (Herdeira) de tipo que não pode ter filha desse tipo"), Inventario("loja.ficha", "outros pais lidos [loja.endereco, loja.pedidos_2025], esperados [loja.endereco, loja.telefone]")]),
        [Defeito.ColacaoEmColunaInteira] = (true, [Consistencia("coluna loja.canonicos.pequeno com colação fora de texto"), Inventario("loja.canonicos", "coluna pequeno: colação lida default (determinística; caixa não, acento não, pontuação não), esperada nenhum")]),
        [Defeito.TextoSemColacao] = (true, [Consistencia("coluna loja.endereco.cidade de texto sem colação"), Inventario("loja.endereco", "coluna cidade: colação lida nenhum, esperada default")]),
        [Defeito.InteiroSemFaixa] = (true, [Consistencia("coluna loja.itens.numero inteira sem faixa"), Inventario("loja.itens", "coluna numero: faixa lida nenhum")]),
        [Defeito.FaixaInvertida] = (true, [Consistencia("coluna loja.canonicos.pequeno com faixa vazia"), Inventario("loja.canonicos", "coluna pequeno: faixa lida [5, 3]")]),
        [Defeito.PrecisaoForaDeDecimal] = (true, [Consistencia("coluna loja.canonicos.dia com precisão ou escala fora de decimal"), Inventario("loja.canonicos", "coluna dia: precisão lida 5, esperada nenhum")]),
        [Defeito.LimiteEmPontosSemCodificacao] = (true, [Consistencia("com comprimento em caracteres num banco sem codificação declarada"), (nameof(SuiteFalsa.As_propriedades_do_banco_sao_lidas), "Definida = False")]),
        [Defeito.GeradaSemReferencias] = (true, [Consistencia("coluna loja.calculos.dobro com referências da geração ausentes ou sobrando"), Inventario("loja.calculos", "coluna dobro: referências da geração lidas nenhum, esperadas (base, extra)")]),
        [Defeito.GeracaoCitaAPropria] = (true, [Consistencia("coluna loja.calculos.dobro com referência da geração a ela mesma"), Inventario("loja.calculos", "coluna dobro: referências da geração lidas (base, dobro), esperadas (base, extra)")]),
        [Defeito.DeterministicaQueIgnoraCaixa] = (true, [Consistencia("coluna loja.canonicos.curto com colação determinística e marca que não é falsa"), Inventario("loja.canonicos", "coluna curto: colação lida loja.sem_caixa (determinística; caixa sim")]),
        [Defeito.ComprimentoForaDoTipo] = (true, [Consistencia("coluna loja.canonicos.dia com comprimento fora de texto e binário"), Inventario("loja.canonicos", "coluna dia: comprimento lido 10 em PontosDeCodigo, esperado nenhum")]),
        [Defeito.FaixaForaDoTipo] = (true, [Consistencia("coluna loja.canonicos.valor com faixa fora de inteiro"), Inventario("loja.canonicos", "coluna valor: faixa lida [0, 9], esperada nenhum")]),
        [Defeito.ReferenciasSemGeracao] = (true, [Consistencia("coluna loja.calculos.base com referências da geração ausentes ou sobrando"), Inventario("loja.calculos", "coluna base: referências da geração lidas (extra), esperadas nenhum")]),
        [Defeito.PropriedadesAusentes] = (true, [(nameof(SuiteFalsa.As_propriedades_do_banco_sao_lidas), "Propriedades do banco: origem: lidas nenhuma, esperadas versão 18.6")]),
        [Defeito.MatrizDescoberta] = (false, [(nameof(SuiteFalsa.A_matriz_de_tipos_e_coberta), "Matriz: valores sem coluna no inventário nem motivo de ausência: TipoColuna.Uuid.")]),
        [Defeito.SentinelaNumNome] = (true, [(nameof(SuiteFalsa.Nada_lido_traz_o_sentinela), "textos lidos trazem um sentinela")]),
        [Defeito.SemSentinelas] = (true, [(nameof(SuiteFalsa.Nada_lido_traz_o_sentinela), "Sentinela: a fixture precisa declarar os sentinelas")]),
        [Defeito.SentinelaVazio] = (true, [(nameof(SuiteFalsa.Nada_lido_traz_o_sentinela), "Sentinela: a fixture precisa declarar os sentinelas, sem item vazio")]),
        [Defeito.SentinelaNaIdentidadeDaOrigem] = (true, [(nameof(SuiteFalsa.Nada_lido_traz_o_sentinela), "Sentinela: 1 textos lidos trazem um sentinela")]),
        [Defeito.SentinelaNoTextoDoDestino] = (true, [(nameof(SuiteFalsa.Nada_lido_traz_o_sentinela), "Sentinela: 1 textos lidos trazem um sentinela")]),
        [Defeito.PropriedadesAusentesNoDestino] = (true, [(nameof(SuiteFalsa.As_propriedades_do_banco_sao_lidas), "Propriedades do banco: destino: lidas nenhuma"), (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "Inventário: o destino, com o mesmo esquema, foi lido diferente da origem.")]),
        [Defeito.MascaraTrocadaNoDestino] = (true, [(nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "Inventário: destino: loja.contatos: coluna email: mascarada lida False, esperada True.")]),
        [Defeito.FaixaSemPositivos] = (true, [Consistencia("coluna loja.canonicos.pequeno com faixa sem positivos"), Inventario("loja.canonicos", "coluna pequeno: faixa lida [-10, -5]")]),
        [Defeito.DeterministicaComMarcaNula] = (true, [Consistencia("coluna loja.endereco.cidade com colação determinística e marca que não é falsa"), Inventario("loja.endereco", "coluna cidade: colação lida default (determinística; caixa ?")]),
        [Defeito.ColacaoPadraoComMarcaNula] = (true, [Consistencia("colação padrão das propriedades do banco determinística com marca que não é falsa"), (nameof(SuiteFalsa.As_propriedades_do_banco_sao_lidas), "acento ?")]),
        [Defeito.GeracaoCitaColunaInexistente] = (true, [Consistencia("coluna loja.calculos.dobro com referência da geração a coluna que não existe"), Inventario("loja.calculos", "coluna dobro: referências da geração lidas (base, fantasma)")]),
        [Defeito.ReferenciasForaDeOrdem] = (true, [Consistencia("coluna loja.calculos.dobro com referências da geração fora da ordem das colunas"), Inventario("loja.calculos", "coluna dobro: referências da geração lidas (extra, base)")]),
        [Defeito.MatrizContraditoria] = (false, [(nameof(SuiteFalsa.A_matriz_de_tipos_e_coberta), "Matriz: valores declarados inexistentes e presentes no inventário: SituacaoTipo.Suportado.")]),
        [Defeito.MatrizSemMotivo] = (false, [(nameof(SuiteFalsa.A_matriz_de_tipos_e_coberta), "Matriz: ausências fora da matriz ou sem motivo: TipoColuna.Uuid.")]),
        [Defeito.IndiceUnicoOmitido] = (true, [Inventario("loja.indexada", "índice ix_indexada_codigo a menos")]),
        [Defeito.IndiceAnexadoDaParticaoListado] = (true, [Inventario("loja.pedidos_2025", "índice pedidos_2025_pkey a mais")]),
        [Defeito.ExclusaoComoIndiceUnico] = (true, [Inventario("loja.indexada", "índice ex_indexada a mais")]),
        [Defeito.TriggerInternaListada] = (true, [Inventario("loja.indexada", "trigger RI_ConstraintTrigger_c_1 a mais")]),
        [Defeito.CheckHerdadoNaParticao] = (true, [Inventario("loja.pedidos_2025", "CHECK ck_pedidos_loja a mais")]),
        [Defeito.CheckInventado] = (true, [Inventario("loja.endereco", "CHECK ck_endereco_cidade a mais")]),
        [Defeito.FkComRiDesligadaComoImposta] = (true, [Inventario("loja.ficha", "chave estrangeira fk_ficha_contato: imposta lida True, esperada False")]),
        [Defeito.ElementoDeExpressaoSemReferencias] = (true, [Consistencia("índice ix_indexada_expressoes de loja.indexada com referências das expressões ausentes ou sobrando"), (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "; expressões nenhuma; predicado (id, fim); nulos não distintos, esperado")]),
        [Defeito.PredicadoCitaColunaInexistente] = (true, [Consistencia("índice ix_indexada_expressoes de loja.indexada com predicado citando fantasma, coluna que não existe"), (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "; expressões (email, ini); predicado (id, fantasma); nulos não distintos, esperado")]),
        [Defeito.IndicePrimarioDivergenteDaChave] = (true, [Consistencia("índice primário pk_indexada de loja.indexada diferente da chave primária"), Inventario("loja.indexada", "índice pk_indexada lido (ini [sem colação]) primário")]),
        [Defeito.DoisIndicesPrimarios] = (true, [Consistencia("tabela loja.indexada com 2 índices primários"), (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "não determinística; caixa sim, acento não, pontuação não)]) primário; expressões (email, ini)")]),
        [Defeito.DependenciaCircular] = (true, [Consistencia("residual loja.mv_base numa dependência circular"), Inventario("residual loja.mv_base", "depende de [loja.resumo_vendas], esperado de []")]),
        [Defeito.DependenciaDeResidualInexistente] = (true, [Consistencia("residual loja.resumo_vendas depende de loja.mv_sumida, que não é materialized view do esquema"), Inventario("residual loja.resumo_vendas", "depende de [loja.mv_base, loja.mv_clientes, loja.mv_sumida]")]),
        [Defeito.TriggerHerdadaForaDeParticao] = (true, [Consistencia("trigger tg_ligada de loja.indexada herdada fora de partição"), Inventario("loja.indexada", "trigger tg_ligada lida ligada, herdada, esperada ligada")]),
        [Defeito.ElementoTextoSemColacao] = (true, [Consistencia("índice ix_indexada_expressoes de loja.indexada com elemento de texto apelido sem colação"), Inventario("loja.indexada", "índice ix_indexada_expressoes lido (apelido [sem colação]")]),
        [Defeito.PrimariaSemIndiceForaDeParticao] = (true, [Consistencia("chave primária de loja.pedidos sem índice primário fora de partição"), Inventario("loja.pedidos", "índice pk_pedidos a menos")]),
        [Defeito.ColacaoEmElementoNaoTexto] = (true, [Consistencia("índice pk_indexada de loja.indexada com colação no elemento id, que não é texto"), Inventario("loja.indexada", "índice pk_indexada lido (id [default")]),
        [Defeito.ElementoDeColunaInexistente] = (true, [Consistencia("índice pk_indexada de loja.indexada com o elemento fantasma, coluna que não existe"), Inventario("loja.indexada", "índice pk_indexada lido (fantasma [sem colação]) primário")]),
        [Defeito.ExpressoesSemElementoDeExpressao] = (true, [Consistencia("índice pk_indexada de loja.indexada com referências das expressões ausentes ou sobrando"), Inventario("loja.indexada", "índice pk_indexada lido (id [sem colação]) primário; expressões (id)")]),
        [Defeito.ReferenciasDoIndiceForaDeOrdem] = (true, [Consistencia("índice ix_indexada_expressoes de loja.indexada com expressões citando colunas fora da ordem da tabela"), (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), "; expressões (ini, email); predicado (id, fim)")]),
        [Defeito.CheckCitaColunaInexistente] = (true, [Consistencia("CHECK ck_indexada_faixa de loja.indexada citando fantasma, coluna que não existe"), Inventario("loja.indexada", "CHECK ck_indexada_faixa lido (ini, fantasma), esperado (ini, fim)")]),
        [Defeito.ExclusaoForaDeOrdem] = (true, [Consistencia("exclusão ex_indexada de loja.indexada citando colunas fora da ordem da tabela"), Inventario("loja.indexada", "exclusão ex_indexada lida (fim, ini), esperada (ini, fim)")]),
        [Defeito.DependenciasForaDeOrdem] = (true, [Consistencia("residual loja.resumo_vendas com dependências fora da ordem ordinal de esquema e nome"), Inventario("residual loja.resumo_vendas", "depende de [loja.mv_clientes, loja.mv_base]")]),
        [Defeito.ColacaoDoElementoIncoerente] = (true, [Consistencia("índice ix_indexada_codigo de loja.indexada com colação determinística e marca que não é falsa no elemento 1 (codigo)"), Inventario("loja.indexada", "índice ix_indexada_codigo lido (codigo [default (determinística; caixa sim")]),
        [Defeito.ExclusaoQueNaoCobreAPrimaria] = (true, [Consistencia("chave primária de loja.periodos sem índice primário fora de partição"), Inventario("loja.periodos", "exclusão pk_periodos lida (sala), esperada (sala, inicio)")]),
        [Defeito.DependenciaDeObjetosGrandes] = (true, [Consistencia("residual loja.resumo_vendas depende de pg_catalog.pg_largeobject, que não é materialized view do esquema"), Inventario("residual loja.resumo_vendas", "depende de [loja.mv_base, loja.mv_clientes, pg_catalog.pg_largeobject]")]),
        [Defeito.ObjetosGrandesComMarcas] = (true, [Consistencia("residual pg_catalog.pg_largeobject (ObjetosGrandes) com dependências ou marcas de materialized view"), Inventario("residual pg_catalog.pg_largeobject", "populada lida True, esperada nenhum")]),
        [Defeito.SuiteAninhada] = (true, [(nameof(SuiteFalsa.A_classe_da_suite_e_selada_e_de_primeiro_nivel), "Suíte: a classe CodeProcess.Incognito.Testes.Motor.Dialetos.SuiteDeConformidadeTestes+SuiteAninhada precisa ser selada e de primeiro nível")]),
        [Defeito.SuiteAberta] = (true, [(nameof(SuiteFalsa.A_classe_da_suite_e_selada_e_de_primeiro_nivel), "Suíte: a classe CodeProcess.Incognito.Testes.Motor.Dialetos.SuiteAberta precisa ser selada e de primeiro nível")]),
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

        Func<FixtureFalsa, ConformidadeDialetoTestes<FixtureFalsa>>? criar = defeito switch
        {
            Defeito.SuiteAninhada => fixture => new SuiteAninhada(fixture),
            Defeito.SuiteAberta => fixture => new SuiteAberta(fixture),
            _ => null,
        };
        var falhas = await RodarAsync(new FixtureFalsa(completo, defeito), criar);

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

    // Roda cada fato da suíte com o ciclo de vida do xUnit (construtor, InitializeAsync, fato, DisposeAsync) e junta as
    // falhas por nome do teste.
    internal static async Task<Dictionary<string, Exception>> RodarAsync(FixtureFalsa fixture, Func<FixtureFalsa, ConformidadeDialetoTestes<FixtureFalsa>>? criar = null)
    {
        var falhas = new Dictionary<string, Exception>(StringComparer.Ordinal);
        foreach (var fato in Fatos())
        {
            var suite = criar?.Invoke(fixture) ?? new SuiteFalsa(fixture);
            await suite.InitializeAsync();
            try
            {
                if (suite.GetType().GetMethod(fato)!.Invoke(suite, null) is Task tarefa)
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

    private static string[] Fatos() =>
    [
        .. typeof(ConformidadeDialetoTestes<>).GetMethods()
            .Where(metodo => metodo.GetCustomAttribute<FactAttribute>() is not null)
            .Select(metodo => metodo.Name),
    ];

    private static (string Teste, string Trecho) Consistencia(string trecho) => (nameof(SuiteFalsa.O_esquema_lido_e_consistente), trecho);

    // O inventário falha nos dois lados; o trecho confere o da origem.
    private static (string Teste, string Trecho) Inventario(string objeto, string trecho) =>
        (nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas), $"origem: {objeto}: {trecho}");

    // Uma suíte aninhada, que o fato da classe da suíte recusa.
    internal sealed class SuiteAninhada(FixtureFalsa fixture) : ConformidadeDialetoTestes<FixtureFalsa>(fixture);
}

// Interna: o xUnit só descobre classes públicas, então os fatos herdados não rodam sozinhos aqui.
internal sealed class SuiteFalsa(FixtureFalsa fixture) : ConformidadeDialetoTestes<FixtureFalsa>(fixture);

// Uma suíte aberta, com uma derivada que rodaria os fatos de novo: o fato da classe da suíte a recusa.
internal class SuiteAberta(FixtureFalsa fixture) : ConformidadeDialetoTestes<FixtureFalsa>(fixture);

internal sealed class SuiteDerivadaDaAberta(FixtureFalsa fixture) : SuiteAberta(fixture);
