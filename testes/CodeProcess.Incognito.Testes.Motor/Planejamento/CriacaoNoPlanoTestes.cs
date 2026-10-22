using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Motor.Configuracao;
using CodeProcess.Incognito.Testes.Motor.Dialetos;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.Motor.Planejamento.EsquemaSintetico;

namespace CodeProcess.Incognito.Testes.Motor.Planejamento;

/// <summary>
/// O tipo, os limites e os nulos de cada coluna tratada (INC3040 a INC3048), os avisos da regra (INC3590 e INC3591) e a
/// criação de validação numa sessão descartável: cada fábrica recebe um contexto novo, montado pela coluna do destino, e
/// o que uma fábrica de fora lança vira erro do plano só com o tipo.
/// </summary>
public sealed class CriacaoNoPlanoTestes
{
    private const string Sentinela = ProvedorDoPlanejar.Sentinela;

    [Fact]
    public async Task Transformador_proprio_registrado_aparece_no_plano_e_e_criado_com_a_coluna_real()
    {
        // A origem mede 50 caracteres; o destino, 41 bytes com até 4 por caractere: chegam 10 (o teto daria 11, a origem 50).
        var fabrica = new FabricaDeTeste();
        var origem = ComColunas(
            Texto("doc", 50),
            BancoFalso.Clonar(Inteiro("numero"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = -2_147_483_648, Maximo = 2_147_483_647 })),
            Inteiro("referencia"));
        var destino = ComColunas(
            BancoFalso.Clonar(
                Texto("doc", 41),
                (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 41, Unidade = UnidadeComprimento.Bytes }),
                (nameof(ColunaEsquema.Colacao), BancoFalso.Clonar(Padrao, (nameof(ColacaoEsquema.Nome), "do-destino")))),
            BancoFalso.Clonar(Inteiro("numero"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = -32_768, Maximo = 32_767 })),
            Texto("referencia", 20));
        var yaml = Tabela("      doc: { transformador: exemplo.gravador@1, derivarDe: referencia }\n      numero: { transformador: exemplo.gravador@1 }\n");

        var plano = await PlanejarAsync(yaml, origem, destino, Registro(fabrica));

        Assert.Equal(2, fabrica.Contextos.Count);
        Assert.Equal(2, fabrica.Contextos.Distinct().Count());
        var doc = fabrica.Contextos[0];
        Assert.Equal((TipoColuna.Texto, 10, "do-destino", TipoColuna.Inteiro), (doc.Tipo, doc.ComprimentoMaximo, doc.Colacao, doc.TipoReferencia));
        Assert.Equal(((long?)null, (long?)null), (doc.MenorInteiro, doc.MaiorInteiro));
        var numero = fabrica.Contextos[1];
        Assert.Equal((TipoColuna.Inteiro, -32_768L, 32_767L, (int?)null, (TipoColuna?)null), (numero.Tipo, numero.MenorInteiro, numero.MaiorInteiro, numero.ComprimentoMaximo, numero.TipoReferencia));
        Assert.Equal("homologacao", numero.Ambiente);

        var regra = plano.Tabelas[0].Colunas.Single(coluna => coluna.Nome == "doc").Regra!;
        Assert.Equal(("exemplo.gravador", 1, Injetividade.Garantida), (regra.Transformador, regra.Versao, regra.Injetividade));
        var tratadas = plano.Execucao.Unidades[0].Tratadas;
        Assert.Equal(new ContextoDaColuna(TipoColuna.Texto, 10, null, null, null, null, "do-destino", TipoColuna.Inteiro), tratadas.Single(tratada => tratada.Coluna == "doc").Contexto);
        Assert.Equal(new ContextoDaColuna(TipoColuna.Inteiro, null, -32_768, 32_767, null, null, null, null), tratadas.Single(tratada => tratada.Coluna == "numero").Contexto);
        Assert.Equal(2, fabrica.Descartes);
    }

    [Fact]
    public async Task O_contexto_decimal_leva_a_precisao_e_a_escala_do_destino_mesmo_estranhas()
    {
        var fabrica = new FabricaDeTeste();
        foreach (var (precisao, escala) in new (int, int?)[] { (5, -2), (2, 5), (28, null) })
        {
            fabrica.Contextos.Clear();
            var esquema = ComColunas(Decimal("valor", precisao, escala));

            await PlanejarAsync(Tabela("      valor: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica));

            var contexto = Assert.Single(fabrica.Contextos);
            Assert.Equal((TipoColuna.Decimal, (int?)precisao, escala), (contexto.Tipo, contexto.Precisao, contexto.Escala));
        }
    }

    [Fact]
    public async Task O_binario_recebe_o_limite_em_bytes_e_o_texto_sem_limite_recebe_nulo()
    {
        var fabrica = new FabricaDeTeste();
        var esquema = ComColunas(Binario("foto", 64), BancoFalso.Clonar(Texto("livre", 1), (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = null, Unidade = UnidadeComprimento.PontosDeCodigo })));

        await PlanejarAsync(Tabela("      foto: { transformador: exemplo.gravador@1 }\n      livre: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica));

        Assert.Equal([(TipoColuna.Binario, (int?)64), (TipoColuna.Texto, null)], fabrica.Contextos.Select(contexto => (contexto.Tipo, contexto.ComprimentoMaximo)));
    }

    [Theory]
    [InlineData(UnidadeComprimento.UnidadesUtf16, 7, 4, 7)]
    [InlineData(UnidadeComprimento.PontosDeCodigo, 7, 4, 7)]
    [InlineData(UnidadeComprimento.Bytes, 7, 4, 1)]
    [InlineData(UnidadeComprimento.Bytes, 7, 1, 7)]
    [InlineData(UnidadeComprimento.Bytes, 8, 2, 4)]
    public async Task O_limite_de_texto_vira_unidades_UTF16_que_cabem_com_certeza(UnidadeComprimento unidade, int valor, int bytesPorCaractere, int esperado)
    {
        var fabrica = new FabricaDeTeste();
        var esquema = ComBytesPorCaractere(ComColunas(BancoFalso.Clonar(Texto("doc", 1), (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = valor, Unidade = unidade }))), bytesPorCaractere);

        await PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica));

        Assert.Equal(esperado, Assert.Single(fabrica.Contextos).ComprimentoMaximo);
    }

    [Fact]
    public async Task Limite_em_bytes_que_nao_comporta_um_caractere_da_INC3043_e_sem_as_propriedades_conta_4()
    {
        var umByte = ComColunas(BancoFalso.Clonar(Texto("doc", 1), (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 3, Unidade = UnidadeComprimento.Bytes })));
        var semPropriedades = BancoFalso.Clonar(umByte, (nameof(EsquemaBanco.Propriedades), null));
        var fabrica = new FabricaDeTeste();

        var erro = Assert.Single(await RecusaAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), umByte, umByte, Registro(fabrica)));
        Assert.Equal(("INC3043", "loja.clientes", "doc"), (erro.Codigo, erro.Tabela!.ToString(), erro.Coluna!.ToString()));
        Assert.Contains("3 bytes, não comporta um caractere de até 4 bytes", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal("INC3043", Assert.Single(await RecusaAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), umByte, semPropriedades, Registro(fabrica))).Codigo);
        Assert.Empty(fabrica.Contextos);

        // Com um byte por caractere, os mesmos 3 bytes cabem.
        await PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), umByte, ComBytesPorCaractere(umByte, 1), Registro(fabrica));
        Assert.Equal(3, Assert.Single(fabrica.Contextos).ComprimentoMaximo);
    }

    [Theory]
    [InlineData(TipoColuna.Texto)]
    [InlineData(TipoColuna.Binario)]
    public async Task Comprimento_nao_medido_da_INC3044(TipoColuna tipo)
    {
        var coluna = BancoFalso.Clonar(tipo == TipoColuna.Texto ? Texto("doc", 1) : Binario("doc", 1), (nameof(ColunaEsquema.Comprimento), null));
        var esquema = ComColunas(coluna);
        var fabrica = new FabricaDeTeste();

        var erro = Assert.Single(await RecusaAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica)));

        Assert.Equal(("INC3044", "doc", "/tabelas/loja.clientes/colunas/doc"), (erro.Codigo, erro.Coluna!.ToString(), erro.Posicao!.Local));
        Assert.Empty(fabrica.Contextos);
    }

    [Fact]
    public async Task Faixa_ou_precisao_nao_medida_da_INC3047()
    {
        var semFaixa = ComColunas(BancoFalso.Clonar(Inteiro("numero"), (nameof(ColunaEsquema.Faixa), null)));
        var semPrecisao = ComColunas(Decimal("valor", null, null));
        var larga = ComColunas(Decimal("valor", 29, 0));
        var fabrica = new FabricaDeTeste();

        var erros = new[]
        {
            Assert.Single(await RecusaAsync(Tabela("      numero: { transformador: exemplo.gravador@1 }\n"), semFaixa, semFaixa, Registro(fabrica))),
            Assert.Single(await RecusaAsync(Tabela("      valor: { transformador: exemplo.gravador@1 }\n"), semPrecisao, semPrecisao, Registro(fabrica))),
            Assert.Single(await RecusaAsync(Tabela("      valor: { transformador: exemplo.gravador@1 }\n"), larga, larga, Registro(fabrica))),
        };

        Assert.Equal(["numero", "valor", "valor"], erros.Select(erro => erro.Coluna!.ToString()));
        Assert.All(erros, erro => Assert.Equal("INC3047", erro.Codigo));
        Assert.Contains("faixa da coluna inteira", erros[0].Mensagem, StringComparison.Ordinal);
        Assert.Contains("não declara a precisão no destino", erros[1].Mensagem, StringComparison.Ordinal);
        Assert.Contains("passa de 28 dígitos no destino", erros[2].Mensagem, StringComparison.Ordinal);
        Assert.Empty(fabrica.Contextos);
    }

    [Theory]
    [InlineData(29, 1, false)]
    [InlineData(28, -1, false)]
    [InlineData(27, -1, true)]
    [InlineData(28, 29, false)]
    [InlineData(28, 28, true)]
    [InlineData(5, -23, true)]
    [InlineData(5, -24, false)]
    public async Task O_decimal_que_passa_de_28_digitos_na_escala_ou_na_parte_inteira_da_INC3047(int precisao, int escala, bool cabe)
    {
        // numeric(28,-1) guarda até 10^29 e numeric(28,29) tem 29 casas: nenhum dos dois cabe no decimal do .NET.
        var fabrica = new FabricaDeTeste();
        var esquema = ComColunas(Decimal("valor", precisao, escala));
        var yaml = Tabela("      valor: { transformador: exemplo.gravador@1 }\n");

        if (cabe)
        {
            await PlanejarAsync(yaml, esquema, esquema, Registro(fabrica));
            Assert.Equal((int?)escala, Assert.Single(fabrica.Contextos).Escala);
        }
        else
        {
            Assert.Equal("INC3047", Assert.Single(await RecusaAsync(yaml, esquema, esquema, Registro(fabrica))).Codigo);
            Assert.Empty(fabrica.Contextos);
        }
    }

    [Fact]
    public async Task CPF_em_integer_e_recusado_pela_faixa_e_em_bigint_passa()
    {
        var integer = ComColunas(BancoFalso.Clonar(Inteiro("documento"), (nameof(ColunaEsquema.Faixa), new FaixaInteira { Minimo = -2_147_483_648, Maximo = 2_147_483_647 })));
        var bigint = ComColunas(Inteiro("documento"));

        var erro = Assert.Single(await RecusaAsync(Tabela("      documento: { transformador: br.cpf@1 }\n"), integer, integer));
        await PlanejarAsync(Tabela("      documento: { transformador: br.cpf@1 }\n"), bigint, bigint);

        Assert.Equal(("INC3045", "documento"), (erro.Codigo, erro.Coluna!.ToString()));
        Assert.Contains("O transformador br.cpf@1 recusou a coluna: A coluna inteira não comporta 99999999999", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coluna_de_texto_curta_demais_para_o_pseudonimo_da_INC3045_com_a_mensagem_da_fabrica()
    {
        var curta = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 10, Unidade = UnidadeComprimento.PontosDeCodigo }));

        var erro = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1 }\n"), curta, curta));

        Assert.Equal(("INC3045", "cpf"), (erro.Codigo, erro.Coluna!.ToString()));
        Assert.Contains("menos de 11 caracteres", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.clientes/colunas/cpf", erro.Posicao!.Local);
    }

    [Fact]
    public async Task A_recusa_da_fabrica_sai_escapada_e_cortada()
    {
        var fabrica = new FabricaDeTeste { NoCriar = () => new RegraInvalidaException("recusada \u001B[2J \u202E " + new string('x', 600)) };
        var esquema = ComColunas(Texto("doc", 20));

        var erro = Assert.Single(await RecusaAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica)));

        Assert.Equal("INC3045", erro.Codigo);
        Assert.Contains("recusada \\u001B[2J \\u202E xxx", erro.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001B", erro.Mensagem, StringComparison.Ordinal);
        Assert.EndsWith("…", erro.Mensagem, StringComparison.Ordinal);
        Assert.Equal(0, fabrica.Descartes);
    }

    public static TheoryData<string, string, string> Falhas => new()
    {
        { "criar", "criar", nameof(InvalidOperationException) },
        { "nome", "criar", nameof(InvalidOperationException) },
        { "nome-depois", "criar", nameof(InvalidOperationException) },
        { "descarte", "descartar", nameof(NotSupportedException) },
        { "incognito", "criar", nameof(IncognitoException) },
        { "cancelamento", "criar", nameof(OperationCanceledException) },
        { "mudou", "criar", nameof(InvalidOperationException) },
        { "mudou-e-descarte-falha", "criar", nameof(InvalidOperationException) },
        { "outro-nome", "criar", nameof(InvalidOperationException) },
    };

    [Theory]
    [MemberData(nameof(Falhas))]
    public async Task Excecao_inesperada_de_fabrica_de_fora_vira_INC3046_so_com_o_tipo_e_o_criado_e_descartado_uma_vez(string falha, string operacao, string tipo)
    {
        // "nome-depois": o getter passa na conferência do núcleo e lança na do registro do motor.
        var leiturasDoNome = 0;
        var fabrica = new FabricaDeTeste
        {
            NoCriar = () => falha switch
            {
                "criar" => new InvalidOperationException(Sentinela),
                "incognito" => new IncognitoException("INC3101", Sentinela),
                "cancelamento" => new OperationCanceledException(Sentinela, new InvalidOperationException(Sentinela)),
                _ => null,
            },
            NoNome = () => falha == "nome" || (falha == "nome-depois" && ++leiturasDoNome > 1) ? new InvalidOperationException(Sentinela) : null,
            NoDescarte = () => falha is "descarte" or "mudou-e-descarte-falha" ? new NotSupportedException(Sentinela) : null,
            NomeCriado = falha == "outro-nome" ? "exemplo.outro" : null,
        };
        var registro = Registro(fabrica);
        if (falha.StartsWith("mudou", StringComparison.Ordinal))
        {
            // A fábrica muda de nome depois do registro e cria pelo nome novo: o núcleo aceita, o registro não.
            fabrica.NomeAtual = "exemplo.mudado";
        }

        var esquema = ComColunas(Texto("doc", 20));

        var recusa = Assert.IsType<PlanoRecusadoException>(
            await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, registro)),
            exactMatch: true);
        var erro = Assert.Single(recusa.Erros);

        Assert.Equal(("INC3046", "loja.clientes", "doc"), (erro.Codigo, erro.Tabela!.ToString(), erro.Coluna!.ToString()));
        Assert.Equal($"A fábrica exemplo.gravador@1 falhou ao {operacao} o transformador da coluna ({tipo}): corrija a fábrica ou fale com quem a mantém.", erro.Mensagem);
        DialetoProtegidoTestes.SemVazamento(recusa);
        Assert.Equal(falha is "criar" or "incognito" or "cancelamento" ? 0 : 1, fabrica.Descartes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Imita a falta de memória de uma fábrica de fora.")]
    public async Task A_falta_de_memoria_de_fabrica_de_fora_sobe_nova_sem_a_mensagem(bool noDescarte)
    {
        Exception? SemMemoria() => new OutOfMemoryException(Sentinela, new InvalidOperationException(Sentinela));
        var fabrica = noDescarte ? new FabricaDeTeste { NoDescarte = SemMemoria } : new FabricaDeTeste { NoCriar = SemMemoria };
        var esquema = ComColunas(Texto("doc", 20));

        var excecao = await Assert.ThrowsAsync<OutOfMemoryException>(() => PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica)));

        Assert.Null(excecao.InnerException);
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task O_cancelamento_pedido_durante_a_criacao_sobe_como_cancelamento_sem_a_interna(bool noDescarte)
    {
        using var fonte = new CancellationTokenSource();
        Exception? Cancelar()
        {
            fonte.Cancel();
            return new OperationCanceledException(Sentinela, new InvalidOperationException(Sentinela));
        }

        var fabrica = noDescarte ? new FabricaDeTeste { NoDescarte = Cancelar } : new FabricaDeTeste { NoCriar = Cancelar };
        var esquema = ComColunas(Texto("doc", 20));

        var excecao = await Assert.ThrowsAsync<OperationCanceledException>(
            () => PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica), cancelamento: fonte.Token));

        Assert.Equal(fonte.Token, excecao.CancellationToken);
        Assert.Null(excecao.InnerException);
        Assert.DoesNotContain(Sentinela, excecao.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task O_cancelamento_pedido_entre_duas_colunas_para_antes_da_proxima_criacao()
    {
        // A fábrica cancela e cria normalmente: a próxima coluna já não é criada.
        using var fonte = new CancellationTokenSource();
        var fabrica = new FabricaDeTeste
        {
            NoCriar = () =>
            {
                fonte.Cancel();
                return null;
            },
        };
        var esquema = ComColunas(Texto("doc", 20), Texto("outro", 20));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n      outro: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, Registro(fabrica), cancelamento: fonte.Token));

        Assert.Single(fabrica.Contextos);
    }

    [Fact]
    public void A_excecao_de_uma_embutida_sobe_e_a_mesma_de_fora_vira_INC3046()
    {
        // Na sessão já descartada, a embutida lança ObjectDisposedException, que é defeito e sobe; a mesma exceção de uma
        // fábrica de fora vira erro só com o tipo.
        var fabrica = new FabricaDeTeste { NoCriar = () => new ObjectDisposedException(Sentinela) };
        var registro = Registro(fabrica);
        Assert.True(registro.TentarObter("br.cpf", 1, out var cpf));
        Assert.True(registro.TentarObter("exemplo.gravador", 1, out var deFora));
        var contexto = new ContextoDaColuna(TipoColuna.Texto, 20, null, null, null, null, null, null);
        var criacao = new CriacaoNoPlano(ChaveIncognito.Gerar, "homologacao", [], TestContext.Current.CancellationToken);
        Assert.Null(criacao.Criar(cpf, new RegraTransformacao(new DominioPseudonimo("cpf", 1)), contexto));
        criacao.Dispose();

        Assert.Throws<ObjectDisposedException>(() => criacao.Criar(cpf, new RegraTransformacao(new DominioPseudonimo("cpf", 1)), contexto));
        Assert.Equal("INC3046", criacao.Criar(deFora, new RegraTransformacao(new DominioPseudonimo("exemplo.gravador", 1)), contexto)?.Codigo);
    }

    [Fact]
    public void O_contexto_que_se_contradiz_e_defeito_do_motor_qualquer_que_seja_a_fabrica()
    {
        // Conferido antes da fábrica: a recusa do núcleo nunca vira erro de uma fábrica de fora.
        var fabrica = new FabricaDeTeste();
        var registro = Registro(fabrica);
        Assert.True(registro.TentarObter("br.cpf", 1, out var cpf));
        Assert.True(registro.TentarObter("exemplo.gravador", 1, out var deFora));
        using var criacao = new CriacaoNoPlano(ChaveIncognito.Gerar, "homologacao", [], TestContext.Current.CancellationToken);
        var valido = new ContextoDaColuna(TipoColuna.Inteiro, null, long.MinValue, long.MaxValue, null, null, null, null);

        foreach (var contradito in new[] { valido with { MenorInteiro = 5, MaiorInteiro = 5 }, valido with { Escala = 0 }, valido with { Precisao = 0 }, valido with { ComprimentoMaximo = 0 } })
        {
            Assert.Throws<InvalidOperationException>(() => criacao.Criar(cpf, new RegraTransformacao(new DominioPseudonimo("cpf", 1)), contradito));
            Assert.Throws<InvalidOperationException>(() => criacao.Criar(deFora, new RegraTransformacao(new DominioPseudonimo("exemplo.gravador", 1)), contradito));
        }

        Assert.Empty(fabrica.Contextos);
        Assert.Null(criacao.Criar(cpf, new RegraTransformacao(new DominioPseudonimo("cpf", 1)), valido));
    }

    [Theory]
    [InlineData("plano")]
    [InlineData("recusa")]
    [InlineData("falha")]
    [InlineData("cancelamento")]
    [InlineData("memoria")]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Imita a falta de memória de uma fábrica de fora.")]
    public async Task A_sessao_descartavel_e_a_chave_dela_sao_descartadas_em_todo_desfecho_e_a_chave_nunca_sai(string desfecho)
    {
        // A chave canário: o texto dela, os bytes em hexadecimal, base64 e decimal nunca aparecem no plano nem no erro.
        var bytes = Enumerable.Range(1, 32).Select(numero => (byte)numero).ToArray();
        var texto = "inc1_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string[] formas = [texto, texto[5..], Convert.ToHexString(bytes), Convert.ToHexStringLower(bytes), Convert.ToBase64String(bytes), string.Join(",", bytes), string.Join(", ", bytes)];
        var chaves = new List<ChaveIncognito>();
        ChaveIncognito Canario()
        {
            var chave = ChaveIncognito.Ler(texto);
            chaves.Add(chave);
            return chave;
        }

        using var fonte = new CancellationTokenSource();
        var fabrica = new FabricaDeTeste
        {
            NoCriar = () => desfecho switch
            {
                "recusa" => new RegraInvalidaException("recusada"),
                "falha" => new InvalidOperationException(Sentinela),
                "cancelamento" => Cancelado(fonte),
                "memoria" => new OutOfMemoryException(Sentinela),
                _ => null,
            },
        };
        var esquema = ComColunas(Texto("doc", 20));
        var yaml = Yaml.Minimo + Tabela("      cpf: { transformador: br.cpf@1 }\n      doc: { transformador: exemplo.gravador@1 }\n");

        var textos = new List<string>();
        try
        {
            var plano = await DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(esquema, esquema), yaml: yaml, cancelamento: fonte.Token, registro: Registro(fabrica), gerarChave: Canario);
            Assert.Equal("plano", desfecho);
            var alcancaveis = Alcancaveis(plano);
            Assert.DoesNotContain(alcancaveis, objeto => objeto is ChaveIncognito or SessaoTransformacao or Transformador or ContextoCriacao);
            Assert.Contains(alcancaveis, objeto => objeto is ContextoDaColuna);
            textos.AddRange(alcancaveis.OfType<string>());
            textos.Add(plano.ToString());
        }
        catch (Exception excecao) when (desfecho != "plano")
        {
            for (var atual = excecao; atual is not null; atual = atual.InnerException)
            {
                textos.Add(atual.ToString());
            }
        }

        // A sessão vista pela fábrica (a mesma da embutida) não cria mais nada: foi descartada, e a chave também.
        var sessao = (SessaoTransformacao)typeof(ContextoCriacao).GetProperty("Sessao", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Assert.Single(fabrica.Contextos))!;
        // A sessão lança antes de olhar a chave: o nome do objeto distingue a sessão descartada da chave descartada.
        var descartada = Assert.Throws<ObjectDisposedException>(() => TransformadoresEmbutidos.Fabricas[0].Criar(new ContextoCriacao(new RegraTransformacao(new DominioPseudonimo("cpf", 1)), TipoColuna.Texto, sessao)));
        Assert.Equal(typeof(SessaoTransformacao).FullName, descartada.ObjectName);
        Assert.Throws<ObjectDisposedException>(() => new SessaoTransformacao(Assert.Single(chaves), "homologacao"));
        Assert.NotEmpty(textos);
        Assert.All(textos, lido => Assert.All(formas, forma => Assert.DoesNotContain(forma, lido, StringComparison.OrdinalIgnoreCase)));
    }

    private static OperationCanceledException Cancelado(CancellationTokenSource fonte)
    {
        fonte.Cancel();
        return new OperationCanceledException(Sentinela);
    }

    [Fact]
    public async Task Tipo_fora_dos_aceitos_da_INC3040_e_sem_forma_canonica_da_INC3041()
    {
        var numerico = ComColunas(Decimal("documento", 11, 0));
        var json = ComColunas(BancoFalso.Clonar(Texto("dados", 1), (nameof(ColunaEsquema.Tipo), null), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva), (nameof(ColunaEsquema.TipoNativo), "json")));
        var xml = ComColunas(BancoFalso.Clonar(Texto("dados", 1), (nameof(ColunaEsquema.Tipo), null), (nameof(ColunaEsquema.Situacao), SituacaoTipo.ComRessalva), (nameof(ColunaEsquema.TipoNativo), "xml")));
        var semTipo = ComColunas(BancoFalso.Clonar(Texto("dados", 1), (nameof(ColunaEsquema.Tipo), null)));

        var tipo = Assert.Single(await RecusaAsync(Tabela("      documento: { transformador: br.cpf@1 }\n"), numerico, numerico));
        var ressalva = Assert.Single(await RecusaAsync(Tabela("      dados: { transformador: texto.substituir@1 }\n"), json, json));
        var deXml = Assert.Single(await RecusaAsync(Tabela("      dados: { transformador: texto.substituir@1 }\n"), xml, xml));
        var nulo = Assert.Single(await RecusaAsync(Tabela("      dados: { transformador: texto.substituir@1 }\n"), semTipo, semTipo));

        Assert.Equal(("INC3040", "documento"), (tipo.Codigo, tipo.Coluna!.ToString()));
        Assert.Contains("não aceita coluna do tipo Decimal (aceita Texto, Inteiro)", tipo.Mensagem, StringComparison.Ordinal);
        Assert.Equal(("INC3041", "INC3041", "INC3041"), (ressalva.Codigo, deXml.Codigo, nulo.Codigo));
        Assert.Contains("(aceito com ressalva, como json, xml", ressalva.Mensagem, StringComparison.Ordinal);
        Assert.Contains("Use 'acao: manter'", ressalva.Mensagem, StringComparison.Ordinal);
        Assert.Contains("(sem representação para o transformador)", nulo.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("json", nulo.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regra_em_coluna_gerada_de_qualquer_lado_da_INC3042_e_a_identity_nao()
    {
        var gerada = (nameof(ColunaEsquema.Geracao), (object?)GeracaoColuna.Armazenada);
        var naOrigem = TrocarColuna(Coerente(), "clientes", "cpf", gerada);
        var fabrica = new FabricaDeTeste();

        var daOrigem = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1 }\n"), naOrigem, Coerente()));
        var doDestino = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1 }\n"), Coerente(), naOrigem));
        var identidade = TrocarColuna(Coerente(), "clientes", "id", (nameof(ColunaEsquema.Identidade), IdentidadeColuna.Sempre));
        await PlanejarAsync(Tabela("      id: { transformador: exemplo.gravador@1 }\n"), identidade, identidade, Registro(fabrica));

        Assert.Equal(("INC3042", "INC3042"), (daOrigem.Codigo, doDestino.Codigo));
        Assert.Contains("gerada na origem", daOrigem.Mensagem, StringComparison.Ordinal);
        Assert.Contains("gerada no destino", doDestino.Mensagem, StringComparison.Ordinal);
        Assert.Single(fabrica.Contextos);
    }

    [Fact]
    public async Task A_INC3042_barra_o_resto_e_vale_tambem_para_a_gerada_que_nao_existe_no_destino()
    {
        // Gerada, obrigatória e curta: só o 3042 sai, sem o 3048 nem o 3045.
        var gerada = TrocarColuna(
            Coerente(),
            "clientes",
            "cpf",
            (nameof(ColunaEsquema.Geracao), GeracaoColuna.Armazenada),
            (nameof(ColunaEsquema.Anulavel), false),
            (nameof(ColunaEsquema.Comprimento), new LimiteComprimento { Valor = 5, Unidade = UnidadeComprimento.PontosDeCodigo }));
        var unica = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1, entradaMalformada: anular }\n"), gerada, gerada));
        Assert.Equal("INC3042", unica.Codigo);

        // A gerada da origem sem par no destino não é copiada, mas a regra nela é recusada, nunca ignorada.
        var semPar = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Where(coluna => coluna.Nome != "cpf").ToArray())));
        var erro = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1 }\n"), gerada, semPar));
        Assert.Equal(("INC3042", "cpf", "/tabelas/loja.clientes/colunas/cpf"), (erro.Codigo, erro.Coluna!.ToString(), erro.Posicao!.Local));
        Assert.Contains("gerada na origem", erro.Mensagem, StringComparison.Ordinal);

        // Sem regra, a mesma gerada sem par segue copiável, como antes.
        await PlanejarAsync("", gerada, semPar);
    }

    [Fact]
    public async Task Politica_que_produz_NULL_em_coluna_obrigatoria_da_INC3048_apontando_de_onde_ela_vem()
    {
        var obrigatoria = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Anulavel), false));

        var naColuna = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1, entradaMalformada: anular }\n"), obrigatoria, obrigatoria));
        var global = Assert.Single(await RecusaAsync("politica:\n  entradaInvalida: anular\n" + Tabela("      cpf: { transformador: br.cpf@1 }\n"), obrigatoria, obrigatoria));
        var noDominio = Assert.Single(await RecusaAsync(
            "dominios:\n  cpf-loja: { transformador: br.cpf@1, entradaInvalida: anular }\n" + Tabela("      cpf: { dominio: cpf-loja }\n"), obrigatoria, obrigatoria));

        Assert.All(new[] { naColuna, global, noDominio }, erro => Assert.Equal(("INC3048", "cpf"), (erro.Codigo, erro.Coluna!.ToString())));
        Assert.Contains("'entradaMalformada: anular' (escrita na coluna)", naColuna.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.clientes/colunas/cpf", naColuna.Posicao!.Local);
        Assert.Contains("'entradaInvalida: anular' (da política global, em 'politica')", global.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.clientes/colunas/cpf", global.Posicao!.Local);
        Assert.Contains("(escrita no domínio cpf-loja)", noDominio.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/dominios/cpf-loja", noDominio.Posicao!.Local);
    }

    [Fact]
    public async Task So_as_politicas_que_o_transformador_aplica_contam_na_INC3048()
    {
        var obrigatorias = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[]
        {
            Inteiro("id", anulavel: false),
            BancoFalso.Clonar(Texto("cpf", 11), (nameof(ColunaEsquema.Anulavel), false)),
            BancoFalso.Clonar(Texto("nome", 100), (nameof(ColunaEsquema.Anulavel), false)),
            BancoFalso.Clonar(Texto("pix", 100), (nameof(ColunaEsquema.Anulavel), false)),
        })));

        // O e-mail ignora a entradaInvalida, até a global; a chave Pix ignora a dela, mas aplica a dos domínios de documento
        // que cita (a global chega aos domínios implícitos cpf e cnpj).
        await PlanejarAsync("politica:\n  entradaInvalida: anular\n" + Tabela("      nome: { transformador: email@1 }\n"), obrigatorias, obrigatorias);
        await PlanejarAsync(Tabela("      pix: { transformador: br.chave-pix@1, entradaInvalida: anular }\n"), obrigatorias, obrigatorias);
        var global = await RecusaAsync("politica:\n  entradaInvalida: anular\n" + Tabela("      pix: { transformador: br.chave-pix@1 }\n"), obrigatorias, obrigatorias);
        Assert.Equal(
            ["do domínio cnpj, citado em 'dominioCnpj' (da política global", "do domínio cpf, citado em 'dominioCpf' (da política global"],
            global.Select(erro => erro.Mensagem[erro.Mensagem.IndexOf("do domínio", StringComparison.Ordinal)..erro.Mensagem.IndexOf(", em 'politica'", StringComparison.Ordinal)]));
        var pix = Assert.Single(await RecusaAsync(
            "dominios:\n  cpf-loja: { transformador: br.cpf@1, entradaInvalida: anular }\n" + Tabela("      pix: { transformador: br.chave-pix@1, dominioCpf: cpf-loja }\n"), obrigatorias, obrigatorias));

        Assert.Equal(("INC3048", "pix"), (pix.Codigo, pix.Coluna!.ToString()));
        Assert.Contains("'entradaInvalida: anular' do domínio cpf-loja, citado em 'dominioCpf' (escrita no domínio cpf-loja)", pix.Mensagem, StringComparison.Ordinal);
        Assert.Contains("troque a política do domínio citado, ou cite outro domínio", pix.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/dominios/cpf-loja", pix.Posicao!.Local);

        // O texto.substituir troca todo texto e nunca anula; a chave de DF-e mantém o emitente pelo deslocamento, e a política
        // do domínio citado não se aplica a ela.
        await PlanejarAsync("politica:\n  entradaMalformada: anular\n" + Tabela("      nome: { transformador: texto.substituir@1 }\n"), obrigatorias, obrigatorias);
        await PlanejarAsync(
            "dominios:\n  cpf-loja: { transformador: br.cpf@1, entradaInvalida: anular }\n" + Tabela("      pix: { transformador: br.chave-dfe@1, dominioCpf: cpf-loja }\n"), obrigatorias, obrigatorias);
        var naColuna = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: br.cpf@1, entradaInvalida: anular }\n"), obrigatorias, obrigatorias));
        Assert.Contains("e a coluna não aceita NULL no destino: a gravação falharia; troque a política onde ela está escrita, ou escreva outra na coluna.", naColuna.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void As_embutidas_que_ignoram_a_entradaMalformada_sao_as_que_o_nucleo_declara()
    {
        var propriedade = TransformadoresEmbutidos.Fabricas[0].GetType().BaseType!.GetProperty("UsaEntradaMalformada", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var doNucleo = TransformadoresEmbutidos.Fabricas.Where(fabrica => !(bool)propriedade.GetValue(fabrica)!).Select(fabrica => fabrica.Nome).Order(StringComparer.Ordinal);

        Assert.Equal(doNucleo, PoliticasQueAnulam.IgnoramEntradaMalformada.Order(StringComparer.Ordinal));
        Assert.Equal(["texto.substituir"], doNucleo);
    }

    [Fact]
    public async Task A_chave_primaria_conta_como_obrigatoria_e_a_coluna_anulavel_fora_dela_nao()
    {
        var naChave = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(
            tabela,
            (nameof(TabelaEsquema.ChavePrimaria), (string[])["id", "cpf"]),
            (nameof(TabelaEsquema.IndicesUnicos), new[] { new IndiceUnico { Nome = "pk_clientes", Primario = true, Elementos = [new ElementoIndice { Coluna = "id" }, new ElementoIndice { Coluna = "cpf" }], NulosDistintos = true } })));
        var fabrica = new FabricaDeTeste();

        var erro = Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: exemplo.gravador@1, entradaInvalida: anular }\n"), naChave, naChave, Registro(fabrica)));
        await PlanejarAsync(Tabela("      cpf: { transformador: exemplo.gravador@1, entradaInvalida: anular, entradaMalformada: anular }\n"), Coerente(), Coerente(), Registro(fabrica));

        Assert.Equal(("INC3048", "cpf"), (erro.Codigo, erro.Coluna!.ToString()));

        // Vale o destino, onde a gravação acontece: obrigatória ou na chave só na origem não recusa.
        var obrigatoria = TrocarColuna(Coerente(), "clientes", "cpf", (nameof(ColunaEsquema.Anulavel), false));
        await PlanejarAsync(Tabela("      cpf: { transformador: exemplo.gravador@1, entradaInvalida: anular }\n"), obrigatoria, Coerente(), Registro(fabrica));
        await PlanejarAsync(Tabela("      cpf: { transformador: exemplo.gravador@1, entradaInvalida: anular }\n"), naChave, Coerente(), Registro(fabrica));
        Assert.Equal("INC3048", Assert.Single(await RecusaAsync(Tabela("      cpf: { transformador: exemplo.gravador@1, entradaInvalida: anular }\n"), Coerente(), obrigatoria, Registro(fabrica))).Codigo);
    }

    [Fact]
    public async Task A_politica_do_dominio_citado_e_a_global_nunca_sao_atribuidas_a_quem_nao_as_escreveu()
    {
        var obrigatorias = Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), new[]
        {
            Inteiro("id", anulavel: false),
            BancoFalso.Clonar(Texto("cpf", 11), (nameof(ColunaEsquema.Anulavel), false)),
            Texto("nome", 100),
            BancoFalso.Clonar(Texto("pix", 100), (nameof(ColunaEsquema.Anulavel), false)),
        })));

        // A coluna Pix escreve a política dela, que não se aplica; a que anula é a do domínio citado.
        var citado = Assert.Single(await RecusaAsync(
            "dominios:\n  cpf-loja: { transformador: br.cpf@1, entradaInvalida: anular }\n" + Tabela("      pix: { transformador: br.chave-pix@1, dominioCpf: cpf-loja, entradaInvalida: anular }\n"),
            obrigatorias,
            obrigatorias));

        // O domínio declarado sem a política herda a global: o erro diz que ela vem da raiz, não do domínio.
        var herdada = Assert.Single(await RecusaAsync(
            "politica:\n  entradaInvalida: anular\ndominios:\n  cpf-loja: { transformador: br.cpf@1 }\n" + Tabela("      cpf: { dominio: cpf-loja }\n"), obrigatorias, obrigatorias));

        Assert.Contains("citado em 'dominioCpf' (escrita no domínio cpf-loja)", citado.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/dominios/cpf-loja", citado.Posicao!.Local);
        Assert.Contains("(da política global, em 'politica')", herdada.Mensagem, StringComparison.Ordinal);
        Assert.Equal("/tabelas/loja.clientes/colunas/cpf", herdada.Posicao!.Local);
    }

    [Fact]
    public void As_embutidas_que_usam_a_entradaInvalida_sao_as_que_o_nucleo_declara()
    {
        // O motor não enxerga a propriedade protegida do núcleo: a lista dele é conferida contra ela.
        var propriedade = TransformadoresEmbutidos.Fabricas[0].GetType().BaseType!.GetProperty("UsaEntradaInvalida", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var doNucleo = TransformadoresEmbutidos.Fabricas.Where(fabrica => (bool)propriedade.GetValue(fabrica)!).Select(fabrica => fabrica.Nome).Order(StringComparer.Ordinal);

        Assert.Equal(doNucleo, PoliticasQueAnulam.UsamEntradaInvalida.Order(StringComparer.Ordinal));
        Assert.Contains(PoliticasQueAnulam.ChavePix, TransformadoresEmbutidos.Fabricas.Select(fabrica => fabrica.Nome));
        Assert.DoesNotContain(PoliticasQueAnulam.ChavePix, (IReadOnlySet<string>)PoliticasQueAnulam.UsamEntradaInvalida);
    }

    [Fact]
    public async Task Versao_mais_nova_registrada_da_o_aviso_INC3590()
    {
        var registro = Registro(new FabricaDeTeste(), new FabricaDeTeste(versao: 2));
        var esquema = ComColunas(Texto("doc", 20));

        var antiga = await PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@1 }\n"), esquema, esquema, registro);
        var nova = await PlanejarAsync(Tabela("      doc: { transformador: exemplo.gravador@2 }\n"), esquema, esquema, registro);

        var aviso = Assert.Single(antiga.Avisos);
        Assert.Equal(("INC3590", "doc", "/tabelas/loja.clientes/colunas/doc"), (aviso.Codigo, aviso.Coluna!.ToString(), aviso.Posicao!.Local));
        Assert.Contains("(@2); a regra usa a @1", aviso.Mensagem, StringComparison.Ordinal);
        Assert.Empty(nova.Avisos);

        // A versão escrita num domínio declarado: um aviso só, na declaração, por mais colunas que o usem.
        var duas = ComColunas(Texto("doc", 20), Texto("outro", 20));
        var porDominio = await PlanejarAsync(
            "dominios:\n  gravados: { transformador: exemplo.gravador@1 }\n" + Tabela("      doc: { dominio: gravados }\n      outro: { dominio: gravados }\n"), duas, duas, registro);
        var doDominio = Assert.Single(porDominio.Avisos);
        Assert.Equal(("INC3590", (NomeTabela?)null, "/dominios/gravados"), (doDominio.Codigo, doDominio.Tabela, doDominio.Posicao!.Local));
        Assert.Contains("Ela vem do domínio gravados.", doDominio.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entrada_malformada_preservada_da_o_aviso_INC3591_apontando_de_onde_ela_vem()
    {
        var preservada = await PlanejarAsync(Tabela("      cpf: { transformador: br.cpf@1, entradaMalformada: preservar }\n"));
        var falhar = await PlanejarAsync(Tabela("      cpf: { transformador: br.cpf@1, entradaMalformada: falhar }\n"));
        var global = await PlanejarAsync("politica:\n  entradaMalformada: preservar\n" + Tabela("      cpf: { transformador: br.cpf@1 }\n"));
        var noDominio = await PlanejarAsync("dominios:\n  cpf-loja: { transformador: br.cpf@1, entradaMalformada: preservar }\n" + Tabela("      cpf: { dominio: cpf-loja }\n"));
        var substituido = await PlanejarAsync("politica:\n  entradaMalformada: preservar\n" + Tabela("      nome: { transformador: texto.substituir@1 }\n"));

        var aviso = Assert.Single(preservada.Avisos);
        Assert.Equal(("INC3591", "cpf", "/tabelas/loja.clientes/colunas/cpf"), (aviso.Codigo, aviso.Coluna!.ToString(), aviso.Posicao!.Local));
        Assert.Contains("(escrita na coluna)", aviso.Mensagem, StringComparison.Ordinal);
        Assert.Empty(falhar.Avisos);
        var daRaiz = Assert.Single(global.Avisos);
        Assert.Equal(("INC3591", "/tabelas/loja.clientes/colunas/cpf"), (daRaiz.Codigo, daRaiz.Posicao!.Local));
        Assert.Contains("(da política global, em 'politica')", daRaiz.Mensagem, StringComparison.Ordinal);
        var doDominio = Assert.Single(noDominio.Avisos);
        Assert.Equal(("INC3591", "cpf", "/dominios/cpf-loja"), (doDominio.Codigo, doDominio.Coluna!.ToString(), doDominio.Posicao!.Local));
        Assert.Empty(substituido.Avisos);
    }

    private static string Tabela(string colunas) => "tabelas:\n  loja.clientes:\n    colunas:\n" + colunas;

    private static Task<Plano> PlanejarAsync(
        string yaml, EsquemaBanco? origem = null, EsquemaBanco? destino = null, RegistroTransformadores? registro = null, CancellationToken? cancelamento = null) =>
        DialetoProtegidoTestes.PlanejarAsync(new ProvedorDoPlanejar(origem, destino), yaml: Yaml.Minimo + yaml, cancelamento: cancelamento, registro: registro);

    private static async Task<IReadOnlyList<ErroPlano>> RecusaAsync(string yaml, EsquemaBanco? origem = null, EsquemaBanco? destino = null, RegistroTransformadores? registro = null) =>
        Assert.IsType<PlanoRecusadoException>(await Assert.ThrowsAnyAsync<Exception>(() => PlanejarAsync(yaml, origem, destino, registro)), exactMatch: true).Erros;

    private static RegistroTransformadores Registro(params FabricaTransformador[] fabricas)
    {
        var registro = new RegistroTransformadores();
        foreach (var fabrica in fabricas)
        {
            registro.Adicionar(fabrica);
        }

        return registro;
    }

    // A tabela clientes com as colunas acrescentadas.
    private static EsquemaBanco ComColunas(params ColunaEsquema[] colunas) =>
        Trocar(Coerente(), "clientes", tabela => BancoFalso.Clonar(tabela, (nameof(TabelaEsquema.Colunas), tabela.Colunas.Concat(colunas).ToArray())));

    private static EsquemaBanco ComBytesPorCaractere(EsquemaBanco esquema, int bytes) =>
        BancoFalso.Clonar(
            esquema,
            (nameof(EsquemaBanco.Propriedades), BancoFalso.Clonar(esquema.Propriedades!, (nameof(PropriedadesBanco.Codificacao), BancoFalso.Clonar(esquema.Propriedades!.Codificacao, (nameof(CodificacaoTexto.BytesPorCaractereMaximo), bytes))))));

    private static ColunaEsquema Decimal(string nome, int? precisao, int? escala) => new()
    {
        Nome = nome,
        TipoNativo = "numeric",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Decimal,
        Anulavel = true,
        Precisao = precisao,
        Escala = escala,
        RestritaPeloTipo = false,
    };

    private static ColunaEsquema Binario(string nome, int comprimento) => new()
    {
        Nome = nome,
        TipoNativo = "bytea",
        Situacao = SituacaoTipo.Suportado,
        Tipo = TipoColuna.Binario,
        Anulavel = true,
        Comprimento = new LimiteComprimento { Valor = comprimento, Unidade = UnidadeComprimento.Bytes },
        RestritaPeloTipo = false,
    };

    // Todo objeto e todo texto alcançáveis pelos campos, públicos ou não, dos tipos do projeto e pelas coleções, como no teste
    // da string de conexão.
    private static List<object> Alcancaveis(object raiz)
    {
        var vistos = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pendentes = new Stack<object>([raiz]);
        while (pendentes.TryPop(out var atual))
        {
            // Os textos entram na lista (para as buscas), mas não são percorridos; nem a fábrica, que a carga guarda por
            // desenho e cujo interior é dela (a de teste guarda os contextos que recebeu).
            var tipo = atual.GetType();
            if (atual is Type or Delegate or MemberInfo || tipo.IsPrimitive || tipo.IsEnum || !vistos.Add(atual) || atual is string or FabricaTransformador)
            {
                continue;
            }

            if (atual is System.Collections.IEnumerable itens)
            {
                foreach (var item in itens)
                {
                    if (item is not null)
                    {
                        pendentes.Push(item);
                    }
                }

                continue;
            }

            if (tipo.Namespace?.StartsWith("CodeProcess.Incognito", StringComparison.Ordinal) != true && !tipo.Name.StartsWith("KeyValuePair", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var campo in tipo.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (campo.GetValue(atual) is { } valor)
                {
                    pendentes.Push(valor);
                }
            }
        }

        return [.. vistos];
    }

    // Uma fábrica de fora configurável: grava cada contexto que recebe e cria um transformador descartável que pode lançar
    // nos getters e no descarte.
    private sealed class FabricaDeTeste(int versao = 1) : FabricaTransformador
    {
        private int _descartes;

        public override string Nome => NomeAtual;

        public override int Versao => versao;

        public override Injetividade Injetividade => Injetividade.Garantida;

        public override IReadOnlySet<TipoColuna> TiposSuportados { get; } = Enum.GetValues<TipoColuna>().ToHashSet();

        internal string NomeAtual { get; set; } = "exemplo.gravador";

        internal List<ContextoCriacao> Contextos { get; } = [];

        internal Func<Exception?> NoCriar { get; init; } = () => null;

        internal Func<Exception?> NoNome { get; init; } = () => null;

        internal Func<Exception?> NoDescarte { get; init; } = () => null;

        internal string? NomeCriado { get; init; }

        internal int Descartes => Volatile.Read(ref _descartes);

        // Aceita o derivarDe e as políticas; recusa parâmetros, como o padrão.
        public override void Validar(RegraTransformacao regra)
        {
            ArgumentNullException.ThrowIfNull(regra);
            if (regra.Parametros.Count > 0)
            {
                throw new RegraInvalidaException("Sem parâmetros.");
            }
        }

        internal void Descartou() => Interlocked.Increment(ref _descartes);

        protected override Transformador CriarCore(ContextoCriacao contexto)
        {
            Contextos.Add(contexto);
            return NoCriar() is { } excecao ? throw excecao : new Criado(this);
        }
    }

    private sealed class Criado(FabricaDeTeste fabrica) : Transformador, IDisposable
    {
        public override string Nome => fabrica.NoNome() is { } excecao ? throw excecao : fabrica.NomeCriado ?? fabrica.Nome;

        public override int Versao => fabrica.Versao;

        public override Injetividade Injetividade => fabrica.Injetividade;

        public override ResultadoTransformacao Transformar(object? valor, in ContextoTransformacao contexto) => new(valor, Desfecho.Substituido);

        public void Dispose()
        {
            fabrica.Descartou();
            if (fabrica.NoDescarte() is { } excecao)
            {
                throw excecao;
            }
        }
    }
}
