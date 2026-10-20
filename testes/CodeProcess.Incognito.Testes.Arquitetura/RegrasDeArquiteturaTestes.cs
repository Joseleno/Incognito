using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Regras de dependência entre os pacotes, verificadas sobre as referências
/// que o compilador de fato gravou em cada assembly, e regras de nomes dos tipos.
/// </summary>
public sealed class RegrasDeArquiteturaTestes
{
    private const string Nucleo = "CodeProcess.Incognito";
    private const string Motor = "CodeProcess.Incognito.Motor";
    private const string PostgreSql = "CodeProcess.Incognito.PostgreSql";
    private const string SqlServer = "CodeProcess.Incognito.SqlServer";
    private const string EntityFrameworkCore = "CodeProcess.Incognito.EntityFrameworkCore";
    private const string Cli = "CodeProcess.Incognito.Cli";
    private const string Dialetos = Nucleo + ".Dialetos";

    private static readonly string[] _driversEEfCore = ["Npgsql", "Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore"];

    // O HTTP do runtime e os invólucros dele (WebRequest, WebClient, o JSON sobre o HttpClient, o cliente de WebSocket),
    // pelo assembly e pelos tipos: uma biblioteca netstandard os alcança pelo netstandard, por encaminhamento de tipo.
    private static readonly string[] _assembliesDeHttp = ["System.Net.Http", "System.Net.Requests", "System.Net.WebClient", "System.Net.Http.Json", "System.Net.WebSockets.Client"];
    private static readonly string[] _tiposDeHttp = ["System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.WebClient", "System.Net.WebSockets.ClientWebSocket"];

    // Cpf, Cnpj e Email colidem com tipos que já existem em muitos projetos brasileiros; Dominio, com o namespace de
    // projetos DDD.
    private static readonly string[] _nomesDeTipoProibidos = ["Cpf", "Cnpj", "Email", "Dominio"];

    // O que o dialeto PostgreSQL nunca lê, casado pelo nome em qualquer forma (sem parêntese, entre aspas, concatenado) e
    // sem diferenciar maiúsculas: as funções que devolvem definição, as árvores de nó e os textos de objeto, locale e regras
    // de colação, comentários, rótulos e estatísticas, as visões que mostram definição, opções e segredos, sessões, arquivos
    // e páginas, o conteúdo dos large objects e das sequences, as funções e os casts que transformam uma linha em texto, a
    // leitura de todas as colunas e o current_setting fora dos parâmetros aprovados (um parâmetro do usuário pode guardar
    // segredo). Uma lista negra: pega o descuido, não quem quer burlá-la.
    private static readonly Regex[] _textoOpaco =
    [
        .. new[]
        {
            @"\bpg_get_",
            @"\b(adbin|conbin|indexprs|indpred|partexprs|polqual|polwithcheck|relpartbound|typdefaultbin|typdefault|attmissingval|stxexprs|prosrc|prosqlbody|probin|proargdefaults|ev_qual|ev_action|tgargs|tgqual)\b",
            @"\b(colllocale|colliculocale|collicurules|collcollate|collctype|daticurules|datlocale|daticulocale|datcollate|datctype)\b",
            @"(obj_description|col_description|pg_description|pg_shdescription|pg_seclabels?|pg_shseclabel|pg_statistic|pg_statistic_ext_data|pg_stats|pg_stats_ext|pg_stats_ext_exprs|\bstxd\w*)\b",
            @"\b(pg_policies|pg_rules|pg_views|pg_matviews|pg_indexes|pg_publication_tables|prqual|information_schema)\b",
            @"\b(ftoptions|umoptions|srvoptions|fdwoptions|attfdwoptions|pg_user_mappings|pg_authid|rolpassword|pg_shadow|passwd|rolconfig|setconfig|pg_db_role_setting|proconfig|subconninfo)\b",
            @"\b(pg_stat_activity|pg_stat_get_\w+|pg_stat_statements|pg_prepared_statements|pg_cursors|client_dn|issuer_dn|client_serial|pg_stat_gssapi|pg_settings|pg_show_all_settings|pg_file_settings|pg_read_file|pg_read_binary_file|pg_logical_slot_\w+|pg_sequence_last_value)\b",
            @"\b(heap_page_items|get_raw_page|tuple_data_split|page_header|bt_page_items)\b",
            @"\b(lo_get|loread|lo_export)\b",
            @"\b(relacl|attacl|nspacl|datacl|lomacl|proacl|fdwacl|srvacl|typacl|defaclacl|aclexplode|acldefault|makeaclitem)\b",
            @"\bpg_largeobject\b",
            @"\b(to_jsonb?|\w*_to_json\w*|jsonb?_agg|jsonb?_build_\w+|jsonb?_object\w*|\w*_to_xml\w*|format)\b",
            @"\brow\s*\(",
            @"::\s*""?\s*(pg_catalog\s*""?\s*\.\s*""?)?(text|varchar|bpchar|name|character|json|jsonb)\b",
            @"\bCAST\s*\(",
            @"\bSELECT\s+(DISTINCT\s+|ALL\s+)?\*",
            @",\s*\*",
            @"\b\w+\s*\.\s*\*",
            @"(^|[(;])\s*TABLE\s+\w",
            @"\bSHOW\s+\w",
            @"\b(current_setting|set_config)\b",
            @"\bDO\s+\$",
            @"\bEXECUTE\s+\w",
        }.Select(padrao => new Regex(padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(1))),
    ];

