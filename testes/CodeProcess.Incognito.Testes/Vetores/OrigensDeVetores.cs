using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Proveniência das entradas dos vetores: refaz a entrada a partir da origem registrada no par. <c>gerador-br</c> chama
/// um método do <see cref="GeradorBr"/> com a semente; <c>construido</c> aplica uma regra sobre a mesma sequência
/// (repdígito, raiz zero, número de serviço: nunca um literal com DV válido); <c>inversa</c> parte de um valor excluído
/// e aplica a permutação inversa com a chave pública do vetor, para a entrada passar pelo cycle-walking. Depois vêm as
/// formas (máscara, DV deslocado, sem zeros, espaços, prefixo, sufixo, troca), na ordem. Uma origem sem campo de versão
/// usa a sequência da versão maior 1 do <see cref="GeradorBr"/>; uma mudança de sequência numa versão maior futura vale
/// só para origens novas, que passam a registrar a versão.
/// </summary>
internal static class OrigensDeVetores
{
    private const string Digitos = "0123456789";
    private const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Hexadecimais = "0123456789abcdef";

    private static readonly Dictionary<string, Func<GeradorBr, string>> _metodos = Metodos();

    private static Dictionary<string, Func<GeradorBr, string>> Metodos()
    {
        var metodos = new Dictionary<string, Func<GeradorBr, string>>(StringComparer.Ordinal)
        {
            ["Cpf()"] = g => g.Cpf(),
            ["Cpf(formatado)"] = g => g.Cpf(formatado: true),
            ["Cnpj()"] = g => g.Cnpj(),
            ["Cnpj(formatado)"] = g => g.Cnpj(formatado: true),
            ["Cnpj(alfanumerico)"] = g => g.Cnpj(alfanumerico: true),
            ["Cnpj(alfanumerico, formatado)"] = g => g.Cnpj(alfanumerico: true, formatado: true),
            ["Telefone(celular)"] = g => g.Telefone(celular: true),
            ["Telefone(celular, comPais)"] = g => g.Telefone(celular: true, comPais: true),
            ["Telefone(celular, formatado)"] = g => g.Telefone(celular: true, formatado: true),
            ["Telefone(celular, comPais, formatado)"] = g => g.Telefone(celular: true, comPais: true, formatado: true),
            ["Telefone(fixo)"] = g => g.Telefone(celular: false),
            ["Telefone(fixo, comPais)"] = g => g.Telefone(celular: false, comPais: true),
            ["Telefone(fixo, formatado)"] = g => g.Telefone(celular: false, formatado: true),
            ["Telefone(fixo, comPais, formatado)"] = g => g.Telefone(celular: false, comPais: true, formatado: true),
            ["Cep()"] = g => g.Cep(),
            ["Cep(formatado)"] = g => g.Cep(formatado: true),
            ["Email()"] = g => g.Email(),
            ["ChavePix(Cpf)"] = g => g.ChavePix(TipoChavePix.Cpf),
            ["ChavePix(Cnpj)"] = g => g.ChavePix(TipoChavePix.Cnpj),
            ["ChavePix(Email)"] = g => g.ChavePix(TipoChavePix.Email),
            ["ChavePix(Telefone)"] = g => g.ChavePix(TipoChavePix.Telefone),
            ["ChavePix(Aleatoria)"] = g => g.ChavePix(TipoChavePix.Aleatoria),
        };
        foreach (var uf in FaixasDeCep.Ufs)
        {
            metodos[$"Cep(uf: {uf})"] = g => g.Cep(uf);
        }

        return metodos;
    }

    /// <summary>Refaz a entrada (texto ou inteiro) a partir da origem.</summary>
    internal static object Construir(JsonElement origem, ContextoDeOrigem contexto)
    {
        var tipo = origem.GetProperty("tipo").GetString();
        if (tipo == "literal")
        {
            throw new InvalidOperationException("Origem literal não se refaz.");
        }

