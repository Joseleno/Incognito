using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>Leitura e validação do incognito.yaml: os critérios de aceite e as regras de cada campo.</summary>
public sealed class ConfiguracaoTestes
{
    [Fact]
    public void O_minimo_e_lido()
    {
        var configuracao = Yaml.Ler(Yaml.Minimo);

        Assert.Equal(ModoTratamento.Reproduzivel, configuracao.Modo);
        Assert.Equal("homologacao", configuracao.Ambiente);
        Assert.Equal(new ReferenciaSegredo(TipoReferenciaSegredo.Ambiente, "INCOGNITO_CHAVE", "INCOGNITO_CHAVE", new(10, 8)), configuracao.Chave);
        Assert.Equal(ProvedorBanco.PostgreSql, configuracao.Origem.Provedor);
        Assert.Equal("INCOGNITO_ORIGEM", configuracao.Origem.Conexao.Alvo);
        Assert.Equal("loja_homolog", configuracao.Destino.BancoEsperado);
        Assert.Null(configuracao.Origem.BancoEsperado);
        Assert.Null(configuracao.Politicas.Paralelismo);
        Assert.Empty(configuracao.Dominios);
        Assert.Empty(configuracao.Tabelas);
    }

    [Fact]
    public void O_exemplo_da_documentacao_e_lido_com_as_regras_na_ordem_do_arquivo()
    {
        var configuracao = Yaml.Ler(Yaml.Exemplo);

        Assert.Equal(["cpf", "cnpj", "email"], configuracao.Dominios.Select(dominio => dominio.Nome));
        Assert.Equal(new ReferenciaTransformador("br.cpf", 1, new(25, 27), "/dominios/cpf/transformador"), configuracao.Dominios[0].Transformador);
        Assert.Equal(4, configuracao.Politicas.Paralelismo);
        Assert.Equal(Consistencia.MelhorEsforco, configuracao.Politicas.Consistencia);
        Assert.Equal(PoliticaEntradaInvalida.Preservar, configuracao.Politicas.Entrada.EntradaInvalida);

        Assert.Equal(["clientes", "pedidos", "auditoria", "outbox_messages"], configuracao.Tabelas.Select(tabela => tabela.Nome.Tabela.Nome));
        Assert.All(configuracao.Tabelas, tabela => Assert.Equal(new Identificador("public", false), tabela.Nome.Esquema));
        Assert.Equal([AcaoTabela.Copiar, AcaoTabela.Copiar, AcaoTabela.Esvaziar, AcaoTabela.Esvaziar], configuracao.Tabelas.Select(tabela => tabela.Acao));

        var clientes = configuracao.Tabelas[0].Colunas.ToDictionary(coluna => coluna.Coluna.Nome);
        Assert.Equal(["cpf", "nome", "email", "telefone", "cep", "nascimento", "observacoes", "laudo", "criado_em"], configuracao.Tabelas[0].Colunas.Select(coluna => coluna.Coluna.Nome));
        Assert.Equal((TipoRegraColuna.Dominio, "cpf"), (clientes["cpf"].Tipo, clientes["cpf"].Dominio));
        Assert.Equal(new Identificador("cpf", false), clientes["nome"].DerivarDe);
        Assert.Equal(TipoRegraColuna.Transformador, clientes["telefone"].Tipo);
        Assert.Null(clientes["telefone"].Dominio);
        Assert.Equal([("precisao", "ano")], clientes["nascimento"].Parametros.Select(parametro => (parametro.Nome, parametro.Valor)));
        Assert.Equal((TipoRegraColuna.Manter, true, "campo exigido pelo teste de integração do parceiro"), (clientes["laudo"].Tipo, clientes["laudo"].Sensivel, clientes["laudo"].Justificativa));
        Assert.Equal(TipoRegraColuna.Manter, clientes["criado_em"].Tipo);
        Assert.All(configuracao.Tabelas[0].Colunas, coluna => Assert.Equal(OrigemRegra.Configuracao, coluna.Origem));
    }

