using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Normas;

namespace CodeProcess.Incognito.Testes;

public sealed class CatalogoNormativoTestes
{
    private const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // A primeira revisão do catálogo: uma data anterior só aparece por engano (ano trocado, cópia de branch antiga).
    private static readonly DateOnly _primeiraRevisao = new(2026, 10, 15);

    private static readonly string[] _identificadores =
    [
        "lgpd",
        "anpd-estudo-anonimizacao-analise-juridica",
        "anpd-estudo-anonimizacao-tecnicas",
        "anpd-guia-anonimizacao",
        "iso-iec-27001-2022",
        "tjue-c-413-23-p",
        "nt-conjunta-2025-001",
        "in-rfb-2229-2024",
        "nt-nfe-2021-002",
        "nt-cte-2020-001",
        "nt-mdfe-2020-002",
        "nt-nfe-2019-001",
        "nt-nfe-2026-001",
        "nt-nfe-2023-002",
        "nt-nfe-2018-001",
        "nt-nfe-2013-007",
        "res-anatel-749-2022",
        "anatel-pgcn",
        "ato-anatel-12712-2024",
        "res-anatel-709-2019",
        "correios-faixas-cep",
        "ibge-dtb-2025",
        "bcb-manual-operacional-dict",
    ];

    // Os hosts das fontes primárias.
    private static readonly string[] _hostsPermitidos =
    [
        "www.planalto.gov.br",
        "www.gov.br",
        "www.iso.org",
        "curia.europa.eu",
        "www.nfe.fazenda.gov.br",
        "www.cte.fazenda.gov.br",
        "dfe-portal.svrs.rs.gov.br",
        "informacoes.anatel.gov.br",
        "buscacepinter.correios.com.br",
        "geoftp.ibge.gov.br",
        "www.bcb.gov.br",
    ];

    // As chaves de consulta que identificam um documento (nunca uma sessão).
    private static readonly string[] _chavesDeConsultaPermitidas = ["num", "conteudo", "sistema", "tipoArquivo", "nomeArquivo"];

    // Radicais comparados sem caixa e sem acento: nenhum texto do projeto promete conformidade nem anonimização garantida.
    private static readonly string[] _promessasProibidas =
    [
        "conformidade", "certifica", "complian", "garant", "assegur", "atende", "cumpre", "irreversivel", "adequad",
        "respeita", "aprovad", "homologad",
    ];

    private static IReadOnlyList<ReferenciaNormativa> Referencias => CatalogoNormativo.Referencias;

    [Fact]
    public void As_referencias_juridicas_estao_no_catalogo()
    {
        string[] juridicas = ["lgpd", "anpd-estudo-anonimizacao-analise-juridica", "anpd-guia-anonimizacao", "iso-iec-27001-2022", "tjue-c-413-23-p"];

        Assert.All(juridicas, identificador => Assert.Contains(identificador, Referencias.Select(referencia => referencia.Identificador)));
        var lgpd = Referencias.Single(referencia => referencia.Identificador == "lgpd");
        Assert.Equal("arts. 5º, 6º, 11, 12, 13, 14, 46, 48 e 49", lgpd.Dispositivos);
    }

    [Fact]
    public void O_catalogo_tem_exatamente_estas_referencias_nesta_ordem()
    {
        // Acrescentar ou retirar uma referência é sempre de propósito: muda esta lista junto.
        Assert.Equal(_identificadores, Referencias.Select(referencia => referencia.Identificador));
    }