        var semente = origem.GetProperty("semente").GetInt64();
        var valor = tipo switch
        {
            "gerador-br" => _metodos.TryGetValue(origem.GetProperty("metodo").GetString()!, out var metodo)
                ? metodo(new GeradorBr(semente))
                : throw new InvalidOperationException("Método do GeradorBr desconhecido."),
            "construido" => Construido(origem.GetProperty("regra").GetString()!, SequenciaXoshiro.DaSemente(semente), contexto),
            "inversa" => Inversa(origem.GetProperty("regra").GetString()!, SequenciaXoshiro.DaSemente(semente), contexto),
            _ => throw new InvalidOperationException("Tipo de origem desconhecido."),
        };

        object resultado = valor;
        foreach (var forma in origem.GetProperty("formas").EnumerateArray())
        {
            if (resultado is not string texto)
            {
                throw new InvalidOperationException("Forma depois de 'numero'.");
            }

            resultado = AplicarForma(texto, forma.GetString()!);
        }

        return resultado;
    }

    /// <summary>Aplica uma forma; <c>numero</c> devolve o inteiro.</summary>
    internal static object AplicarForma(string valor, string forma)
    {
        var (nome, argumento) = forma.IndexOf(':', StringComparison.Ordinal) is var dois and >= 0
            ? (forma[..dois], forma[(dois + 1)..])
            : (forma, "");
        switch (nome)
        {
            case "numero":
                return long.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
                    ? numero
                    : throw new InvalidDataException("Forma 'numero' sobre texto que não é só dígitos.");
            case "mascara-cpf":
                Exigir(valor.Length == 11, forma);
                return $"{valor[..3]}.{valor[3..6]}.{valor[6..9]}-{valor[9..]}";
            case "mascara-cnpj":
                Exigir(valor.Length == 14, forma);
                return $"{valor[..2]}.{valor[2..5]}.{valor[5..8]}/{valor[8..12]}-{valor[12..]}";
            case "mascara-cep-ponto":
                Exigir(valor.Length == 8, forma);
                return $"{valor[..2]}.{valor[2..5]}-{valor[5..]}";
            case "dv":
                // dv:K desloca os dois últimos dígitos em K (mod 100).
                var deslocamento = Inteiro(argumento, forma);
                Exigir(deslocamento is > 0 and < 100 && valor.Length >= 2 && char.IsAsciiDigit(valor[^1]) && char.IsAsciiDigit(valor[^2]), forma);
                var dv = (((valor[^2] - '0') * 10) + (valor[^1] - '0') + deslocamento) % 100;
                return valor[..^2] + dv.ToString("00", CultureInfo.InvariantCulture);
            case "sem-zeros":
                var zeros = Inteiro(argumento, forma);
                Exigir(zeros > 0 && valor.Length > zeros && valor[..zeros].All(c => c == '0'), forma);
                return valor[zeros..];
            case "espacos":
                return valor + new string(' ', Inteiro(argumento, forma));
            case "prefixo":
                return argumento + valor;
            case "sufixo":
                return valor + argumento;
            case "prefixo-utf16":
                return EntradaDoPar.DeUtf16(argumento) + valor;
            case "sufixo-utf16":
                return valor + EntradaDoPar.DeUtf16(argumento);
            case "trocar" or "trocar-utf16":
                // trocar:I:T põe T no lugar do caractere I.
                var separador = argumento.IndexOf(':', StringComparison.Ordinal);
                Exigir(separador > 0, forma);
                var indice = Inteiro(argumento[..separador], forma);
                var novo = nome == "trocar" ? argumento[(separador + 1)..] : EntradaDoPar.DeUtf16(argumento[(separador + 1)..]);
                Exigir(indice < valor.Length, forma);
                return valor[..indice] + novo + valor[(indice + 1)..];
            case "inserir":
                // inserir:I:T põe T antes do caractere I.
                var posicao = argumento.IndexOf(':', StringComparison.Ordinal);
                Exigir(posicao > 0, forma);
                var antes = Inteiro(argumento[..posicao], forma);
                Exigir(antes <= valor.Length, forma);
                return valor.Insert(antes, argumento[(posicao + 1)..]);
            case "remover":
                // remover:I:N tira N caracteres a partir de I.
                var partes = argumento.Split(':');
                Exigir(partes.Length == 2, forma);
                var inicio = Inteiro(partes[0], forma);
                var quantidade = Inteiro(partes[1], forma);
                Exigir(quantidade > 0 && inicio + quantidade <= valor.Length, forma);
                return valor.Remove(inicio, quantidade);
            case "maiusculas-ascii":
                return new string([.. valor.Select(c => char.IsAsciiLetterLower(c) ? (char)(c - 32) : c)]);
            case "minusculas-ascii":
                return new string([.. valor.Select(c => char.IsAsciiLetterUpper(c) ? (char)(c + 32) : c)]);
            default:
                throw new InvalidOperationException("Forma desconhecida.");
        }
    }

