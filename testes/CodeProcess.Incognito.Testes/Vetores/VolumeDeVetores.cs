using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>Um caso do vetor de volume: transformador, domínio, opções, regra das entradas e quantidade.</summary>
/// <param name="Nome">O nome do caso, que também alimenta o contador das entradas.</param>
/// <param name="Transformador">O transformador, na versão 1.</param>
/// <param name="Dominio">O domínio.</param>
/// <param name="Quantidade">Quantas entradas.</param>
/// <param name="PermutarOrdem">Só no CNPJ.</param>
/// <param name="N">Só no <c>id.permutar</c>.</param>
/// <param name="Entradas">
/// A regra das entradas, gravada no caso (os três primeiros casos seguem a regra da descrição do arquivo, que não muda).
/// </param>
internal sealed record CasoDeVolume(string Nome, string Transformador, string Dominio, int Quantidade, bool? PermutarOrdem = null, long? N = null, string? Entradas = null);

/// <summary>
/// Vetor de volume (<c>volume@1.json</c>): o resumo SHA-256 das saídas de milhares de valores por caso, para um erro que
/// atinge só uma fração pequena dos valores também quebrar. As entradas não vêm do <see cref="GeradorBr"/>: saem de um
/// contador por SHA-256, numa regra simples que a referência em Python refaz.
/// </summary>
/// <remarks>
/// Para cada caso e i de 0 a quantidade − 1: <c>h = SHA-256(nome do caso ‖ 0x00 ‖ u32be(i))</c>. CPF: base =
/// <c>u64be(h[0..8]) mod 10⁹</c> com 9 dígitos. CNPJ: alfanumérico se <c>h[10]</c> for ímpar, e cada um dos 12 primeiros
/// caracteres é <c>h[11 + k]</c> mod 36 em <c>0-9A-Z</c> (mod 10 em <c>0-9</c> no numérico). DV calculado e, quando
/// <c>h[8] mod 4 = 0</c>, deslocado em <c>1 + h[9] mod 99</c> (mod 100). Cada saída entra no resumo como
/// <c>saída ‖ " " ‖ desfecho ‖ "\n"</c> em UTF-8. Políticas: DV errado preservado, malformado falha. Os casos dos outros
/// transformadores trazem a regra deles no campo <c>entradas</c>, com malformado preservado.
/// </remarks>
internal static class VolumeDeVetores
{
    internal const string Arquivo = "volume@1.json";

    private const string Digitos = "0123456789";
    private const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // O início comum do campo entradas dos casos que trazem a regra (a descrição do arquivo, congelada, fala só de CPF e
    // CNPJ), e o fim das linhas de resumo de uma saída.
    private const string Comum =
        "h = SHA-256(UTF-8(nome do caso) ‖ 0x00 ‖ u32be(i)) para i de 0 a quantidade - 1; h[a..b] são os bytes de a a b - 1; "
        + "u16be, u32be e u64be leem big-endian; resumo = SHA-256 da concatenação das linhas, em UTF-8. ";

    private const string LinhaDeSaida = "Linha do resumo: saída ‖ espaço ‖ desfecho pelo nome ‖ LF, com a saída nula como texto vazio.";

    private const string Identificadores = Comum
        + "x = (u64be(h[0..8]) mod (N + N/5)) - N/10, inteiro com sinal: cerca de 1/12 das entradas fica abaixo de 1 (ponto "
        + "fixo) e 1/12 a partir de N (malformado). Saída em decimal, com - nos negativos. " + LinhaDeSaida;

    private static readonly int[] _ufs = [11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31, 32, 33, 35, 41, 42, 43, 50, 51, 52, 53];
    private static readonly int[] _modelos = [55, 65, 57, 58, 67, 64, 63, 66, 62];
    private static readonly string[] _dominiosDeEmail = ["example.org", "example.com", "example.net"];
    private static readonly string[] _naoGeograficos = ["800", "300", "303", "500", "900"];
    private static readonly char[] _acentos = ['À', 'Ç', 'É', 'Õ', 'Þ', '×', 'ß', 'ÿ'];
    private static readonly string[] _dddsInexistentes = ["23", "25", "26", "29", "36", "39", "52", "56", "57", "58", "59", "72", "76", "78"];

