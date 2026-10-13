using System.Buffers;
using System.Globalization;
using System.Text;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Origens das chaves de DF-e dos vetores: a regra traz a <see cref="EspecificacaoChaveDfe"/> do
/// <see cref="GeradorChaveDfe"/> em texto (<c>chave-dfe:aamm=2405,modelo=55,...</c>), e a chave sai do gerador com a
/// semente do par. As regras de caminhada partem dessa chave e trocam uma parte pela pré-imagem de um valor excluído,
/// calculada com a permutação inversa e o ajuste que o pseudonimizador vai usar (emitente, número ou dia já
/// pseudonimizados): a entrada passa pelo cycle-walking. Nenhuma chave de entrada traz um emitente de raiz baixa com DV
/// certo (<see cref="RaizBaixaComDvCerto"/>): a semente que daria isso é pulada, e as especificações desses emitentes
/// pedem o DV deslocado; o gerador de vetores pula também a semente cuja saída traria um.
/// </summary>
internal static class OrigensChaveDfe
{
    private const string Prefixo = "chave-dfe";
    private const int TamanhoDaChave = 44;
    private const int IndiceDoEmitente = 6;
    private const int IndiceDoNumero = 25;
    private const int IndiceDoCodigo = 35;
    private const string ChaveZerada = "00000000000000000000000000000000000000000000";

    private static readonly string[] _camposDaEspecificacao =
        ["aamm", "modelo", "serie", "tpemis", "emitente", "numero", "codigo", "nff", "cdv", "dv", "site", "dia", "mes", "codigo-fixo"];

    private static readonly SearchValues<char> _base36 = SearchValues.Create(OrigensDeVetores.Base36);

    private static readonly string[] _prefixosDeId = ["NFe", "CTe", "MDFe", "BPe", "NF3e", "NFCom"];

    /// <summary>Se a regra é de chave de DF-e (ou da chave de 50 posições da NFS-e).</summary>
    internal static bool Trata(string nome) => nome.StartsWith(Prefixo, StringComparison.Ordinal) || nome == "nfse-50";

    /// <summary>Chave construída pela especificação, ou com o código igual ao número pseudonimizado.</summary>
    internal static string Construido(string nome, string argumento, FonteDeSorteio fonte, ContextoDeOrigem contexto)
    {
        if (nome == "nfse-50")
        {
            // A chave da NFS-e Padrão Nacional tem 50 letras ou dígitos: sorteada, nunca a de um manual.
            Programacao(argumento.Length == 0, "a regra nfse-50 não tem argumento");
            return OrigensDeVetores.Sortear(fonte, OrigensDeVetores.Base36, 50);
        }

        var especificacao = Ler(argumento);
        var chave = GeradorChaveDfe.Gerar(fonte, especificacao);
        switch (nome)
        {
            case Prefixo:
                return Conferida(chave);
            case Prefixo + "-codigo-igual-ao-numero":
                // O código de entrada igual ao número já pseudonimizado fica como veio (comparação numérica).
                var numero = Numero(contexto.PseudonimizarChaveDfe(chave).AsSpan(IndiceDoNumero, 9));
                Exigir(numero < 100_000_000 && !GeradorChaveDfe.EhCodigoProibido(numero), "o número pseudonimizado não cabe no código");
                return Conferida(ComCdv(Trocar(chave, IndiceDoCodigo, numero, 8), especificacao.DeslocamentoCdv));
            default:
                throw new InvalidOperationException("Regra de construção de chave de DF-e desconhecida.");
        }
    }

    /// <summary>Chave cuja parte pedida, ao ser permutada, cai num valor excluído (ou, na saída 00, não cai) e caminha.</summary>
    internal static string Inversa(string nome, string argumento, FonteDeSorteio fonte, ContextoDeOrigem contexto)
    {
        var especificacao = Ler(argumento);
        var chave = GeradorChaveDfe.Gerar(fonte, especificacao);
        var trocada = nome switch
        {
            Prefixo + "-caminhada-codigo-proibido" => CodigoQueCaminha(chave, fonte, contexto, paraONumero: false),
            Prefixo + "-caminhada-codigo-numero" => CodigoQueCaminha(chave, fonte, contexto, paraONumero: true),
            Prefixo + "-caminhada-numero-zero" => NumeroQueCaminha(chave, contexto),
            Prefixo + "-caminhada-dia" => DiaQueCaminha(chave, especificacao, fonte, contexto),
            Prefixo + "-caminhada-sequencial" => SequencialQueCaminha(chave, especificacao, contexto),
            Prefixo + "-caminhada-cpf" => CpfQueCaminha(chave, fonte, contexto, especificacao.DeslocamentoDvEmitente),
            Prefixo + "-caminhada-raiz-000" => CnpjQueCaminha(chave, fonte, contexto, especificacao.DeslocamentoDvEmitente, zerosDaSaida: 3),
            Prefixo + "-saida-raiz-00" => CnpjQueCaminha(chave, fonte, contexto, especificacao.DeslocamentoDvEmitente, zerosDaSaida: 2),
            _ => throw new InvalidOperationException("Regra de inversa de chave de DF-e desconhecida."),
        };
        return Conferida(ComCdv(trocada, especificacao.DeslocamentoCdv));
    }

