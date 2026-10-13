using System.Globalization;
using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Testes.Vetores.ReceitaDePar;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Receitas dos vetores fixos: para cada transformador e versão, os casos (domínio, ambiente, opções e políticas) e os
/// ramos que cada um precisa cobrir. O gerador transforma as receitas em arquivos; depois de gravado, um arquivo só
/// cresce (casos novos), nunca muda.
/// </summary>
internal static partial class ReceitasDeVetores
{
    private const string Pseudonimizado = "Pseudonimizado";
    private const string DvInvalidoPreservado = "DvInvalidoPreservado";
    private const string PontoFixo = "PontoFixo";
    private const string AnuladoDvInvalido = "AnuladoDvInvalido";
    private const string AnuladoMalformado = "AnuladoMalformado";
    private const string CopiadoEmClaro = "CopiadoEmClaro";
    private const string ServicoPreservado = "ServicoPreservado";
    private const string Vazio = "Vazio";
    private const string Zerado = "Zerado";
    private const string Malformado = "INC1003";
    private const string DvInvalido = "INC1004";

    // Todos os caracteres de char.IsWhiteSpace, que o Trim das pontas tira no e-mail e na entidade do CEP.
    private const string EspacosDoNet =
        "\t\n\u000B\u000C\r \u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";

    internal static IReadOnlyList<ReceitaDeArquivo> Todas { get; } =
    [
        Cpf(),
        Cnpj(),
        Telefone(),
        Email(),
        Cep(),
        Identificador(),
        ChavePix(),
        ChaveDfe(),
    ];

    private static string Descricao(string transformador, int versao) => transformador == "br.chave-dfe"
        ? DescricaoDaChaveDfe(versao)
        : $"{transformador}, versão {versao}. Chave pública de teste: nunca use fora dos testes. Contrato de compatibilidade: um "
        + $"par existente que mudar exige {transformador}@{versao + 1}; casos novos só se acrescentam. Cada entrada vem do "
        + "GeradorBr com a semente registrada, de uma regra de construção sobre a mesma sequência ou da permutação inversa "
        + "com esta chave, ou é uma borda literal sem documento válido (origem em cada par). Nenhuma foi copiada de um "
        + "cadastro, mas um documento válido gerado pode coincidir com o de alguém: não use estes valores fora dos testes."
        + (transformador is "br.telefone" or "br.chave-pix"
            ? " Os números de serviço (0800, 0300, 0303, 0500 e 0900 com 7 dígitos sorteados, ou 6 no formato antigo; DDD "
                + "inexistente com 3003 ou 4004 e 4 sorteados) podem coincidir com números de empresas: são necessários para "
                + "provar a preservação e nunca são de pessoa."
            : "");

    private static string DescricaoDaChaveDfe(int versao) =>
        $"br.chave-dfe, versão {versao}. Chave pública de teste: nunca use fora dos testes. Contrato de compatibilidade: um "
        + $"par existente que mudar exige br.chave-dfe@{versao + 1}; casos novos só se acrescentam. Cada chave de entrada sai "
        + "do gerador detalhado de chaves de DF-e do projeto de testes (regra chave-dfe com a especificação e a semente), de "
        + "uma regra de construção sobre ela ou da permutação inversa com esta chave, ou é uma borda literal: a chave de 44 "
        + "zeros (valor de preenchimento, com cDV válido, de ninguém) ou texto vazio; a de 50 posições da NFS-e é sorteada. "
        + "Nenhuma foi copiada de um documento autorizado, mas os emitentes (CPF e CNPJ válidos gerados) podem coincidir com "
        + "o de alguém: não use estes valores fora dos testes. Nenhum par, de entrada ou de saída, traz um emitente de raiz "
        + "numérica começando com 00, ordem de estabelecimento e DV certo: nas entradas, esses emitentes saem com o DV "
        + "deslocado.";

    private static KeyValuePair<string, object>[] Parametros(string dominio, string ambiente, params (string Nome, object Valor)[] outros) =>
        [new("dominio", dominio), new("ambiente", ambiente), .. outros.Select(p => new KeyValuePair<string, object>(p.Nome, p.Valor))];

    private static ReceitaDeArquivo Cpf()
    {
        ReceitaDePar[] comuns =
        [
            Gerador("pseudonimizado", "Cpf()").X(30).E(Pseudonimizado),
            Gerador("mascara", "Cpf(formatado)").X(6).E(Pseudonimizado),
            Gerador("dv-invalido", "Cpf()", "dv:1").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cpf()", "dv:37").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cpf()", "dv:99").E(DvInvalidoPreservado),
            Gerador("mascara-dv-invalido", "Cpf(formatado)", "dv:50").E(DvInvalidoPreservado),
            Gerador("sem-zeros-1", "Cpf()", "sem-zeros:1").X(3).E(Pseudonimizado),
            Gerador("sem-zeros-2", "Cpf()", "sem-zeros:2").X(3).E(Pseudonimizado),
            Gerador("sem-zeros-dv-invalido", "Cpf()", "dv:7", "sem-zeros:1").E(DvInvalidoPreservado),
            Gerador("preenchimento", "Cpf()", "espacos:3").X(2).E(Pseudonimizado),
            Gerador("mascara-preenchimento", "Cpf(formatado)", "espacos:2").E(Pseudonimizado),
            Gerador("sem-zeros-preenchimento-menor", "Cpf()", "sem-zeros:2", "espacos:1").E(Pseudonimizado),
            Gerador("sem-zeros-preenchimento-igual", "Cpf()", "sem-zeros:2", "espacos:2").E(Pseudonimizado),
            Gerador("sem-zeros-preenchimento-maior", "Cpf()", "sem-zeros:1", "espacos:4").E(Pseudonimizado),
            Construido("repdigito", "cpf-repdigito").X(4).E(PontoFixo),
            Construido("repdigito-mascara", "cpf-repdigito", "mascara-cpf").E(PontoFixo),
            Construido("repdigito-dv-invalido", "cpf-repdigito", "dv:11").X(2).E(PontoFixo),
            Construido("repdigito-sem-zeros", "cpf-repdigito", "sem-zeros:2").E(PontoFixo),
            Inversa("caminhada", "cpf-caminhada").X(3).E(Pseudonimizado),
            DeLiteral("vazio", "").E(Vazio),
            DeLiteral("vazio", "   ").E(Vazio),
            Gerador("malformado-tamanho", "Cpf()", "sufixo:0").E(Malformado),
            Gerador("malformado-tamanho", "Cpf()", "sem-zeros:3").E(Malformado),
            Gerador("malformado-letra", "Cpf()", "trocar:4:A").E(Malformado),
            Gerador("digito-nao-ascii", "Cpf()", "trocar:4:\u0663").E(Malformado),
            Gerador("digito-nao-ascii", "Cpf()", "trocar:0:\uFF10").E(Malformado),
            Gerador("sufixo-nao-espaco", "Cpf()", "sufixo:\t").E(Malformado),
            Gerador("sufixo-nao-espaco", "Cpf()", "sufixo:\u00A0").E(Malformado),
            Gerador("sufixo-nao-espaco", "Cpf()", "sufixo:\u3000").E(Malformado),
            Gerador("espaco-a-esquerda", "Cpf()", "prefixo: ").E(Malformado),
            Gerador("mascara-errada", "Cpf(formatado)", "trocar:3:,").E(Malformado),
            Gerador("mascara-errada", "Cpf(formatado)", "trocar:11:.").E(Malformado),
            Gerador("mascara-errada", "Cpf(formatado)", "remover:3:1").E(Malformado),
            Gerador("numero", "Cpf()", "numero").X(6).E(Pseudonimizado),
            Gerador("numero-com-zeros", "Cpf()", "sem-zeros:1", "numero").X(2).E(Pseudonimizado),
            Gerador("numero-com-zeros", "Cpf()", "sem-zeros:3", "numero").E(Pseudonimizado),
            Gerador("numero-dv-invalido", "Cpf()", "dv:5", "numero").E(DvInvalidoPreservado),
            Construido("numero-repdigito", "cpf-repdigito", "numero").X(2).E(PontoFixo),
            DeLiteral("numero-fora-da-faixa", -1L).E(Malformado),
            DeLiteral("numero-fora-da-faixa", long.MinValue).E(Malformado),
            DeLiteral("numero-fora-da-faixa", 100_000_000_000L).E(Malformado),
            DeLiteral("zerado", "00000000000").E(PontoFixo),
            DeLiteral("zerado", "000.000.000-00").E(PontoFixo),
            DeLiteral("numero-zerado", 0L).E(PontoFixo),
            Construido("numero-maximo", "cpf-repdigito", "numero").Onde((entrada, _) => entrada is 99_999_999_999L).E(PontoFixo),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("pseudonimizado", "Cpf()").X(8).E(Pseudonimizado),
            Gerador("mascara", "Cpf(formatado)").X(2).E(Pseudonimizado),
            Gerador("sem-zeros-1", "Cpf()", "sem-zeros:1").E(Pseudonimizado),
            Gerador("dv-invalido", "Cpf()", "dv:23").X(3).E(AnuladoDvInvalido),
            Gerador("dv-deslocado-em-1", "Cpf()", "dv:1").E(AnuladoDvInvalido),
            Construido("repdigito", "cpf-repdigito").E(PontoFixo),
            Construido("repdigito-dv-invalido", "cpf-repdigito", "dv:3").E(AnuladoDvInvalido),
            Gerador("malformado-letra", "Cpf()", "trocar:4:A").E(AnuladoMalformado),
            DeLiteral("vazio", "  ").E(Vazio),
            Gerador("numero", "Cpf()", "numero").X(2).E(Pseudonimizado),
            Gerador("numero-dv-invalido", "Cpf()", "dv:9", "numero").E(AnuladoDvInvalido),
            DeLiteral("numero-fora-da-faixa", -1L).E(AnuladoMalformado),
        ];

