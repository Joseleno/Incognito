using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Conformidade;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>Um defeito do dialeto falso (ou da declaração da fixture), cada um ligado aos testes da suíte que precisam pegá-lo.</summary>
public enum Defeito
{
    Nenhum,
    CasoSemDeclaracao,
    TiposSemCobertura,
    TipoMalClassificado,
    TipoCanonicoForaDoSuportado,
    ParticaoSemPai,
    HerancaSemMae,
    SequenceComUmaColuna,
    SequenceComColunaRepetida,
    GeradaNaoMarcada,
    GeradaNaCopia,
    ChaveForaDeOrdem,
    ChaveDesbalanceada,
    TabelaDuplicada,
    TemporalSemHistorico,
    ResiduoAusente,
    SegurancaDeLinhaAusente,
    MascaraAusente,
    NomeNaCaixaErrada,
    CitacaoSemEscape,
    CitacaoErradaNoDestino,
    EsquemaDoIncognitoLido,
    IncognitoForaDoEsquema,
    TabelasForaDeOrdem,
    LeituraInstavel,
    IdentidadeInstavel,
    BancoErrado,
    LinhagemVazia,
    InventaRecursos,

    // O inventário: cada um mexe num objeto que nenhum outro caso declara.
    InventarioVazio,
    TabelaOmitida,
    TabelaOmitidaNoDestino,
    ColunaAMais,
    ColunaAMenos,
    ColunasForaDeOrdem,
    PropriedadeDeColunaTrocada,
    ChaveEstrangeiraRepetida,
    ChaveEstrangeiraComReferenciadaErrada,
    ChavePrimariaForaDeOrdem,
    TipoDeTabelaTrocado,
    SequenceOmitida,
    SituacaoTrocadaNoDestino,

    // A estrutura que só a consistência e o inventário conferem.
    OutroPaiForaDoEsquema,
    ChaveDeParticaoAusente,
    ChaveDeParticaoComColunaInexistente,
    ChaveDeParticaoVaziaCompleta,
    MaeDeTipoErrado,

    // A classe concreta da suíte aninhada em outra, ou aberta.
    SuiteAninhada,
    SuiteAberta,
}

/// <summary>
/// O banco do dialeto falso: um esquema completo (todos os recursos do catálogo) ou um sem os recursos opcionais, e o
/// que a fixture declara sobre cada um. Nomes fictícios.
/// </summary>
internal static class BancoFalso
{
    private const string Loja = "loja";

    internal static NomeObjeto Nome(string nome) => new(Loja, nome);

    internal static ReferenciaColuna Coluna(string tabela, string coluna) => new(Nome(tabela), coluna);