    private static string Construido(string regra, FonteDeSorteio fonte, ContextoDeOrigem contexto)
    {
        var (nome, argumento) = regra.IndexOf(':', StringComparison.Ordinal) is var dois and >= 0
            ? (regra[..dois], regra[(dois + 1)..])
            : (regra, "");
        switch (nome)
        {
            case "cpf-repdigito":
                return ComDvDoCpf(new string((char)('0' + (int)fonte.Uniforme(10)), 9));
            case "cnpj-raiz-zero":
                // A raiz 00000000 com ordem numérica e DV certo é cadastrada: aqui o DV sai sempre deslocado.
                return AplicarDeslocamento(ComDvDoCnpj("00000000" + Sortear(fonte, Digitos, 4)), 1 + (int)fonte.Uniforme(99));
            case "cnpj-raiz-zero-ordem-alfa":
                // Com o DV certo, poderia coincidir com um estabelecimento de raiz 00000000: o DV sai sempre deslocado.
                return AplicarDeslocamento(ComDvDoCnpj("00000000" + SortearComLetra(fonte, 4)), 1 + (int)fonte.Uniforme(99));
            case "cnpj-raiz-quase-zero":
                // Raiz 0000000d (d de 1 a 9): fora da raiz zero, mas abaixo de 10^5 com a ordem.
                // Raízes tão baixas são de bancos e órgãos públicos: o DV sai sempre deslocado.
                return AplicarDeslocamento(ComDvDoCnpj("0000000" + (char)('1' + (int)fonte.Uniforme(9)) + Sortear(fonte, Digitos, 4)), 1 + (int)fonte.Uniforme(99));
            case "cnpj-raiz-quase-zero-letra":
                return AplicarDeslocamento(ComDvDoCnpj("0000000" + (char)('A' + (int)fonte.Uniforme(26)) + Sortear(fonte, Digitos, 4)), 1 + (int)fonte.Uniforme(99));
            case "cnpj-ordem-alfa":
                string raiz;
                do
                {
                    raiz = Sortear(fonte, Digitos, 8);
                }
                while (raiz == "00000000");

                return ComDvDoCnpj(raiz + SortearComLetra(fonte, 4));
            case "telefone-celular-antigo":
                return Ddd(fonte) + (char)('6' + (int)fonte.Uniforme(4)) + Sortear(fonte, Digitos, 7);
            case "telefone-servico-nao-geografico" when argumento.Length == 0:
                return "0" + "8359"[(int)fonte.Uniforme(4)] + "00" + Sortear(fonte, Digitos, 7);
            case "telefone-servico-nao-geografico":
                // Com o prefixo fixo (como 303), 7 dígitos sorteados depois dele.
                Exigir(_prefixosNaoGeograficos.Contains(argumento), regra);
                return "0" + argumento + Sortear(fonte, Digitos, 7);
            case "telefone-servico-antigo":
                // Código não geográfico anterior à padronização em 7 dígitos: 6 sorteados depois do prefixo.
                Exigir(argumento.Length == 0 || _prefixosNaoGeograficos.Contains(argumento), regra);
                var prefixoAntigo = argumento.Length > 0 ? argumento : _prefixosNaoGeograficos[(int)fonte.Uniforme((ulong)_prefixosNaoGeograficos.Length)];
                return "0" + prefixoAntigo + Sortear(fonte, Digitos, 6);
            case "telefone-servico-fixo":
                return DddInexistente(fonte) + (fonte.Uniforme(2) == 0 ? "3003" : "4004") + Sortear(fonte, Digitos, 4);
            case "telefone-celular-ddd":
                Exigir(argumento.Length == 2, regra);
                return argumento + "9" + Sortear(fonte, Digitos, 8);
            case "telefone-fixo-ddd":
                Exigir(argumento.Length == 2, regra);
                return argumento + Fixo(fonte);
            case "telefone-fixo-numero":
                Exigir(argumento.Length == 8, regra);
                return DddInexistente(fonte) + argumento;
            case "id-uniforme":
                return (1 + (long)fonte.Uniforme((ulong)(contexto.N - 1))).ToString(CultureInfo.InvariantCulture);
            case "email-parte-local":
                return Sortear(fonte, "abcdefghijklmnopqrstuvwxyz0123456789", Inteiro(argumento, regra)) + "@example.org";
            case "uuid-sem-versao":
                var uuid = Sortear(fonte, Hexadecimais, 32);
                return $"{uuid[..8]}-{uuid[8..12]}-{uuid[12..16]}-{uuid[16..20]}-{uuid[20..]}";
            default:
                throw new InvalidOperationException("Regra de construção desconhecida.");
        }
    }

