using System.Diagnostics;
using System.Text;
using CodeProcess.Incognito.Documentos;

namespace CodeProcess.Incognito.Pseudonimizacao;

/// <summary>
/// Pseudonimização da chave de acesso de DF-e de 44 posições (NF-e, NFC-e, CT-e, CT-e OS, GTV-e, MDF-e, BP-e, NF3e e
/// NFCom): o emitente vai aos pseudonimizadores das colunas de CPF e de CNPJ, recebidos prontos, conforme a
/// classificação; o número e o código, ao domínio <c>dfe-numero</c>; o cDV é recalculado com o mesmo deslocamento
/// (δ mod 10). cUF, AAMM, modelo, série, tpEmis e o <c>nSiteAutoriz</c> (posição 36 da NF3e e da NFCom) ficam. A saída
/// tem a mesma classificação da entrada e nunca é anomalia. Injetividade garantida: é uma bijeção sobre as chaves
/// classificadas (a classificação depende só do que a saída preserva, e cada parte é uma permutação condicionada ao que
/// já está na saída), e as anomalias preservadas não têm a forma de nenhuma saída; o teste de propriedade sobre a grade
/// de estratos e a inversa (<see cref="Inverter"/>) conferem isso.
/// </summary>
/// <remarks>
/// <para>
/// Formas aceitas (sem os espaços à direita, o preenchimento de <c>char(n)</c>, que volta como veio): as 44 posições;
/// o prefixo do <c>Id</c> do modelo e as 44 posições, com o prefixo comparado sem distinguir a caixa ASCII e devolvido
/// como veio (<c>NFe</c> para 55 e 65, <c>CTe</c> para 57, 64 e 67, <c>MDFe</c> para 58, <c>BPe</c> para 63,
/// <c>NF3e</c> para 66, <c>NFCom</c> para 62); ou 11 grupos de 4 separados por um espaço (DANFE), sem prefixo. Letras
/// maiúsculas só nas posições 7 a 18. O pseudônimo depende só das 44 posições.
/// </para>
/// <para>
/// Seguem a política de entrada malformada: forma fora dessas (inclusive espaço à esquerda, outro separador e prefixo
/// de outro modelo), modelo fora dos nove ou dos aceitos pela coluna, chave sem emitente classificável (contada como
/// <see cref="OcorrenciasChaveDfe.Anomalia"/>) e a chave de 50 posições da NFS-e Padrão Nacional, com a dica do
/// <c>br.chave-nfse</c>. A chave de 44 zeros volta como veio (<see cref="Desfecho.Zerado"/>), antes da política,
/// com qualquer prefixo de <c>Id</c> e qualquer que seja o parâmetro <c>modelos</c>: não tem modelo nem dado pessoal.
/// cDV errado segue a política de entrada inválida; o DV interno do emitente errado é sempre mantido pelo deslocamento
/// (δ mod 100) e contado, sem a política da coluna. Chave que sai igual à entrada (emitente, número e código em ponto
/// fixo) é <see cref="Desfecho.PontoFixo"/>. Nenhuma exceção traz o valor.
/// </para>
/// <para>
/// Pode ser usado por várias threads ao mesmo tempo. O descarte é do dono, depois de todo uso, e só descarta o domínio
/// <c>dfe-numero</c>: os pseudonimizadores de CPF e de CNPJ continuam com quem os criou e precisam viver até o fim do
/// uso deste.
/// </para>
/// </remarks>
internal sealed class PseudonimizadorChaveDfe : IDisposable
{
    private const int TamanhoDaChave = RegrasChaveDfe.Tamanho;
    private const int TamanhoEmGrupos = 54;
    private const int TamanhoDoGrupo = 4;
    private const int TamanhoDaNfse = 50;
    private const int IndiceDoEmitente = 6;
    private const int IndiceDoCpf = 9;
    private const int TamanhoDoCnpj = 14;
    private const int TamanhoDoCpf = 11;
    private const int IndiceDoCdv = 43;
    private const string MensagemMalformado =
        "Valor fora da forma de chave de acesso de DF-e (44 posições, com o prefixo do Id do modelo ou em 11 grupos de 4; letras maiúsculas só nas posições 7 a 18), de modelo fora dos tratados por esta coluna ou sem emitente classificável; a política entradaMalformada é falhar. O valor não é exibido.";
    private const string MensagemNfse =
        "Valor com 50 posições, como a chave da NFS-e Padrão Nacional, que o br.chave-dfe não trata (é o formato do br.chave-nfse); a política entradaMalformada é falhar. O valor não é exibido.";

