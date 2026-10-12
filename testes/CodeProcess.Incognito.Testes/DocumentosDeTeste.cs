using System.Globalization;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Documentos sorteados para os testes e referências clássicas do DV, escritas à parte da implementação. Nenhum
/// identificador fica escrito no código: tudo é sorteado na hora, com a semente de cada teste.
/// </summary>
internal static class DocumentosDeTeste
{
    public const string Alfanumerico = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Faixas de CEP por UF dos Correios, escritas à parte da implementação.
    public static readonly Dictionary<string, (int Inicio, int Fim)[]> FaixasDeCepPorUf = new()
    {
        ["SP"] = [(1_000_000, 19_999_999)],
        ["RJ"] = [(20_000_000, 28_999_999)],
        ["ES"] = [(29_000_000, 29_999_999)],
        ["MG"] = [(30_000_000, 39_999_999)],
        ["BA"] = [(40_000_000, 48_999_999)],
        ["SE"] = [(49_000_000, 49_999_999)],
        ["PE"] = [(50_000_000, 56_999_999)],
        ["AL"] = [(57_000_000, 57_999_999)],
        ["PB"] = [(58_000_000, 58_999_999)],
        ["RN"] = [(59_000_000, 59_999_999)],
        ["CE"] = [(60_000_000, 63_999_999)],
        ["PI"] = [(64_000_000, 64_999_999)],
        ["MA"] = [(65_000_000, 65_999_999)],
        ["PA"] = [(66_000_000, 68_899_999)],
        ["AP"] = [(68_900_000, 68_999_999)],
        ["AM"] = [(69_000_000, 69_299_999), (69_400_000, 69_899_999)],
        ["RR"] = [(69_300_000, 69_399_999)],
        ["AC"] = [(69_900_000, 69_999_999)],
        ["DF"] = [(70_000_000, 72_799_999), (73_000_000, 73_699_999)],
        ["GO"] = [(72_800_000, 72_999_999), (73_700_000, 76_799_999)],
        ["RO"] = [(76_800_000, 76_999_999)],
        ["TO"] = [(77_000_000, 77_999_999)],
        ["MT"] = [(78_000_000, 78_899_999)],
        ["MS"] = [(79_000_000, 79_999_999)],
        ["PR"] = [(80_000_000, 87_999_999)],
        ["SC"] = [(88_000_000, 89_999_999)],
        ["RS"] = [(90_000_000, 99_999_999)],
    };

    public static string CpfValido(Random sorteio)
    {
        string baseCpf;
        do
        {
            baseCpf = Digitos(sorteio, 9);
        }
        while (baseCpf.Distinct().Count() == 1);

        return baseCpf + DvCpfClassico(baseCpf).ToString("00", CultureInfo.InvariantCulture);
    }

    public static string CnpjValido(Random sorteio, bool alfanumerico)
    {
        var alfabeto = alfanumerico ? Alfanumerico : "0123456789";
        string baseCnpj;
        do
        {
            baseCnpj = new string(Enumerable.Range(0, 12).Select(_ => alfabeto[sorteio.Next(alfabeto.Length)]).ToArray());
        }
        while (baseCnpj.Distinct().Count() == 1);

        return baseCnpj + DvCnpjClassico(baseCnpj).ToString("00", CultureInfo.InvariantCulture);
    }

    public static string ChaveValida(Random sorteio, bool emitenteAlfanumerico)
    {
        var primeiros43 = Digitos(sorteio, 6) + CnpjValido(sorteio, emitenteAlfanumerico) + Digitos(sorteio, 23);
        return primeiros43 + (char)('0' + DvChaveClassico(primeiros43));
    }

    public static string Digitos(Random sorteio, int quantidade) =>
        new(Enumerable.Range(0, quantidade).Select(_ => (char)('0' + sorteio.Next(10))).ToArray());

    public static string MascararCpf(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    public static string MascararCnpj(string cnpj) => $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

    // Referências clássicas, escritas à parte da implementação: dígitos inteiros e, no CNPJ e na chave, o valor
    // ASCII − 48 de cada caractere (num dígito, é o próprio dígito; letras valem de 17 a 42).
    public static int DvCpfClassico(string baseCpf)
    {
        var primeiro = Modulo11(baseCpf.Select((c, i) => (c - '0') * (10 - i)).Sum());
        var segundo = Modulo11(baseCpf.Select((c, i) => (c - '0') * (11 - i)).Sum() + (primeiro * 2));
        return (primeiro * 10) + segundo;
    }

    public static int DvCnpjClassico(string baseCnpj)
    {
        int[] pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var primeiro = Modulo11(baseCnpj.Select((c, i) => Valor(c) * pesos1[i]).Sum());
        var segundo = Modulo11(baseCnpj.Select((c, i) => Valor(c) * pesos2[i]).Sum() + (primeiro * pesos2[12]));
        return (primeiro * 10) + segundo;
    }

    public static int DvChaveClassico(string primeiros43)
    {
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += Valor(primeiros43[i]) * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        return Modulo11(soma);
    }

    public static int Valor(char caractere) => caractere - 48;

    public static int Modulo11(int soma) => soma % 11 < 2 ? 0 : 11 - (soma % 11);
}