    /// <summary>O esquema que o banco tem de verdade, inclusive o do Incognito, fora de ordem.</summary>
    internal static List<TabelaEsquema> Tabelas(bool completo)
    {
        var tabelas = new List<TabelaEsquema>
        {
            Tabela(
                "tipos",
                Texto("texto"),
                Comum("dados", "jsonb", SituacaoTipo.ComRessalva),
                Comum("versao", "rowversion", SituacaoTipo.SuportadoPorExclusao),
                new ColunaEsquema { Nome = "codigo", TipoNativo = "loja.codigo", Situacao = SituacaoTipo.ComRessalva, TipoBase = "text" }),
            Tabela("Cliente", Texto("nome")),
            Tabela("cliente", Texto("nome")),
            Tabela("select", Texto("nome")),
            Tabela("nota fiscal", Texto("numero")),
            new()
            {
                Nome = Nome("pedidos"),
                Tipo = completo ? TipoTabela.Particionada : TipoTabela.Comum,
                ChaveParticao = completo ? new ChaveParticao { Colunas = ["loja", "id"] } : null,
                Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")],
                ChavePrimaria = ["loja", "id"],
            },
            new()
            {
                Nome = Nome("itens"),
                Colunas = [Inteiro("pedido", anulavel: false), Inteiro("loja", anulavel: false), Inteiro("numero")],
                ChavesEstrangeiras =
                [
                    new() { Nome = "fk_itens_pedidos", Colunas = ["loja", "pedido"], Referenciada = Nome("pedidos"), ColunasReferenciadas = ["loja", "id"] },
                ],
            },
            new() { Nome = new NomeObjeto("incognito", "execucao"), Colunas = [Texto("status")] },
        };

        if (completo)
        {
            tabelas.AddRange(
            [
                new() { Nome = Nome("pedidos_2025"), Tipo = TipoTabela.Particao, Pai = Nome("pedidos"), Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")] },
                new() { Nome = Nome("pedidos_2026"), Tipo = TipoTabela.Particao, Pai = Nome("pedidos"), Colunas = [Inteiro("loja", anulavel: false), Inteiro("id", anulavel: false), Inteiro("numero")] },
                Tabela("pessoa", Texto("nome")),
                new() { Nome = Nome("funcionario"), Tipo = TipoTabela.Herdeira, Pai = Nome("pessoa"), Colunas = [Texto("nome"), Texto("cargo")] },
                Tabela("contato", Texto("canal")),
                Tabela("endereco", Texto("cidade")),
                Tabela("telefone", Texto("numero")),
                new()
                {
                    Nome = Nome("ficha"),
                    Tipo = TipoTabela.Herdeira,
                    Pai = Nome("contato"),
                    OutrosPais = [Nome("endereco"), Nome("telefone")],
                    Colunas = [Texto("canal"), Texto("cidade"), Texto("numero")],
                    ChavesEstrangeiras = [new() { Nome = "fk_ficha_contato", Colunas = ["canal"], Referenciada = Nome("contato"), ColunasReferenciadas = ["canal"] }],
                },
                Tabela("calculos", Inteiro("base"), new ColunaEsquema { Nome = "dobro", TipoNativo = "integer", Situacao = SituacaoTipo.SuportadoPorExclusao, Geracao = GeracaoColuna.Armazenada }),
                Tabela("contadores", new ColunaEsquema { Nome = "sempre", TipoNativo = "bigint", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Inteiro, Identidade = IdentidadeColuna.Sempre }),
                new() { Nome = Nome("contratos"), Tipo = TipoTabela.Temporal, Historico = Nome("contratos_historico"), Colunas = [Texto("numero")] },
                new() { Nome = Nome("contratos_historico"), Tipo = TipoTabela.HistoricoDeTemporal, Colunas = [Texto("numero")] },
                new() { Nome = Nome("segredos"), SegurancaDeLinha = true, Colunas = [Texto("valor")] },
                Tabela("contatos", new ColunaEsquema { Nome = "email", TipoNativo = "text", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Texto, Mascarada = true }),
                new() { Nome = Nome("externa"), Estrangeira = true, Colunas = [Comum("linha", "text", SituacaoTipo.NaoSuportado)] },
                new() { Nome = Nome("da_extensao"), Extensao = "extensao_falsa", Colunas = [Inteiro("id")] },
            ]);
        }

        return tabelas;
    }

    internal static List<SequenceEsquema> Sequences(bool completo)
    {
        var sequences = new List<SequenceEsquema>
        {
            new() { Nome = Nome("seq_numero"), Colunas = completo ? [Coluna("pedidos", "numero"), Coluna("itens", "numero")] : [Coluna("pedidos", "numero")] },
            new() { Nome = new NomeObjeto("incognito", "seq_execucao"), Colunas = [] },
        };
        if (completo)
        {
            sequences.Add(new() { Nome = Nome("seq_avulsa"), Colunas = [] });
            sequences.Add(new() { Nome = Nome("contadores_sempre_seq"), Colunas = [Coluna("contadores", "sempre")] });
        }

        return sequences;
    }

    internal static List<ObjetoResidual> Residuais(bool completo) =>
        completo ? [new() { Nome = Nome("resumo_vendas"), Tipo = TipoObjetoResidual.VisaoMaterializada }] : [];

    /// <summary>O que a fixture declara sobre o banco.</summary>
    internal static EsquemaDeConformidade Esperado(bool completo, Defeito defeito) => new()
    {
        Inventario = defeito == Defeito.InventarioVazio ? new Inventario([], []) : Inventario(completo),
        Tipos = defeito == Defeito.TiposSemCobertura
            ? [new(Coluna("tipos", "texto"), SituacaoTipo.Suportado, TipoColuna.Texto)]
            :
            [
                new(Coluna("tipos", "texto"), SituacaoTipo.Suportado, TipoColuna.Texto),
                new(Coluna("tipos", "dados"), SituacaoTipo.ComRessalva, null),
                new(Coluna("tipos", "versao"), SituacaoTipo.SuportadoPorExclusao, null),
                new(Coluna("tipos", "codigo"), SituacaoTipo.ComRessalva, null),
            ],
        Particao = completo
            ? Caso.Presente(new Hierarquia(Nome("pedidos"), [Nome("pedidos_2025"), Nome("pedidos_2026")]))
            : Caso.Inexistente<Hierarquia>("banco falso sem partições"),
        Heranca = completo
            ? Caso.Presente(new Hierarquia(Nome("pessoa"), [Nome("funcionario")]))
            : Caso.Inexistente<Hierarquia>("banco falso sem herança"),
        SequenceCompartilhada = completo
            ? Caso.Presente(new SequenceEsperada(Nome("seq_numero"), [Coluna("itens", "numero"), Coluna("pedidos", "numero")]))
            : Caso.Inexistente<SequenceEsperada>("banco falso sem sequence compartilhada"),
        ColunasGeradas = completo
            ? Caso.Presente<IReadOnlyList<ColunaGeradaEsperada>>([new(Coluna("calculos", "dobro"), GeracaoColuna.Armazenada)])
            : Caso.Inexistente<IReadOnlyList<ColunaGeradaEsperada>>("banco falso sem colunas geradas"),
        ChaveEstrangeiraComposta = new(Nome("itens"), "fk_itens_pedidos", ["loja", "pedido"], Nome("pedidos"), ["loja", "id"]),
        Temporal = completo
            ? Caso.Presente(new TemporalEsperada(Nome("contratos"), Nome("contratos_historico")))
            : Caso.Inexistente<TemporalEsperada>("banco falso sem temporais"),
        ObjetosResiduais = completo ? [new(Nome("resumo_vendas"), TipoObjetoResidual.VisaoMaterializada)] : [],
        SegurancaDeLinha = completo ? Caso.Presente(Nome("segredos")) : Caso.Inexistente<NomeObjeto>("banco falso sem segurança por linha"),
        Mascara = completo ? Caso.Presente(Coluna("contatos", "email")) : Caso.Inexistente<ReferenciaColuna>("banco falso sem máscara"),

        // Um caso sem declaração: o 'required' não impede null!.
        NomesExatos = defeito == Defeito.CasoSemDeclaracao ? null! : [Nome("Cliente"), Nome("cliente"), Nome("select"), Nome("nota fiscal")],
        Citacoes = [new("Cliente", "\"Cliente\""), new("a\"b", "\"a\"\"b\"")],
        ObjetoDoIncognito = defeito == Defeito.IncognitoForaDoEsquema ? Nome("execucao") : new NomeObjeto("incognito", "execucao"),
    };

    // O inventário do falso é a projeção do banco certo (no dialeto de verdade, ele é escrito à mão a partir do DDL).
    private static Inventario Inventario(bool completo) => new(
        [
            .. Tabelas(completo).Where(tabela => tabela.Nome.Esquema != "incognito").Select(tabela => new TabelaInventariada
            {
                Nome = tabela.Nome,
                Tipo = tabela.Tipo,
                Pai = tabela.Pai,
                OutrosPais = tabela.OutrosPais,
                Historico = tabela.Historico,
                ChaveParticao = tabela.ChaveParticao is { } chave ? new ChaveParticaoEsperada(chave.Colunas, chave.ColunasIncompletas) : null,
                Estrangeira = tabela.Estrangeira,
                Extensao = tabela.Extensao,
                SegurancaDeLinha = tabela.SegurancaDeLinha,
                Colunas =
                [
                    .. tabela.Colunas.Select(coluna => new ColunaInventariada
                    {
                        Nome = coluna.Nome,
                        TipoNativo = coluna.TipoNativo,
                        Anulavel = coluna.Anulavel,
                        Identidade = coluna.Identidade,
                        Geracao = coluna.Geracao,
                        TipoBase = coluna.TipoBase,
                    }),
                ],
                ChavePrimaria = tabela.ChavePrimaria,
                ChavesEstrangeiras = [.. tabela.ChavesEstrangeiras.Select(chave => new ChaveEstrangeiraInventariada(chave.Nome, chave.Colunas, chave.Referenciada, chave.ColunasReferenciadas))],
            }),
        ],
        [.. Sequences(completo).Where(sequence => sequence.Nome.Esquema != "incognito").Select(sequence => new SequenceEsperada(sequence.Nome, sequence.Colunas))]);

    private static TabelaEsquema Tabela(string nome, params ColunaEsquema[] colunas) => new() { Nome = Nome(nome), Colunas = colunas };

    private static ColunaEsquema Texto(string nome) => new() { Nome = nome, TipoNativo = "text", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Texto, Anulavel = true };

    private static ColunaEsquema Inteiro(string nome, bool anulavel = true) =>
        new() { Nome = nome, TipoNativo = "bigint", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Inteiro, Anulavel = anulavel };

    private static ColunaEsquema Comum(string nome, string tipoNativo, SituacaoTipo situacao) => new() { Nome = nome, TipoNativo = tipoNativo, Situacao = situacao, Anulavel = true };
}

/// <summary>O dialeto falso: lê o banco em memória e aplica o defeito pedido e a perturbação, se há.</summary>
internal sealed class ProvedorFalso(bool completo, Defeito defeito, Func<EsquemaBanco, EsquemaBanco>? perturbar) : ProvedorDialeto
{
    public override string Nome => "falso";