    /// <summary>
    /// Se o emitente (as posições 7 a 20), lido como CNPJ, tem raiz numérica começando com <c>00</c>, ordem diferente de
    /// <c>0000</c> (numérica ou com letra) e DV certo. Raízes tão baixas são de bancos, órgãos públicos e estatais: nenhum
    /// par dos vetores traz um emitente assim, na entrada ou na saída. A raiz zero com a ordem <c>0000</c> não é de
    /// estabelecimento.
    /// </summary>
    internal static bool RaizBaixaComDvCerto(ReadOnlySpan<char> emitente) =>
        emitente.Length == 14
        && !emitente[..8].ContainsAnyExceptInRange('0', '9')
        && emitente.StartsWith("00")
        && !emitente[8..12].SequenceEqual("0000")
        && !emitente[8..12].ContainsAnyExcept(_base36)
        && !emitente[12..].ContainsAnyExceptInRange('0', '9')
        && DigitosVerificadores.Cnpj(emitente[..12]) == ((emitente[12] - '0') * 10) + (emitente[13] - '0');

    /// <summary>
    /// As 44 posições de uma chave numa das formas aceitas (sem prefixo, com o prefixo de um <c>Id</c> ou em 11 grupos de
    /// 4, com preenchimento à direita); nulo se o texto não tem uma dessas formas.
    /// </summary>
    internal static string? ChaveDaForma(string? texto)
    {
        var corpo = texto?.TrimEnd(' ');
        if (corpo is null || corpo.Length < TamanhoDaChave)
        {
            return null;
        }

        if (corpo.Length == 54 && Enumerable.Range(1, 10).All(grupo => corpo[(grupo * 5) - 1] == ' '))
        {
            return corpo.Replace(" ", "", StringComparison.Ordinal);
        }

        var prefixo = corpo[..^TamanhoDaChave];
        return prefixo.Length == 0 || _prefixosDeId.Contains(prefixo, StringComparer.OrdinalIgnoreCase) ? corpo[^TamanhoDaChave..] : null;
    }

    /// <summary>
    /// Toda janela de 44 posições com a estrutura de uma chave, no texto sem espaços, hífens e pontos e com as letras em
    /// maiúsculas: acha a chave em qualquer forma, até nas malformadas.
    /// </summary>
    internal static IEnumerable<string> Janelas(string? texto)
    {
        if (texto is null)
        {
            yield break;
        }

        var normalizado = new string([.. texto.Where(c => c is not (' ' or '-' or '.')).Select(c => char.IsAsciiLetterLower(c) ? char.ToUpperInvariant(c) : c)]);
        for (var inicio = 0; inicio + TamanhoDaChave <= normalizado.Length; inicio++)
        {
            var janela = normalizado.Substring(inicio, TamanhoDaChave);
            if (RegrasChaveDfe.TemEstrutura(janela) && janela != ChaveZerada)
            {
                yield return janela;
            }
        }
    }

    // aamm=2405,modelo=55,serie=1,tpemis=1,emitente=Cpf[,numero=Nff][,codigo=Proibido][,nff=2][,cdv=3][,dv=7][,site=1]
    // [,dia=29][,mes=2][,codigo-fixo=1234567]. Os enumerados só pelo nome exato.
    private static EspecificacaoChaveDfe Ler(string texto)
    {
        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var campo in texto.Split(','))
        {
            var igual = campo.IndexOf('=', StringComparison.Ordinal);
            Programacao(igual > 0 && campos.TryAdd(campo[..igual], campo[(igual + 1)..]), "especificação de chave de DF-e fora do formato");
        }