    /// <summary>Os casos, na ordem do arquivo. Os três primeiros já são contrato; os outros só se acrescentam.</summary>
    internal static IReadOnlyList<CasoDeVolume> Casos { get; } =
    [
        new("br.cpf@1, DV preservado", "br.cpf", "cpf", 3_000),
        new("br.cnpj@1, raiz permutada", "br.cnpj", "cnpj", 3_000, PermutarOrdem: false),
        new("br.cnpj@1, raiz e ordem permutadas", "br.cnpj", "cnpj", 3_000, PermutarOrdem: true),
        new("br.telefone@1, classes e formas", "br.telefone", "telefone", 3_000, Entradas: Comum
            + "DDD = (1 + h[0] mod 9) ‖ (1 + h[1] mod 9); r = u64be(h[3..11]); número por h[2] mod 5: 0 celular, 9 ‖ (r mod 10^8, 8 "
            + "dígitos); 1 fixo, (2 + h[11] mod 4) ‖ (r mod 10^7, 7 dígitos); 2 celular antigo, (6 + h[11] mod 4) ‖ (r mod 10^7, 7 "
            + "dígitos); 3 fixo de serviço, (3003, 4004)[h[11] mod 2] ‖ (r mod 10^4, 4 dígitos), com o DDD trocado por um "
            + "inexistente, (23, 25, 26, 29, 36, 39, 52, 56, 57, 58, 59, 72, 76, 78)[h[0] mod 14]; 4 não geográfico, 0 ‖ (800, 300, "
            + "303, 500, 900)[h[11] mod 5] ‖ (r mod 10^7, 7 dígitos), e o texto é só esse número. Nas classes 0 a 3, forma por "
            + "h[12] mod 6: 0 DDD ‖ número; 1 ( ‖ DDD ‖ ) ‖ espaço ‖ número sem os 4 últimos ‖ - ‖ os 4 últimos, como (DD) "
            + "NNNNN-NNNN; 2 +55 ‖ DDD ‖ número; 3 0 ‖ DDD ‖ número; 4 só o número (na classe 3, a forma 0); 5 +55 ‖ espaço ‖ a "
            + "forma 1. Em qualquer classe, se h[13] mod 32 = 0, acrescenta-se # no fim (malformado). Os números de serviço "
            + "(0800, 0300, 0303, 0500 e 0900 com 7 dígitos sorteados; DDD inexistente com 3003 ou 4004 e 4 sorteados) podem "
            + "coincidir com números de empresas: são necessários para provar a preservação e nunca são de pessoa. " + LinhaDeSaida),
        new("id.permutar@1, N = 10^4", "id.permutar", "pedido-id", 3_000, N: 10_000, Entradas: Identificadores),
        new("id.permutar@1, N = 10^9", "id.permutar", "pedido-id", 3_000, N: 1_000_000_000, Entradas: Identificadores),
        new("br.cep@1, CEPs e entidades", "br.cep", "cep", 3_000, Entradas: Comum
            + "c = u64be(h[0..8]) mod 10^8; forma por h[8] mod 4: 0 c com 8 dígitos; 1 os 5 primeiros ‖ - ‖ os 3 últimos; 2 os 2 "
            + "primeiros ‖ . ‖ os 3 seguintes ‖ - ‖ os 3 últimos; 3 c em decimal, sem zeros à esquerda. Em qualquer forma, se "
            + "h[13] mod 16 = 0, acrescentam-se dois espaços no fim (preenchimento de char(n)). Entidade = pessoa- ‖ h[9..13] em "
            + "hexadecimal minúsculo. " + LinhaDeSaida),
        new("email@1, endereços", "email", "email", 3_000, Entradas: Comum
            + "base = h[0..6] em hexadecimal minúsculo; acento = (À, Ç, É, Õ, Þ, ×, ß, ÿ)[h[9] mod 8]; local por h[6] mod 4: 0 "
            + "base; 1 base em maiúsculas; 2 acento ‖ base; 3 acento, sem mudar a caixa, ‖ base em maiúsculas. Domínio = "
            + "(example.org, example.com, example.net)[h[7] mod 3], em maiúsculas se h[8] for ímpar. Por h[10] mod 16: 0 local ‖ "
            + "@ ‖ domínio ‖ dois espaços (preenchimento de char(n)); 1 local ‖ domínio, sem a @ (malformado); senão local ‖ @ "
            + "‖ domínio. " + LinhaDeSaida),
        new("br.chave-dfe@1, chaves dos nove modelos", "br.chave-dfe", "dfe-numero", 3_000, Entradas: Comum
            + "cUF = (11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31, 32, 33, 35, 41, 42, 43, 50, 51, 52, 53)[h[0] "
            + "mod 27]; AAMM = (18 + h[1] mod 9) ‖ (1 + h[2] mod 12), 2 dígitos cada; emitente, 14 caracteres, por h[4] mod 3: 0 "
            + "h[5 + k] mod 10 para k = 0..11 ‖ DV do CNPJ; 1 000 ‖ h[5 + k] mod 10 para k = 0..8 ‖ DV do CPF; 2 (0-9A-Z)[h[5 + k] "
            + "mod 36] para k = 0..11 ‖ DV do CNPJ. Nos três tipos, se os 14 caracteres do emitente, lidos como CNPJ, têm os 8 "
            + "primeiros só dígitos começando com 00, os 4 seguintes diferentes de 0000 e o DV de CNPJ dos 12 primeiros certo "
            + "(raiz de banco, órgão público ou estatal), o último dígito d vira (d + 1) mod 10. modelo = (55, 65, 57, 58, 67, "
            + "64, 63, 66, 62)[h[3] mod 9]; série = u16be(h[17..19]) mod 1000, 3 dígitos; número = u32be(h[19..23]) mod 10^9, 9 "
            + "dígitos; tpEmis = 3 se h[23] mod 4 = 0, senão 1; código = u32be(h[24..28]) mod 10^8, 8 dígitos. Chave = cUF ‖ "
            + "AAMM ‖ emitente ‖ modelo ‖ série ‖ número ‖ tpEmis ‖ código ‖ cDV, com o cDV certo deslocado em 1 + h[30] mod 9 "
            + "(mod 10) se h[29] mod 8 = 0. Forma por h[28] mod 4: 0 só a chave; 1 prefixo do Id ‖ chave, com NFe (55, 65), CTe "
            + "(57, 64, 67), MDFe (58), BPe (63), NF3e (66) ou NFCom (62); 2 a forma 1 com o prefixo em minúsculas; 3 os 11 "
            + "grupos de 4 separados por um espaço. Opções: os nove modelos, DV errado preservado (entradaInvalida preservar), "
            + "componentes br.cpf@1 no domínio cpf e br.cnpj@1 no domínio cnpj com permutarOrdem falso. Linha do resumo: "
            + "saída ‖ espaço ‖ desfecho pelo nome ‖ espaço ‖ ocorrências ‖ LF, com as ocorrências pelo nome, separadas por | "
            + "na ordem NumeroZero, SequencialZero, DataNffInvalida, CodigoProibido, CodigoIgualAoNumero, EmitentePontoFixo, "
            + "DvDoEmitenteInvalido, Anomalia; sem ocorrência, texto vazio, e a linha termina em desfecho ‖ espaço ‖ LF."),
    ];