    public override Task<DialetoOrigem> ConectarOrigemAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Task.FromResult<DialetoOrigem>(new OrigemFalsa(conexao.Revelar(), new LeitorFalso(completo, defeito, perturbar, destino: false), defeito));

    public override Task<DialetoDestino> ConectarDestinoAsync(SegredoConexao conexao, CancellationToken cancelamento) =>
        Task.FromResult<DialetoDestino>(new DestinoFalso(conexao.Revelar(), new LeitorFalso(completo, defeito, perturbar, destino: true), defeito));
}

internal sealed class OrigemFalsa(string banco, LeitorEsquema leitor, Defeito defeito) : DialetoOrigem
{
    public override LeitorEsquema Esquema { get; } = leitor;

    public override IdentidadeBanco Identidade { get; } = new IdentidadeFalsa(banco, defeito);

    public override DialetoSql Sql { get; } = new SqlFalso(defeito is Defeito.CitacaoSemEscape);
}

// O destino da suíte tem o mesmo esquema da origem, lido pelo dono.
internal sealed class DestinoFalso(string banco, LeitorEsquema leitor, Defeito defeito) : DialetoDestino
{
    public override LeitorEsquema Esquema { get; } = leitor;

    public override IdentidadeBanco Identidade { get; } = new IdentidadeFalsa(banco, defeito);