    // As formas permitidas, tiradas antes da busca: os números das colunas citadas por uma expressão, extraídos no servidor;
    // a presença de um texto (nunca o texto); o nome do esquema do sistema no filtro; o objeto dos large objects; e o
    // current_setting dos parâmetros do servidor que o dialeto precisa.
    private static readonly Regex[] _formasPermitidas =
    [
        .. new[]
        {
            @"\bpg_catalog\.regexp_matches\(\s*\w+\.(indexprs|indpred|partexprs)::text,\s*':varattno \(-\?\[0-9\]\+\)',\s*'g'\)",
            @"\b\w+\.(indexprs|indpred|partexprs|tgqual|collicurules|daticurules)\s+IS\s+NOT\s+NULL\b",
            @"'information_schema'",
            @"new\(""pg_catalog"", ""pg_largeobject""\)",
            @"\bpg_catalog\.current_setting\('(server_version_num|search_path|client_encoding|default_transaction_read_only|standard_conforming_strings|lo_compat_privileges|max_identifier_length)'\)",
            @"\bpg_catalog\.current_setting\('lc_monetary'\)\s*=\s*'C'",
        }.Select(padrao => new Regex(padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
    ];

    // Toda propriedade do contrato de dialeto que é ou contém texto, com o motivo de ser só nome de catálogo ou de um
    // conjunto fechado.
    private static readonly Dictionary<string, string> _textoAprovado = new(StringComparer.Ordinal)
    {
        ["NomeObjeto.Esquema"] = "nome de catálogo",
        ["NomeObjeto.Nome"] = "nome de catálogo",
        ["ReferenciaColuna.Tabela"] = "nome de catálogo",
        ["ReferenciaColuna.Coluna"] = "nome de coluna do catálogo",
        ["TabelaEsquema.Nome"] = "nome de catálogo",
        ["TabelaEsquema.Pai"] = "nome de catálogo",
        ["TabelaEsquema.OutrosPais"] = "nomes de catálogo",
        ["TabelaEsquema.Historico"] = "nome de catálogo",
        ["TabelaEsquema.Extensao"] = "nome de extensão do catálogo",
        ["TabelaEsquema.ChavePrimaria"] = "nomes de coluna do catálogo",
        ["ChaveParticao.Colunas"] = "nomes de coluna do catálogo",
        ["ColunaEsquema.Nome"] = "nome de coluna do catálogo",
        ["ColunaEsquema.TipoNativo"] = "nome de tipo do catálogo, sem modificador",
        ["ColunaEsquema.TipoBase"] = "nome de tipo do catálogo, sem modificador",
        ["ChaveEstrangeira.Nome"] = "nome de restrição do catálogo",
        ["ChaveEstrangeira.Colunas"] = "nomes de coluna do catálogo",
        ["ChaveEstrangeira.Referenciada"] = "nome de catálogo",
        ["ChaveEstrangeira.ColunasReferenciadas"] = "nomes de coluna do catálogo",
        ["SequenceEsquema.Nome"] = "nome de catálogo",
        ["SequenceEsquema.Colunas"] = "nomes de coluna do catálogo",
        ["ObjetoResidual.Nome"] = "nome de catálogo",
        ["ObjetoResidual.DependeDe"] = "nomes de catálogo",
        ["IndiceUnico.Nome"] = "nome de índice do catálogo",
        ["ElementoIndice.Coluna"] = "nome de coluna do catálogo",
        ["RestricaoCheck.Nome"] = "nome de restrição do catálogo",
        ["RestricaoExclusao.Nome"] = "nome de restrição do catálogo",
        ["TriggerEsquema.Nome"] = "nome de trigger do catálogo",
        ["ReferenciasDaExpressao.Colunas"] = "nomes de coluna do catálogo",
        ["ColacaoEsquema.Nome"] = "nome de colação do catálogo, nunca o locale",
        ["ColacaoEsquema.Esquema"] = "nome de esquema do catálogo",
        ["CodificacaoTexto.Nome"] = "nome de codificação, de um conjunto fechado do banco",
        ["PermissoesTabela.Tabela"] = "nome de catálogo",
        ["PermissoesTabela.ColunasSemLeitura"] = "nomes de coluna do catálogo",
        ["PermissoesResidual.Nome"] = "nome de catálogo",
        ["PermissoesSequence.Nome"] = "nome de catálogo",
        ["IdentidadeLida.Banco"] = "nome do banco do catálogo",
        ["IdentidadeLida.Servidor"] = "endereço e porta do servidor",
        ["IdentidadeLida.Linhagem"] = "exceção nomeada: identificadores numéricos do cluster e do banco",
        ["ProvedorDialeto.Nome"] = "nome fixo do dialeto",
    };

    // As propriedades dos tipos do modelo antes da semântica do esquema: as novas nesses tipos são anuláveis ou
    // listas.
    private static readonly Dictionary<string, string[]> _propriedadesCongeladas = new(StringComparer.Ordinal)
    {
        ["EsquemaBanco"] = ["Tabelas", "Sequences", "ObjetosResiduais"],
        ["TabelaEsquema"] = ["Nome", "Tipo", "Pai", "OutrosPais", "Historico", "ChaveParticao", "Estrangeira", "Extensao", "Colunas", "ChavePrimaria", "ChavesEstrangeiras", "SegurancaDeLinha"],
        ["ChaveParticao"] = ["Colunas", "ColunasIncompletas"],
        ["ColunaEsquema"] = ["Nome", "TipoNativo", "TipoBase", "Situacao", "Tipo", "Anulavel", "Geracao", "Identidade", "Mascarada"],
        ["ChaveEstrangeira"] = ["Nome", "Colunas", "Referenciada", "ColunasReferenciadas"],
        ["SequenceEsquema"] = ["Nome", "Colunas"],
        ["ObjetoResidual"] = ["Nome", "Tipo"],
        ["IdentidadeLida"] = ["Banco", "Servidor", "Linhagem"],
        ["NomeObjeto"] = ["Esquema", "Nome"],
        ["ReferenciaColuna"] = ["Tabela", "Coluna"],
    };

    [Fact]
    public void Nucleo_nao_referencia_pacotes_de_terceiros()
    {
        var foraDoRuntime = Referencias(Nucleo)
            .Where(referencia => !ParteDoRuntime(referencia))
            .Select(referencia => referencia.Name)
            .ToArray();

        Assert.True(foraDoRuntime.Length == 0, $"O núcleo referencia assemblies fora do runtime do .NET: {string.Join(", ", foraDoRuntime)}.");
    }

    [Fact]
    public void Motor_nao_referencia_driver_de_banco_nem_EF_Core()
    {
        var proibidas = Referencias(Motor)
            .Select(referencia => referencia.Name!)
            .Where(nome => _driversEEfCore.Any(prefixo => nome == prefixo || nome.StartsWith(prefixo + ".", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(proibidas.Length == 0, $"O motor referencia driver de banco ou EF Core: {string.Join(", ", proibidas)}.");
    }

    [Theory]
    [InlineData(PostgreSql, SqlServer)]
    [InlineData(SqlServer, PostgreSql)]
    public void Dialetos_nao_se_referenciam(string dialeto, string outroDialeto)
    {
        var nomes = ReferenciasTransitivasDoProjeto(dialeto).Select(referencia => referencia.Name);

        Assert.DoesNotContain(outroDialeto, nomes);
    }

    [Theory]
    [InlineData(Nucleo)]
    [InlineData(Motor)]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    public void Nucleo_motor_e_dialetos_nao_usam_System_Net_Http(string assembly)
    {
        var nomes = ReferenciasTransitivasDoProjeto(assembly).Select(referencia => referencia.Name).Intersect(_assembliesDeHttp);

        Assert.Empty(nomes);
    }

    [Theory]
    [InlineData(PostgreSql, "Npgsql")]
    [InlineData(SqlServer, null)]
    public void Nenhum_assembly_de_terceiros_que_vai_com_o_dialeto_usa_System_Net_Http(string dialeto, string? driver)
    {
        // A regra acima não percorre pacotes de terceiros, que vão junto em todo uso do dialeto: aqui o fecho inteiro das
        // referências, a partir do dialeto, por todo assembly fora do runtime (o driver e as dependências dele).
        var visitados = new HashSet<string>(StringComparer.Ordinal) { dialeto };
        var pendentes = new Queue<string>([dialeto]);
        var usam = new List<string>();
        while (pendentes.TryDequeue(out var atual))
        {
            foreach (var referencia in Referencias(atual))
            {
                if (_assembliesDeHttp.Contains(referencia.Name))
                {
                    usam.Add($"{atual} ({referencia.Name})");
                }
                else if (!ParteDoRuntime(referencia) && visitados.Add(referencia.Name!))
                {
                    pendentes.Enqueue(referencia.Name!);
                }
            }

            usam.AddRange(TiposDeHttpReferenciados(Assembly.Load(new AssemblyName(atual))).Select(tipo => $"{atual} ({tipo})"));
        }

        Assert.True(driver is null || visitados.Contains(driver), $"O fecho de {dialeto} não chegou ao driver {driver}.");
        Assert.True(usam.Count == 0, $"Assemblies que vão com {dialeto} e usam System.Net.Http: {string.Join(", ", usam)}.");
    }

    [Fact]
    public void A_leitura_dos_tipos_referenciados_acha_o_HttpClient()
    {
        // O controle da regra acima: este assembly referencia o HttpClient só aqui, e a leitura dos metadados o acha.
        Assert.Equal("HttpClient", typeof(System.Net.Http.HttpClient).Name);

        Assert.Contains("System.Net.Http.HttpClient", TiposDeHttpReferenciados(typeof(RegrasDeArquiteturaTestes).Assembly));
    }

    [Theory]
    [InlineData(Nucleo)]
    [InlineData(Motor)]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    [InlineData(EntityFrameworkCore)]
    [InlineData(Cli)]
    public void Nenhum_tipo_nem_namespace_se_chama_Cpf_Cnpj_Email_ou_Dominio(string assembly)
    {
        // Sem diferenciar maiúsculas: CPF, EMail ou DOMINIO colidiriam do mesmo jeito.
        var proibidos = Tipos(assembly)
            .Where(tipo => _nomesDeTipoProibidos.Contains(NomeSemAridade(tipo.Name), StringComparer.OrdinalIgnoreCase)
                || (tipo.Namespace ?? "").Split('.').Intersect(_nomesDeTipoProibidos, StringComparer.OrdinalIgnoreCase).Any())
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(proibidos.Length == 0, $"Tipos com nome ou namespace proibido: {string.Join(", ", proibidos)}.");
    }

    [Fact]
    public void Assinaturas_do_gerador_nao_citam_a_chave_nem_a_pseudonimizacao()
    {
        // O GeradorBr não é criptográfico e não pode ser confundido com a pseudonimização: nenhum campo, parâmetro,
        // retorno ou tipo aninhado dele e do namespace Geracao vem da criptografia, da pseudonimização ou da chave.
        var gerador = Tipos(Nucleo).Where(tipo => tipo.FullName == Nucleo + ".GeradorBr" || tipo.Namespace == Nucleo + ".Geracao").ToArray();
        string[] proibidos = [Nucleo + ".Criptografia", Nucleo + ".Pseudonimizacao"];

        var usados = gerador
            .SelectMany(TiposDaAssinatura)
            .Where(tipo => proibidos.Contains(tipo.Namespace) || tipo.FullName == Nucleo + ".ChaveIncognito")
            .Select(tipo => tipo.FullName)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(gerador);
        Assert.True(usados.Length == 0, $"O gerador usa tipos da chave ou da pseudonimização: {string.Join(", ", usados)}.");
    }

    [Fact]
    public void Os_contratos_de_extensao_sao_classes_abstratas_sem_construtor_publico_e_o_Criar_nao_e_virtual()
    {
        // ADR 0017: classe abstrata pode ganhar membro virtual sem quebrar quem estende; o Criar confere a regra antes e
        // o transformador depois, e quem estende não consegue pular a conferência.
        var contratos = Tipos(Nucleo).Where(tipo => tipo.FullName is Nucleo + ".Transformadores.Transformador" or Nucleo + ".Transformadores.FabricaTransformador").ToArray();
        var criar = contratos.Single(tipo => tipo.Name == "FabricaTransformador").GetMethod("Criar")!;

        Assert.Equal(2, contratos.Length);
        Assert.All(contratos, tipo => Assert.True(tipo.IsAbstract && !tipo.IsInterface && tipo.GetConstructors().Length == 0, $"{tipo.FullName} precisa ser classe abstrata sem construtor público."));
        Assert.False(criar.IsVirtual, "O Criar da fábrica é o método-modelo: não pode ser virtual.");
    }

    [Fact]
    public void Tipos_publicos_do_motor_so_nascem_do_motor()
    {
        // ADR 0017: o modelo produzido pelo motor (configuração, erros, plano) não tem construtor público. O contrato de
        // dialeto fica de fora: o esquema lido é saída de um dialeto, que pode ser de fora, e tem construtor público.
        var comConstrutor = Tipos(Motor)
            .Where(tipo => tipo.IsVisible && !NoContrato(tipo) && tipo.GetConstructors().Length > 0)
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(comConstrutor.Length == 0, $"Tipos públicos do motor com construtor público: {string.Join(", ", comConstrutor)}.");
    }

    [Fact]
    public void Os_tipos_publicos_do_contrato_sao_selados_abstratos_structs_ou_enums()
    {
        // ADR 0017: as entradas e saídas do contrato têm construtor público, mas ninguém herda delas; os contratos de
        // extensão são abstratos.
        var abertos = Tipos(Nucleo)
            .Where(tipo => tipo.IsVisible && tipo.Namespace == Nucleo + ".Transformadores")
            .Where(tipo => tipo.IsClass && !tipo.IsSealed && !tipo.IsAbstract)
            .Select(tipo => tipo.FullName)
            .ToArray();

        Assert.True(abertos.Length == 0, $"Tipos do contrato que se podem herdar sem serem contratos: {string.Join(", ", abertos)}.");
    }

    [Fact]
    public void O_contrato_de_dialeto_e_experimental_e_so_tem_classes_seladas_ou_abstratas()
    {
        // O contrato muda até a 1.0 (INCX001); contratos de extensão como classes abstratas (ADR 0017), sem interface.
        var tipos = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToArray();

        Assert.NotEmpty(tipos);
        Assert.All(tipos, tipo =>
        {
            Assert.True(tipo.GetCustomAttribute<ExperimentalAttribute>()?.DiagnosticId == "INCX001", $"{tipo.Name} sem [Experimental(\"INCX001\")].");
            Assert.False(tipo.IsInterface, $"{tipo.Name} é interface: o contrato de extensão é classe abstrata.");
            Assert.True(!tipo.IsClass || tipo.IsSealed || tipo.IsAbstract, $"{tipo.Name} se pode herdar sem ser contrato.");
            Assert.True(!tipo.IsAbstract || tipo.IsSealed || tipo.GetConstructors().Length == 0, $"{tipo.Name} é abstrato com construtor público.");
        });
    }

    [Fact]
    public void Nenhum_tipo_estavel_do_motor_expoe_o_contrato_de_dialeto()
    {
        // Um membro público fora do namespace experimental que use um tipo do contrato espalharia o INCX001 por quem só usa
        // a parte estável.
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var expostos = Tipos(Motor)
            .Where(tipo => tipo.IsVisible && !NoContrato(tipo))
            .SelectMany(tipo => tipo.GetMembers(Todos).Where(VisivelFora).SelectMany(TiposDoMembro)
                .Append(tipo.BaseType).Concat(tipo.GetInterfaces()).OfType<Type>().SelectMany(Desembrulhar).Select(usado => (tipo, usado)))
            .Where(par => NoContrato(par.usado))
            .Select(par => $"{par.tipo.Name} usa {par.usado.Name}")
            .Distinct()
            .ToArray();

        Assert.True(expostos.Length == 0, $"Tipos estáveis do motor que expõem o contrato de dialeto: {string.Join(", ", expostos)}.");
    }

    [Fact]
    public void O_dialeto_PostgreSql_nao_le_texto_opaco_do_catalogo()
    {
        // Uma lista negra: expressões, defaults, locale, comentários, estatísticas, opções, segredos, sessões e o conteúdo
        // dos large objects guardam valor de linha ou segredo, e um canário de texto não acharia todos (as árvores de nó
        // guardam as constantes em bytes). Os comentários do C# ficam de fora; as formas permitidas saem antes da busca.
        // Também o SQL fora do C# (script ou recurso embutido), se um dia houver.
        string[] arquivos =
        [
            .. Repositorio.Versionados("src/CodeProcess.Incognito.PostgreSql/*.cs"),
            .. Repositorio.Versionados("src/CodeProcess.Incognito.PostgreSql/*.sql"),
            .. Repositorio.Versionados("src/CodeProcess.Incognito.PostgreSql/*.resx"),
        ];
        var achados = arquivos
            .SelectMany(arquivo => TextoOpaco(File.ReadAllText(Path.Combine(Repositorio.Raiz(), arquivo))).Select(trecho => $"{arquivo}: {trecho}"))
            .ToArray();

        Assert.Contains(arquivos, arquivo => arquivo.EndsWith("LeitorEsquemaPostgreSql.cs", StringComparison.Ordinal));
        Assert.True(achados.Length == 0, $"O dialeto PostgreSQL lê texto opaco do catálogo: {string.Join("; ", achados)}.");
    }

    [Theory]
    [InlineData("SELECT pg_catalog.pg_get_expr(i.indpred, i.indrelid) FROM pg_catalog.pg_index i")]
    [InlineData("SELECT k::text FROM pg_catalog.pg_constraint k")]
    [InlineData("SELECT pg_catalog.to_json(k) FROM pg_catalog.pg_constraint k")]
    [InlineData("SELECT c.colliculocale FROM pg_catalog.pg_collation c")]
    [InlineData("SELECT pg_catalog.current_setting('app.segredo')")]
    [InlineData("SELECT x.* FROM pg_catalog.pg_class x")]
    [InlineData("SELECT data FROM pg_largeobject")]
    [InlineData("SELECT i.indexprs FROM pg_catalog.pg_index i")]
    [InlineData("SELECT pg_catalog.shobj_description(d.oid, 'pg_database') FROM pg_catalog.pg_database d")]
    [InlineData("SELECT pg_catalog.query_to_xml('SELECT 1', true, false, '')")]
    [InlineData("SELECT pg_catalog.array_to_json(x.a) FROM x")]
    [InlineData("SELECT pg_catalog.json_build_object('c', k.conname) FROM pg_catalog.pg_constraint k")]
    [InlineData("SELECT CAST(k AS text) FROM pg_catalog.pg_constraint k")]
    [InlineData("SELECT k::\"varchar\" FROM pg_catalog.pg_constraint k")]
    [InlineData("SELECT pg_catalog.format('%s', k) FROM pg_catalog.pg_constraint k")]
    [InlineData("TABLE pg_catalog.pg_class")]
    [InlineData("SELECT DISTINCT * FROM pg_catalog.pg_class")]
    [InlineData("SELECT c.oid, * FROM pg_catalog.pg_class c")]
    [InlineData("SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.\"pg_largeobject\" l ON true")]
    [InlineData("SELECT pg_catalog.\"current_setting\"('app.segredo')")]
    [InlineData("SHOW app.segredo")]
    [InlineData("SELECT pg_catalog.pg_stat_get_backend_activity(1)")]
    [InlineData("\"pg_get_\" + \"expr\"")]
    [InlineData("DO $$ BEGIN EXECUTE x; END $$")]
    [InlineData("SELECT pg_catalog.current_setting('lc_monetary')")]
    [InlineData("SELECT c.relacl FROM pg_catalog.pg_class c")]
    [InlineData("SELECT pg_catalog.aclexplode(c.relacl) FROM pg_catalog.pg_class c")]
    public void A_lista_de_texto_opaco_pega_cada_forma_proibida(string consulta) => Assert.NotEmpty(TextoOpaco("const string C = \"\"\"\n" + consulta + "\n\"\"\";"));

    [Fact]
    public void A_lista_de_texto_opaco_deixa_as_formas_permitidas()
    {
        const string Permitidas = """
            // pg_get_expr num comentário não conta.
            private const string F = "NOT pg_catalog.starts_with(n.nspname, 'pg_') AND n.nspname NOT IN ('information_schema', 'incognito')";
            private const string P = "SELECT e.citada FROM pg_catalog.regexp_matches(pt.partexprs::text, ':varattno (-?[0-9]+)', 'g') AS e(citada)";
            private const string R = ", k.collicurules IS NOT NULL AS regras, d.daticurules IS NOT NULL";
            private const string S = "SELECT pg_catalog.current_setting('server_version_num'), pg_catalog.current_setting('max_identifier_length')::integer";
            private static readonly NomeObjeto _objetosGrandes = new("pg_catalog", "pg_largeobject");
            private const string L = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_largeobject_metadata)";
            """;

        Assert.Empty(TextoOpaco(Permitidas));
    }

    [Fact]
    public void Toda_propriedade_do_modelo_com_texto_esta_aprovada()
    {
        // Texto no modelo só de nomes de catálogo e de conjuntos fechados: um campo de texto livre (expressão, locale,
        // default) levaria valor de linha ao plano e ao relatório. A linhagem da identidade é a exceção nomeada.
        var comTexto = PropriedadesDoContrato()
            .Where(propriedade => ContemTexto(propriedade.PropertyType))
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}")
            .ToHashSet(StringComparer.Ordinal);

        var semAprovacao = comTexto.Except(_textoAprovado.Keys).Order(StringComparer.Ordinal).ToArray();
        var sobrando = _textoAprovado.Keys.Except(comTexto).Order(StringComparer.Ordinal).ToArray();
        Assert.True(semAprovacao.Length == 0, $"Propriedades do modelo com texto sem aprovação: {string.Join(", ", semAprovacao)}.");
        Assert.True(sobrando.Length == 0, $"Aprovações de texto sem propriedade: {string.Join(", ", sobrando)}.");
    }

    [Fact]
    public void Propriedade_nova_em_tipo_antigo_do_modelo_e_anulavel_ou_lista()
    {
        // Um dialeto que não conhece o campo novo deixa o padrão: nulo diz "não informa", o pior caso; um bool com padrão
        // false diria "não tem". As propriedades de antes da semântica do esquema ficam congeladas aqui.
        var contexto = new NullabilityInfoContext();
        var tipos = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToDictionary(tipo => tipo.Name);
        var ausentes = _propriedadesCongeladas.Keys.Where(nome => !tipos.ContainsKey(nome)).ToArray();
        var naoAnulaveis = _propriedadesCongeladas
            .Where(par => tipos.ContainsKey(par.Key))
            .SelectMany(par => tipos[par.Key].GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(propriedade => !par.Value.Contains(propriedade.Name))
                .Where(propriedade => Nullable.GetUnderlyingType(propriedade.PropertyType) is null
                    && !(propriedade.PropertyType.IsGenericType && propriedade.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                    && (propriedade.PropertyType.IsValueType || contexto.Create(propriedade).ReadState != NullabilityState.Nullable))
                .Select(propriedade => $"{par.Key}.{propriedade.Name}"))
            .ToArray();

        Assert.True(ausentes.Length == 0, $"Tipos congelados que não existem mais: {string.Join(", ", ausentes)}.");
        Assert.True(naoAnulaveis.Length == 0, $"Propriedades novas em tipos antigos do modelo que não são anuláveis nem listas: {string.Join(", ", naoAnulaveis)}.");
    }

    [Fact]
    public void O_que_depende_do_login_so_fica_nas_permissoes_do_acesso_lido()
    {
        // As permissões dependem de quem lê, e a comparação entre a origem e o destino deixa de fora só o EsquemaBanco.Acesso:
        // um campo do login em outro lugar faria o mesmo esquema sair diferente nos dois lados, ou passaria sem conferência
        // quando os dois logins coincidem nele. Pelo tipo (os Permissoes*, só no AcessoLido, e o AcessoLido, só no
        // EsquemaBanco.Acesso) e pelos nomes das capacidades, que não aparecem em outro tipo.
        var doModelo = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToArray();
        var permissoes = doModelo.Where(tipo => tipo.Name.StartsWith("Permissoes", StringComparison.Ordinal)).ToHashSet();
        var acesso = doModelo.Single(tipo => tipo.Name == "AcessoLido");
        var capacidades = permissoes.SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(propriedade => propriedade.Name)
            .Except(["Tabela", "Nome", "Tipo"])
            .Concat(["Administrador", "Superusuario", "LocalMonetario"])
            .ToHashSet(StringComparer.Ordinal);
        var foraDoLugar = PropriedadesDoContrato()
            .Where(propriedade => propriedade.DeclaringType != acesso && !permissoes.Contains(propriedade.DeclaringType!))
            .Where(propriedade => Desembrulhar(propriedade.PropertyType).Any(permissoes.Contains)
                || (Desembrulhar(propriedade.PropertyType).Contains(acesso) && $"{propriedade.DeclaringType!.Name}.{propriedade.Name}" != "EsquemaBanco.Acesso")
                || capacidades.Contains(propriedade.Name))
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}")
            .ToArray();

        Assert.True(permissoes.Count == 4 && capacidades.Count > 10, $"Só {permissoes.Count} tipos de permissões e {capacidades.Count} capacidades achados: a regra não está olhando o contrato.");
        Assert.True(foraDoLugar.Length == 0, $"Campos do login fora das permissões do EsquemaBanco.Acesso: {string.Join(", ", foraDoLugar)}.");
    }

    [Fact]
    public void O_modelo_so_tem_propriedades_de_tipos_conhecidos()
    {
        // Um campo público ou um tipo que as guardas não conhecem (objeto, memória de caracteres, exceção) escaparia da
        // lista aprovada de texto, do despejo e do canário: só passam números, marcas, enums, versão, nomes e objetos do
        // próprio modelo, e listas deles.
        var doModelo = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToHashSet();
        var modelo = doModelo.Where(tipo => tipo.GetProperties().Any(propriedade => propriedade.SetMethod is { IsPublic: true })).ToArray();
        bool Conhecido(Type tipo) =>
            tipo == typeof(string) || tipo == typeof(bool) || tipo == typeof(int) || tipo == typeof(long) || tipo == typeof(Version) || tipo.IsEnum
            || doModelo.Contains(tipo)
            || (Nullable.GetUnderlyingType(tipo) is { } valor && Conhecido(valor))
            || (tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && Conhecido(tipo.GetGenericArguments()[0]));
        var campos = doModelo.Where(tipo => !tipo.IsEnum).SelectMany(tipo => tipo.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(campo => $"{tipo.Name}.{campo.Name}")).ToArray();
        var desconhecidas = modelo
            .SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}" != "IdentidadeLida.Linhagem" && !Conhecido(propriedade.PropertyType))
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name} ({propriedade.PropertyType.Name})")
            .ToArray();

        Assert.True(modelo.Length > 10, $"Só {modelo.Length} tipos do modelo achados: a reflexão não está olhando o contrato.");
        Assert.True(campos.Length == 0, $"Campos públicos no contrato de dialeto: {string.Join(", ", campos)}.");
        Assert.True(desconhecidas.Length == 0, $"Propriedades do modelo de tipo que as guardas não conhecem: {string.Join(", ", desconhecidas)}.");
    }

    [Fact]
    public void O_modelo_nao_tem_dicionario_nem_lista_de_valor()
    {
        // O despejo e a perturbação da suíte percorrem objetos do modelo, nomes e texto: um dicionário ou uma lista de
        // números passaria sem conferência. A linhagem da identidade é a exceção nomeada.
        var doModelo = Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo)).ToHashSet();
        var proibidas = PropriedadesDoContrato()
            .Where(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}" != "IdentidadeLida.Linhagem")
            .Where(propriedade => propriedade.PropertyType.GetInterfaces().Append(propriedade.PropertyType).Any(tipo => tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
                || (propriedade.PropertyType != typeof(string)
                    && propriedade.PropertyType.GetInterfaces().Append(propriedade.PropertyType).FirstOrDefault(tipo => tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IEnumerable<>)) is { } lista
                    && lista.GetGenericArguments()[0] is var item && item != typeof(string) && !doModelo.Contains(item)))
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}")
            .ToArray();

