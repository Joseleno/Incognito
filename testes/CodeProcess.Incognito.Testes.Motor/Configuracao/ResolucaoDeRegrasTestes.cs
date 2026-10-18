using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// Resolução das regras contra o registro: versão 1 como padrão permanente, parâmetro recusado pela fábrica apontando
/// linha e coluna, domínio padrão e conflitos (um nome, uma definição), exigências de coluna, precedência das políticas,
/// forma canônica dos parâmetros e o transformador próprio registrado, criado e usado. As mensagens nunca trazem valores.
/// </summary>
public sealed class ResolucaoDeRegrasTestes
{
    private const string Sentinela = "SENTINELA987654321";

    public static TheoryData<string> Embutidas => [.. TransformadoresEmbutidos.Fabricas.Select(fabrica => fabrica.Nome)];

    [Fact]
    public void Transformador_sem_versao_escrita_resolve_sempre_para_a_versao_1()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.maiusculas", 2));
        registro.Adicionar(new FabricaDeExemplo("exemplo.maiusculas", 1));

        var colunas = Resolver(Colunas("      nome: { transformador: exemplo.maiusculas }\n      cpf: { transformador: br.cpf }\n"), registro).Colunas;
        var escrita = Assert.Single(Resolver(Colunas("      outro: { transformador: exemplo.maiusculas@2 }\n"), registro).Colunas);

