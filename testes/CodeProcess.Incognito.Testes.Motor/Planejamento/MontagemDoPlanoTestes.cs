using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// Do esquema e das regras ao plano: os nomes resolvidos pelo dialeto (o casamento exato nunca vence), as unidades de
/// cópia, a origem contra o destino, as estrangeiras e o esquema reservado, cada código com o seu caso.
/// </summary>
public sealed class MontagemDoPlanoTestes
{
    [Fact]
    public async Task Sem_entradas_toda_tabela_e_copiada_sem_regra_e_com_a_classificacao()
    {
        var montado = await PlanejarAsync("");

        Assert.Equal(["loja.clientes", "loja.pedidos"], montado.Tabelas.Select(tabela => tabela.Nome.ToString()));
        Assert.All(montado.Tabelas, tabela => Assert.Equal((AcaoTabela.Copiar, false, (PosicaoConfiguracao?)null), (tabela.Acao, tabela.AcaoEscrita, tabela.Posicao)));
        Assert.All(montado.Tabelas.SelectMany(tabela => tabela.Colunas), coluna => Assert.Null(coluna.Regra));
        Assert.Equal(
            [("id", true), ("cpf", false), ("nome", false)],
            montado.Tabelas[0].Colunas.Select(coluna => (coluna.Nome, coluna.Classificacao.Identificador)));
        Assert.Equal([("id", true), ("cliente", true)], montado.Tabelas[1].Colunas.Select(coluna => (coluna.Nome, coluna.Classificacao.Identificador)));
        Assert.Empty(montado.Avisos);
        Assert.Equal(montado.Tabelas.Select(tabela => tabela.Nome), montado.Execucao.Unidades.Select(unidade => unidade.Nome));
    }

    [Fact]
    public async Task A_regra_escrita_vira_a_regra_efetiva_com_a_posicao_e_o_nome_do_catalogo()
    {
        var yaml = """
            tabelas:
              LOJA.Clientes:
                colunas:
                  CPF: { transformador: br.cpf@1 }
                  nome: { acao: manter, classe: sensivel, justificativa: "o nome do teste" }

            """;

        var montado = await PlanejarAsync(yaml);

        var clientes = montado.Tabelas[0];
        Assert.Equal(("loja.clientes", true), (clientes.Nome.ToString(), clientes.Posicao is { Linha: > 0 }));
        var cpf = clientes.Colunas.Single(coluna => coluna.Nome == "cpf");
        var regra = cpf.Regra!;
        Assert.Equal((TipoRegra.Transformar, "br.cpf", 1, Injetividade.Garantida), (regra.Tipo, regra.Transformador, regra.Versao, regra.Injetividade));
        Assert.Equal(new DominioPseudonimo("cpf", 1), regra.Dominio);
        Assert.Equal((OrigemRegra.Configuracao, (PropagacaoRegra?)null, (NomeColuna?)null), (regra.Origem, regra.Propagacao, regra.PropagadaDe));
        Assert.Equal("/tabelas/LOJA.Clientes/colunas/CPF", regra.Posicao!.Local);
        var nome = clientes.Colunas.Single(coluna => coluna.Nome == "nome");
        Assert.Equal((TipoRegra.Manter, true), (nome.Regra!.Tipo, nome.Classificacao.Sensivel));
        Assert.Equal("cpf", Assert.Single(montado.Execucao.Unidades[0].Tratadas).Coluna);
    }

    [Fact]
    public async Task Tabela_inexistente_da_INC3010_com_a_dica_de_aspas()
    {
        var erros = await RecusaAsync("tabelas:\n  loja.sumida: {}\n  '\"loja\".\"Clientes\"': {}\n");

        Assert.Equal(["INC3010", "INC3010"], erros.Select(erro => erro.Codigo));
        Assert.Contains("A tabela loja.sumida não existe", erros[0].Mensagem, StringComparison.Ordinal);
        Assert.Contains("Há loja.clientes, que só difere na caixa", erros[1].Mensagem, StringComparison.Ordinal);
        Assert.All(erros, erro => Assert.NotNull(erro.Posicao));
    }

