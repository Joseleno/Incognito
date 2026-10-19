using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Conformidade;

/// <summary>
/// A suíte de conformidade de dialeto: todo dialeto do repositório a herda numa classe concreta vazia do próprio projeto
/// de testes (<c>public sealed class ConformidadePostgreSqlTestes(FixturePostgreSql f) : ConformidadeDialetoTestes&lt;FixturePostgreSql&gt;(f);</c>)
/// e passa em tudo, sem teste pulado.
/// </summary>
/// <remarks>
/// <para>
/// Regras da suíte, porque ela também roda por reflexão contra um dialeto falso: só <c>[Fact]</c> sem parâmetros, sem
/// <c>Skip</c>, <c>Explicit</c> nem <c>Assert.Skip</c>, sem <c>ITestOutputHelper</c>; falha sempre por asserção, com a
/// mensagem começando pelo recurso ("Tipos:", "Partição:") e dizendo o objeto. Um recurso que o banco não tem é
/// declarado inexistente pela fixture, e a suíte confere que o leitor não o inventa.
/// </para>
/// <para>Cada teste recebe um par de bancos novo, criado antes e apagado depois.</para>
/// </remarks>
/// <typeparam name="TFixture">A fixture do dialeto.</typeparam>
/// <param name="fixture">A fixture.</param>
public abstract class ConformidadeDialetoTestes<TFixture>(TFixture fixture) : IClassFixture<TFixture>, IAsyncLifetime
    where TFixture : FixtureDialeto
{
    private ParDeBancos? _par;

    /// <summary>O par de bancos do teste.</summary>
    protected ParDeBancos Par => _par ?? throw new InvalidOperationException("O par de bancos só existe durante o teste.");

    /// <summary>O que a fixture declarou.</summary>
    protected EsquemaDeConformidade Esperado => fixture.Esperado;

    /// <summary>Cria o par de bancos do teste.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask InitializeAsync() => _par = await fixture.CriarParAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

    /// <summary>Descarta o par de bancos do teste.</summary>
    /// <returns>A tarefa.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_par is not null)
        {
            await _par.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void Todo_caso_do_catalogo_tem_declaracao()
    {
        var semDeclaracao = typeof(EsquemaDeConformidade).GetProperties()
            .Where(propriedade => propriedade.GetValue(Esperado) is null)
            .Select(propriedade => propriedade.Name)
            .ToArray();

        Assert.True(semDeclaracao.Length == 0, $"Declaração: casos sem declaração na fixture: {string.Join(", ", semDeclaracao)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_lido_e_consistente()
    {
        var esquema = await LerOrigemAsync();
        var problemas = Inconsistencias(esquema).ToArray();

        Assert.True(problemas.Length == 0, $"Consistência: {string.Join("; ", problemas)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_vem_em_ordem_de_esquema_e_nome()
    {
        var esquema = await LerOrigemAsync();

        Assert.True(EmOrdem(esquema.Tabelas.Select(tabela => tabela.Nome)), "Ordem: tabelas fora da ordem ordinal de esquema e nome.");
        Assert.True(EmOrdem(esquema.Sequences.Select(sequence => sequence.Nome)), "Ordem: sequences fora da ordem ordinal de esquema e nome.");
        Assert.True(EmOrdem(esquema.ObjetosResiduais.Select(objeto => objeto.Nome)), "Ordem: objetos residuais fora da ordem ordinal de esquema e nome.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_tipos_da_matriz_sao_classificados()
    {
        var esquema = await LerOrigemAsync();
        var situacoes = Esperado.Tipos.Select(tipo => tipo.Situacao).ToHashSet();

        Assert.True(
            situacoes.IsSupersetOf([SituacaoTipo.Suportado, SituacaoTipo.ComRessalva, SituacaoTipo.SuportadoPorExclusao]),
            "Tipos: a matriz declarada precisa cobrir as situações suportado, com ressalva e suportado por exclusão.");
        var divergentes = Esperado.Tipos
            .Select(esperada => (esperada, coluna: Coluna(esquema, esperada.Coluna, "Tipos")))
            .Where(par => (par.coluna.Situacao, par.coluna.Tipo) != (par.esperada.Situacao, par.esperada.Tipo))
            .Select(par => $"{par.esperada.Coluna} ({par.coluna.TipoNativo}) lida como {par.coluna.Situacao}/{par.coluna.Tipo}, esperada {par.esperada.Situacao}/{par.esperada.Tipo}")
            .ToArray();
        Assert.True(divergentes.Length == 0, $"Tipos: {string.Join("; ", divergentes)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_particionada_e_as_particoes_apontam_o_pai() =>
        ConferirHierarquia(await LerOrigemAsync(), Esperado.Particao, [TipoTabela.Particionada], TipoTabela.Particao, "Partição");

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_herdeiras_apontam_a_mae() =>
        ConferirHierarquia(await LerOrigemAsync(), Esperado.Heranca, [TipoTabela.Comum, TipoTabela.Herdeira], TipoTabela.Herdeira, "Herança");

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_sequence_compartilhada_e_vista_por_todas_as_colunas()
    {
        var esquema = await LerOrigemAsync();
        var compartilhadas = esquema.Sequences.Where(sequence => sequence.Colunas.Select(coluna => coluna.Tabela).Distinct().Count() > 1).ToArray();
        if (!Esperado.SequenceCompartilhada.Existe)
        {
            Assert.True(compartilhadas.Length == 0, $"Sequence compartilhada lida num banco que a declara inexistente: {string.Join(", ", compartilhadas.Select(s => s.Nome))}.");
            return;
        }

        var esperada = Esperado.SequenceCompartilhada.Valor;
        var lidas = esquema.Sequences.Where(sequence => sequence.Nome.Equals(esperada.Nome)).ToArray();
        Assert.True(lidas.Length == 1, $"Sequence compartilhada: {esperada.Nome} lida {lidas.Length} vezes.");
        Assert.True(
            MesmoConjunto(lidas[0].Colunas, esperada.Colunas),
            $"Sequence compartilhada: {esperada.Nome} lida com [{string.Join(", ", lidas[0].Colunas)}], esperada com [{string.Join(", ", esperada.Colunas)}].");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_colunas_geradas_sao_marcadas_e_ficam_fora_da_copia()
    {
        var esquema = await LerOrigemAsync();
        var lidas = esquema.Tabelas
            .SelectMany(tabela => tabela.Colunas.Where(coluna => coluna.Geracao != GeracaoColuna.Nenhuma).Select(coluna => (Referencia: new ReferenciaColuna(tabela.Nome, coluna.Nome), coluna)))
            .ToArray();
        var esperadas = Esperado.ColunasGeradas.Existe ? Esperado.ColunasGeradas.Valor : [];

        Assert.True(
            MesmoConjunto(lidas.Select(lida => new ColunaGeradaEsperada(lida.Referencia, lida.coluna.Geracao)).ToArray(), esperadas),
            $"Colunas geradas: lidas [{string.Join(", ", lidas.Select(l => $"{l.Referencia} {l.coluna.Geracao}"))}], esperadas [{string.Join(", ", esperadas.Select(e => $"{e.Coluna} {e.Geracao}"))}].");
        var naCopia = lidas.Where(lida => lida.coluna.Situacao != SituacaoTipo.SuportadoPorExclusao).Select(lida => lida.Referencia.ToString()).ToArray();
        Assert.True(naCopia.Length == 0, $"Colunas geradas: sem a situação 'suportado por exclusão': {string.Join(", ", naCopia)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_chave_estrangeira_composta_mantem_a_ordem_da_chave()
    {
        const string Recurso = "Chave estrangeira composta";
        var esperada = Esperado.ChaveEstrangeiraComposta;
        var tabela = Tabela(await LerOrigemAsync(), esperada.Tabela, Recurso);
        var lidas = tabela.ChavesEstrangeiras.Where(chave => chave.Nome == esperada.Nome).ToArray();

        Assert.True(lidas.Length == 1, $"{Recurso}: {esperada.Nome} lida {lidas.Length} vezes em {esperada.Tabela}.");
        var lida = lidas[0];
        Assert.True(
            lida.Colunas.SequenceEqual(esperada.Colunas) && lida.Referenciada.Equals(esperada.Referenciada) && lida.ColunasReferenciadas.SequenceEqual(esperada.ColunasReferenciadas),
            $"{Recurso}: {esperada.Nome} lida como ({string.Join(", ", lida.Colunas)}) → {lida.Referenciada} ({string.Join(", ", lida.ColunasReferenciadas)}), esperada ({string.Join(", ", esperada.Colunas)}) → {esperada.Referenciada} ({string.Join(", ", esperada.ColunasReferenciadas)}).");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_temporal_aponta_o_historico()
    {
        var esquema = await LerOrigemAsync();
        var temporais = esquema.Tabelas.Where(tabela => tabela.Tipo is TipoTabela.Temporal or TipoTabela.HistoricoDeTemporal).Select(tabela => tabela.Nome).ToArray();
        if (!Esperado.Temporal.Existe)
        {
            Assert.True(temporais.Length == 0, $"Temporal lida num banco que a declara inexistente: {string.Join(", ", temporais.Select(n => n.ToString()))}.");
            return;
        }

        var esperada = Esperado.Temporal.Valor;
        var temporal = Tabela(esquema, esperada.Tabela, "Temporal");
        Assert.True(
            temporal.Tipo == TipoTabela.Temporal && esperada.Historico.Equals(temporal.Historico),
            $"Temporal: {esperada.Tabela} lida como {temporal.Tipo} com histórico {temporal.Historico}, esperada temporal com {esperada.Historico}.");
        Assert.True(Tabela(esquema, esperada.Historico, "Temporal").Tipo == TipoTabela.HistoricoDeTemporal, $"Temporal: {esperada.Historico} não lida como histórico.");
        Assert.True(
            MesmoConjunto(temporais, [esperada.Tabela, esperada.Historico]),
            $"Temporal: tabelas temporais ou de histórico lidas [{string.Join(", ", temporais.Select(n => n.ToString()))}], esperadas {esperada.Tabela} e {esperada.Historico}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_objetos_residuais_sao_inventariados()
    {
        var lidos = (await LerOrigemAsync()).ObjetosResiduais.Select(objeto => new ObjetoResidualEsperado(objeto.Nome, objeto.Tipo)).ToArray();

        Assert.True(
            MesmoConjunto(lidos, Esperado.ObjetosResiduais),
            $"Objetos residuais: lidos [{string.Join(", ", lidos.Select(o => $"{o.Nome} {o.Tipo}"))}], esperados [{string.Join(", ", Esperado.ObjetosResiduais.Select(o => $"{o.Nome} {o.Tipo}"))}].");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_e_apontada()
    {
        var comSeguranca = (await LerOrigemAsync()).Tabelas.Where(tabela => tabela.SegurancaDeLinha).Select(tabela => tabela.Nome).ToArray();
        NomeObjeto[] esperadas = Esperado.SegurancaDeLinha.Existe ? [Esperado.SegurancaDeLinha.Valor] : [];

        Assert.True(
            MesmoConjunto(comSeguranca, esperadas),
            $"Segurança por linha: lida em [{string.Join(", ", comSeguranca.Select(n => n.ToString()))}], esperada em [{string.Join(", ", esperadas.Select(n => n.ToString()))}].");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_mascara_e_apontada()
    {
        var mascaradas = (await LerOrigemAsync()).Tabelas
            .SelectMany(tabela => tabela.Colunas.Where(coluna => coluna.Mascarada).Select(coluna => new ReferenciaColuna(tabela.Nome, coluna.Nome)))
            .ToArray();
        ReferenciaColuna[] esperadas = Esperado.Mascara.Existe ? [Esperado.Mascara.Valor] : [];

        Assert.True(
            MesmoConjunto(mascaradas, esperadas),
            $"Máscara: lida em [{string.Join(", ", mascaradas.Select(c => c.ToString()))}], esperada em [{string.Join(", ", esperadas.Select(c => c.ToString()))}].");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Os_nomes_sao_lidos_como_o_catalogo_os_guarda()
    {
        var esperados = Esperado.NomesExatos;
        Assert.True(esperados is { Count: > 0 }, "Nomes: a fixture precisa declarar nomes que só diferem na caixa, palavra reservada e espaço.");

        var lidos = (await LerOrigemAsync()).Tabelas.Select(tabela => tabela.Nome).ToHashSet();
        var faltando = esperados.Where(nome => !lidos.Contains(nome)).Select(nome => nome.ToString()).ToArray();
        Assert.True(faltando.Length == 0, $"Nomes: não lidos exatamente como o catálogo os guarda: {string.Join(", ", faltando)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public void Os_identificadores_sao_citados_como_o_banco_espera()
    {
        Assert.True(Esperado.Citacoes.Count > 0, "Citação: a fixture precisa declarar identificadores e a forma citada.");
        var divergentes = Esperado.Citacoes
            .SelectMany(esperada => new (string Banco, DialetoBanco Dialeto)[] { ("origem", Par.Origem), ("destino", Par.Destino) }
                .Select(lado => (lado.Banco, esperada, citado: lado.Dialeto.Sql.Citar(esperada.Identificador))))
            .Where(par => par.citado != par.esperada.Citado)
            .Select(par => $"{par.esperada.Identificador} citado como {par.citado} no {par.Banco}, esperado {par.esperada.Citado}")
            .ToArray();

        Assert.True(divergentes.Length == 0, $"Citação: {string.Join("; ", divergentes)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_do_Incognito_fica_fora_da_leitura()
    {
        var esquema = await LerOrigemAsync();
        var doIncognito = esquema.Tabelas.Select(tabela => tabela.Nome)
            .Concat(esquema.Sequences.Select(sequence => sequence.Nome))
            .Concat(esquema.ObjetosResiduais.Select(objeto => objeto.Nome))
            .Where(nome => nome.Esquema == Esperado.ObjetoDoIncognito.Esquema)
            .Select(nome => nome.ToString())
            .ToArray();

        Assert.True(Esperado.ObjetoDoIncognito.Esquema == "incognito", $"Esquema incognito: o objeto declarado ({Esperado.ObjetoDoIncognito}) precisa estar no esquema incognito.");
        Assert.True(doIncognito.Length == 0, $"Esquema incognito: objetos lidos, entre eles o declarado ({Esperado.ObjetoDoIncognito}): {string.Join(", ", doIncognito)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_leitura_do_esquema_e_estavel()
    {
        var primeira = Resumo(await LerOrigemAsync());
        var segunda = Resumo(await LerOrigemAsync());

        Assert.True(primeira == segunda, "Leitura estável: duas leituras seguidas do mesmo esquema deram resultados diferentes.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_identidade_aponta_o_banco_e_e_estavel()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await Par.Origem.Identidade.LerAsync(cancelamento);
        var deNovo = await Par.Origem.Identidade.LerAsync(cancelamento);
        var destino = await Par.Destino.Identidade.LerAsync(cancelamento);

        Assert.True(origem.Banco == Par.BancoOrigem, $"Identidade: banco da origem lido como {origem.Banco}, esperado {Par.BancoOrigem}.");
        Assert.True(destino.Banco == Par.BancoDestino, $"Identidade: banco do destino lido como {destino.Banco}, esperado {Par.BancoDestino}.");
        Assert.True(
            origem.Banco == deNovo.Banco && origem.Servidor == deNovo.Servidor && Linhagem(origem) == Linhagem(deNovo),
            "Identidade: duas leituras seguidas da origem deram resultados diferentes.");
        Assert.True(origem.Linhagem.Count > 0, "Identidade: linhagem vazia.");
    }

    private Task<EsquemaBanco> LerOrigemAsync() => Par.Origem.Esquema.LerAsync(TestContext.Current.CancellationToken);

    private static void ConferirHierarquia(EsquemaBanco esquema, Caso<Hierarquia> caso, TipoTabela[] tiposDoPai, TipoTabela tipoDaFilha, string recurso)
    {
        var filhas = esquema.Tabelas.Where(tabela => tabela.Tipo == tipoDaFilha).ToArray();
        if (!caso.Existe)
        {
            var inventadas = filhas.Select(tabela => tabela.Nome)
                .Concat(esquema.Tabelas.Where(tabela => tabela.Tipo == TipoTabela.Particionada && tipoDaFilha == TipoTabela.Particao).Select(tabela => tabela.Nome))
                .ToArray();
            Assert.True(inventadas.Length == 0, $"{recurso} lida num banco que a declara inexistente: {string.Join(", ", inventadas.Select(n => n.ToString()))}.");
            return;
        }

        var esperada = caso.Valor;
        var pai = Tabela(esquema, esperada.Pai, recurso);
        Assert.True(tiposDoPai.Contains(pai.Tipo), $"{recurso}: {esperada.Pai} lida como {pai.Tipo}, esperada {string.Join(" ou ", tiposDoPai)}.");
        var lidas = filhas.Where(tabela => esperada.Pai.Equals(tabela.Pai)).Select(tabela => tabela.Nome).ToArray();
        Assert.True(
            MesmoConjunto(lidas, esperada.Filhas),
            $"{recurso}: filhas de {esperada.Pai} lidas [{string.Join(", ", lidas.Select(n => n.ToString()))}], esperadas [{string.Join(", ", esperada.Filhas.Select(n => n.ToString()))}].");
    }

    private static TabelaEsquema Tabela(EsquemaBanco esquema, NomeObjeto nome, string recurso)
    {
        var tabelas = esquema.Tabelas.Where(lida => lida.Nome.Equals(nome)).ToArray();
        Assert.True(tabelas.Length == 1, $"{recurso}: tabela {nome} lida {tabelas.Length} vezes.");
        return tabelas[0];
    }

    private static ColunaEsquema Coluna(EsquemaBanco esquema, ReferenciaColuna referencia, string recurso)
    {
        var colunas = Tabela(esquema, referencia.Tabela, recurso).Colunas.Where(lida => lida.Nome == referencia.Coluna).ToArray();
        Assert.True(colunas.Length == 1, $"{recurso}: coluna {referencia} lida {colunas.Length} vezes.");
        return colunas[0];
    }

    // Mesmos itens, na mesma quantidade (uma duplicata lida não passa por um conjunto).
    private static bool MesmoConjunto<T>(IReadOnlyCollection<T> lidos, IReadOnlyCollection<T> esperados)
        where T : notnull =>
        lidos.Count == esperados.Count && lidos.Distinct().Count() == lidos.Count && lidos.ToHashSet().SetEquals(esperados);

    private static bool EmOrdem(IEnumerable<NomeObjeto> nomes)
    {
        var lista = nomes.ToArray();
        return lista.Zip(lista.Skip(1)).All(par =>
        {
            var esquema = string.CompareOrdinal(par.First.Esquema, par.Second.Esquema);
            return esquema < 0 || (esquema == 0 && string.CompareOrdinal(par.First.Nome, par.Second.Nome) < 0);
        });
    }

    // O que o modelo não confere no init (a ordem das propriedades é livre) e o motor confere de novo ao receber o esquema.
    private static IEnumerable<string> Inconsistencias(EsquemaBanco esquema)
    {
        var tabelas = esquema.Tabelas.DistinctBy(tabela => tabela.Nome).ToDictionary(tabela => tabela.Nome);
        foreach (var repetida in Repetidos(esquema.Tabelas.Select(tabela => tabela.Nome)))
        {
            yield return $"tabela {repetida} repetida";
        }

        foreach (var repetida in Repetidos(esquema.Sequences.Select(sequence => sequence.Nome)).Concat(Repetidos(esquema.ObjetosResiduais.Select(objeto => objeto.Nome))))
        {
            yield return $"objeto {repetida} repetido";
        }

        foreach (var tabela in esquema.Tabelas)
        {
            foreach (var coluna in Repetidos(tabela.Colunas.Select(coluna => coluna.Nome)))
            {
                yield return $"coluna {tabela.Nome}.{coluna} repetida";
            }

            var colunas = tabela.Colunas.Select(coluna => coluna.Nome).ToHashSet(StringComparer.Ordinal);
            var precisaDePai = tabela.Tipo is TipoTabela.Particao or TipoTabela.Herdeira;
            if (precisaDePai != (tabela.Pai is not null) || (tabela.Pai is not null && !tabelas.ContainsKey(tabela.Pai)))
            {
                yield return $"pai de {tabela.Nome} ({tabela.Tipo}) ausente, sobrando ou fora do esquema";
            }

            var precisaDeHistorico = tabela.Tipo == TipoTabela.Temporal;
            if (precisaDeHistorico != (tabela.Historico is not null)
                || (tabela.Historico is not null && (!tabelas.TryGetValue(tabela.Historico, out var historico) || historico.Tipo != TipoTabela.HistoricoDeTemporal)))
            {
                yield return $"histórico de {tabela.Nome} ({tabela.Tipo}) ausente, sobrando ou não marcado como histórico";
            }

            if (tabela.Tipo == TipoTabela.HistoricoDeTemporal && esquema.Tabelas.Count(temporal => tabela.Nome.Equals(temporal.Historico)) != 1)
            {
                yield return $"histórico {tabela.Nome} sem uma temporal só que o aponte";
            }

            foreach (var coluna in tabela.ChavePrimaria.Where(coluna => !colunas.Contains(coluna)))
            {
                yield return $"chave primária de {tabela.Nome} com a coluna {coluna}, que não existe";
            }

            foreach (var coluna in tabela.Colunas.Where(coluna => coluna.Tipo is not null && coluna.Situacao != SituacaoTipo.Suportado))
            {
                yield return $"coluna {tabela.Nome}.{coluna.Nome} com tipo canônico fora de 'suportado'";
            }

            foreach (var chave in tabela.ChavesEstrangeiras)
            {
                if (chave.Colunas.Count == 0 || chave.Colunas.Count != chave.ColunasReferenciadas.Count)
                {
                    yield return $"chave {chave.Nome} de {tabela.Nome} com {chave.Colunas.Count} colunas e {chave.ColunasReferenciadas.Count} referenciadas";
                }

                if (chave.Colunas.Any(coluna => !colunas.Contains(coluna)))
                {
                    yield return $"chave {chave.Nome} de {tabela.Nome} com coluna que não existe";
                }

                if (!tabelas.TryGetValue(chave.Referenciada, out var referenciada)
                    || chave.ColunasReferenciadas.Any(coluna => !referenciada.Colunas.Any(lida => lida.Nome == coluna)))
                {
                    yield return $"chave {chave.Nome} de {tabela.Nome} referencia tabela ou coluna fora do esquema";
                }
            }
        }

        foreach (var sequence in esquema.Sequences)
        {
            foreach (var repetida in Repetidos(sequence.Colunas))
            {
                yield return $"coluna {repetida} repetida na sequence {sequence.Nome}";
            }

            foreach (var coluna in sequence.Colunas.Where(coluna => !tabelas.TryGetValue(coluna.Tabela, out var tabela) || !tabela.Colunas.Any(lida => lida.Nome == coluna.Coluna)))
            {
                yield return $"sequence {sequence.Nome} usada pela coluna {coluna}, que não existe";
            }
        }
    }

    private static IEnumerable<T> Repetidos<T>(IEnumerable<T> itens)
        where T : notnull => itens.GroupBy(item => item).Where(grupo => grupo.Count() > 1).Select(grupo => grupo.Key);

    private static string Linhagem(IdentidadeLida identidade) =>
        string.Join(";", identidade.Linhagem.OrderBy(par => par.Key, StringComparer.Ordinal).Select(par => $"{par.Key}={par.Value}"));

    // Tudo o que o modelo diz, em texto, para comparar duas leituras.
    private static string Resumo(EsquemaBanco esquema) => string.Join(
        "\n",
        esquema.Tabelas.Select(tabela => string.Join(
            "|",
            tabela.Nome,
            tabela.Tipo,
            tabela.Pai,
            tabela.Historico,
            tabela.SegurancaDeLinha,
            string.Join(",", tabela.ChavePrimaria),
            string.Join(";", tabela.Colunas.Select(c => $"{c.Nome}:{c.TipoNativo}:{c.Situacao}:{c.Tipo}:{c.Anulavel}:{c.Geracao}:{c.Identidade}:{c.Mascarada}")),
            string.Join(";", tabela.ChavesEstrangeiras.Select(f => $"{f.Nome}:{string.Join(",", f.Colunas)}:{f.Referenciada}:{string.Join(",", f.ColunasReferenciadas)}"))))
            .Concat(esquema.Sequences.Select(s => $"{s.Nome}:{string.Join(",", s.Colunas)}"))
            .Concat(esquema.ObjetosResiduais.Select(o => $"{o.Nome}:{o.Tipo}")));
}
