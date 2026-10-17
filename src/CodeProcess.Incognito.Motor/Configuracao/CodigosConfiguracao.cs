namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// Códigos dos erros de configuração, na faixa <c>INC2xxx</c>. Um código nunca é reutilizado nem renumerado: um erro que
/// deixa de existir deixa o número vago.
/// </summary>
internal static class CodigosConfiguracao
{
    /// <summary>Configuração inválida, com a lista dos erros (o código da exceção).</summary>
    internal const string Agregado = "INC2000";

    /// <summary>Arquivo ausente, ilegível, vazio, grande demais ou fora de UTF-8.</summary>
    internal const string Arquivo = "INC2001";

    /// <summary>Caractere proibido no texto (controle, quebra de linha Unicode, controle bidirecional, BOM no meio).</summary>
    internal const string Caractere = "INC2002";

    /// <summary>YAML com erro de sintaxe.</summary>
    internal const string Sintaxe = "INC2003";

    /// <summary>Construção do YAML não aceita: âncora, alias, tag, diretiva, mais de um documento, chave que não é texto, profundidade.</summary>
    internal const string ConstrucaoNaoAceita = "INC2004";

    /// <summary>Chave repetida no mesmo mapa.</summary>
    internal const string ChaveRepetida = "INC2005";

    /// <summary>Chave desconhecida.</summary>
    internal const string ChaveDesconhecida = "INC2006";

    /// <summary>Campo obrigatório ausente.</summary>
    internal const string Obrigatorio = "INC2007";

    /// <summary>Tipo errado: texto, número, mapa ou lista onde se esperava outro.</summary>
    internal const string Tipo = "INC2008";

    /// <summary>Valor fora dos aceitos pelo campo.</summary>
    internal const string Valor = "INC2009";

    /// <summary>Nome de domínio ou ambiente fora da regra dos nomes.</summary>
    internal const string Nome = "INC2010";

    /// <summary>Segredo por extenso no arquivo (chave do Incognito, senha ou URL com credencial).</summary>
    internal const string SegredoPorExtenso = "INC2011";

    /// <summary>Referência de segredo fora da forma <c>ambiente:NOME</c> ou <c>arquivo:caminho</c>, ou recusada.</summary>
    internal const string Referencia = "INC2012";

    /// <summary>Domínio citado e não declarado.</summary>
    internal const string DominioDesconhecido = "INC2013";

    /// <summary>Ciclo em <c>derivarDe</c>.</summary>
    internal const string Ciclo = "INC2014";

    /// <summary>Coluna sensível mantida sem justificativa.</summary>
    internal const string Justificativa = "INC2015";

    /// <summary>Campos incompatíveis entre si.</summary>
    internal const string Incoerente = "INC2016";

    /// <summary>Nome de tabela ou coluna fora da forma.</summary>
    internal const string Identificador = "INC2017";

    /// <summary>Transformador fora da forma <c>nome@versao</c>.</summary>
    internal const string Transformador = "INC2018";

    /// <summary>Domínio declarado mais de uma vez.</summary>
    internal const string DominioRepetido = "INC2019";

    /// <summary>Transformador sem fábrica registrada com esse nome.</summary>
    internal const string TransformadorDesconhecido = "INC2020";

    /// <summary>Versão do transformador sem fábrica registrada (sem versão escrita, vale a 1).</summary>
    internal const string VersaoInexistente = "INC2021";

    /// <summary>Regra recusada pela fábrica: parâmetro, <c>derivarDe</c> ou <c>justificativa</c>.</summary>
    internal const string RegraRecusada = "INC2022";

    /// <summary>Um nome de domínio com duas definições, ou o domínio padrão de uma fábrica com outra definição.</summary>
    internal const string ConflitoDeDominio = "INC2023";

    /// <summary>Todos os códigos, para o teste de unicidade.</summary>
    internal static IReadOnlyList<string> Todos { get; } =
    [
        Agregado, Arquivo, Caractere, Sintaxe, ConstrucaoNaoAceita, ChaveRepetida, ChaveDesconhecida, Obrigatorio, Tipo,
        Valor, Nome, SegredoPorExtenso, Referencia, DominioDesconhecido, Ciclo, Justificativa, Incoerente,
        Identificador, Transformador, DominioRepetido, TransformadorDesconhecido, VersaoInexistente, RegraRecusada,
        ConflitoDeDominio,
    ];
}
