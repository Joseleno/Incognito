using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Propriedade do <c>br.chave-dfe</c>: em todas as células da grade (modelo, série com as fronteiras, tpEmis, AAMM com as
/// fronteiras das épocas, tipo de emitente e posição 30), com número, código e deslocamentos sorteados, a saída de uma
/// chave classificada tem a classificação esperada (por uma tabela escrita à parte do classificador), nunca é anomalia,
/// mantém a estrutura, os campos preservados e os deslocamentos, tem o emitente das colunas e volta pela inversa (numa
/// instância separada); a anomalia segue a política e volta como veio; nenhuma saída se repete entre entradas
/// diferentes; e P(P⁻¹(y)) = y. As caminhadas raras (regra dos zeros, repdígito, código proibido, código igual ao
/// número pseudonimizado, número 0) são montadas por pré-imagem. Tudo com as duas chaves públicas dos vetores e os dois
/// modos de <c>permutarOrdem</c>; as asserções não imprimem valores. O teste confere por amostra; a garantia da
/// injetividade é o argumento do ADR 0019, que ele exercita em cada ramo.
/// </summary>
public sealed class PropriedadeChaveDfeTestes
{
    private static readonly int[] _modelos = [55, 65, 57, 58, 62, 63, 64, 66, 67];
    private static readonly int[] _aamms = [1407, 1807, 1808, 2005, 2006, 2103, 2104, 2402, 2502, 2604, 2607, 2500, 2513];
    private static readonly int[] _aammsDoGerador = [2402, 2502, 2604, 2601, 2513];
    private static readonly int[] _seriesDaNotaFiscal = [0, 889, 890, 899, 900, 909, 910, 919, 920, 969, 970, 979, 980, 989, 990, 999];
    private static readonly int[] _seriesDoTransporte = [0, 909, 910, 969, 970, 979, 980, 989, 990, 999];
    private static readonly int[] _seriesSoCnpj = [0, 500, 999];
    private static readonly EmitenteGerado[] _emitentes = Enum.GetValues<EmitenteGerado>();
    private static readonly NumeroGerado[] _numeros = Enum.GetValues<NumeroGerado>();
    private static readonly CodigoGerado[] _codigos = Enum.GetValues<CodigoGerado>();
    private static readonly NumeroGerado[] _numerosDaNff = [NumeroGerado.Nff, NumeroGerado.NffDataInvalida, NumeroGerado.NffSequencialZero];

    public static TheoryData<string, bool> Configuracoes => new()
    {
        { "aleatoria", false },
        { "aleatoria", true },
        { "bordas", false },
        { "bordas", true },
    };

    [Theory]
    [MemberData(nameof(Configuracoes))]
    public void Propriedade_da_chave_de_DF_e_em_todas_as_celulas(string nomeDaChave, bool permutarOrdem)
    {
        using var contexto = new Contexto(nomeDaChave, permutarOrdem);
        var fonte = SequenciaXoshiro.DaSemente(2900 + (permutarOrdem ? 1 : 0) + (nomeDaChave == "bordas" ? 2 : 0));
        var celulas = Celulas().ToArray();

        // Sob a mutação, uma célula em 10 (a grade inteira continua conferida fora dela).
        var passo = Mutacao.Tamanho(1, 10);
        foreach (var (modelo, serie, tipoEmissao, aamm, emitente, posicaoTrinta) in celulas.Where((_, indice) => indice % passo == 0))
        {
            // Nas células de tpEmis 3 dos modelos com NFF, o número quase sempre tem as partes dela, para a posição 30 e as
            // datas da célula decidirem.
            var numero = tipoEmissao == '3' && modelo is 55 or 65 or 57 or 58 && fonte.Uniforme(4) != 0
                ? _numerosDaNff[(int)fonte.Uniforme((ulong)_numerosDaNff.Length)]
                : _numeros[(int)fonte.Uniforme((ulong)_numeros.Length)];
            var codigo = _codigos[(int)fonte.Uniforme((ulong)_codigos.Length)];
            if (codigo == CodigoGerado.IgualAoNumero && numero != NumeroGerado.Qualquer)
            {
                codigo = CodigoGerado.Qualquer;
            }

            var especificacao = new EspecificacaoChaveDfe(
                aamm,
                modelo,
                serie,
                tipoEmissao,
                emitente,
                numero,
                codigo,
                posicaoTrinta,
                fonte.Uniforme(3) == 0 ? 1 + (int)fonte.Uniforme(9) : 0,
                fonte.Uniforme(4) == 0 ? 1 + (int)fonte.Uniforme(99) : 0,
                (char)('0' + (int)fonte.Uniforme(10)));
            var chave = GeradorChaveDfe.Gerar(fonte, especificacao);
            contexto.Conferir(chave);

            // Vizinha que muda um só campo: as duas saídas entram na conferência global de distintas.
            if (fonte.Uniforme(8) == 0)
            {
                contexto.Conferir(Vizinha(fonte, chave));
            }

            // P(P⁻¹(y)) = y para uma chave classificada qualquer tomada como saída.
            if (fonte.Uniforme(4) == 0)
            {
                contexto.ConferirInversaComoEntrada(chave);
            }
        }

        contexto.ConferirCobertura();
        Assert.True(celulas.Length > 20_000, "A grade encolheu.");
    }

    [Theory]
    [MemberData(nameof(Configuracoes))]
    public void Regra_dos_zeros_caminha_pela_raiz_000_e_volta_pela_inversa(string nomeDaChave, bool permutarOrdem)
    {
        // Procura emitentes cuja saída na coluna começa com 000 (cerca de um em mil): na chave de CT-e e de MDF-e o
        // cycle-walking passa pela raiz excluída, e a inversa precisa caminhar de volta.
        using var contexto = new Contexto(nomeDaChave, permutarOrdem);
        var fonte = SequenciaXoshiro.DaSemente(2910);
        var caminhadas = 0;
        for (var tentativa = 0; caminhadas < 6; tentativa++)
        {
            Assert.True(tentativa < 300_000, "Nenhuma caminhada pela raiz 000 em 300 mil tentativas.");
            var chave = GeradorChaveDfe.Gerar(fonte, new(2601, 57 + (tentativa % 2), 1, '1', EmitenteGerado.CnpjFilial, NumeroGerado.Qualquer, CodigoGerado.Qualquer));
            if (chave.AsSpan(6, 3).SequenceEqual("000"))
            {
                continue;
            }

            contexto.Cnpj.Pseudonimizar(chave.Substring(6, 14), out var daColuna);
            if (!daColuna!.StartsWith("000", StringComparison.Ordinal))
            {
                continue;
            }

            caminhadas++;
            var saida = contexto.Conferir(chave);
            Assert.False(saida.AsSpan(6, 3).SequenceEqual("000"));
        }
    }

    [Theory]
    [MemberData(nameof(Configuracoes))]
    public void Caminhadas_do_CPF_pelos_repdigitos_voltam_pela_inversa(string nomeDaChave, bool permutarOrdem)
    {
        // Pré-imagem de cada repdígito pela permutação da coluna de CPF sem exclusão: a chave com essa base caminha
        // além do repdígito, e a inversa volta.
        using var contexto = new Contexto(nomeDaChave, permutarOrdem);
        using var bruta = contexto.Feistel("cpf", Variantes.Numerico, 10_000, 100_000);
        var fonte = SequenciaXoshiro.DaSemente(2911);
        var caminhadas = 0;
        for (ulong digito = 0; digito <= 9; digito++)
        {
            var baseCpf = bruta.Inverter(digito * 111_111_111, []);
            if (baseCpf % 111_111_111 == 0)
            {
                continue;
            }

            var base9 = baseCpf.ToString("000000000", CultureInfo.InvariantCulture);
            var emitente = "000" + base9 + DvCpfClassico(base9).ToString("00", CultureInfo.InvariantCulture);
            foreach (var (modelo, serie) in new[] { (55, 920), (57, 1) })
            {
                var chave = Substituir(GeradorChaveDfe.Gerar(fonte, new(2601, modelo, serie, '1', EmitenteGerado.Cpf, NumeroGerado.Qualquer, CodigoGerado.Qualquer)), 6, emitente);
                Assert.Equal(TipoEmitente.Cpf, ClassificadorEmitente.Classificar(chave).Tipo);
                var saida = contexto.Conferir(chave);
                Assert.False(saida.AsSpan(9, 9).SequenceEqual(new string((char)('0' + (int)digito), 9)), "A saída caiu no repdígito.");
                caminhadas++;

                // A permutação bruta do teste é a da coluna: numa chave comum, a saída é a da bruta.
                var comum = GeradorChaveDfe.Gerar(fonte, new(2601, modelo, serie, '1', EmitenteGerado.Cpf, NumeroGerado.Qualquer, CodigoGerado.Qualquer));
                var baseComum = ulong.Parse(comum.AsSpan(9, 9), CultureInfo.InvariantCulture);
                var bruto = bruta.Permutar(baseComum, []);
                if (bruto % 111_111_111 != 0)
                {
                    Assert.Equal(bruto, ulong.Parse(contexto.Conferir(comum).AsSpan(9, 9), CultureInfo.InvariantCulture));
                }
            }
        }

        Assert.True(caminhadas >= 16, "Poucas caminhadas pelos repdígitos.");
    }

    [Theory]
    [MemberData(nameof(Configuracoes))]
    public void Caminhadas_do_codigo_e_do_numero_voltam_pela_inversa(string nomeDaChave, bool permutarOrdem)
    {
        // Com o emitente e o número pseudonimizados de uma chave, monta pela permutação sem exclusão a pré-imagem de cada
        // código proibido, do número pseudonimizado e do número 0: as chaves com essas pré-imagens caminham, e a inversa
        // volta. Também o código de entrada igual ao número pseudonimizado, que fica e é contado.
        using var contexto = new Contexto(nomeDaChave, permutarOrdem);
        using var codigo8 = contexto.Feistel(PseudonimizadorNumeroDfe.NomeDoDominio, Variantes.CodigoNumerico8, 10_000, 10_000);
        using var numero = contexto.Feistel(PseudonimizadorNumeroDfe.NomeDoDominio, Variantes.NumeroDfe, 10_000, 100_000);
        var fonte = SequenciaXoshiro.DaSemente(2912);
        var porCaso = new int[4];
        for (var vez = 0; porCaso.Min() < 15; vez++)
        {
            Assert.True(vez < 3_000, "Uma caminhada ficou sem amostra em 3 mil chaves.");
            var modelo = vez % 2 == 0 ? 55 : 57;
            var chave = GeradorChaveDfe.Gerar(fonte, new(2601, modelo, 1, '1', EmitenteGerado.CnpjAlfanumerico, NumeroGerado.Qualquer, CodigoGerado.Qualquer));
            var saida = contexto.Conferir(chave);
            var ajusteDoCodigo = Encoding.ASCII.GetBytes(saida[6..20] + saida[25..34]);
            var numeroSaida = ulong.Parse(saida.AsSpan(25, 9), CultureInfo.InvariantCulture);
            var ajusteDoNumero = Encoding.ASCII.GetBytes(saida[6..20] + "\0" + saida[20..25]);

            // As permutações brutas do teste são as do domínio: sem caminhada, a saída é a delas.
            var codigoBruto = codigo8.Permutar(ulong.Parse(chave.AsSpan(35, 8), CultureInfo.InvariantCulture), ajusteDoCodigo);
            if (codigoBruto != numeroSaida && !RegrasChaveDfe.EhCodigoNumericoProibido((int)codigoBruto) && !RegrasChaveDfe.EhCodigoNumericoProibido(int.Parse(chave.AsSpan(35, 8), CultureInfo.InvariantCulture)))
            {
                Assert.Equal(codigoBruto, ulong.Parse(saida.AsSpan(35, 8), CultureInfo.InvariantCulture));
            }

            var numeroBruto = numero.Permutar(ulong.Parse(chave.AsSpan(25, 9), CultureInfo.InvariantCulture), ajusteDoNumero);
            if (numeroBruto != 0)
            {
                Assert.Equal(numeroBruto, numeroSaida);
            }

            foreach (var proibido in RegrasChaveDfe.CodigosNumericosProibidos.ToArray())
            {
                var previo = codigo8.Inverter((ulong)proibido, ajusteDoCodigo);
                if (previo != numeroSaida && !RegrasChaveDfe.EhCodigoNumericoProibido((int)previo))
                {
                    contexto.Conferir(Substituir(chave, 35, previo.ToString("00000000", CultureInfo.InvariantCulture)));
                    porCaso[0]++;
                }
            }

            if (numeroSaida < 100_000_000)
            {
                var previo = codigo8.Inverter(numeroSaida, ajusteDoCodigo);
                if (!RegrasChaveDfe.EhCodigoNumericoProibido((int)previo) && previo != numeroSaida)
                {
                    contexto.Conferir(Substituir(chave, 35, previo.ToString("00000000", CultureInfo.InvariantCulture)));
                    porCaso[1]++;
                }

                var igual = Substituir(chave, 35, numeroSaida.ToString("00000000", CultureInfo.InvariantCulture));
                contexto.Conferir(igual);
                contexto.ChaveDfe.Pseudonimizar(igual, out _, out var ocorrencias);
                Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.CodigoIgualAoNumero), "O código igual ao número pseudonimizado não foi contado.");
                porCaso[2]++;
            }

            var previoDoZero = numero.Inverter(0, ajusteDoNumero);
            if (previoDoZero != 0)
            {
                contexto.Conferir(Substituir(chave, 25, previoDoZero.ToString("000000000", CultureInfo.InvariantCulture)));
                porCaso[3]++;
            }
        }

    }

    [Theory]
    [InlineData((int)EmitenteGerado.CpfComZeros, 55, 1, 920, '1', false)]
    [InlineData((int)EmitenteGerado.Cpf, 55, 1, 920, '1', false)]
    [InlineData((int)EmitenteGerado.CpfComCincoZeros, 55, 1, 920, '1', false)]
    [InlineData((int)EmitenteGerado.CpfComZeros, 65, 1, 920, '1', false)]
    [InlineData((int)EmitenteGerado.CpfComZeros, 55, 985, 975, '3', false)]
    [InlineData((int)EmitenteGerado.CpfComZeros, 55, 25, 25, '3', true)]
    [InlineData((int)EmitenteGerado.CpfComZeros, 65, 25, 25, '3', true)]
    public void As_mesmas_14_posicoes_de_emitente_lidas_como_CNPJ_e_como_CPF_sao_reversiveis_e_caem_em_dominios_diferentes(
        int emitente, int modelo, int serieCnpj, int serieCpf, char tipoEmissao, bool nff)
    {
        // Série de CNPJ contra série de CPF (e, na NFF, a posição 30 em 1 contra 2), com os mesmos 14 caracteres: cada
        // leitura vai à coluna do seu domínio (com nomes diferentes), as duas voltam pela inversa e não coincidem.
        var emitenteGerado = (EmitenteGerado)emitente;
        foreach (var permutarOrdem in new[] { false, true })
        {
            ConferirAsDuasLeituras(emitenteGerado, modelo, serieCnpj, serieCpf, tipoEmissao, nff, permutarOrdem);
        }
    }

    private static void ConferirAsDuasLeituras(EmitenteGerado emitenteGerado, int modelo, int serieCnpj, int serieCpf, char tipoEmissao, bool nff, bool permutarOrdem)
    {
        using var contexto = new Contexto("aleatoria", permutarOrdem, dominioCpf: "cpf-pessoa", dominioCnpj: "cnpj-empresa");
        var fonte = SequenciaXoshiro.DaSemente(2913);
        for (var vez = 0; vez < 50; vez++)
        {
            var numeroGerado = nff ? NumeroGerado.Nff : NumeroGerado.Qualquer;
            var comoCnpj = GeradorChaveDfe.Gerar(fonte, new(2601, modelo, serieCnpj, tipoEmissao, emitenteGerado, numeroGerado, CodigoGerado.Qualquer, '1'));
            var comoCpf = Substituir(comoCnpj, 22, serieCpf.ToString("000", CultureInfo.InvariantCulture));
            if (nff)
            {
                comoCpf = Substituir(comoCpf, 29, "2");
            }

            Assert.Equal(TipoEmitente.Cnpj, ClassificadorEmitente.Classificar(comoCnpj).Tipo);
            Assert.Equal(TipoEmitente.Cpf, ClassificadorEmitente.Classificar(comoCpf).Tipo);

            var saidaCnpj = contexto.Conferir(comoCnpj);
            var saidaCpf = contexto.Conferir(comoCpf);
            contexto.Cpf.Pseudonimizar(comoCpf.Substring(9, 11), out var cpfDaColuna);

            Assert.True(saidaCnpj[6..20] != saidaCpf[6..20], "As duas leituras deram o mesmo emitente.");
            Assert.True(saidaCnpj[6..20] != "000" + cpfDaColuna, "A leitura como CNPJ foi ao domínio de CPF.");
            if (emitenteGerado == EmitenteGerado.CpfComZeros)
            {
                // Com o CPF começando com 00, os 14 caracteres também são um CNPJ de DV certo: nenhuma leitura tem DV errado.
                contexto.ChaveDfe.Pseudonimizar(comoCnpj, out _, out var ocorrenciasCnpj);
                contexto.ChaveDfe.Pseudonimizar(comoCpf, out _, out var ocorrenciasCpf);
                Assert.False(ocorrenciasCnpj.HasFlag(OcorrenciasChaveDfe.DvDoEmitenteInvalido) || ocorrenciasCpf.HasFlag(OcorrenciasChaveDfe.DvDoEmitenteInvalido));
            }
        }
    }

    [Fact]
    public void Gerador_detalhado_escreve_os_campos_pedidos()
    {
        // Os campos pedidos nas posições, os deslocamentos do cDV e do DV do emitente, e cada tipo de emitente, de número
        // e de código na forma prometida, conferidos à parte do gerador.
        var fonte = SequenciaXoshiro.DaSemente(2915);
        foreach (var emitente in _emitentes)
        {
            foreach (var numero in _numeros)
            {
                foreach (var codigo in _codigos)
                {
                    for (var vez = 0; vez < 40; vez++)
                    {
                        var modelo = _modelos[vez % _modelos.Length];
                        var aamm = _aammsDoGerador[vez % _aammsDoGerador.Length];
                        var deslocamentoCdv = vez % 10;
                        var deslocamentoDv = (vez * 7) % 100;
                        var especificacao = new EspecificacaoChaveDfe(aamm, modelo, 123, '3', emitente, numero, codigo, '2', deslocamentoCdv, deslocamentoDv, '7');
                        if (codigo == CodigoGerado.IgualAoNumero && numero != NumeroGerado.Qualquer)
                        {
                            Assert.Throws<ArgumentException>("especificacao", () => GeradorChaveDfe.Gerar(fonte, especificacao));
                            continue;
                        }

                        var chave = GeradorChaveDfe.Gerar(fonte, especificacao);

                        Assert.True(RegrasChaveDfe.TemEstrutura(chave), "Fora da estrutura.");
                        Assert.True(chave[2..6] == aamm.ToString("0000", CultureInfo.InvariantCulture) && chave[20..22] == modelo.ToString(CultureInfo.InvariantCulture) && chave[22..25] == "123" && chave[34] == '3', "Campo pedido fora do lugar.");
                        Assert.Equal(deslocamentoCdv, Deslocamento(chave));
                        ConferirEmitenteGerado(emitente, chave[6..20], emitente == EmitenteGerado.CpfComCincoZeros && deslocamentoDv == 0 ? 1 : deslocamentoDv);
                        ConferirNumeroGerado(numero, modelo, aamm, chave);
                        ConferirCodigoGerado(codigo, modelo, chave);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("aamm")]
    [InlineData("modelo")]
    [InlineData("serie")]
    [InlineData("tpEmis")]
    [InlineData("posicao 30")]
    [InlineData("site")]
    [InlineData("deslocamento do cDV")]
    [InlineData("deslocamento do DV")]
    [InlineData("emitente")]
    [InlineData("numero")]
    [InlineData("codigo")]
    public void Gerador_detalhado_recusa_especificacao_fora_da_faixa(string campo)
    {
        var valida = new EspecificacaoChaveDfe(2601, 55, 1, '1', EmitenteGerado.CnpjNumerico, NumeroGerado.Qualquer, CodigoGerado.Qualquer);
        var invalida = campo switch
        {
            "aamm" => valida with { Aamm = 10_000 },
            "modelo" => valida with { Modelo = 100 },
            "serie" => valida with { Serie = -1 },
            "tpEmis" => valida with { TipoEmissao = 'A' },
            "posicao 30" => valida with { TipoDoEmissorNff = ' ' },
            "site" => valida with { SiteAutorizador = 'x' },
            "deslocamento do cDV" => valida with { DeslocamentoCdv = 10 },
            "deslocamento do DV" => valida with { DeslocamentoDvEmitente = -1 },
            "emitente" => valida with { Emitente = (EmitenteGerado)99 },
            "numero" => valida with { Numero = (NumeroGerado)99 },
            _ => valida with { Codigo = (CodigoGerado)99 },
        };
        var fonte = SequenciaXoshiro.DaSemente(2916);

        Assert.Throws<ArgumentOutOfRangeException>("especificacao", () => GeradorChaveDfe.Gerar(fonte, invalida));
    }

    [Theory]
    [InlineData(55, 0, 1, null)]
    [InlineData(55, 31, 13, null)]
    [InlineData(57, 29, 2, 1_234_567L)]
    [InlineData(58, 99, 0, 0L)]
    [InlineData(66, 15, null, 1_111_111L)]
    [InlineData(62, null, 7, 0L)]
    public void Gerador_detalhado_escreve_o_dia_o_mes_e_o_codigo_pedidos(int modelo, int? dia, int? mes, long? codigo)
    {
        // Pedidos mesmo fora do mês (dia 0, 31 de fevereiro, mês 13): a NFF com data inválida em cada parte.
        var fonte = SequenciaXoshiro.DaSemente(2917);
        var especificacao = new EspecificacaoChaveDfe(2402, modelo, 123, '3', EmitenteGerado.CnpjNumerico, NumeroGerado.Nff, CodigoGerado.Qualquer, '1', Dia: dia, MesDoNumero: mes, ValorDoCodigo: codigo);
        var transporte = modelo is 57 or 58;

        var chave = GeradorChaveDfe.Gerar(fonte, especificacao);

        Assert.True(RegrasChaveDfe.TemEstrutura(chave), "Fora da estrutura.");
        Assert.Equal(0, Deslocamento(chave));
        if (dia is { } diaPedido)
        {
            Assert.Equal(diaPedido, int.Parse(chave.AsSpan(transporte ? 27 : 25, 2), CultureInfo.InvariantCulture));
        }

        Assert.Equal(mes ?? 2, int.Parse(chave.AsSpan(transporte ? 25 : 27, 2), CultureInfo.InvariantCulture));
        if (codigo is { } codigoPedido)
        {
            var de7 = modelo is 62 or 66;
            Assert.Equal(codigoPedido, long.Parse(chave.AsSpan(de7 ? 36 : 35, de7 ? 7 : 8), CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData((int)NumeroGerado.Qualquer, 1, null, null, (int)CodigoGerado.Qualquer)]
    [InlineData((int)NumeroGerado.NffDataInvalida, null, 1, null, (int)CodigoGerado.Qualquer)]
    [InlineData((int)NumeroGerado.Nff, 100, null, null, (int)CodigoGerado.Qualquer)]
    [InlineData((int)NumeroGerado.Nff, null, -1, null, (int)CodigoGerado.Qualquer)]
    [InlineData((int)NumeroGerado.Qualquer, null, null, 1L, (int)CodigoGerado.Proibido)]
    [InlineData((int)NumeroGerado.Qualquer, null, null, 100_000_000L, (int)CodigoGerado.Qualquer)]
    [InlineData((int)NumeroGerado.Qualquer, null, null, -1L, (int)CodigoGerado.Qualquer)]
    public void Gerador_detalhado_recusa_dia_mes_e_codigo_fora_do_lugar(int numero, int? dia, int? mes, long? codigo, int tipoDoCodigo)
    {
        var especificacao = new EspecificacaoChaveDfe(2402, 55, 1, '1', EmitenteGerado.CnpjNumerico, (NumeroGerado)numero, (CodigoGerado)tipoDoCodigo, Dia: dia, MesDoNumero: mes, ValorDoCodigo: codigo);
        var fonte = SequenciaXoshiro.DaSemente(2918);

        var excecao = Assert.ThrowsAny<ArgumentException>(() => GeradorChaveDfe.Gerar(fonte, especificacao));
        Assert.Equal("especificacao", excecao.ParamName);
    }

    [Fact]
    public void Oraculo_de_classificacao_bate_com_o_classificador_em_toda_a_grade()
    {
        // A tabela escrita à parte, a partir da classificação do ADR 0019, e o classificador concordam em cada célula; a grade tem as
        // classes em cada modelo que as admite.
        var fonte = SequenciaXoshiro.DaSemente(2914);
        var classes = new HashSet<(int, string)>();
        foreach (var (modelo, serie, tipoEmissao, aamm, emitente, posicaoTrinta) in Celulas())
        {
            var chave = GeradorChaveDfe.Gerar(fonte, new(aamm, modelo, serie, tipoEmissao, emitente, NumeroGerado.Nff, CodigoGerado.Qualquer, posicaoTrinta));
            var esperada = Esperada(chave);
            var real = ClassificadorEmitente.Classificar(chave);

            Assert.True(esperada == Resumo(real), "O classificador diverge da tabela da classificação.");
            classes.Add((modelo, esperada));
        }

        string[] daNotaFiscal = ["Cpf", "Cnpj", "Cpf nff", "Cnpj nff", "anomalia"];
        string[] doTransporte = ["Cpf", "Cnpj", "Cnpj exclui", "Cpf nff", "Cnpj nff", "Cnpj nff exclui"];
        foreach (var modelo in new[] { 55, 65 })
        {
            Assert.All(daNotaFiscal, classe => Assert.Contains((modelo, classe), classes));
        }

        foreach (var modelo in new[] { 57, 58 })
        {
            Assert.All(doTransporte, classe => Assert.Contains((modelo, classe), classes));
        }
    }

    // Células: o produto só das dimensões que mudam a classificação ou o caminho, com as fronteiras; os modelos só com
    // CNPJ colapsam a série e a época, que não os afetam.
    private static IEnumerable<(int Modelo, int Serie, char TipoEmissao, int Aamm, EmitenteGerado Emitente, char PosicaoTrinta)> Celulas()
    {
        foreach (var modelo in _modelos)
        {
            var notaFiscal = modelo is 55 or 65;
            var transporte = modelo is 57 or 58;
            var series = notaFiscal ? _seriesDaNotaFiscal : transporte ? _seriesDoTransporte : _seriesSoCnpj;
            var tipos = modelo switch
            {
                55 => "12345679",
                65 => "139",
                57 => "13578",
                58 => "123",
                _ => "1238",
            };
            var aamms = notaFiscal || transporte ? _aamms : [2402, 2607];
            foreach (var serie in series)
            {
                foreach (var tipoEmissao in tipos)
                {
                    foreach (var aamm in aamms)
                    {
                        foreach (var emitente in _emitentes)
                        {
                            // A posição 30 só decide na NFF da NF-e e da NFC-e: 1, 2 e um valor fora.
                            var posicoes = notaFiscal && tipoEmissao == '3' ? "125" : "1";
                            foreach (var posicaoTrinta in posicoes)
                            {
                                yield return (modelo, serie, tipoEmissao, aamm, emitente, posicaoTrinta);
                            }
                        }
                    }
                }
            }
        }
    }

    // A classificação do ADR 0019 escrita à parte do classificador, lida direto das posições: "anomalia", "zerada" ou o tipo seguido
    // de " nff" e " exclui" quando valem.
    private static string Esperada(string chave)
    {
        if (chave.All(c => c == '0'))
        {
            return "zerada";
        }

        var modelo = int.Parse(chave.AsSpan(20, 2), CultureInfo.InvariantCulture);
        var aamm = int.Parse(chave.AsSpan(2, 4), CultureInfo.InvariantCulture);
        var serie = int.Parse(chave.AsSpan(22, 3), CultureInfo.InvariantCulture);
        var tpEmis3 = chave[34] == '3';
        var paa = serie is >= 970 and <= 989;
        var letra = chave[6..18].Any(char.IsAsciiLetterUpper);
        var zerosDoCpf = chave[6..9] == "000";
        switch (modelo)
        {
            case 62 or 63 or 64 or 66 or 67:
                return "Cnpj";
            case 57 or 58:
                var nffTransporte = tpEmis3 && !paa && aamm >= 2006 ? " nff" : string.Empty;
                return letra ? "Cnpj" + nffTransporte : zerosDoCpf ? "Cpf" + nffTransporte : "Cnpj" + nffTransporte + " exclui";
            case 55 or 65:
                var nff = tpEmis3 && !paa && aamm >= 2104;
                var marca = nff ? " nff" : string.Empty;
                if (letra || aamm < 1808)
                {
                    return "Cnpj" + marca;
                }

                if (tpEmis3 && !paa && !nff)
                {
                    return "anomalia";
                }

                string? tipo = nff
                    ? chave[29] switch { '1' => "Cnpj", '2' => "Cpf", _ => null }
                    : serie switch
                    {
                        <= 909 => "Cnpj",
                        <= 969 => "Cpf",
                        <= 979 when modelo == 55 => "Cpf",
                        <= 989 when modelo == 55 => "Cnpj",
                        _ => null,
                    };
                return tipo is null || (tipo == "Cpf" && !zerosDoCpf) ? "anomalia" : tipo + marca;
            default:
                return "anomalia";
        }
    }

    private static void ConferirEmitenteGerado(EmitenteGerado emitente, string cnpjOuCpf, int deslocamentoDv)
    {
        var ehCpf = emitente is EmitenteGerado.Cpf or EmitenteGerado.CpfComZeros or EmitenteGerado.CpfComCincoZeros or EmitenteGerado.CpfRepdigito;
        var dv = int.Parse(cnpjOuCpf.AsSpan(12, 2), CultureInfo.InvariantCulture);
        var dvCerto = ehCpf ? DvCpfClassico(cnpjOuCpf[3..12]) : DvCnpjClassico(cnpjOuCpf[..12]);
        Assert.Equal(deslocamentoDv, (dv - dvCerto + 100) % 100);
        Assert.False(cnpjOuCpf[..12] == "000000000001", "O CNPJ de raiz zero e ordem 0001, cadastrado, foi gerado.");
        var raiz = cnpjOuCpf[..8];
        var ordem = cnpjOuCpf[8..12];
        var letraNaRaiz = raiz.Any(char.IsAsciiLetterUpper);
        var letraNaOrdem = ordem.Any(char.IsAsciiLetterUpper);
        var forma = emitente switch
        {
            EmitenteGerado.CnpjNumerico => !letraNaRaiz && ordem == "0001" && raiz != "00000000",
            EmitenteGerado.CnpjAlfanumerico => letraNaRaiz && ordem == "0001",
            EmitenteGerado.CnpjAlfanumericoComZeros => raiz.StartsWith("000", StringComparison.Ordinal) && letraNaRaiz && ordem == "0001",
            EmitenteGerado.CnpjNumericoComOrdemAlfanumerica => !letraNaRaiz && letraNaOrdem,
            EmitenteGerado.CnpjAlfanumericoComOrdemAlfanumerica => letraNaRaiz,
            EmitenteGerado.CnpjFilial => !letraNaRaiz && !letraNaOrdem && ordem != "0001" && raiz != "00000000",
            EmitenteGerado.CnpjRaizComZeros => raiz.StartsWith("000", StringComparison.Ordinal) && raiz != "00000000" && !letraNaRaiz && !letraNaOrdem,
            EmitenteGerado.CnpjRaizComZerosEOrdemAlfanumerica => raiz.StartsWith("000", StringComparison.Ordinal) && raiz != "00000000" && !letraNaRaiz && letraNaOrdem,
            EmitenteGerado.CnpjRaizZero => raiz == "00000000" && ordem == "0000",
            EmitenteGerado.Cpf => cnpjOuCpf.StartsWith("000", StringComparison.Ordinal) && cnpjOuCpf[3..12].Distinct().Count() > 1,
            EmitenteGerado.CpfComZeros => cnpjOuCpf.StartsWith("00000", StringComparison.Ordinal) && !cnpjOuCpf.StartsWith("00000000", StringComparison.Ordinal) && cnpjOuCpf[3..12].Distinct().Count() > 1,
            EmitenteGerado.CpfComCincoZeros => cnpjOuCpf.StartsWith("00000000", StringComparison.Ordinal) && cnpjOuCpf[3..12].Distinct().Count() > 1,
            _ => cnpjOuCpf.StartsWith("000", StringComparison.Ordinal) && cnpjOuCpf[3..12].Distinct().Count() == 1,
        };
        Assert.True(forma, "O emitente não tem a forma pedida.");
    }

    private static void ConferirNumeroGerado(NumeroGerado numero, int modelo, int aamm, string chave)
    {
        var transporte = modelo is 57 or 58;
        var dia = int.Parse(chave.AsSpan(transporte ? 27 : 25, 2), CultureInfo.InvariantCulture);
        var mes = int.Parse(chave.AsSpan(transporte ? 25 : 27, 2), CultureInfo.InvariantCulture);
        var sequencial = int.Parse(transporte ? chave.AsSpan(29, 5) : chave.AsSpan(30, 4), CultureInfo.InvariantCulture);
        var mesDoAammValido = aamm % 100 is >= 1 and <= 12;
        var dataValida = mesDoAammValido && mes == aamm % 100 && dia >= 1 && dia <= DateTime.DaysInMonth(2000 + (aamm / 100), mes);
        switch (numero)
        {
            case NumeroGerado.Zero:
                Assert.Equal("000000000", chave[25..34]);
                break;
            case NumeroGerado.Qualquer:
                Assert.NotEqual("000000000", chave[25..34]);
                break;
            case NumeroGerado.Nff:
                Assert.True(dataValida == mesDoAammValido && mes == aamm % 100 && sequencial != 0 && (transporte || chave[29] == '2'), "NFF fora da forma pedida.");
                break;
            case NumeroGerado.NffSequencialZero:
                Assert.True(dataValida == mesDoAammValido && sequencial == 0, "NFF com sequencial 0 fora da forma pedida.");
                break;
            default:
                Assert.False(dataValida, "A data da NFF inválida saiu válida.");
                break;
        }
    }

    private static void ConferirCodigoGerado(CodigoGerado codigo, int modelo, string chave)
    {
        if (modelo is 62 or 66)
        {
            Assert.Equal('7', chave[35]);
            return;
        }

        var valor = int.Parse(chave.AsSpan(35, 8), CultureInfo.InvariantCulture);
        if (codigo == CodigoGerado.Proibido)
        {
            Assert.True(RegrasChaveDfe.EhCodigoNumericoProibido(valor), "O código não é proibido.");
        }
        else if (codigo == CodigoGerado.IgualAoNumero)
        {
            Assert.Equal(long.Parse(chave.AsSpan(25, 9), CultureInfo.InvariantCulture), valor);
        }
    }

    private static string Resumo(ClassificacaoEmitente classificacao) =>
        classificacao.EhChaveZerada ? "zerada"
        : classificacao.EhAnomalia ? "anomalia"
        : classificacao.Tipo + (classificacao.EhNff ? " nff" : string.Empty) + (classificacao.ExcluiRaizComZeros ? " exclui" : string.Empty);

    // Muda um só campo: emitente, número, código, cDV, série, AAMM, tpEmis, posição 30 ou posição 36; o cDV é refeito
    // com o deslocamento de antes, salvo quando o campo mudado é o próprio cDV.
    private static string Vizinha(FonteDeSorteio fonte, string chave)
    {
        int[] posicoes = [6, 10, 15, 19, 26, 30, 33, 36, 40, 43, 23, 5, 34, 29, 35];
        var posicao = posicoes[(int)fonte.Uniforme((ulong)posicoes.Length)];
        var caracteres = chave.ToCharArray();
        var original = caracteres[posicao];
        do
        {
            caracteres[posicao] = posicao < 18 && posicao >= 6 && fonte.Uniforme(4) == 0
                ? (char)('A' + (int)fonte.Uniforme(26))
                : (char)('0' + (int)fonte.Uniforme(10));
        }
        while (caracteres[posicao] == original);

        return posicao == 43 ? new string(caracteres) : Substituir(chave, posicao, caracteres[posicao].ToString());
    }

    // Troca um trecho das 43 primeiras posições e refaz o cDV com o deslocamento que a chave tinha.
    private static string Substituir(string chave, int indice, string trecho)
    {
        var primeiros43 = chave[..indice] + trecho + chave[(indice + trecho.Length)..43];
        return primeiros43 + (char)('0' + ((DvChaveClassico(primeiros43) + Deslocamento(chave)) % 10));
    }

    private static int Deslocamento(string chave) => (chave[43] - '0' - DvChaveClassico(chave[..43]) + 10) % 10;

    private sealed class Contexto : IDisposable
    {
        private readonly ChaveIncognito _chave;
        private readonly PseudonimizadorChaveDfe _inversa;
        private readonly PseudonimizadorCpf _cpfDaInversa;
        private readonly PseudonimizadorCnpj _cnpjDaInversa;
        private readonly Dictionary<string, string> _saidas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _cobertura = new(StringComparer.Ordinal);
        private readonly bool _permutarOrdem;

        internal Contexto(string nomeDaChave, bool permutarOrdem, string dominioCpf = "cpf", string dominioCnpj = "cnpj")
        {
            using (var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json"))
            {
                _chave = ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", nomeDaChave), "texto"));
            }

            _permutarOrdem = permutarOrdem;
            Cpf = new PseudonimizadorCpf(_chave, dominioCpf, 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            Cnpj = new PseudonimizadorCnpj(_chave, dominioCnpj, 1, "homologacao", permutarOrdem, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            ChaveDfe = new PseudonimizadorChaveDfe(_chave, 1, "homologacao", Cpf, Cnpj, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar);

            // A inversa numa instância separada, com pseudonimizadores próprios: nada de estado compartilhado.
            _cpfDaInversa = new PseudonimizadorCpf(_chave, dominioCpf, 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            _cnpjDaInversa = new PseudonimizadorCnpj(_chave, dominioCnpj, 1, "homologacao", permutarOrdem, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
            _inversa = new PseudonimizadorChaveDfe(_chave, 1, "homologacao", _cpfDaInversa, _cnpjDaInversa, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar);
        }

        internal PseudonimizadorCpf Cpf { get; }

        internal PseudonimizadorCnpj Cnpj { get; }

        internal PseudonimizadorChaveDfe ChaveDfe { get; }

        public void Dispose()
        {
            ChaveDfe.Dispose();
            _inversa.Dispose();
            Cpf.Dispose();
            Cnpj.Dispose();
            _cpfDaInversa.Dispose();
            _cnpjDaInversa.Dispose();
            _chave.Dispose();
        }

        internal PermutacaoFeistel Feistel(string dominio, string variante, ulong a, ulong b)
        {
            var subchave = new byte[Subchaves.Tamanho];
            Subchaves.DerivarChaveDeDominio(_chave, dominio, 1, variante, "homologacao", subchave);
            return new PermutacaoFeistel(subchave, 10, a, b);
        }

        // Confere uma entrada e devolve a saída; registra a cobertura pelo desfecho observado.
        internal string Conferir(string chave)
        {
            var esperada = Esperada(chave);
            var desfecho = ChaveDfe.Pseudonimizar(chave, out var saida, out var ocorrencias);
            Assert.NotNull(saida);
            Registrar(saida, chave);
            if (esperada is "anomalia" or "zerada")
            {
                Assert.Same(chave, saida);
                Assert.Equal(esperada == "zerada" ? Desfecho.Zerado : Desfecho.CopiadoEmClaro, desfecho);
                Assert.Equal(esperada == "zerada" ? OcorrenciasChaveDfe.Nenhuma : OcorrenciasChaveDfe.Anomalia, ocorrencias);
                _inversa.Inverter(chave, out var anomalaDeVolta, out _);
                Assert.Same(chave, anomalaDeVolta);
                Contar(esperada);
                return saida;
            }

            // As ocorrências calculadas à parte, da entrada (e do número da saída, para o código igual a ele).
            Assert.Equal(OcorrenciasEsperadas(chave, saida, esperada), ocorrencias);

            Assert.True(desfecho is Desfecho.Pseudonimizado or Desfecho.DvInvalidoPreservado or Desfecho.PontoFixo, "Desfecho inesperado para chave classificada.");
            Contar(desfecho.ToString());
            ConferirExclusoes(saida, esperada, ocorrencias);
            Assert.True(RegrasChaveDfe.TemEstrutura(saida), "A saída saiu da estrutura.");
            Assert.True(Esperada(saida) == esperada, "A classificação da saída não é a da entrada.");
            Assert.Equal(Deslocamento(chave), Deslocamento(saida));
            Assert.True(saida[..6] == chave[..6] && saida[20..25] == chave[20..25] && saida[34] == chave[34], "Um campo preservado mudou.");
            Assert.True(chave[20..22] is not ("62" or "66") || saida[35] == chave[35], "O nSiteAutoriz mudou.");
            ConferirEmitente(chave, saida, esperada);

            Assert.Equal(desfecho, _inversa.Inverter(saida, out var volta, out var ocorrenciasDaVolta));
            Assert.True(volta == chave, "A inversa não devolveu a entrada.");
            Assert.Equal(ocorrencias, ocorrenciasDaVolta);

            Contar(esperada);
            Contar(chave[20..22] + " " + esperada);
            foreach (var ocorrencia in Enum.GetValues<OcorrenciasChaveDfe>().Where(o => o != OcorrenciasChaveDfe.Nenhuma && ocorrencias.HasFlag(o)))
            {
                Contar(ocorrencia.ToString());
            }

            if (Deslocamento(chave) != 0)
            {
                Contar("cdv");
            }

            return saida;
        }

        // A chave como saída: a inversa dá uma entrada, e a ida a devolve.
        internal void ConferirInversaComoEntrada(string y)
        {
            var esperada = Esperada(y);
            if (esperada is "anomalia" or "zerada")
            {
                return;
            }

            var desfecho = _inversa.Inverter(y, out var x, out _);
            Assert.True(desfecho is Desfecho.Pseudonimizado or Desfecho.DvInvalidoPreservado or Desfecho.PontoFixo, "A inversa não tratou uma chave classificada.");
            Assert.True(desfecho == Desfecho.PontoFixo || x != y, "A inversa devolveu a própria chave sem ponto fixo.");
            ChaveDfe.Pseudonimizar(x!, out var ida, out _);
            Assert.True(ida == y, "P(P⁻¹(y)) não é y.");
            Contar("ida da inversa");
        }

        internal void ConferirCobertura()
        {
            string[] exigidos =
            [
                "Cpf", "Cnpj", "Cnpj exclui", "Cpf nff", "Cnpj nff", "Cnpj nff exclui", "anomalia", "cdv", "ida da inversa",
                nameof(OcorrenciasChaveDfe.EmitentePontoFixo), nameof(OcorrenciasChaveDfe.DvDoEmitenteInvalido),
                nameof(OcorrenciasChaveDfe.NumeroZero), nameof(OcorrenciasChaveDfe.SequencialZero),
                nameof(OcorrenciasChaveDfe.DataNffInvalida), nameof(OcorrenciasChaveDfe.CodigoProibido),
            ];
            foreach (var exigido in exigidos)
            {
                Assert.True(_cobertura.GetValueOrDefault(exigido) >= Mutacao.Tamanho(20, 1), $"Célula com pouca amostra: {exigido}.");
            }

            // Cada classe que o modelo admite, em cada modelo.
            string[] daNotaFiscal = ["Cpf", "Cnpj", "Cpf nff", "Cnpj nff"];
            string[] doTransporte = ["Cpf", "Cnpj", "Cnpj exclui", "Cpf nff", "Cnpj nff", "Cnpj nff exclui"];
            foreach (var modelo in _modelos)
            {
                var classes = modelo switch { 55 or 65 => daNotaFiscal, 57 or 58 => doTransporte, _ => ["Cnpj"] };
                foreach (var classe in classes)
                {
                    Assert.True(_cobertura.GetValueOrDefault(modelo.ToString(CultureInfo.InvariantCulture) + " " + classe) >= Mutacao.Tamanho(5, 1), $"Classe sem amostra: {modelo} {classe}.");
                }
            }

            // O ponto fixo da chave inteira é raro: quase tudo é pseudonimizado.
            var transformadas = _cobertura.GetValueOrDefault(nameof(Desfecho.Pseudonimizado)) + _cobertura.GetValueOrDefault(nameof(Desfecho.DvInvalidoPreservado));
            Assert.True(_cobertura.GetValueOrDefault(nameof(Desfecho.PontoFixo)) * 100 < transformadas, "Ponto fixo demais.");
        }

        private static OcorrenciasChaveDfe OcorrenciasEsperadas(string chave, string saida, string esperada)
        {
            var ocorrencias = OcorrenciasChaveDfe.Nenhuma;
            var dv = int.Parse(chave.AsSpan(18, 2), CultureInfo.InvariantCulture);
            if (esperada.StartsWith("Cpf", StringComparison.Ordinal))
            {
                var baseCpf = chave[9..18];
                ocorrencias |= baseCpf.Distinct().Count() == 1 ? OcorrenciasChaveDfe.EmitentePontoFixo : OcorrenciasChaveDfe.Nenhuma;
                ocorrencias |= DvCpfClassico(baseCpf) != dv ? OcorrenciasChaveDfe.DvDoEmitenteInvalido : OcorrenciasChaveDfe.Nenhuma;
            }
            else
            {
                ocorrencias |= chave[6..14] == "00000000" ? OcorrenciasChaveDfe.EmitentePontoFixo : OcorrenciasChaveDfe.Nenhuma;
                ocorrencias |= DvCnpjClassico(chave[6..18]) != dv ? OcorrenciasChaveDfe.DvDoEmitenteInvalido : OcorrenciasChaveDfe.Nenhuma;
            }

            var transporte = chave[20..22] is "57" or "58";
            if (!esperada.Contains("nff", StringComparison.Ordinal))
            {
                ocorrencias |= chave[25..34] == "000000000" ? OcorrenciasChaveDfe.NumeroZero : OcorrenciasChaveDfe.Nenhuma;
            }
            else
            {
                // O dia vale se existe no mês do AAMM e no mês do número, com o ano 2000 + AA.
                var dia = int.Parse(chave.AsSpan(transporte ? 27 : 25, 2), CultureInfo.InvariantCulture);
                var mesDoNumero = int.Parse(chave.AsSpan(transporte ? 25 : 27, 2), CultureInfo.InvariantCulture);
                var mesDoAamm = int.Parse(chave.AsSpan(4, 2), CultureInfo.InvariantCulture);
                var ano = 2000 + int.Parse(chave.AsSpan(2, 2), CultureInfo.InvariantCulture);
                var valido = mesDoAamm is >= 1 and <= 12 && mesDoNumero is >= 1 and <= 12 && dia >= 1
                    && dia <= Math.Min(DateTime.DaysInMonth(ano, mesDoAamm), DateTime.DaysInMonth(ano, mesDoNumero));
                ocorrencias |= valido ? OcorrenciasChaveDfe.Nenhuma : OcorrenciasChaveDfe.DataNffInvalida;
                var sequencial = transporte ? chave[29..34] : chave[30..34];
                ocorrencias |= sequencial.All(c => c == '0') ? OcorrenciasChaveDfe.SequencialZero : OcorrenciasChaveDfe.Nenhuma;
            }

            if (chave[20..22] is not ("62" or "66"))
            {
                // O proibido vem antes; o igual ao número compara com o número já pseudonimizado.
                var codigo = int.Parse(chave.AsSpan(35, 8), CultureInfo.InvariantCulture);
                if (RegrasChaveDfe.EhCodigoNumericoProibido(codigo))
                {
                    ocorrencias |= OcorrenciasChaveDfe.CodigoProibido;
                }
                else if (codigo == long.Parse(saida.AsSpan(25, 9), CultureInfo.InvariantCulture))
                {
                    ocorrencias |= OcorrenciasChaveDfe.CodigoIgualAoNumero;
                }
            }

            return ocorrencias;
        }

        // A saída nunca cai num valor excluído, salvo quando a entrada já estava nele (ocorrência): código fora dos
        // proibidos e diferente do número (o proibido fica antes, mesmo igual ao número), número diferente de 0 e
        // sequencial da NFF diferente de 0.
        private static void ConferirExclusoes(string saida, string esperada, OcorrenciasChaveDfe ocorrencias)
        {
            if (saida[20..22] is not ("62" or "66"))
            {
                var codigo = int.Parse(saida.AsSpan(35, 8), CultureInfo.InvariantCulture);
                Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.CodigoProibido) || !RegrasChaveDfe.EhCodigoNumericoProibido(codigo), "A saída tem código proibido.");
                Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.CodigoIgualAoNumero) || ocorrencias.HasFlag(OcorrenciasChaveDfe.CodigoProibido) || codigo != long.Parse(saida.AsSpan(25, 9), CultureInfo.InvariantCulture), "O código da saída é igual ao número.");
            }

            if (!esperada.Contains("nff", StringComparison.Ordinal))
            {
                Assert.True(ocorrencias.HasFlag(OcorrenciasChaveDfe.NumeroZero) || saida[25..34] != "000000000", "A saída tem número 0.");
            }
            else if (!ocorrencias.HasFlag(OcorrenciasChaveDfe.SequencialZero))
            {
                var sequencial = saida[20..22] is "57" or "58" ? saida[29..34] : saida[30..34];
                Assert.True(sequencial.Any(c => c != '0'), "A saída tem sequencial 0.");
            }
        }

        private void ConferirEmitente(string chave, string saida, string esperada)
        {
            if (esperada.StartsWith("Cpf", StringComparison.Ordinal))
            {
                Cpf.Pseudonimizar(chave.Substring(9, 11), out var daColuna);
                Assert.True(saida[6..9] == "000" && saida[9..20] == daColuna, "O CPF da chave difere do da coluna.");
                return;
            }

            Cnpj.Pseudonimizar(chave.Substring(6, 14), out var cnpj);
            var caminhou = false;
            while (esperada.EndsWith("exclui", StringComparison.Ordinal) && cnpj!.StartsWith("000", StringComparison.Ordinal) && cnpj[..12].All(char.IsAsciiDigit))
            {
                Cnpj.Pseudonimizar(cnpj, out cnpj);
                caminhou = true;
            }

            Assert.True(saida[6..20] == cnpj, "O CNPJ da chave difere do da coluna.");
            Assert.True(!caminhou || !saida.AsSpan(6, 3).SequenceEqual("000"), "A regra dos zeros deixou a raiz 000.");
        }

        // Nenhuma saída de duas entradas diferentes, transformadas ou preservadas.
        private void Registrar(string saida, string entrada)
        {
            if (_saidas.TryGetValue(saida, out var anterior))
            {
                Assert.True(anterior == entrada, "Duas entradas diferentes deram a mesma saída.");
                return;
            }

            _saidas.Add(saida, entrada);
        }

        private void Contar(string celula) => _cobertura[celula] = _cobertura.GetValueOrDefault(celula) + 1;
    }
}