    internal static string Descricao =>
        "Volume: resumo SHA-256 das saídas de CPFs e CNPJs (versão 1) cujas entradas saem de um contador. Chave pública de "
        + "teste: nunca use fora dos testes. Para cada caso e i de 0 a quantidade - 1, h = SHA-256(nome do caso, 0x00, u32be(i)); "
        + "CPF: base = u64be(h[0..8]) mod 10^9; CNPJ: alfanumérico se h[10] for ímpar, caractere k = h[11 + k] mod 36 em 0-9A-Z "
        + "(mod 10 em 0-9 no numérico); DV calculado e, se h[8] mod 4 = 0, deslocado em 1 + h[9] mod 99. Linha do resumo: "
        + "saída, espaço, desfecho, LF, em UTF-8. DV errado preservado, malformado falha. Contrato: um resumo que mudar exige "
        + "outra versão do transformador; casos novos só se acrescentam.";

    /// <summary>O resumo SHA-256, em hexadecimal minúsculo, das saídas do caso.</summary>
    internal static string Resumo(ChaveIncognito chave, CasoDeVolume caso, string ambiente)
    {
        using var resumo = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var executor = Executor(chave, caso, ambiente);
        for (var i = 0; i < caso.Quantidade; i++)
        {
            resumo.AppendData(Encoding.UTF8.GetBytes(executor.Linha(Semente(caso.Nome, i))));
        }

        return Convert.ToHexStringLower(resumo.GetHashAndReset());
    }