    public override DialetoSql Sql { get; } = new SqlFalso(defeito is Defeito.CitacaoSemEscape or Defeito.CitacaoErradaNoDestino);
}

internal sealed class LeitorFalso(bool completo, Defeito defeito, Func<EsquemaBanco, EsquemaBanco>? perturbar, bool destino) : LeitorEsquema
{
    private int _leituras;

    public override Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        _leituras++;
        var comRecursos = completo || defeito == Defeito.InventaRecursos;
        var tabelas = BancoFalso.Tabelas(comRecursos);
        var sequences = BancoFalso.Sequences(comRecursos);
        var residuais = BancoFalso.Residuais(comRecursos);

        if (defeito != Defeito.EsquemaDoIncognitoLido)
        {
            tabelas.RemoveAll(tabela => tabela.Nome.Esquema == "incognito");
            sequences.RemoveAll(sequence => sequence.Nome.Esquema == "incognito");
        }

        Aplicar(tabelas, sequences, residuais);

        // Em ordem de esquema e nome, como o contrato pede.
        var ordem = Comparer<TabelaEsquema>.Create((uma, outra) => Comparar(uma.Nome, outra.Nome));
        tabelas.Sort(ordem);
        sequences.Sort((uma, outra) => Comparar(uma.Nome, outra.Nome));
        residuais.Sort((um, outro) => Comparar(um.Nome, outro.Nome));
        if (defeito == Defeito.TabelasForaDeOrdem || (defeito == Defeito.LeituraInstavel && _leituras > 1))
        {
            tabelas.Reverse();
        }