    [Fact]
    public void Erro_de_configuracao_aponta_linha_e_coluna()
    {
        var erro = Yaml.UnicoErro(Yaml.Trocando("modo: reproduzivel", "modo: reprodutivel"));

        Assert.Equal(("INC2009", 9, 7, "/modo"), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
        Assert.Equal("incognito.yaml(9,7): INC2009: 'modo' aceita: reproduzivel, efemero.", erro.ToString());
    }

    [Fact]
    public void Erros_vem_todos_juntos_na_ordem_do_arquivo()
    {
        var texto = Yaml.Trocando("modo: reproduzivel", "modo: reprodutivel").Replace("ambiente: homologacao", "ambiente: Homologação", StringComparison.Ordinal);

        var excecao = Yaml.Excecao(texto);

        Assert.Equal("INC2000", excecao.Codigo);
        Assert.Equal([("INC2009", 9), ("INC2010", 11)], excecao.Erros.Select(erro => (erro.Codigo, erro.Linha!.Value)));
        Assert.StartsWith("Configuração inválida (2 erros). Primeiro: incognito.yaml(9,7): INC2009:", excecao.Message, StringComparison.Ordinal);
        Assert.Null(excecao.InnerException);
    }

    [Fact]
    public void No_maximo_100_erros_sao_guardados()
    {
        var dominios = string.Concat(Enumerable.Range(0, 150).Select(indice => $"  D{indice}: {{ transformador: br.cpf@1 }}\n"));

        var excecao = Yaml.Excecao(Yaml.Com("dominios:\n" + dominios));

        Assert.Equal(100, excecao.Erros.Count);
        Assert.Contains("mais de 100 erros", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manter_em_coluna_sensivel_sem_justificativa_e_recusado()
    {
        static string Coluna(string regra) => Yaml.Com($"tabelas:\n  public.pacientes:\n    colunas:\n      laudo: {regra}\n");

        var erro = Yaml.UnicoErro(Coluna("{ acao: manter, classe: sensivel }"));
        Assert.Equal(("INC2015", 15, 14, "/tabelas/public.pacientes/colunas/laudo"), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
        Assert.Equal("INC2015", Yaml.UnicoErro(Coluna("{ acao: manter, classe: sensivel, justificativa: \"  \" }")).Codigo);
        Assert.Equal("INC2015", Yaml.UnicoErro(Coluna("{ acao: manter, classe: sensivel, justificativa: ~ }")).Codigo);

        var regra = Assert.Single(Yaml.Ler(Coluna("{ acao: manter, classe: sensivel, justificativa: dado exigido pelo contrato }")).Tabelas[0].Colunas);
        Assert.Equal((TipoRegraColuna.Manter, true, "dado exigido pelo contrato"), (regra.Tipo, regra.Sensivel, regra.Justificativa));
        Assert.False(Assert.Single(Yaml.Ler(Coluna("{ acao: manter }")).Tabelas[0].Colunas).Sensivel);
    }

    [Theory]
    [InlineData("versao: 1", "/versao")]
    [InlineData("modo: reproduzivel", "/modo")]
    [InlineData("ambiente: homologacao", "/ambiente")]
    [InlineData("chave: ambiente:INCOGNITO_CHAVE", "/chave")]
    [InlineData("  bancoEsperado: loja_homolog", "/destino/bancoEsperado")]
    [InlineData("  conexao: ambiente:INCOGNITO_ORIGEM", "/origem/conexao")]
    public void Campo_obrigatorio_ausente_e_recusado(string linha, string local)
    {
        var erro = Yaml.UnicoErro(Yaml.Trocando(linha, null));

        Assert.Equal(("INC2007", local), (erro.Codigo, erro.Local));
    }

    [Theory]
    [InlineData("ambiente: ~")]
    [InlineData("ambiente:")]
    [InlineData("ambiente: null")]
    public void Valor_nulo_conta_como_ausente(string linha)
    {
        Assert.Equal("INC2007", Yaml.UnicoErro(Yaml.Trocando("ambiente: homologacao", linha)).Codigo);
    }

    [Fact]
    public void Origem_e_destino_ausentes_sao_recusados()
    {
        var texto = "versao: 1\nmodo: efemero\nambiente: homologacao\n";

        Assert.Equal(["/origem", "/destino"], Yaml.Erros(texto).Select(erro => erro.Local));
    }

    [Fact]
    public void Chave_no_modo_efemero_e_recusada()
    {
        var erro = Yaml.UnicoErro(Yaml.Trocando("modo: reproduzivel", "modo: efemero"));

        Assert.Equal(("INC2016", "/chave"), (erro.Codigo, erro.Local));
        Assert.Null(Yaml.Ler(Yaml.Trocando("modo: reproduzivel", "modo: efemero").Replace("chave: ambiente:INCOGNITO_CHAVE\n", "", StringComparison.Ordinal)).Chave);
    }

    [Fact]
    public void Origem_e_destino_de_tipos_diferentes_sao_recusados()
    {
        var texto = Yaml.Minimo.Replace("destino:\n  provedor: postgresql", "destino:\n  provedor: sqlserver", StringComparison.Ordinal);

        Assert.Equal("INC2016", Yaml.UnicoErro(texto).Codigo);
    }

    [Theory]
    [InlineData("  conexao: ambiente:INCOGNITO_DESTINO", "  conexao: ambiente:INCOGNITO_ORIGEM")]
    [InlineData("chave: ambiente:INCOGNITO_CHAVE", "chave: ambiente:INCOGNITO_ORIGEM")]
    public void Mesma_referencia_em_dois_segredos_e_recusada(string linha, string nova)
    {
        Assert.Equal("INC2016", Yaml.UnicoErro(Yaml.Trocando(linha, nova)).Codigo);
    }

    [Theory]
    [InlineData("ambiente: Homologacao", "INC2010")]
    [InlineData("ambiente: homologação", "INC2010")]
    [InlineData("ambiente: \"homologacao \"", "INC2010")]
    [InlineData("ambiente: homologacao..1", "INC2010")]
    [InlineData("ambiente: 2024", "INC2008")]
    [InlineData("ambiente: true", "INC2008")]
    [InlineData("ambiente: [a]", "INC2008")]
    public void Ambiente_fora_da_regra_dos_nomes_e_recusado(string linha, string codigo)
    {
        Assert.Equal(codigo, Yaml.UnicoErro(Yaml.Trocando("ambiente: homologacao", linha)).Codigo);
    }

    [Fact]
    public void Ambiente_com_65_caracteres_e_recusado()
    {
        Assert.Equal("INC2010", Yaml.UnicoErro(Yaml.Trocando("ambiente: homologacao", "ambiente: " + new string('a', 65))).Codigo);
        Assert.Equal(new string('a', 64), Yaml.Ler(Yaml.Trocando("ambiente: homologacao", "ambiente: " + new string('a', 64))).Ambiente);
    }

    [Theory]
    [InlineData("versao: 2", "INC2009")]
    [InlineData("versao: \"1\"", "INC2008")]
    [InlineData("versao: 1.0", "INC2008")]
    [InlineData("versao: um", "INC2008")]
    public void Versao_diferente_de_1_e_recusada(string linha, string codigo)
    {
        Assert.Equal(codigo, Yaml.UnicoErro(Yaml.Trocando("versao: 1", linha)).Codigo);
    }

    [Fact]
    public void Chave_desconhecida_sugere_a_certa_sem_repetir_a_escrita()
    {
        var erro = Yaml.UnicoErro(Yaml.Com("politica:\n  entradaMalformda: falhar\n"));

        Assert.Equal(("INC2006", 13, 3, "/politica"), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
        Assert.Contains("'entradaMalformada'", erro.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("entradaMalformda", erro.ToString(), StringComparison.Ordinal);
        Assert.Equal("INC2006", Yaml.UnicoErro(Yaml.Com("tamanhoDoLote: 10\n")).Codigo);
    }

    [Theory]
    [InlineData("suspeitaSemRegra: copiar")]
    [InlineData("semClassificacao: falhar")]
    [InlineData("entradaInvalida: ignorar")]
    [InlineData("entradaMalformada: corrigir")]
    [InlineData("consistencia: total")]
    [InlineData("paralelismo: 0")]
    [InlineData("paralelismo: 65")]
    public void Politica_fora_dos_valores_aceitos_e_recusada(string linha)
    {
        Assert.Equal("INC2009", Yaml.UnicoErro(Yaml.Com($"politica:\n  {linha}\n")).Codigo);
    }

    [Fact]
    public void Paralelismo_entre_aspas_e_recusado()
    {
        Assert.Equal("INC2008", Yaml.UnicoErro(Yaml.Com("politica:\n  paralelismo: \"4\"\n")).Codigo);
        Assert.Equal(64, Yaml.Ler(Yaml.Com("politica:\n  paralelismo: 64\n")).Politicas.Paralelismo);
    }

    [Theory]
    [InlineData("  CPF: { transformador: br.cpf@1 }", "INC2010")]
    [InlineData("  manter: { transformador: br.cpf@1 }", "INC2010")]
    [InlineData("  cpf: { entradaMalformada: anular }", "INC2007")]
    [InlineData("  cpf: { transformador: br.cpf@0 }", "INC2018")]
    [InlineData("  cpf: { transformador: BR.CPF@1 }", "INC2018")]
    [InlineData("  cpf: { transformador: br.cpf@ }", "INC2018")]
    [InlineData("  cpf: { transformador: br.cpf@1@2 }", "INC2018")]
    [InlineData("  cpf: { transformador: br.cpf@1, derivarDe: nome }", "INC2016")]
    [InlineData("  cpf: { transformador: br.cpf@1, Ordem: x }", "INC2006")]
    [InlineData("  cpf: { transformador: br.cpf@1, ordem: { a: 1 } }", "INC2008")]
    [InlineData("  cpf: { transformador: br.cpf@1, ordem: ~ }", "INC2008")]
    [InlineData("  cpf: br.cpf@1", "INC2008")]
    public void Dominio_fora_da_forma_e_recusado(string linha, string codigo)
    {
        Assert.Equal(codigo, Yaml.UnicoErro(Yaml.Com("dominios:\n" + linha + "\n")).Codigo);
    }

    [Fact]
    public void Dominio_guarda_parametros_politicas_e_versao_ausente()
    {
        var dominio = Assert.Single(Yaml.Ler(Yaml.Com("dominios:\n  chave-dfe: { transformador: br.chave-dfe, modelos: [55, 65], permutarOrdem: true, entradaMalformada: anular }\n")).Dominios);

        Assert.Equal(("br.chave-dfe", (int?)null), (dominio.Transformador.Nome, dominio.Transformador.Versao));
        Assert.Equal([("modelos", "55,65"), ("permutarOrdem", "true")], dominio.Parametros.Select(parametro => (parametro.Nome, parametro.Valor)));
        Assert.Equal(new PoliticasDeEntrada(null, PoliticaEntradaMalformada.Anular), dominio.Politicas);
    }

    [Theory]
    [InlineData("cpf: rg", "INC2013")]
    [InlineData("cpf: Cpf", "INC2010")]
    [InlineData("cpf:", "INC2007")]
    [InlineData("cpf: 123", "INC2008")]
    [InlineData("cpf: [manter]", "INC2008")]
    [InlineData("cpf: { acao: apagar }", "INC2009")]
    [InlineData("cpf: { acao: manter, transformador: br.cpf@1 }", "INC2016")]
    [InlineData("cpf: { acao: manter, derivarDe: nome }", "INC2016")]
    [InlineData("cpf: { transformador: br.cpf@1, dominio: outro }", "INC2016")]
    [InlineData("cpf: { dominio: cpf, permutarOrdem: true }", "INC2016")]
    [InlineData("cpf: { dominio: rg }", "INC2013")]
    [InlineData("cpf: { classe: sensivel }", "INC2007")]
    [InlineData("cpf: { dominio: cpf, classe: publica }", "INC2009")]
    [InlineData("cpf: { dominio: cpf, entradaInvalida: corrigir }", "INC2009")]
    [InlineData("\"a.b\": manter", "INC2017")]
    [InlineData("a.b: manter", "INC2017")]
    [InlineData("1coluna: manter", "INC2017")]
    public void Regra_de_coluna_fora_da_forma_e_recusada(string linha, string codigo)
    {
        var texto = Yaml.Com("dominios:\n  cpf: { transformador: br.cpf@1 }\ntabelas:\n  public.clientes:\n    colunas:\n      " + linha + "\n");

        Assert.Equal(codigo, Yaml.UnicoErro(texto).Codigo);
    }

    [Fact]
    public void Justificativa_tem_no_maximo_500_caracteres()
    {
        static string Coluna(int tamanho) => Yaml.Com($"tabelas:\n  public.pacientes:\n    colunas:\n      laudo: {{ acao: manter, classe: sensivel, justificativa: {new string('x', tamanho)} }}\n");

        Assert.Equal("INC2009", Yaml.UnicoErro(Coluna(501)).Codigo);
        Assert.Equal(500, Assert.Single(Yaml.Ler(Coluna(500)).Tabelas[0].Colunas).Justificativa!.Length);
    }

    [Fact]
    public void Coluna_guarda_politicas_proprias_e_referencia_ao_dominio()
    {
        var texto = Yaml.Com("dominios:\n  cpf: { transformador: br.cpf@1, entradaInvalida: falhar }\ntabelas:\n  public.clientes:\n    colunas:\n      cpf: { dominio: cpf, entradaMalformada: anular, derivarDe: id }\n");

        var configuracao = Yaml.Ler(texto);
        var coluna = Assert.Single(configuracao.Tabelas[0].Colunas);

        Assert.Equal((TipoRegraColuna.Dominio, "cpf"), (coluna.Tipo, coluna.Dominio));
        Assert.Equal(new PoliticasDeEntrada(null, PoliticaEntradaMalformada.Anular), coluna.Politicas);
        Assert.Equal(new PoliticasDeEntrada(PoliticaEntradaInvalida.Falhar, null), configuracao.Dominios[0].Politicas);
    }

    [Fact]
    public void Id_permutar_declara_o_dominio_na_coluna_de_PK_e_a_FK_o_usa_pelo_nome()
    {
        var texto = Yaml.Com("tabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n  public.itens:\n    colunas:\n      pedido_id: pedido_id\n");

        var configuracao = Yaml.Ler(texto);

        Assert.Equal((TipoRegraColuna.Transformador, "pedido_id"), (configuracao.Tabelas[0].Colunas[0].Tipo, configuracao.Tabelas[0].Colunas[0].Dominio));
        Assert.Equal((TipoRegraColuna.Dominio, "pedido_id"), (configuracao.Tabelas[1].Colunas[0].Tipo, configuracao.Tabelas[1].Colunas[0].Dominio));
    }

    [Theory]
    [InlineData("dominios:\n  pedido_id: { transformador: br.cpf@1 }\ntabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n")]
    [InlineData("tabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n  public.vendas:\n    colunas:\n      id: { transformador: id.permutar@1, dominio: pedido_id }\n")]
    public void Dominio_declarado_duas_vezes_e_recusado(string trecho)
    {
        Assert.Equal("INC2019", Yaml.UnicoErro(Yaml.Com(trecho)).Codigo);
    }

    [Fact]
    public void DerivarDe_em_ciclo_e_recusado_e_coluna_sem_regra_e_aceita()
    {
        static string Tabela(string colunas) => Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n" + colunas);

        var ciclo = Yaml.Erros(Tabela("      a: { transformador: br.cep@1, derivarDe: b }\n      b: { transformador: br.cep@1, derivarDe: a }\n      c: { transformador: br.cep@1, derivarDe: a }\n"));
        Assert.Equal([("INC2014", "/tabelas/public.clientes/colunas/a"), ("INC2014", "/tabelas/public.clientes/colunas/b")], ciclo.Select(erro => (erro.Codigo, erro.Local)));
        Assert.Equal("INC2014", Yaml.UnicoErro(Tabela("      a: { transformador: br.cep@1, derivarDe: a }\n")).Codigo);
        Assert.Equal(new Identificador("cpf", false), Assert.Single(Yaml.Ler(Tabela("      cep: { transformador: br.cep@1, derivarDe: cpf }\n")).Tabelas[0].Colunas).DerivarDe);
    }

    [Theory]
    [InlineData("clientes")]
    [InlineData("loja.public.clientes")]
    [InlineData("public.")]
    [InlineData(".clientes")]
    [InlineData("'\"public\".\"\"'")]
    [InlineData("'\"public\".clientes\"'")]
    [InlineData("public.1clientes")]
    [InlineData("'\"public\" .clientes'")]
    public void Tabela_fora_da_forma_esquema_tabela_e_recusada(string nome)
    {
        var erro = Yaml.UnicoErro(Yaml.Com($"tabelas:\n  {nome}: {{ acao: esvaziar }}\n"));

        Assert.Equal(("INC2017", "/tabelas"), (erro.Codigo, erro.Local));
    }

    [Fact]
    public void Tabela_entre_aspas_guarda_o_nome_como_escrito()
    {
        var tabelas = Yaml.Ler(Yaml.Com("tabelas:\n  '\"Vendas\".\"Cli\"\"entes\"': { acao: esvaziar }\n  dbo.\"Ped.idos\": { acao: ignorar }\n  public.clientes_ção$1: { acao: esvaziar }\n")).Tabelas;

        Assert.Equal(
            [
                new NomeTabelaEscrito(new Identificador("Vendas", true), new Identificador("Cli\"entes", true)),
                new NomeTabelaEscrito(new Identificador("dbo", false), new Identificador("Ped.idos", true)),
                new NomeTabelaEscrito(new Identificador("public", false), new Identificador("clientes_ção$1", false)),
            ],
            tabelas.Select(tabela => tabela.Nome));
    }

    [Fact]
    public void Nome_com_marca_combinante_ou_caractere_invisivel_e_recusado()
    {
        var combinante = "\"cafe" + (char)0x0301 + "\"";
        var espacoEstranho = "\"ca" + (char)0x00A0 + "fe\"";
        var foraDeNfc = (char)0x212B + "ngulo";
        foreach (var nome in new[] { combinante, espacoEstranho, foraDeNfc })
        {
            Assert.Equal("INC2017", Yaml.UnicoErro(Yaml.Com($"tabelas:\n  public.clientes:\n    colunas:\n      '{nome}': manter\n")).Codigo);
        }

        // Invisíveis (largura zero, preenchedor Hangul) já são recusados no texto, antes do nome.
        foreach (var invisivel in new[] { "\"ca" + (char)0x200D + "fe\"", "cpf" + (char)0x3164 })
        {
            Assert.Equal("INC2002", Yaml.UnicoErro(Yaml.Com($"tabelas:\n  public.clientes:\n    colunas:\n      '{invisivel}': manter\n")).Codigo);
        }
        Assert.Equal((char)0xC5 + "ngulo", Assert.Single(Yaml.Ler(Yaml.Com($"tabelas:\n  public.clientes:\n    colunas:\n      {(char)0xC5}ngulo: manter\n")).Tabelas[0].Colunas).Coluna.Nome);

        Assert.Equal("nome cliente", Assert.Single(Yaml.Ler(Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      '\"nome cliente\"': manter\n")).Tabelas[0].Colunas).Coluna.Nome);
    }

    [Theory]
    [InlineData("{ acao: esvaziar, colunas: { cpf: manter } }", "INC2016")]
    [InlineData("{ acao: apagar }", "INC2009")]
    [InlineData("{ acao: copiar, filtro: x }", "INC2006")]
    [InlineData("esvaziar", "INC2008")]
    public void Acao_de_tabela_fora_da_forma_e_recusada(string regra, string codigo)
    {
        Assert.Equal(codigo, Yaml.UnicoErro(Yaml.Com($"tabelas:\n  public.clientes: {regra}\n")).Codigo);
    }

    [Fact]
    public void Codigos_sao_unicos_e_estao_na_faixa_da_configuracao()
    {
        var constantes = typeof(CodigosConfiguracao)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(campo => campo.IsLiteral)
            .Select(campo => (string)campo.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(constantes.Length, constantes.Distinct().Count());
        Assert.Equal(constantes.Order(), CodigosConfiguracao.Todos.Order());
        Assert.All(constantes, codigo => Assert.Matches(@"\AINC2[0-9]{3}\z", codigo));
    }

    [Theory]
    [InlineData("  bancoEsperado: \"\"")]
    [InlineData("  bancoEsperado: \"   \"")]
    public void Banco_esperado_em_branco_e_recusado(string linha)
    {
        Assert.Equal("INC2007", Yaml.UnicoErro(Yaml.Trocando("  bancoEsperado: loja_homolog", linha)).Codigo);
    }

    [Theory]
    [InlineData("  conexao: ambiente:INCOGNITO_DESTINO", "  conexao: ambiente:incognito_origem")]
    [InlineData("chave: ambiente:INCOGNITO_CHAVE", "chave: ambiente:Incognito_Destino")]
    public void Mesma_referencia_com_outra_caixa_e_recusada(string linha, string nova)
    {
        Assert.Equal("INC2016", Yaml.UnicoErro(Yaml.Trocando(linha, nova)).Codigo);
    }

    [Fact]
    public void Campo_opcional_nulo_vale_ausente()
    {
        var configuracao = Yaml.Ler(Yaml.Com("politica:\n  paralelismo: ~\n  consistencia: ~\n  entradaInvalida: null\n"));

        Assert.Equal((null, null, null), (configuracao.Politicas.Paralelismo, configuracao.Politicas.Consistencia, configuracao.Politicas.Entrada.EntradaInvalida));
    }

    [Fact]
    public void Item_de_lista_de_parametro_com_virgula_e_recusado()
    {
        Assert.Equal("INC2008", Yaml.UnicoErro(Yaml.Com("dominios:\n  chave-dfe: { transformador: br.chave-dfe@1, modelos: [55, \"57,65\"] }\n")).Codigo);
    }

    [Theory]
    [InlineData("dominios:\n  cpf: br.cpf@1\ntabelas:\n  public.clientes:\n    colunas:\n      cpf: cpf\n", "INC2008")]
    [InlineData("tabelas:\n  public.pedidos:\n    colunas:\n      id: { transformador: id.permutar@0, dominio: pedido_id }\n  public.itens:\n    colunas:\n      pedido_id: pedido_id\n", "INC2018")]
    public void Definicao_errada_de_dominio_nao_gera_erro_nas_colunas_que_o_citam(string trecho, string codigo)
    {
        Assert.Equal(codigo, Yaml.UnicoErro(Yaml.Com(trecho)).Codigo);
    }

    [Fact]
    public void Tabela_ou_coluna_com_nome_invalido_nao_tem_os_filhos_validados()
    {
        Assert.Equal("INC2017", Yaml.UnicoErro(Yaml.Com("tabelas:\n  clientes: { acao: apagar }\n")).Codigo);
        Assert.Equal("INC2017", Yaml.UnicoErro(Yaml.Com("tabelas:\n  public.clientes:\n    colunas:\n      \"a.b\": rg\n")).Codigo);
    }

    [Fact]
    public void Cadeia_longa_de_derivarDe_e_conferida_em_tempo_linear()
    {
        var colunas = string.Concat(Enumerable.Range(0, 20_000).Select(indice => $"      c{indice}: {{ transformador: br.cep@1, derivarDe: c{indice + 1} }}\n"));
        var texto = Yaml.Com("tabelas:\n  public.t:\n    colunas:\n" + colunas + "      c20000: { transformador: br.cep@1, derivarDe: c19990 }\n");
        var cronometro = System.Diagnostics.Stopwatch.StartNew();

        var erros = Yaml.Erros(texto);

        Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
        Assert.Equal(11, erros.Count);
        Assert.All(erros, erro => Assert.Equal("INC2014", erro.Codigo));
    }
}