        ReceitaDePar[] falhando =
        [
            Gerador("pseudonimizado", "Cpf()").X(5).E(Pseudonimizado),
            Gerador("dv-invalido", "Cpf()", "dv:41").X(2).E(DvInvalido),
            Gerador("dv-deslocado-em-1", "Cpf()", "dv:1").E(DvInvalido),
            Construido("repdigito", "cpf-repdigito").E(PontoFixo),
            Construido("repdigito-dv-invalido", "cpf-repdigito", "dv:60").E(DvInvalido),
            Gerador("malformado-letra", "Cpf()", "trocar:4:A").E(CopiadoEmClaro),
            Gerador("sufixo-nao-espaco", "Cpf()", "sufixo:\t").E(CopiadoEmClaro),
            Gerador("malformado-tamanho", "Cpf()", "sufixo:0").E(CopiadoEmClaro),
            DeLiteral("numero-fora-da-faixa", 100_000_000_000L).E(CopiadoEmClaro),
            Gerador("numero-dv-invalido", "Cpf()", "dv:2", "numero").E(DvInvalido),
        ];

        return new("br.cpf@1.json", "br.cpf", 1, Descricao("br.cpf", 1),
        [
            new("texto e número, DV preservado, malformado falha",
                Parametros("cpf", "homologacao", ("entradaInvalida", "preservar"), ("entradaMalformada", "falhar")), comuns),
            new("domínio e ambiente próprios, DV e malformado anulados",
                Parametros("cpf-cliente", "desenvolvimento", ("entradaInvalida", "anular"), ("entradaMalformada", "anular")), anulando),
            new("DV falha, malformado preservado",
                Parametros("cpf", "homologacao", ("entradaInvalida", "falhar"), ("entradaMalformada", "preservar")), falhando),
            new("domínio padrão em outro ambiente",
                Parametros("cpf", "producao", ("entradaInvalida", "preservar"), ("entradaMalformada", "falhar")),
            [
                Gerador("pseudonimizado", "Cpf()").X(5).E(Pseudonimizado),
                Gerador("mascara", "Cpf(formatado)").E(Pseudonimizado),
                Gerador("dv-invalido", "Cpf()", "dv:12").E(DvInvalidoPreservado),
                Gerador("malformado-letra", "Cpf()", "trocar:4:A").E(Malformado),
            ]),
        ]);
    }

    private static ReceitaDeArquivo Cnpj()
    {
        ReceitaDePar[] semOrdem =
        [
            Gerador("raiz-numerica", "Cnpj()").X(15).E(Pseudonimizado),
            Gerador("raiz-alfanumerica", "Cnpj(alfanumerico)").X(15).E(Pseudonimizado),
            Gerador("mascara", "Cnpj(formatado)").X(3).E(Pseudonimizado),
            Gerador("mascara", "Cnpj(alfanumerico, formatado)").X(3).E(Pseudonimizado),
            Gerador("dv-invalido", "Cnpj()", "dv:1").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cnpj()", "dv:73").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cnpj(alfanumerico)", "dv:99").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cnpj(alfanumerico, formatado)", "dv:44").E(DvInvalidoPreservado),
            Gerador("sem-zeros-1", "Cnpj()", "sem-zeros:1").X(2).E(Pseudonimizado),
            Gerador("sem-zeros-2", "Cnpj()", "sem-zeros:2").X(2).E(Pseudonimizado),
            Gerador("preenchimento", "Cnpj()", "espacos:2").E(Pseudonimizado),
            Gerador("sem-zeros-preenchimento-menor", "Cnpj()", "sem-zeros:2", "espacos:1").E(Pseudonimizado),
            Gerador("sem-zeros-preenchimento-maior", "Cnpj()", "sem-zeros:1", "espacos:3").E(Pseudonimizado),
            Construido("raiz-zero", "cnpj-raiz-zero").X(3).E(PontoFixo),
            Construido("raiz-zero", "cnpj-raiz-zero", "mascara-cnpj").E(PontoFixo),
            Construido("raiz-zero-ordem-alfanumerica", "cnpj-raiz-zero-ordem-alfa").X(2).E(PontoFixo),
            Construido("ordem-alfanumerica", "cnpj-ordem-alfa").X(3).E(Pseudonimizado),
            Construido("raiz-quase-zero", "cnpj-raiz-quase-zero").X(2).E(DvInvalidoPreservado),
            Inversa("alvo-so-com-digitos-e-a", "cnpj-alvo-digitos-e-a").X(2).E(Pseudonimizado),
            Inversa("caminhada-num", "cnpj-caminhada-num").E(Pseudonimizado),
            Inversa("caminhada-alfa", "cnpj-caminhada-alfa").X(3).E(Pseudonimizado),
            DeLiteral("vazio", "    ").E(Vazio),
            Gerador("minuscula", "Cnpj(alfanumerico)", "minusculas-ascii").E(Malformado),
            Gerador("letra-no-dv", "Cnpj()", "trocar:12:A").E(Malformado),
            Gerador("sem-zeros-alfanumerico", "Cnpj(alfanumerico)", "sem-zeros:1").E(Malformado),
            Gerador("malformado-tamanho", "Cnpj()", "sufixo:0").E(Malformado),
            Gerador("malformado-tamanho", "Cnpj()", "sem-zeros:3").E(Malformado),
            Gerador("digito-nao-ascii", "Cnpj()", "trocar:3:\u0663").E(Malformado),
            Gerador("letra-nao-ascii", "Cnpj(alfanumerico)", "trocar:0:\u212A").E(Malformado),
            Gerador("sufixo-nao-espaco", "Cnpj()", "sufixo:\t").E(Malformado),
            Gerador("mascara-errada", "Cnpj(formatado)", "trocar:10:-").E(Malformado),
            Gerador("numero", "Cnpj()", "numero").X(5).E(Pseudonimizado),
            Gerador("numero-com-zeros", "Cnpj()", "sem-zeros:1", "numero").E(Pseudonimizado),
            Gerador("numero-dv-invalido", "Cnpj()", "dv:8", "numero").E(DvInvalidoPreservado),
            Construido("numero-raiz-zero", "cnpj-raiz-zero", "numero").E(PontoFixo),
            DeLiteral("numero-fora-da-faixa", -1L).E(Malformado),
            DeLiteral("numero-fora-da-faixa", 100_000_000_000_000L).E(Malformado),
            DeLiteral("numero-maximo", 99_999_999_999_999L).E(DvInvalidoPreservado),
            DeLiteral("zerado", "00000000000000").E(PontoFixo),
            DeLiteral("zerado", "00.000.000/0000-00").E(PontoFixo),
            DeLiteral("numero-zerado", 0L).E(PontoFixo),
        ];

        ReceitaDePar[] comOrdem =
        [
            Gerador("raiz-numerica", "Cnpj()").X(10).E(Pseudonimizado),
            Gerador("raiz-alfanumerica", "Cnpj(alfanumerico)").X(10).E(Pseudonimizado),
            Construido("ordem-alfanumerica", "cnpj-ordem-alfa").X(4).E(Pseudonimizado),
            Construido("raiz-quase-zero", "cnpj-raiz-quase-zero").X(3).E(DvInvalidoPreservado),
            Construido("raiz-quase-zero", "cnpj-raiz-quase-zero-letra").X(2).E(DvInvalidoPreservado),
            Inversa("alvo-so-com-digitos-e-a", "cnpj-alvo-digitos-e-a").X(2).E(Pseudonimizado),
            Gerador("mascara", "Cnpj(alfanumerico, formatado)").X(2).E(Pseudonimizado),
            Gerador("dv-invalido", "Cnpj()", "dv:17").E(DvInvalidoPreservado),
            Gerador("dv-invalido", "Cnpj(alfanumerico)", "dv:52").X(2).E(DvInvalidoPreservado),
            Gerador("sem-zeros-1", "Cnpj()", "sem-zeros:1").E(Pseudonimizado),
            Construido("raiz-zero", "cnpj-raiz-zero").X(2).E(PontoFixo),
            Construido("raiz-zero-ordem-alfanumerica", "cnpj-raiz-zero-ordem-alfa").X(2).E(PontoFixo),
            Inversa("caminhada-num", "cnpj-caminhada-num").X(3).E(Pseudonimizado),
            Inversa("caminhada-alfa", "cnpj-caminhada-alfa").X(3).E(Pseudonimizado),
            Inversa("caminhada-alfa-raiz-zero", "cnpj-caminhada-alfa-raiz-zero").X(2).E(Pseudonimizado),
            Gerador("numero", "Cnpj()", "numero").X(3).E(Pseudonimizado),
            Construido("numero-raiz-zero", "cnpj-raiz-zero", "numero").E(PontoFixo),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("raiz-numerica", "Cnpj()").X(5).E(Pseudonimizado),
            Gerador("raiz-alfanumerica", "Cnpj(alfanumerico)").X(5).E(Pseudonimizado),
            Gerador("dv-invalido", "Cnpj()", "dv:29").X(2).E(AnuladoDvInvalido),
            Gerador("dv-deslocado-em-1", "Cnpj()", "dv:1").E(AnuladoDvInvalido),
            Construido("raiz-zero", "cnpj-raiz-zero").E(AnuladoDvInvalido),
            Gerador("minuscula", "Cnpj(alfanumerico)", "minusculas-ascii").E(AnuladoMalformado),
            Gerador("numero", "Cnpj()", "numero").X(2).E(Pseudonimizado),
            DeLiteral("numero-fora-da-faixa", -1L).E(AnuladoMalformado),
        ];

        ReceitaDePar[] falhando =
        [
            Gerador("raiz-numerica", "Cnpj()").X(3).E(Pseudonimizado),
            Gerador("raiz-alfanumerica", "Cnpj(alfanumerico)").X(3).E(Pseudonimizado),
            Gerador("dv-invalido", "Cnpj()", "dv:31").X(2).E(DvInvalido),
            Gerador("dv-deslocado-em-1", "Cnpj()", "dv:1").E(DvInvalido),
            Construido("raiz-zero", "cnpj-raiz-zero").E(DvInvalido),
            Gerador("minuscula", "Cnpj(alfanumerico)", "minusculas-ascii").E(CopiadoEmClaro),
            DeLiteral("numero-fora-da-faixa", -1L).E(CopiadoEmClaro),
        ];

        return new("br.cnpj@1.json", "br.cnpj", 1, Descricao("br.cnpj", 1),
        [
            new("raiz permutada, DV preservado, malformado falha",
                Parametros("cnpj", "homologacao", ("permutarOrdem", false), ("entradaInvalida", "preservar"), ("entradaMalformada", "falhar")), semOrdem),
            new("raiz e ordem permutadas",
                Parametros("cnpj", "homologacao", ("permutarOrdem", true), ("entradaInvalida", "preservar"), ("entradaMalformada", "falhar")), comOrdem),
            new("domínio e ambiente próprios, DV e malformado anulados",
                Parametros("cnpj-fornecedor", "desenvolvimento", ("permutarOrdem", false), ("entradaInvalida", "anular"), ("entradaMalformada", "anular")), anulando),
            new("raiz e ordem permutadas, DV falha, malformado preservado",
                Parametros("cnpj", "homologacao", ("permutarOrdem", true), ("entradaInvalida", "falhar"), ("entradaMalformada", "preservar")), falhando),
            new("domínio padrão em outro ambiente",
                Parametros("cnpj", "producao", ("permutarOrdem", false), ("entradaInvalida", "preservar"), ("entradaMalformada", "falhar")),
            [
                Gerador("raiz-numerica", "Cnpj()").X(3).E(Pseudonimizado),
                Gerador("raiz-alfanumerica", "Cnpj(alfanumerico)").X(3).E(Pseudonimizado),
                Gerador("dv-invalido", "Cnpj()", "dv:21").E(DvInvalidoPreservado),
                Gerador("minuscula", "Cnpj(alfanumerico)", "minusculas-ascii").E(Malformado),
            ]),
        ]);
    }

    private static ReceitaDeArquivo Telefone()
    {
        ReceitaDePar[] falhando =
        [
            Gerador("celular", "Telefone(celular)").X(10).E(Pseudonimizado),
            Gerador("fixo", "Telefone(fixo)").X(10).E(Pseudonimizado),
            Gerador("celular-com-pais", "Telefone(celular, comPais)").X(3).E(Pseudonimizado),
            Gerador("fixo-com-pais", "Telefone(fixo, comPais)").X(3).E(Pseudonimizado),
            Gerador("celular-formatado", "Telefone(celular, formatado)").X(3).E(Pseudonimizado),
            Gerador("fixo-formatado", "Telefone(fixo, formatado)").X(3).E(Pseudonimizado),
            Gerador("com-pais-formatado", "Telefone(celular, comPais, formatado)").X(2).E(Pseudonimizado),
            Gerador("com-pais-formatado", "Telefone(fixo, comPais, formatado)").X(2).E(Pseudonimizado),
            Gerador("pais-sem-mais", "Telefone(celular)", "prefixo:55").X(2).E(Pseudonimizado),
            Gerador("tronco", "Telefone(fixo)", "prefixo:0").X(2).E(Pseudonimizado),
            Gerador("tronco", "Telefone(celular)", "prefixo:0").X(2).E(Pseudonimizado),
            Gerador("tronco-parenteses", "Telefone(fixo, formatado)", "trocar:0:(0").E(Pseudonimizado),
            Gerador("sem-ddd", "Telefone(celular)", "remover:0:2").X(3).E(Pseudonimizado),
            Gerador("sem-ddd", "Telefone(fixo)", "remover:0:2").X(3).E(Pseudonimizado),
            Gerador("sem-ddd", "Telefone(celular, formatado)", "remover:0:5").E(Pseudonimizado),
            Gerador("grupos-1-4-4", "Telefone(celular, formatado)", "trocar:5:9 ").Onde((e, _) => ((string)e)[6] == ' ').E(Pseudonimizado),
            Gerador("grupos-com-ponto", "Telefone(fixo, formatado)", "trocar:9:.").E(Pseudonimizado),
            Gerador("grupos-com-espaco", "Telefone(fixo, formatado)", "trocar:9: ").E(Pseudonimizado),
            Gerador("ddd-separado", "Telefone(celular)", "inserir:2: ").E(Pseudonimizado),
            Gerador("ddd-separado", "Telefone(fixo)", "inserir:2:-").E(Pseudonimizado),
            Gerador("pais-separado", "Telefone(celular, comPais)", "inserir:3:.").E(Pseudonimizado),
            Construido("celular-antigo", "telefone-celular-antigo").X(4).E(Pseudonimizado),
            Construido("celular-antigo", "telefone-celular-antigo", "inserir:2: ", "inserir:7:-").E(Pseudonimizado),
            Construido("celular-antigo-sem-ddd", "telefone-celular-antigo", "remover:0:2").X(2).E(Pseudonimizado),
            Construido("ddd-55", "telefone-celular-ddd:55").X(2).E(Pseudonimizado),
            Construido("ddd-55", "telefone-celular-ddd:55", "prefixo:+55").E(Pseudonimizado),
            Construido("ddd-55", "telefone-fixo-ddd:55", "prefixo:55").E(Pseudonimizado),
            Construido("ddd-inexistente", "telefone-celular-ddd:23").E(Pseudonimizado),
            Construido("ddd-extremo", "telefone-celular-ddd:11").E(Pseudonimizado),
            Construido("ddd-extremo", "telefone-fixo-ddd:99").E(Pseudonimizado),
            Construido("servico-nao-geografico", "telefone-servico-nao-geografico").X(4).E(ServicoPreservado),
            Construido("servico-nao-geografico", "telefone-servico-nao-geografico", "prefixo:+55").X(2).E(ServicoPreservado),
            Construido("servico-nao-geografico", "telefone-servico-nao-geografico", "prefixo:55").E(ServicoPreservado),
            Construido("servico-nao-geografico", "telefone-servico-nao-geografico", "inserir:4: ", "inserir:8: ").E(ServicoPreservado),
            Construido("servico-0303", "telefone-servico-nao-geografico:303").X(2).E(ServicoPreservado),
            Construido("servico-0303", "telefone-servico-nao-geografico:303", "prefixo:+55 ", "inserir:8: ").E(ServicoPreservado),
            Construido("servico-antigo", "telefone-servico-antigo").X(4).E(ServicoPreservado),
            Construido("servico-antigo", "telefone-servico-antigo:303").E(ServicoPreservado),
            Construido("servico-antigo", "telefone-servico-antigo", "prefixo:+55").E(ServicoPreservado),
            Construido("servico-antigo", "telefone-servico-antigo", "prefixo:55").E(ServicoPreservado),
            Construido("servico-antigo", "telefone-servico-antigo", "inserir:4: ", "inserir:8:-").E(ServicoPreservado),
            Construido("servico-fixo", "telefone-servico-fixo").X(4).E(ServicoPreservado),
            Construido("servico-fixo", "telefone-servico-fixo", "remover:0:2").X(2).E(ServicoPreservado),
            Construido("servico-fixo", "telefone-servico-fixo", "inserir:2: ", "inserir:7:-").E(ServicoPreservado),
            Construido("borda-servico", "telefone-fixo-numero:30029999").E(Pseudonimizado),
            Construido("borda-servico", "telefone-fixo-numero:30030000").E(ServicoPreservado),
            Construido("borda-servico", "telefone-fixo-numero:30039999").E(ServicoPreservado),
            Construido("borda-servico", "telefone-fixo-numero:30040000").E(Pseudonimizado),
            Construido("borda-servico", "telefone-fixo-numero:40039999").E(Pseudonimizado),
            Construido("borda-servico", "telefone-fixo-numero:40040000").E(ServicoPreservado),
            Construido("borda-servico", "telefone-fixo-numero:40049999").E(ServicoPreservado),
            Construido("borda-servico", "telefone-fixo-numero:40050000").E(Pseudonimizado),
            Inversa("caminhada-fixo", "telefone-caminhada-fixo").X(3).E(Pseudonimizado),
            DeLiteral("vazio", "").E(Vazio),
            DeLiteral("zerado", "0").E(Zerado),
            DeLiteral("zerado", "00000000").E(Zerado),
            DeLiteral("zerado", "0000000000000").E(Zerado),
            DeLiteral("zerado", "00000000   ").E(Zerado),
            DeLiteral("zerado-formatado", "(11) 0000-0000").E(Zerado),
            DeLiteral("zerado-formatado", "+55 (00) 0000-0000").E(Zerado),
            DeLiteral("zerado-formatado", "(21) 00000-0000").E(Zerado),
            DeLiteral("zerado-formatado", "+0 (0)--0..0").E(Zerado),
            DeLiteral("zerado-assinante", "1100000000").E(Zerado),
            DeLiteral("zerado-assinante", "11000000000").E(Zerado),
            DeLiteral("zerado-assinante", "02100000000").E(Zerado),
            DeLiteral("zerado-assinante", "021000000000").E(Zerado),
            DeLiteral("zerado-assinante", "551100000000").E(Zerado),
            DeLiteral("zerado-assinante", "5511000000000").E(Zerado),
            DeLiteral("zerado-tamanho-maximo", "0" + new string(' ', 30) + "0").E(Zerado),
            DeLiteral("zerado-acima-do-maximo", "0" + new string(' ', 31) + "0").E(Malformado),
            DeLiteral("zerado-acima-de-13-digitos", "00000000000000").E(Malformado),
            DeLiteral("quase-zerado", "1100000001").E(Malformado),
            DeLiteral("quase-zerado", "541100000000").E(Malformado),
            DeLiteral("quase-zerado", "5411000000000").E(Malformado),
            DeLiteral("quase-zerado", "0/0").E(Malformado),
            DeLiteral("quase-zerado", "1110000000").E(Malformado),
            DeLiteral("quase-zerado", "11100000000").E(Malformado),
            DeLiteral("quase-zerado", "121000000000").E(Malformado),
            DeLiteral("sem-digitos", "()-").E(Malformado),
            Construido("ddd-com-zero", "telefone-celular-ddd:30").E(Malformado),
            Construido("servico-com-mais", "telefone-servico-nao-geografico", "prefixo:+").E(Malformado),
            Gerador("celular-sem-9", "Telefone(celular)", "trocar:2:8").E(Malformado),
            Gerador("malformado-tamanho", "Telefone(celular, comPais)", "sufixo:1").E(Malformado),
            Gerador("malformado-tamanho", "Telefone(fixo)", "remover:0:3").E(Malformado),
            Gerador("mais-sem-55", "Telefone(celular)", "prefixo:+").E(Malformado),
            Gerador("separador-no-ddd", "Telefone(celular)", "inserir:1: ").E(Malformado),
            Gerador("parentese-sem-par", "Telefone(fixo, formatado)", "remover:3:1").E(Malformado),
            Gerador("grupo-fora-do-lugar", "Telefone(fixo)", "inserir:5:-").E(Malformado),
            Gerador("grupo-fora-do-lugar", "Telefone(celular)", "inserir:3: ").E(Malformado),
            Gerador("tamanho-maximo", "Telefone(celular, comPais, formatado)", "inserir:3:" + new string(' ', 13)).E(Pseudonimizado),
            Gerador("tamanho-acima-do-maximo", "Telefone(celular, comPais, formatado)", "inserir:3:" + new string(' ', 14)).E(Malformado),
            Gerador("malformado-mais-no-meio", "Telefone(celular)", "prefixo:55+").E(Malformado),
            Gerador("malformado-treze-digitos-sem-55", "Telefone(celular)", "prefixo:54").E(Malformado),
            Gerador("malformado-doze-digitos-sem-55-nem-0", "Telefone(fixo)", "prefixo:54").E(Malformado),
            Gerador("malformado-ddd-com-zero", "Telefone(fixo)", "trocar:0:0").E(Malformado),
            Gerador("malformado-ddd-com-zero", "Telefone(celular)", "trocar:1:0").E(Malformado),
            Gerador("malformado-fixo-comecando-com-1", "Telefone(fixo)", "trocar:2:1").E(Malformado),
            Gerador("malformado-fixo-comecando-com-0", "Telefone(fixo)", "trocar:2:0", "inserir:2: ").E(Malformado),
            Gerador("malformado-tab-no-meio", "Telefone(celular)", "inserir:2:\t").E(Malformado),
            Gerador("malformado-barra", "Telefone(celular)", "inserir:2:/").E(Malformado),
            Gerador("malformado-espaco-a-esquerda", "Telefone(celular)", "prefixo: ").E(Malformado),
            Gerador("malformado-separador-dentro-do-55", "Telefone(celular)", "prefixo:5 5 ").E(Malformado),
            Gerador("malformado-separador-entre-o-0-e-o-ddd", "Telefone(fixo)", "prefixo:0 ", "inserir:4: ").E(Malformado),
            Gerador("malformado-parenteses-em-volta-do-numero", "Telefone(celular)", "inserir:2: (", "sufixo:)").E(Malformado),
            Gerador("malformado-parenteses-em-volta-do-55", "Telefone(celular)", "prefixo:(55) ").E(Malformado),
            Gerador("malformado-dois-pares-de-parenteses", "Telefone(celular, formatado)", "prefixo:(", "inserir:4:)").E(Malformado),
            Gerador("malformado-ramal", "Telefone(fixo)", "remover:0:2", "inserir:4:-", "sufixo: 12").E(Malformado),
            Gerador("malformado-ramal", "Telefone(fixo, formatado)", "sufixo: 12").E(Malformado),
            Gerador("malformado-ramal", "Telefone(fixo, formatado)", "sufixo: r. 12").E(Malformado),
            Gerador("malformado-celular-de-7-digitos", "Telefone(fixo, formatado)", "remover:5:1").E(Malformado),
            Gerador("malformado-fixo-em-grupos-de-3-e-5", "Telefone(fixo)", "inserir:2: ", "inserir:6:-").E(Malformado),
            Gerador("malformado-celular-em-grupos-de-1-e-8", "Telefone(celular)", "inserir:2: ", "inserir:4: ").E(Malformado),
            Gerador("malformado-celular-em-grupos-de-4-e-5", "Telefone(celular)", "inserir:2: ", "inserir:7:-").E(Malformado),
            Gerador("malformado-dois-separadores-dentro-do-numero", "Telefone(celular)", "inserir:2: ", "inserir:8:--").E(Malformado),
            Gerador("malformado-hifen-no-fim", "Telefone(celular)", "sufixo:-").E(Malformado),
            Gerador("malformado-parentese-abrindo-antes-do-55", "Telefone(celular)", "prefixo:(55 ", "inserir:6:) ").E(Malformado),
            Gerador("malformado-espaco-antes-de-fechar-o-parentese", "Telefone(celular, formatado)", "inserir:3: ").E(Malformado),
            Gerador("malformado-parentese-de-abrir-repetido", "Telefone(celular, formatado)", "inserir:5:(").E(Malformado),
            Gerador("malformado-separador-dentro-do-ddd-com-o-0", "Telefone(fixo)", "prefixo:0", "inserir:2: ", "inserir:4: ").E(Malformado),
            Gerador("malformado-zero-xx", "Telefone(celular)", "prefixo:0xx").E(Malformado),
            Gerador("malformado-zero-xx", "Telefone(fixo, formatado)", "trocar:0:(0xx").E(Malformado),
            Construido("malformado-servico-com-mais", "telefone-servico-fixo", "remover:0:2", "inserir:4:-", "prefixo:+").E(Malformado),
            Construido("malformado-parece-0800-sem-zero", "telefone-servico-nao-geografico", "remover:0:1").E(Malformado),
            Construido("malformado-servico-antigo-com-mais", "telefone-servico-antigo", "prefixo:+").E(Malformado),
            Construido("malformado-servico-com-5-digitos", "telefone-servico-antigo", "remover:4:1").E(Malformado),
            Gerador("malformado-letra", "Telefone(fixo)", "trocar:5:x").E(Malformado),
            Gerador("digito-nao-ascii", "Telefone(fixo)", "trocar:5:\u0663").E(Malformado),
            Gerador("sufixo-nao-espaco", "Telefone(fixo)", "sufixo:\t").E(Malformado),
            Gerador("numero", "Telefone(celular)", "numero").X(3).E(Pseudonimizado),
            Gerador("numero", "Telefone(fixo)", "numero").X(3).E(Pseudonimizado),
            Gerador("numero-com-pais", "Telefone(celular, comPais)", "remover:0:1", "numero").X(2).E(Pseudonimizado),
            Gerador("numero-sem-ddd", "Telefone(fixo)", "remover:0:2", "numero").X(2).E(Pseudonimizado),
            Construido("numero-celular-antigo", "telefone-celular-antigo", "numero").E(Pseudonimizado),
            Construido("numero-servico", "telefone-servico-nao-geografico", "sem-zeros:1", "numero").X(2).E(ServicoPreservado),
            Construido("numero-servico", "telefone-servico-fixo", "numero").E(ServicoPreservado),
            Construido("numero-servico-0303", "telefone-servico-nao-geografico:303", "sem-zeros:1", "numero").E(ServicoPreservado),
            Construido("numero-servico-antigo", "telefone-servico-antigo:800", "sem-zeros:1", "numero").E(ServicoPreservado),
            Construido("numero-servico-antigo", "telefone-servico-antigo:300", "sem-zeros:1", "numero").E(ServicoPreservado),
            Construido("numero-servico-antigo", "telefone-servico-antigo:303", "sem-zeros:1", "numero").E(ServicoPreservado),
            Construido("numero-servico-antigo", "telefone-servico-antigo:500", "sem-zeros:1", "numero").E(ServicoPreservado),
            Construido("numero-900-antigo-e-celular", "telefone-servico-antigo:900", "sem-zeros:1", "numero").E(Pseudonimizado),
            DeLiteral("numero-zerado", 0L).E(Zerado),
            DeLiteral("numero-zerado", 1_100_000_000L).E(Zerado),
            DeLiteral("numero-zerado", 11_000_000_000L).E(Zerado),
            DeLiteral("numero-zerado", 551_100_000_000L).E(Zerado),
            DeLiteral("numero-zerado", 5_511_000_000_000L).E(Zerado),
            DeLiteral("numero-quase-zerado", 1_100_000_001L).E(Malformado),
            DeLiteral("numero-fora-da-forma", 1L).E(Malformado),
            DeLiteral("numero-fora-da-forma", -1L).E(Malformado),
            DeLiteral("numero-fora-da-forma", 99_999_999_999_999L).E(Malformado),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("celular", "Telefone(celular)").X(3).E(Pseudonimizado),
            Gerador("fixo", "Telefone(fixo)").X(3).E(Pseudonimizado),
            Construido("servico-fixo", "telefone-servico-fixo").E(ServicoPreservado),
            Gerador("malformado-letra", "Telefone(fixo)", "trocar:5:x").E(AnuladoMalformado),
            DeLiteral("numero-fora-da-forma", 7L).E(AnuladoMalformado),
            DeLiteral("zerado-formatado", "(11) 0000-0000").E(Zerado),
            DeLiteral("numero-zerado", 0L).E(Zerado),
        ];

        ReceitaDePar[] preservando =
        [
            Gerador("celular", "Telefone(celular, comPais, formatado)").X(2).E(Pseudonimizado),
            Gerador("malformado-letra", "Telefone(fixo)", "trocar:5:x").E(CopiadoEmClaro),
            DeLiteral("numero-fora-da-forma", -1L).E(CopiadoEmClaro),
            DeLiteral("zerado-formatado", "+55 (00) 0000-0000").E(Zerado),
        ];

        return new("br.telefone@1.json", "br.telefone", 1, Descricao("br.telefone", 1),
        [
            new("malformado falha", Parametros("telefone", "homologacao", ("entradaMalformada", "falhar")), falhando),
            new("domínio e ambiente próprios, malformado anulado", Parametros("telefone-loja", "desenvolvimento", ("entradaMalformada", "anular")), anulando),
            new("malformado preservado", Parametros("telefone", "homologacao", ("entradaMalformada", "preservar")), preservando),
            new("domínio padrão em outro ambiente", Parametros("telefone", "producao", ("entradaMalformada", "falhar")),
            [
                Gerador("celular", "Telefone(celular)").X(3).E(Pseudonimizado),
                Gerador("fixo", "Telefone(fixo, formatado)").X(3).E(Pseudonimizado),
                Gerador("celular-com-pais", "Telefone(celular, comPais)").E(Pseudonimizado),
                Gerador("malformado-letra", "Telefone(fixo)", "trocar:5:x").E(Malformado),
            ]),
        ]);
    }

    private static ReceitaDeArquivo Email()
    {
        var dominioLongo = "sufixo:." + new string('a', 177);
        ReceitaDePar[] falhando =
        [
            Gerador("pseudonimizado", "Email()").X(20).E(Pseudonimizado),
            Gerador("maiusculas", "Email()", "maiusculas-ascii").X(3).E(Pseudonimizado),
            Gerador("latin1", "Email()", "prefixo:\u00C0\u00C9\u00DE").E(Pseudonimizado),
            Gerador("latin1", "Email()", "prefixo:\u00E0\u00E9\u00FE").E(Pseudonimizado),
            Gerador("fora-da-regra", "Email()", "prefixo:\u00D7").E(Pseudonimizado),
            Gerador("fora-da-regra", "Email()", "prefixo:\u00DF").E(Pseudonimizado),
            Gerador("fora-da-regra", "Email()", "prefixo:\u0178").E(Pseudonimizado),
            Gerador("fora-da-regra", "Email()", "prefixo:\u0130").E(Pseudonimizado),
            Gerador("fora-da-regra", "Email()", "prefixo:\u212A").E(Pseudonimizado),
            Gerador("composicao", "Email()", "prefixo:\u00E9").E(Pseudonimizado),
            Gerador("composicao", "Email()", "prefixo:e\u0301").E(Pseudonimizado),
            Gerador("par-substituto", "Email()", "prefixo:\uD83D\uDE00").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "prefixo:  ").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "prefixo:\u00A0").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "prefixo:\u0085").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "sufixo:\t").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "sufixo:\u3000").E(Pseudonimizado),
            Gerador("espacos-nas-pontas", "Email()", "prefixo:" + EspacosDoNet, "sufixo:" + EspacosDoNet).E(Pseudonimizado),
            Gerador("preenchimento-menor-que-excesso", "Email()", "espacos:2").E(Pseudonimizado),
            Gerador("preenchimento-maior-que-excesso", "Email()", "espacos:40").E(Pseudonimizado),
            Gerador("espacos-a-esquerda-e-preenchimento", "Email()", "prefixo:   ", "espacos:10").E(Pseudonimizado),
            Construido("preenchimento-sem-excesso", "email-parte-local:40", "espacos:3").E(Pseudonimizado),
            Construido("parte-local-64", "email-parte-local:64").E(Pseudonimizado),
            Construido("parte-local-64", "email-parte-local:62", "prefixo:\uD83D\uDE00").E(Pseudonimizado),
            Construido("endereco-254", "email-parte-local:64", dominioLongo).E(Pseudonimizado),
            DeLiteral("vazio", "").E(Vazio),
            DeLiteral("vazio", "     ").E(Vazio),
            Construido("parte-local-65", "email-parte-local:65").E(Malformado),
            Construido("parte-local-65", "email-parte-local:63", "prefixo:\uD83D\uDE00").E(Malformado),
            Construido("endereco-255", "email-parte-local:64", dominioLongo, "sufixo:a").E(Malformado),
            Gerador("controle", "Email()", "prefixo:\u001F").E(Malformado),
            Gerador("controle", "Email()", "inserir:3:\u007F").E(Malformado),
            Gerador("controle-c1", "Email()", "inserir:3:\u0080").E(Malformado),
            Gerador("controle-c1", "Email()", "inserir:3:\u009F").E(Malformado),
            Gerador("duas-arrobas", "Email()", "prefixo:a@").E(Malformado),
            Construido("sem-arroba", "email-parte-local:10", "remover:10:1").E(Malformado),
            Construido("parte-local-vazia", "email-parte-local:10", "remover:0:10").E(Malformado),
            Construido("dominio-sem-ponto", "email-parte-local:10", "remover:18:1").E(Malformado),
            Construido("rotulo-vazio", "email-parte-local:10", "inserir:11:.").E(Malformado),
            Construido("rotulo-vazio", "email-parte-local:10", "sufixo:.").E(Malformado),
            Construido("rotulo-vazio", "email-parte-local:10", "inserir:18:.").E(Malformado),
            Gerador("espaco-no-meio", "Email()", "inserir:3: ").E(Malformado),
            Gerador("espaco-no-meio", "Email()", "inserir:3:\u2009").E(Malformado),
            Construido("surrogate-sem-par", "email-parte-local:10", "prefixo-utf16:D800").E(Malformado),
            Construido("surrogate-sem-par", "email-parte-local:10", "inserir:5:x", "trocar-utf16:5:DC00").E(Malformado),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("pseudonimizado", "Email()").X(5).E(Pseudonimizado),
            Gerador("duas-arrobas", "Email()", "prefixo:a@").E(AnuladoMalformado),
            Construido("surrogate-sem-par", "email-parte-local:10", "prefixo-utf16:DBFF").E(AnuladoMalformado),
        ];

        ReceitaDePar[] preservando =
        [
            Gerador("pseudonimizado", "Email()").X(3).E(Pseudonimizado),
            Gerador("duas-arrobas", "Email()", "prefixo:a@").E(CopiadoEmClaro),
            Construido("dominio-sem-ponto", "email-parte-local:10", "remover:18:1").E(CopiadoEmClaro),
        ];

        return new("email@1.json", "email", 1, Descricao("email", 1),
        [
            new("malformado falha", Parametros("email", "homologacao", ("entradaMalformada", "falhar")), falhando),
            new("domínio e ambiente próprios, malformado anulado", Parametros("email-cliente", "desenvolvimento", ("entradaMalformada", "anular")), anulando),
            new("malformado preservado", Parametros("email", "homologacao", ("entradaMalformada", "preservar")), preservando),
            new("domínio padrão em outro ambiente", Parametros("email", "producao", ("entradaMalformada", "falhar")),
            [
                Gerador("pseudonimizado", "Email()").X(5).E(Pseudonimizado),
                Gerador("maiusculas", "Email()", "maiusculas-ascii").E(Pseudonimizado),
                Gerador("duas-arrobas", "Email()", "prefixo:a@").E(Malformado),
            ]),
        ]);
    }

    private static ReceitaDeArquivo Cep()
    {
        const string Pessoa = "pessoa-{n}";
        ReceitaDePar[] falhando =
        [
            Gerador("pseudonimizado", "Cep()").X(20).Com(Pessoa).E(Pseudonimizado),
            Gerador("mascara-hifen", "Cep(formatado)").X(3).Com(Pessoa).E(Pseudonimizado),
            Gerador("mascara-ponto", "Cep()", "mascara-cep-ponto").X(3).Com(Pessoa).E(Pseudonimizado),
            Gerador("sete-digitos", "Cep()", "sem-zeros:1").X(2).Com(Pessoa).E(Pseudonimizado),
            Gerador("sete-digitos-preenchimento", "Cep()", "sem-zeros:1", "espacos:1").Com(Pessoa).E(Pseudonimizado),
            Gerador("sete-digitos-preenchimento", "Cep()", "sem-zeros:1", "espacos:3").Com(Pessoa).E(Pseudonimizado),
            Gerador("preenchimento", "Cep(formatado)", "espacos:2").Com(Pessoa).E(Pseudonimizado),
            .. Fronteiras(Pessoa),
            .. TodasAsUfs(Pessoa),
            DeLiteral("primeira-faixa", "69050000").Com(Pessoa).Onde(SaidaEntre(69_000_000, 69_299_999)).E(Pseudonimizado),
            DeLiteral("primeira-faixa", "70100000").Com(Pessoa).Onde(SaidaEntre(70_000_000, 72_799_999)).E(Pseudonimizado),
            DeLiteral("primeira-faixa", "72850000").Com(Pessoa).Onde(SaidaEntre(72_800_000, 72_999_999)).E(Pseudonimizado),
            DeLiteral("segunda-faixa", "69050000").Com(Pessoa).Onde(SaidaEntre(69_400_000, 69_899_999)).E(Pseudonimizado),
            DeLiteral("segunda-faixa", "70100000").Com(Pessoa).Onde(SaidaEntre(73_000_000, 73_699_999)).E(Pseudonimizado),
            DeLiteral("segunda-faixa", "72850000").Com(Pessoa).Onde(SaidaEntre(73_700_000, 76_799_999)).E(Pseudonimizado),
            Gerador("mesma-entidade", "Cep(uf: MG)").X(3).Com("pessoa-mesma").E(Pseudonimizado),
            Gerador("entidade-vazia", "Cep(uf: AM)").X(2).Com("").E(Pseudonimizado),
            Gerador("entidade-vazia", "Cep(uf: GO)").Com("   ").E(Pseudonimizado),
            Gerador("entidade-aparada", "Cep(uf: DF)").Com("pessoa-aparada").E(Pseudonimizado),
            Gerador("entidade-aparada", "Cep(uf: DF)").Com("  pessoa-aparada\t").E(Pseudonimizado),
            Gerador("entidade-aparada", "Cep(uf: DF)").Com("\u00A0pessoa-aparada\u0085").E(Pseudonimizado),
            Gerador("entidade-aparada", "Cep(uf: DF)").Com("\u001Fpessoa-aparada").E(Pseudonimizado),
            Gerador("entidade-aparada", "Cep(uf: DF)").Com(EspacosDoNet + "pessoa-aparada" + EspacosDoNet).E(Pseudonimizado),
            Gerador("entidade-acentuada", "Cep(uf: MG)").Com("Pessoa Fict\u00EDcia A\u00E7\u00E3o").E(Pseudonimizado),
            Gerador("entidade-caixa", "Cep(uf: MG)").Com("PESSOA FICT\u00CDCIA A\u00C7\u00C3O").E(Pseudonimizado),
            Gerador("entidade-surrogate-sem-par", "Cep(uf: MG)").Com("utf16:0070D800").E(Pseudonimizado),
            Gerador("entidade-surrogate-sem-par", "Cep(uf: MG)").Com("p\uFFFD").E(Pseudonimizado),
            DeLiteral("vazio", "").Com(Pessoa).E(Vazio),
            DeLiteral("zerado", "00000000").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "0000000").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "00000").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "0").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "00000000  ").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "00000-000").Com(Pessoa).E(Zerado),
            DeLiteral("zerado", "00.000-000").Com(Pessoa).E(Zerado),
            DeLiteral("quase-zerado", "00000001").Com(Pessoa).E(Malformado),
            DeLiteral("quase-zerado", "000000000").Com(Pessoa).E(Malformado),
            DeLiteral("quase-zerado", "0000-0000").Com(Pessoa).E(Malformado),
            DeLiteral("quase-zerado", "00.000.000").Com(Pessoa).E(Malformado),
            DeLiteral("quase-zerado", "00-000-000").Com(Pessoa).E(Malformado),
            DeLiteral("fora-das-faixas", "00100000").Com(Pessoa).E(Malformado),
            DeLiteral("fora-das-faixas", "00999999").Com(Pessoa).E(Malformado),
            DeLiteral("fora-das-faixas", "0999999").Com(Pessoa).E(Malformado),
            DeLiteral("cinco-digitos", "01310").Com(Pessoa).E(Malformado),
            DeLiteral("cinco-digitos", "90010-").Com(Pessoa).E(Malformado),
            Gerador("malformado-letra", "Cep()", "trocar:3:A").Com(Pessoa).E(Malformado),
            Gerador("malformado-tamanho", "Cep()", "sufixo:0").Com(Pessoa).E(Malformado),
            Gerador("malformado-tamanho", "Cep()", "remover:0:2").Com(Pessoa).E(Malformado),
            Gerador("mascara-errada", "Cep(formatado)", "trocar:5:.").Com(Pessoa).E(Malformado),
            Gerador("mascara-errada", "Cep()", "mascara-cep-ponto", "trocar:2:-").Com(Pessoa).E(Malformado),
            Gerador("digito-nao-ascii", "Cep()", "trocar:2:\u0663").Com(Pessoa).E(Malformado),
            Gerador("sufixo-nao-espaco", "Cep()", "sufixo:\t").Com(Pessoa).E(Malformado),
            Gerador("numero", "Cep()", "numero").X(4).Com(Pessoa).E(Pseudonimizado),
            Gerador("numero-sete-digitos", "Cep()", "sem-zeros:1", "numero").Com(Pessoa).E(Pseudonimizado),
            DeLiteral("numero-ro-antiga", 78_950_000L).Com(Pessoa).E(Pseudonimizado),
            DeLiteral("numero-maximo", 99_999_999L).Com(Pessoa).E(Pseudonimizado),
            DeLiteral("numero-zerado", 0L).Com(Pessoa).E(Zerado),
            DeLiteral("numero-fora-das-faixas", 1L).Com(Pessoa).E(Malformado),
            DeLiteral("numero-fora-das-faixas", 999_999L).Com(Pessoa).E(Malformado),
            DeLiteral("numero-fora-das-faixas", -1L).Com(Pessoa).E(Malformado),
            DeLiteral("numero-fora-das-faixas", 100_000_000L).Com(Pessoa).E(Malformado),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("pseudonimizado", "Cep()").X(4).Com(Pessoa).E(Pseudonimizado),
            DeLiteral("fora-das-faixas", "00100000").Com(Pessoa).E(AnuladoMalformado),
            DeLiteral("zerado", "00000-000").Com(Pessoa).E(Zerado),
        ];

        ReceitaDePar[] preservando =
        [
            Gerador("pseudonimizado", "Cep()").X(2).Com(Pessoa).E(Pseudonimizado),
            DeLiteral("fora-das-faixas", "00100000").Com(Pessoa).E(CopiadoEmClaro),
            DeLiteral("numero-fora-das-faixas", 1L).Com(Pessoa).E(CopiadoEmClaro),
            DeLiteral("zerado", "00000000").Com(Pessoa).E(Zerado),
            DeLiteral("numero-zerado", 0L).Com(Pessoa).E(Zerado),
        ];

        return new("br.cep@1.json", "br.cep", 1, Descricao("br.cep", 1),
        [
            new("malformado falha", Parametros("cep", "homologacao", ("entradaMalformada", "falhar")), falhando),
            new("domínio e ambiente próprios, malformado anulado", Parametros("cep-entrega", "desenvolvimento", ("entradaMalformada", "anular")), anulando),
            new("malformado preservado", Parametros("cep", "homologacao", ("entradaMalformada", "preservar")), preservando),
        ]);
    }

    // As bordas entre as faixas de UF e a faixa antiga de RO, como literais: CEP é código de área, não de pessoa.
    private static IEnumerable<ReceitaDePar> Fronteiras(string entidade)
    {
        string[] bordas =
        [
            "01000000", "19999999", "68899999", "68900000", "69299999", "69300000", "69399999", "69400000", "69899999",
            "69900000", "72799999", "72800000", "73699999", "73700000", "76799999", "76800000", "78899999", "79000000", "99999999",
        ];
        foreach (var borda in bordas)
        {
            yield return DeLiteral("fronteira", borda).Com(entidade).E(Pseudonimizado);
        }

        yield return DeLiteral("ro-antiga", "78900000").Com(entidade).E(Pseudonimizado);
        yield return DeLiteral("ro-antiga", "78999999").Com(entidade).E(Pseudonimizado);
        yield return DeLiteral("ro-antiga", "78950-000").Com(entidade).E(Pseudonimizado);
    }

    // Um CEP de cada UF, com o ramo da UF: a saída de cada tabela de faixas fica presa.
    private static IEnumerable<ReceitaDePar> TodasAsUfs(string entidade)
    {
        foreach (var uf in FaixasDeCep.Ufs.ToArray())
        {
            yield return Gerador("uf-" + uf.ToLowerInvariant(), $"Cep(uf: {uf})").Com(entidade).E(Pseudonimizado);
        }
    }

    private static Func<object, ResultadoDoPar, bool> SaidaEntre(int inicio, int fim) =>
        (_, resultado) => resultado.Saida is string saida
            && int.TryParse(saida, NumberStyles.None, CultureInfo.InvariantCulture, out var cep)
            && cep >= inicio
            && cep <= fim;

    private static ReceitaDeArquivo Identificador()
    {
        long[] tamanhos = [10_000, 100_000, 1_000_000_000, 1_000_000_000_000, 100_000_000_000_000_000, 1_000_000_000_000_000_000];
        var casos = new List<ReceitaDeCaso>();
        foreach (var n in tamanhos)
        {
            casos.Add(new($"N = 10^{Math.Round(Math.Log10(n))}", Parametros("pedido-id", "homologacao", ("n", n), ("entradaMalformada", "falhar")),
            [
                Construido("uniforme", "id-uniforme", "numero").X(8).E(Pseudonimizado),
                DeLiteral("extremo", 1L).E(Pseudonimizado),
                DeLiteral("extremo", n - 1).E(Pseudonimizado),
                Inversa("caminhada", "id-caminhada", "numero").E(Pseudonimizado),
                DeLiteral("zero-ou-negativo", 0L).E(PontoFixo),
                DeLiteral("zero-ou-negativo", -1L).E(PontoFixo),
                DeLiteral("zero-ou-negativo", long.MinValue).E(PontoFixo),
                DeLiteral("a-partir-de-n", n).E(Malformado),
                DeLiteral("a-partir-de-n", long.MaxValue).E(Malformado),
            ]));
        }

        casos.Add(new("domínio e ambiente próprios, malformado anulado", Parametros("cliente-id", "desenvolvimento", ("n", 1_000_000L), ("entradaMalformada", "anular")),
        [
            Construido("uniforme", "id-uniforme", "numero").X(3).E(Pseudonimizado),
            DeLiteral("a-partir-de-n", 1_000_000L).E(AnuladoMalformado),
        ]));
        casos.Add(new("malformado preservado", Parametros("pedido-id", "homologacao", ("n", 10_000_000L), ("entradaMalformada", "preservar")),
        [
            Construido("uniforme", "id-uniforme", "numero").X(2).E(Pseudonimizado),
            DeLiteral("a-partir-de-n", 10_000_005L).E(CopiadoEmClaro),
        ]));

        return new("id.permutar@1.json", "id.permutar", 1, Descricao("id.permutar", 1), casos);
    }

    private static ReceitaDeArquivo ChavePix()
    {
        ReceitaDePar[] falhando =
        [
            Gerador("cpf", "ChavePix(Cpf)").X(5).E(Pseudonimizado),
            Gerador("cnpj", "ChavePix(Cnpj)").X(6).E(Pseudonimizado),
            Gerador("email", "ChavePix(Email)").X(4).E(Pseudonimizado),
            Gerador("telefone", "ChavePix(Telefone)").X(4).E(Pseudonimizado),
            Gerador("aleatoria", "ChavePix(Aleatoria)").X(6).E(Pseudonimizado),
            Gerador("cpf-mascara", "Cpf(formatado)").E(Pseudonimizado),
            Gerador("cnpj-mascara", "Cnpj(formatado)").E(Pseudonimizado),
            Gerador("cnpj-mascara", "Cnpj(alfanumerico, formatado)").E(Pseudonimizado),
            Gerador("cnpj-minusculas", "Cnpj(alfanumerico)", "minusculas-ascii").X(3).E(Pseudonimizado),
            Gerador("cnpj-minusculas", "Cnpj(alfanumerico, formatado)", "minusculas-ascii").E(Pseudonimizado),
            Construido("cnpj-raiz-zero", "cnpj-raiz-zero").E(PontoFixo),
            Gerador("cnpj-dv-invalido", "Cnpj()", "dv:4").E(DvInvalidoPreservado),
            Gerador("cpf-dv-invalido", "Cpf()", "dv:9").E(DvInvalidoPreservado),
            Construido("cpf-repdigito", "cpf-repdigito").E(PontoFixo),
            Gerador("aleatoria-maiusculas", "ChavePix(Aleatoria)", "maiusculas-ascii").X(2).E(Pseudonimizado),
            Construido("aleatoria-sem-versao", "uuid-sem-versao").X(2).E(Pseudonimizado),
            Construido("aleatoria-sem-versao", "uuid-sem-versao", "maiusculas-ascii").E(Pseudonimizado),
            DeLiteral("aleatoria-assimetrica", "00010203-0405-0607-0809-0a0b0c0d0e0f").E(Pseudonimizado),
            Gerador("onze-digitos-e-cpf", "Telefone(celular)").X(2),
            Gerador("telefone-dez-digitos", "Telefone(fixo)").X(2).E(Pseudonimizado),
            Gerador("telefone-doze-ou-treze-digitos", "Telefone(fixo, comPais)", "remover:0:1").E(Pseudonimizado),
            Gerador("telefone-doze-ou-treze-digitos", "Telefone(celular, comPais)", "remover:0:1").E(Pseudonimizado),
            Gerador("telefone-nove-digitos", "Telefone(celular)", "remover:0:2").E(Pseudonimizado),
            Gerador("telefone-formatado", "Telefone(celular, formatado)").E(Pseudonimizado),
            Gerador("telefone-formatado", "Telefone(fixo, comPais, formatado)").E(Pseudonimizado),
            Construido("telefone-servico", "telefone-servico-nao-geografico", "prefixo:+55").E(ServicoPreservado),
            Construido("telefone-servico", "telefone-servico-nao-geografico:303", "prefixo:+55").E(ServicoPreservado),
            DeLiteral("telefone-zerado", "+5511000000000").E(Zerado),
            DeLiteral("telefone-zerado", "0000000000").E(Zerado),
            Gerador("arroba-vence", "Cpf()", "sufixo:@example.org").E(Pseudonimizado),
            Gerador("email-maiusculas", "ChavePix(Email)", "maiusculas-ascii").E(Pseudonimizado),
            Gerador("preenchimento", "ChavePix(Cpf)", "espacos:3").E(Pseudonimizado),
            Gerador("preenchimento", "ChavePix(Aleatoria)", "espacos:2").E(Pseudonimizado),
            Gerador("preenchimento", "ChavePix(Email)", "espacos:10").E(Pseudonimizado),
            Gerador("preenchimento", "ChavePix(Telefone)", "espacos:1").E(Pseudonimizado),
            DeLiteral("vazio", "  ").E(Vazio),
            Gerador("cnpj-minusculas-dv-errado", "Cnpj(alfanumerico)", "minusculas-ascii", "dv:3").E(Malformado),
            Gerador("letra-nao-ascii", "Cnpj(alfanumerico)", "trocar:0:\u212A").E(Malformado),
            Gerador("letra-nao-ascii", "Cnpj(alfanumerico)", "trocar:0:\u0131").E(Malformado),
            Construido("uuid-fora-da-forma", "uuid-sem-versao", "remover:8:1").E(Malformado),
            Construido("uuid-fora-da-forma", "uuid-sem-versao", "trocar:8:_").E(Malformado),
            Gerador("telefone-com-letra", "Telefone(celular, formatado)", "trocar:6:x").E(Malformado),
            Gerador("mais-sem-digito", "Telefone(celular)", "prefixo:+ ").E(Malformado),
            Gerador("cpf-com-espaco-a-esquerda", "Cpf()", "prefixo: ").E(Malformado),
        ];

        ReceitaDePar[] anulando =
        [
            Gerador("cpf", "ChavePix(Cpf)").X(2).E(Pseudonimizado),
            Gerador("cnpj", "ChavePix(Cnpj)").X(3).E(Pseudonimizado),
            Gerador("email", "ChavePix(Email)").X(2).E(Pseudonimizado),
            Gerador("telefone", "ChavePix(Telefone)").X(2).E(Pseudonimizado),
            Gerador("aleatoria", "ChavePix(Aleatoria)").X(2).E(Pseudonimizado),
            Gerador("cpf-dv-invalido", "Cpf()", "dv:15").E(AnuladoDvInvalido),
            Gerador("cnpj-dv-invalido", "Cnpj()", "dv:15").E(DvInvalido),
            Gerador("cnpj-minusculas-dv-errado", "Cnpj(alfanumerico)", "minusculas-ascii", "dv:3").E(AnuladoMalformado),
            Gerador("telefone-com-letra", "Telefone(celular, formatado)", "trocar:6:x").E(AnuladoMalformado),
            DeLiteral("telefone-zerado", "+55 (11) 00000-0000").E(Zerado),
        ];

        ReceitaDePar[] preservando =
        [
            Gerador("cpf", "ChavePix(Cpf)").E(Pseudonimizado),
            Gerador("cnpj", "ChavePix(Cnpj)").E(Pseudonimizado),
            Gerador("email", "ChavePix(Email)").E(Pseudonimizado),
            Gerador("telefone", "ChavePix(Telefone)").E(Pseudonimizado),
            Gerador("aleatoria-maiusculas", "ChavePix(Aleatoria)", "maiusculas-ascii").E(Pseudonimizado),
            Construido("uuid-fora-da-forma", "uuid-sem-versao", "remover:8:1").E(CopiadoEmClaro),
            Gerador("cpf-dv-invalido", "Cpf()", "dv:77").E(AnuladoDvInvalido),
        ];

        return new("br.chave-pix@1.json", "br.chave-pix", 1, Descricao("br.chave-pix", 1),
        [
            new("domínios padrão, malformado falha",
                Parametros("chave-pix", "homologacao", ("entradaMalformada", "falhar"), ("componentes", Componentes("cpf", "preservar", "cnpj", false, "preservar", "email", "telefone"))),
                falhando),
            new("domínios e ambiente próprios, ordem permutada, malformado anulado",
                Parametros("chave-pix-loja", "desenvolvimento", ("entradaMalformada", "anular"), ("componentes", Componentes("cpf-cliente", "anular", "cnpj-cliente", true, "falhar", "email-cliente", "telefone-cliente"))),
                anulando),
            new("malformado preservado",
                Parametros("chave-pix", "homologacao", ("entradaMalformada", "preservar"), ("componentes", Componentes("cpf", "anular", "cnpj", false, "preservar", "email", "telefone"))),
                preservando),
            new("domínios padrão em outro ambiente",
                Parametros("chave-pix", "producao", ("entradaMalformada", "falhar"), ("componentes", Componentes("cpf", "preservar", "cnpj", false, "preservar", "email", "telefone"))),
            [
                Gerador("cpf", "ChavePix(Cpf)").X(2).E(Pseudonimizado),
                Gerador("cnpj", "ChavePix(Cnpj)").X(2).E(Pseudonimizado),
                Gerador("email", "ChavePix(Email)").X(2).E(Pseudonimizado),
                Gerador("telefone", "ChavePix(Telefone)").X(2).E(Pseudonimizado),
                Gerador("aleatoria", "ChavePix(Aleatoria)").X(3).E(Pseudonimizado),
                Gerador("telefone-com-letra", "Telefone(celular, formatado)", "trocar:6:x").E(Malformado),
            ]),
        ]);
    }

    private static KeyValuePair<string, object>[] Componentes(
        string dominioCpf, string invalidaCpf, string dominioCnpj, bool permutarOrdem, string invalidaCnpj, string dominioEmail, string dominioTelefone) =>
    [
        new("cpf", (KeyValuePair<string, object>[])[new("dominio", dominioCpf), new("versao", 1L), new("entradaInvalida", invalidaCpf)]),
        new("cnpj", (KeyValuePair<string, object>[])[new("dominio", dominioCnpj), new("versao", 1L), new("permutarOrdem", permutarOrdem), new("entradaInvalida", invalidaCnpj)]),
        new("email", (KeyValuePair<string, object>[])[new("dominio", dominioEmail), new("versao", 1L)]),
        new("telefone", (KeyValuePair<string, object>[])[new("dominio", dominioTelefone), new("versao", 1L)]),
    ];
}