        Assert.NotEmpty(PropriedadesDoContrato());
        Assert.True(proibidas.Length == 0, $"Dicionários ou listas de valor no modelo do esquema: {string.Join(", ", proibidas)}.");
    }

    [Theory]
    [InlineData("ContextoTransformacao")]
    [InlineData("ResultadoTransformacao")]
    public void Os_structs_com_valor_de_coluna_nao_sao_record_e_nao_herdam_o_ToString(string nome)
    {
        // O ToString de um record mostraria o valor (às vezes o próprio dado pessoal).
        var tipo = Tipos(Nucleo).Single(t => t.FullName == $"{Nucleo}.Transformadores.{nome}");

        Assert.True(tipo.IsValueType, $"{nome} precisa ser struct.");
        Assert.Null(tipo.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(tipo, tipo.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
    }

    // Os tipos que aparecem nos campos, propriedades, parâmetros, retornos, tipo base e tipos aninhados (inclusive os
    // gerados para lambdas). Não percorre o corpo dos métodos.
    private static IEnumerable<Type> TiposDaAssinatura(Type tipo)
    {
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var tipos = new List<Type>();
        if (tipo.BaseType is not null)
        {
            tipos.Add(tipo.BaseType);
        }

        tipos.AddRange(tipo.GetFields(Todos).Select(campo => campo.FieldType));
        tipos.AddRange(tipo.GetProperties(Todos).Select(propriedade => propriedade.PropertyType));
        foreach (var metodo in tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)))
        {
            tipos.AddRange(metodo.GetParameters().Select(parametro => parametro.ParameterType));
            if (metodo is MethodInfo comRetorno)
            {
                tipos.Add(comRetorno.ReturnType);
            }
        }

        return tipos
            .SelectMany(Desembrulhar)
            .Concat(tipo.GetNestedTypes(Todos).SelectMany(TiposDaAssinatura));
    }

    // Os trechos proibidos de um arquivo C#, sem as linhas de comentário e sem as formas permitidas.
    private static IEnumerable<string> TextoOpaco(string fonte)
    {
        var codigo = string.Join("\n", fonte.Split('\n').Where(linha => !linha.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        codigo = _formasPermitidas.Aggregate(codigo, (texto, forma) => forma.Replace(texto, " "));
        return _textoOpaco.SelectMany(padrao => padrao.Matches(codigo).Select(achado => achado.Value)).Distinct(StringComparer.Ordinal);
    }

    // As propriedades públicas dos tipos públicos do contrato de dialeto.
    private static IEnumerable<PropertyInfo> PropriedadesDoContrato() =>
        Tipos(Motor).Where(tipo => tipo.IsVisible && NoContrato(tipo))
            .SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    // Texto, lista ou dicionário de texto, e os nomes do contrato, que são texto.
    private static bool ContemTexto(Type tipo) =>
        tipo == typeof(string)
        || tipo.FullName is Dialetos + ".NomeObjeto" or Dialetos + ".ReferenciaColuna"
        || (tipo.IsGenericType && tipo.GetGenericArguments().Any(ContemTexto))
        || (tipo.IsArray && ContemTexto(tipo.GetElementType()!));

    private static bool NoContrato(Type tipo) => tipo.Namespace is { } nome && (nome == Dialetos || nome.StartsWith(Dialetos + ".", StringComparison.Ordinal));

    // Público ou protegido: o que quem herda de um tipo público também enxerga.
    private static bool VisivelFora(MemberInfo membro) => membro switch
    {
        MethodBase metodo => metodo.IsPublic || metodo.IsFamily || metodo.IsFamilyOrAssembly,
        FieldInfo campo => campo.IsPublic || campo.IsFamily || campo.IsFamilyOrAssembly,
        PropertyInfo propriedade => propriedade.GetAccessors(nonPublic: true).Any(VisivelFora),
        EventInfo evento => evento.AddMethod is { } adicionar && VisivelFora(adicionar),
        Type aninhado => aninhado.IsNestedPublic || aninhado.IsNestedFamily || aninhado.IsNestedFamORAssem,
        _ => false,
    };

    private static IEnumerable<Type> TiposDoMembro(MemberInfo membro) => membro switch
    {
        FieldInfo campo => [campo.FieldType],
        PropertyInfo propriedade => [propriedade.PropertyType],
        MethodInfo metodo => metodo.GetParameters().Select(parametro => parametro.ParameterType).Append(metodo.ReturnType),
        ConstructorInfo construtor => construtor.GetParameters().Select(parametro => parametro.ParameterType),
        EventInfo evento => evento.EventHandlerType is { } tipo ? [tipo] : [],
        _ => [],
    };

    // O tipo dos elementos de array, ponteiro e referência, e os argumentos de tipos genéricos.
    private static IEnumerable<Type> Desembrulhar(Type tipo)
    {
        if (tipo.HasElementType)
        {
            return Desembrulhar(tipo.GetElementType()!);
        }

        return tipo.IsGenericType ? tipo.GetGenericArguments().SelectMany(Desembrulhar).Prepend(tipo) : [tipo];
    }

    // Um tipo que não carrega não pode esconder os outros da verificação.
    private static IEnumerable<Type> Tipos(string assembly)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assembly)).GetTypes();
        }
        catch (ReflectionTypeLoadException excecao)
        {
            return excecao.Types.OfType<Type>();
        }
    }

    private static string NomeSemAridade(string nome)
    {
        var crase = nome.IndexOf('`', StringComparison.Ordinal);
        return crase < 0 ? nome : nome[..crase];
    }

    private static AssemblyName[] Referencias(string assembly) =>
        Assembly.Load(new AssemblyName(assembly)).GetReferencedAssemblies();

    /// <summary>
    /// Referências diretas do assembly e, recursivamente, as dos assemblies do próprio projeto que ele referencia.
    /// Pacotes de terceiros não são percorridos: a regra vale para o código deste repositório.
    /// </summary>
    private static List<AssemblyName> ReferenciasTransitivasDoProjeto(string assembly)
    {
        var visitados = new HashSet<string>(StringComparer.Ordinal) { assembly };
        var pendentes = new Queue<string>([assembly]);
        var resultado = new List<AssemblyName>();

        while (pendentes.TryDequeue(out var atual))
        {
            foreach (var referencia in Referencias(atual))
            {
                resultado.Add(referencia);
                if (referencia.Name!.StartsWith(Nucleo, StringComparison.Ordinal) && visitados.Add(referencia.Name))
                {
                    pendentes.Enqueue(referencia.Name);
                }
            }
        }

        return resultado;
    }

    /// <summary>Um assembly é do runtime quando está no diretório do framework compartilhado Microsoft.NETCore.App.</summary>
    // Os tipos de HTTP que o assembly referencia, pelos metadados, qualquer que seja o assembly que os resolve.
    private static string[] TiposDeHttpReferenciados(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();
        return metadados.TypeReferences
            .Select(manipulador => metadados.GetTypeReference(manipulador))
            .Select(tipo => metadados.GetString(tipo.Namespace) + "." + metadados.GetString(tipo.Name))
            .Where(nome => nome.StartsWith("System.Net.Http.", StringComparison.Ordinal) || _tiposDeHttp.Contains(nome))
            .Distinct()
            .ToArray();
    }

    private static bool ParteDoRuntime(AssemblyName referencia)
    {
        var diretorioDoRuntime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return File.Exists(Path.Combine(diretorioDoRuntime, referencia.Name + ".dll"));
    }
}