    // Parte de um valor excluído e aplica a permutação sem exclusões ao contrário: a entrada cai no excluído no primeiro
    // passo e o cycle-walking acontece. Se a pré-imagem também for excluída, sorteia outro alvo.
    private static string Inversa(string regra, FonteDeSorteio fonte, ContextoDeOrigem contexto)
    {
        switch (regra)
        {
            case "cpf-caminhada":
                using (var permutacao = contexto.Feistel("num", 10, 10_000, 100_000))
                {
                    while (true)
                    {
                        var alvo = fonte.Uniforme(10) * 111_111_111;
                        var entrada = permutacao.Inverter(alvo, []);
                        if (entrada % 111_111_111 != 0)
                        {
                            return ComDvDoCpf(entrada.ToString("D9", CultureInfo.InvariantCulture));
                        }
                    }
                }

            case "cnpj-caminhada-num" when !contexto.PermutarOrdem:
                using (var permutacao = contexto.Feistel("num", 10, 10_000, 10_000))
                {
                    var entrada = permutacao.Inverter(0, []);
                    Exigir(entrada != 0, regra);
                    return ComDvDoCnpj(entrada.ToString("D8", CultureInfo.InvariantCulture) + Sortear(fonte, Digitos, 4));
                }

            case "cnpj-caminhada-num":
                using (var permutacao = contexto.Feistel("ordem", 10, 1_000_000, 1_000_000))
                {
                    while (true)
                    {
                        var entrada = permutacao.Inverter(fonte.Uniforme(10_000), []);
                        if (entrada >= 10_000)
                        {
                            return ComDvDoCnpj(entrada.ToString("D12", CultureInfo.InvariantCulture));
                        }
                    }
                }

            case "cnpj-caminhada-alfa":
                var tamanho = contexto.PermutarOrdem ? 12 : 8;
                var lado = (ulong)Math.Pow(36, tamanho / 2);
                using (var permutacao = contexto.Feistel(contexto.PermutarOrdem ? "ordem" : "alfa", 36, lado, lado))
                {
                    var limiteFixo = contexto.PermutarOrdem ? 1_679_616UL : 1UL;
                    while (true)
                    {
                        // Alvo só com dígitos, escrito em base 36.
                        var alvo = LerBase36(Sortear(fonte, Digitos, tamanho));
                        var entrada = EscreverBase36(permutacao.Inverter(alvo, []), tamanho);
                        if (entrada.Any(char.IsAsciiLetterUpper) && LerBase36(entrada) >= limiteFixo)
                        {
                            return ComDvDoCnpj(contexto.PermutarOrdem ? entrada : entrada + Sortear(fonte, Digitos, 4));
                        }
                    }
                }

            case "cnpj-alvo-digitos-e-a":
                // Alvo só com dígitos e a letra A (ao menos uma): não é excluído, então não há caminhada; prende o
                // limite entre dígito e letra na conferência das cadeias só com dígitos.
                var tamanhoDoAlvo = contexto.PermutarOrdem ? 12 : 8;
                var ladoDoAlvo = (ulong)Math.Pow(36, tamanhoDoAlvo / 2);
                using (var permutacao = contexto.Feistel(contexto.PermutarOrdem ? "ordem" : "alfa", 36, ladoDoAlvo, ladoDoAlvo))
                {
                    while (true)
                    {
                        var alvo = Sortear(fonte, "0123456789A", tamanhoDoAlvo);
                        var entrada = EscreverBase36(permutacao.Inverter(LerBase36(alvo), []), tamanhoDoAlvo);
                        if (alvo.Contains('A', StringComparison.Ordinal) && !alvo.StartsWith("0000", StringComparison.Ordinal)
                            && entrada.Any(char.IsAsciiLetterUpper) && !entrada.StartsWith("0000", StringComparison.Ordinal))
                        {
                            return ComDvDoCnpj(contexto.PermutarOrdem ? entrada : entrada + Sortear(fonte, Digitos, 4));
                        }
                    }
                }

            case "cnpj-caminhada-alfa-raiz-zero":
                // Com a ordem permutada, as cadeias de raiz 00000000 (valor abaixo de 36^4) também são excluídas.
                Exigir(contexto.PermutarOrdem, regra);
                using (var permutacao = contexto.Feistel("ordem", 36, 2_176_782_336, 2_176_782_336))
                {
                    while (true)
                    {
                        var alvo = LerBase36("00000000" + SortearComLetra(fonte, 4));
                        var entrada = EscreverBase36(permutacao.Inverter(alvo, []), 12);
                        if (entrada.Any(char.IsAsciiLetterUpper) && LerBase36(entrada) >= 1_679_616UL)
                        {
                            return ComDvDoCnpj(entrada);
                        }
                    }
                }

            case "telefone-caminhada-fixo":
                using (var permutacao = contexto.Feistel("fixo", 10, 4_000, 10_000))
                {
                    while (true)
                    {
                        var ddd = DddInexistente(fonte);
                        var alvo = (fonte.Uniforme(2) == 0 ? 10_030_000UL : 20_040_000UL) + fonte.Uniforme(10_000);
                        var valor = permutacao.Inverter(alvo, Encoding.ASCII.GetBytes(ddd));
                        var numero = (char)('2' + (int)(valor / 10_000_000)) + (valor % 10_000_000).ToString("D7", CultureInfo.InvariantCulture);
                        if (!numero.StartsWith("3003", StringComparison.Ordinal) && !numero.StartsWith("4004", StringComparison.Ordinal))
                        {
                            return ddd + numero;
                        }
                    }
                }

            case "id-caminhada":
                var digitos = (int)Math.Round(Math.Log10(contexto.N));
                using (var permutacao = contexto.Feistel("id", 10, Potencia10((digitos + 1) / 2), Potencia10(digitos / 2)))
                {
                    return permutacao.Inverter(0, Encoding.ASCII.GetBytes(contexto.N.ToString(CultureInfo.InvariantCulture))).ToString(CultureInfo.InvariantCulture);
                }

            default:
                throw new InvalidOperationException("Regra de inversa desconhecida.");
        }
    }

