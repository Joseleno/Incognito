using System.Globalization;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Documentos sorteados para os testes e referências clássicas do DV, escritas à parte da implementação. Nenhum
/// identificador fica escrito no código: tudo é sorteado na hora, com a semente de cada teste.
/// </summary>
internal static class DocumentosDeTeste
{
    public const string Alfanumerico = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

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
