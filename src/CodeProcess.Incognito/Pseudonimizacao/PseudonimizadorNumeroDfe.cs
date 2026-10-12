using System.Security.Cryptography;
using CodeProcess.Incognito.Criptografia;
using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Documentos.Caracteres;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Domínio <c>dfe-numero</c>: permuta o número e o código numérico dentro da chave de DF-e, depois de o emitente já ter
/// sido pseudonimizado no lugar. A classificação é refeita sobre a própria chave (ela não muda com o emitente
/// pseudonimizado), para nenhuma classificação de outra chave decidir as partes. Mesma chave, domínio, versão e ambiente
/// dão o mesmo resultado; cada parte é uma bijeção no seu espaço, então chaves diferentes continuam diferentes.
/// </summary>
/// <remarks>
/// <para>
/// Número (posições 26 a 34): fora da Nota Fiscal Fácil, inteiro na variante <c>nnf</c> (10⁴ × 10⁵), com ajuste
/// <c>emitente' ‖ 0x00 ‖ mod ‖ série</c>; o número 0 fica. Na NFF, o dia na variante <c>nff-dia</c> (6 × 6, valor = o
/// próprio dia, 24 rodadas), entre os dias que existem no mês do AAMM e no mês do número, e o sequencial na variante
/// <c>nff-seq</c> (10² × 10² ou 10² × 10³), sem o 0; mês e tipo ficam. O ajuste do dia é
/// <c>emitente' ‖ 0x00 ‖ AAMM ‖ mod ‖ série ‖ número</c> com o dia e o sequencial em <c>0</c>; o do sequencial é o
/// mesmo com o dia já permutado no lugar, para o mesmo sequencial não sair igual em todos os dias do mês.
/// </para>
/// <para>
/// Código: de 8 dígitos (posições 36 a 43) na variante <c>cnf8</c>, sem os 20 proibidos nem o número pseudonimizado
/// (comparação numérica); de 7 dígitos (posições 37 a 43, NF3e e NFCom, com a posição 36 preservada) na variante
/// <c>cnf7</c>. Ajuste <c>emitente' ‖ número'</c>. Um código de entrada excluído fica como veio.
/// </para>
/// <para>
/// Cada ajuste só usa posições que a saída mostra e que nenhuma etapa seguinte muda, então a inversa refaz os mesmos
/// ajustes a partir da saída, desfazendo o código antes do número. Nenhuma exceção traz o valor. Pode ser usado por
/// várias threads ao mesmo tempo; o descarte é do dono, depois de todo uso.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorNumeroDfe : IDisposable
{
    /// <summary>Nome canônico do domínio dos números e códigos de todos os DF-e.</summary>
    internal const string NomeDoDominio = "dfe-numero";

    private const uint Radix = 10;
    private const int RodadasDoDia = 24;
    private const int IndiceDoEmitente = 6;
    private const int TamanhoDoEmitente = 14;
    private const int IndiceDoAamm = 2;
    private const int IndiceDoModelo = 20;
    private const int IndiceDoNumero = 25;
    private const int TamanhoDoNumero = 9;
    private const int IndiceDoCodigo8 = 35;
    private const int IndiceDoCodigo7 = 36;
    private const int PrimeiroDiaForaDoDominio = 36;
    private const ulong LimiteDoCodigo8 = 100_000_000;

    private static readonly ConjuntoExcluido _zero = ConjuntoExcluido.DeValores(0);
    private static readonly ConjuntoExcluido _codigosProibidos = CodigosProibidos();

    // Índice = dias válidos − 28: {0} ∪ os dias depois do último, até 35.
    private static readonly ConjuntoExcluido[] _diasExcluidos = [DiasExcluidos(28), DiasExcluidos(29), DiasExcluidos(30), DiasExcluidos(31)];

    private readonly PermutacaoFeistel _numero;
    private readonly PermutacaoFeistel _dia;
    private readonly PermutacaoFeistel _sequencial4;
    private readonly PermutacaoFeistel _sequencial5;
    private readonly PermutacaoFeistel _codigo8;
    private readonly PermutacaoFeistel _codigo7;
    private int _descartado;

    /// <summary>Prepara as permutações do domínio.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="dominio">Nome canônico do domínio (<see cref="NomeDoDominio"/>).</param>
    /// <param name="versao">Versão do <c>br.chave-dfe</c>, a partir de 1.</param>
    /// <param name="ambiente">Ambiente da configuração.</param>
    internal PseudonimizadorNumeroDfe(ChaveIncognito chave, string dominio, int versao, string ambiente)
    {
        PermutacaoFeistel? numero = null, dia = null, sequencial4 = null, sequencial5 = null, codigo8 = null, codigo7 = null;
        Span<byte> chaveDeDominio = stackalloc byte[Subchaves.Tamanho];
        try
        {
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.NumeroDfe, ambiente, chaveDeDominio);
            numero = new PermutacaoFeistel(chaveDeDominio, Radix, 10_000, 100_000);
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.DiaNff, ambiente, chaveDeDominio);
            dia = new PermutacaoFeistel(chaveDeDominio, Radix, 6, 6, RodadasDoDia);

            // Os dois tamanhos de sequencial usam a mesma chave, separados por a e b na função de rodada.
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.SequencialNff, ambiente, chaveDeDominio);
            sequencial4 = new PermutacaoFeistel(chaveDeDominio, Radix, 100, 100);
            sequencial5 = new PermutacaoFeistel(chaveDeDominio, Radix, 100, 1_000);
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.CodigoNumerico8, ambiente, chaveDeDominio);
            codigo8 = new PermutacaoFeistel(chaveDeDominio, Radix, 10_000, 10_000);
            Subchaves.DerivarChaveDeDominio(chave, dominio, versao, Variantes.CodigoNumerico7, ambiente, chaveDeDominio);
            codigo7 = new PermutacaoFeistel(chaveDeDominio, Radix, 1_000, 10_000);
        }
        catch
        {
            numero?.Dispose();
            dia?.Dispose();
            sequencial4?.Dispose();
            sequencial5?.Dispose();
            codigo8?.Dispose();
            codigo7?.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chaveDeDominio);
        }

        _numero = numero;
        _dia = dia;
        _sequencial4 = sequencial4;
        _sequencial5 = sequencial5;
        _codigo8 = codigo8;
        _codigo7 = codigo7;
    }

    /// <summary>Permuta o número e o código, no lugar.</summary>
    /// <param name="chave">
    /// As 44 posições, sem prefixo nem separadores, com emitente classificado e já pseudonimizado; o cDV não é tocado.
    /// Quem chama trabalha numa cópia: uma exceção no meio deixa a chave pela metade.
    /// </param>
    /// <returns>O que ficou como veio.</returns>
    /// <exception cref="ArgumentException">Chave fora da estrutura ou sem emitente classificado.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal OcorrenciasChaveDfe Pseudonimizar(Span<char> chave) => Pseudonimizar(chave, out _);

    /// <summary>
    /// Como <see cref="Pseudonimizar(Span{char})"/>, devolvendo a classificação lida na chave com o emitente já
    /// pseudonimizado, para quem chama conferir que ela não mudou.
    /// </summary>
    /// <param name="chave">As 44 posições, com o emitente já pseudonimizado.</param>
    /// <param name="classificacao">A classificação que decidiu as partes.</param>
    /// <returns>O que ficou como veio.</returns>
    /// <exception cref="ArgumentException">Chave fora da estrutura ou sem emitente classificado.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal OcorrenciasChaveDfe Pseudonimizar(Span<char> chave, out ClassificacaoEmitente classificacao)
    {
        classificacao = Classificar(chave);
        var leiaute = classificacao.Leiaute!;
        var ocorrencias = classificacao.EhNff ? PartesDaNff(chave, leiaute.Nff!, inversa: false) : NumeroInteiro(chave, inversa: false);
        return ocorrencias | Codigo(chave, leiaute, inversa: false);
    }

    /// <summary>Desfaz <see cref="Pseudonimizar(Span{char})"/>, no lugar. Uso interno e de testes.</summary>
    /// <param name="chave">A chave pseudonimizada, com o emitente ainda pseudonimizado.</param>
    /// <returns>O que ficou como veio.</returns>
    /// <exception cref="ArgumentException">Chave fora da estrutura ou sem emitente classificado.</exception>
    /// <exception cref="ObjectDisposedException">Já descartado.</exception>
    internal OcorrenciasChaveDfe Inverter(Span<char> chave)
    {
        var classificacao = Classificar(chave);
        var leiaute = classificacao.Leiaute!;
        var ocorrencias = Codigo(chave, leiaute, inversa: true);
        return ocorrencias | (classificacao.EhNff ? PartesDaNff(chave, leiaute.Nff!, inversa: true) : NumeroInteiro(chave, inversa: true));
    }

    /// <summary>Descarta as permutações e as cópias das chaves de domínio.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _descartado, 1);
        _numero.Dispose();
        _dia.Dispose();
        _sequencial4.Dispose();
        _sequencial5.Dispose();
        _codigo8.Dispose();
        _codigo7.Dispose();
    }

    private ClassificacaoEmitente Classificar(ReadOnlySpan<char> chave)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);

        // Sem o valor nas mensagens: a chave traz dado pessoal.
        var classificacao = ClassificadorEmitente.Classificar(chave);
        if (classificacao.Anomalia == MotivoAnomaliaDfe.EstruturaInvalida)
        {
            throw new ArgumentException("A chave de DF-e está fora da estrutura de 44 posições.", nameof(chave));
        }

        if (classificacao.EhAnomalia)
        {
            throw new ArgumentException("A chave de DF-e não tem emitente classificado.", nameof(chave));
        }

        return classificacao;
    }

    private OcorrenciasChaveDfe NumeroInteiro(Span<char> chave, bool inversa)
    {
        var posicoes = chave.Slice(IndiceDoNumero, TamanhoDoNumero);
        var numero = (ulong)Valor(posicoes);
        if (numero == 0)
        {
            return OcorrenciasChaveDfe.NumeroZero;
        }

        Span<byte> ajuste = stackalloc byte[TamanhoDoEmitente + 1 + 5];
        Copiar(chave.Slice(IndiceDoEmitente, TamanhoDoEmitente), ajuste);
        ajuste[TamanhoDoEmitente] = 0x00;
        Copiar(chave.Slice(IndiceDoModelo, 5), ajuste[(TamanhoDoEmitente + 1)..]);
        Escrever(inversa ? _numero.Inverter(numero, ajuste, _zero) : _numero.Permutar(numero, ajuste, _zero), posicoes);
        return OcorrenciasChaveDfe.Nenhuma;
    }

    // Na ida, o dia antes do sequencial, que leva o dia já permutado no ajuste; na volta, o contrário.
    private OcorrenciasChaveDfe PartesDaNff(Span<char> chave, LeiauteNff nff, bool inversa)
    {
        var ocorrencias = OcorrenciasChaveDfe.Nenhuma;
        var dias = DiasValidos(chave, nff);
        var posicoesDoDia = chave.Slice(nff.IndiceDia, 2);
        var dia = (ulong)Valor(posicoesDoDia);
        var diaPermutavel = dia >= 1 && dia <= (ulong)dias;
        if (!diaPermutavel)
        {
            ocorrencias |= OcorrenciasChaveDfe.DataNffInvalida;
        }

        var posicoesDoSequencial = chave.Slice(nff.IndiceSequencial, nff.TamanhoSequencial);
        var sequencial = (ulong)Valor(posicoesDoSequencial);
        if (sequencial == 0)
        {
            ocorrencias |= OcorrenciasChaveDfe.SequencialZero;
        }

        var permutacaoDoSequencial = nff.TamanhoSequencial == 4 ? _sequencial4 : _sequencial5;
        Span<byte> ajuste = stackalloc byte[TamanhoDoEmitente + 1 + 4 + 5 + TamanhoDoNumero];
        if (!inversa && diaPermutavel)
        {
            MontarAjusteDaNff(chave, nff, comDia: false, ajuste);
            Escrever(_dia.Permutar(dia, ajuste, _diasExcluidos[dias - 28]), posicoesDoDia);
        }

        if (sequencial != 0)
        {
            MontarAjusteDaNff(chave, nff, comDia: true, ajuste);
            Escrever(
                inversa ? permutacaoDoSequencial.Inverter(sequencial, ajuste, _zero) : permutacaoDoSequencial.Permutar(sequencial, ajuste, _zero),
                posicoesDoSequencial);
        }

        if (inversa && diaPermutavel)
        {
            MontarAjusteDaNff(chave, nff, comDia: false, ajuste);
            Escrever(_dia.Inverter(dia, ajuste, _diasExcluidos[dias - 28]), posicoesDoDia);
        }

        return ocorrencias;
    }

    private OcorrenciasChaveDfe Codigo(Span<char> chave, LeiauteDfe leiaute, bool inversa)
    {
        Span<byte> ajuste = stackalloc byte[TamanhoDoEmitente + TamanhoDoNumero];
        Copiar(chave.Slice(IndiceDoEmitente, TamanhoDoEmitente), ajuste);
        Copiar(chave.Slice(IndiceDoNumero, TamanhoDoNumero), ajuste[TamanhoDoEmitente..]);
        if (leiaute.PreservaPosicao36)
        {
            var posicoes7 = chave[IndiceDoCodigo7..(IndiceDoCodigo7 + 7)];
            var codigo7 = (ulong)Valor(posicoes7);
            Escrever(
                inversa ? _codigo7.Inverter(codigo7, ajuste, ConjuntoExcluido.Vazio) : _codigo7.Permutar(codigo7, ajuste, ConjuntoExcluido.Vazio),
                posicoes7);
            return OcorrenciasChaveDfe.Nenhuma;
        }

        var posicoes = chave[IndiceDoCodigo8..(IndiceDoCodigo8 + 8)];
        var codigo = (ulong)Valor(posicoes);
        if (_codigosProibidos.Contem(codigo))
        {
            return OcorrenciasChaveDfe.CodigoProibido;
        }

        var numero = (ulong)Valor(chave.Slice(IndiceDoNumero, TamanhoDoNumero));
        if (codigo == numero)
        {
            return OcorrenciasChaveDfe.CodigoIgualAoNumero;
        }

        ulong? adicional = numero < LimiteDoCodigo8 ? numero : null;
        Escrever(
            inversa ? _codigo8.Inverter(codigo, ajuste, _codigosProibidos, adicional) : _codigo8.Permutar(codigo, ajuste, _codigosProibidos, adicional),
            posicoes);
        return OcorrenciasChaveDfe.Nenhuma;
    }

    // emitente' ‖ 0x00 ‖ AAMM ‖ mod ‖ série ‖ número, com o sequencial e, sem o dia, também o dia em '0'.
    private static void MontarAjusteDaNff(ReadOnlySpan<char> chave, LeiauteNff nff, bool comDia, Span<byte> ajuste)
    {
        Copiar(chave.Slice(IndiceDoEmitente, TamanhoDoEmitente), ajuste);
        ajuste[TamanhoDoEmitente] = 0x00;
        var posicao = TamanhoDoEmitente + 1;
        Copiar(chave.Slice(IndiceDoAamm, 4), ajuste[posicao..]);
        posicao += 4;
        Copiar(chave.Slice(IndiceDoModelo, 5), ajuste[posicao..]);
        posicao += 5;
        var numero = ajuste.Slice(posicao, TamanhoDoNumero);
        Copiar(chave.Slice(IndiceDoNumero, TamanhoDoNumero), numero);
        numero.Slice(nff.IndiceSequencial - IndiceDoNumero, nff.TamanhoSequencial).Fill((byte)'0');
        if (!comDia)
        {
            numero.Slice(nff.IndiceDia - IndiceDoNumero, 2).Fill((byte)'0');
        }
    }

    // Os dias que existem no mês do AAMM e no do número (o menor dos dois), com o ano 2000 + AA; 0 se um mês é inválido.
    private static int DiasValidos(ReadOnlySpan<char> chave, LeiauteNff nff)
    {
        var ano = Valor(chave.Slice(IndiceDoAamm, 2));
        var mesDoAamm = Valor(chave.Slice(IndiceDoAamm + 2, 2));
        var mesDoNumero = Valor(chave.Slice(nff.IndiceMes, 2));
        if (mesDoAamm is < 1 or > 12 || mesDoNumero is < 1 or > 12)
        {
            return 0;
        }

        return Math.Min(DiasNoMes(mesDoAamm, ano), DiasNoMes(mesDoNumero, ano));
    }

    // De 2000 a 2099, bissexto é o ano divisível por 4.
    private static int DiasNoMes(int mes, int ano) => mes switch
    {
        2 => ano % 4 == 0 ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    // Os caracteres já são ASCII (estrutura conferida).
    private static void Copiar(ReadOnlySpan<char> origem, Span<byte> destino)
    {
        for (var i = 0; i < origem.Length; i++)
        {
            destino[i] = (byte)origem[i];
        }
    }

    // Da direita para a esquerda, com zeros à esquerda.
    private static void Escrever(ulong valor, Span<char> destino)
    {
        for (var i = destino.Length - 1; i >= 0; i--)
        {
            destino[i] = (char)('0' + (int)(valor % 10));
            valor /= 10;
        }
    }

    private static ConjuntoExcluido CodigosProibidos()
    {
        Span<ulong> codigos = stackalloc ulong[RegrasChaveDfe.CodigosNumericosProibidos.Length];
        for (var i = 0; i < codigos.Length; i++)
        {
            codigos[i] = (ulong)RegrasChaveDfe.CodigosNumericosProibidos[i];
        }

        return ConjuntoExcluido.DeValores(codigos);
    }

    private static ConjuntoExcluido DiasExcluidos(int diasValidos)
    {
        Span<ulong> dias = stackalloc ulong[PrimeiroDiaForaDoDominio - diasValidos];
        dias[0] = 0;
        for (var i = 1; i < dias.Length; i++)
        {
            dias[i] = (ulong)(diasValidos + i);
        }

        return ConjuntoExcluido.DeValores(dias);
    }
}