        Programacao(campos.Keys.All(nome => _camposDaEspecificacao.Contains(nome, StringComparer.Ordinal)), "campo desconhecido na especificação de chave de DF-e");
        return new EspecificacaoChaveDfe(
            Inteiro(Obrigatorio(campos, "aamm")),
            Inteiro(Obrigatorio(campos, "modelo")),
            Inteiro(Obrigatorio(campos, "serie")),
            Caractere(Obrigatorio(campos, "tpemis")),
            Enumerado<EmitenteGerado>(Obrigatorio(campos, "emitente")),
            campos.TryGetValue("numero", out var numero) ? Enumerado<NumeroGerado>(numero) : NumeroGerado.Qualquer,
            campos.TryGetValue("codigo", out var codigo) ? Enumerado<CodigoGerado>(codigo) : CodigoGerado.Qualquer,
            campos.TryGetValue("nff", out var tipo) ? Caractere(tipo) : '1',
            campos.TryGetValue("cdv", out var cdv) ? Inteiro(cdv) : 0,
            campos.TryGetValue("dv", out var dv) ? Inteiro(dv) : 0,
            campos.TryGetValue("site", out var site) ? Caractere(site) : '0',
            campos.TryGetValue("dia", out var dia) ? Inteiro(dia) : null,
            campos.TryGetValue("mes", out var mes) ? Inteiro(mes) : null,
            campos.TryGetValue("codigo-fixo", out var fixo) ? Inteiro(fixo) : null);
    }

    // O código que a permutação cnf8 levaria a um proibido ou ao número pseudonimizado.
    private static string CodigoQueCaminha(string chave, FonteDeSorteio fonte, ContextoDeOrigem contexto, bool paraONumero)
    {
        var saida = contexto.PseudonimizarChaveDfe(chave);
        var numero = Numero(saida.AsSpan(IndiceDoNumero, 9));
        var proibidos = GeradorChaveDfe.CodigosProibidos;
        var alvo = paraONumero ? numero : proibidos[(int)fonte.Uniforme((ulong)proibidos.Length)];
        Exigir(!paraONumero || (numero < 100_000_000 && !GeradorChaveDfe.EhCodigoProibido(numero)), "o número pseudonimizado não cabe no código");
        var ajuste = Ascii(saida[IndiceDoEmitente..20] + saida[IndiceDoNumero..34]);
        using var permutacao = contexto.Feistel("cnf8", 10, 10_000, 10_000);
        var codigo = (long)permutacao.Inverter((ulong)alvo, ajuste);
        Exigir(!GeradorChaveDfe.EhCodigoProibido(codigo) && codigo != numero, "a pré-imagem do código é excluída");
        return Trocar(chave, IndiceDoCodigo, codigo, 8);
    }

    // O número (fora da NFF) que a permutação nnf levaria ao 0.
    private static string NumeroQueCaminha(string chave, ContextoDeOrigem contexto)
    {
        var saida = contexto.PseudonimizarChaveDfe(chave);
        var ajuste = Ascii(saida[IndiceDoEmitente..20] + "\0" + chave[20..25]);
        using var permutacao = contexto.Feistel("nnf", 10, 10_000, 100_000);
        var numero = permutacao.Inverter(0, ajuste);
        Exigir(numero != 0, "a pré-imagem do zero é o zero");
        return Trocar(chave, IndiceDoNumero, (long)numero, 9);
    }

    // O dia da NFF que a permutação nff-dia levaria ao 0 ou a um dia depois do último do mês.
    private static string DiaQueCaminha(string chave, EspecificacaoChaveDfe especificacao, FonteDeSorteio fonte, ContextoDeOrigem contexto)
    {
        var (indiceDia, indiceMes, indiceSequencial, tamanhoSequencial) = PartesDaNff(chave, especificacao);
        var dias = DiasValidos(chave, indiceMes);
        Programacao(dias > 0, "a caminhada do dia pede uma data da NFF válida");
        var excluidos = new List<ulong> { 0 };
        for (var dia = dias + 1; dia <= 35; dia++)
        {
            excluidos.Add((ulong)dia);
        }

        var alvo = excluidos[(int)fonte.Uniforme((ulong)excluidos.Count)];
        var saida = contexto.PseudonimizarChaveDfe(chave);
        var numero = chave[IndiceDoNumero..34].ToCharArray();
        Zerar(numero, indiceSequencial - IndiceDoNumero, tamanhoSequencial);
        Zerar(numero, indiceDia - IndiceDoNumero, 2);
        var ajuste = Ascii(saida[IndiceDoEmitente..20] + "\0" + chave[2..6] + chave[20..25] + new string(numero));
        using var permutacao = contexto.Feistel(contexto.Dominio, contexto.Versao, "nff-dia", 10, 6, 6, rodadas: 24);
        var diaDeEntrada = permutacao.Inverter(alvo, ajuste);
        Exigir(diaDeEntrada >= 1 && diaDeEntrada <= (ulong)dias, "a pré-imagem do dia é excluída");
        return Trocar(chave, indiceDia, (long)diaDeEntrada, 2);
    }

    // O sequencial da NFF que a permutação nff-seq levaria ao 0, com o dia já pseudonimizado no ajuste.
    private static string SequencialQueCaminha(string chave, EspecificacaoChaveDfe especificacao, ContextoDeOrigem contexto)
    {
        var (indiceDia, _, indiceSequencial, tamanhoSequencial) = PartesDaNff(chave, especificacao);
        var saida = contexto.PseudonimizarChaveDfe(chave);
        var numero = chave[IndiceDoNumero..34].ToCharArray();
        Zerar(numero, indiceSequencial - IndiceDoNumero, tamanhoSequencial);
        numero[indiceDia - IndiceDoNumero] = saida[indiceDia];
        numero[indiceDia - IndiceDoNumero + 1] = saida[indiceDia + 1];
        var ajuste = Ascii(saida[IndiceDoEmitente..20] + "\0" + chave[2..6] + chave[20..25] + new string(numero));
        using var permutacao = contexto.Feistel("nff-seq", 10, 100, tamanhoSequencial == 4 ? 100UL : 1_000UL);
        var sequencial = permutacao.Inverter(0, ajuste);
        Exigir(sequencial != 0, "a pré-imagem do zero é o zero");
        return Trocar(chave, indiceSequencial, (long)sequencial, tamanhoSequencial);
    }

    // O CPF do emitente (com 000 à frente) que a coluna de CPF levaria a uma base repdígito.
    private static string CpfQueCaminha(string chave, FonteDeSorteio fonte, ContextoDeOrigem contexto, int deslocamentoDv)
    {
        var cpf = Colunas(contexto).Cpf;
        using var permutacao = contexto.Feistel(cpf.Dominio, cpf.Versao, "num", 10, 10_000, 100_000);
        var baseCpf = permutacao.Inverter(fonte.Uniforme(10) * 111_111_111, []);
        Exigir(baseCpf % 111_111_111 != 0, "a pré-imagem é repdígito");
        var base9 = baseCpf.ToString("D9", CultureInfo.InvariantCulture);
        var dv = (DigitosVerificadores.Cpf(base9) + deslocamentoDv) % 100;
        return string.Concat(chave[..IndiceDoEmitente], "000", base9, dv.ToString("00", CultureInfo.InvariantCulture)) + chave[20..];
    }

    // O CNPJ do emitente que a coluna de CNPJ levaria a uma raiz começando com 3 zeros (000xxxxx; com a ordem permutada,
    // 12 caracteres menores que 10⁹), e então na regra dos zeros a chave caminha além da coluna; ou com exatamente 2
    // (00xxxxxx), que a chave não exclui e deixa como a coluna. A entrada nunca começa com 000.
    private static string CnpjQueCaminha(string chave, FonteDeSorteio fonte, ContextoDeOrigem contexto, int deslocamentoDv, int zerosDaSaida)
    {
        var cnpj = Colunas(contexto).Cnpj;
        var tamanho = cnpj.PermutarOrdem ? 12 : 8;
        var piso = zerosDaSaida == 3 ? (cnpj.PermutarOrdem ? 10_000UL : 1UL) : OrigensDeVetores.Potencia10(tamanho - 3);
        var teto = OrigensDeVetores.Potencia10(tamanho - zerosDaSaida);
        using var permutacao = cnpj.PermutarOrdem
            ? contexto.Feistel(cnpj.Dominio, cnpj.Versao, "ordem", 10, 1_000_000, 1_000_000)
            : contexto.Feistel(cnpj.Dominio, cnpj.Versao, "num", 10, 10_000, 10_000);
        var entrada = permutacao.Inverter(piso + fonte.Uniforme(teto - piso), []);
        Exigir(entrada >= OrigensDeVetores.Potencia10(tamanho - 3), "a pré-imagem também começa com 000");
        var base12 = cnpj.PermutarOrdem
            ? entrada.ToString("D12", CultureInfo.InvariantCulture)
            : entrada.ToString("D8", CultureInfo.InvariantCulture) + "0001";
        var dv = (DigitosVerificadores.Cnpj(base12) + deslocamentoDv) % 100;
        return string.Concat(chave[..IndiceDoEmitente], base12, dv.ToString("00", CultureInfo.InvariantCulture)) + chave[20..];
    }

    private static ColunasDoEmitente Colunas(ContextoDeOrigem contexto) =>
        contexto.Emitente ?? throw new InvalidOperationException("Caso sem as colunas do emitente da chave de DF-e.");

    // Índices do dia, do mês e do sequencial: NF-e e NFC-e (dia, mês, tipo, sequencial de 4) e CT-e e MDF-e (mês, dia,
    // sequencial de 5). A especificação precisa pedir as partes da NFF.
    private static (int Dia, int Mes, int Sequencial, int TamanhoSequencial) PartesDaNff(string chave, EspecificacaoChaveDfe especificacao)
    {
        Programacao(especificacao.Numero is NumeroGerado.Nff or NumeroGerado.NffSequencialZero, "a caminhada do dia ou do sequencial pede numero=Nff");
        return chave[20..22] is "57" or "58" ? (27, 25, 29, 5) : (25, 27, 30, 4);
    }

    private static int DiasValidos(string chave, int indiceMes)
    {
        var ano = 2000 + Inteiro(chave[2..4]);
        var mesDoAamm = Inteiro(chave[4..6]);
        var mesDoNumero = Inteiro(chave.Substring(indiceMes, 2));
        return mesDoAamm is >= 1 and <= 12 && mesDoNumero is >= 1 and <= 12
            ? Math.Min(DateTime.DaysInMonth(ano, mesDoAamm), DateTime.DaysInMonth(ano, mesDoNumero))
            : 0;
    }

    // Pula a semente cuja chave traria um emitente de raiz baixa com DV certo.
    private static string Conferida(string chave)
    {
        Exigir(!RaizBaixaComDvCerto(chave.AsSpan(IndiceDoEmitente, 14)), "emitente de raiz baixa com DV certo");
        return chave;
    }

    private static string ComCdv(string chave, int deslocamento) =>
        chave[..43] + (char)('0' + ((DigitosVerificadores.ChaveDfe(chave.AsSpan(0, 43)) + deslocamento) % 10));

    private static string Trocar(string chave, int indice, long valor, int tamanho) =>
        chave[..indice] + valor.ToString(new string('0', tamanho), CultureInfo.InvariantCulture) + chave[(indice + tamanho)..];

    private static void Zerar(char[] numero, int inicio, int tamanho) => Array.Fill(numero, '0', inicio, tamanho);

    private static byte[] Ascii(string texto) => Encoding.ASCII.GetBytes(texto);

    private static long Numero(ReadOnlySpan<char> digitos) =>
        long.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new InvalidOperationException("Parte numérica da chave fora do formato.");

    private static string Obrigatorio(Dictionary<string, string> campos, string nome) =>
        campos.TryGetValue(nome, out var valor) ? valor : throw new InvalidOperationException($"Falta o campo '{nome}' na especificação de chave de DF-e.");

    private static T Enumerado<T>(string texto)
        where T : struct, Enum =>
        Enum.GetNames<T>().Contains(texto, StringComparer.Ordinal) && Enum.TryParse<T>(texto, out var valor)
            ? valor
            : throw new InvalidOperationException("Enumerado desconhecido na especificação de chave de DF-e.");

    private static int Inteiro(string texto) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new InvalidOperationException("Número fora do formato na especificação de chave de DF-e.");

    private static char Caractere(string texto) =>
        texto.Length == 1 ? texto[0] : throw new InvalidOperationException("Campo de um caractere na especificação de chave de DF-e.");

    // Uma semente que não serve é pulada pelo gerador de vetores.
    private static void Exigir(bool condicao, string motivo)
    {
        if (!condicao)
        {
            throw new InvalidDataException($"Chave de DF-e: {motivo}.");
        }
    }

    // Erro de receita: interrompe o gerador em vez de pular a semente.
    private static void Programacao(bool condicao, string motivo)
    {
        if (!condicao)
        {
            throw new InvalidOperationException($"Chave de DF-e: {motivo}.");
        }
    }
}
