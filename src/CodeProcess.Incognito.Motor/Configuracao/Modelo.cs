using CodeProcess.Incognito.Configuracao.Leitura;

namespace CodeProcess.Incognito.Configuracao;

// Modelo da configuração lida, interno até o `Plano` decidir o que publica. Imutável; cada elemento guarda a posição no
// YAML, para erros posteriores e para o SARIF do relatório apontarem o arquivo.

/// <summary>Modo de tratamento.</summary>
internal enum ModoTratamento
{
    /// <summary>Chave persistente: a mesma entrada dá o mesmo pseudônimo entre execuções.</summary>
    Reproduzivel,

    /// <summary>Chave gerada na memória e descartada ao final.</summary>
    Efemero,
}

/// <summary>Banco de origem ou de destino.</summary>
internal enum ProvedorBanco
{
    PostgreSql,
    SqlServer,
}

/// <summary>Tipo de referência a um segredo.</summary>
internal enum TipoReferenciaSegredo
{
    /// <summary>Variável de ambiente.</summary>
    Ambiente,

    /// <summary>Arquivo, com caminho absoluto.</summary>
    Arquivo,
}

/// <summary>DV errado (<c>entradaInvalida</c>).</summary>
internal enum TratamentoEntradaInvalida
{
    Preservar,
    Falhar,
    Anular,
}

/// <summary>Tamanho ou caracteres errados (<c>entradaMalformada</c>).</summary>
internal enum TratamentoEntradaMalformada
{
    Falhar,
    Anular,
    Preservar,
}

/// <summary>Consistência da leitura.</summary>
internal enum Consistencia
{
    MelhorEsforco,
    Exigir,
}

/// <summary>Ação sobre uma tabela.</summary>
internal enum AcaoTabela
{
    /// <summary>Esvazia o destino e copia, aplicando as regras das colunas (padrão).</summary>
    Copiar,

    /// <summary>Esvazia o destino e não copia nada.</summary>
    Esvaziar,

    /// <summary>Não toca a tabela no destino.</summary>
    Ignorar,
}

/// <summary>De onde veio a regra.</summary>
internal enum OrigemRegra
{
    Configuracao,
}

/// <summary>Forma da regra de uma coluna.</summary>
internal enum TipoRegraColuna
{
    /// <summary>Copia sem alterar (<c>manter</c>).</summary>
    Manter,

    /// <summary>Usa um domínio declarado em <c>dominios</c> ou numa coluna.</summary>
    Dominio,

    /// <summary>Transformador escrito na coluna, com o domínio padrão da fábrica ou o declarado ali.</summary>
    Transformador,
}

/// <summary>Referência a um segredo, nunca o valor: o nome da variável ou o caminho absoluto do arquivo.</summary>
internal sealed record ReferenciaSegredo(TipoReferenciaSegredo Tipo, string Alvo, Posicao Posicao);

/// <summary>Origem ou destino.</summary>
internal sealed record ConexaoBanco(ProvedorBanco Provedor, ReferenciaSegredo Conexao, string? BancoEsperado, Posicao Posicao);

/// <summary>Transformador escrito como <c>nome@versao</c>; a versão ausente fica nula (o registro resolve para 1).</summary>
internal sealed record ReferenciaTransformador(string Nome, int? Versao, Posicao Posicao);

/// <summary>Políticas que podem ser escritas na raiz, no domínio ou na coluna; nulo quando não escrita naquele nível.</summary>
internal sealed record PoliticasDeEntrada(TratamentoEntradaInvalida? EntradaInvalida, TratamentoEntradaMalformada? EntradaMalformada)
{
    internal static PoliticasDeEntrada Nenhuma { get; } = new(null, null);
}

/// <summary>Políticas da raiz (<c>politica</c>); nulo quando omitida, para o padrão e a precedência ficarem com quem usa.</summary>
internal sealed record PoliticasGerais(PoliticasDeEntrada Entrada, Consistencia? Consistencia, int? Paralelismo);

/// <summary>Domínio declarado em <c>dominios</c> ou numa coluna (o do <c>id.permutar</c>).</summary>
internal sealed record RegraDominio(
    string Nome,
    ReferenciaTransformador Transformador,
    IReadOnlyList<KeyValuePair<string, string>> Parametros,
    PoliticasDeEntrada Politicas,
    Posicao Posicao);

/// <summary>Nome de tabela ou coluna como escrito, sem resolver maiúsculas (o dialeto resolve no planejar).</summary>
internal sealed record Identificador(string Nome, bool EntreAspas);

/// <summary>Tabela com o esquema obrigatório (<c>public.clientes</c>, <c>'"Vendas"."Clientes"'</c>).</summary>
internal sealed record NomeTabela(Identificador Esquema, Identificador Tabela);

/// <summary>Regra de uma coluna.</summary>
internal sealed record RegraColuna(
    Identificador Coluna,
    TipoRegraColuna Tipo,
    string? Dominio,
    ReferenciaTransformador? Transformador,
    Identificador? DerivarDe,
    IReadOnlyList<KeyValuePair<string, string>> Parametros,
    PoliticasDeEntrada Politicas,
    bool Sensivel,
    string? Justificativa,
    OrigemRegra Origem,
    Posicao Posicao);

/// <summary>Regra de uma tabela, com as colunas na ordem do arquivo.</summary>
internal sealed record RegraTabela(NomeTabela Nome, AcaoTabela Acao, IReadOnlyList<RegraColuna> Colunas, Posicao Posicao);