        var esquema = new EsquemaBanco { Tabelas = tabelas, Sequences = sequences, ObjetosResiduais = residuais };
        return Task.FromResult(perturbar is null ? esquema : perturbar(esquema));
    }

    private void Aplicar(List<TabelaEsquema> tabelas, List<SequenceEsquema> sequences, List<ObjetoResidual> residuais)
    {
        switch (defeito)
        {
            case Defeito.TipoMalClassificado:
                TrocarColuna(tabelas, "tipos", "dados", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Texto, Anulavel = c.Anulavel });
                break;
            case Defeito.TipoCanonicoForaDoSuportado:
                TrocarColuna(tabelas, "calculos", "dobro", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = c.Situacao, Geracao = c.Geracao, Tipo = TipoColuna.Inteiro });
                break;
            case Defeito.ParticaoSemPai:
                Trocar(tabelas, "pedidos_2026", tabela => Com(tabela, semPai: true));
                break;
            case Defeito.HerancaSemMae:
                Trocar(tabelas, "funcionario", tabela => Com(tabela, semPai: true));
                break;
            case Defeito.SequenceComUmaColuna:
                TrocarSequence(sequences, "seq_numero", s => new SequenceEsquema { Nome = s.Nome, Colunas = [s.Colunas[0]] });
                break;
            case Defeito.SequenceComColunaRepetida:
                TrocarSequence(sequences, "seq_numero", s => new SequenceEsquema { Nome = s.Nome, Colunas = [.. s.Colunas, s.Colunas[0]] });
                break;
            case Defeito.GeradaNaoMarcada:
                TrocarColuna(tabelas, "calculos", "dobro", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = c.Situacao });
                break;
            case Defeito.GeradaNaCopia:
                TrocarColuna(tabelas, "calculos", "dobro", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Inteiro, Geracao = c.Geracao });
                break;
            case Defeito.ChaveForaDeOrdem:
                TrocarChave(tabelas, f => [new ChaveEstrangeira { Nome = f.Nome, Colunas = [.. f.Colunas.Reverse()], Referenciada = f.Referenciada, ColunasReferenciadas = [.. f.ColunasReferenciadas.Reverse()] }]);
                break;
            case Defeito.ChaveDesbalanceada:
                TrocarChave(tabelas, f => [new ChaveEstrangeira { Nome = f.Nome, Colunas = f.Colunas, Referenciada = f.Referenciada, ColunasReferenciadas = [f.ColunasReferenciadas[0]] }]);
                break;
            case Defeito.TabelaDuplicada:
                tabelas.Add(tabelas.Single(tabela => tabela.Nome.Equals(BancoFalso.Nome("itens"))));
                break;
            case Defeito.TemporalSemHistorico:
                Trocar(tabelas, "contratos", tabela => Com(tabela, semHistorico: true));
                break;
            case Defeito.ResiduoAusente:
                residuais.Clear();
                break;
            case Defeito.SegurancaDeLinhaAusente:
                Trocar(tabelas, "segredos", tabela => Com(tabela, semSeguranca: true));
                break;
            case Defeito.MascaraAusente:
                TrocarColuna(tabelas, "contatos", "email", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = c.Situacao, Tipo = c.Tipo, Anulavel = c.Anulavel });
                break;
            case Defeito.NomeNaCaixaErrada:
                Trocar(tabelas, "Cliente", tabela => new TabelaEsquema { Nome = BancoFalso.Nome("CLIENTE"), Colunas = tabela.Colunas });
                break;
            case Defeito.TabelaOmitida:
            case Defeito.TabelaOmitidaNoDestino when destino:
                tabelas.RemoveAll(tabela => tabela.Nome.Equals(BancoFalso.Nome("da_extensao")));
                break;
            case Defeito.ColunaAMais:
                Trocar(tabelas, "endereco", tabela => Com(tabela, colunas: [.. tabela.Colunas, Copiar(tabela.Colunas[0], "bairro")]));
                break;
            case Defeito.ColunaAMenos:
                Trocar(tabelas, "ficha", tabela => Com(tabela, colunas: [tabela.Colunas[0]]));
                break;
            case Defeito.ColunasForaDeOrdem:
                Trocar(tabelas, "ficha", tabela => Com(tabela, colunas: [.. tabela.Colunas.Reverse()]));
                break;
            case Defeito.PropriedadeDeColunaTrocada:
                TrocarColuna(tabelas, "contato", "canal", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = c.Situacao, Tipo = c.Tipo, Anulavel = !c.Anulavel });
                break;
            case Defeito.ChaveEstrangeiraRepetida:
                TrocarChave(tabelas, f => [f, f]);
                break;
            case Defeito.ChaveEstrangeiraComReferenciadaErrada:
                TrocarChave(tabelas, f => [new ChaveEstrangeira { Nome = f.Nome, Colunas = f.Colunas, Referenciada = BancoFalso.Nome("ficha"), ColunasReferenciadas = f.ColunasReferenciadas }], "ficha");
                break;
            case Defeito.ChavePrimariaForaDeOrdem:
                Trocar(tabelas, "pedidos", tabela => Com(tabela, chavePrimaria: [.. tabela.ChavePrimaria.Reverse()]));
                break;
            case Defeito.TipoDeTabelaTrocado:
                Trocar(tabelas, "contadores", tabela => Com(tabela, tipo: TipoTabela.Herdeira));
                break;
            case Defeito.SituacaoTrocadaNoDestino when destino:
                TrocarColuna(tabelas, "tipos", "dados", c => new ColunaEsquema { Nome = c.Nome, TipoNativo = c.TipoNativo, Situacao = SituacaoTipo.NaoSuportado, Anulavel = c.Anulavel });
                break;
            case Defeito.SequenceOmitida:
                sequences.RemoveAll(sequence => sequence.Nome.Equals(BancoFalso.Nome("seq_avulsa")));
                break;
            case Defeito.OutroPaiForaDoEsquema:
                Trocar(tabelas, "ficha", tabela => Com(tabela, outrosPais: [BancoFalso.Nome("endereco_antigo")]));
                break;
            case Defeito.ChaveDeParticaoAusente:
                Trocar(tabelas, "pedidos", tabela => Com(tabela, semChaveParticao: true));
                break;
            case Defeito.ChaveDeParticaoComColunaInexistente:
                Trocar(tabelas, "pedidos", tabela => Com(tabela, chaveParticao: new ChaveParticao { Colunas = ["filial"] }));
                break;
            case Defeito.ChaveDeParticaoVaziaCompleta:
                Trocar(tabelas, "pedidos", tabela => Com(tabela, chaveParticao: new ChaveParticao { Colunas = [] }));
                break;
            case Defeito.MaeDeTipoErrado:
                Trocar(tabelas, "ficha", tabela => Com(tabela, outrosPais: [BancoFalso.Nome("endereco"), BancoFalso.Nome("pedidos_2025")]));
                break;
        }
    }

    private static int Comparar(NomeObjeto um, NomeObjeto outro)
    {
        var esquema = string.CompareOrdinal(um.Esquema, outro.Esquema);
        return esquema != 0 ? esquema : string.CompareOrdinal(um.Nome, outro.Nome);
    }

    private static void Trocar(List<TabelaEsquema> tabelas, string nome, Func<TabelaEsquema, TabelaEsquema> trocar)
    {
        var indice = tabelas.FindIndex(tabela => tabela.Nome.Equals(BancoFalso.Nome(nome)));
        tabelas[indice] = trocar(tabelas[indice]);
    }

    private static void TrocarSequence(List<SequenceEsquema> sequences, string nome, Func<SequenceEsquema, SequenceEsquema> trocar)
    {
        var indice = sequences.FindIndex(sequence => sequence.Nome.Equals(BancoFalso.Nome(nome)));
        sequences[indice] = trocar(sequences[indice]);
    }

    private static void TrocarColuna(List<TabelaEsquema> tabelas, string tabela, string coluna, Func<ColunaEsquema, ColunaEsquema> trocar) =>
        Trocar(tabelas, tabela, lida => Com(lida, colunas: [.. lida.Colunas.Select(c => c.Nome == coluna ? trocar(c) : c)]));

    private static void TrocarChave(List<TabelaEsquema> tabelas, Func<ChaveEstrangeira, IEnumerable<ChaveEstrangeira>> trocar, string tabela = "itens") =>
        Trocar(tabelas, tabela, lida => Com(lida, chaves: [.. lida.ChavesEstrangeiras.SelectMany(trocar)]));

    private static ColunaEsquema Copiar(ColunaEsquema coluna, string nome) =>
        new() { Nome = nome, TipoNativo = coluna.TipoNativo, Situacao = coluna.Situacao, Tipo = coluna.Tipo, Anulavel = coluna.Anulavel };

    private static TabelaEsquema Com(
        TabelaEsquema tabela, IReadOnlyList<ColunaEsquema>? colunas = null, IReadOnlyList<ChaveEstrangeira>? chaves = null,
        IReadOnlyList<NomeObjeto>? outrosPais = null, ChaveParticao? chaveParticao = null, IReadOnlyList<string>? chavePrimaria = null, TipoTabela? tipo = null,
        bool semPai = false, bool semHistorico = false, bool semSeguranca = false, bool semChaveParticao = false) => new()
        {
            Nome = tabela.Nome,
            Tipo = tipo ?? tabela.Tipo,
            Pai = semPai ? null : tabela.Pai,
            OutrosPais = outrosPais ?? tabela.OutrosPais,
            Historico = semHistorico ? null : tabela.Historico,
            ChaveParticao = semChaveParticao ? null : chaveParticao ?? tabela.ChaveParticao,
            Estrangeira = tabela.Estrangeira,
            Extensao = tabela.Extensao,
            Colunas = colunas ?? tabela.Colunas,
            ChavePrimaria = chavePrimaria ?? tabela.ChavePrimaria,
            ChavesEstrangeiras = chaves ?? tabela.ChavesEstrangeiras,
            SegurancaDeLinha = !semSeguranca && tabela.SegurancaDeLinha,
        };
}

