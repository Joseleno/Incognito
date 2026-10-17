using System.Globalization;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Transformador <c>br.chave-dfe</c>: recálculo independente pelo caminho de baixo (colunas de CPF e CNPJ, domínio
/// <c>dfe-numero</c> e cDV clássico), emitente igual ao das colunas, cDV com o deslocamento, chaves diferentes
/// continuam diferentes, as formas de entrada, a classificação invariante, as ocorrências, as políticas e o parâmetro
/// <c>modelos</c>. Toda chave é montada na hora, com emitente, número e código sorteados e o cDV calculado; a chave do
/// Incognito é a pública do vetor de derivação; as asserções não imprimem valores.
/// </summary>
public sealed class PseudonimizadorChaveDfeTestes : IDisposable
{
    private static readonly string[] _modelos = ["55", "65", "57", "58", "62", "63", "64", "66", "67"];

    private readonly ChaveIncognito _chave = ChaveDoVetor();
    private readonly PseudonimizadorCpf _cpf;
    private readonly PseudonimizadorCnpj _cnpj;
    private readonly PseudonimizadorCnpj _cnpjComOrdem;

    public PseudonimizadorChaveDfeTestes()
    {
        _cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        _cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        _cnpjComOrdem = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", true, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
    }

    public static TheoryData<string> Modelos => [.. _modelos];

    public static TheoryData<bool> ComESemOrdem => [false, true];

    // Cada caso parte de uma chave válida do modelo 55 com emitente CNPJ e a estraga num ponto.
    public static TheoryData<string> FormasMalformadas =>
    [
        "43 posições",
        "45 posições",
        "46 posições",
        "espaço à esquerda",
        "tabulação à direita",
        "espaço não separável à direita",
        "minúscula no emitente",
        "letra na posição 19",
        "letra no cUF",
        "dígito de largura total",
        "dígito árabe-índico",
        "NUL no meio",
        "prefixo desconhecido",
        "prefixo com homóglifo",
        "prefixo de outro modelo",
        "prefixo com espaço",
        "prefixo em grupos",
        "grupos com espaço duplo",
        "grupos com hífen",
        "grupos com hífen só no primeiro separador",
        "grupos com hífen só no último separador",
        "grupos com espaço não separável",
        "grupo de 3 e de 5",
        "espaço dentro de grupo",
        "modelo 59",
        "modelo 00",
        "muito longo",
    ];

    public void Dispose()
    {
        _cpf.Dispose();
        _cnpj.Dispose();
        _cnpjComOrdem.Dispose();
        _chave.Dispose();
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void Saida_e_o_recalculo_independente_pelas_colunas_pelo_dfe_numero_e_pelo_cDV(bool permutarOrdem)
    {
        // Pelo caminho de baixo: emitente pelas colunas (CNPJ com cycle-walking pela própria coluna quando a regra dos
        // zeros pede), número e código por outra instância do dfe-numero, cDV clássico com o mesmo deslocamento.
        var cnpj = permutarOrdem ? _cnpjComOrdem : _cnpj;
        using var chaveDfe = Criar(cnpj: cnpj);
        using var numeroDfe = new PseudonimizadorNumeroDfe(_chave, PseudonimizadorNumeroDfe.NomeDoDominio, 1, "homologacao");
        var sorteio = new Random(permutarOrdem ? 2802 : 2801);
        foreach (var modelo in _modelos)
        {
            for (var vez = 0; vez < 300; vez++)
            {
                var entrada = ComCdvDeslocado(ChaveSorteada(sorteio, modelo), sorteio.Next(4) == 0 ? 1 + sorteio.Next(9) : 0);
                var esperado = Recalcular(entrada, cnpj, numeroDfe);

                var desfecho = chaveDfe.Pseudonimizar(entrada, out var saida, out _);

                Assert.True(esperado == saida, "A saída difere do recálculo independente.");
                Assert.Equal(Deslocamento(entrada) == 0 ? Desfecho.Pseudonimizado : Desfecho.DvInvalidoPreservado, desfecho);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void Inversa_desfaz_a_pseudonimizacao_nas_formas_e_devolve_as_mesmas_ocorrencias(bool permutarOrdem)
    {
        using var chaveDfe = Criar(cnpj: permutarOrdem ? _cnpjComOrdem : _cnpj);
        var sorteio = new Random(permutarOrdem ? 2902 : 2901);
        for (var vez = 0; vez < 9_000; vez++)
        {
            var chave = ComCdvDeslocado(ChaveSorteada(sorteio, _modelos[vez % _modelos.Length]), vez % 7 == 0 ? 1 + sorteio.Next(9) : 0);
            var entrada = (vez % 3) switch
            {
                0 => chave,
                1 => PrefixoDoModelo(chave[20..22]) + chave + "  ",
                _ => EmGrupos(chave),
            };

            var desfecho = chaveDfe.Pseudonimizar(entrada, out var saida, out var ocorrencias);
            var desfechoDaVolta = chaveDfe.Inverter(saida!, out var volta, out var ocorrenciasDaVolta);

            Assert.True(volta == entrada, "A inversa não devolveu a entrada.");
            Assert.Equal(ocorrencias, ocorrenciasDaVolta);
            Assert.Equal(desfecho, desfechoDaVolta);
        }
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void CPF_e_CNPJ_dentro_da_chave_geram_o_mesmo_pseudonimo_que_nas_colunas(bool permutarOrdem)
    {
        var cnpj = permutarOrdem ? _cnpjComOrdem : _cnpj;
        using var chaveDfe = Criar(cnpj: cnpj);
        var sorteio = new Random(permutarOrdem ? 2804 : 2803);
        var porTipo = new int[3];
        for (var vez = 0; vez < 6_000; vez++)
        {
            var entrada = ChaveSorteada(sorteio, _modelos[vez % _modelos.Length]);
            var classificacao = ClassificadorEmitente.Classificar(entrada);
            chaveDfe.Pseudonimizar(entrada, out var saida, out _);

            if (classificacao.Tipo == TipoEmitente.Cpf)
            {
                _cpf.Pseudonimizar(entrada.Substring(9, 11), out var daColuna);
                Assert.True(saida![9..20] == daColuna, "O CPF da chave difere do da coluna.");
                Assert.True(saida[6..9] == "000", "O prefixo 000 do CPF mudou.");
                porTipo[0]++;
            }
            else
            {
                cnpj.Pseudonimizar(entrada.Substring(6, 14), out var daColuna);
                if (classificacao.ExcluiRaizComZeros && daColuna!.StartsWith("000", StringComparison.Ordinal) && SoDigitosTexto(daColuna[..8]))
                {
                    continue;
                }

                Assert.True(saida![6..20] == daColuna, "O CNPJ da chave difere do da coluna.");
                porTipo[entrada[6..18].Any(char.IsAsciiLetterUpper) ? 2 : 1]++;
            }
        }

        Assert.All(porTipo, quantidade => Assert.True(quantidade > 500, "Um tipo de emitente ficou sem amostra."));
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void Na_regra_dos_zeros_o_CNPJ_que_a_coluna_levaria_a_raiz_000_e_caminhado_pela_propria_coluna(bool permutarOrdem)
    {
        // Procura CNPJs cuja saída na coluna começa com 000 (cerca de um em mil) e confere que, na chave de CT-e e de
        // MDF-e, o emitente é a primeira saída da coluna, aplicada de novo, que não começa com 000; na NF-e, é a da coluna.
        var cnpj = permutarOrdem ? _cnpjComOrdem : _cnpj;
        using var chaveDfe = Criar(cnpj: cnpj);
        var sorteio = new Random(permutarOrdem ? 2806 : 2805);
        var encontrados = 0;
        for (var tentativa = 0; encontrados < 8; tentativa++)
        {
            Assert.True(tentativa < 200_000, "Nenhum CNPJ da coluna caiu na raiz 000 em 200 mil tentativas.");
            var emitente = CnpjNumericoSemZerosDoCpf(sorteio);
            cnpj.Pseudonimizar(emitente, out var daColuna);
            if (!daColuna!.StartsWith("000", StringComparison.Ordinal))
            {
                continue;
            }

            encontrados++;
            var caminhado = daColuna;
            while (caminhado!.StartsWith("000", StringComparison.Ordinal))
            {
                cnpj.Pseudonimizar(caminhado, out caminhado);
            }

            foreach (var modelo in new[] { "57", "58" })
            {
                chaveDfe.Pseudonimizar(Montar(sorteio, "2601", emitente, modelo, "001", Digitos(sorteio, 9), '1', Digitos(sorteio, 8)), out var saida, out _);
                Assert.True(saida![6..20] == caminhado, "O CNPJ da regra dos zeros não é o caminhado pela coluna.");
            }

            chaveDfe.Pseudonimizar(Montar(sorteio, "2601", emitente, "55", "001", Digitos(sorteio, 9), '1', Digitos(sorteio, 8)), out var daNfe, out _);
            Assert.True(daNfe![6..20] == daColuna, "Fora da regra dos zeros, o CNPJ não é o da coluna.");
        }
    }

    [Fact]
    public void CNPJ_de_raiz_000_no_CT_e_e_lido_como_CPF_e_vai_pela_coluna_de_CPF()
    {
        // Pela regra dos zeros, as posições 7 a 9 em 000 dão CPF: as posições 10 a 20 vão à coluna de CPF, com o DV
        // (quase sempre errado como CPF) mantido pelo deslocamento.
        using var chaveDfe = Criar();
        var sorteio = new Random(2807);
        for (var vez = 0; vez < 200; vez++)
        {
            var raiz = "000" + Digitos(sorteio, 5);
            var baseCnpj = raiz + Digitos(sorteio, 4);
            var emitente = baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", CultureInfo.InvariantCulture);
            var entrada = Montar(sorteio, "2601", emitente, "57", "001", Digitos(sorteio, 9), '1', Digitos(sorteio, 8));

            chaveDfe.Pseudonimizar(entrada, out var saida, out var ocorrencias);
            _cpf.Pseudonimizar(entrada.Substring(9, 11), out var daColuna);

            Assert.True(saida![9..20] == daColuna && saida[6..9] == "000", "O CNPJ de raiz 000 não foi tratado como o CPF da coluna.");
            var dvDeCpfCerto = DvCpfClassico(entrada.Substring(9, 9)) == int.Parse(entrada.AsSpan(18, 2), CultureInfo.InvariantCulture);
            Assert.Equal(!dvDeCpfCerto, ocorrencias.HasFlag(OcorrenciasChaveDfe.DvDoEmitenteInvalido));
        }
    }

    [Fact]
    public void Toda_chave_valida_gerada_passa_no_validador_e_cDV_errado_continua_com_o_mesmo_deslocamento()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2808);
        for (var vez = 0; vez < 4_000; vez++)
        {
            var deslocamento = vez % 10;
            var entrada = ComCdvDeslocado(ChaveSorteada(sorteio, _modelos[sorteio.Next(_modelos.Length)]), deslocamento);

            chaveDfe.Pseudonimizar(entrada, out var saida, out _);

            Assert.Equal(deslocamento == 0, ValidadorBr.ChaveDfe(saida));
            Assert.Equal(deslocamento, Deslocamento(saida!));
            Assert.True(EmitenteValido(entrada) == EmitenteValido(saida!), "A validade do DV do emitente mudou.");
        }
    }

    [Fact]
    public void Chaves_que_diferem_so_no_cDV_so_no_numero_ou_so_no_codigo_continuam_diferentes()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2809);
        var classes = new HashSet<(string, TipoEmitente, bool)>();
        foreach (var modelo in _modelos.SelectMany(modelo => Enumerable.Repeat(modelo, modelo is "55" or "57" ? 15 : 4)))
        {
            var base43 = ChaveSorteada(sorteio, modelo)[..43];
            var classificacao = ClassificadorEmitente.Classificar(base43 + "0");
            classes.Add((modelo, classificacao.Tipo, classificacao.EhNff));

            // Na NFF da NF-e e da NFC-e, a posição 30 decide o emitente e fica.
            var nffComTipo = modelo is "55" or "65" && ClassificadorEmitente.Classificar(base43 + "0").EhNff;

            // Só no cDV: os 10.
            Assert.Equal(10, Enumerable.Range(0, 10).Select(dv => Pseudonimizar(chaveDfe, base43 + (char)('0' + dv))).Distinct().Count());

            // Só no número (inclusive o 0) e só no código (inclusive os proibidos), com o cDV de cada uma.
            var numeros = Enumerable.Range(0, Mutacao.Tamanho(500, 50))
                .Select(i => i == 0 ? "000000000" : Digitos(sorteio, 9))
                .Select(numero => nffComTipo ? numero[..4] + base43[29] + numero[5..] : numero)
                .Distinct().ToArray();
            var porNumero = numeros.Select(numero => ComCdv(base43[..25] + numero + base43[34..43])).ToArray();
            Assert.All(porNumero, chave => Assert.True(Classificada(chave), "Número que tirou a classificação."));
            Assert.Equal(porNumero.Length, porNumero.Select(chave => Pseudonimizar(chaveDfe, chave)).Distinct().Count());

            // Na NF3e e na NFCom, o código tem 7 dígitos e a posição 36 (nSiteAutoriz) fica.
            var setes = modelo is "62" or "66";
            var codigos = (setes ? Enumerable.Empty<string>() : RegrasChaveDfe.CodigosNumericosProibidos.ToArray().Select(codigo => codigo.ToString("00000000", CultureInfo.InvariantCulture)))
                .Concat(Enumerable.Range(0, Mutacao.Tamanho(500, 50)).Select(_ => Digitos(sorteio, setes ? 7 : 8))).Distinct().ToArray();
            var porCodigo = codigos.Select(codigo => ComCdv(base43[..(43 - codigo.Length)] + codigo)).ToArray();
            Assert.Equal(porCodigo.Length, porCodigo.Select(chave => Pseudonimizar(chaveDfe, chave)).Distinct().Count());
        }

        // NF-e e CT-e: CPF e CNPJ, com e sem NFF.
        foreach (var modelo in new[] { "55", "57" })
        {
            Assert.Contains((modelo, TipoEmitente.Cpf, false), classes);
            Assert.Contains((modelo, TipoEmitente.Cnpj, false), classes);
            Assert.True(classes.Contains((modelo, TipoEmitente.Cpf, true)) || classes.Contains((modelo, TipoEmitente.Cnpj, true)), "Sem NFF na amostra.");
        }
    }

    [Fact]
    public void Amostra_misturada_com_CPF_e_CNPJ_nos_mesmos_campos_nao_colide()
    {
        // Fronteira entre classes: os mesmos cUF, AAMM, modelo, série, número e código com emitentes CPF e CNPJ.
        using var chaveDfe = Criar();
        var sorteio = new Random(2810);
        var entradas = new HashSet<string>();
        foreach (var modelo in new[] { "57", "58", "55" })
        {
            var serie = modelo == "55" ? "920" : "001";
            for (var vez = 0; vez < Mutacao.Tamanho(3_000, 300); vez++)
            {
                var emitente = vez % 2 == 0 ? "000" + CpfValido(sorteio) : CnpjNumericoSemZerosDoCpf(sorteio);
                if (modelo == "55" && vez % 2 == 1)
                {
                    continue;
                }

                entradas.Add(Montar(new Random(vez), "2601", emitente, modelo, serie, "000001234", '1', "00009876"));
            }
        }

        Assert.Equal(entradas.Count, entradas.Select(chave => Pseudonimizar(chaveDfe, chave)).Distinct().Count());
    }

    [Fact]
    public void A_mesma_chave_com_e_sem_prefixo_em_grupos_e_com_preenchimento_da_o_mesmo_pseudonimo()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2811);
        foreach (var modelo in _modelos)
        {
            for (var vez = 0; vez < 30; vez++)
            {
                var entrada = ChaveSorteada(sorteio, modelo);
                var pura = Pseudonimizar(chaveDfe, entrada);
                var prefixo = Caixa(sorteio, PrefixoDoModelo(modelo));

                chaveDfe.Pseudonimizar(prefixo + entrada, out var comPrefixo, out _);
                chaveDfe.Pseudonimizar(EmGrupos(entrada) + "   ", out var emGrupos, out _);
                chaveDfe.Pseudonimizar(entrada + "      ", out var preenchida, out _);
                chaveDfe.Pseudonimizar(prefixo + entrada + " ", out var comPrefixoEPreenchimento, out _);

                Assert.True(comPrefixo == prefixo + pura, "O prefixo não voltou como veio ou as 44 posições mudaram.");
                Assert.True(emGrupos == EmGrupos(pura) + "   ", "Os grupos não voltaram como vieram ou as 44 posições mudaram.");
                Assert.True(preenchida == pura + "      ", "O preenchimento não voltou como veio.");
                Assert.True(comPrefixoEPreenchimento == prefixo + pura + " ", "Prefixo e preenchimento não voltaram como vieram.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void A_classificacao_da_saida_e_a_da_entrada_e_nunca_anomalia(bool permutarOrdem)
    {
        using var chaveDfe = Criar(cnpj: permutarOrdem ? _cnpjComOrdem : _cnpj);
        var sorteio = new Random(permutarOrdem ? 2812 : 2836);
        var classes = new HashSet<(TipoEmitente, bool, bool, string)>();
        for (var vez = 0; vez < Mutacao.Tamanho(20_000, 1_000); vez++)
        {
            var entrada = ChaveSorteada(sorteio, _modelos[sorteio.Next(_modelos.Length)]);
            var antes = ClassificadorEmitente.Classificar(entrada);

            var depois = ClassificadorEmitente.Classificar(Pseudonimizar(chaveDfe, entrada));

            Assert.False(depois.EhAnomalia);
            Assert.True(depois.Tipo == antes.Tipo && depois.EhNff == antes.EhNff && depois.ExcluiRaizComZeros == antes.ExcluiRaizComZeros && depois.Leiaute == antes.Leiaute, "A classificação mudou.");
            classes.Add((antes.Tipo, antes.EhNff, antes.ExcluiRaizComZeros, entrada[20..22]));
        }

        // 55 e 65: CPF e CNPJ, com e sem NFF; 57 e 58: CPF, CNPJ com e sem exclusão, com e sem NFF; os outros, CNPJ.
        Assert.True(classes.Count >= 25, "A amostra não cobriu as classes.");
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Campos_preservados_ficam_e_o_nSiteAutoriz_fica_na_NF3e_e_na_NFCom(string modelo)
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2813);
        for (var vez = 0; vez < 300; vez++)
        {
            var entrada = ChaveSorteada(sorteio, modelo);

            var saida = Pseudonimizar(chaveDfe, entrada);

            Assert.True(saida[..6] == entrada[..6] && saida[20..25] == entrada[20..25] && saida[34] == entrada[34], "cUF, AAMM, modelo, série ou tpEmis mudou.");
            Assert.True(modelo is not ("62" or "66") || saida[35] == entrada[35], "O nSiteAutoriz mudou.");
        }
    }

    [Fact]
    public void Emitente_em_ponto_fixo_e_DV_interno_errado_sao_contados_e_o_resto_segue()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2814);

        // CPF de base zero (repdígito) com DV certo, na NF-e com série de CPF.
        var cpfZero = "000" + "000000000" + DvCpfClassico("000000000").ToString("00", CultureInfo.InvariantCulture);
        var comCpfZero = Montar(sorteio, "2601", cpfZero, "55", "920", "000012345", '1', "00012345");
        var saida = Pseudonimizar(chaveDfe, comCpfZero, out var ocorrencias);
        Assert.Equal(OcorrenciasChaveDfe.EmitentePontoFixo, ocorrencias);
        Assert.True(saida[6..20] == comCpfZero[6..20] && saida[25..34] != comCpfZero[25..34], "O emitente mudou ou o número ficou.");

        // CNPJ de raiz zero, montado na hora, na NF-e com série de CNPJ.
        var raizZero = CnpjDeRaizZero();
        saida = Pseudonimizar(chaveDfe, Montar(sorteio, "2601", raizZero, "55", "001", "000012345", '1', "00012345"), out ocorrencias);
        Assert.Equal(OcorrenciasChaveDfe.EmitentePontoFixo, ocorrencias);
        Assert.True(saida[6..20] == raizZero, "O CNPJ de raiz zero mudou.");

        // DV interno errado: deslocamento mantido, igual à coluna que preserva.
        var cnpjValido = CnpjValido(sorteio, alfanumerico: true);
        var dvErrado = cnpjValido[..12] + ((int.Parse(cnpjValido[12..], CultureInfo.InvariantCulture) + 37) % 100).ToString("00", CultureInfo.InvariantCulture);
        saida = Pseudonimizar(chaveDfe, Montar(sorteio, "2601", dvErrado, "55", "001", "000012345", '1', "00012345"), out ocorrencias);
        _cnpj.Pseudonimizar(dvErrado, out var daColuna);
        Assert.Equal(OcorrenciasChaveDfe.DvDoEmitenteInvalido, ocorrencias);
        Assert.True(saida[6..20] == daColuna, "O CNPJ com DV errado difere da coluna que preserva.");
        Assert.False(ValidadorBr.Cnpj(saida[6..20]));

        // Repdígito com DV errado: ponto fixo e DV errado.
        var cpfZeroErrado = cpfZero[..12] + ((cpfZero[12] - '0' + 1) % 10) + cpfZero[13..];
        saida = Pseudonimizar(chaveDfe, Montar(sorteio, "2601", cpfZeroErrado, "55", "920", "000012345", '1', "00012345"), out ocorrencias);
        Assert.Equal(OcorrenciasChaveDfe.EmitentePontoFixo | OcorrenciasChaveDfe.DvDoEmitenteInvalido, ocorrencias);
        Assert.True(saida[6..20] == cpfZeroErrado, "O CPF repdígito com DV errado mudou.");
    }

    [Fact]
    public void Ocorrencias_do_numero_e_do_codigo_saem_junto_com_as_do_emitente()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2815);
        var entrada = Montar(sorteio, "2601", CnpjNumericoSemZerosDoCpf(sorteio), "57", "001", "000000000", '1', "12345678");

        Pseudonimizar(chaveDfe, entrada, out var ocorrencias);

        Assert.Equal(OcorrenciasChaveDfe.NumeroZero | OcorrenciasChaveDfe.CodigoProibido, ocorrencias);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Chave_identica_a_entrada_quando_tudo_e_ponto_fixo_e_ponto_fixo_com_as_ocorrencias(int deslocamento)
    {
        // NF-e antes do emitente CPF (AAMM < 1808), CNPJ de raiz zero, número 0 e código proibido: nada muda, nem com o
        // cDV errado preservado.
        using var chaveDfe = Criar();
        var sorteio = new Random(2816);
        var entrada = ComCdvDeslocado(Montar(sorteio, "1701", CnpjDeRaizZero(), "55", "001", "000000000", '1', "00000000"), deslocamento);

        var desfecho = chaveDfe.Pseudonimizar(entrada, out var saida, out var ocorrencias);

        Assert.Equal(Desfecho.PontoFixo, desfecho);
        Assert.Same(entrada, saida);
        Assert.Equal(OcorrenciasChaveDfe.EmitentePontoFixo | OcorrenciasChaveDfe.NumeroZero | OcorrenciasChaveDfe.CodigoProibido, ocorrencias);
    }

    [Fact]
    public void Chave_com_alguma_parte_trocada_nao_e_ponto_fixo()
    {
        // Só o emitente em ponto fixo: o número muda e a chave é pseudonimizada.
        using var chaveDfe = Criar();
        var sorteio = new Random(2835);
        var entrada = Montar(sorteio, "1701", CnpjDeRaizZero(), "55", "001", "000012345", '1', "00000000");

        Assert.Equal(Desfecho.Pseudonimizado, chaveDfe.Pseudonimizar(entrada, out _, out _));
    }

    [Theory]
    [MemberData(nameof(FormasMalformadas))]
    public void Formas_malformadas_seguem_a_politica_sem_o_valor_na_mensagem(string caso)
    {
        var sorteio = new Random(2817);
        var valida = Montar(sorteio, "2601", CnpjValido(sorteio, alfanumerico: true), "55", "001", Digitos(sorteio, 9), '1', CodigoQualquer(sorteio));
        var entrada = Estragar(valida, caso);

        AssertarMalformado(entrada, OcorrenciasChaveDfe.Nenhuma, "Valor fora da forma de chave de acesso de DF-e");
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("ABCD")]
    [InlineData("ABCDE")]
    [InlineData("NFe ")]
    public void Prefixo_desconhecido_antes_da_chave_zerada_segue_a_politica(string prefixo)
    {
        AssertarMalformado(prefixo + new string('0', 44), OcorrenciasChaveDfe.Nenhuma, "Valor fora da forma de chave de acesso de DF-e");
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Prefixo_de_outro_modelo_segue_a_politica(string modelo)
    {
        var sorteio = new Random(2830);
        var entrada = ChaveSorteada(sorteio, modelo);
        using var chaveDfe = Criar(malformada: PoliticaEntradaMalformada.Anular);
        foreach (var prefixo in new[] { "NFe", "CTe", "MDFe", "BPe", "NF3e", "NFCom" })
        {
            var esperado = prefixo == PrefixoDoModelo(modelo) ? Desfecho.Pseudonimizado : Desfecho.AnuladoMalformado;
            Assert.Equal(esperado, chaveDfe.Pseudonimizar(prefixo + entrada, out _, out _));
        }
    }

    [Theory]
    [InlineData(51, "")]
    [InlineData(53, "")]
    [InlineData(50, "NFT")]
    public void Valor_alfanumerico_que_nao_e_a_chave_da_NFS_e_nao_recebe_a_dica(int tamanho, string prefixo)
    {
        var sorteio = new Random(2831);
        var entrada = prefixo + new string([.. Enumerable.Range(0, tamanho).Select(_ => Alfanumerico[sorteio.Next(36)])]);
        using var chaveDfe = Criar();

        var excecao = Assert.Throws<IncognitoException>(() => chaveDfe.Pseudonimizar(entrada, out _, out _));
        Assert.DoesNotContain("br.chave-nfse", excecao.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('-')]
    [InlineData(' ')]
    [InlineData('é')]
    public void Valor_de_50_posicoes_com_caractere_que_nao_e_letra_nem_digito_nao_recebe_a_dica(char intruso)
    {
        var sorteio = new Random(2834);
        var entrada = Digitos(sorteio, 25) + intruso + Digitos(sorteio, 24);
        using var chaveDfe = Criar();

        var excecao = Assert.Throws<IncognitoException>(() => chaveDfe.Pseudonimizar(entrada, out _, out _));
        Assert.DoesNotContain("br.chave-nfse", excecao.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NFS")]
    [InlineData("nfs")]
    public void Chave_de_50_posicoes_da_NFS_e_segue_a_politica_com_a_dica(string prefixo)
    {
        var sorteio = new Random(2818);
        var nfse = Digitos(sorteio, 9) + CnpjValido(sorteio, alfanumerico: true) + Digitos(sorteio, 27);
        var entrada = prefixo + nfse;

        AssertarMalformado(entrada, OcorrenciasChaveDfe.Nenhuma, "br.chave-nfse");
    }

    [Theory]
    [InlineData("55", "2601", "920", true, '1', "123456789")]
    [InlineData("55", "2601", "995", false, '1', "123456789")]
    [InlineData("65", "2601", "975", false, '1', "123456789")]
    [InlineData("55", "1901", "001", false, '3', "123456789")]
    [InlineData("55", "2601", "025", false, '3', "150730001")]
    public void Chave_sem_emitente_classificavel_segue_a_politica_e_e_contada_como_anomalia(string modelo, string aamm, string serie, bool cpfSemZeros, char tipoEmissao, string numero)
    {
        // CPF sem os zeros à esquerda, série sem emissão (na NF-e e, sem PAA, na NFC-e), tpEmis 3 entre o SCAN e a NFF e
        // NFF com o tipo do emissor fora de 1 e 2.
        var sorteio = new Random(2819);
        var emitente = cpfSemZeros ? "123" + CpfValido(sorteio) : CnpjNumericoSemZerosDoCpf(sorteio);
        var entrada = Montar(sorteio, aamm, emitente, modelo, serie, numero, tipoEmissao, CodigoQualquer(sorteio));
        Assert.True(ClassificadorEmitente.Classificar(entrada).EhAnomalia, "O caso não é anomalia.");

        AssertarMalformado(entrada, OcorrenciasChaveDfe.Anomalia, "sem emitente classificável");
    }

    [Theory]
    [InlineData("")]
    [InlineData("NFe")]
    [InlineData("cte")]
    [InlineData("MDFe")]
    [InlineData("BPe")]
    [InlineData("NF3e")]
    [InlineData("NFCom")]
    [InlineData("grupos")]
    [InlineData("preenchida")]
    public void Chave_zerada_volta_como_veio_em_toda_politica(string forma)
    {
        var zerada = new string('0', 44);
        var entrada = forma switch
        {
            "grupos" => EmGrupos(zerada),
            "preenchida" => zerada + "    ",
            _ => forma + zerada,
        };
        foreach (var malformada in Enum.GetValues<PoliticaEntradaMalformada>())
        {
            foreach (var invalida in Enum.GetValues<PoliticaEntradaInvalida>())
            {
                using var chaveDfe = Criar(invalida: invalida, malformada: malformada);

                var desfecho = chaveDfe.Pseudonimizar(entrada, out var saida, out var ocorrencias);

                Assert.Equal(Desfecho.Zerado, desfecho);
                Assert.Same(entrada, saida);
                Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
            }
        }

        // Também numa coluna que só aceita NF-e: a chave zerada não tem modelo.
        using var soNfe = Criar(modelos: [55], malformada: PoliticaEntradaMalformada.Falhar);
        Assert.Equal(Desfecho.Zerado, soNfe.Pseudonimizar(entrada, out _, out _));
    }

    [Theory]
    [InlineData("anomalia")]
    [InlineData("modelo fora da coluna")]
    [InlineData("prefixo de outro modelo")]
    public void Malformado_com_cDV_errado_segue_a_politica_de_malformado_e_nao_a_de_DV(string caso)
    {
        var sorteio = new Random(2837);
        var entrada = caso switch
        {
            "anomalia" => Montar(sorteio, "2601", CnpjNumericoSemZerosDoCpf(sorteio), "55", "995", "000012345", '1', "00012345"),
            "modelo fora da coluna" => ChaveSorteada(sorteio, "57"),
            _ => "CTe" + ChaveSorteada(sorteio, "55"),
        };
        entrada = entrada[..^1] + (char)('0' + ((entrada[^1] - '0' + 3) % 10));
        using var chaveDfe = Criar(modelos: [55, 65], invalida: PoliticaEntradaInvalida.Falhar, malformada: PoliticaEntradaMalformada.Falhar);

        var excecao = Assert.Throws<IncognitoException>(() => chaveDfe.Pseudonimizar(entrada, out _, out _));

        Assert.Equal("INC1003", excecao.Codigo);
        using var anular = Criar(modelos: [55, 65], invalida: PoliticaEntradaInvalida.Falhar, malformada: PoliticaEntradaMalformada.Anular);
        Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out _, out var ocorrencias));
        Assert.Equal(caso == "anomalia" ? OcorrenciasChaveDfe.Anomalia : OcorrenciasChaveDfe.Nenhuma, ocorrencias);
    }

    [Fact]
    public void Anomalia_e_contada_mesmo_com_modelo_fora_da_coluna_ou_prefixo_de_outro_modelo()
    {
        var sorteio = new Random(2838);
        var anomala = Montar(sorteio, "2601", CnpjNumericoSemZerosDoCpf(sorteio), "55", "995", "000012345", '1', "00012345");
        using var soCte = Criar(modelos: [57], malformada: PoliticaEntradaMalformada.Anular);

        soCte.Pseudonimizar(anomala, out _, out var foraDaColuna);
        soCte.Pseudonimizar("CTe" + anomala, out _, out var comPrefixoErrado);

        Assert.Equal(OcorrenciasChaveDfe.Anomalia, foraDaColuna);
        Assert.Equal(OcorrenciasChaveDfe.Anomalia, comPrefixoErrado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("      ")]
    public void Vazio_volta_como_veio(string entrada)
    {
        using var chaveDfe = Criar();

        Assert.Equal(Desfecho.Vazio, chaveDfe.Pseudonimizar(entrada, out var saida, out _));
        Assert.Same(entrada, saida);
    }

    [Fact]
    public void cDV_errado_segue_a_politica_de_entrada_invalida()
    {
        var sorteio = new Random(2820);
        var entrada = ComCdvDeslocado(ChaveSorteada(sorteio, "55"), 3);

        using (var falhar = Criar(invalida: PoliticaEntradaInvalida.Falhar))
        {
            var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _, out _));
            Assert.Equal("INC1004", excecao.Codigo);
            Assert.Contains("Chave de DF-e", excecao.Message, StringComparison.Ordinal);
            AssertarSemTrechoDaEntrada(entrada, excecao);
        }

        using (var anular = Criar(invalida: PoliticaEntradaInvalida.Anular))
        {
            Assert.Equal(Desfecho.AnuladoDvInvalido, anular.Pseudonimizar(entrada, out var saida, out var ocorrencias));
            Assert.Null(saida);
            Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
        }

        using var preservar = Criar(invalida: PoliticaEntradaInvalida.Preservar);
        Assert.Equal(Desfecho.DvInvalidoPreservado, preservar.Pseudonimizar(entrada, out var preservada, out _));
        Assert.Equal(3, Deslocamento(preservada!));
    }

    [Fact]
    public void Modelo_fora_dos_aceitos_pela_coluna_segue_a_politica()
    {
        var sorteio = new Random(2821);
        using var soNfe = Criar(modelos: [55], malformada: PoliticaEntradaMalformada.Anular);

        Assert.Equal(Desfecho.Pseudonimizado, soNfe.Pseudonimizar(ChaveSorteada(sorteio, "55"), out _, out _));
        Assert.Equal(Desfecho.AnuladoMalformado, soNfe.Pseudonimizar(ChaveSorteada(sorteio, "65"), out var saida, out var ocorrencias));
        Assert.Null(saida);
        Assert.Equal(OcorrenciasChaveDfe.Nenhuma, ocorrencias);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 59 })]
    [InlineData(new[] { 55, 0 })]
    [InlineData(new[] { 55, 100 })]
    [InlineData(new[] { 55, 311 })]
    [InlineData(new[] { -1 })]
    public void Lista_de_modelos_vazia_ou_com_codigo_fora_dos_nove_e_recusada(int[] codigos)
    {
        Assert.Throws<ArgumentException>("modelos", () => Criar(modelos: codigos));
    }

    [Fact]
    public void Construtor_recusa_politica_desconhecida_pseudonimizador_nulo_ou_de_outro_ambiente()
    {
        Assert.Throws<ArgumentOutOfRangeException>("invalida", () => Criar(invalida: (PoliticaEntradaInvalida)99));
        Assert.Throws<ArgumentOutOfRangeException>("malformada", () => Criar(malformada: (PoliticaEntradaMalformada)99));
        Assert.Throws<ArgumentNullException>("cpf", () => new PseudonimizadorChaveDfe(_chave, 1, "homologacao", null!, _cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar));
        Assert.Throws<ArgumentNullException>("cnpj", () => new PseudonimizadorChaveDfe(_chave, 1, "homologacao", _cpf, null!, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar));
        Assert.Throws<ArgumentException>("cpf", () => new PseudonimizadorChaveDfe(_chave, 1, "producao", _cpf, _cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar));
        using var cnpjDeOutroAmbiente = new PseudonimizadorCnpj(_chave, "cnpj", 1, "producao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        Assert.Throws<ArgumentException>("cnpj", () => new PseudonimizadorChaveDfe(_chave, 1, "homologacao", _cpf, cnpjDeOutroAmbiente, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar));
    }

    [Fact]
    public void A_politica_de_DV_da_coluna_nao_vale_dentro_da_chave()
    {
        // A coluna de CPF falha com DV errado; na chave, o DV interno errado só é contado.
        using var cpfQueFalha = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = new PseudonimizadorChaveDfe(_chave, 1, "homologacao", cpfQueFalha, _cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(2822);
        var cpf = CpfValido(sorteio);
        var cpfErrado = cpf[..9] + ((int.Parse(cpf[9..], CultureInfo.InvariantCulture) + 1) % 100).ToString("00", CultureInfo.InvariantCulture);

        chaveDfe.Pseudonimizar(Montar(sorteio, "2601", "000" + cpfErrado, "57", "001", "000012345", '1', "00012345"), out var saida, out var ocorrencias);

        Assert.Equal(OcorrenciasChaveDfe.DvDoEmitenteInvalido, ocorrencias);
        Assert.NotNull(saida);
    }

    [Fact]
    public void Configuracoes_diferentes_dao_numero_e_codigo_diferentes_e_o_mesmo_emitente()
    {
        var sorteio = new Random(2823);
        var entrada = Montar(sorteio, "2601", CnpjValido(sorteio, alfanumerico: false), "55", "001", "000012345", '1', "00054321");
        using var versao1 = Criar();
        using var versao2 = Criar(versao: 2);

        var uma = Pseudonimizar(versao1, entrada);
        var outra = Pseudonimizar(versao2, entrada);

        Assert.True(uma[6..20] == outra[6..20], "A versão do br.chave-dfe mudou o emitente.");
        Assert.True(uma[25..34] != outra[25..34] && uma[35..43] != outra[35..43], "A versão do br.chave-dfe não mudou o número e o código.");
    }

    [Fact]
    public void Entrada_nula_e_recusada()
    {
        using var chaveDfe = Criar();

        Assert.Throws<ArgumentNullException>("entrada", () => chaveDfe.Pseudonimizar(null!, out _, out _));
        Assert.Throws<ArgumentNullException>("ambiente", () => new PseudonimizadorChaveDfe(_chave, 1, null!, _cpf, _cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar));
    }

    [Theory]
    [MemberData(nameof(ComESemOrdem))]
    public void CNPJ_de_raiz_numerica_com_ordem_alfanumerica_e_CNPJ_e_bate_com_a_coluna_inclusive_com_raiz_000(bool permutarOrdem)
    {
        // A letra decide antes dos zeros: no CT-e, até a raiz 000 com ordem alfanumérica é CNPJ, sem exclusão.
        var cnpj = permutarOrdem ? _cnpjComOrdem : _cnpj;
        using var chaveDfe = Criar(cnpj: cnpj);
        var sorteio = new Random(permutarOrdem ? 2840 : 2839);
        for (var vez = 0; vez < 400; vez++)
        {
            var raiz = vez % 2 == 0 ? "000" + Digitos(sorteio, 5) : vez % 4 == 1 ? Digitos(sorteio, 8) : "000" + Alfanumerico[10 + sorteio.Next(26)] + Digitos(sorteio, 4);
            var ordem = raiz.Any(char.IsAsciiLetterUpper) ? Digitos(sorteio, 4) : (char)('A' + sorteio.Next(26)) + Digitos(sorteio, 3);
            var emitente = raiz + ordem + DvCnpjClassico(raiz + ordem).ToString("00", CultureInfo.InvariantCulture);
            var entrada = Montar(sorteio, "2601", emitente, _modelos[vez % 4 == 3 ? 0 : 2 + (vez % 2)], "001", Digitos(sorteio, 9), '1', Digitos(sorteio, 8));
            var antes = ClassificadorEmitente.Classificar(entrada);

            chaveDfe.Pseudonimizar(entrada, out var saida, out _);
            cnpj.Pseudonimizar(emitente, out var daColuna);

            Assert.Equal(TipoEmitente.Cnpj, antes.Tipo);
            Assert.False(antes.ExcluiRaizComZeros);
            Assert.True(saida![6..20] == daColuna, "O CNPJ com letra difere da coluna.");
            Assert.Equal(TipoEmitente.Cnpj, ClassificadorEmitente.Classificar(saida).Tipo);
        }
    }

    [Theory]
    [InlineData("55", "1807", '1', "920")]
    [InlineData("55", "1808", '1', "920")]
    [InlineData("55", "2103", '1', "001")]
    [InlineData("55", "2104", '3', "025")]
    [InlineData("55", "2601", '3', "975")]
    [InlineData("55", "2601", '3', "985")]
    [InlineData("57", "2005", '3', "001")]
    [InlineData("57", "2006", '3', "020")]
    [InlineData("57", "2601", '3', "975")]
    [InlineData("58", "2601", '8', "001")]
    public void Fronteiras_das_epocas_e_o_PAA_mantem_a_classificacao_e_o_recalculo(string modelo, string aamm, char tipoEmissao, string serie)
    {
        using var chaveDfe = Criar();
        using var numeroDfe = new PseudonimizadorNumeroDfe(_chave, PseudonimizadorNumeroDfe.NomeDoDominio, 1, "homologacao");
        var sorteio = new Random(2841);
        var classesVistas = new HashSet<(TipoEmitente, bool)>();
        for (var vez = 0; vez < 200; vez++)
        {
            var cpf = vez % 2 == 0;
            var emitente = cpf ? "000" + CpfValido(sorteio) : CnpjNumericoSemZerosDoCpf(sorteio);
            var numero = tipoEmissao == '3' && serie is "025" or "020"
                ? (modelo == "55" ? "15" + aamm[2..] + (cpf ? '2' : '1') + Digitos(sorteio, 4) : aamm[2..] + "15" + Digitos(sorteio, 5))
                : Digitos(sorteio, 9);
            var entrada = Montar(sorteio, aamm, emitente, modelo, serie, numero, tipoEmissao, Digitos(sorteio, 8));
            var antes = ClassificadorEmitente.Classificar(entrada);
            if (antes.EhAnomalia)
            {
                continue;
            }

            var saida = Pseudonimizar(chaveDfe, entrada);
            var depois = ClassificadorEmitente.Classificar(saida);

            Assert.True(depois.Tipo == antes.Tipo && depois.EhNff == antes.EhNff, "A classificação mudou na fronteira.");
            Assert.True(Recalcular(entrada, _cnpj, numeroDfe) == saida, "A saída difere do recálculo na fronteira.");
            classesVistas.Add((antes.Tipo, antes.EhNff));
        }

        Assert.NotEmpty(classesVistas);
    }

    [Fact]
    public void Descartado_recusa_uso_e_o_descarte_duplo_e_inofensivo()
    {
        var sorteio = new Random(2824);
        var entrada = ChaveSorteada(sorteio, "55");
        var chaveDfe = Criar();
        chaveDfe.Dispose();
        chaveDfe.Dispose();

        Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar(entrada, out _, out _));
        Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar(new string('0', 44), out _, out _));
        Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar("NFe", out _, out _));
    }

    [Fact]
    public void Pseudonimizador_do_emitente_descartado_e_recusado_mesmo_com_emitente_em_ponto_fixo()
    {
        // CPF repdígito e CNPJ de raiz zero não chegam à permutação: só a conferência do descarte os recusa.
        var sorteio = new Random(2832);
        var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = new PseudonimizadorChaveDfe(_chave, 1, "homologacao", cpf, cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        cpf.Dispose();
        cnpj.Dispose();
        var cpfZero = "000" + "000000000" + DvCpfClassico("000000000").ToString("00", CultureInfo.InvariantCulture);
        var raizZero = CnpjDeRaizZero();

        Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar(Montar(sorteio, "2601", cpfZero, "55", "920", "000012345", '1', "00012345"), out _, out _));
        Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar(Montar(sorteio, "2601", raizZero, "55", "001", "000012345", '1', "00012345"), out _, out _));
    }

    [Fact]
    public void Pseudonimizadores_do_emitente_de_outro_ambiente_que_bate_sao_aceitos()
    {
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "producao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "producao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = new PseudonimizadorChaveDfe(_chave, 1, "producao", cpf, cnpj, null, PoliticaEntradaInvalida.Falhar, PoliticaEntradaMalformada.Falhar);
        var sorteio = new Random(2833);
        var entrada = Montar(sorteio, "2601", "000" + CpfValido(sorteio), "57", "001", "000012345", '1', "00012345");

        chaveDfe.Pseudonimizar(entrada, out var saida, out _);
        cpf.Pseudonimizar(entrada.Substring(9, 11), out var daColuna);

        Assert.True(saida![9..20] == daColuna, "O CPF da chave difere do da coluna do mesmo ambiente.");
    }

    [Fact]
    public void Pseudonimizador_do_emitente_descartado_e_recusado_sem_o_valor()
    {
        var sorteio = new Random(2825);
        var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var chaveDfe = Criar(cnpj: cnpj);
        cnpj.Dispose();
        var entrada = Montar(sorteio, "2601", CnpjValido(sorteio, alfanumerico: false), "55", "001", "000012345", '1', "00054321");

        var excecao = Assert.Throws<ObjectDisposedException>(() => chaveDfe.Pseudonimizar(entrada, out _, out _));
        AssertarSemTrechoDaEntrada(entrada, excecao);
    }

    [Fact]
    public void Pontos_de_entrada_da_chave_no_CPF_e_no_CNPJ_recusam_forma_errada_e_descarte()
    {
        using var cpf = new PseudonimizadorCpf(_chave, "cpf", 1, "homologacao", PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        using var cnpj = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);

        Assert.Throws<ArgumentException>("cpf", () => cpf.TransformarNaChave("1234567890".ToCharArray()));
        Assert.Throws<ArgumentException>("cpf", () => cpf.TransformarNaChave("1234567890A".ToCharArray()));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.TransformarNaChave("1234567890123".ToCharArray(), false));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.TransformarNaChave("1234567890123A".ToCharArray(), false));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.TransformarNaChave("12345678901a34".ToCharArray(), false));

        // Com a regra dos zeros, a raiz 000 numérica (com permutarOrdem, os 12 caracteres numéricos) é lida como CPF:
        // recusada, nunca copiada como ponto fixo. Com letra na raiz (ou nos 12), ou fora da regra, segue.
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.TransformarNaChave("00012345678901".ToCharArray(), true));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.TransformarNaChave("00000000000000".ToCharArray(), true));
        cnpj.TransformarNaChave("000A2345678901".ToCharArray(), true);
        cnpj.TransformarNaChave("00012345678901".ToCharArray(), false);
        using var cnpjComOrdem = new PseudonimizadorCnpj(_chave, "cnpj", 1, "homologacao", true, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar);
        Assert.Throws<ArgumentException>("cnpj", () => cnpjComOrdem.TransformarNaChave("00012345678901".ToCharArray(), true));
        cnpjComOrdem.TransformarNaChave("00012345A78901".ToCharArray(), true);

        // As inversas recusam as mesmas formas, inclusive a raiz que a regra dos zeros lê como CPF (nunca é saída).
        Assert.Throws<ArgumentException>("cpf", () => cpf.InverterNaChave("1234567890A".ToCharArray()));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.InverterNaChave("12345678901a34".ToCharArray(), false));
        Assert.Throws<ArgumentException>("cnpj", () => cnpj.InverterNaChave("00012345678901".ToCharArray(), true));
        Assert.Throws<ArgumentException>("cnpj", () => cnpjComOrdem.InverterNaChave("00012345678901".ToCharArray(), true));
        cpf.Dispose();
        cnpj.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cpf.TransformarNaChave("12345678901".ToCharArray()));
        Assert.Throws<ObjectDisposedException>(() => cnpj.TransformarNaChave("12345678901234".ToCharArray(), false));
        Assert.Throws<ObjectDisposedException>(() => cpf.InverterNaChave("12345678901".ToCharArray()));
        Assert.Throws<ObjectDisposedException>(() => cnpj.InverterNaChave("12345678901234".ToCharArray(), false));
    }

    [Fact]
    public void Inversa_segue_a_politica_de_malformado_como_a_ida()
    {
        // Forma errada e anomalia seguem a política de malformado também na inversa; a chave zerada volta como veio.
        var sorteio = new Random(2904);
        var anomala = Montar(sorteio, "2601", CnpjNumericoSemZerosDoCpf(sorteio), "55", "995", "000012345", '1', "00012345");
        var zerada = new string('0', 44);
        using var falhar = Criar(malformada: PoliticaEntradaMalformada.Falhar);
        using var anular = Criar(malformada: PoliticaEntradaMalformada.Anular);

        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falhar.Inverter(anomala[..43], out _, out _)).Codigo);
        Assert.Equal("INC1003", Assert.Throws<IncognitoException>(() => falhar.Inverter(anomala, out _, out _)).Codigo);
        Assert.Equal(Desfecho.AnuladoMalformado, anular.Inverter(anomala, out var anulada, out var ocorrencias));
        Assert.Null(anulada);
        Assert.Equal(OcorrenciasChaveDfe.Anomalia, ocorrencias);
        Assert.Equal(Desfecho.Zerado, falhar.Inverter(zerada, out var zeradaDeVolta, out _));
        Assert.Same(zerada, zeradaDeVolta);
    }

    [Fact]
    public void Inversa_desfaz_o_cDV_errado_mesmo_com_a_politica_de_falhar()
    {
        // A inversa não aplica a política de DV: um pseudônimo com cDV errado preservado volta numa instância que falharia.
        var sorteio = new Random(2903);
        using var preservar = Criar(invalida: PoliticaEntradaInvalida.Preservar);
        using var falhar = Criar(invalida: PoliticaEntradaInvalida.Falhar);
        for (var vez = 0; vez < 50; vez++)
        {
            var entrada = ComCdvDeslocado(ChaveSorteada(sorteio, _modelos[vez % _modelos.Length]), 1 + (vez % 9));
            preservar.Pseudonimizar(entrada, out var saida, out _);

            Assert.Equal(Desfecho.DvInvalidoPreservado, falhar.Inverter(saida!, out var volta, out _));
            Assert.True(volta == entrada, "A inversa não desfez o cDV errado.");
        }
    }

    [Fact]
    public void Uso_concorrente_da_o_mesmo_resultado_inclusive_com_a_coluna_ao_mesmo_tempo()
    {
        using var chaveDfe = Criar();
        var sorteio = new Random(2826);
        var entradas = Enumerable.Range(0, 2_000).Select(i => ChaveSorteada(sorteio, _modelos[i % _modelos.Length])).ToArray();
        var cnpjs = Enumerable.Range(0, 2_000).Select(_ => CnpjValido(sorteio, alfanumerico: false)).ToArray();
        var esperadas = entradas.Select(entrada => Pseudonimizar(chaveDfe, entrada)).ToArray();
        var esperadasDaColuna = cnpjs.Select(valor => { _cnpj.Pseudonimizar(valor, out var saida); return saida; }).ToArray();

        var saidas = new string?[entradas.Length];
        var daColuna = new string?[cnpjs.Length];
        Parallel.For(0, entradas.Length * 2, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            if (i % 2 == 0)
            {
                chaveDfe.Pseudonimizar(entradas[i / 2], out saidas[i / 2], out _);
            }
            else
            {
                _cnpj.Pseudonimizar(cnpjs[i / 2], out daColuna[i / 2]);
            }
        });

        Assert.True(esperadas.SequenceEqual(saidas), "O uso concorrente mudou a saída da chave.");
        Assert.True(esperadasDaColuna.SequenceEqual(daColuna), "O uso concorrente mudou a saída da coluna.");
    }

    private static string PrefixoDoModelo(string modelo) => modelo switch
    {
        "55" or "65" => "NFe",
        "57" or "64" or "67" => "CTe",
        "58" => "MDFe",
        "63" => "BPe",
        "66" => "NF3e",
        _ => "NFCom",
    };

    private static string Caixa(Random sorteio, string texto) =>
        new([.. texto.Select(c => sorteio.Next(3) == 0 ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c)]);

    private static string EmGrupos(string chave) => string.Join(' ', Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));

    private static string Estragar(string valida, string caso) => caso switch
    {
        "43 posições" => valida[..43],
        "45 posições" => valida + "0",
        "46 posições" => "00" + valida,
        "espaço à esquerda" => " " + valida,
        "tabulação à direita" => valida + "\t",
        "espaço não separável à direita" => valida + " ",
        "minúscula no emitente" => valida[..6] + char.ToLowerInvariant(LetraEm(valida)) + valida[7..],
        "letra na posição 19" => valida[..18] + "A" + valida[19..],
        "letra no cUF" => "A" + valida[1..],
        "dígito de largura total" => valida[..30] + "１" + valida[31..],
        "dígito árabe-índico" => valida[..30] + "٣" + valida[31..],
        "NUL no meio" => valida[..30] + "\0" + valida[31..],
        "prefixo desconhecido" => "NFC" + valida,
        "prefixo com homóglifo" => "NFе" + valida,
        "prefixo de outro modelo" => "CTe" + valida,
        "prefixo com espaço" => "NFe " + valida,
        "prefixo em grupos" => "NFe" + EmGrupos(valida),
        "grupos com espaço duplo" => EmGrupos(valida).Replace(" ", "  ", StringComparison.Ordinal),
        "grupos com hífen" => EmGrupos(valida).Replace(' ', '-'),
        "grupos com hífen só no primeiro separador" => EmGrupos(valida)[..4] + "-" + EmGrupos(valida)[5..],
        "grupos com hífen só no último separador" => EmGrupos(valida)[..49] + "-" + EmGrupos(valida)[50..],
        "grupos com espaço não separável" => EmGrupos(valida).Replace(' ', ' '),
        "grupo de 3 e de 5" => EmGrupos(valida)[..3] + " " + EmGrupos(valida)[3..4] + EmGrupos(valida)[5..],
        "espaço dentro de grupo" => valida[..2] + " " + valida[3..],
        "modelo 59" => ComCdv(valida[..20] + "59" + valida[22..43]),
        "modelo 00" => ComCdv(valida[..20] + "00" + valida[22..43]),
        "muito longo" => string.Concat(Enumerable.Repeat(valida, 25_000)),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    private static char LetraEm(string chave) => chave[6..18].First(char.IsAsciiLetterUpper);

    private static bool SoDigitosTexto(string texto) => texto.All(char.IsAsciiDigit);

    private static bool Classificada(string chave) => !ClassificadorEmitente.Classificar(chave).EhAnomalia;

    private static string Pseudonimizar(PseudonimizadorChaveDfe chaveDfe, string entrada) => Pseudonimizar(chaveDfe, entrada, out _);

    private static string Pseudonimizar(PseudonimizadorChaveDfe chaveDfe, string entrada, out OcorrenciasChaveDfe ocorrencias)
    {
        chaveDfe.Pseudonimizar(entrada, out var saida, out ocorrencias);
        return saida!;
    }

    // O caminho de baixo: colunas, outra instância do dfe-numero e o cDV clássico.
    private string Recalcular(string entrada, PseudonimizadorCnpj cnpj, PseudonimizadorNumeroDfe numeroDfe)
    {
        var classificacao = ClassificadorEmitente.Classificar(entrada);
        var caracteres = entrada.ToCharArray();
        if (classificacao.Tipo == TipoEmitente.Cpf)
        {
            _cpf.Pseudonimizar(entrada.Substring(9, 11), out var cpf);
            cpf!.CopyTo(0, caracteres, 9, 11);
        }
        else
        {
            cnpj.Pseudonimizar(entrada.Substring(6, 14), out var emitente);
            while (classificacao.ExcluiRaizComZeros && emitente!.StartsWith("000", StringComparison.Ordinal) && SoDigitosTexto(emitente[..12]))
            {
                cnpj.Pseudonimizar(emitente, out emitente);
            }

            emitente!.CopyTo(0, caracteres, 6, 14);
        }

        numeroDfe.Pseudonimizar(caracteres);
        var primeiros43 = new string(caracteres, 0, 43);
        return primeiros43 + (char)('0' + ((DvChaveClassico(primeiros43) + Deslocamento(entrada)) % 10));
    }

    private static int Deslocamento(string chave) => (chave[43] - '0' - DvChaveClassico(chave[..43]) + 10) % 10;

    private static string ComCdv(string primeiros43) => primeiros43 + (char)('0' + DvChaveClassico(primeiros43));

    private static string ComCdvDeslocado(string chave, int deslocamento) =>
        chave[..43] + (char)('0' + ((DvChaveClassico(chave[..43]) + deslocamento) % 10));

    // Chave com emitente classificado do modelo dado: épocas do AAMM (antes do emitente CPF, entre ele e a NFF, depois),
    // NFF, tpEmis 3 do PAA e do CT-e e MDF-e antes de 2006, CPF começando com 00, CNPJ numérico, alfanumérico e de raiz
    // numérica com ordem alfanumérica, DV do emitente errado, número 0 e códigos proibidos de vez em quando.
    private static string ChaveSorteada(Random sorteio, string modelo)
    {
        var transporte = modelo is "57" or "58";
        var notaFiscal = modelo is "55" or "65";
        for (var tentativa = 0; tentativa < 10_000; tentativa++)
        {
            var nff = (notaFiscal || transporte) && sorteio.Next(3) == 0;
            var paa = !nff && (modelo == "55" || transporte) && sorteio.Next(8) == 0;
            var tpEmis3Antigo = !nff && !paa && transporte && sorteio.Next(8) == 0;
            var ano = nff || paa ? 22 + sorteio.Next(6) : tpEmis3Antigo ? 15 + sorteio.Next(6) : sorteio.Next(3) switch
            {
                0 => 12 + sorteio.Next(6),
                1 => 18 + sorteio.Next(3),
                _ => 21 + sorteio.Next(7),
            };
            var mes = 1 + sorteio.Next(12);
            var aamm = ano.ToString("00", CultureInfo.InvariantCulture) + mes.ToString("00", CultureInfo.InvariantCulture);
            var cpf = (notaFiscal || transporte) && sorteio.Next(2) == 0;
            var emitente = ComDvDoEmitenteErradoAsVezes(sorteio, cpf ? "000" + CpfComZerosAsVezes(sorteio) : CnpjQualquer(sorteio));
            string serie;
            string numero;
            char tipoEmissao;
            if (nff)
            {
                tipoEmissao = '3';
                serie = sorteio.Next(2).ToString(CultureInfo.InvariantCulture) + ano.ToString("00", CultureInfo.InvariantCulture);
                var dia = sorteio.Next(15) == 0 ? sorteio.Next(100) : 1 + sorteio.Next(28);
                var sequencial = sorteio.Next(20) == 0 ? 0 : 1 + sorteio.Next(transporte ? 99_999 : 9_999);
                numero = transporte
                    ? mes.ToString("00", CultureInfo.InvariantCulture) + dia.ToString("00", CultureInfo.InvariantCulture) + sequencial.ToString("00000", CultureInfo.InvariantCulture)
                    : dia.ToString("00", CultureInfo.InvariantCulture) + mes.ToString("00", CultureInfo.InvariantCulture) + (cpf ? '2' : '1') + sequencial.ToString("0000", CultureInfo.InvariantCulture);
            }
            else
            {
                tipoEmissao = paa || tpEmis3Antigo ? '3' : "124567789"[sorteio.Next(9)];
                serie = paa
                    ? (970 + (modelo == "55" ? (cpf ? 0 : 10) + sorteio.Next(10) : sorteio.Next(20))).ToString(CultureInfo.InvariantCulture)
                    : notaFiscal && cpf
                        ? (910 + sorteio.Next(modelo == "55" ? 70 : 60)).ToString(CultureInfo.InvariantCulture)
                        : modelo == "55" && sorteio.Next(10) == 0 ? (980 + sorteio.Next(10)).ToString(CultureInfo.InvariantCulture) : sorteio.Next(910).ToString("000", CultureInfo.InvariantCulture);
                numero = sorteio.Next(50) == 0 ? "000000000" : Digitos(sorteio, 9);
            }

            var chave = Montar(sorteio, aamm, emitente, modelo, serie, numero, tipoEmissao, CodigoQualquer(sorteio));
            if (Classificada(chave))
            {
                return chave;
            }
        }

        Assert.Fail("Nenhuma chave classificada em 10 mil tentativas.");
        return string.Empty;
    }

    private static string CnpjQualquer(Random sorteio)
    {
        switch (sorteio.Next(4))
        {
            case 0:
                return CnpjValido(sorteio, alfanumerico: true);
            case 1:
                // Raiz numérica com ordem alfanumérica (filial nova de raiz antiga).
                var baseCnpj = Digitos(sorteio, 8) + (char)('A' + sorteio.Next(26)) + Digitos(sorteio, 3);
                return baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", CultureInfo.InvariantCulture);
            default:
                return CnpjValido(sorteio, alfanumerico: false);
        }
    }

    private static string ComDvDoEmitenteErradoAsVezes(Random sorteio, string emitente)
    {
        if (sorteio.Next(8) != 0)
        {
            return emitente;
        }

        var dv = int.Parse(emitente.AsSpan(12, 2), CultureInfo.InvariantCulture);
        return emitente[..12] + ((dv + 1 + sorteio.Next(99)) % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    // CNPJ de raiz zero com ordem 0000, que a Receita não atribui: o de ordem 0001 é cadastrado e nunca aparece.
    private static string CnpjDeRaizZero() => "000000000000" + DvCnpjClassico("000000000000").ToString("00", CultureInfo.InvariantCulture);

    private static bool EmitenteValido(string chave) =>
        ClassificadorEmitente.Classificar(chave).Tipo == TipoEmitente.Cpf ? ValidadorBr.Cpf(chave.AsSpan(9, 11)) : ValidadorBr.Cnpj(chave.AsSpan(6, 14));

    private static string CpfComZerosAsVezes(Random sorteio)
    {
        if (sorteio.Next(8) != 0)
        {
            return CpfValido(sorteio);
        }

        var baseCpf = "00" + Digitos(sorteio, 7);
        return baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string CnpjNumericoSemZerosDoCpf(Random sorteio)
    {
        string cnpj;
        do
        {
            cnpj = CnpjValido(sorteio, alfanumerico: false);
        }
        while (cnpj.StartsWith("000", StringComparison.Ordinal));

        return cnpj;
    }

    private static string CodigoQualquer(Random sorteio) =>
        sorteio.Next(20) == 0
            ? RegrasChaveDfe.CodigosNumericosProibidos[sorteio.Next(20)].ToString("00000000", CultureInfo.InvariantCulture)
            : Digitos(sorteio, 8);

    private static string Montar(Random sorteio, string aamm, string emitente, string modelo, string serie, string numero, char tipoEmissao, string codigo) =>
        ComCdv(UfQualquer(sorteio) + aamm + emitente + modelo + serie + numero + tipoEmissao + codigo);

    private static string UfQualquer(Random sorteio) => (11 + sorteio.Next(43)).ToString(CultureInfo.InvariantCulture);

    // Nenhum trecho de 4 letras ou dígitos da entrada nas mensagens da exceção e das internas (a pilha de chamadas não
    // traz valores, e os números de linha dariam falsos positivos); os 60 primeiros caracteres cobrem toda forma aceita
    // (até 54) e o começo das entradas longas, que repetem a mesma chave.
    private static void AssertarSemTrechoDaEntrada(string entrada, Exception excecao)
    {
        var texto = string.Empty;
        for (Exception? atual = excecao; atual is not null; atual = atual.InnerException)
        {
            texto += atual.Message + '\n';
        }

        for (var i = 0; i + 4 <= Math.Min(entrada.Length, 60); i++)
        {
            var trecho = entrada.Substring(i, 4);
            if (trecho.All(char.IsAsciiLetterOrDigit))
            {
                Assert.True(!texto.Contains(trecho, StringComparison.Ordinal) || "INC1003INC1004".Contains(trecho, StringComparison.Ordinal), "A mensagem traz um trecho da entrada.");
            }
        }
    }

    private void AssertarMalformado(string entrada, OcorrenciasChaveDfe ocorrenciasEsperadas, string trechoDaMensagem)
    {
        using (var falhar = Criar(malformada: PoliticaEntradaMalformada.Falhar))
        {
            var excecao = Assert.Throws<IncognitoException>(() => falhar.Pseudonimizar(entrada, out _, out _));
            Assert.Equal("INC1003", excecao.Codigo);
            Assert.Contains(trechoDaMensagem, excecao.Message, StringComparison.Ordinal);
            AssertarSemTrechoDaEntrada(entrada, excecao);
        }

        using (var anular = Criar(malformada: PoliticaEntradaMalformada.Anular))
        {
            Assert.Equal(Desfecho.AnuladoMalformado, anular.Pseudonimizar(entrada, out var saida, out var ocorrencias));
            Assert.Null(saida);
            Assert.Equal(ocorrenciasEsperadas, ocorrencias);
        }

        using var preservar = Criar(malformada: PoliticaEntradaMalformada.Preservar);
        Assert.Equal(Desfecho.CopiadoEmClaro, preservar.Pseudonimizar(entrada, out var copiada, out _));
        Assert.Same(entrada, copiada);
    }

    private static ChaveIncognito ChaveDoVetor()
    {
        using var documento = ArquivosDeTeste.Ler("vetores", "derivacao@1.json");
        return ChaveIncognito.Ler(ArquivosDeTeste.Texto(ArquivosDeTeste.Caso(documento, "chaves", "aleatoria"), "texto"));
    }

    private PseudonimizadorChaveDfe Criar(
        PseudonimizadorCnpj? cnpj = null,
        int[]? modelos = null,
        int versao = 1,
        PoliticaEntradaInvalida invalida = PoliticaEntradaInvalida.Preservar,
        PoliticaEntradaMalformada malformada = PoliticaEntradaMalformada.Falhar) =>
        new(_chave, versao, "homologacao", _cpf, cnpj ?? _cnpj, modelos, invalida, malformada);
}
