using System.Collections;
using System.Reflection;
using System.Text;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

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
    public void A_classe_da_suite_e_selada_e_de_primeiro_nivel()
    {
        // Uma classe aninhada ou aberta pode nunca ser descoberta (ou rodar duas vezes por herança) sem ninguém notar.
        var classe = GetType();

        Assert.True(classe.IsSealed && !classe.IsNested, $"Suíte: a classe {classe.FullName} precisa ser selada e de primeiro nível.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_e_lido_sem_perdas()
    {
        Assert.True(Esperado.Inventario.Tabelas.Count > 0, "Inventário: a fixture precisa declarar as tabelas do esquema de conformidade.");

        // O mesmo esquema nos dois lados: a origem lida por um usuário só de leitura, o destino pelo dono.
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await Par.Origem.Esquema.LerAsync(cancelamento).ConfigureAwait(false);
        var destino = await Par.Destino.Esquema.LerAsync(cancelamento).ConfigureAwait(false);
        var diferencas = ComparacaoDoInventario.Diferencas(origem, Esperado.Inventario).Select(diferenca => "origem: " + diferenca)
            .Concat(ComparacaoDoInventario.Diferencas(destino, Esperado.Inventario).Select(diferenca => "destino: " + diferenca))
            .ToArray();

        Assert.True(diferencas.Length == 0, $"Inventário: {string.Join("; ", diferencas)}.");

        // O inventário não diz tudo (as propriedades do banco têm fato próprio): com o mesmo esquema, o destino tem de sair
        // igual à origem em tudo o que o modelo diz, fora as permissões do login, que dependem de quem lê.
        Assert.True(
            DespejoDoModelo.TextoSemAcesso(origem) == DespejoDoModelo.TextoSemAcesso(destino),
            "Inventário: o destino, com o mesmo esquema, foi lido diferente da origem (fora as permissões do login).");
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
    public void A_matriz_de_tipos_e_coberta()
    {
        // Cada situação, tipo canônico e unidade de comprimento aparece em alguma coluna do inventário (que o fato do
        // inventário compara com a leitura) ou é declarado inexistente, com o motivo; nunca os dois.
        var colunas = Esperado.Inventario.Tabelas.SelectMany(tabela => tabela.Colunas).ToArray();
        var presentes = colunas.Select(coluna => (Enum)coluna.Situacao)
            .Concat(colunas.Where(coluna => coluna.Tipo is not null).Select(coluna => (Enum)coluna.Tipo!.Value))
            .Concat(colunas.Where(coluna => coluna.Comprimento is not null).Select(coluna => (Enum)coluna.Comprimento!.Unidade))
            .ToHashSet();
        Enum[] valores =
        [
            .. Enum.GetValues<SituacaoTipo>().Cast<Enum>(),
            .. Enum.GetValues<TipoColuna>().Cast<Enum>(),
            .. Enum.GetValues<UnidadeComprimento>().Cast<Enum>(),
        ];

        var descobertos = valores.Where(valor => !presentes.Contains(valor) && !Esperado.Inexistentes.ContainsKey(valor)).Select(Nomear).ToArray();
        Assert.True(descobertos.Length == 0, $"Matriz: valores sem coluna no inventário nem motivo de ausência: {string.Join(", ", descobertos)}.");
        var contraditos = Esperado.Inexistentes.Keys.Where(presentes.Contains).Select(Nomear).ToArray();
        Assert.True(contraditos.Length == 0, $"Matriz: valores declarados inexistentes e presentes no inventário: {string.Join(", ", contraditos)}.");
        var semMotivo = Esperado.Inexistentes.Where(par => !valores.Contains(par.Key) || string.IsNullOrWhiteSpace(par.Value)).Select(par => Nomear(par.Key)).ToArray();
        Assert.True(semMotivo.Length == 0, $"Matriz: ausências fora da matriz ou sem motivo: {string.Join(", ", semMotivo)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task As_propriedades_do_banco_sao_lidas()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var lados = new (string Lado, PropriedadesBanco? Lidas)[]
        {
            ("origem", (await Par.Origem.Esquema.LerAsync(cancelamento)).Propriedades),
            ("destino", (await Par.Destino.Esquema.LerAsync(cancelamento)).Propriedades),
        };
        var esperadas = Esperado.Propriedades.Existe ? Esperado.Propriedades.Valor : null;
        var divergentes = lados
            .Where(lado => PropriedadesEsperadas.De(lado.Lidas) != esperadas)
            .Select(lado => $"{lado.Lado}: lidas {Texto(PropriedadesEsperadas.De(lado.Lidas))}, esperadas {Texto(esperadas)}")
            .ToArray();

        Assert.True(divergentes.Length == 0, $"Propriedades do banco: {string.Join("; ", divergentes)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Nada_lido_traz_o_sentinela()
    {
        // Os sentinelas estão em todo texto opaco do esquema de conformidade e nas senhas; procurados também em bytes
        // decimais e em hexadecimal, as formas em que as árvores de expressão do catálogo guardam as constantes. Nos dois
        // lados, e lidos também pelo administrador do servidor, que vê canais que o leitor e o dono não veem.
        var sentinelas = Esperado.Sentinelas;
        Assert.True(sentinelas is { Count: > 0 } && sentinelas.All(sentinela => !string.IsNullOrEmpty(sentinela)), "Sentinela: a fixture precisa declarar os sentinelas, sem item vazio.");

        var cancelamento = TestContext.Current.CancellationToken;
        var textos = new List<string>();
        await ColetarAsync(Par, textos, cancelamento);
        await using (var privilegiado = await fixture.CriarParPrivilegiadoAsync(cancelamento))
        {
            await ColetarAsync(privilegiado, textos, cancelamento);
        }

        var formas = sentinelas.SelectMany(sentinela => new[]
        {
            sentinela,
            string.Join(" ", Encoding.UTF8.GetBytes(sentinela)),
            Convert.ToHexString(Encoding.UTF8.GetBytes(sentinela)),
        }).ToArray();
        var achados = textos.Count(texto => formas.Any(forma => texto.Contains(forma, StringComparison.OrdinalIgnoreCase)));

        // Sem o texto achado na mensagem: o log do CI é público.
        Assert.True(achados == 0, $"Sentinela: {achados} textos lidos trazem um sentinela.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_conexao_inaceitavel_e_recusada_pelo_tipo_da_recusa()
    {
        // A recusa diz pelo tipo que a configuração é inaceitável (saída 2): uma IncognitoException comum sairia como falha
        // de execução, e uma exceção do driver, só com o tipo. O código fica na faixa dos dialetos, sem o que estava na string
        // (a interna nunca vem: o tipo não tem construtor com ela).
        var sentinela = "Recusa" + Guid.NewGuid().ToString("N")[..12] + "Q";
        var cancelamento = TestContext.Current.CancellationToken;
        foreach (var destino in new[] { false, true })
        {
            var lado = destino ? "destino" : "origem";
            Exception? erro = null;
            try
            {
                DialetoBanco banco = destino
                    ? await fixture.Provedor.ConectarDestinoAsync(fixture.ConexaoRecusada(sentinela), cancelamento)
                    : await fixture.Provedor.ConectarOrigemAsync(fixture.ConexaoRecusada(sentinela), cancelamento);
                await banco.DisposeAsync();
            }
            catch (Exception excecao)
            {
                erro = excecao;
            }

            var recusa = erro as DialetoRecusouException;
            Assert.True(recusa is not null, $"Recusa: a {lado} {(erro is null ? "conectou" : $"lançou {erro.GetType().Name}")}, e não DialetoRecusouException.");
            var codigo = recusa.Codigo;
            Assert.True(
                codigo is { Length: 7 } && codigo.StartsWith("INC3", StringComparison.Ordinal) && codigo[4] is >= '1' and <= '4' && char.IsAsciiDigit(codigo[5]) && char.IsAsciiDigit(codigo[6]),
                $"Recusa: a {lado} recusou com um código fora da faixa dos dialetos (INC31xx a INC34xx).");
            Assert.False(recusa.ToString().Contains(sentinela, StringComparison.OrdinalIgnoreCase), $"Recusa: a recusa da {lado} traz o que estava na string de conexão.");
        }
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
        // Só os nomes e os tipos: o resto o inventário compara.
        var lidos = (await LerOrigemAsync()).ObjetosResiduais.Select(objeto => (objeto.Nome, objeto.Tipo)).ToArray();
        var esperados = Esperado.Inventario.Residuais.Select(objeto => (objeto.Nome, objeto.Tipo)).ToArray();

        Assert.True(
            MesmoConjunto(lidos, esperados),
            $"Objetos residuais: lidos [{string.Join(", ", lidos.Select(o => $"{o.Nome} {o.Tipo}"))}], esperados [{string.Join(", ", esperados.Select(o => $"{o.Nome} {o.Tipo}"))}].");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_seguranca_por_linha_e_apontada()
    {
        // Quem não tem segurança por linha a declara inexistente, com o motivo: presente sem tabela não diz por quê.
        Assert.True(
            !Esperado.SegurancaDeLinha.Existe || Esperado.SegurancaDeLinha.Valor.Count > 0,
            "Segurança por linha: a fixture a declara presente sem nenhuma tabela; declare inexistente, com o motivo.");
        var comSeguranca = (await LerOrigemAsync()).Tabelas.Where(tabela => tabela.SegurancaDeLinha).Select(tabela => tabela.Nome).ToArray();
        var esperadas = Esperado.SegurancaDeLinha.Existe ? Esperado.SegurancaDeLinha.Valor : [];

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
    public void Os_nomes_escritos_sao_comparados_como_o_banco_compara()
    {
        // Um dialeto que dobrasse a caixa a mais, ou ignorasse as aspas, faria o plano tratar uma coluna que não é a escrita.
        var declaradas = Esperado.ComparacoesDeNome;
        Assert.True(
            declaradas.Any(nome => nome.Esperada == CorrespondenciaNome.Corresponde) && declaradas.Any(nome => nome.Esperada == CorrespondenciaNome.NaoCorresponde),
            "Comparação de nomes: a fixture precisa declarar nomes que correspondem e nomes que não correspondem.");

        var divergentes = declaradas
            .SelectMany(esperada => new (string Banco, DialetoBanco Dialeto)[] { ("na origem", Par.Origem), ("no destino", Par.Destino) }
                .Select(lado => (lado.Banco, esperada, lida: lado.Dialeto.Sql.CompararNome(esperada.Escrito, esperada.EntreAspas, esperada.DoCatalogo))))
            .Where(par => par.lida != par.esperada.Esperada)
            .Select(par => $"{(par.esperada.EntreAspas ? $"\"{par.esperada.Escrito}\"" : par.esperada.Escrito)} contra {par.esperada.DoCatalogo} {par.Banco}: {par.lida}, esperada {par.esperada.Esperada}")
            .ToArray();

        Assert.True(divergentes.Length == 0, $"Comparação de nomes: {string.Join("; ", divergentes)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_do_Incognito_fica_fora_das_tabelas_e_e_informado_a_parte()
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

        // Fora das tabelas, mas nunca escondido: o planejar precisa saber o que há no esquema reservado.
        var reservado = esquema.ObjetosNoEsquemaReservado;
        Assert.True(
            reservado is not null && reservado.Contains(Esperado.ObjetoDoIncognito),
            $"Esquema incognito: o objeto declarado ({Esperado.ObjetoDoIncognito}) não está entre os objetos do esquema reservado ({(reservado is null ? "não informados" : string.Join(", ", reservado))}).");
        var fora = reservado!.Where(nome => nome.Esquema != "incognito").Select(nome => nome.ToString()).ToArray();
        Assert.True(fora.Length == 0, $"Esquema incognito: objetos de outro esquema entre os do reservado: {string.Join(", ", fora)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_leitura_do_esquema_e_estavel()
    {
        var primeira = DespejoDoModelo.Texto(await LerOrigemAsync());
        var segunda = DespejoDoModelo.Texto(await LerOrigemAsync());

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

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_acesso_e_lido_em_cada_lado()
    {
        // O leitor da origem e o dono do destino, cada um com as suas permissões, sobre o mesmo esquema: toda tabela, todo
        // objeto residual e toda sequence lidos têm as permissões dele, na ordem do esquema, e iguais às declaradas.
        var cancelamento = TestContext.Current.CancellationToken;
        var problemas = new List<string>();
        foreach (var (lado, dialeto, esperado) in new (string, DialetoBanco, LadoDoAcesso?)[]
        {
            ("origem", Par.Origem, Esperado.Acesso.Existe ? Esperado.Acesso.Valor.Origem : null),
            ("destino", Par.Destino, Esperado.Acesso.Existe ? Esperado.Acesso.Valor.Destino : null),
        })
        {
            problemas.AddRange(DiferencasDoAcesso(await dialeto.Esquema.LerAsync(cancelamento), esperado).Select(problema => $"{lado}: {problema}"));
        }

        Assert.True(problemas.Count == 0, $"Acesso: {string.Join("; ", problemas)}.");
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task A_identidade_informa_o_transporte()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var problemas = new List<string>();
        foreach (var (lado, dialeto) in new (string, DialetoBanco)[] { ("origem", Par.Origem), ("destino", Par.Destino) })
        {
            var transporte = (await dialeto.Identidade.LerAsync(cancelamento)).Transporte;
            if (!Esperado.Transporte.Existe)
            {
                if (transporte is not null)
                {
                    problemas.Add($"{lado}: transporte lido num banco que o declara inexistente");
                }

                continue;
            }

            if (transporte is null)
            {
                problemas.Add($"{lado}: transporte ausente");
                continue;
            }

            if (transporte.ServidorVerificado == true && transporte.Cifrado != true)
            {
                problemas.Add($"{lado}: servidor verificado numa conexão que não é cifrada");
            }

            if (TransporteEsperado.De(transporte) != Esperado.Transporte.Valor)
            {
                problemas.Add($"{lado}: lido {TransporteEsperado.De(transporte)}, esperado {Esperado.Transporte.Valor}");
            }
        }

        Assert.True(problemas.Count == 0, $"Transporte: {string.Join("; ", problemas)}.");
    }

    private Task<EsquemaBanco> LerOrigemAsync() => Par.Origem.Esquema.LerAsync(TestContext.Current.CancellationToken);

    // As permissões de um lado: presentes se declaradas, uma por objeto do esquema e na ordem dele, com o valor da exceção
    // do objeto ou do padrão, e as colunas sem leitura coerentes com a tabela.
    private static IEnumerable<string> DiferencasDoAcesso(EsquemaBanco esquema, LadoDoAcesso? esperado)
    {
        if (esperado is null)
        {
            if (esquema.Acesso is not null)
            {
                yield return "acesso do login lido num banco que o declara inexistente";
            }

            yield break;
        }

        if (esquema.Acesso is not { } acesso)
        {
            yield return "acesso do login ausente";
            yield break;
        }

        if (PermissoesBancoEsperadas.De(acesso.Banco) != esperado.Banco)
        {
            yield return $"acesso do login com as permissões do banco lidas {PermissoesBancoEsperadas.De(acesso.Banco)}, esperadas {esperado.Banco}";
        }

        var tabelas = esquema.Tabelas.DistinctBy(tabela => tabela.Nome).ToDictionary(tabela => tabela.Nome);
        var residuais = esquema.ObjetosResiduais.DistinctBy(residual => residual.Nome).ToDictionary(residual => residual.Nome);
        var sequences = esquema.Sequences.Select(sequence => sequence.Nome).ToHashSet();
        var sobras = esperado.ExcecoesDasTabelas.Keys.Where(nome => !tabelas.ContainsKey(nome)).Select(nome => $"exceção de acesso da tabela {nome}, que não foi lida")
            .Concat(esperado.ExcecoesDosResiduais.Keys.Where(nome => !residuais.ContainsKey(nome)).Select(nome => $"exceção de acesso do objeto residual {nome}, que não foi lido"))
            .Concat(esperado.ExcecoesDasSequences.Keys.Where(nome => !sequences.Contains(nome)).Select(nome => $"exceção de acesso da sequence {nome}, que não foi lida"));
        foreach (var sobra in sobras)
        {
            yield return sobra;
        }

        var divergencias = new[]
        {
            Divergencia(acesso.Tabelas.Select(tabela => tabela.Tabela).ToArray(), esquema.Tabelas.Select(tabela => tabela.Nome).ToArray(), "tabela"),
            Divergencia(acesso.Residuais.Select(residual => residual.Nome).ToArray(), esquema.ObjetosResiduais.Select(residual => residual.Nome).ToArray(), "objeto residual"),
            Divergencia(acesso.Sequences.Select(sequence => sequence.Nome).ToArray(), esquema.Sequences.Select(sequence => sequence.Nome).ToArray(), "sequence"),
        };
        foreach (var divergencia in divergencias.OfType<string>())
        {
            yield return divergencia;
        }

        foreach (var permissoes in acesso.Tabelas.Where(permissoes => tabelas.ContainsKey(permissoes.Tabela)))
        {
            var nome = permissoes.Tabela;
            var declaradas = esperado.ExcecoesDasTabelas.GetValueOrDefault(nome) ?? esperado.Tabelas;
            if (!PermissoesTabelaEsperadas.De(permissoes).Igual(declaradas))
            {
                yield return $"{nome}: lidas {PermissoesTabelaEsperadas.De(permissoes)}, esperadas {declaradas}";
            }

            var posicoes = permissoes.ColunasSemLeitura.Select(coluna => tabelas[nome].Colunas.ToList().FindIndex(lida => lida.Nome == coluna)).ToArray();
            if (posicoes.Contains(-1))
            {
                yield return $"{nome}: coluna sem leitura {permissoes.ColunasSemLeitura[Array.IndexOf(posicoes, -1)]}, que não existe";
            }
            else if (posicoes.Zip(posicoes.Skip(1)).Any(par => par.First >= par.Second))
            {
                yield return $"{nome}: colunas sem leitura fora da ordem da tabela";
            }

            if (permissoes.PodeLer == true && permissoes.ColunasSemLeitura.Count > 0)
            {
                yield return $"{nome}: com leitura e colunas sem leitura";
            }
        }

        foreach (var permissoes in acesso.Residuais.Where(permissoes => residuais.ContainsKey(permissoes.Nome)))
        {
            var nome = permissoes.Nome;
            if (permissoes.Tipo != residuais[nome].Tipo)
            {
                yield return $"objeto residual {nome}: tipo {permissoes.Tipo} nas permissões, {residuais[nome].Tipo} no esquema";
            }

            var declarado = esperado.ExcecoesDosResiduais.TryGetValue(nome, out var excecao) ? excecao : esperado.PodeLimpar;
            if (permissoes.PodeLimpar != declarado)
            {
                yield return $"objeto residual {nome}: limpar lido {Marcas.Texto(permissoes.PodeLimpar)}, esperado {Marcas.Texto(declarado)}";
            }
        }

        foreach (var permissoes in acesso.Sequences.Where(permissoes => sequences.Contains(permissoes.Nome)))
        {
            var declarado = esperado.ExcecoesDasSequences.TryGetValue(permissoes.Nome, out var excecao) ? excecao : esperado.PodeAjustar;
            if (permissoes.PodeAjustar != declarado)
            {
                yield return $"sequence {permissoes.Nome}: ajustar lido {Marcas.Texto(permissoes.PodeAjustar)}, esperado {Marcas.Texto(declarado)}";
            }
        }
    }

    // A primeira diferença entre os objetos das permissões e os do esquema: um que falta, um a mais (ou repetido), um no
    // lugar de outro ou a ordem.
    private static string? Divergencia(NomeObjeto[] lidos, NomeObjeto[] doEsquema, string objeto)
    {
        if (lidos.SequenceEqual(doEsquema))
        {
            return null;
        }

        if (doEsquema.FirstOrDefault(nome => !lidos.Contains(nome)) is { } falta && lidos.Length <= doEsquema.Length)
        {
            return lidos.FirstOrDefault(nome => !doEsquema.Contains(nome)) is { } outro
                ? $"acesso do login com as permissões de {objeto} {outro} no lugar de {falta}"
                : $"acesso do login sem as permissões de {objeto} {falta}";
        }

        if (lidos.Length > doEsquema.Length)
        {
            var sobra = lidos.FirstOrDefault(nome => !doEsquema.Contains(nome)) ?? Repetidos(lidos).First();
            return $"acesso do login com permissões a mais de {objeto} {sobra}";
        }

        // Com um objeto repetido no esquema (a consistência o acusa), as permissões dele podem faltar sem nenhum nome faltar.
        var posicao = lidos.Zip(doEsquema).TakeWhile(par => par.First.Equals(par.Second)).Count();
        if (posicao >= lidos.Length)
        {
            return $"acesso do login sem as permissões de {objeto} {doEsquema[posicao]}";
        }

        return $"acesso do login com as permissões de {objeto} fora da ordem do esquema: {lidos[posicao]} no lugar de {doEsquema[posicao]}";
    }

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

    private static string Nomear(Enum valor) => $"{valor.GetType().Name}.{valor}";

    private static string Texto(object? valor) => valor?.ToString() ?? "nenhuma";

    // O esquema e a identidade dos dois lados e o texto dos próprios dialetos.
    private static async Task ColetarAsync(ParDeBancos par, List<string> textos, CancellationToken cancelamento)
    {
        foreach (var dialeto in new DialetoBanco[] { par.Origem, par.Destino })
        {
            Coletar(await dialeto.Esquema.LerAsync(cancelamento), textos);
            Coletar(await dialeto.Identidade.LerAsync(cancelamento), textos);
            textos.Add(dialeto.ToString()!);
        }
    }

    // Todo texto alcançável pelas propriedades públicas, inclusive o ToString de cada objeto.
    private static void Coletar(object? valor, List<string> textos)
    {
        switch (valor)
        {
            case null:
                return;
            case string texto:
                textos.Add(texto);
                return;
            case IDictionary dicionario:
                foreach (DictionaryEntry par in dicionario)
                {
                    Coletar(par.Key, textos);
                    Coletar(par.Value, textos);
                }

                return;
            case IEnumerable itens:
                foreach (var item in itens)
                {
                    Coletar(item, textos);
                }

                return;
            case Enum or ValueType or Version:
                textos.Add(valor.ToString()!);
                return;
        }

        textos.Add(valor.ToString()!);
        foreach (var propriedade in valor.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(propriedade => propriedade.GetIndexParameters().Length == 0))
        {
            Coletar(propriedade.GetValue(valor), textos);
        }
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

            // A partição é de uma particionada (ou de uma intermediária, que é partição); a herdeira, de tabelas comuns ou
            // herdeiras (o banco não deixa herdar de particionada nem de partição).
            TipoTabela[] tiposDasMaes = tabela.Tipo == TipoTabela.Particao ? [TipoTabela.Particionada, TipoTabela.Particao] : [TipoTabela.Comum, TipoTabela.Herdeira];
            if (precisaDePai
                && new[] { tabela.Pai }.Concat(tabela.OutrosPais).Any(mae => mae is not null && tabelas.TryGetValue(mae, out var lida) && !tiposDasMaes.Contains(lida.Tipo)))
            {
                yield return $"pai de {tabela.Nome} ({tabela.Tipo}) de tipo que não pode ter filha desse tipo";
            }

            if ((tabela.OutrosPais.Count > 0 && tabela.Tipo != TipoTabela.Herdeira)
                || tabela.OutrosPais.Any(pai => pai.Equals(tabela.Pai) || !tabelas.ContainsKey(pai))
                || Repetidos(tabela.OutrosPais).Any())
            {
                yield return $"outros pais de {tabela.Nome} ({tabela.Tipo}) fora de herdeira, repetidos, com o pai ou fora do esquema";
            }

            // A particionada tem chave, e a intermediária (partição que é pai de partição) também; ninguém mais tem.
            var particionada = tabela.Tipo == TipoTabela.Particionada
                || esquema.Tabelas.Any(filha => filha.Tipo == TipoTabela.Particao && tabela.Nome.Equals(filha.Pai));
            if (particionada != (tabela.ChaveParticao is not null))
            {
                yield return $"chave de partição de {tabela.Nome} ({tabela.Tipo}) ausente ou sobrando";
            }

            if (tabela.ChaveParticao is { } chaveParticao
                && (chaveParticao.Colunas.Any(coluna => !colunas.Contains(coluna)) || Repetidos(chaveParticao.Colunas).Any()))
            {
                yield return $"chave de partição de {tabela.Nome} com coluna que não existe ou repetida";
            }

            if (tabela.ChaveParticao is { Colunas.Count: 0, ColunasIncompletas: false })
            {
                yield return $"chave de partição de {tabela.Nome} sem coluna e marcada completa";
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

            foreach (var problema in tabela.Colunas.SelectMany(coluna => Inconsistencias(tabela, coluna, esquema.Propriedades)).Concat(InconsistenciasDosObjetos(tabela)))
            {
                yield return problema;
            }

            foreach (var repetida in Repetidos(tabela.ChavesEstrangeiras.Select(chave => chave.Nome)))
            {
                yield return $"chave {repetida} de {tabela.Nome} repetida";
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

        if (esquema.Propriedades?.Colacao is { } padrao && !Coerente(padrao))
        {
            yield return "colação padrão das propriedades do banco determinística com marca que não é falsa";
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

        // As dependências entre materialized views: só das que existem, sem a própria, em ordem e sem ciclo (a atualização
        // segue essa ordem).
        var visoes = esquema.ObjetosResiduais.Where(objeto => objeto.Tipo == TipoObjetoResidual.VisaoMaterializada).DistinctBy(objeto => objeto.Nome).ToDictionary(objeto => objeto.Nome);
        foreach (var residual in esquema.ObjetosResiduais)
        {
            if (residual.Tipo != TipoObjetoResidual.VisaoMaterializada && (residual.DependeDe.Count > 0 || residual.DependenciasIncompletas is not null || residual.Populada is not null))
            {
                yield return $"residual {residual.Nome} ({residual.Tipo}) com dependências ou marcas de materialized view";
            }
            else if (residual.DependeDe.Contains(residual.Nome))
            {
                yield return $"residual {residual.Nome} depende dele mesmo";
            }
            else if (residual.DependeDe.FirstOrDefault(dependencia => !visoes.ContainsKey(dependencia)) is { } fora)
            {
                yield return $"residual {residual.Nome} depende de {fora}, que não é materialized view do esquema";
            }
            else if (!EmOrdem(residual.DependeDe))
            {
                yield return $"residual {residual.Nome} com dependências fora da ordem ordinal de esquema e nome";
            }
            else if (EmCiclo(residual, visoes))
            {
                yield return $"residual {residual.Nome} numa dependência circular";
            }
        }
    }

    // Os índices, CHECKs, exclusões e triggers da tabela. As regras de presença (o elemento de expressão tem referências,
    // o elemento de texto tem colação, a chave primária tem índice) valem só aqui, como as das colunas.
    private static IEnumerable<string> InconsistenciasDosObjetos(TabelaEsquema tabela)
    {
        var colunas = tabela.Colunas.DistinctBy(coluna => coluna.Nome).ToDictionary(coluna => coluna.Nome, StringComparer.Ordinal);
        var repetidos = Repetidos(tabela.IndicesUnicos.Select(indice => $"índice {indice.Nome} de {tabela.Nome} repetido"))
            .Concat(Repetidos(tabela.RestricoesCheck.Select(check => $"CHECK {check.Nome} de {tabela.Nome} repetido")))
            .Concat(Repetidos(tabela.RestricoesExclusao.Select(exclusao => $"exclusão {exclusao.Nome} de {tabela.Nome} repetida")))
            .Concat(Repetidos(tabela.Triggers.Select(trigger => $"trigger {trigger.Nome} de {tabela.Nome} repetida")));
        foreach (var repetido in repetidos)
        {
            yield return repetido;
        }

        foreach (var indice in tabela.IndicesUnicos)
        {
            var nome = $"índice {indice.Nome} de {tabela.Nome}";
            foreach (var inexistente in indice.Elementos.Select(elemento => elemento.Coluna).OfType<string>().Where(coluna => !colunas.ContainsKey(coluna)))
            {
                yield return $"{nome} com o elemento {inexistente}, coluna que não existe";
            }

            if (indice.Elementos.Any(elemento => elemento.Coluna is null) != (indice.ReferenciasDasExpressoes is not null))
            {
                yield return $"{nome} com referências das expressões ausentes ou sobrando";
            }

            foreach (var elemento in indice.Elementos.Where(elemento => elemento.Coluna is { } coluna && colunas.ContainsKey(coluna)))
            {
                var tipo = colunas[elemento.Coluna!].Tipo;
                if (tipo == TipoColuna.Texto && elemento.Colacao is null)
                {
                    yield return $"{nome} com elemento de texto {elemento.Coluna} sem colação";
                }
                else if (tipo is not (null or TipoColuna.Texto) && elemento.Colacao is not null)
                {
                    yield return $"{nome} com colação no elemento {elemento.Coluna}, que não é texto";
                }
            }

            foreach (var (elemento, posicao) in indice.Elementos.Select((elemento, posicao) => (elemento, posicao + 1)).Where(par => par.elemento.Colacao is { } colacao && !Coerente(colacao)))
            {
                yield return $"{nome} com colação determinística e marca que não é falsa no elemento {posicao} ({elemento.Coluna ?? "expressão"})";
            }

            foreach (var (parte, referencias) in new[] { ("expressões", indice.ReferenciasDasExpressoes), ("predicado", indice.Predicado) })
            {
                if (referencias is not null && Problema(referencias, tabela) is { } problema)
                {
                    yield return $"{nome} com {parte} {problema}";
                }
            }
        }

        foreach (var problema in InconsistenciasDaChavePrimaria(tabela))
        {
            yield return problema;
        }

        foreach (var (objeto, referencias) in tabela.RestricoesCheck.Select(check => ($"CHECK {check.Nome}", check.Referencias))
            .Concat(tabela.RestricoesExclusao.Select(exclusao => ($"exclusão {exclusao.Nome}", exclusao.Referencias))))
        {
            if (Problema(referencias, tabela) is { } problema)
            {
                yield return $"{objeto} de {tabela.Nome} {problema}";
            }
        }

        foreach (var trigger in tabela.Triggers.Where(trigger => trigger.Herdada && tabela.Tipo != TipoTabela.Particao))
        {
            yield return $"trigger {trigger.Nome} de {tabela.Nome} herdada fora de partição";
        }
    }

    // No máximo um índice primário, com as colunas da chave primária; a chave primária sem índice primário só numa partição
    // (o índice anexado fica no pai) ou com sobreposição proibida (uma exclusão que cita todas as colunas dela).
    private static IEnumerable<string> InconsistenciasDaChavePrimaria(TabelaEsquema tabela)
    {
        var primarios = tabela.IndicesUnicos.Where(indice => indice.Primario).ToArray();
        if (primarios.Length > 1)
        {
            yield return $"tabela {tabela.Nome} com {primarios.Length} índices primários";
        }
        else if (primarios is [var primario] && !primario.Elementos.Select(elemento => elemento.Coluna).SequenceEqual(tabela.ChavePrimaria))
        {
            yield return $"índice primário {primario.Nome} de {tabela.Nome} diferente da chave primária";
        }
        else if (primarios.Length == 0 && tabela.ChavePrimaria.Count > 0 && tabela.Tipo != TipoTabela.Particao
            && !tabela.RestricoesExclusao.Any(exclusao => tabela.ChavePrimaria.All(exclusao.Referencias.Colunas.Contains)))
        {
            yield return $"chave primária de {tabela.Nome} sem índice primário fora de partição";
        }
    }

    // O que há de errado nas colunas citadas: uma que não existe, ou fora da ordem das colunas da tabela.
    private static string? Problema(ReferenciasDaExpressao referencias, TabelaEsquema tabela)
    {
        var posicoes = referencias.Colunas.Select(citada => tabela.Colunas.ToList().FindIndex(lida => lida.Nome == citada)).ToArray();
        return posicoes.Contains(-1) ? $"citando {referencias.Colunas[Array.IndexOf(posicoes, -1)]}, coluna que não existe"
            : posicoes.Zip(posicoes.Skip(1)).Any(par => par.First >= par.Second) ? "citando colunas fora da ordem da tabela"
            : null;
    }

    // Se as dependências de uma materialized view voltam a ela.
    private static bool EmCiclo(ObjetoResidual inicio, Dictionary<NomeObjeto, ObjetoResidual> visoes)
    {
        var vistas = new HashSet<NomeObjeto>();
        var pendentes = new Stack<NomeObjeto>(inicio.DependeDe);
        while (pendentes.TryPop(out var atual))
        {
            if (atual.Equals(inicio.Nome))
            {
                return true;
            }

            if (vistas.Add(atual) && visoes.TryGetValue(atual, out var visao))
            {
                foreach (var proxima in visao.DependeDe)
                {
                    pendentes.Push(proxima);
                }
            }
        }

        return false;
    }

    // A semântica da coluna. Um tipo canônico nulo (domínio, array) pode ter qualquer campo; os que só valem para um tipo
    // canônico ficam fora dos outros. As regras de presença ("texto tem colação", "inteiro tem faixa", "gerada tem
    // referências") valem só aqui, para os dialetos do repositório: o motor trata o campo nulo como o pior caso.
    private static IEnumerable<string> Inconsistencias(TabelaEsquema tabela, ColunaEsquema coluna, PropriedadesBanco? propriedades)
    {
        var nome = $"coluna {tabela.Nome}.{coluna.Nome}";
        var texto = coluna.Tipo is null or TipoColuna.Texto;
        if (coluna.Colacao is not null && !texto)
        {
            yield return $"{nome} com colação fora de texto";
        }

        if (coluna.Tipo == TipoColuna.Texto && coluna.Colacao is null)
        {
            yield return $"{nome} de texto sem colação";
        }

        if (coluna.Colacao is { } colacao && !Coerente(colacao))
        {
            yield return $"{nome} com colação determinística e marca que não é falsa";
        }

        if ((coluna.IgnoraCaixaPeloTipo is not null || coluna.IgnoraEspacoFinalPeloTipo is not null) && !texto)
        {
            yield return $"{nome} com marca de texto fora de texto";
        }

        if (coluna.Comprimento is not null && coluna.Tipo is not (null or TipoColuna.Texto or TipoColuna.Binario))
        {
            yield return $"{nome} com comprimento fora de texto e binário";
        }

        if (coluna.Comprimento is { Unidade: UnidadeComprimento.PontosDeCodigo } && propriedades?.Codificacao.Definida == false)
        {
            yield return $"{nome} com comprimento em caracteres num banco sem codificação declarada";
        }

        if ((coluna.Precisao is not null || coluna.Escala is not null) && coluna.Tipo is not (null or TipoColuna.Decimal))
        {
            yield return $"{nome} com precisão ou escala fora de decimal";
        }

        if (coluna.Escala is not null && coluna.Precisao is null)
        {
            yield return $"{nome} com escala sem precisão";
        }

        if (coluna.Faixa is not null && coluna.Tipo is not (null or TipoColuna.Inteiro))
        {
            yield return $"{nome} com faixa fora de inteiro";
        }

        if (coluna.Tipo == TipoColuna.Inteiro && coluna.Faixa is null)
        {
            yield return $"{nome} inteira sem faixa";
        }

        if (coluna.Faixa is { Minimo: var minimo, Maximo: var maximo } && minimo >= maximo)
        {
            yield return $"{nome} com faixa vazia";
        }

        if (coluna.Faixa is { Maximo: <= 0 })
        {
            yield return $"{nome} com faixa sem positivos";
        }

        if ((coluna.ReferenciasDaGeracao is not null) != (coluna.Geracao != GeracaoColuna.Nenhuma))
        {
            yield return $"{nome} com referências da geração ausentes ou sobrando";
        }

        if (coluna.ReferenciasDaGeracao is { } referencias)
        {
            var posicoes = referencias.Colunas.Select(citada => tabela.Colunas.ToList().FindIndex(lida => lida.Nome == citada)).ToArray();
            if (referencias.Colunas.Contains(coluna.Nome))
            {
                yield return $"{nome} com referência da geração a ela mesma";
            }
            else if (posicoes.Contains(-1))
            {
                yield return $"{nome} com referência da geração a coluna que não existe";
            }
            else if (posicoes.Zip(posicoes.Skip(1)).Any(par => par.First >= par.Second))
            {
                yield return $"{nome} com referências da geração fora da ordem das colunas";
            }
        }
    }

    // Uma colação determinística não ignora nada, e mediu isso: as três marcas falsas, nunca nulas.
    private static bool Coerente(ColacaoEsquema colacao) =>
        !colacao.Deterministica || colacao is { IgnoraCaixa: false, IgnoraAcento: false, IgnoraPontuacao: false };

    private static IEnumerable<T> Repetidos<T>(IEnumerable<T> itens)
        where T : notnull => itens.GroupBy(item => item).Where(grupo => grupo.Count() > 1).Select(grupo => grupo.Key);

    private static string Linhagem(IdentidadeLida identidade) =>
        string.Join(";", identidade.Linhagem.OrderBy(par => par.Key, StringComparer.Ordinal).Select(par => $"{par.Key}={par.Value}"));

}
