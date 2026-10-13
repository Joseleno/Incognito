using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Vetor de volume (<c>volume@1.json</c>): o resumo SHA-256 das saídas de milhares de CPFs e CNPJs, para um erro que
/// atinge só uma fração pequena dos valores também quebrar. As entradas não vêm do <see cref="GeradorBr"/>: saem de um
/// contador por SHA-256, numa regra simples que a referência em Python refaz.
/// </summary>
/// <remarks>
/// Para cada caso e i de 0 a quantidade − 1: <c>h = SHA-256(nome do caso ‖ 0x00 ‖ u32be(i))</c>. CPF: base =
/// <c>u64be(h[0..8]) mod 10⁹</c> com 9 dígitos. CNPJ: alfanumérico se <c>h[10]</c> for ímpar, e cada um dos 12 primeiros
/// caracteres é <c>h[11 + k]</c> mod 36 em <c>0-9A-Z</c> (mod 10 em <c>0-9</c> no numérico). DV calculado e, quando
/// <c>h[8] mod 4 = 0</c>, deslocado em <c>1 + h[9] mod 99</c> (mod 100). Cada saída entra no resumo como
/// <c>saída ‖ " " ‖ desfecho ‖ "\n"</c> em UTF-8. Políticas: DV errado preservado, malformado falha.
/// </remarks>
internal static class VolumeDeVetores
{
    internal const string Arquivo = "volume@1.json";

    private const string Digitos = "0123456789";
    private const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>Os casos: nome, transformador, domínio, ordem permutada (só CNPJ) e quantidade.</summary>
    internal static IReadOnlyList<(string Nome, string Transformador, string Dominio, bool? PermutarOrdem, int Quantidade)> Casos { get; } =
    [
        ("br.cpf@1, DV preservado", "br.cpf", "cpf", null, 3_000),
        ("br.cnpj@1, raiz permutada", "br.cnpj", "cnpj", false, 3_000),
        ("br.cnpj@1, raiz e ordem permutadas", "br.cnpj", "cnpj", true, 3_000),
    ];

    internal static string Descricao =>
        "Volume: resumo SHA-256 das saídas de CPFs e CNPJs (versão 1) cujas entradas saem de um contador. Chave pública de "
        + "teste: nunca use fora dos testes. Para cada caso e i de 0 a quantidade - 1, h = SHA-256(nome do caso, 0x00, u32be(i)); "
        + "CPF: base = u64be(h[0..8]) mod 10^9; CNPJ: alfanumérico se h[10] for ímpar, caractere k = h[11 + k] mod 36 em 0-9A-Z "
        + "(mod 10 em 0-9 no numérico); DV calculado e, se h[8] mod 4 = 0, deslocado em 1 + h[9] mod 99. Linha do resumo: "
        + "saída, espaço, desfecho, LF, em UTF-8. DV errado preservado, malformado falha. Contrato: um resumo que mudar exige "
        + "outra versão do transformador; casos novos só se acrescentam.";

    /// <summary>O resumo SHA-256, em hexadecimal minúsculo, das saídas do caso.</summary>
    internal static string Resumo(ChaveIncognito chave, string nome, string transformador, string dominio, string ambiente, bool? permutarOrdem, int quantidade)
    {
        using var cpf = transformador == "br.cpf"
            ? new PseudonimizadorCpf(chave, dominio, 1, ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar)
            : null;
        using var cnpj = transformador == "br.cnpj"
            ? new PseudonimizadorCnpj(chave, dominio, 1, ambiente, permutarOrdem!.Value, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar)
            : null;
        using var resumo = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < quantidade; i++)
        {
            var h = Semente(nome, i);
            string? saida;
            Desfecho desfecho;
            if (cpf is not null)
            {
                var base9 = (BinaryPrimitives.ReadUInt64BigEndian(h) % 1_000_000_000UL).ToString("D9", CultureInfo.InvariantCulture);
                desfecho = cpf.Pseudonimizar(base9 + Dv(h, DigitosVerificadores.Cpf(base9)), out saida);
            }
            else
            {
                var alfabeto = h[10] % 2 == 1 ? Base36 : Digitos;
                var base12 = new string([.. Enumerable.Range(0, 12).Select(k => alfabeto[h[11 + k] % alfabeto.Length])]);
                desfecho = cnpj!.Pseudonimizar(base12 + Dv(h, DigitosVerificadores.Cnpj(base12)), out saida);
            }

            resumo.AppendData(Encoding.UTF8.GetBytes($"{saida} {desfecho}\n"));
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
            foreach (var (nome, transformador, dominio, permutarOrdem, quantidade) in Casos)
            {
                escritor.WriteStartObject();
                escritor.WriteString("nome", nome);
                escritor.WriteString("transformador", transformador);
                escritor.WriteNumber("versao", 1);
                escritor.WriteString("dominio", dominio);
                escritor.WriteString("ambiente", "homologacao");
                if (permutarOrdem is { } ordem)
                {
                    escritor.WriteBoolean("permutarOrdem", ordem);
                }

                escritor.WriteNumber("quantidade", quantidade);
                escritor.WriteString("resumo", Resumo(chave, nome, transformador, dominio, "homologacao", permutarOrdem, quantidade));
                escritor.WriteEndObject();
            }

            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }

        memoria.WriteByte((byte)'\n');
        return memoria.ToArray();
    }

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
}