    [Fact]
    public void Os_numeros_do_identificador_estao_no_titulo()
    {
        // Pega o título trocado entre duas referências parecidas (duas notas técnicas, por exemplo).
        Assert.All(Referencias, referencia =>
        {
            var tituloSemPontos = referencia.Titulo.Replace(".", "", StringComparison.Ordinal);
            Assert.All(Regex.Matches(referencia.Identificador, "[0-9]+"), numero => Assert.Contains(numero.Value, tituloSemPontos, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Cada_referencia_traz_a_data_da_ultima_revisao()
    {
        // Monótono: o relógio só avança, então o teste nunca passa a reprovar um commit antigo. Em UTC, a data é a mesma
        // de Brasília ou a seguinte; numa máquina com o relógio atrasado, reprova.
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.All(Referencias, referencia => Assert.InRange(referencia.RevisadaEm, _primeiraRevisao, hoje));
    }

    [Fact]
    public void A_revisao_do_catalogo_e_a_mais_antiga_das_referencias()
    {
        Assert.Equal(Referencias.Min(referencia => referencia.RevisadaEm), CatalogoNormativo.RevisaoMaisAntiga);
    }

    [Fact(Skip = Mutacao.MotivoIl, SkipType = typeof(Mutacao), SkipWhen = nameof(Mutacao.Ativa))]
    public void A_revisao_do_catalogo_e_calculada_sobre_todas_as_referencias()
    {
        // Enquanto todas as datas são iguais, ler a primeira ou o relógio daria o mesmo valor: a ligação é conferida no IL
        // (sem as chamadas que a cobertura do CI acrescenta).
        var todas = typeof(ReferenciasNormativas).GetProperty(nameof(ReferenciasNormativas.Todas), Todos)!.GetMethod!;
        var maisAntiga = typeof(CatalogoNormativo).GetMethod(nameof(CatalogoNormativo.MaisAntiga), Todos)!;
        var revisao = typeof(CatalogoNormativo).GetProperty(nameof(CatalogoNormativo.RevisaoMaisAntiga))!.GetMethod!;
        var referencias = typeof(CatalogoNormativo).GetProperty(nameof(CatalogoNormativo.Referencias))!.GetMethod!;

        Assert.Equal([todas, maisAntiga], SemCobertura(MembrosNoIl.Citados(revisao)));
        Assert.Equal([todas], SemCobertura(MembrosNoIl.Citados(referencias)));
    }

    [Theory]
    [InlineData("2027-03-01", "2026-11-20", "2027-01-05", "2026-11-20")]
    [InlineData("2026-11-20", "2027-03-01", "2027-01-05", "2026-11-20")]
    [InlineData("2027-03-01", "2027-01-05", "2026-11-20", "2026-11-20")]
    [InlineData("2027-01-05", "2027-01-05", "2027-01-05", "2027-01-05")]
    public void A_mais_antiga_e_a_menor_data_em_qualquer_posicao(string primeira, string segunda, string terceira, string esperada)
    {
        ReferenciaNormativa[] referencias = [Sintetica(primeira), Sintetica(segunda), Sintetica(terceira)];

        Assert.Equal(DateOnly.Parse(esperada, CultureInfo.InvariantCulture), CatalogoNormativo.MaisAntiga(referencias));
    }

    [Fact]
    public void Catalogo_vazio_nao_tem_revisao()
    {
        var excecao = Assert.Throws<ArgumentException>(() => CatalogoNormativo.MaisAntiga([]));

        Assert.Equal("referencias", excecao.ParamName);
    }

    [Fact]
    public void Identificadores_sao_unicos_em_minusculas_com_hifens()
    {
        Assert.Equal(Referencias.Count, Referencias.Select(referencia => referencia.Identificador).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Referencias, referencia => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", referencia.Identificador));
    }

    [Fact]
    public void Textos_sao_preenchidos_sem_espaco_nas_pontas_nem_caractere_invisivel()
    {
        Assert.All(Referencias.SelectMany(Textos), texto =>
        {
            Assert.False(string.IsNullOrWhiteSpace(texto));
            Assert.Equal(texto.Trim(), texto);
            Assert.DoesNotContain("  ", texto, StringComparison.Ordinal);

            // Controle, formato (largura zero, inversão de direção) e espaço que não seja o comum: escondem uma frase dos
            // outros testes.
            Assert.DoesNotMatch(@"[\p{C}\p{Z}-[ ]]", texto);
        });
    }

    [Fact]
    public void So_o_guia_da_ANPD_esta_em_elaboracao()
    {
        var emElaboracao = Referencias.Where(referencia => referencia.Situacao == SituacaoNormativa.EmElaboracao)
            .Select(referencia => referencia.Identificador);

        Assert.Equal(["anpd-guia-anonimizacao"], emElaboracao);
    }

    [Fact]
    public void Fontes_sao_https_em_hosts_permitidos_sem_dado_de_sessao()
    {
        Assert.All(Referencias, referencia =>
        {
            var fonte = referencia.Fonte;
            Assert.True(fonte.IsAbsoluteUri, referencia.Identificador);
            Assert.Equal(Uri.UriSchemeHttps, fonte.Scheme);
            Assert.Contains(fonte.Host, _hostsPermitidos);
            Assert.True(fonte.IsDefaultPort, referencia.Identificador);
            Assert.Empty(fonte.UserInfo);
            Assert.Empty(fonte.Fragment);
            Assert.DoesNotContain(";", fonte.AbsolutePath, StringComparison.Ordinal);
            Assert.DoesNotContain("%", fonte.Query, StringComparison.Ordinal);
            var chaves = fonte.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(parametro => parametro.Split('=', 2)[0]);
            Assert.All(chaves, chave => Assert.Contains(chave, _chavesDeConsultaPermitidas));
        });
    }

    [Fact]
    public void A_lista_nao_aceita_escrita_e_e_sempre_a_mesma()
    {
        var lista = Assert.IsAssignableFrom<IList<ReferenciaNormativa>>(Referencias);

        Assert.True(lista.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => lista[0] = lista[1]);
        Assert.IsNotType<ReferenciaNormativa[]>(Referencias);
        Assert.Same(CatalogoNormativo.Referencias, CatalogoNormativo.Referencias);
    }

    [Fact]
    public void Textos_nao_prometem_conformidade()
    {
        // Sem ICU (globalização invariante), a decomposição não separa os acentos e o teste ficaria cego.
        Assert.Equal(2, "ã".Normalize(NormalizationForm.FormD).Length);

        Assert.All(Referencias.SelectMany(Textos), texto =>
        {
            var normalizado = SemAcento(texto).ToLowerInvariant();
            Assert.All(_promessasProibidas, promessa => Assert.DoesNotContain(promessa, normalizado, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Textos_nao_trazem_documento_nem_dado_local()
    {
        // Nenhum CPF, CNPJ (numérico ou alfanumérico) ou chave de acesso copiado de uma nota técnica ou notícia, com ou
        // sem máscara, nem e-mail ou caminho de máquina das notas de pesquisa.
        Assert.All(Referencias.SelectMany(Textos), texto =>
        {
            SemDocumento(texto);
            SemDocumento(Regex.Replace(texto, @"[\s./\p{Pd}․‧·]", "").ToUpperInvariant());
            Assert.DoesNotContain("@", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("..", texto, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"(?i)\b[a-z]:[\\/]|/home/|/users/|~/|pesquisa-local|file:", texto);
        });

        // Nas fontes, sem a regra dos 11 dígitos depois de tirar a máscara: o caminho tem anos separados por barra.
        Assert.All(Referencias.Select(referencia => referencia.Fonte.AbsoluteUri), fonte =>
        {
            SemDocumento(fonte);
            SemCnpj(Regex.Replace(fonte, "[./-]", "").ToUpperInvariant());
            Assert.DoesNotContain("@", fonte, StringComparison.Ordinal);
        });
    }

    [Fact(Skip = Mutacao.MotivoIl, SkipType = typeof(Mutacao), SkipWhen = nameof(Mutacao.Ativa))]
    public void A_classe_de_dados_so_tem_os_dados()
    {
        // Os dados ficam fora da mutação noturna; lógica posta ali ficaria sem mutação em silêncio.
        var tipo = typeof(ReferenciasNormativas);
        var todas = tipo.GetProperty(nameof(ReferenciasNormativas.Todas), Todos)!;

        Assert.Equal([todas], tipo.GetProperties(Todos));
        Assert.Equal([todas.GetMethod!], tipo.GetMethods(Todos));
        Assert.Empty(tipo.GetNestedTypes(Todos));
        Assert.All(tipo.GetFields(Todos), campo => Assert.Contains(campo.FieldType, (Type[])[typeof(DateOnly), typeof(IReadOnlyList<ReferenciaNormativa>)]));

        // O getter só lê o campo, e o inicializador só monta referências: nada de filtro, ordenação ou cálculo.
        Assert.All(MembrosNoIl.Citados(todas.GetMethod!), membro => Assert.Equal(tipo, membro.DeclaringType));
        var citados = MembrosNoIl.Citados(tipo.TypeInitializer!).ToArray();
        Assert.NotEmpty(citados);
        Assert.All(citados, membro => Assert.True(PermitidoNosDados(tipo, membro), $"{membro.DeclaringType?.Name}.{membro.Name}"));
    }

    [Fact(Skip = Mutacao.MotivoIl, SkipType = typeof(Mutacao), SkipWhen = nameof(Mutacao.Ativa))]
    public void Nenhum_transformador_le_o_catalogo()
    {
        // Rever uma referência nunca pode mudar a saída em silêncio: fora de Normas, nenhum método do núcleo cita o
        // catálogo, nem no corpo (o IL pega também um using global ou do csproj).
        var foraDeNormas = typeof(CatalogoNormativo).Assembly.GetTypes().Where(tipo => !EmNormas(tipo)).ToArray();

        var usos = foraDeNormas
            .SelectMany(Metodos)
            .SelectMany(metodo => MembrosNoIl.Citados(metodo)
                .Where(membro => EmNormas(MembrosNoIl.Dono(membro)))
                .Select(membro => $"{metodo.DeclaringType!.FullName}.{metodo.Name} -> {MembrosNoIl.Dono(membro)!.Name}.{membro.Name}"))
            .ToArray();

        Assert.Contains(foraDeNormas, tipo => tipo == typeof(Pseudonimizador));
        Assert.Empty(usos);
    }

    [Fact(Skip = Mutacao.MotivoIl, SkipType = typeof(Mutacao), SkipWhen = nameof(Mutacao.Ativa))]
    public void O_catalogo_nao_le_o_resto_do_nucleo()
    {
        // O outro sentido: o catálogo não empurra dados para transformador nenhum. Fora de Normas, só o runtime (e a
        // instrumentação da cobertura no CI, que reescreve o IL).
        var deNormas = typeof(CatalogoNormativo).Assembly.GetTypes().Where(EmNormas).ToArray();

        var usos = deNormas
            .SelectMany(Metodos)
            .SelectMany(metodo => MembrosNoIl.Citados(metodo)
                .Select(MembrosNoIl.Dono)
                .Where(dono => dono is not null && !EmNormas(dono) && !DoRuntimeOuDaCobertura(dono))
                .Select(dono => $"{metodo.DeclaringType!.FullName}.{metodo.Name} -> {dono!.FullName}"))
            .ToArray();

        Assert.Equal(4, deNormas.Length);
        Assert.Empty(usos);
    }

    [Fact(Skip = "Só no release (INCOGNITO_DATA_DO_RELEASE).", SkipType = typeof(CatalogoNormativoTestes), SkipUnless = nameof(NoRelease))]
    public void No_release_o_catalogo_foi_revisto_nos_30_dias_antes_do_commit()
    {
        // A data do commit da tag, e não o relógio: a mesma tag dá sempre o mesmo resultado.
        Assert.True(
            DateOnly.TryParseExact(DataDoRelease, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data),
            "INCOGNITO_DATA_DO_RELEASE fora do formato aaaa-mm-dd.");

        Assert.InRange(CatalogoNormativo.RevisaoMaisAntiga, data.AddDays(-30), data);
    }

    [Fact]
    public void No_CI_a_data_do_release_so_vale_no_workflow_release()
    {
        // Fora do release, a variável ligaria uma conferência que depende da data de outro commit.
        var noCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var workflow = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW");

        Assert.False(noCi && NoRelease && workflow != "release", "INCOGNITO_DATA_DO_RELEASE definida fora do workflow release.");
    }

    public static bool NoRelease => !string.IsNullOrEmpty(DataDoRelease);

    private static string? DataDoRelease => Environment.GetEnvironmentVariable("INCOGNITO_DATA_DO_RELEASE");

    private static IEnumerable<MethodBase> Metodos(Type tipo) =>
        tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos));

    private static bool EmNormas(Type? tipo)
    {
        var normas = typeof(CatalogoNormativo).Namespace!;
        return tipo?.Namespace is { } espaco && (espaco == normas || espaco.StartsWith(normas + ".", StringComparison.Ordinal));
    }

    private static bool DoRuntimeOuDaCobertura(Type tipo) =>
        DaCobertura(tipo) || (tipo.Namespace is { } espaco && (espaco == "System" || espaco.StartsWith("System.", StringComparison.Ordinal)));

    // O coverlet, no job rapido, reescreve o IL e acrescenta chamadas ao rastreador dele.
    private static bool DaCobertura(Type? tipo) =>
        tipo?.Namespace?.StartsWith("Coverlet.Core.Instrumentation", StringComparison.Ordinal) == true;

    private static IEnumerable<MemberInfo> SemCobertura(IEnumerable<MemberInfo> membros) =>
        membros.Where(membro => !DaCobertura(MembrosNoIl.Dono(membro)));

    // Os únicos membros que o inicializador dos dados pode citar: os campos da própria classe, o tipo e o construtor da
    // referência, os construtores de Uri e DateOnly que recebem os valores e o Array.AsReadOnly.
    private static bool PermitidoNosDados(Type tipo, MemberInfo membro) => membro switch
    {
        FieldInfo campo => campo.DeclaringType == tipo,
        Type citado => citado == typeof(ReferenciaNormativa),
        ConstructorInfo construtor => construtor.DeclaringType == typeof(ReferenciaNormativa)
            || construtor == typeof(Uri).GetConstructor([typeof(string)])
            || construtor == typeof(DateOnly).GetConstructor([typeof(int), typeof(int), typeof(int)]),
        MethodInfo { IsGenericMethod: true } metodo => metodo.GetGenericMethodDefinition() == typeof(Array).GetMethod(nameof(Array.AsReadOnly)),
        _ => false,
    };

    private static void SemDocumento(string texto)
    {
        Assert.DoesNotMatch("[0-9]{11}", texto);
        SemCnpj(texto);
    }

    private static void SemCnpj(string texto)
    {
        for (var inicio = 0; inicio + 14 <= texto.Length; inicio++)
        {
            Assert.False(ValidadorBr.Cnpj(texto.AsSpan(inicio, 14)), $"CNPJ na posição {inicio}.");
        }
    }

    private static ReferenciaNormativa Sintetica(string revisadaEm) => new(
        "sintetica", "Referência sintética", "Teste", null, SituacaoNormativa.Atual, "Sem observação.", "Teste.",
        new Uri("https://www.gov.br/"), DateOnly.Parse(revisadaEm, CultureInfo.InvariantCulture));

    private static IEnumerable<string> Textos(ReferenciaNormativa referencia)
    {
        yield return referencia.Identificador;
        yield return referencia.Titulo;
        yield return referencia.Emissor;
        yield return referencia.Observacao;
        yield return referencia.Motivo;
        if (referencia.Dispositivos is { } dispositivos)
        {
            yield return dispositivos;
        }
    }

    private static string SemAcento(string texto)
    {
        var construtor = new StringBuilder(texto.Length);
        foreach (var caractere in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                construtor.Append(caractere);
            }
        }

        return construtor.ToString();
    }
}