        Assert.Equal(1, colunas[0].Fabrica.Versao);
        Assert.Equal(new DominioPseudonimo("exemplo.maiusculas", 1), colunas[0].Regra.Dominio);
        Assert.Equal(new DominioPseudonimo("cpf", 1), colunas[1].Regra.Dominio);
        Assert.Equal(new DominioPseudonimo("exemplo.maiusculas", 2), escrita.Regra.Dominio);
        Assert.Equal(2, registro.VersaoMaisRecente("exemplo.maiusculas"));
    }

    [Fact]
    public void Sem_versao_escrita_e_sem_a_versao_1_o_erro_pede_a_versao()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.novo", 2));

        var erro = Assert.Single(Erros(Colunas("      nome: { transformador: exemplo.novo }\n"), registro));

        Assert.Equal(("INC2021", 15, 30, "/tabelas/public.clientes/colunas/nome/transformador"), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
        Assert.Contains("@2", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC2021", Assert.Single(Erros(Colunas("      cpf: { transformador: br.cpf@2 }\n"))).Codigo);
    }

    [Fact]
    public void Transformador_desconhecido_e_erro_e_quem_cita_o_dominio_dele_nao_ganha_outro()
    {
        var erros = Erros(Colunas("      cpf: x\n      outro: { transformador: exemplo.inexistente@1 }\n", "dominios:\n  x: { transformador: exemplo.inexistente@1 }\n"));

        Assert.Equal(["INC2020", "INC2020"], erros.Select(erro => erro.Codigo));
        Assert.Equal(["/dominios/x/transformador", "/tabelas/public.clientes/colunas/outro/transformador"], erros.Select(erro => erro.Local));
    }

    [Theory]
    [MemberData(nameof(Embutidas))]
    public void Parametro_desconhecido_de_um_transformador_embutido_e_recusado_pela_fabrica_com_linha_e_coluna(string nome)
    {
        var derivar = nome == "br.cep" ? " derivarDe: cpf," : "";
        var erro = Assert.Single(Erros(Colunas($"      x: {{ transformador: {nome}@1,{derivar} precisao: {Sentinela} }}\n")));

        Assert.Equal(("INC2022", 15), (erro.Codigo, erro.Linha));
        Assert.Equal(nome.Length + derivar.Length + 31, erro.Coluna);
        Assert.Equal("/tabelas/public.clientes/colunas/x/precisao", erro.Local);
        Assert.DoesNotContain(Sentinela, erro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parametro_desconhecido_num_dominio_declarado_aponta_o_parametro_no_dominio()
    {
        var erro = Assert.Single(Erros(Yaml.Com("dominios:\n  pessoa: { transformador: br.cpf@1, precisao: ano }\n")));

        Assert.Equal(("INC2022", 13, 38, "/dominios/pessoa/precisao"), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
    }

    [Fact]
    public void Cada_embutida_tem_o_dominio_padrao_da_fachada_ou_o_proprio_nome()
    {
        var colunas = Resolver(Colunas(
            "      a: { transformador: br.cpf@1 }\n      b: { transformador: br.cnpj@1 }\n      c: { transformador: br.telefone@1 }\n"
            + "      d: { transformador: email@1 }\n      e: { transformador: br.cep@1, derivarDe: a }\n      f: { transformador: texto.substituir@1 }\n")).Colunas;

        Assert.Equal(["cpf@1", "cnpj@1", "telefone@1", "email@1", "cep@1", "texto.substituir@1"], colunas.Select(coluna => coluna.Regra.Dominio.ToString()));
    }

    [Fact]
    public void Dominio_declarado_com_o_nome_de_um_padrao_e_outro_transformador_e_erro()
    {
        Assert.Equal(("INC2023", (int?)13, (int?)25, "/dominios/cpf/transformador"), Tupla(Unico(Yaml.Com("dominios:\n  cpf: { transformador: br.cnpj@1 }\n"))));
        Assert.Equal("INC2023", Unico(Yaml.Com("dominios:\n  email: { transformador: br.cpf@1 }\n")).Codigo);
        Assert.Equal("INC2023", Unico(Yaml.Com("dominios:\n  texto.substituir: { transformador: br.cpf@1 }\n")).Codigo);
    }

    [Fact]
    public void Dominio_declarado_com_o_nome_de_um_padrao_e_parametros_de_identidade_diferentes_e_erro()
    {
        Assert.Equal("INC2023", Unico(Yaml.Com("dominios:\n  cnpj: { transformador: br.cnpj@1, permutarOrdem: true }\n")).Codigo);
        Resolver(Yaml.Com("dominios:\n  cnpj: { transformador: br.cnpj@1, permutarOrdem: false }\n"));
        Resolver(Yaml.Com("dominios:\n  cpf: { transformador: br.cpf@1, entradaInvalida: anular }\n"));
    }

    [Fact]
    public void O_padrao_declarado_e_o_implicito_sao_o_mesmo_universo()
    {
        var colunas = Resolver(Colunas("      a: cpf\n      b: { transformador: br.cpf@1 }\n", "dominios:\n  cpf: { transformador: br.cpf@1 }\n")).Colunas;

        Assert.Equal(colunas[0].Regra.Dominio, colunas[1].Regra.Dominio);
    }

    [Fact]
    public void Coluna_sem_dominio_com_parametro_de_identidade_fora_do_padrao_e_erro_e_com_dominio_proprio_nao()
    {
        var erro = Unico(Colunas("      cnpj: { transformador: br.cnpj@1, permutarOrdem: true }\n"));
        Assert.Equal(("INC2023", "/tabelas/public.clientes/colunas/cnpj/permutarOrdem"), (erro.Codigo, erro.Local));

        Resolver(Colunas("      cnpj: { transformador: br.cnpj@1, permutarOrdem: false }\n"));
        var coluna = Assert.Single(Resolver(Colunas("      cnpj: fornecedor\n", "dominios:\n  fornecedor: { transformador: br.cnpj@1, permutarOrdem: true }\n")).Colunas);
        Assert.Equal(("fornecedor@1", "true"), (coluna.Regra.Dominio.ToString(), coluna.Regra.Parametros["permutarOrdem"]));
    }

    [Fact]
    public void Parametro_que_nao_muda_o_universo_nao_cria_conflito()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.com-parametro", 1, aceita: "estilo", identidade: null, injetividade: Injetividade.Garantida));
        registro.Adicionar(new FabricaDeExemplo("exemplo.sem-ligacao", 1, aceita: "estilo", identidade: "estilo", injetividade: Injetividade.Nenhuma));

        var semIdentidade = Resolver(Colunas("      a: { transformador: exemplo.com-parametro@1, estilo: x }\n      b: { transformador: exemplo.com-parametro@1, estilo: y }\n"), registro).Colunas;
        var semLigacao = Resolver(Colunas("      a: { transformador: exemplo.sem-ligacao@1, estilo: x }\n      b: { transformador: exemplo.sem-ligacao@1, estilo: y }\n"), registro).Colunas;

        Assert.Equal(["x", "y"], semIdentidade.Select(coluna => coluna.Regra.Parametros["estilo"]));
        Assert.Equal(["x", "y"], semLigacao.Select(coluna => coluna.Regra.Parametros["estilo"]));
        Assert.All(semIdentidade, coluna => Assert.Equal("exemplo.com-parametro@1", coluna.Regra.Dominio.ToString()));
    }

    [Fact]
    public void Um_nome_de_dominio_tem_uma_definicao_so_inclusive_na_versao()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.doc", 1, injetividade: Injetividade.Garantida));
        registro.Adicionar(new FabricaDeExemplo("exemplo.doc", 2, injetividade: Injetividade.Garantida));

        var implicitos = Unico(Colunas("      a: { transformador: exemplo.doc@1 }\n      b: { transformador: exemplo.doc@2 }\n"), registro);
        var declaradoEImplicito = Unico(Colunas("      a: { transformador: exemplo.doc@1 }\n", "dominios:\n  exemplo.doc: { transformador: exemplo.doc@2 }\n"), registro);

        Assert.Equal(("INC2023", "/tabelas/public.clientes/colunas/b/transformador"), (implicitos.Codigo, implicitos.Local));
        Assert.Equal(("INC2023", "/tabelas/public.clientes/colunas/a/transformador"), (declaradoEImplicito.Codigo, declaradoEImplicito.Local));
        Resolver(Colunas("      a: { transformador: exemplo.doc@2 }\n", "dominios:\n  exemplo.doc: { transformador: exemplo.doc@2 }\n"), registro);
    }

    [Fact]
    public void O_CEP_exige_derivarDe_na_coluna_e_nao_na_declaracao_do_dominio()
    {
        var semDerivar = Unico(Colunas("      cep: { transformador: br.cep@1 }\n"));
        Assert.Equal(("INC2022", 15, 7, "/tabelas/public.clientes/colunas/cep"), (semDerivar.Codigo, semDerivar.Linha, semDerivar.Coluna, semDerivar.Local));

        var dominio = "dominios:\n  cep: { transformador: br.cep@1 }\n";
        Assert.Equal("INC2022", Unico(Colunas("      cep: cep\n", dominio)).Codigo);
        var coluna = Assert.Single(Resolver(Colunas("      cpf: manter\n      cep: { dominio: cep, derivarDe: cpf }\n", dominio)).Colunas);
        Assert.Equal((true, "cep@1"), (coluna.Regra.TemDerivarDe, coluna.Regra.Dominio.ToString()));
    }

    [Fact]
    public void DerivarDe_onde_o_transformador_nao_usa_e_recusado()
    {
        Assert.Equal("INC2022", Unico(Colunas("      a: manter\n      cpf: { transformador: br.cpf@1, derivarDe: a }\n")).Codigo);
    }

    [Fact]
    public void A_politica_mais_especifica_vence_coluna_dominio_global_e_padrao()
    {
        var dominios = "dominios:\n  pessoa: { transformador: br.cpf@1, entradaInvalida: anular, entradaMalformada: preservar }\n";
        var semGlobal = Resolver(Colunas("      a: pessoa\n      b: { dominio: pessoa, entradaInvalida: preservar }\n      c: { transformador: br.cpf@1 }\n", dominios)).Colunas;
        var comGlobal = Resolver(Colunas("      c: { transformador: br.cpf@1 }\n      d: { transformador: br.cpf@1, entradaMalformada: anular }\n").Replace("ambiente: homologacao\n", "ambiente: homologacao\npolitica:\n  entradaInvalida: falhar\n  entradaMalformada: preservar\n", StringComparison.Ordinal)).Colunas;

        Assert.Equal((PoliticaEntradaInvalida.Anular, PoliticaEntradaMalformada.Preservar), Politicas(semGlobal[0]));
        Assert.Equal((PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar), Politicas(semGlobal[1]));
        Assert.Equal((PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar), Politicas(semGlobal[2]));
        Assert.Equal((PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Preservar), Politicas(comGlobal[0]));
        Assert.Equal((PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Anular), Politicas(comGlobal[1]));
    }

    [Fact]
    public void A_coluna_sem_dominio_herda_a_politica_do_dominio_declarado_com_o_mesmo_nome()
    {
        var texto = Colunas("      a: { transformador: br.cpf@1 }\n      b: cpf\n      c: { transformador: br.cpf@1, entradaInvalida: falhar }\n", "dominios:\n  cpf: { transformador: br.cpf@1, entradaInvalida: anular }\n")
            .Replace("ambiente: homologacao\n", "ambiente: homologacao\npolitica:\n  entradaInvalida: preservar\n", StringComparison.Ordinal);

        var colunas = Resolver(texto).Colunas;

        Assert.Equal([PoliticaEntradaInvalida.Anular, PoliticaEntradaInvalida.Anular, PoliticaEntradaInvalida.Falhar], colunas.Select(coluna => coluna.Regra.EntradaInvalida));
    }

    [Fact]
    public void Fabrica_que_nao_normaliza_ou_falha_na_regra_padrao_e_recusada_sem_a_mensagem()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaQueNaoNormaliza());
        registro.Adicionar(new FabricaQueFalhaNoPadrao());

        Assert.Equal("INC2022", Unico(Colunas("      a: { transformador: exemplo.sem-normalizar@1 }\n"), registro).Codigo);
        var padrao = Unico(Colunas("      a: { transformador: exemplo.falha-padrao@1, estilo: x }\n"), registro);
        Assert.Equal("INC2022", padrao.Codigo);
        Assert.Contains(nameof(KeyNotFoundException), padrao.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, padrao.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_erro_da_regra_padrao_de_uma_fabrica_sai_uma_vez_e_o_cancelamento_sobe()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaQueFalhaNoPadrao());
        registro.Adicionar(new FabricaQueCancela());

        var erros = Erros(Colunas("      a: { transformador: exemplo.falha-padrao@1, estilo: x }\n      b: { transformador: exemplo.falha-padrao@1, estilo: x }\n"), registro);

        Assert.Single(erros);
        Assert.Throws<OperationCanceledException>(() => Resolver(Colunas("      a: { transformador: exemplo.cancela@1 }\n"), registro));
    }

    [Fact]
    public void A_fabrica_registrada_cria_conferindo_o_que_registrou()
    {
        var fabrica = new FabricaInconstante();
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        Assert.True(registro.TentarObter("empresa.inconstante", 1, out var registrada));
        using var chave = ChaveIncognito.Gerar();
        using var sessao = new SessaoTransformacao(chave, "homologacao");
        var regra = new RegraTransformacao(new DominioPseudonimo("empresa.inconstante", 1));

        Assert.Throws<RegraInvalidaException>(() => registrada.Criar(new ContextoCriacao(regra, TipoColuna.Binario, sessao)));
        fabrica.Mudar();
        Assert.Throws<InvalidOperationException>(() => registrada.Criar(new ContextoCriacao(regra, TipoColuna.Texto, sessao)));
    }

    [Theory]
    [InlineData("9223372036854775808", "9223372036854775808")]
    [InlineData("+9223372036854775808", "9223372036854775808")]
    [InlineData("0009223372036854775808", "9223372036854775808")]
    [InlineData("0xFFFFFFFFFFFFFFFFF", "295147905179352825855")]
    [InlineData("0o7", "7")]
    [InlineData("-0", "0")]
    public void Inteiros_grandes_do_YAML_tambem_chegam_na_forma_canonica(string escrito, string canonico)
    {
        var dominio = Assert.Single(Yaml.Ler(Yaml.Com($"dominios:\n  notas: {{ transformador: exemplo.x@1, limite: {escrito} }}\n")).Dominios);

        Assert.Equal(canonico, Assert.Single(dominio.Parametros).Valor);
    }

    [Fact]
    public void Lista_vazia_de_parametro_e_recusada_na_leitura()
    {
        var erro = Yaml.UnicoErro(Yaml.Com("dominios:\n  notas: { transformador: exemplo.x@1, modelos: [] }\n"));

        Assert.Equal(("INC2008", "/dominios/notas/modelos"), (erro.Codigo, erro.Local));
    }

    [Theory]
    [InlineData("True", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("false", "false")]
    [InlineData("\"true\"", "true")]
    public void Logicos_do_YAML_chegam_na_forma_canonica(string escrito, string canonico)
    {
        var coluna = Assert.Single(Resolver(Colunas("      cnpj: fornecedor\n", $"dominios:\n  fornecedor: {{ transformador: br.cnpj@1, permutarOrdem: {escrito} }}\n")).Colunas);

        Assert.Equal(canonico, coluna.Regra.Parametros["permutarOrdem"]);
    }

    [Fact]
    public void Inteiros_do_YAML_chegam_em_decimal_e_listas_separadas_por_virgula()
    {
        var dominio = Assert.Single(Yaml.Ler(Yaml.Com("dominios:\n  notas: { transformador: br.chave-dfe@1, modelos: [0x37, 0o101, +57, 058] }\n")).Dominios);

        Assert.Equal("55,65,57,58", Assert.Single(dominio.Parametros).Valor);
    }

    [Fact]
    public void Todos_os_erros_vem_juntos_e_sem_valores()
    {
        var erros = Erros(Colunas(
            $"      a: {{ transformador: br.cpf@1, precisao: {Sentinela} }}\n      b: {{ transformador: br.cnpj@1, permutarOrdem: {Sentinela} }}\n      c: {{ transformador: exemplo.x@1 }}\n      d: {{ transformador: br.cep@1 }}\n"));

        Assert.Equal(["INC2022", "INC2022", "INC2020", "INC2022"], erros.Select(erro => erro.Codigo));
        Assert.All(erros, erro => Assert.DoesNotContain(Sentinela, erro.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Transformador_proprio_registrado_e_resolvido_criado_e_usado()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.maiusculas", 1));
        var coluna = Assert.Single(Resolver(Colunas("      apelido: { transformador: exemplo.maiusculas@1 }\n"), registro).Colunas);

        using var chave = ChaveIncognito.Gerar();
        using var sessao = new SessaoTransformacao(chave, "homologacao");
        var transformador = coluna.Fabrica.Criar(new ContextoCriacao(coluna.Regra, TipoColuna.Texto, sessao) { ComprimentoMaximo = 40 });

        Assert.Equal(("public", "clientes", "apelido"), (coluna.Tabela.Esquema.Nome, coluna.Tabela.Tabela.Nome, coluna.Coluna.Coluna.Nome));
        Assert.Equal(new DominioPseudonimo("exemplo.maiusculas", 1), coluna.Regra.Dominio);
        Assert.Equal(("ANA", Desfecho.Substituido), (transformador.Transformar("ana", default).Valor, transformador.Transformar("ana", default).Desfecho));
    }

    [Fact]
    public void A_fabrica_propria_confere_a_regra_e_um_erro_inesperado_vira_recusa_sem_a_mensagem()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("exemplo.maiusculas", 1));
        registro.Adicionar(new FabricaQueFalha());

        Assert.Equal(("INC2022", "/tabelas/public.clientes/colunas/a/estilo"), Codigo(Unico(Colunas("      a: { transformador: exemplo.maiusculas@1, estilo: x }\n"), registro)));
        var inesperado = Unico(Colunas("      a: { transformador: exemplo.falha@1 }\n"), registro);
        Assert.Equal("INC2022", inesperado.Codigo);
        Assert.Contains(nameof(FormatException), inesperado.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, inesperado.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("br.algo")]
    [InlineData("id.algo")]
    [InlineData("texto.algo")]
    [InlineData("data.algo")]
    [InlineData("numero.algo")]
    [InlineData("json.algo")]
    [InlineData("incognito.algo")]
    [InlineData("email.algo")]
    [InlineData("fixo.algo")]
    [InlineData("nulo.algo")]
    [InlineData("email")]
    [InlineData("fixo")]
    [InlineData("nulo")]
    [InlineData("chave-pix")]
    [InlineData("dfe-numero")]
    public void Nomes_reservados_sao_recusados_no_registro(string nome)
    {
        Assert.Throws<ArgumentException>(() => new RegistroTransformadores().Adicionar(new FabricaDeExemplo(nome, 1)));
    }

    [Fact]
    public void O_registro_recusa_repetido_fora_da_regra_e_dominio_padrao_de_outra_fabrica()
    {
        var registro = new RegistroTransformadores();
        registro.Adicionar(new FabricaDeExemplo("empresa.doc", 1));

        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("empresa.doc", 1)));
        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("cpf", 1)));
        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("Empresa.Doc", 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => registro.Adicionar(new FabricaDeExemplo("empresa.outro", 0)));
        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("empresa.estranha", 1, injetividade: (Injetividade)9)));
        Assert.Throws<ArgumentNullException>(() => registro.Adicionar(null!));
        Assert.Throws<ArgumentNullException>(() => registro.Adicionar(new FabricaDeExemplo(null!, 1)));
        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("empresa.sem-tipos", 1, tipos: [])));
        Assert.Throws<ArgumentException>(() => registro.Adicionar(new FabricaDeExemplo("empresa.tipo-estranho", 1, tipos: [(TipoColuna)42])));
        registro.Adicionar(new FabricaDeExemplo("empresa.doc", 2));
        Assert.True(registro.TentarObter("empresa.doc", 2, out _));
        Assert.Null(registro.VersaoMaisRecente("empresa.nada"));
    }

    [Fact]
    public void O_registro_le_os_metadados_uma_vez()
    {
        var fabrica = new FabricaInconstante();
        var registro = new RegistroTransformadores();
        registro.Adicionar(fabrica);
        fabrica.Mudar();

        Assert.True(registro.TentarObter("empresa.inconstante", 1, out var registrada));
        Assert.Equal(("empresa.inconstante", "empresa.inconstante"), (registrada.Nome, registrada.DominioPadrao));
    }

    private static (string Codigo, string Local) Codigo(ErroConfiguracao erro) => (erro.Codigo, erro.Local);

    private static (string Codigo, int? Linha, int? Coluna, string Local) Tupla(ErroConfiguracao erro) => (erro.Codigo, erro.Linha, erro.Coluna, erro.Local);

    private static (PoliticaEntradaInvalida, PoliticaEntradaMalformada) Politicas(ColunaResolvida coluna) => (coluna.Regra.EntradaInvalida, coluna.Regra.EntradaMalformada);

    private static string Colunas(string colunas, string dominios = "") =>
        Yaml.Com(dominios + "tabelas:\n  public.clientes:\n    colunas:\n" + colunas);

    private static ResolucaoDeRegras Resolver(string texto, RegistroTransformadores? registro = null) =>
        ResolvedorDeRegras.Resolver(Yaml.Ler(texto), registro ?? new RegistroTransformadores());

    private static ErroConfiguracao Unico(string texto, RegistroTransformadores? registro = null) => Assert.Single(Erros(texto, registro));

    private static IReadOnlyList<ErroConfiguracao> Erros(string texto, RegistroTransformadores? registro = null)
    {
        try
        {
            Resolver(texto, registro);
        }
        catch (ConfiguracaoInvalidaException excecao)
        {
            Assert.Equal("INC2000", excecao.Codigo);
            return excecao.Erros;
        }

        throw new XunitException("A resolução deveria ter sido recusada.");
    }

    // Uma fábrica de fora do núcleo, sem chave: não é pseudonimização, só prova o caminho do registro ao transformador.
    private sealed class FabricaDeExemplo(
        string nome, int versao, string? aceita = null, string? identidade = null, Injetividade injetividade = Injetividade.Nenhuma, TipoColuna[]? tipos = null) : FabricaTransformador
    {
        public override string Nome => nome;

        public override int Versao => versao;

        public override Injetividade Injetividade => injetividade;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna>(tipos ?? [TipoColuna.Texto]);

        public override IReadOnlySet<string> ParametrosDeIdentidade { get; } = identidade is null ? new HashSet<string>() : new HashSet<string> { identidade };

        public override void Validar(RegraTransformacao regra)
        {
            ArgumentNullException.ThrowIfNull(regra);
            if (regra.Parametros.Keys.FirstOrDefault(parametro => parametro != aceita) is { } desconhecido)
            {
                throw new RegraInvalidaException("Parâmetro desconhecido.", desconhecido);
            }
        }

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Maiusculas(nome, versao);
    }

    private sealed class Maiusculas(string nome, int versao) : Transformador
    {
        public override string Nome => nome;

        public override int Versao => versao;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) =>
            new(((string)valor!).ToUpperInvariant(), Desfecho.Substituido);
    }

    private sealed class FabricaQueFalha : FabricaTransformador
    {
        public override string Nome => "exemplo.falha";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override void Validar(RegraTransformacao regra) => throw new FormatException($"The input string '{Sentinela}' was not in a correct format.");

        protected override Transformador CriarCore(ContextoCriacao contexto) => throw new NotSupportedException();
    }

    private sealed class FabricaQueNaoNormaliza : FabricaTransformador
    {
        public override string Nome => "exemplo.sem-normalizar";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override IReadOnlyDictionary<string, string> Normalizar(RegraTransformacao regra) => null!;

        protected override Transformador CriarCore(ContextoCriacao contexto) => throw new NotSupportedException();
    }

    // Com parâmetro de identidade e injetividade, o resolvedor pede a regra padrão, e esta fábrica falha nela.
    private sealed class FabricaQueFalhaNoPadrao : FabricaTransformador
    {
        public override string Nome => "exemplo.falha-padrao";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override IReadOnlySet<string> ParametrosDeIdentidade { get; } = new HashSet<string> { "estilo" };

        public override void Validar(RegraTransformacao regra)
        {
        }

        public override IReadOnlyDictionary<string, string> Normalizar(RegraTransformacao regra) =>
            regra.Parametros.Count > 0 ? regra.Parametros : throw new KeyNotFoundException($"{Sentinela} sem estilo.");

        protected override Transformador CriarCore(ContextoCriacao contexto) => throw new NotSupportedException();
    }

    private sealed class FabricaQueCancela : FabricaTransformador
    {
        public override string Nome => "exemplo.cancela";

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        public override void Validar(RegraTransformacao regra) => throw new OperationCanceledException();

        protected override Transformador CriarCore(ContextoCriacao contexto) => throw new NotSupportedException();
    }

    private sealed class FabricaInconstante : FabricaTransformador
    {
        private string _nome = "empresa.inconstante";

        public override string Nome => _nome;

        public override int Versao => 1;

        public override Injetividade Injetividade => Injetividade.Nenhuma;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = new HashSet<TipoColuna> { TipoColuna.Texto };

        internal void Mudar() => _nome = "br.cpf";

        protected override Transformador CriarCore(ContextoCriacao contexto) => new Maiusculas(_nome, 1);
    }
}