    private readonly PseudonimizadorNumeroDfe _numero;
    private readonly PseudonimizadorCpf _cpf;
    private readonly PseudonimizadorCnpj _cnpj;
    private readonly UInt128 _modelos;
    private readonly PoliticaEntradaInvalida _invalida;
    private readonly PoliticaEntradaMalformada _malformada;
    private int _descartado;

    /// <summary>Prepara a pseudonimização da chave de DF-e.</summary>
    /// <param name="chave">Chave do Incognito.</param>
    /// <param name="versao">Versão do <c>br.chave-dfe</c>, a partir de 1, que só entra no domínio <c>dfe-numero</c>.</param>
    /// <param name="ambiente">Ambiente da configuração, o mesmo dos pseudonimizadores de CPF e de CNPJ.</param>
    /// <param name="cpf">O pseudonimizador da coluna de CPF (domínio <c>dominioCpf</c>), que continua com quem o criou.</param>
    /// <param name="cnpj">O pseudonimizador da coluna de CNPJ (domínio <c>dominioCnpj</c>), que continua com quem o criou.</param>
    /// <param name="modelos">Códigos dos modelos aceitos (parâmetro <c>modelos</c>); nulo para os nove.</param>
    /// <param name="invalida">Política para cDV errado.</param>
    /// <param name="malformada">Política para valor fora da forma, de outro modelo ou sem emitente classificável.</param>
    /// <exception cref="ArgumentOutOfRangeException">Política fora das definidas.</exception>
    /// <exception cref="ArgumentNullException">Pseudonimizador nulo.</exception>
    /// <exception cref="ArgumentException">
    /// Pseudonimizador de outro ambiente, ou modelos vazios ou com código fora dos nove.
    /// </exception>
    internal PseudonimizadorChaveDfe(
        ChaveIncognito chave,
        int versao,
        string ambiente,
        PseudonimizadorCpf cpf,
        PseudonimizadorCnpj cnpj,
        IReadOnlyCollection<int>? modelos,
        PoliticaEntradaInvalida invalida,
        PoliticaEntradaMalformada malformada)
    {
        // Antes de derivar: depois, o domínio dfe-numero criado ficaria sem descarte.
        Politicas.Validar(invalida, nameof(invalida));
        Politicas.Validar(malformada, nameof(malformada));
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(cpf);
        ArgumentNullException.ThrowIfNull(cnpj);
        ExigirMesmoAmbiente(cpf.Ambiente, ambiente, nameof(cpf));
        ExigirMesmoAmbiente(cnpj.Ambiente, ambiente, nameof(cnpj));
        _modelos = Mascara(modelos);

        _numero = new PseudonimizadorNumeroDfe(chave, PseudonimizadorNumeroDfe.NomeDoDominio, versao, ambiente);
        _cpf = cpf;
        _cnpj = cnpj;
        _invalida = invalida;
        _malformada = malformada;
    }

    /// <summary>Pseudonimiza uma chave de DF-e em texto.</summary>
    /// <param name="entrada">A chave como veio da coluna.</param>
    /// <param name="saida">
    /// O pseudônimo, com o prefixo, os grupos e o preenchimento como vieram; nulo se anulado; a própria entrada se
    /// copiada em claro, vazia ou zerada.
    /// </param>
    /// <param name="ocorrencias">O que a chave teve de fora do comum, inclusive quando segue uma política.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado) ou <c>INC1004</c> (cDV errado), pelas políticas.</exception>
    /// <exception cref="ObjectDisposedException">Este ou o pseudonimizador do emitente já descartado.</exception>
    internal Desfecho Pseudonimizar(string entrada, out string? saida, out OcorrenciasChaveDfe ocorrencias) =>
        Aplicar(entrada, inversa: false, out saida, out ocorrencias);

