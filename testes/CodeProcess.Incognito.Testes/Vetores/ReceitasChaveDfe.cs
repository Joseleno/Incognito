using System.Globalization;
using static CodeProcess.Incognito.Testes.Vetores.ReceitaDePar;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Receita do <c>br.chave-dfe@1</c>: os estratos da classificação do emitente (modelos, séries, épocas do AAMM, tpEmis,
/// posição 30 da NFF, letra no emitente, regra dos zeros), as partes da NFF, o número e o código com as caminhadas, as
/// formas e as políticas. Cada chave sai do <see cref="GeradorChaveDfe"/> pela especificação na regra.
/// </summary>
internal static partial class ReceitasDeVetores
{
    private const string ChaveZerada = "00000000000000000000000000000000000000000000";
    private static readonly int[] _modelosDaChave = [55, 65, 57, 58, 67, 64, 63, 66, 62];

    private static ReceitaDeArquivo ChaveDfe()
    {
        ReceitaDePar[] padrao =
        [
            .. PorModelo("modelo", "aamm=2405,serie=1,tpemis=1,emitente=CnpjNumerico", 2),
            .. PorModelo("cnpj-alfanumerico", "aamm=2607,serie=1,tpemis=1,emitente=CnpjAlfanumerico", 1),
            Chave("cnpj-ordem-alfanumerica", "aamm=2607,modelo=55,serie=1,tpemis=1,emitente=CnpjNumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("cnpj-ordem-alfanumerica", "aamm=2607,modelo=57,serie=1,tpemis=1,emitente=CnpjNumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("cnpj-raiz-e-ordem-alfanumericas", "aamm=2607,modelo=58,serie=1,tpemis=1,emitente=CnpjAlfanumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("cnpj-filial", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjFilial").E(Pseudonimizado),
            Chave("cnpj-filial", "aamm=2405,modelo=63,serie=1,tpemis=1,emitente=CnpjFilial").E(Pseudonimizado),

            // NF-e: séries, PAA, épocas e NFF.
            .. PorSerie("serie-cnpj", 55, "CnpjNumerico", [0, 889, 890, 899, 900, 909], Pseudonimizado),
            .. PorSerie("serie-cpf", 55, "Cpf", [910, 919, 920, 969], Pseudonimizado),
            .. PorSerie("serie-paa-cpf", 55, "Cpf", [970, 979], Pseudonimizado),
            .. PorSerie("serie-paa-cnpj", 55, "CnpjNumerico", [980, 989], Pseudonimizado),
            .. PorSerie("serie-sem-emissao", 55, "CnpjNumerico", [990, 999], Malformado),
            .. PorSerie("serie-cnpj", 65, "CnpjNumerico", [909], Pseudonimizado),
            .. PorSerie("serie-cpf", 65, "Cpf", [910, 969], Pseudonimizado),
            .. PorSerie("serie-sem-emissao", 65, "Cpf", [970, 979], Malformado),
            .. PorSerie("serie-sem-emissao", 65, "CnpjNumerico", [980, 989, 990, 999], Malformado),
            Chave("cpf-sem-zeros", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=CnpjNumerico").E(Malformado),
            Chave("cpf-sem-zeros", "aamm=2405,modelo=55,serie=975,tpemis=1,emitente=CnpjNumerico").E(Malformado),
            Chave("cpf-sem-zeros", "aamm=2405,modelo=65,serie=920,tpemis=1,emitente=CnpjNumerico").E(Malformado),
            Chave("cpf-sem-zeros", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=CnpjNumerico", "trocar:6:1", "trocar:7:0", "trocar:8:0", "recalcular-cdv").E(Malformado),
            Chave("aamm-antes-do-cpf", "aamm=1807,modelo=55,serie=920,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("aamm-antes-do-cpf", "aamm=1807,modelo=65,serie=930,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("aamm-primeiro-com-cpf", "aamm=1808,modelo=55,serie=920,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("scan", "aamm=1407,modelo=55,serie=950,tpemis=3,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("tpemis3-fora-do-periodo", "aamm=1808,modelo=55,serie=1,tpemis=3,emitente=CnpjNumerico").E(Malformado),
            Chave("tpemis3-fora-do-periodo", "aamm=2103,modelo=65,serie=1,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Malformado),
            Chave("tpemis3-paa", "aamm=2405,modelo=55,serie=975,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("tpemis3-paa", "aamm=2405,modelo=55,serie=985,tpemis=3,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("tpemis3-paa", "aamm=2405,modelo=65,serie=975,tpemis=3,emitente=Cpf").E(Malformado),
            Chave("tpemis3-paa", "aamm=2405,modelo=55,serie=970,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("tpemis3-paa", "aamm=2405,modelo=55,serie=989,tpemis=3,emitente=CnpjNumerico").E(Pseudonimizado),
            // A série da NFF é 0AA ou 1AA; as séries 990 e 969 com tpEmis 3 prendem só a precedência das regras.
            Chave("tpemis3-fora-do-paa", "aamm=2405,modelo=55,serie=990,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("tpemis3-fora-do-paa", "aamm=2405,modelo=55,serie=969,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("outros-tpemis", "aamm=2405,modelo=55,serie=1,tpemis=6,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("outros-tpemis", "aamm=2405,modelo=55,serie=920,tpemis=9,emitente=Cpf").E(Pseudonimizado),
            Chave("outros-tpemis", "aamm=2405,modelo=65,serie=1,tpemis=9,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("nff-cnpj", "aamm=2104,modelo=55,serie=21,tpemis=3,emitente=CnpjNumerico,numero=Nff,nff=1").E(Pseudonimizado),
            Chave("nff-cpf", "aamm=2104,modelo=55,serie=121,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("nff-cpf", "aamm=2405,modelo=65,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("nff-sem-tipo", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=3").E(Malformado),
            Chave("nff-sem-tipo", "aamm=2405,modelo=65,serie=24,tpemis=3,emitente=CnpjNumerico,numero=Nff,nff=0").E(Malformado),
            Chave("nff-cpf-sem-zeros", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=CnpjNumerico,numero=Nff,nff=2").E(Malformado),
            Chave("nff-cnpj-com-zeros-do-cpf", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=CpfComZeros,numero=Nff,nff=1,dv=7").E(Pseudonimizado),
            Chave("nff-ano-do-aamm", "aamm=2402,modelo=55,serie=25,tpemis=3,emitente=Cpf,numero=Nff,nff=2,dia=29").E(Pseudonimizado),
            Chave("nff-ano-do-aamm", "aamm=2502,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2,dia=29").E(Pseudonimizado),

            // Letra no emitente decide CNPJ antes da série, da época e da posição 30.
            Chave("letra-vence-serie", "aamm=2607,modelo=55,serie=920,tpemis=1,emitente=CnpjAlfanumerico").E(Pseudonimizado),
            Chave("letra-vence-serie", "aamm=2607,modelo=55,serie=995,tpemis=1,emitente=CnpjAlfanumerico").E(Pseudonimizado),
            Chave("letra-vence-serie", "aamm=2607,modelo=65,serie=975,tpemis=1,emitente=CnpjNumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("letra-vence-tpemis3", "aamm=1905,modelo=55,serie=1,tpemis=3,emitente=CnpjAlfanumerico").E(Pseudonimizado),
            Chave("letra-vence-nff", "aamm=2607,modelo=55,serie=26,tpemis=3,emitente=CnpjAlfanumerico,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("letra-vence-nff", "aamm=2607,modelo=65,serie=126,tpemis=3,emitente=CnpjAlfanumericoComOrdemAlfanumerica,numero=Nff,nff=7").E(Pseudonimizado),

            // Emitentes especiais. Os que, lidos como CNPJ numérico, têm raiz 000xxxxx saem com o DV deslocado.
            Chave("cpf-com-zeros", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=CpfComZeros,dv=7").E(Pseudonimizado),
            Chave("cpf-com-cinco-zeros", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=CpfComCincoZeros").E(Pseudonimizado),
            Chave("cpf-repdigito", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=CpfRepdigito").E(Pseudonimizado),
            Caminhada("caminhada-cpf", "cpf", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("cnpj-raiz-zero", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjRaizZero").E(Pseudonimizado),
            Chave("cnpj-raiz-000", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjRaizComZeros,dv=11").E(Pseudonimizado),
            .. SoCnpjComRaiz000(),
            Chave("dv-emitente-invalido", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,dv=37").E(Pseudonimizado),
            Chave("dv-emitente-invalido", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf,dv=5").E(Pseudonimizado),

            // CT-e e MDF-e: regra dos zeros e NFF a partir de 2006.
            Chave("zeros-cpf", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").X(2).E(Pseudonimizado),
            Chave("zeros-cpf", "aamm=2405,modelo=58,serie=920,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("zeros-cnpj", "aamm=2405,modelo=57,serie=975,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("zeros-cnpj", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjFilial").E(Pseudonimizado),
            Chave("zeros-raiz-000-e-cpf", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjRaizComZeros,dv=13").E(Pseudonimizado),
            Chave("zeros-raiz-000-com-letra", "aamm=2607,modelo=58,serie=1,tpemis=1,emitente=CnpjRaizComZerosEOrdemAlfanumerica,dv=19").E(Pseudonimizado),
            Chave("zeros-alfanumerico-000", "aamm=2607,modelo=57,serie=1,tpemis=1,emitente=CnpjAlfanumericoComZeros,dv=23").E(Pseudonimizado),
            Chave("zeros-raiz-zero", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjRaizZero").E(Pseudonimizado),
            Chave("zeros-quase-cpf", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:6:1", "trocar:7:0", "trocar:8:0", "recalcular-cdv").E(Pseudonimizado),
            Inversa("saida-raiz-00", "chave-dfe-saida-raiz-00:aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico,dv=29").E(Pseudonimizado),
            Inversa("saida-raiz-00", "chave-dfe-saida-raiz-00:aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjNumerico,dv=29").E(Pseudonimizado),
            Chave("zeros-cpf-repdigito", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CpfRepdigito").E(Pseudonimizado),
            Caminhada("caminhada-raiz-000", "raiz-000", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico").X(2).E(Pseudonimizado),
            Caminhada("caminhada-raiz-000", "raiz-000", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("transporte-antes-da-nff", "aamm=2005,modelo=57,serie=20,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("transporte-antes-da-nff", "aamm=2005,modelo=58,serie=20,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2006,modelo=57,serie=20,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2006,modelo=58,serie=20,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=58,serie=124,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=57,serie=24,tpemis=3,emitente=CnpjNumerico,numero=Nff").E(Pseudonimizado),
            Chave("paa-transporte", "aamm=2405,modelo=57,serie=975,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("paa-transporte", "aamm=2405,modelo=58,serie=985,tpemis=3,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("paa-transporte", "aamm=2405,modelo=57,serie=970,tpemis=3,emitente=Cpf").E(Pseudonimizado),
            Chave("paa-transporte", "aamm=2405,modelo=58,serie=989,tpemis=3,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=57,serie=990,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=58,serie=969,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),

            // Partes da NFF: dias que existem nos dois meses, datas inválidas e sequencial.
            .. UltimoDia(55, "Cpf", "nff=2"),
            .. UltimoDia(57, "Cpf", null),
            Chave("nff-dia-zero", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2,dia=0").E(Pseudonimizado),
            Chave("nff-dia-zero", "aamm=2405,modelo=58,serie=24,tpemis=3,emitente=Cpf,numero=Nff,dia=0").E(Pseudonimizado),
            Chave("nff-mes-do-numero-invalido", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2,mes=0").E(Pseudonimizado),
            Chave("nff-mes-do-numero-invalido", "aamm=2405,modelo=57,serie=24,tpemis=3,emitente=Cpf,numero=Nff,mes=13").E(Pseudonimizado),
            Chave("nff-mes-do-aamm-invalido", "aamm=2500,modelo=55,serie=25,tpemis=3,emitente=Cpf,numero=Nff,nff=2,mes=1,dia=10").E(Pseudonimizado),
            Chave("nff-mes-do-aamm-invalido", "aamm=2513,modelo=58,serie=25,tpemis=3,emitente=Cpf,numero=Nff,mes=1,dia=10").E(Pseudonimizado),
            Chave("nff-mes-diferente", "aamm=2401,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2,mes=2,dia=29").E(Pseudonimizado),
            Chave("nff-mes-diferente", "aamm=2401,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2,mes=2,dia=30").E(Pseudonimizado),
            Chave("nff-mes-diferente", "aamm=2402,modelo=57,serie=24,tpemis=3,emitente=Cpf,numero=Nff,mes=1,dia=30").E(Pseudonimizado),
            Chave("nff-sequencial-zero", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=NffSequencialZero,nff=2").E(Pseudonimizado),
            Chave("nff-sequencial-zero", "aamm=2405,modelo=57,serie=24,tpemis=3,emitente=Cpf,numero=NffSequencialZero").E(Pseudonimizado),
            Chave("nff-numero-zero", "aamm=2405,modelo=57,serie=24,tpemis=3,emitente=Cpf,numero=Zero").E(Pseudonimizado),
            Chave("nff-numero-zero", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Zero").E(Malformado),
            Caminhada("caminhada-dia", "dia", "aamm=2402,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2").X(2).E(Pseudonimizado),
            Caminhada("caminhada-dia", "dia", "aamm=2404,modelo=57,serie=24,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Caminhada("caminhada-sequencial", "sequencial", "aamm=2405,modelo=65,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Caminhada("caminhada-sequencial", "sequencial", "aamm=2405,modelo=58,serie=24,tpemis=3,emitente=CnpjNumerico,numero=Nff").E(Pseudonimizado),

            // Número e código.
            Chave("numero-zero", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,numero=Zero").E(Pseudonimizado),
            Chave("numero-zero", "aamm=2405,modelo=67,serie=1,tpemis=1,emitente=CnpjNumerico,numero=Zero").E(Pseudonimizado),
            Caminhada("caminhada-numero-zero", "numero-zero", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Caminhada("caminhada-numero-zero", "numero-zero", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("codigo-proibido", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,codigo=Proibido").X(2).E(Pseudonimizado),
            Chave("codigo-proibido", "aamm=2405,modelo=63,serie=1,tpemis=1,emitente=CnpjNumerico,codigo=Proibido").E(Pseudonimizado),
            Chave("codigo-proibido-com-zero", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,codigo-fixo=1234567").E(Pseudonimizado),
            .. TodosOsProibidos(),
            Chave("codigo-proibido-antes-do-igual", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,numero=Zero,codigo-fixo=0").E(Pseudonimizado),
            Construido("codigo-igual-ao-numero", "chave-dfe-codigo-igual-ao-numero:aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Construido("codigo-igual-ao-numero", "chave-dfe-codigo-igual-ao-numero:aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("codigo-igual-ao-numero-de-entrada", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,codigo=IgualAoNumero").E(Pseudonimizado),
            Caminhada("caminhada-codigo-proibido", "codigo-proibido", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").X(2).E(Pseudonimizado),
            Caminhada("caminhada-codigo-numero", "codigo-numero", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("numero-pseudonimizado-acima-de-10-8", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").Onde(NumeroDaSaida(acima: true)).E(Pseudonimizado),
            Chave("numero-pseudonimizado-abaixo-de-10-8", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").Onde(NumeroDaSaida(acima: false)).E(Pseudonimizado),
            Chave("nsiteautoriz", "aamm=2405,modelo=66,serie=1,tpemis=1,emitente=CnpjNumerico,site=3").E(Pseudonimizado),
            Chave("nsiteautoriz", "aamm=2405,modelo=62,serie=1,tpemis=1,emitente=CnpjNumerico,site=9").E(Pseudonimizado),
            Chave("codigo-7-com-cara-de-proibido", "aamm=2405,modelo=66,serie=1,tpemis=1,emitente=CnpjNumerico,codigo-fixo=1111111").E(Pseudonimizado),
            Chave("codigo-7-com-cara-de-proibido", "aamm=2405,modelo=62,serie=1,tpemis=1,emitente=CnpjNumerico,codigo-fixo=0").E(Pseudonimizado),
            Chave("ponto-fixo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjRaizZero,numero=Zero,codigo=Proibido").E(PontoFixo),
            Chave("ponto-fixo", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CpfRepdigito,numero=Zero,codigo=Proibido,cdv=4").E(PontoFixo),
            Chave("cdv-errado", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,cdv=3").E(DvInvalidoPreservado),
            Chave("cdv-errado", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf,cdv=9").E(DvInvalidoPreservado),

            // Formas.
            Chave("prefixo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:NFe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=65,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:NFe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf", "prefixo:CTe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=64,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:CTe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=67,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:CTe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:MDFe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=63,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:BPe").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=66,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:NF3e").E(Pseudonimizado),
            Chave("prefixo", "aamm=2405,modelo=62,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:NFCom").E(Pseudonimizado),
            Chave("prefixo-em-outra-caixa", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:nfe").E(Pseudonimizado),
            Chave("prefixo-em-outra-caixa", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=Cpf", "prefixo:MDFE").E(Pseudonimizado),
            Chave("prefixo-em-outra-caixa", "aamm=2405,modelo=62,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:nfCOM").E(Pseudonimizado),
            Chave("prefixo-de-outro-modelo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:CTe").E(Malformado),
            Chave("prefixo-de-outro-modelo", "aamm=2405,modelo=67,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:MDFe").E(Malformado),
            Chave("prefixo-desconhecido", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:NFC").E(Malformado),
            Chave("prefixo-desconhecido", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo:Id").E(Malformado),
            Chave("grupos", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "grupos").E(Pseudonimizado),
            Chave("grupos", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf", "grupos").E(Pseudonimizado),
            Chave("grupos", "aamm=2607,modelo=66,serie=1,tpemis=1,emitente=CnpjAlfanumerico", "grupos", "espacos:2").E(Pseudonimizado),
            Chave("preenchimento", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "espacos:3").E(Pseudonimizado),
            Chave("preenchimento", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=Cpf", "prefixo:MDFe", "espacos:1").E(Pseudonimizado),
            Chave("grupos-com-outro-separador", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "grupos", "trocar:4:-").E(Malformado),
            Chave("grupos-com-espaco-duplo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "grupos", "inserir:4: ").E(Malformado),
            Chave("grupos-com-prefixo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "grupos", "prefixo:NFe").E(Malformado),
            Chave("espaco-a-esquerda", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "prefixo: ").E(Malformado),
            Chave("tamanho", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "remover:43:1").E(Malformado),
            Chave("tamanho", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "sufixo:0").E(Malformado),
            Chave("tamanho", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "sufixo:1234567890").E(Malformado),
            Chave("tamanho", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "sufixo:12345678901").E(Malformado),
            Chave("minuscula-no-emitente", "aamm=2607,modelo=55,serie=1,tpemis=1,emitente=CnpjAlfanumerico", "minusculas-ascii").E(Malformado),
            Chave("letra-fora-do-emitente", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:30:A").E(Malformado),
            Chave("letra-fora-do-emitente", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:19:B").E(Malformado),
            Chave("letra-fora-do-emitente", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:18:B").E(Malformado),
            Chave("letra-fora-do-emitente", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:5:B").E(Malformado),
            Chave("modelo-fora-da-tabela", "aamm=2405,modelo=59,serie=1,tpemis=1,emitente=CnpjNumerico").E(Malformado),
            Chave("modelo-fora-da-tabela", "aamm=2405,modelo=0,serie=1,tpemis=1,emitente=CnpjNumerico").E(Malformado),
            Construido("chave-da-nfse", "nfse-50").E(Malformado),
            Construido("chave-da-nfse", "nfse-50", "prefixo:NFS").E(Malformado),
            DeLiteral("zerada", ChaveZerada).E(Zerado),
            DeLiteral("zerada", "NFe" + ChaveZerada).E(Zerado),
            DeLiteral("zerada", "MDFe" + ChaveZerada + "  ").E(Zerado),
            DeLiteral("zerada", string.Join(' ', Enumerable.Repeat("0000", 11))).E(Zerado),
            DeLiteral("zerada-com-prefixo-desconhecido", "Id" + ChaveZerada).E(Malformado),
            DeLiteral("vazio", "").E(Vazio),
            DeLiteral("vazio", "   ").E(Vazio),
        ];

        ReceitaDePar[] anulando =
        [
            .. PorModelo("modelo", "aamm=2405,serie=1,tpemis=1,emitente=CnpjNumerico", 1),
            Chave("zeros-cnpj", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("cnpj-ordem-alfanumerica", "aamm=2607,modelo=57,serie=1,tpemis=1,emitente=CnpjNumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("cnpj-ordem-alfanumerica", "aamm=2607,modelo=55,serie=1,tpemis=1,emitente=CnpjNumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("cnpj-raiz-e-ordem-alfanumericas", "aamm=2607,modelo=58,serie=1,tpemis=1,emitente=CnpjAlfanumericoComOrdemAlfanumerica").E(Pseudonimizado),
            Chave("zeros-raiz-000-com-letra", "aamm=2607,modelo=57,serie=1,tpemis=1,emitente=CnpjRaizComZerosEOrdemAlfanumerica,dv=19").E(Pseudonimizado),
            Caminhada("caminhada-raiz-000", "raiz-000", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico").X(2).E(Pseudonimizado),
            Caminhada("caminhada-raiz-000", "raiz-000", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Caminhada("caminhada-cpf", "cpf", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Inversa("saida-raiz-00", "chave-dfe-saida-raiz-00:aamm=2405,modelo=57,serie=1,tpemis=1,emitente=CnpjNumerico,dv=29").E(Pseudonimizado),
            Chave("cnpj-raiz-zero", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjRaizZero").E(Pseudonimizado),
            Chave("nff-cpf", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=57,serie=24,tpemis=3,emitente=CnpjNumerico,numero=Nff").E(Pseudonimizado),
            Chave("cdv-errado", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,cdv=2").E(AnuladoDvInvalido),
            Chave("serie-sem-emissao", "aamm=2405,modelo=55,serie=995,tpemis=1,emitente=CnpjNumerico").E(AnuladoMalformado),
            Chave("prefixo-de-outro-modelo", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf", "prefixo:NFe").E(AnuladoMalformado),
            DeLiteral("zerada", ChaveZerada).E(Zerado),
        ];

        ReceitaDePar[] preservando =
        [
            Chave("modelo", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("modelo", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("cdv-errado", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,cdv=1").E(DvInvalido),
            Chave("serie-sem-emissao", "aamm=2405,modelo=55,serie=990,tpemis=1,emitente=CnpjNumerico").E(CopiadoEmClaro),
            Chave("serie-sem-emissao", "aamm=2405,modelo=65,serie=975,tpemis=1,emitente=Cpf").E(CopiadoEmClaro),
            Chave("anomalia-com-cdv-errado", "aamm=2405,modelo=55,serie=990,tpemis=1,emitente=CnpjNumerico,cdv=3").E(CopiadoEmClaro),
            Chave("anomalia-com-prefixo-de-outro-modelo", "aamm=2405,modelo=55,serie=990,tpemis=1,emitente=CnpjNumerico", "prefixo:CTe").E(CopiadoEmClaro),
            Chave("modelo-fora-da-coluna", "aamm=2405,modelo=63,serie=1,tpemis=1,emitente=CnpjNumerico").E(CopiadoEmClaro),
            Chave("modelo-fora-da-coluna", "aamm=2405,modelo=66,serie=1,tpemis=1,emitente=CnpjNumerico").E(CopiadoEmClaro),
            Chave("modelo-fora-da-coluna-com-cdv-errado", "aamm=2405,modelo=63,serie=1,tpemis=1,emitente=CnpjNumerico,cdv=3").E(CopiadoEmClaro),
            Chave("prefixo-de-outro-modelo-com-cdv-errado", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,cdv=3", "prefixo:CTe").E(CopiadoEmClaro),
            Chave("tamanho", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "sufixo:0").E(CopiadoEmClaro),
            Chave("modelo-fora-da-tabela", "aamm=2405,modelo=59,serie=1,tpemis=1,emitente=CnpjNumerico").E(CopiadoEmClaro),
            Chave("letra-fora-do-emitente", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico", "trocar:30:A").E(CopiadoEmClaro),
            DeLiteral("zerada", "CTe" + ChaveZerada).E(Zerado),
        ];

        ReceitaDePar[] soAlgunsModelos =
        [
            Chave("modelo", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").E(Pseudonimizado),
            Chave("modelo", "aamm=2405,modelo=58,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("modelo", "aamm=2405,modelo=66,serie=1,tpemis=1,emitente=CnpjNumerico").E(Pseudonimizado),
            Chave("modelo-fora-da-coluna", "aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico").E(AnuladoMalformado),
            Chave("anomalia-com-modelo-fora-da-coluna", "aamm=2405,modelo=55,serie=995,tpemis=1,emitente=CnpjNumerico").E(AnuladoMalformado),
            DeLiteral("zerada", "NFe" + ChaveZerada).E(Zerado),
        ];

        ReceitaDePar[] outroAmbiente =
        [
            .. PorModelo("modelo", "aamm=2405,serie=1,tpemis=1,emitente=CnpjNumerico", 2),
            Chave("serie-cpf", "aamm=2405,modelo=55,serie=920,tpemis=1,emitente=Cpf").X(2).E(Pseudonimizado),
            Chave("zeros-cpf", "aamm=2405,modelo=57,serie=1,tpemis=1,emitente=Cpf").X(2).E(Pseudonimizado),
            Chave("nff-cpf", "aamm=2405,modelo=55,serie=24,tpemis=3,emitente=Cpf,numero=Nff,nff=2").E(Pseudonimizado),
            Chave("nff-transporte", "aamm=2405,modelo=58,serie=24,tpemis=3,emitente=Cpf,numero=Nff").E(Pseudonimizado),
            Chave("cnpj-alfanumerico", "aamm=2607,modelo=55,serie=1,tpemis=1,emitente=CnpjAlfanumerico").E(Pseudonimizado),
            Chave("serie-sem-emissao", "aamm=2405,modelo=55,serie=999,tpemis=1,emitente=CnpjNumerico").E(Malformado),
        ];

        return new("br.chave-dfe@1.json", "br.chave-dfe", 1, Descricao("br.chave-dfe", 1),
        [
            new("componentes padrão, malformado falha", ParametrosDaChave("homologacao", _modelosDaChave, "preservar", "falhar", "cpf", "cnpj", false), padrao),
            new("componentes e ambiente próprios, ordem permutada, anulando", ParametrosDaChave("desenvolvimento", _modelosDaChave, "anular", "anular", "cpf-cliente", "cnpj-cliente", true), anulando),
            new("malformado preservado, cDV errado falha", ParametrosDaChave("homologacao", [55, 65, 57, 58], "falhar", "preservar", "cpf", "cnpj", false), preservando),
            new("só alguns modelos, malformado anulado", ParametrosDaChave("producao", [57, 58, 66], "preservar", "anular", "cpf", "cnpj", false), soAlgunsModelos),
            new("componentes padrão em outro ambiente", ParametrosDaChave("producao", _modelosDaChave, "preservar", "falhar", "cpf", "cnpj", false), outroAmbiente),
        ]);
    }

    private static KeyValuePair<string, object>[] ParametrosDaChave(
        string ambiente, int[] modelos, string invalida, string malformada, string dominioCpf, string dominioCnpj, bool permutarOrdem) =>
    [
        new("dominio", "dfe-numero"),
        new("ambiente", ambiente),
        new("modelos", modelos.Select(m => (long)m).ToArray()),
        new("entradaInvalida", invalida),
        new("entradaMalformada", malformada),
        new("componentes", (KeyValuePair<string, object>[])
        [
            new("cpf", (KeyValuePair<string, object>[])[new("dominio", dominioCpf), new("versao", 1L)]),
            new("cnpj", (KeyValuePair<string, object>[])[new("dominio", dominioCnpj), new("versao", 1L), new("permutarOrdem", permutarOrdem)]),
        ]),
    ];

    private static ReceitaDePar Chave(string ramo, string especificacao, params string[] formas) =>
        Construido(ramo, "chave-dfe:" + especificacao, formas);

    private static ReceitaDePar Caminhada(string ramo, string parte, string especificacao) =>
        Inversa(ramo, $"chave-dfe-caminhada-{parte}:{especificacao}");

    private static IEnumerable<ReceitaDePar> PorModelo(string ramo, string especificacao, int vezes)
    {
        foreach (var modelo in _modelosDaChave)
        {
            yield return Chave(ramo, $"modelo={modelo},{especificacao}").X(vezes).E(Pseudonimizado);
        }
    }

    // Os 20 códigos proibidos, cada um num par: a lista inteira fica presa.
    private static IEnumerable<ReceitaDePar> TodosOsProibidos()
    {
        foreach (var codigo in GeradorChaveDfe.CodigosProibidos)
        {
            yield return Chave("codigo-proibido-cada", $"aamm=2405,modelo=55,serie=1,tpemis=1,emitente=CnpjNumerico,codigo-fixo={codigo}").E(Pseudonimizado);
        }
    }

    // Nos modelos que só admitem CNPJ, a raiz 000xxxxx é CNPJ (com o DV deslocado: raízes tão baixas são cadastradas).
    private static IEnumerable<ReceitaDePar> SoCnpjComRaiz000()
    {
        foreach (var modelo in new[] { 67, 64, 63, 66, 62 })
        {
            yield return Chave("so-cnpj-raiz-000", $"aamm=2405,modelo={modelo},serie=1,tpemis=1,emitente=CnpjRaizComZeros,dv=17").E(Pseudonimizado);
        }
    }

    private static IEnumerable<ReceitaDePar> PorSerie(string ramo, int modelo, string emitente, int[] series, string esperado)
    {
        foreach (var serie in series)
        {
            yield return Chave(ramo, $"aamm=2405,modelo={modelo},serie={serie},tpemis=1,emitente={emitente}").E(esperado);
        }
    }

    // O último dia e o dia seguinte nos meses de 28, 29, 30 e 31 dias.
    private static IEnumerable<ReceitaDePar> UltimoDia(int modelo, string emitente, string? tipoDoEmissor)
    {
        foreach (var (aamm, dias) in new[] { (2502, 28), (2402, 29), (2404, 30), (2401, 31) })
        {
            var especificacao = $"aamm={aamm},modelo={modelo},serie={aamm / 100},tpemis=3,emitente={emitente},numero=Nff"
                + (tipoDoEmissor is null ? "" : "," + tipoDoEmissor);
            yield return Chave("nff-ultimo-dia", $"{especificacao},dia={dias}").E(Pseudonimizado);
            yield return Chave("nff-dia-depois-do-ultimo", $"{especificacao},dia={dias + 1}").E(Pseudonimizado);
        }
    }

    private static Func<object, ResultadoDoPar, bool> NumeroDaSaida(bool acima) =>
        (_, resultado) => resultado.Saida is string saida
            && long.TryParse(saida.AsSpan(25, 9), NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            && numero >= 100_000_000 == acima;
}