    /// <summary>O arquivo inteiro, para o gerador.</summary>
    internal static byte[] Escrever(ChaveIncognito chave, string textoDaChave)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            escritor.WriteStartObject();
            escritor.WriteString("descricao", Descricao);
            escritor.WriteString("chave", textoDaChave);
            escritor.WriteStartArray("casos");
            foreach (var caso in Casos)
            {
                escritor.WriteStartObject();
                escritor.WriteString("nome", caso.Nome);
                escritor.WriteString("transformador", caso.Transformador);
                escritor.WriteNumber("versao", 1);
                escritor.WriteString("dominio", caso.Dominio);
                escritor.WriteString("ambiente", "homologacao");
                if (caso.PermutarOrdem is { } ordem)
                {
                    escritor.WriteBoolean("permutarOrdem", ordem);
                }

                if (caso.N is { } n)
                {
                    escritor.WriteNumber("n", n);
                }

                if (caso.Entradas is { } entradas)
                {
                    escritor.WriteString("entradaMalformada", "preservar");
                    escritor.WriteString("entradas", entradas);
                }

                escritor.WriteNumber("quantidade", caso.Quantidade);
                escritor.WriteString("resumo", Resumo(chave, caso, "homologacao"));
                escritor.WriteEndObject();
            }

            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }

        memoria.WriteByte((byte)'\n');
        return memoria.ToArray();
    }

    /// <summary>As propriedades de cada caso no arquivo, na ordem.</summary>
    internal static string[] Propriedades(CasoDeVolume caso) =>
    [
        "nome", "transformador", "versao", "dominio", "ambiente",
        .. caso.PermutarOrdem is null ? Array.Empty<string>() : ["permutarOrdem"],
        .. caso.N is null ? Array.Empty<string>() : ["n"],
        .. caso.Entradas is null ? Array.Empty<string>() : ["entradaMalformada", "entradas"],
        "quantidade", "resumo",
    ];

    private static ExecutorDeVolume Executor(ChaveIncognito chave, CasoDeVolume caso, string ambiente)
    {
        var descartaveis = new List<IDisposable>();
        try
        {
            Func<byte[], string> linha;
            switch (caso.Transformador)
            {
                case "br.cpf":
                    var cpf = Guardar(descartaveis, new PseudonimizadorCpf(chave, caso.Dominio, 1, ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
                    linha = h =>
                    {
                        var base9 = (BinaryPrimitives.ReadUInt64BigEndian(h) % 1_000_000_000UL).ToString("D9", CultureInfo.InvariantCulture);
                        return Linha(cpf.Pseudonimizar(base9 + Dv(h, DigitosVerificadores.Cpf(base9)), out var saida), saida);
                    };
                    break;
                case "br.cnpj":
                    var cnpj = Guardar(descartaveis, new PseudonimizadorCnpj(
                        chave, caso.Dominio, 1, ambiente, caso.PermutarOrdem!.Value, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
                    linha = h =>
                    {
                        var alfabeto = h[10] % 2 == 1 ? Base36 : Digitos;
                        var base12 = new string([.. Enumerable.Range(0, 12).Select(k => alfabeto[h[11 + k] % alfabeto.Length])]);
                        return Linha(cnpj.Pseudonimizar(base12 + Dv(h, DigitosVerificadores.Cnpj(base12)), out var saida), saida);
                    };
                    break;
                case "br.telefone":
                    var telefone = Guardar(descartaveis, new PseudonimizadorTelefone(chave, caso.Dominio, 1, ambiente, PoliticaEntradaMalformada.Preservar));
                    linha = h => Linha(telefone.Pseudonimizar(Telefone(h), out var saida), saida);
                    break;
                case "id.permutar":
                    var identificador = Guardar(descartaveis, new PseudonimizadorIdentificador(chave, caso.Dominio, 1, ambiente, caso.N!.Value, PoliticaEntradaMalformada.Preservar));
                    linha = h =>
                    {
                        var n = caso.N.Value;
                        var x = (long)(BinaryPrimitives.ReadUInt64BigEndian(h) % (ulong)(n + (n / 5))) - (n / 10);
                        var desfecho = identificador.Pseudonimizar(x, out var saida);
                        return Linha(desfecho, saida?.ToString(CultureInfo.InvariantCulture));
                    };
                    break;
                case "br.cep":
                    var cep = Guardar(descartaveis, new PseudonimizadorCep(chave, caso.Dominio, 1, ambiente, PoliticaEntradaMalformada.Preservar));
                    linha = h => Linha(cep.Pseudonimizar(Cep(h), "pessoa-" + Convert.ToHexStringLower(h, 9, 4), out var saida), saida);
                    break;
                case "email":
                    var email = Guardar(descartaveis, new PseudonimizadorEmail(chave, caso.Dominio, 1, ambiente, PoliticaEntradaMalformada.Preservar));
                    linha = h => Linha(email.Pseudonimizar(Email(h), out var saida), saida);
                    break;
                case "br.chave-dfe":
                    var cpfDoEmitente = Guardar(descartaveis, new PseudonimizadorCpf(chave, "cpf", 1, ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar));
                    var cnpjDoEmitente = Guardar(descartaveis, new PseudonimizadorCnpj(chave, "cnpj", 1, ambiente, false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar));
                    var chaveDfe = Guardar(descartaveis, new PseudonimizadorChaveDfe(
                        chave, 1, ambiente, cpfDoEmitente, cnpjDoEmitente, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Preservar));
                    linha = h =>
                    {
                        var desfecho = chaveDfe.Pseudonimizar(ChaveDfe(h), out var saida, out var ocorrencias);
                        var nomes = Enum.GetValues<OcorrenciasChaveDfe>().Where(o => o != OcorrenciasChaveDfe.Nenhuma && ocorrencias.HasFlag(o));
                        return $"{saida} {desfecho} {string.Join('|', nomes)}\n";
                    };
                    break;
                default:
                    throw new InvalidDataException("Transformador fora do volume.");
            }

            return new ExecutorDeVolume(descartaveis, linha);
        }
        catch
        {
            descartaveis.ForEach(d => d.Dispose());
            throw;
        }
    }

    private static T Guardar<T>(List<IDisposable> descartaveis, T descartavel)
        where T : IDisposable
    {
        descartaveis.Add(descartavel);
        return descartavel;
    }

    private static string Linha(Desfecho desfecho, string? saida) => $"{saida} {desfecho}\n";

    private static string Telefone(byte[] h)
    {
        var ddd = string.Concat((char)('1' + (h[0] % 9)), (char)('1' + (h[1] % 9)));
        var r = BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(3));
        var numero = (h[2] % 5) switch
        {
            0 => "9" + (r % 100_000_000UL).ToString("D8", CultureInfo.InvariantCulture),
            1 => (char)('2' + (h[11] % 4)) + (r % 10_000_000UL).ToString("D7", CultureInfo.InvariantCulture),
            2 => (char)('6' + (h[11] % 4)) + (r % 10_000_000UL).ToString("D7", CultureInfo.InvariantCulture),
            3 => (h[11] % 2 == 0 ? "3003" : "4004") + (r % 10_000UL).ToString("D4", CultureInfo.InvariantCulture),
            _ => null,
        };
        var servico = h[2] % 5 == 3;
        ddd = servico ? _dddsInexistentes[h[0] % 14] : ddd;
        var forma = servico && h[12] % 6 == 4 ? 0 : h[12] % 6;
        var telefone = numero is null
            ? "0" + _naoGeograficos[h[11] % 5] + (r % 10_000_000UL).ToString("D7", CultureInfo.InvariantCulture)
            : forma switch
            {
                0 => ddd + numero,
                1 => $"({ddd}) {numero[..^4]}-{numero[^4..]}",
                2 => "+55" + ddd + numero,
                3 => "0" + ddd + numero,
                4 => numero,
                _ => $"+55 ({ddd}) {numero[..^4]}-{numero[^4..]}",
            };
        return h[13] % 32 == 0 ? telefone + "#" : telefone;
    }

    private static string Cep(byte[] h)
    {
        var valor = BinaryPrimitives.ReadUInt64BigEndian(h) % 100_000_000UL;
        var cep = valor.ToString("D8", CultureInfo.InvariantCulture);
        var texto = (h[8] % 4) switch
        {
            0 => cep,
            1 => $"{cep[..5]}-{cep[5..]}",
            2 => $"{cep[..2]}.{cep[2..5]}-{cep[5..]}",
            _ => valor.ToString(CultureInfo.InvariantCulture),
        };
        return h[13] % 16 == 0 ? texto + "  " : texto;
    }

    private static string Email(byte[] h)
    {
        var local = Convert.ToHexStringLower(h, 0, 6);
        local = (h[6] % 4) switch
        {
            0 => local,
            1 => local.ToUpperInvariant(),
            2 => _acentos[h[9] % 8] + local,
            _ => _acentos[h[9] % 8] + local.ToUpperInvariant(),
        };
        var dominio = _dominiosDeEmail[h[7] % 3];
        dominio = h[8] % 2 == 1 ? dominio.ToUpperInvariant() : dominio;
        return (h[10] % 16) switch
        {
            0 => local + "@" + dominio + "  ",
            1 => local + dominio,
            _ => local + "@" + dominio,
        };
    }

    private static string ChaveDfe(byte[] h)
    {
        var emitente = (h[4] % 3) switch
        {
            0 => ComDvDoCnpj(new string([.. Enumerable.Range(0, 12).Select(k => Digitos[h[5 + k] % 10])])),
            1 => "000" + ComDvDoCpf(new string([.. Enumerable.Range(0, 9).Select(k => Digitos[h[5 + k] % 10])])),
            _ => ComDvDoCnpj(new string([.. Enumerable.Range(0, 12).Select(k => Base36[h[5 + k] % 36])])),
        };
        if (OrigensChaveDfe.RaizBaixaComDvCerto(emitente))
        {
            emitente = emitente[..13] + (char)('0' + ((emitente[13] - '0' + 1) % 10));
        }

        var modelo = _modelos[h[3] % 9];
        var chave = string.Concat(
            _ufs[h[0] % 27].ToString("D2", CultureInfo.InvariantCulture),
            (18 + (h[1] % 9)).ToString("D2", CultureInfo.InvariantCulture),
            (1 + (h[2] % 12)).ToString("D2", CultureInfo.InvariantCulture),
            emitente)
            + modelo.ToString("D2", CultureInfo.InvariantCulture)
            + (BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(17)) % 1000).ToString("D3", CultureInfo.InvariantCulture)
            + (BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(19)) % 1_000_000_000U).ToString("D9", CultureInfo.InvariantCulture)
            + (h[23] % 4 == 0 ? "3" : "1")
            + (BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(24)) % 100_000_000U).ToString("D8", CultureInfo.InvariantCulture);
        var cdv = DigitosVerificadores.ChaveDfe(chave);
        chave += (char)('0' + (h[29] % 8 == 0 ? (cdv + 1 + (h[30] % 9)) % 10 : cdv));
        var prefixo = modelo switch
        {
            55 or 65 => "NFe",
            57 or 64 or 67 => "CTe",
            58 => "MDFe",
            63 => "BPe",
            66 => "NF3e",
            _ => "NFCom",
        };
        return (h[28] % 4) switch
        {
            0 => chave,
            1 => prefixo + chave,
            2 => prefixo.ToLowerInvariant() + chave,
            _ => string.Join(' ', Enumerable.Range(0, 11).Select(g => chave.Substring(g * 4, 4))),
        };
    }

    private static string ComDvDoCpf(string base9) => base9 + DigitosVerificadores.Cpf(base9).ToString("00", CultureInfo.InvariantCulture);

    private static string ComDvDoCnpj(string base12) => base12 + DigitosVerificadores.Cnpj(base12).ToString("00", CultureInfo.InvariantCulture);

    private static byte[] Semente(string nome, int i)
    {
        var mensagem = new byte[Encoding.UTF8.GetByteCount(nome) + 1 + sizeof(uint)];
        var escritos = Encoding.UTF8.GetBytes(nome, mensagem);
        mensagem[escritos] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(mensagem.AsSpan(escritos + 1), (uint)i);
        return SHA256.HashData(mensagem);
    }

    private static string Dv(byte[] h, int correto) =>
        (h[8] % 4 == 0 ? (correto + 1 + (h[9] % 99)) % 100 : correto).ToString("00", CultureInfo.InvariantCulture);

    private sealed class ExecutorDeVolume(List<IDisposable> descartaveis, Func<byte[], string> linha) : IDisposable
    {
        internal string Linha(byte[] h) => linha(h);

        // Os compostos antes das instâncias que eles usam.
        public void Dispose()
        {
            for (var i = descartaveis.Count - 1; i >= 0; i--)
            {
                descartaveis[i].Dispose();
            }
        }
    }
}
