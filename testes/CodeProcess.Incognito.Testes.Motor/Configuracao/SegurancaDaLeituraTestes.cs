using System.Diagnostics;
using CodeProcess.Incognito.Configuracao;
using CsCheck;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>
/// O que a leitura recusa para não esconder nem vazar nada: construções do YAML perigosas, caracteres que o diff não
/// mostra, segredo por extenso em qualquer lugar e referências de arquivo que acessariam a rede ou um dispositivo.
/// </summary>
public sealed class SegurancaDaLeituraTestes
{
    private const string Sentinela = "SENTINELAq7Zx";

    [Theory]
    [InlineData(17, true)]
    [InlineData(16, false)]
    public void Profundidade_acima_de_16_e_recusada_em_bloco_e_em_fluxo(int niveis, bool recusada)
    {
        var bloco = string.Concat(Enumerable.Range(0, niveis - 2).Select(nivel => new string(' ', 2 * nivel) + "a:\n")) + new string(' ', 2 * (niveis - 2)) + "a: 1\n";
        var fluxo = "x: " + new string('[', niveis - 1) + new string(']', niveis - 1) + "\n";

        foreach (var texto in new[] { Yaml.Com("tabelas:\n" + Recuar(bloco, 2)), fluxo })
        {
            var codigos = Yaml.Erros(texto).Select(erro => erro.Codigo);
            Assert.Equal(recusada, codigos.Contains("INC2004"));
        }

        static string Recuar(string texto, int espacos) => string.Concat(texto.Split('\n').Where(linha => linha.Length > 0).Select(linha => new string(' ', espacos) + linha + "\n"));
    }

    [Fact]
    public void Aninhamento_enorme_nao_derruba_o_processo()
    {
        var cronometro = Stopwatch.StartNew();

        var erro = Yaml.UnicoErro("x: " + new string('[', 100_000) + "\n");

        Assert.Equal(("INC2004", 1, 19), (erro.Codigo, erro.Linha, erro.Coluna));
        Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
    }

    [Fact]
    public void Bomba_de_aliases_e_recusada_na_primeira_ancora()
    {
        var bomba = "a0: &a0 [x, x, x, x]\n" + string.Concat(Enumerable.Range(1, 30).Select(nivel => $"a{nivel}: &a{nivel} [*a{nivel - 1}, *a{nivel - 1}, *a{nivel - 1}, *a{nivel - 1}]\n")) + "? *a30\n: 1\n";
        var cronometro = Stopwatch.StartNew();

        var erro = Yaml.UnicoErro(bomba);

        Assert.Equal(("INC2004", 1, 5), (erro.Codigo, erro.Linha, erro.Coluna));
        Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
    }

    [Theory]
    [InlineData("ambiente: &a homologacao", 1)]
    [InlineData("ambiente: !!str homologacao", 1)]
    [InlineData("ambiente: !x homologacao", 1)]
    [InlineData("ambiente: *a", 1)]
    [InlineData("dominios: { ? [a] : 1 }", 1)]
    [InlineData("%YAML 1.2\n---\nambiente: homologacao", 1)]
    [InlineData("%TAG ! tag:x,2026:\n---\nambiente: homologacao", 1)]
    [InlineData("%FOO bar\n---\nambiente: homologacao", 1)]
    [InlineData("ambiente: homologacao\n---\nambiente: producao", 1)]
    public void Construcao_do_YAML_nao_aceita_e_recusada(string trecho, int quantidade)
    {
        var erros = Yaml.Erros(trecho + "\n");

        Assert.Equal(quantidade, erros.Count);
        Assert.Equal("INC2004", erros[0].Codigo);
    }

    [Fact]
    public void Um_unico_documento_com_marcador_e_aceito()
    {
        Assert.Equal("homologacao", Yaml.Ler("---\n" + Yaml.Minimo + "...\n").Ambiente);
    }

    [Fact]
    public void Chave_repetida_e_recusada_na_segunda_ocorrencia_mesmo_com_aspas()
    {
        var erro = Yaml.UnicoErro(Yaml.Minimo + "\"ambiente\": producao\n");

        Assert.Equal(("INC2005", 12, 1, ""), (erro.Codigo, erro.Linha, erro.Coluna, erro.Local));
    }

