using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// A chave Pix e a chave de DF-e na resolução: os domínios citados em <c>dominioCpf</c>, <c>dominioCnpj</c>,
/// <c>dominioEmail</c> e <c>dominioTelefone</c> conferidos antes do banco (existem e são do transformador certo), a
/// tabela de domínios da sessão com os declarados, os implícitos e os citados, com as políticas de cada domínio, o
/// domínio fixo da DF-e e a justificativa do <c>preservar</c> na chave Pix. Ponta a ponta: a chave criada com a tabela
/// da resolução sai igual à coluna do documento.
/// </summary>
public sealed class DominiosCitadosTestes
{
    private const string Sentinela = "SENTINELA987654321";
    private const string GlobalAnulando = "ambiente: homologacao\npolitica:\n  entradaInvalida: anular\n";

    [Fact]
    public void A_chave_Pix_sem_parametros_resolve_no_dominio_padrao_e_poe_os_quatro_documentos_na_tabela()
    {
        var resolucao = Resolver(Colunas("      pix: { transformador: br.chave-pix }\n"));

        var coluna = Assert.Single(resolucao.Colunas);
        Assert.Equal("chave-pix@1", coluna.Regra.Dominio.ToString());
        Assert.Equal(
            [("chave-pix", "br.chave-pix"), ("cnpj", "br.cnpj"), ("cpf", "br.cpf"), ("email", "email"), ("telefone", "br.telefone")],
            resolucao.Dominios.Select(dominio => (dominio.Nome, dominio.Transformador)));
        Assert.All(resolucao.Dominios, dominio => Assert.Equal(1, dominio.Regra.Dominio.Versao));
    }

    [Fact]
    public void A_chave_de_DFe_resolve_sempre_no_dfe_numero_e_poe_o_CPF_e_o_CNPJ_na_tabela()
    {
        var resolucao = Resolver(Colunas("      nota: { transformador: br.chave-dfe, modelos: [65, 55] }\n"));

        var coluna = Assert.Single(resolucao.Colunas);
        Assert.Equal(("dfe-numero@1", "55,65"), (coluna.Regra.Dominio.ToString(), coluna.Fabrica.Fabrica.Normalizar(coluna.Regra)["modelos"]));
        Assert.Equal(["cnpj", "cpf", "dfe-numero"], resolucao.Dominios.Select(dominio => dominio.Nome));
    }