    private static string AplicarDeslocamento(string documento, int deslocamento) =>
        (string)AplicarForma(documento, "dv:" + deslocamento.ToString(CultureInfo.InvariantCulture));

    private static ulong Potencia10(int expoente)
    {
        ulong valor = 1;
        for (var i = 0; i < expoente; i++)
        {
            valor *= 10;
        }

        return valor;
    }

    private static string Fixo(FonteDeSorteio fonte)
    {
        while (true)
        {
            var numero = (char)('2' + (int)fonte.Uniforme(4)) + Sortear(fonte, Digitos, 7);
            if (!numero.StartsWith("3003", StringComparison.Ordinal) && !numero.StartsWith("4004", StringComparison.Ordinal))
            {
                return numero;
            }
        }
    }

    // DDDs com os dois dígitos de 1 a 9 que não estão no plano de numeração: um número fixo montado sobre eles não é de
    // ninguém.
    private static readonly string[] _dddsInexistentes = ["23", "25", "26", "29", "36", "39", "52", "56", "57", "58", "59", "72", "76", "78"];

    // Códigos não geográficos (Res. Anatel 749/2022, art. 18): números de empresa, nunca de pessoa.
    private static readonly string[] _prefixosNaoGeograficos = ["800", "300", "303", "500", "900"];