internal sealed class IdentidadeFalsa(string banco, Defeito defeito) : IdentidadeBanco
{
    private int _leituras;

    public override Task<IdentidadeLida> LerAsync(CancellationToken cancelamento)
    {
        _leituras++;
        return Task.FromResult(new IdentidadeLida
        {
            Banco = defeito == Defeito.BancoErrado ? "outro-banco" : banco,
            Servidor = defeito == Defeito.IdentidadeInstavel ? $"servidor-{_leituras}" : "servidor-falso",
            Linhagem = defeito == Defeito.LinhagemVazia ? new Dictionary<string, string>() : new Dictionary<string, string> { ["identificador"] = "7" },
        });
    }
}

internal sealed class SqlFalso(bool semEscape) : DialetoSql
{
    public override string Citar(string identificador) =>
        "\"" + (semEscape ? identificador : identificador.Replace("\"", "\"\"", StringComparison.Ordinal)) + "\"";
}

/// <summary>A fixture do dialeto falso: um par de bancos em memória por teste, com o mesmo esquema nos dois lados.</summary>
internal sealed class FixtureFalsa(bool completo, Defeito defeito, Func<EsquemaBanco, EsquemaBanco>? perturbar = null) : FixtureDialeto
{
    private readonly ProvedorFalso _provedor = new(completo, defeito, perturbar);

    public override EsquemaDeConformidade Esperado { get; } = BancoFalso.Esperado(completo, defeito);

    public override async ValueTask<ParDeBancos> CriarParAsync(CancellationToken cancelamento)
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var origem = await _provedor.ConectarOrigemAsync(new SegredoConexao($"origem-{sufixo}"), cancelamento);
        var destino = await _provedor.ConectarDestinoAsync(new SegredoConexao($"destino-{sufixo}"), cancelamento);
        return new ParDeBancos(origem, destino, $"origem-{sufixo}", $"destino-{sufixo}", () => ValueTask.CompletedTask);
    }
}