    [Fact]
    public void Dominio_de_DFe_com_outro_nome_e_recusado()
    {
        var erro = Unico(Yaml.Com("dominios:\n  notas: { transformador: br.chave-dfe@1 }\n"));

        Assert.Equal(("INC2022", "/dominios/notas"), (erro.Codigo, erro.Local));
        Assert.Contains("dfe-numero", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Dominio_citado_declarado_e_aceito_e_o_padrao_nao_citado_fica_fora_da_tabela()
    {
        var antes = Resolver(Colunas("      pix: { transformador: br.chave-pix, dominioCpf: cpf-loja }\n", "dominios:\n  cpf-loja: { transformador: br.cpf@1 }\n"));
        var naDfe = Resolver(Colunas("      nota: { transformador: br.chave-dfe, dominioCnpj: cnpj-loja }\n", "dominios:\n  cnpj-loja: { transformador: br.cnpj@1, permutarOrdem: true }\n"));

        Assert.Contains(antes.Dominios, dominio => dominio.Nome == "cpf-loja");
        Assert.DoesNotContain(antes.Dominios, dominio => dominio.Nome == "cpf");
        Assert.Equal("true", Assert.Single(naDfe.Dominios, dominio => dominio.Nome == "cnpj-loja").Regra.Parametros["permutarOrdem"]);
    }

    [Fact]
    public void Dominio_citado_inexistente_e_erro_no_parametro_sem_o_valor()
    {
        var erro = Unico(Colunas($"      pix: {{ transformador: br.chave-pix, dominioTelefone: tel-{Sentinela.ToLowerInvariant()} }}\n"));

        Assert.Equal(("INC2022", "/tabelas/public.clientes/colunas/pix/dominioTelefone"), (erro.Codigo, erro.Local));
        Assert.Contains("br.telefone@1", erro.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, erro.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pessoa: { transformador: br.cnpj@1 }", "dominioCpf: pessoa")]
    [InlineData("loja: { transformador: email@1 }", "dominioTelefone: loja")]
    [InlineData("", "dominioCpf: cnpj")]
    [InlineData("", "dominioEmail: telefone")]
    public void Dominio_citado_de_outro_transformador_e_erro_no_parametro(string declarado, string parametro)
    {
        var dominios = declarado.Length == 0 ? "" : $"dominios:\n  {declarado}\n";
        var erro = Unico(Colunas($"      pix: {{ transformador: br.chave-pix, {parametro} }}\n      outra: {{ transformador: br.cnpj }}\n      tel: {{ transformador: br.telefone }}\n", dominios));

        Assert.Equal(("INC2022", $"/tabelas/public.clientes/colunas/pix/{parametro[..parametro.IndexOf(':', StringComparison.Ordinal)]}"), (erro.Codigo, erro.Local));
    }

    [Fact]
    public void Dominio_citado_na_declaracao_de_um_dominio_aponta_o_parametro_no_dominio()
    {
        var erro = Unico(Yaml.Com("dominios:\n  pix-loja: { transformador: br.chave-pix@1, dominioCnpj: inexistente }\n"));

        Assert.Equal(("INC2022", "/dominios/pix-loja/dominioCnpj"), (erro.Codigo, erro.Local));
    }

    [Fact]
    public void Dominio_citado_que_falhou_nao_ganha_outro_erro()
    {
        var erros = Erros(Colunas("      pix: { transformador: br.chave-pix, dominioCpf: pessoa }\n", "dominios:\n  pessoa: { transformador: br.cpf@1, precisao: ano }\n"));

        Assert.Equal(["/dominios/pessoa/precisao"], erros.Select(erro => erro.Local));
    }

    [Fact]
    public void Os_dominios_citados_sao_referencia_e_a_coluna_sem_dominio_os_aceita_fora_do_padrao()
    {
        var resolucao = Resolver(Colunas(
            "      pix: { transformador: br.chave-pix, dominioCpf: cpf-loja }\n      outra: { transformador: br.chave-pix }\n      nota: { transformador: br.chave-dfe, dominioCpf: cpf-loja }\n      nota2: { transformador: br.chave-dfe }\n",
            "dominios:\n  cpf-loja: { transformador: br.cpf@1 }\n"));

        Assert.Equal(["chave-pix@1", "chave-pix@1", "dfe-numero@1", "dfe-numero@1"], resolucao.Colunas.Select(coluna => coluna.Regra.Dominio.ToString()));
        Assert.Equal("cpf-loja", resolucao.Colunas[0].Regra.Parametros["dominioCpf"]);
    }

    [Fact]
    public void A_tabela_traz_as_politicas_do_dominio_e_nao_as_da_coluna()
    {
        var texto = Colunas(
            "      pix: { transformador: br.chave-pix }\n      doc: { transformador: br.cpf, entradaInvalida: falhar }\n      cnpj: fornecedor\n",
            "dominios:\n  fornecedor: { transformador: br.cnpj@1, entradaInvalida: preservar }\n").Replace("ambiente: homologacao\n", GlobalAnulando, StringComparison.Ordinal);

        var dominios = Resolver(texto).Dominios.ToDictionary(dominio => dominio.Nome, StringComparer.Ordinal);

        Assert.Equal(PoliticaEntradaInvalida.Anular, dominios["cpf"].Regra.EntradaInvalida);
        Assert.Equal(PoliticaEntradaInvalida.Anular, dominios["cnpj"].Regra.EntradaInvalida);
        Assert.Equal(PoliticaEntradaInvalida.Preservar, dominios["fornecedor"].Regra.EntradaInvalida);
        Assert.All(dominios.Values, dominio => Assert.Equal((null, null), (dominio.Regra.TemDerivarDe, dominio.Regra.TemJustificativa)));
    }

    [Fact]
    public void Preservar_na_chave_Pix_exige_justificativa_na_coluna_e_pode_vir_do_dominio()
    {
        Assert.Equal(
            ("INC2022", "/tabelas/public.clientes/colunas/pix"),
            Codigo(Unico(Colunas("      pix: { transformador: br.chave-pix, entradaMalformada: preservar }\n"))));
        Assert.Equal(
            ("INC2022", "/tabelas/public.clientes/colunas/pix"),
            Codigo(Unico(Colunas("      pix: pix-loja\n", "dominios:\n  pix-loja: { transformador: br.chave-pix@1, entradaMalformada: preservar }\n"))));

        Resolver(Colunas("      pix: { transformador: br.chave-pix, entradaMalformada: preservar, justificativa: \"chaves antigas fora do padrao do DICT\" }\n"));
        Resolver(Colunas("      pix: { dominio: pix-loja, justificativa: \"chaves antigas fora do padrao do DICT\" }\n", "dominios:\n  pix-loja: { transformador: br.chave-pix@1, entradaMalformada: preservar }\n"));
    }

    [Fact]
    public void A_chave_criada_com_a_tabela_da_resolucao_sai_igual_a_coluna_do_documento()
    {
        var resolucao = Resolver(Colunas(
            "      doc: { transformador: br.cpf }\n      pix: { transformador: br.chave-pix }\n      emit: cnpj-loja\n      nota: { transformador: br.chave-dfe, dominioCnpj: cnpj-loja }\n",
            "dominios:\n  cnpj-loja: { transformador: br.cnpj@1, permutarOrdem: true }\n"));
        using var chave = ChaveIncognito.Gerar();
        using var sessao = new SessaoTransformacao(chave, "homologacao") { Dominios = resolucao.Dominios };
        var cpf = new GeradorBr(3301).Cpf();

        var transformadores = resolucao.Colunas.Select(coluna => coluna.Fabrica.Criar(new ContextoCriacao(coluna.Regra, TipoColuna.Texto, sessao))).ToList();

        Assert.Equal(transformadores[0].Transformar(cpf, default).Valor, transformadores[1].Transformar(cpf, default).Valor);

        // O emitente da chave (CNPJ de matriz, posições 7 a 20) sai igual à coluna do domínio cnpj-loja.
        var chaveDfe = new GeradorBr(3302).ChaveDfe(55);
        var pseudonimo = (string)transformadores[3].Transformar(chaveDfe, default).Valor!;
        Assert.Equal(transformadores[2].Transformar(chaveDfe[6..20], default).Valor, pseudonimo[6..20]);
    }

    [Fact]
    public void Dominio_padrao_de_outra_fabrica_citado_sem_coluna_e_erro()
    {
        Assert.Equal(
            ("INC2022", "/tabelas/public.clientes/colunas/pix/dominioCpf"),
            Codigo(Unico(Colunas("      pix: { transformador: br.chave-pix, dominioCpf: cnpj }\n"))));
    }

    [Fact]
    public void Dominio_citado_em_conflito_da_um_erro_so()
    {
        var erro = Unico(Colunas("      pix: { transformador: br.chave-pix }\n", "dominios:\n  cpf: { transformador: br.cnpj@1 }\n"));

        Assert.Equal(("INC2023", "/dominios/cpf/transformador"), Codigo(erro));
    }

    [Fact]
    public void Cada_coluna_que_cita_um_dominio_inexistente_ganha_o_proprio_erro()
    {
        var erros = Erros(Colunas("      a: { transformador: br.chave-pix, dominioEmail: inexistente }\n      b: { transformador: br.chave-pix, dominioEmail: inexistente }\n"));

        Assert.Equal(["/tabelas/public.clientes/colunas/a/dominioEmail", "/tabelas/public.clientes/colunas/b/dominioEmail"], erros.Select(erro => erro.Local));
    }

    [Fact]
    public void O_padrao_citado_e_nao_usado_entra_com_as_politicas_globais()
    {
        var texto = Colunas("      pix: { transformador: br.chave-pix }\n")
            .Replace("ambiente: homologacao\n", "ambiente: homologacao\npolitica:\n  entradaInvalida: anular\n  entradaMalformada: anular\n", StringComparison.Ordinal);

        var dominios = Resolver(texto).Dominios.ToDictionary(dominio => dominio.Nome, StringComparer.Ordinal);

        Assert.All(["cpf", "cnpj", "email", "telefone"], nome => Assert.Equal(
            (PoliticaEntradaInvalida.Anular, PoliticaEntradaMalformada.Anular),
            (dominios[nome].Regra.EntradaInvalida, dominios[nome].Regra.EntradaMalformada)));
        Assert.All(dominios.Values, dominio => Assert.Empty(dominio.Regra.Parametros));
    }

    [Fact]
    public void O_dominio_implicito_entra_na_tabela_com_a_definicao_padrao_e_nao_com_os_parametros_da_coluna()
    {
        var resolucao = Resolver(Colunas(
            "      a: { transformador: br.chave-pix, dominioCpf: cpf-loja }\n      c: { transformador: br.chave-dfe, modelos: [55] }\n",
            "dominios:\n  cpf-loja: { transformador: br.cpf@1 }\n"));

        Assert.Equal("cpf-loja", resolucao.Colunas[0].Regra.Parametros["dominioCpf"]);
        Assert.Equal("55", resolucao.Colunas[1].Regra.Parametros["modelos"]);
        Assert.Empty(Assert.Single(resolucao.Dominios, dominio => dominio.Nome == "chave-pix").Regra.Parametros);
        Assert.Empty(Assert.Single(resolucao.Dominios, dominio => dominio.Nome == "dfe-numero").Regra.Parametros);
    }

    [Fact]
    public void Preservar_global_na_coluna_que_cita_o_dominio_da_chave_Pix_pelo_nome_exige_justificativa()
    {
        var texto = Colunas("      pix: pix-loja\n", "dominios:\n  pix-loja: { transformador: br.chave-pix@1 }\n")
            .Replace("ambiente: homologacao\n", "ambiente: homologacao\npolitica:\n  entradaMalformada: preservar\n", StringComparison.Ordinal);

        Assert.Equal(("INC2022", "/tabelas/public.clientes/colunas/pix"), Codigo(Unico(texto)));
    }

    private static (string Codigo, string Local) Codigo(ErroConfiguracao erro) => (erro.Codigo, erro.Local);

    private static string Colunas(string colunas, string dominios = "") =>
        Yaml.Com(dominios + "tabelas:\n  public.clientes:\n    colunas:\n" + colunas);

    private static ResolucaoDeRegras Resolver(string texto) => ResolvedorDeRegras.Resolver(Yaml.Ler(texto), new RegistroTransformadores());

    private static ErroConfiguracao Unico(string texto) => Assert.Single(Erros(texto));

    private static IReadOnlyList<ErroConfiguracao> Erros(string texto)
    {
        try
        {
            Resolver(texto);
        }
        catch (ConfiguracaoInvalidaException excecao)
        {
            Assert.Equal("INC2000", excecao.Codigo);
            return excecao.Erros;
        }

        throw new XunitException("A resolução deveria ter sido recusada.");
    }
}