    private static string DddInexistente(FonteDeSorteio fonte) => _dddsInexistentes[(int)fonte.Uniforme((ulong)_dddsInexistentes.Length)];

    // DDD com os dois dígitos de 1 a 9 (forma aceita; nem todo DDD assim existe).
    private static string Ddd(FonteDeSorteio fonte) =>
        string.Concat((char)('1' + (int)fonte.Uniforme(9)), (char)('1' + (int)fonte.Uniforme(9)));

    private static string Sortear(FonteDeSorteio fonte, string alfabeto, int tamanho)
    {
        var texto = new char[tamanho];
        for (var i = 0; i < tamanho; i++)
        {
            texto[i] = alfabeto[(int)fonte.Uniforme((ulong)alfabeto.Length)];
        }

        return new string(texto);
    }

    private static string SortearComLetra(FonteDeSorteio fonte, int tamanho)
    {
        while (true)
        {
            var texto = Sortear(fonte, Base36, tamanho);
            if (texto.Any(char.IsAsciiLetterUpper))
            {
                return texto;
            }
        }
    }

    private static string ComDvDoCpf(string base9) =>
        base9 + DigitosVerificadores.Cpf(base9).ToString("00", CultureInfo.InvariantCulture);

    private static string ComDvDoCnpj(string base12) =>
        base12 + DigitosVerificadores.Cnpj(base12).ToString("00", CultureInfo.InvariantCulture);

    private static ulong LerBase36(string texto)
    {
        ulong valor = 0;
        foreach (var caractere in texto)
        {
            valor = (valor * 36) + (ulong)Base36.IndexOf(caractere, StringComparison.Ordinal);
        }

        return valor;
    }

    private static string EscreverBase36(ulong valor, int tamanho)
    {
        var texto = new char[tamanho];
        for (var i = tamanho - 1; i >= 0; i--)
        {
            texto[i] = Base36[(int)(valor % 36)];
            valor /= 36;
        }

        return new string(texto);
    }

    private static int Inteiro(string texto, string onde) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new InvalidOperationException($"Argumento inválido em '{onde}'.");

    private static void Exigir(bool condicao, string onde)
    {
        if (!condicao)
        {
            throw new InvalidDataException($"A forma ou regra '{onde}' não se aplica a este valor.");
        }
    }
}