    [Fact]
    public async Task Coluna_inexistente_da_INC3011_com_a_dica_de_aspas()
    {
        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      '\"CPF\"': { transformador: br.cpf@1 }\n"));

        Assert.Equal(("INC3011", "loja.clientes"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Contains("A coluna \"CPF\" não existe", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains("Há cpf, que só difere na caixa", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nome_indeterminado_ou_com_mais_de_um_candidato_da_INC3012()
    {
        // Indeterminada nunca vira palpite, nem no pareamento nem na entrada.
        var indeterminada = await RecusaAsync("tabelas:\n  loja.clientes: {}\n", forcada: CorrespondenciaNome.Indeterminada);

        Assert.All(indeterminada, erro => Assert.Equal("INC3012", erro.Codigo));
        Assert.Contains(indeterminada, erro => erro.Posicao is not null && erro.Mensagem.Contains("A entrada loja.clientes designa mais de uma tabela", StringComparison.Ordinal));
        Assert.Contains(indeterminada, erro => erro.Posicao is null && erro.Mensagem.Contains("corresponde a mais de uma tabela do destino", StringComparison.Ordinal));

        // Um só candidato, indeterminado (banco de um byte, letra fora do ASCII sem aspas): nunca vira o resolvido.
        var esquema = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = new NomeObjeto("vendas", "ação"), Colunas = [Inteiro("id")] });
        var umByte = Assert.IsType<PlanoRecusadoException>(
            await Assert.ThrowsAnyAsync<Exception>(() => DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(esquema, esquema) { UmBytePorCaractere = true }, yaml: Yaml.Minimo + "tabelas:\n  vendas.AÇÃO: {}\n")),
            exactMatch: true);
        Assert.Equal("INC3012", Assert.Single(umByte.Erros).Codigo);
        Assert.Contains("vendas.ação", umByte.Erros[0].Mensagem, StringComparison.Ordinal);

        // Quando tudo corresponde, cada tabela da origem tem mais de um par no destino.
        var todas = await RecusaAsync("", forcada: CorrespondenciaNome.Corresponde);
        Assert.Equal(["INC3012", "INC3012"], todas.Select(erro => erro.Codigo));
    }

    [Fact]
    public async Task Duas_entradas_na_mesma_tabela_ou_coluna_dao_INC3013()
    {
        var tabelas = await RecusaAsync("tabelas:\n  loja.clientes: {}\n  '\"loja\".\"clientes\"': {}\n");
        var colunas = await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: manter\n      CPF: manter\n");

        Assert.Equal("INC3013", Assert.Single(tabelas).Codigo);
        Assert.Contains("mesma tabela que a entrada loja.clientes", tabelas[0].Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3013", "cpf"), (Assert.Single(colunas).Codigo, colunas[0].Coluna));
    }

    [Fact]
    public async Task Entrada_numa_particao_da_INC3014()
    {
        var esquema = ComTabelas(Clientes(), Pedidos(), Vendas(), Particao("vendas_1", "vendas"));

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.vendas_1: { acao: esvaziar }\n", esquema, esquema));

        Assert.Equal(("INC3014", "loja.vendas_1"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Contains("escreva a regra em loja.vendas", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entrada_em_esquema_de_sistema_ou_reservado_da_INC3015()
    {
        var erros = await RecusaAsync("tabelas:\n  incognito.execucao: {}\n  pg_catalog.pg_class: {}\n  information_schema.tables: {}\n  PG_TEMP_3.x: {}\n");

        Assert.Equal(["INC3015", "INC3015", "INC3015", "INC3015"], erros.Select(erro => erro.Codigo));

        // O sys do PostgreSQL é um esquema comum: a tabela dele se configura como qualquer outra.
        var comSys = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = new NomeObjeto("sys", "pessoas"), Colunas = [Inteiro("id")] });
        var plano = await PlanejarAsync("tabelas:\n  sys.pessoas: { acao: ignorar }\n", comSys, comSys);
        Assert.Equal(AcaoTabela.Ignorar, plano.Tabelas.Single(tabela => tabela.Nome.Esquema == "sys").Acao);
    }

    [Fact]
    public async Task Coluna_tratada_que_so_muda_a_situacao_da_INC3023()
    {
        // Sem forma canônica nos dois lados, o tipo é igual (nulo); a situação, não.
        var origem = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Tipo), null), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva));
        var destino = TrocarColuna(origem, "clientes", "cpf", (nameof(ColunaEsquema.Situacao), SituacaoTipo.NaoSuportado));

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", origem, destino));

        Assert.Equal(("INC3023", "cpf"), (erro.Codigo, erro.Coluna));
    }

    [Fact]
    public async Task Faixa_e_precisao_mais_estreitas_no_destino_avisam_INC3521()
    {
        var valor = new ColunaEsquema { Nome = "valor", TipoNativo = "numeric", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Decimal, Precisao = 10, Escala = 2 };
        var origem = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("precos"), Colunas = [valor] });
        var livre = TrocarColuna(origem, "precos", "valor", (nameof(ColunaEsquema.Precisao), null), (nameof(ColunaEsquema.Escala), null));
        EsquemaBanco Precos(int precisao, int escala) => TrocarColuna(origem, "precos", "valor", (nameof(ColunaEsquema.Precisao), precisao), (nameof(ColunaEsquema.Escala), escala));
        EsquemaBanco Faixa(long minimo, long maximo) => TrocarColuna(origem, "pedidos", "id", (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = minimo, Maximo = maximo }));
        async Task<(string, string?)[]> AvisosAsync(EsquemaBanco daOrigem, EsquemaBanco doDestino) =>
            [.. (await PlanejarAsync("", daOrigem, doDestino)).Avisos.Select(aviso => (aviso.Codigo, aviso.Coluna))];

        Assert.Equal([("INC3521", "id")], await AvisosAsync(origem, Faixa(0, long.MaxValue)));
        Assert.Equal([("INC3521", "id")], await AvisosAsync(origem, Faixa(long.MinValue, int.MaxValue)));
        Assert.Empty(await AvisosAsync(origem, Faixa(long.MinValue, long.MaxValue)));
        Assert.Equal([("INC3521", "valor")], await AvisosAsync(origem, Precos(9, 2)));
        Assert.Equal([("INC3521", "valor")], await AvisosAsync(origem, Precos(11, 1)));
        Assert.Empty(await AvisosAsync(origem, Precos(12, 2)));

        // O decimal sem precisão não tem limite: com limite no destino, é mais estreito; o contrário, não.
        Assert.Equal([("INC3521", "valor")], await AvisosAsync(livre, origem));
        Assert.Empty(await AvisosAsync(origem, livre));
    }

    [Fact]
    public async Task Coluna_so_no_destino_que_aceita_nulo_ou_tem_identidade_nao_avisa()
    {
        var destino = ComTabelas(
            BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Colunas), Clientes().Colunas
                .Append(Inteiro("opcional"))
                .Append(BancoFalso.Clonar(Inteiro("sequencial", anulavel: false), (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre)))
                .ToArray())),
            Pedidos());

        Assert.Empty((await PlanejarAsync("", destino: destino)).Avisos);
    }

    [Fact]
    public async Task Chave_primaria_so_num_lado_avisa_INC3523()
    {
        var destino = ComTabelas(
            BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.ChavePrimaria), Array.Empty<string>()), (nameof(TabelaEsquema.IndicesUnicos), Array.Empty<IndiceUnico>())),
            Pedidos());

        var aviso = Assert.Single((await PlanejarAsync("", destino: destino)).Avisos);

        Assert.Equal(("INC3523", "pk_clientes"), (aviso.Codigo, aviso.Objeto));
        Assert.Contains("A chave primária pk_clientes só existe na origem", aviso.Mensagem, StringComparison.Ordinal);
        Assert.Contains("só existe na origem", aviso.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coluna_que_difere_so_na_caixa_avisa_INC3510()
    {
        var esquema = ComTabelas(BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Colunas), Clientes().Colunas.Append(Texto("Nome", 100)).ToArray())), Pedidos());

        var aviso = Assert.Single((await PlanejarAsync("tabelas:\n  loja.clientes:\n    colunas:\n      nome: manter\n", esquema, esquema)).Avisos);

        Assert.Equal(("INC3510", "nome", "Nome"), (aviso.Codigo, aviso.Coluna, aviso.Objeto));
    }

    [Fact]
    public async Task Marca_da_colacao_ou_do_tipo_diferente_avisa_INC3522()
    {
        var comRegras = new ColacaoEsquema { Nome = "default", Deterministica = true, TemRegras = true, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false };

        var regras = (await PlanejarAsync("", destino: TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Colacao), comRegras)))).Avisos;
        var tipo = (await PlanejarAsync("", destino: TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), true)))).Avisos;

        Assert.Equal(("INC3522", "nome"), (Assert.Single(regras).Codigo, regras[0].Coluna));
        Assert.Equal(("INC3522", "nome"), (Assert.Single(tipo).Codigo, tipo[0].Coluna));
    }

    [Fact]
    public async Task DerivarDe_inexistente_da_INC3016_e_ciclo_pelos_nomes_resolvidos_da_INC3017()
    {
        var esquema = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("enderecos"), Colunas = [Texto("a", 10), Texto("b", 10)] });

        var inexistente = Assert.Single(await RecusaAsync("tabelas:\n  loja.enderecos:\n    colunas:\n      a: { transformador: br.cep@1, derivarDe: sumida }\n", esquema, esquema));

        // "A" e "a" sem aspas são a mesma coluna: a leitura do YAML não via o ciclo.
        var ciclo = await RecusaAsync(
            "tabelas:\n  loja.enderecos:\n    colunas:\n      A: { transformador: br.cep@1, derivarDe: b }\n      b: { transformador: br.cep@1, derivarDe: a }\n",
            esquema,
            esquema);

        Assert.Equal(("INC3016", "a"), (inexistente.Codigo, inexistente.Coluna));
        Assert.Equal([("INC3017", "a"), ("INC3017", "b")], ciclo.Select(erro => (erro.Codigo, erro.Coluna!)));
    }

    [Fact]
    public async Task Tabela_so_na_origem_da_INC3020_salvo_ignorada()
    {
        var origem = ComTabelas(Clientes(), Pedidos(), Simples("extra"));

        var erro = Assert.Single(await RecusaAsync("", origem));
        var montado = await PlanejarAsync("tabelas:\n  loja.extra: { acao: ignorar }\n", origem);

        Assert.Equal(("INC3020", "loja.extra"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Equal((AcaoTabela.Ignorar, true), montado.Tabelas.Single(tabela => tabela.Nome.Tabela == "extra") is var extra ? (extra.Acao, extra.AcaoEscrita) : default);
    }

    [Fact]
    public async Task Tabela_so_no_destino_da_INC3021_salvo_esvaziada_ou_ignorada_por_escrito()
    {
        var destino = ComTabelas(Clientes(), Pedidos(), Simples("sobra"));

        var erro = Assert.Single(await RecusaAsync("", destino: destino));
        var semAcao = Assert.Single(await RecusaAsync("tabelas:\n  loja.sobra: {}\n", destino: destino));
        var montado = await PlanejarAsync("tabelas:\n  loja.sobra: { acao: esvaziar }\n", destino: destino);

        Assert.Equal(("INC3021", "INC3021"), (erro.Codigo, semAcao.Codigo));
        var sobra = montado.Tabelas.Single(tabela => tabela.Nome.Tabela == "sobra");
        Assert.Equal((AcaoTabela.Esvaziar, true, 0), (sobra.Acao, sobra.AcaoEscrita, sobra.Colunas.Count));
    }

    [Fact]
    public async Task Coluna_da_origem_ausente_no_destino_da_INC3022()
    {
        var clientes = Clientes();
        var destino = ComTabelas(BancoFalso.Clonar(clientes, (nameof(TabelaEsquema.Colunas), clientes.Colunas.Take(2).ToArray())), Pedidos());

        var erro = Assert.Single(await RecusaAsync("", destino: destino));

        Assert.Equal(("INC3022", "loja.clientes", "nome"), (erro.Codigo, erro.Tabela!.ToString(), erro.Coluna));
    }

    [Fact]
    public async Task Coluna_tratada_com_tipo_diferente_da_INC3023_e_a_copiada_so_avisa_INC3521()
    {
        var destino = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Tipo), TipoColuna.Inteiro), (nameof(ColunaEsquema.Colacao), null), (nameof(ColunaEsquema.Comprimento), null),
            (nameof(ColunaEsquema.IgnoraCaixaPeloTipo), null), (nameof(ColunaEsquema.IgnoraEspacoFinalPeloTipo), null), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = long.MinValue, Maximo = long.MaxValue }));

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: { transformador: br.cpf@1 }\n", destino: destino));
        var montado = await PlanejarAsync("", destino: destino);

        Assert.Equal(("INC3023", "cpf"), (erro.Codigo, erro.Coluna));
        Assert.Contains("tipo Texto na origem e Inteiro no destino", erro.Mensagem, StringComparison.Ordinal);
        Assert.Contains(montado.Avisos, aviso => aviso.Codigo == "INC3521" && aviso.Coluna == "cpf");
    }

    [Fact]
    public async Task Coluna_mais_estreita_no_destino_avisa_INC3521()
    {
        var destino = TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 50, Unidade = UnidadeComprimento.PontosDeCodigo }));

        var aviso = Assert.Single((await PlanejarAsync("", destino: destino)).Avisos);

        Assert.Equal(("INC3521", "nome"), (aviso.Codigo, aviso.Coluna));
    }

    [Fact]
    public async Task Colacao_diferente_avisa_INC3522()
    {
        var naoDeterministica = new ColacaoEsquema { Nome = "ci", Deterministica = false, TemRegras = false, IgnoraCaixa = true, IgnoraAcento = false, IgnoraPontuacao = false };
        var destino = TrocarColuna(Coerente(), "clientes", "nome", (nameof(ColunaEsquema.Colacao), naoDeterministica));

        var aviso = Assert.Single((await PlanejarAsync("", destino: destino)).Avisos);

        Assert.Equal(("INC3522", "nome"), (aviso.Codigo, aviso.Coluna));
    }

    [Fact]
    public async Task Coluna_obrigatoria_so_no_destino_avisa_INC3520_e_restricao_so_num_lado_avisa_INC3523()
    {
        var destino = ComTabelas(
            BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Colunas), Clientes().Colunas.Append(Inteiro("versao", anulavel: false)).ToArray())),
            BancoFalso.Clonar(Pedidos(), (nameof(TabelaEsquema.ChavesEstrangeiras), Array.Empty<ChaveEstrangeira>())));

        var avisos = (await PlanejarAsync("", destino: destino)).Avisos;

        Assert.Equal([("INC3520", "loja.clientes", "versao", null), ("INC3523", "loja.pedidos", null, "fk_pedidos_clientes")], avisos.Select(aviso => (aviso.Codigo, aviso.Tabela!.ToString(), aviso.Coluna, aviso.Objeto)));
        Assert.Contains("só existe na origem", avisos[1].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nome_que_difere_so_na_caixa_avisa_INC3510()
    {
        var esquema = ComTabelas(Clientes(), Pedidos(), Simples("Clientes"));

        var montado = await PlanejarAsync("tabelas:\n  loja.clientes: {}\n", esquema, esquema);

        var aviso = Assert.Single(montado.Avisos);
        Assert.Equal(("INC3510", "loja.clientes", (string?)null), (aviso.Codigo, aviso.Tabela!.ToString(), aviso.Objeto));
        Assert.Contains("A tabela loja.Clientes difere desta só na caixa", aviso.Mensagem, StringComparison.Ordinal);

        // O casamento exato nunca vence: Clientes sem aspas é clientes, mesmo existindo "Clientes".
        var semAspas = await PlanejarAsync("tabelas:\n  loja.Clientes: { acao: esvaziar }\n", esquema, esquema);
        Assert.Equal(
            [("loja.Clientes", AcaoTabela.Copiar), ("loja.clientes", AcaoTabela.Esvaziar), ("loja.pedidos", AcaoTabela.Copiar)],
            semAspas.Tabelas.Select(tabela => (tabela.Nome.ToString(), tabela.Acao)));
    }

    [Fact]
    public async Task Os_nomes_do_catalogo_saem_escapados_nas_mensagens()
    {
        var origem = ComTabelas(Clientes(), Pedidos(), Simples("a\u001B[2Jb"));

        var erros = await RecusaAsync("", origem, origem, forcada: CorrespondenciaNome.Corresponde);

        Assert.Contains(erros, erro => erro.Mensagem.Contains("loja.a\\u001B[2Jb", StringComparison.Ordinal));
        Assert.All(erros, erro => Assert.DoesNotContain('\u001B', erro.Mensagem + erro.ToString()));
    }

    [Fact]
    public async Task Estrangeira_fora_da_configuracao_e_ignorada_com_INC3572_e_tocada_da_INC3071()
    {
        var esquema = ComTabelas(Clientes(), Pedidos(), BancoFalso.Clonar(Simples("remota"), (nameof(TabelaEsquema.Estrangeira), true)));

        var montado = await PlanejarAsync("", esquema, esquema);
        var copiada = Assert.Single(await RecusaAsync("tabelas:\n  loja.remota: { acao: copiar }\n", esquema, esquema));
        var comColuna = Assert.Single(await RecusaAsync("tabelas:\n  loja.remota:\n    colunas:\n      id: manter\n", esquema, esquema));

        var remota = montado.Tabelas.Single(tabela => tabela.Nome.Tabela == "remota");
        Assert.Equal((AcaoTabela.Ignorar, false), (remota.Acao, remota.AcaoEscrita));
        Assert.Equal(("INC3572", "loja.remota"), (Assert.Single(montado.Avisos).Codigo, montado.Avisos[0].Tabela!.ToString()));
        Assert.Equal(("INC3071", "INC3071"), (copiada.Codigo, comColuna.Codigo));
    }

    [Fact]
    public async Task Estrangeira_num_lado_so_da_INC3075_fora_da_configuracao_e_a_INC3071_pelo_lado_que_a_acao_toca()
    {
        var comRemota = ComTabelas(Clientes(), Pedidos(), BancoFalso.Clonar(Simples("remota"), (nameof(TabelaEsquema.Estrangeira), true)));
        var comLocal = ComTabelas(Clientes(), Pedidos(), Simples("remota"));

        // Fora da configuração, nunca ignorada sozinha: o dado do destino ficaria intocado.
        var naOrigem = Assert.Single(await RecusaAsync("", comRemota, comLocal));
        var noDestino = Assert.Single(await RecusaAsync("tabelas:\n  loja.remota: {}\n", comLocal, comRemota));

        // Esvaziar só toca o destino; copiar toca os dois lados; ignorar, nenhum.
        var esvaziada = await PlanejarAsync("tabelas:\n  loja.remota: { acao: esvaziar }\n", comRemota, comLocal);
        var ignorada = await PlanejarAsync("tabelas:\n  loja.remota: { acao: ignorar }\n", comLocal, comRemota);
        var tocadas = new[]
        {
            Assert.Single(await RecusaAsync("tabelas:\n  loja.remota: { acao: esvaziar }\n", comLocal, comRemota)),
            Assert.Single(await RecusaAsync("tabelas:\n  loja.remota: { acao: copiar }\n", comRemota, comLocal)),
            Assert.Single(await RecusaAsync("tabelas:\n  loja.remota: { acao: copiar }\n", comLocal, comRemota)),
            Assert.Single(await RecusaAsync("tabelas:\n  loja.remota:\n    colunas:\n      id: manter\n", comRemota, comLocal)),
        };

        Assert.Equal(("INC3075", "loja.remota", (PosicaoConfiguracao?)null), (naOrigem.Codigo, naOrigem.Tabela!.ToString(), naOrigem.Posicao));
        Assert.Contains("estrangeira na origem e local no destino", naOrigem.Mensagem, StringComparison.Ordinal);
        Assert.Contains("'acao: ignorar' ou 'acao: esvaziar'", naOrigem.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3075", true), (noDestino.Codigo, noDestino.Posicao is not null));
        Assert.Contains("estrangeira no destino e local na origem", noDestino.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("esvaziar'", noDestino.Mensagem, StringComparison.Ordinal);
        Assert.Equal((AcaoTabela.Esvaziar, true), esvaziada.Tabelas.Where(tabela => tabela.Nome.Tabela == "remota").Select(tabela => (tabela.Acao, tabela.AcaoEscrita)).Single());
        Assert.Equal((AcaoTabela.Ignorar, true), ignorada.Tabelas.Where(tabela => tabela.Nome.Tabela == "remota").Select(tabela => (tabela.Acao, tabela.AcaoEscrita)).Single());
        Assert.All([.. esvaziada.Avisos, .. ignorada.Avisos], aviso => Assert.NotEqual("INC3572", aviso.Codigo));
        Assert.All(tocadas, erro => Assert.Equal(("INC3071", true), (erro.Codigo, erro.Posicao is not null)));
    }

    [Fact]
    public async Task Particionada_com_particao_estrangeira_da_INC3072_salvo_ignorada()
    {
        var esquema = ComTabelas(Clientes(), Pedidos(), Vendas(), BancoFalso.Clonar(Particao("vendas_1", "vendas"), (nameof(TabelaEsquema.Estrangeira), true)));

        var erro = Assert.Single(await RecusaAsync("", esquema, esquema));
        await PlanejarAsync("tabelas:\n  loja.vendas: { acao: ignorar }\n", esquema, esquema);

        // A partição comum segue a raiz, copiada pelo pai, sem erro.
        var comum = ComTabelas(Clientes(), Pedidos(), Vendas(), Particao("vendas_1", "vendas"));
        var plano = await PlanejarAsync("", comum, comum);

        Assert.Equal(("INC3072", "loja.vendas"), (erro.Codigo, erro.Tabela!.ToString()));
        Assert.Equal(["loja.clientes", "loja.pedidos", "loja.vendas"], plano.Tabelas.Select(tabela => tabela.Nome.ToString()));
    }

    [Fact]
    public async Task Objeto_no_esquema_reservado_ou_nao_informado_da_INC3073()
    {
        var comObjeto = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosNoEsquemaReservado), new[] { new NomeObjeto("incognito", "execucao") }));
        var naoInformado = BancoFalso.Clonar(Coerente(), (nameof(EsquemaBanco.ObjetosNoEsquemaReservado), null));

        var objeto = Assert.Single(await RecusaAsync("", comObjeto));
        var nulo = Assert.Single(await RecusaAsync("", destino: naoInformado));

        Assert.Equal(("INC3073", "execucao"), (objeto.Codigo, objeto.Objeto));
        Assert.Contains("na origem", objeto.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC3073", nulo.Codigo);
        Assert.Contains("não informa o que há no destino", nulo.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_erros_saem_em_ordem_de_tabela_coluna_e_codigo()
    {
        var origem = ComTabelas(Clientes(), Pedidos(), Simples("zeta"), Simples("alfa"));

        var erros = await RecusaAsync("", origem);

        Assert.Equal(["loja.alfa", "loja.zeta"], erros.Select(erro => erro.Tabela!.ToString()));
    }

    [Fact]
    public async Task A_forma_da_unidade_olha_os_dois_lados()
    {
        // O esvaziamento é no destino: a herdeira que só existe lá (ignorada por escrito) exige o ONLY na mãe; e a gravação
        // numa particionada do destino não admite congelar, mesmo com a origem comum.
        var origem = ComTabelas(Clientes(), Pedidos(), Simples("pessoa"), Simples("vendas"));
        var herdeira = new TabelaEsquema { Nome = Nome("funcionario"), Tipo = TipoTabela.Herdeira, Pai = Nome("pessoa"), Colunas = [Inteiro("id")] };
        var destino = ComTabelas(Clientes(), Pedidos(), Simples("pessoa"), herdeira, BancoFalso.Clonar(Vendas(), (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id") })),
            BancoFalso.Clonar(Particao("vendas_1", "vendas"), (nameof(TabelaEsquema.Colunas), new[] { Inteiro("id") })));

        var plano = await PlanejarAsync("tabelas:\n  loja.funcionario: { acao: ignorar }\n", origem, destino);

        var unidades = plano.Execucao.Unidades.ToDictionary(unidade => unidade.Nome.Tabela);
        Assert.Equal((true, true), (unidades["pessoa"].SoAPropria, unidades["pessoa"].AdmiteFreeze));
        Assert.Equal((false, false), (unidades["vendas"].SoAPropria, unidades["vendas"].AdmiteFreeze));
        Assert.Equal((false, true), (unidades["clientes"].SoAPropria, unidades["clientes"].AdmiteFreeze));
    }

    [Fact]
    public async Task Tabela_do_esquema_reservado_entre_as_tabelas_da_INC3073()
    {
        // Um dialeto com defeito que deixe o diário entre as tabelas: o plano nunca o copia.
        var origem = ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = new NomeObjeto("incognito", "diario"), Colunas = [Inteiro("id")] });

        var erros = await RecusaAsync("", origem);

        Assert.Contains(erros, erro => erro.Codigo == "INC3073" && erro.Objeto == "diario" && erro.Mensagem.Contains("na origem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Particao_so_num_lado_tambem_da_INC3014()
    {
        var origem = ComTabelas(Clientes(), Pedidos(), Vendas(), Particao("vendas_2026", "vendas"));
        var destino = ComTabelas(Clientes(), Pedidos(), Vendas());

        var erro = Assert.Single(await RecusaAsync("tabelas:\n  loja.vendas_2026: { acao: esvaziar }\n", origem, destino));

        Assert.Equal("INC3014", erro.Codigo);
        Assert.Contains("escreva a regra em loja.vendas", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dois_nomes_da_origem_para_um_do_destino_dao_INC3012()
    {
        // O destino compara sem a caixa (como uma colação insensível no catálogo): "Pais" e "pais" da origem caem na mesma
        // tabela do destino, e "Email" e "email" na mesma coluna. Nenhum pareamento em silêncio.
        CorrespondenciaNome? SemCaixaNoDestino(string escrito, bool aspas, string catalogo, bool destino) =>
            destino ? (string.Equals(escrito, catalogo, StringComparison.OrdinalIgnoreCase) ? CorrespondenciaNome.Corresponde : CorrespondenciaNome.NaoCorresponde) : null;
        var tabelas = await RecusaAsync("", ComTabelas(Clientes(), Pedidos(), Simples("Pais"), Simples("pais")), ComTabelas(Clientes(), Pedidos(), Simples("pais")), comparar: SemCaixaNoDestino);
        var contatos = new TabelaEsquema { Nome = Nome("contatos"), Colunas = [Texto("Email", 50), Texto("email", 50)] };
        var colunas = await RecusaAsync("", ComTabelas(Clientes(), Pedidos(), contatos), ComTabelas(Clientes(), Pedidos(), new TabelaEsquema { Nome = Nome("contatos"), Colunas = [Texto("email", 50)] }), comparar: SemCaixaNoDestino);

        Assert.Equal([("INC3012", "loja.Pais"), ("INC3012", "loja.pais")], tabelas.Select(erro => (erro.Codigo, erro.Tabela!.ToString())));
        Assert.All(tabelas, erro => Assert.Contains("do destino corresponde a mais de uma tabela da origem", erro.Mensagem, StringComparison.Ordinal));
        Assert.Equal([("INC3012", "Email"), ("INC3012", "email")], colunas.Select(erro => (erro.Codigo, erro.Coluna!)));
    }

    [Fact]
    public async Task Coluna_e_derivarDe_indeterminados_dao_INC3012_e_cada_lado_usa_o_proprio_comparador()
    {
        // Só a origem acha "cpf" indeterminado: a entrada da coluna e o derivarDe caem no 3012.
        CorrespondenciaNome? CpfIndeterminadoNaOrigem(string escrito, bool aspas, string catalogo, bool destino) =>
            !destino && escrito == "cpf" && catalogo == "cpf" ? CorrespondenciaNome.Indeterminada : null;
        var coluna = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      cpf: manter\n", comparar: CpfIndeterminadoNaOrigem));
        var derivada = Assert.Single(await RecusaAsync("tabelas:\n  loja.clientes:\n    colunas:\n      nome: { transformador: br.cep@1, derivarDe: cpf }\n", comparar: CpfIndeterminadoNaOrigem));

        Assert.Equal(("INC3012", (string?)null), (coluna.Codigo, coluna.Coluna));
        Assert.Contains("A coluna cpf designa mais de uma coluna", coluna.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3012", "nome"), (derivada.Codigo, derivada.Coluna));

        // A entrada só resolve pela comparação da origem; o destino só pareia pelo nome do catálogo, entre aspas.
        CorrespondenciaNome? SoAOrigemEntende(string escrito, bool aspas, string catalogo, bool destino) =>
            !destino && escrito == "CLIENTES_X" && catalogo == "clientes" ? CorrespondenciaNome.Corresponde : null;
        var origem = ComTabelas(Clientes(), Pedidos());
        var semDestino = await RecusaAsync("tabelas:\n  loja.CLIENTES_X: { acao: esvaziar }\n", origem, origem, comparar: SoAOrigemEntende);
        Assert.Equal("INC3012", Assert.Single(semDestino).Codigo);
    }

    [Fact]
    public async Task Coluna_gerada_so_na_origem_nao_da_INC3022()
    {
        var gerada = new ColunaEsquema { Nome = "dobro", TipoNativo = "bigint", Situacao = SituacaoTipo.SuportadoPorExclusao, Geracao = GeracaoColuna.Armazenada };
        var origem = ComTabelas(BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Colunas), Clientes().Colunas.Append(gerada).ToArray())), Pedidos());

        var plano = await PlanejarAsync("", origem);

        Assert.DoesNotContain("dobro", plano.Execucao.Unidades.Single(unidade => unidade.Nome.Tabela == "clientes").ColunasLidas);
        Assert.Contains(plano.Tabelas[0].Colunas, coluna => coluna.Nome == "dobro");
    }

    [Fact]
    public async Task Identificador_vem_da_chave_do_indice_unico_da_expressao_e_da_chave_estrangeira_que_chega()
    {
        var clientes = BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.IndicesUnicos), Clientes().IndicesUnicos
            .Append(new IndiceUnico { Nome = "uq_cpf", Primario = false, Elementos = [new ElementoIndice { Coluna = "cpf" }], NulosDistintos = true })
            .ToArray()));
        var notas = new TabelaEsquema
        {
            Nome = Nome("notas"),
            Colunas = [Inteiro("id"), Texto("autor", 100)],
            ChavesEstrangeiras = [new ChaveEstrangeira { Nome = "fk_notas_autor", Colunas = ["autor"], Referenciada = Nome("clientes"), ColunasReferenciadas = ["nome"], Imposta = true, Validada = true }],
        };
        var contas = new TabelaEsquema
        {
            Nome = Nome("contas"),
            Colunas = [Texto("email", 100), Texto("apelido", 50)],
            IndicesUnicos =
            [
                new IndiceUnico
                {
                    Nome = "uq_email_minusculo", Primario = false, Elementos = [new ElementoIndice { Coluna = null }], NulosDistintos = true,
                    ReferenciasDasExpressoes = new ReferenciasDaExpressao { Colunas = ["email"], Incompletas = false },
                },
            ],
        };
        var esquema = ComTabelas(clientes, Pedidos(), notas, contas);

        var plano = await PlanejarAsync("", esquema, esquema);

        bool Identificador(string tabela, string coluna) =>
            plano.Tabelas.Single(mapa => mapa.Nome.Tabela == tabela).Colunas.Single(mapa => mapa.Nome == coluna).Classificacao.Identificador;
        Assert.True(Identificador("clientes", "cpf"));
        Assert.True(Identificador("clientes", "nome"));
        Assert.True(Identificador("contas", "email"));
        Assert.False(Identificador("contas", "apelido"));
        Assert.True(Identificador("notas", "autor"));
    }

    [Fact]
    public async Task Indice_unico_e_CHECK_so_num_lado_e_estrangeira_ignorada_por_escrito()
    {
        var comCheck = BancoFalso.Clonar(Clientes(),
            (nameof(TabelaEsquema.RestricoesCheck), new[] { new RestricaoCheck { Nome = "ck_nome", Referencias = new ReferenciasDaExpressao { Colunas = ["nome"], Incompletas = false }, Imposta = true } }),
            (nameof(TabelaEsquema.IndicesUnicos), Clientes().IndicesUnicos.Append(new IndiceUnico { Nome = "uq_cpf", Primario = false, Elementos = [new ElementoIndice { Coluna = "cpf" }], NulosDistintos = true }).ToArray()));
        var avisos = (await PlanejarAsync("", ComTabelas(comCheck, Pedidos()))).Avisos;

        Assert.Equal(
            [("INC3523", "ck_nome", "O CHECK ck_nome só existe na origem."), ("INC3523", "uq_cpf", "O índice único uq_cpf só existe na origem.")],
            avisos.Select(aviso => (aviso.Codigo, aviso.Objeto!, aviso.Mensagem)));

        // A estrangeira ignorada por escrito não ganha o aviso da ignorada sozinha.
        var esquema = ComTabelas(Clientes(), Pedidos(), BancoFalso.Clonar(Simples("remota"), (nameof(TabelaEsquema.Estrangeira), true)));
        Assert.Empty((await PlanejarAsync("tabelas:\n  loja.remota: { acao: ignorar }\n", esquema, esquema)).Avisos);
    }

    [Fact]
    public async Task A_regra_efetiva_leva_o_derivarDe_pelo_nome_do_catalogo_e_as_politicas()
    {
        var esquema = ComTabelas(BancoFalso.Clonar(Clientes(), (nameof(TabelaEsquema.Colunas), Clientes().Colunas.Append(Texto("cidade", 50)).ToArray())), Pedidos());
        var yaml = "tabelas:\n  loja.clientes:\n    colunas:\n      nome: { transformador: br.cep@1, derivarDe: CIDADE }\n      cpf: { transformador: br.cpf@1, entradaInvalida: falhar, entradaMalformada: anular }\n";

        var colunas = (await PlanejarAsync(yaml, esquema, esquema)).Tabelas[0].Colunas.ToDictionary(coluna => coluna.Nome);

        Assert.Equal("cidade", colunas["nome"].Regra!.DerivarDe);
        Assert.Equal((PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Anular), (colunas["cpf"].Regra!.EntradaInvalida, colunas["cpf"].Regra!.EntradaMalformada));
    }

    private static Task<Plano> PlanejarAsync(
        string tabelas, EsquemaBanco? origem = null, EsquemaBanco? destino = null, CorrespondenciaNome? forcada = null, Func<string, bool, string, bool, CorrespondenciaNome?>? comparar = null) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino) { Correspondencia = forcada, Comparar = comparar }, yaml: Yaml.Minimo + tabelas);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(
        string tabelas, EsquemaBanco? origem = null, EsquemaBanco? destino = null, CorrespondenciaNome? forcada = null, Func<string, bool, string, bool, CorrespondenciaNome?>? comparar = null) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(tabelas, origem, destino, forcada, comparar)), exactMatch: true).Erros;

    private static TabelaEsquema Simples(string nome) => new() { Nome = Nome(nome), Colunas = [Inteiro("id")] };

    private static TabelaEsquema Vendas() => new()
    {
        Nome = Nome("vendas"),
        Tipo = TipoTabela.Particionada,
        ChaveParticao = new ChaveParticao { Colunas = ["id"], ColunasIncompletas = false },
        Colunas = [Inteiro("id", anulavel: false)],
    };

    private static TabelaEsquema Particao(string nome, string pai) => new()
    {
        Nome = Nome(nome),
        Tipo = TipoTabela.Particao,
        Pai = Nome(pai),
        Colunas = [Inteiro("id", anulavel: false)],
    };
}