    [Fact]
    public void Erro_de_sintaxe_aponta_a_posicao_sem_repetir_o_texto()
    {
        var erro = Yaml.UnicoErro(Yaml.Com($"justificativa: \"{Sentinela}\n"));

        Assert.Equal("INC2003", erro.Codigo);
        Assert.NotNull(erro.Linha);
        Assert.DoesNotContain(Sentinela, erro.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<int, string> CaracteresProibidos() => new()
    {
        { 0x0001, "controle C0" },
        { 0x007F, "DEL" },
        { 0x0085, "NEL" },
        { 0x0090, "controle C1" },
        { 0x2028, "LS" },
        { 0x2029, "PS" },
        { 0x202E, "RLO" },
        { 0x2066, "LRI" },
        { 0x200F, "RLM" },
        { 0xFEFF, "BOM no meio" },
        { 0xD800, "surrogate sem par" },
    };

    [Theory]
    [MemberData(nameof(CaracteresProibidos))]
    public void Caractere_que_o_diff_nao_mostra_e_recusado_com_a_posicao(int codigo, string nome)
    {
        var texto = Yaml.Com("# comentario " + (char)codigo + " fim\n");

        var erro = Yaml.UnicoErro(texto);

        Assert.True(("INC2002", 12, 14) == (erro.Codigo, erro.Linha, erro.Coluna), $"{nome}: {erro}");
    }

    [Fact]
    public void Linha_escondida_por_separador_Unicode_e_recusada()
    {
        var texto = Yaml.Minimo.Replace("ambiente: homologacao", "ambiente: homologacao" + (char)0x2028 + "modo: efemero", StringComparison.Ordinal);

        Assert.Equal("INC2002", Yaml.UnicoErro(texto).Codigo);
    }

    [Fact]
    public void BOM_no_inicio_e_aceito_sem_deslocar_as_colunas()
    {
        var erro = Yaml.UnicoErro((char)0xFEFF + Yaml.Trocando("versao: 1", "versao: 2"));

        Assert.Equal((1, 9), (erro.Linha, erro.Coluna));
    }

    [Fact]
    public void Coluna_conta_unidades_UTF16_tab_e_CRLF()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var texto = Yaml.Minimo
            .Replace("ambiente: homologacao\n", "ambiente: homologacao\npolitica:\t{ consistencia: \"" + emoji + "\", paralelismo: \"4\" }\n", StringComparison.Ordinal)
            .Replace("modo: reproduzivel", "modo:\treproduzivel", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        var erros = Yaml.Erros(texto);

        Assert.Equal([(12, 27), (12, 46)], erros.Select(erro => (erro.Linha!.Value, erro.Coluna!.Value)));
    }

    public static TheoryData<string> ChavesPorExtenso()
    {
        var chave = Yaml.ChaveGerada();
        var corpo = chave["inc1_".Length..];
        return
        [
            $"chave: {chave}",
            $"chave: \"{chave}\"",
            $"chave: \"inc1\\x5f{corpo}\"",
            $"chave: \"\\x69nc1_{corpo}\"",
            $"chave: \"inc1_{corpo[..20]}\\\n  {corpo[20..]}\"",
            $"chave: >-\n  inc1_{corpo[..20]}\n  {corpo[20..]}",
            $"chave: ambiente:X # {chave}",
            $"# chave antiga: {chave}",
            $"{chave}: 1",
            $"politica: {{ consistencia: \"{chave}\" }}",
            $"justificativa: |\n  {chave}",
            $"chave: {chave}-velha",
            $"chave: INC1_{corpo}",
            $"chave: inc1_{corpo.Replace('-', '+').Replace('_', '/')}==",
            $"chave: inc1_{corpo[..24]}",
        ];
    }

    [Theory]
    [MemberData(nameof(ChavesPorExtenso))]
    public void Chave_por_extenso_no_arquivo_e_recusada(string trecho)
    {
        var excecao = Yaml.Excecao(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", trecho));

        Assert.Contains(excecao.Erros, erro => erro.Codigo == "INC2011");
        Assert.DoesNotContain("inc1_", excecao.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("chave: inc1_curto")]
    [InlineData("# o formato da chave começa por inc1_")]
    public void Prefixo_sem_chave_nao_dispara(string trecho)
    {
        var erros = Yaml.Erros(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", trecho));

        Assert.DoesNotContain(erros, erro => erro.Codigo == "INC2011");
    }

    [Theory]
    [InlineData("# conexao antiga: Host=db;Password=" + Sentinela)]
    [InlineData("# Server=db;Pwd =" + Sentinela)]
    [InlineData("# senha=" + Sentinela)]
    [InlineData("# postgres://usuario:" + Sentinela + "@db:5432/loja")]
    public void Senha_por_extenso_no_arquivo_e_recusada(string trecho)
    {
        var excecao = Yaml.Excecao(Yaml.Com(trecho + "\n"));

        Assert.Equal("INC2011", Assert.Single(excecao.Erros).Codigo);
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  conexao: Host=db;Database=loja", "INC2012")]
    [InlineData("  conexao: ambiente: INCOGNITO_ORIGEM", "INC2003")]
    [InlineData("  conexao: \"ambiente: INCOGNITO_ORIGEM\"", "INC2012")]
    [InlineData("  conexao: ambiente:1ORIGEM", "INC2012")]
    [InlineData("  conexao: ambiente:", "INC2003")]
    [InlineData("  conexao: arquivo:", "INC2003")]
    [InlineData("  conexao: \"arquivo:\"", "INC2012")]
    [InlineData("  conexao: \"ambiente:\"", "INC2012")]
    [InlineData("  conexao: \"arquivo: /x\"", "INC2012")]
    [InlineData("  conexao: arquivo:~/.pgpass", "INC2012")]
    [InlineData("  conexao: arquivo://servidor/x", "INC2012")]
    [InlineData("  conexao: arquivo:\\\\servidor\\share\\x", "INC2012")]
    [InlineData("  conexao: arquivo:\\\\.\\pipe\\x", "INC2012")]
    [InlineData("  conexao: arquivo:https://servidor/x", "INC2012")]
    [InlineData("  conexao: arquivo:Host=db;Database=loja", "INC2012")]
    [InlineData("  conexao: arquivo:NUL", "INC2012")]
    [InlineData("  conexao: arquivo:segredos/com1.txt", "INC2012")]
    [InlineData("  conexao: arquivo:segredos/origem", "INC2012")]
    [InlineData("  conexao: ~", "INC2007")]
    [InlineData("  conexao: { a: 1 }", "INC2008")]
    public void Referencia_de_segredo_fora_da_forma_e_recusada(string linha, string codigo)
    {
        var erros = Yaml.Erros(Yaml.Trocando("  conexao: ambiente:INCOGNITO_ORIGEM", linha));

        Assert.Equal(codigo, erros[0].Codigo);
    }

    [Fact]
    public void Arquivo_absoluto_e_aceito()
    {
        var caminho = Yaml.CaminhoAbsoluto("incognito.chave");

        var chave = Yaml.Ler(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: arquivo:" + caminho)).Chave!;

        Assert.Equal((TipoReferenciaSegredo.Arquivo, caminho), (chave.Tipo, chave.Alvo));
    }

    [Fact]
    public void Mensagens_nunca_repetem_o_que_foi_escrito()
    {
        var casos = new[]
        {
            Yaml.Com($"{Sentinela}: 1\n"),
            Yaml.Com($"politica:\n  {Sentinela}: 1\n"),
            Yaml.Minimo + $"\"ambiente\": {Sentinela}\n",
            $"x: &{Sentinela} 1\ny: *{Sentinela}\n",
            $"x: *{Sentinela}\n",
            $"x: !{Sentinela} 1\n",
            Yaml.Trocando("  conexao: ambiente:INCOGNITO_ORIGEM", $"  conexao: {Sentinela}"),
            Yaml.Trocando("  conexao: ambiente:INCOGNITO_ORIGEM", $"  conexao: arquivo:\\\\{Sentinela}\\x"),
            Yaml.Trocando("ambiente: homologacao", $"ambiente: {Sentinela}"),
            Yaml.Trocando("modo: reproduzivel", $"modo: {Sentinela}"),
            Yaml.Com($"dominios:\n  {Sentinela}: {{ transformador: br.cpf@1 }}\n"),
            Yaml.Com($"dominios:\n  cpf: {{ transformador: {Sentinela} }}\n"),
            Yaml.Com($"dominios:\n  cpf: {{ transformador: br.cpf@1, {Sentinela}: x }}\n"),
            Yaml.Com($"tabelas:\n  {Sentinela}: {{ acao: esvaziar }}\n"),
            Yaml.Com($"tabelas:\n  public.clientes:\n    colunas:\n      cpf: {Sentinela}\n"),
            Yaml.Com($"tabelas:\n  public.clientes:\n    colunas:\n      \"{Sentinela}.x\": manter\n"),
            $"x: \"{Sentinela}\\q\"\n",
        };

        foreach (var texto in casos)
        {
            var excecao = Yaml.Excecao(texto);
            Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.All(excecao.Erros, erro => Assert.DoesNotContain(Sentinela, erro.ToString() + erro.Local, StringComparison.OrdinalIgnoreCase));
            Assert.Null(excecao.InnerException);
        }
    }

    [Fact]
    public void Segredo_escrito_como_chave_de_mapa_nunca_volta_no_local()
    {
        var chave = Yaml.ChaveGerada();
        var casos = new[]
        {
            $"\"{chave}\":\n  a: 1\n  a: 2\n",
            $"\"{chave}\":\n  x: |\n    Password=abc\n",
            "\"Host=db;Password=" + Sentinela + "\": { a: 1, a: 1 }\n",
            "\"postgresql://u:" + Sentinela + "@db/x\": { a: 1, a: 1 }\n",
            "tabelas:\n  \"" + Sentinela + " livre 123\": 1\n",
            "tabelas:\n  public.t:\n    colunas:\n      c: { acao: manter, \"" + Sentinela + " x\": x }\n",
        };

        foreach (var texto in casos)
        {
            var excecao = Yaml.Excecao(Yaml.Com(texto));
            foreach (var erro in excecao.Erros)
            {
                Assert.DoesNotContain(chave, erro.ToString() + erro.Local, StringComparison.Ordinal);
                Assert.DoesNotContain(Sentinela, erro.ToString() + erro.Local, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Chave_longa_com_muitos_filhos_nao_esgota_a_memoria()
    {
        var texto = "? " + new string('k', 200_000) + "\n: [" + string.Join(", ", Enumerable.Repeat("1", 20_000)) + "]\n";
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var antes = GC.GetAllocatedBytesForCurrentThread();

        var erros = Yaml.Erros(texto);

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - antes < 200_000_000, "Alocou demais.");
        Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
        Assert.Contains(erros, erro => erro.Codigo == "INC2006" && erro.Local.Length == 0);
    }

    [Fact]
    public void Url_com_credencial_e_varrida_em_tempo_linear()
    {
        foreach (var texto in new[] { "#" + string.Concat(Enumerable.Repeat("a://x:", 200_000)), "# " + new string('a', 1_000_000) })
        {
            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal("homologacao", ConfiguracaoIncognito.LerYaml(Yaml.Com(texto + "\n")).Ambiente);
            Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
        }
    }

    [Theory]
    [InlineData("# Host=db;Username=u;Psw=s3nh4")]
    [InlineData("# postgresql://:s3nh4@db/loja")]
    public void Outras_formas_de_senha_sao_recusadas(string trecho)
    {
        Assert.Equal("INC2011", Yaml.UnicoErro(Yaml.Com(trecho + "\n")).Codigo);
    }

    [Fact]
    public void Chave_com_invisiveis_ou_largura_total_e_recusada()
    {
        var chave = Yaml.ChaveGerada();
        var comInvisiveis = string.Concat(chave.Chunk(15).Select(pedaco => new string(pedaco) + (char)0x200B));
        var larguraTotal = string.Concat(chave[..5].Select(caractere => (char)(caractere + 0xFEE0))) + chave[5..];

        Assert.Contains(Yaml.Erros(Yaml.Com("# " + comInvisiveis + "\n")), erro => erro.Codigo == "INC2011");
        Assert.Contains(Yaml.Erros(Yaml.Com("# " + larguraTotal + "\n")), erro => erro.Codigo == "INC2011");
    }

    [Fact]
    public void Chave_por_extenso_e_acusada_mesmo_com_outro_caractere_proibido()
    {
        var erros = Yaml.Erros(Yaml.Com("# " + Yaml.ChaveGerada() + "\n# fim" + (char)0x0085 + "\n"));

        Assert.Equal(["INC2011", "INC2002"], erros.Select(erro => erro.Codigo));
    }

    [Theory]
    [InlineData("justificativa: \"ok\\e[2J\"")]
    [InlineData("justificativa: \"ok\\x00\"")]
    [InlineData("justificativa: \"ok\\u202Eabc\"")]
    [InlineData("justificativa: \"ok\\u200Babc\"")]
    [InlineData("justificativa: \"ok\\rabc\"")]
    public void Caractere_proibido_escrito_por_escape_e_recusado(string trecho)
    {
        Assert.Contains(Yaml.Erros(Yaml.Com(trecho + "\n")), erro => erro.Codigo == "INC2002");
    }

    [Fact]
    public void CR_sozinho_e_recusado()
    {
        var erro = Yaml.UnicoErro(Yaml.Minimo.Replace("ambiente: homologacao\n", "ambiente: homologacao\r# cobre a linha\n", StringComparison.Ordinal));

        Assert.Equal(("INC2002", 11, 22), (erro.Codigo, erro.Linha, erro.Coluna));
    }

    [Fact]
    public void Quebra_de_linha_escapada_entre_aspas_e_YAML_valido()
    {
        Assert.Equal("INC2010", Yaml.UnicoErro(Yaml.Trocando("ambiente: homologacao", "ambiente: \"h\\n\"")).Codigo);
    }

    [Fact]
    public void Porcento_no_comeco_de_linha_depois_do_conteudo_nao_e_diretiva()
    {
        Assert.Equal("INC2003", Yaml.UnicoErro(Yaml.Com("politica: { consistencia: \"melhor\n%Esforco\" }\n")).Codigo);
    }

    [Fact]
    public void Documento_vazio_explicito_e_arquivo_vazio()
    {
        Assert.Equal(("INC2001", (int?)null), (Yaml.UnicoErro("---\n...\n").Codigo, Yaml.UnicoErro("---\n...\n").Linha));
        Assert.Equal("INC2001", Yaml.UnicoErro("# só comentário\n").Codigo);
    }

    [Theory]
    [InlineData(@"\??\UNC\127.0.0.1\c$\k.key")]
    [InlineData(@"\??\pipe\qualquer")]
    [InlineData(@"\??\GLOBALROOT\Device\Mup\srv\share\k")]
    [InlineData(@"/??/UNC/srv/k")]
    [InlineData(@"\\?\UNC\srv\k")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"segredos/COM¹")]
    public void Caminho_de_objeto_do_NT_ou_dispositivo_e_recusado(string caminho)
    {
        Assert.Equal("INC2012", Yaml.UnicoErro(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: \"arquivo:" + caminho.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"")).Codigo);
    }

    [Fact]
    public void No_Windows_caminho_relativo_a_unidade_e_recusado()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Só o Windows tem caminho relativo a uma unidade.");

        foreach (var caminho in new[] { "C:segredos", @"\segredos\k", @"/\servidor\share\k" })
        {
            Assert.Equal("INC2012", Yaml.UnicoErro(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: 'arquivo:" + caminho + "'")).Codigo);
        }
    }

    [Fact]
    public void Mensagens_e_locais_nunca_trazem_o_texto_escrito_em_YAML_aleatorio()
    {
        var sentinela = "SENT" + (char)0x0301 + "INELA";
        var pedacos = new[] { "a: ", sentinela, ": ", "\n", "  ", "{", "}", "[", "]", ", ", "\"", "'", "- ", "? ", "tabelas:\n", "dominios:\n", "  public.t:\n", "    colunas:\n", "x" };

        Gen.Int[0, pedacos.Length - 1].Select(indice => pedacos[indice]).Array[1, 40].Select(string.Concat).Sample(texto =>
        {
            try
            {
                ConfiguracaoIncognito.LerYaml(texto);
            }
            catch (ConfiguracaoInvalidaException excecao)
            {
                Assert.DoesNotContain(sentinela, excecao.ToString(), StringComparison.Ordinal);
                Assert.All(excecao.Erros, erro => Assert.DoesNotContain(sentinela, erro.ToString() + erro.Local, StringComparison.Ordinal));
            }
        }, iter: 3000);
    }

    [Fact]
    public void Linha_longa_com_prefixo_de_chave_e_varrida_sem_alocar_por_caractere()
    {
        var texto = Yaml.Com("# inc1_" + new string('A', 4 * 1024 * 1024) + "\n");
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var antes = GC.GetAllocatedBytesForCurrentThread();

        var erro = Yaml.UnicoErro(texto);

        Assert.Equal("INC2011", erro.Codigo);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - antes < 400_000_000, "Alocou demais.");
        Assert.True(cronometro.Elapsed < Yaml.LimiteDeTempo, $"Levou {cronometro.Elapsed}.");
    }

    [Fact]
    public void Local_so_traz_texto_onde_ele_e_estrutura()
    {
        var casos = new (string Texto, string Local)[]
        {
            ("'\"zqSENT conta 0001-9 ag 1234: abc\"': { a: 1, a: 2 }\n", "/?"),
            ("zqSENTabc123: { a: 1, a: 2 }\n", "/?"),
            ("zqsent.111-222.333-44: { a: 1, a: 2 }\n", "/?"),
            ("origem2: { a: 1, a: 2 }\n", "/?"),
            ("tabelas:\n  a.b.zqSENT: { a: 1, a: 2 }\n", "/tabelas/?"),
            ("tabelas:\n  public.t:\n    colunas:\n      c: { acao: manter, '\"zqSENT minha senha: Tr0ub4dor&3\"': x }\n", "/tabelas/public.t/colunas/c/?"),
            ("dominios:\n  cpf: { transformador: br.cpf@1 }\ntabelas:\n  public.t:\n    colunas:\n      c: { dominio: cpf, '\"zqSENT x\"': y }\n", "/tabelas/public.t/colunas/c/?"),
        };

        foreach (var (texto, local) in casos)
        {
            var erros = Yaml.Erros(Yaml.Com(texto));
            Assert.Contains(erros, erro => erro.Local == local);
            Assert.All(erros, erro => Assert.DoesNotContain("zqsent", erro.ToString() + erro.Local, StringComparison.OrdinalIgnoreCase));
        }

        var coluna = Yaml.Erros(Yaml.Com("tabelas:\n  public.t:\n    colunas:\n      c: { dominio: rg }\n"));
        Assert.Equal("/tabelas/public.t/colunas/c/dominio", Assert.Single(coluna).Local);
    }

    public static TheoryData<int> InvisiveisForaDoPlanoBasico() => [0xE0041, 0xE0020, 0xE0001, 0x1D173, 0x1BCA0, 0xE0100, 0xF0000, 0xFE0F, 0x034F, 0x2800, 0x180B, 0x17B4, 0xE000];

    [Theory]
    [MemberData(nameof(InvisiveisForaDoPlanoBasico))]
    public void Caractere_invisivel_ou_de_uso_privado_em_qualquer_plano_e_recusado(int codigo)
    {
        var caractere = char.ConvertFromUtf32(codigo);

        var erro = Yaml.UnicoErro(Yaml.Com("# comentario " + caractere + " fim\n"));

        Assert.Equal(("INC2002", 12, 14), (erro.Codigo, erro.Linha, erro.Coluna));
    }

    [Theory]
    [InlineData(0xE0020)]
    [InlineData(0xFE0F)]
    [InlineData(0x034F)]
    [InlineData(0x3164)]
    [InlineData(0x2800)]
    [InlineData(0xE0100)]
    public void Chave_partida_por_invisivel_continua_acusada(int codigo)
    {
        var separador = char.ConvertFromUtf32(codigo);
        var chave = string.Join(separador, Yaml.ChaveGerada().Chunk(4).Select(pedaco => new string(pedaco)));

        var erros = Yaml.Erros(Yaml.Com("# " + chave + "\n"));

        Assert.Contains(erros, erro => erro.Codigo == "INC2011");
        Assert.Contains(Yaml.Erros(Yaml.Com("# Pass" + separador + "word=x\n")), erro => erro.Codigo == "INC2011");
    }

    [Fact]
    public void Escape_de_caractere_fora_do_plano_basico_e_recusado()
    {
        Assert.Contains(Yaml.Erros(Yaml.Com("justificativa: \"ok\\U000E0041\"\n")), erro => erro.Codigo == "INC2002");
    }

    [Fact]
    public void Aviso_de_chave_sai_mesmo_com_o_maximo_de_outros_erros()
    {
        var invisiveis = string.Concat(Enumerable.Repeat("# x" + (char)0x200B + "\n", 150));

        var excecao = Yaml.Excecao(Yaml.Com(invisiveis + "# " + Yaml.ChaveGerada() + "\n"));

        Assert.Contains(excecao.Erros, erro => erro.Codigo == "INC2011");
        Assert.Contains("mais de 100 erros", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mais_de_um_milhao_de_nos_e_recusado()
    {
        var erro = Yaml.UnicoErro("x: [" + string.Join(',', Enumerable.Repeat('1', 1_000_000)) + "]\n");

        Assert.Equal("INC2004", erro.Codigo);
    }

    [Theory]
    [InlineData("CON:")]
    [InlineData("COM1:")]
    [InlineData("CONIN$:")]
    [InlineData("NUL:x")]
    [InlineData("NUL::$DATA")]
    [InlineData("com\u00b9:")]
    public void Nome_de_dispositivo_com_dois_pontos_e_recusado(string nome)
    {
        var caminho = Yaml.CaminhoAbsoluto(nome);

        Assert.Equal("INC2012", Yaml.UnicoErro(Yaml.Trocando("chave: ambiente:INCOGNITO_CHAVE", "chave: 'arquivo:" + caminho + "'")).Codigo);
    }
}