    /// <summary>
    /// Desfaz <see cref="Pseudonimizar"/>: a mesma classificação lida no pseudônimo, o número e o código desfeitos com o
    /// emitente ainda pseudonimizado no ajuste, depois o emitente e o cDV. Uso interno e de testes; o cDV errado é
    /// sempre desfeito pelo deslocamento, e as formas e as políticas de malformado são as mesmas.
    /// </summary>
    /// <param name="entrada">O pseudônimo como saiu de <see cref="Pseudonimizar"/>.</param>
    /// <param name="saida">A chave original, com o prefixo, os grupos e o preenchimento como vieram.</param>
    /// <param name="ocorrencias">O que a chave original teve de fora do comum.</param>
    /// <returns>Como o valor foi tratado.</returns>
    /// <exception cref="IncognitoException"><c>INC1003</c> (malformado), pela política.</exception>
    /// <exception cref="ObjectDisposedException">Este ou o pseudonimizador do emitente já descartado.</exception>
    internal Desfecho Inverter(string entrada, out string? saida, out OcorrenciasChaveDfe ocorrencias) =>
        Aplicar(entrada, inversa: true, out saida, out ocorrencias);

    /// <summary>Descarta o domínio <c>dfe-numero</c>; os pseudonimizadores do emitente continuam com quem os criou.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _descartado, 1) == 0)
        {
            _numero.Dispose();
        }
    }

    private Desfecho Aplicar(string entrada, bool inversa, out string? saida, out OcorrenciasChaveDfe ocorrencias)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _descartado) != 0, this);
        ocorrencias = OcorrenciasChaveDfe.Nenhuma;
        var corpo = entrada.AsSpan().TrimEnd(' ');
        if (corpo.IsEmpty)
        {
            saida = entrada;
            return Desfecho.Vazio;
        }

        // Antes de qualquer cópia: nenhuma forma aceita passa de 54 caracteres.
        if (corpo.Length > TamanhoEmGrupos)
        {
            return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }

        Span<char> chave = stackalloc char[TamanhoDaChave];
        Span<char> entradaDaChave = stackalloc char[TamanhoDaChave];
        var prefixo = 0;
        var emGrupos = corpo.Length == TamanhoEmGrupos;
        if (emGrupos)
        {
            if (!JuntarGrupos(corpo, chave))
            {
                return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
            }
        }
        else if (corpo.Length >= TamanhoDaChave && EhPrefixoDeId(corpo[..(corpo.Length - TamanhoDaChave)]))
        {
            prefixo = corpo.Length - TamanhoDaChave;
            corpo[prefixo..].CopyTo(chave);
        }
        else
        {
            var mensagem = EhChaveDaNfse(corpo) ? MensagemNfse : MensagemMalformado;
            return Politicas.AplicarMalformado(_malformada, mensagem, entrada, out saida);
        }

        var classificacao = ClassificadorEmitente.Classificar(chave);
        if (classificacao.EhChaveZerada)
        {
            saida = entrada;
            return Desfecho.Zerado;
        }

        // Estrutura e modelo fora da tabela não têm leiaute; as outras anomalias são contadas mesmo com o modelo ou o
        // prefixo também fora.
        if (classificacao.EhAnomalia && classificacao.Leiaute is not null)
        {
            ocorrencias = OcorrenciasChaveDfe.Anomalia;
        }

        if (classificacao.EhAnomalia
            || classificacao.Leiaute is not { } leiaute
            || !ModeloAceito(leiaute.Modelo)
            || (prefixo != 0 && !PrefixoDoModelo(corpo[..prefixo], leiaute.Modelo)))
        {
            return Politicas.AplicarMalformado(_malformada, MensagemMalformado, entrada, out saida);
        }

        chave.CopyTo(entradaDaChave);
        var deslocamento = ((chave[IndiceDoCdv] - '0') - DigitosVerificadores.ChaveDfe(chave[..IndiceDoCdv]) + 10) % 10;
        if (deslocamento != 0 && !inversa && !Politicas.SeguirComDvInvalido(_invalida, "Chave de DF-e"))
        {
            saida = null;
            return Desfecho.AnuladoDvInvalido;
        }

        // Emitente, depois número e código (que usam o emitente pseudonimizado no ajuste), depois o cDV; a inversa desfaz
        // o número e o código antes do emitente, com o mesmo ajuste.
        ClassificacaoEmitente depoisDoEmitente;
        if (inversa)
        {
            ocorrencias = _numero.Inverter(chave);
            ocorrencias |= Emitente(chave, classificacao, inversa: true);
            depoisDoEmitente = ClassificadorEmitente.Classificar(chave);
        }
        else
        {
            ocorrencias = Emitente(chave, classificacao, inversa: false);
            ocorrencias |= _numero.Pseudonimizar(chave, out depoisDoEmitente);
        }

        // Invariante da classificação: o emitente pseudonimizado mantém a classificação. Se quebrar, a saída perde a inversa.
        if (depoisDoEmitente.Tipo != classificacao.Tipo || depoisDoEmitente.EhNff != classificacao.EhNff
            || depoisDoEmitente.ExcluiRaizComZeros != classificacao.ExcluiRaizComZeros)
        {
            throw new UnreachableException("A pseudonimização do emitente mudou a classificação da chave de DF-e.");
        }

        chave[IndiceDoCdv] = (char)('0' + ((DigitosVerificadores.ChaveDfe(chave[..IndiceDoCdv]) + deslocamento) % 10));
        if (chave.SequenceEqual(entradaDaChave))
        {
            saida = entrada;
            return Desfecho.PontoFixo;
        }

        Span<char> corpoDaSaida = stackalloc char[TamanhoEmGrupos];
        corpo.CopyTo(corpoDaSaida);
        if (emGrupos)
        {
            SepararEmGrupos(chave, corpoDaSaida);
        }
        else
        {
            chave.CopyTo(corpoDaSaida[prefixo..]);
        }

        saida = string.Concat(corpoDaSaida[..corpo.Length], entrada.AsSpan(corpo.Length));
        return deslocamento == 0 ? Desfecho.Pseudonimizado : Desfecho.DvInvalidoPreservado;
    }

    private OcorrenciasChaveDfe Emitente(Span<char> chave, in ClassificacaoEmitente classificacao, bool inversa)
    {
        if (classificacao.Tipo == TipoEmitente.Cpf)
        {
            var cpf = chave.Slice(IndiceDoCpf, TamanhoDoCpf);
            return inversa ? _cpf.InverterNaChave(cpf) : _cpf.TransformarNaChave(cpf);
        }

        var cnpj = chave.Slice(IndiceDoEmitente, TamanhoDoCnpj);
        return inversa
            ? _cnpj.InverterNaChave(cnpj, classificacao.ExcluiRaizComZeros)
            : _cnpj.TransformarNaChave(cnpj, classificacao.ExcluiRaizComZeros);
    }

    private static void ExigirMesmoAmbiente(string doPseudonimizador, string ambiente, string parametro)
    {
        if (!string.Equals(doPseudonimizador, ambiente, StringComparison.Ordinal))
        {
            throw new ArgumentException("O pseudonimizador do emitente é de outro ambiente.", parametro);
        }
    }

    private static UInt128 Mascara(IReadOnlyCollection<int>? modelos)
    {
        if (modelos is null)
        {
            modelos = Enum.GetValues<ModeloDfe>().Select(modelo => (int)modelo).ToArray();
        }

        if (modelos.Count == 0)
        {
            throw new ArgumentException("A lista de modelos está vazia.", nameof(modelos));
        }

        UInt128 mascara = 0;
        foreach (var codigo in modelos)
        {
            if (codigo is < 0 or > byte.MaxValue || !Enum.IsDefined((ModeloDfe)codigo))
            {
                throw new ArgumentException("Modelo fora dos nove tratados pelo br.chave-dfe.", nameof(modelos));
            }

            // Os códigos dos modelos vão de 55 a 67: cabem nos 128 bits sem o deslocamento dar a volta.
            mascara |= UInt128.One << codigo;
        }

        return mascara;
    }

    private bool ModeloAceito(ModeloDfe modelo) => (_modelos & (UInt128.One << (int)modelo)) != 0;

    // Vazio (sem prefixo) ou um dos seis; se é o do modelo, só se sabe depois de ler a chave.
    private static bool EhPrefixoDeId(ReadOnlySpan<char> prefixo) => prefixo.Length switch
    {
        0 => true,
        3 => Ascii.EqualsIgnoreCase(prefixo, "NFe") || Ascii.EqualsIgnoreCase(prefixo, "CTe") || Ascii.EqualsIgnoreCase(prefixo, "BPe"),
        4 => Ascii.EqualsIgnoreCase(prefixo, "MDFe") || Ascii.EqualsIgnoreCase(prefixo, "NF3e"),
        5 => Ascii.EqualsIgnoreCase(prefixo, "NFCom"),
        _ => false,
    };

    // Seis prefixos para os nove modelos: a NFC-e usa o da NF-e; o CT-e OS e a GTV-e, o do CT-e.
    private static bool PrefixoDoModelo(ReadOnlySpan<char> prefixo, ModeloDfe modelo) => modelo switch
    {
        ModeloDfe.Nfe or ModeloDfe.Nfce => Ascii.EqualsIgnoreCase(prefixo, "NFe"),
        ModeloDfe.Cte or ModeloDfe.CteOs or ModeloDfe.Gtve => Ascii.EqualsIgnoreCase(prefixo, "CTe"),
        ModeloDfe.Mdfe => Ascii.EqualsIgnoreCase(prefixo, "MDFe"),
        ModeloDfe.Bpe => Ascii.EqualsIgnoreCase(prefixo, "BPe"),
        ModeloDfe.Nf3e => Ascii.EqualsIgnoreCase(prefixo, "NF3e"),
        ModeloDfe.Nfcom => Ascii.EqualsIgnoreCase(prefixo, "NFCom"),
        _ => false,
    };

    // 11 grupos de 4 com um espaço exatamente entre eles; o que está dentro dos grupos a classificação confere. Copia
    // todos os grupos antes de responder, para a chave nunca ficar pela metade.
    private static bool JuntarGrupos(ReadOnlySpan<char> corpo, Span<char> chave)
    {
        var separados = true;
        for (var grupo = 0; grupo < TamanhoDaChave / TamanhoDoGrupo; grupo++)
        {
            var inicio = grupo * (TamanhoDoGrupo + 1);
            separados &= grupo == 0 || corpo[inicio - 1] == ' ';
            corpo.Slice(inicio, TamanhoDoGrupo).CopyTo(chave[(grupo * TamanhoDoGrupo)..]);
        }

        return separados;
    }

    private static void SepararEmGrupos(ReadOnlySpan<char> chave, Span<char> corpo)
    {
        for (var grupo = 0; grupo < TamanhoDaChave / TamanhoDoGrupo; grupo++)
        {
            chave.Slice(grupo * TamanhoDoGrupo, TamanhoDoGrupo).CopyTo(corpo[(grupo * (TamanhoDoGrupo + 1))..]);
        }
    }

    // A chave da NFS-e Padrão Nacional, com ou sem o prefixo NFS do Id: 50 letras ou dígitos ASCII.
    private static bool EhChaveDaNfse(ReadOnlySpan<char> corpo)
    {
        if (corpo.Length == TamanhoDaNfse + 3 && Ascii.EqualsIgnoreCase(corpo[..3], "NFS"))
        {
            corpo = corpo[3..];
        }

        if (corpo.Length != TamanhoDaNfse)
        {
            return false;
        }

        foreach (var caractere in corpo)
        {
            if (!char.IsAsciiLetterOrDigit(caractere))
            {
                return false;
            }
        }

        return true;
    }
}
