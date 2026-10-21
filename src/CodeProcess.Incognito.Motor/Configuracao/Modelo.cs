using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Configuracao;

// Modelo da configuração lida, interno: o `Plano` publica a regra efetiva, não a escrita. Só os três enums que o `Plano`
// cita são públicos, com valores explícitos que nunca se renumeram. Imutável; cada elemento guarda a posição no YAML, para
// erros posteriores e para o SARIF do relatório apontarem o arquivo.

/// <summary>Modo de tratamento.</summary>
public enum ModoTratamento
{
    /// <summary>
    /// Chave fornecida por quem executa: a mesma entrada, com a mesma chave e o mesmo domínio, dá a mesma saída em qualquer
    /// tabela, banco ou execução. O resultado é dado pessoal, e a chave permite reverter todos os valores.
    /// </summary>
    Reproduzivel = 0,

    /// <summary>
    /// Chave aleatória gerada na memória e descartada ao final: consistente dentro da execução, diferente entre execuções.
    /// Não é anonimização garantida: a base resultante normalmente continua sendo dado pessoal para o controlador.
    /// </summary>
    Efemero = 1,
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

/// <summary>Consistência da leitura.</summary>
internal enum Consistencia
{
    MelhorEsforco,
    Exigir,
}

/// <summary>Ação sobre uma tabela.</summary>
public enum AcaoTabela
{
    /// <summary>Esvazia o destino e copia, aplicando as regras das colunas (padrão).</summary>
    Copiar = 0,

    /// <summary>Esvazia o destino e não copia nada.</summary>
    Esvaziar = 1,

    /// <summary>Não toca a tabela no destino.</summary>
    Ignorar = 2,
}

/// <summary>De onde veio a regra de uma coluna.</summary>
public enum OrigemRegra
{
    /// <summary>Do arquivo de configuração.</summary>
    Configuracao = 0,
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

/// <summary>
/// Referência a um segredo, nunca o valor: o nome da variável ou o caminho absoluto do arquivo, e o texto como foi escrito
/// (com o separador de caminho normalizado para <c>/</c>), que entra no hash da configuração sem depender da máquina.
/// </summary>
internal sealed record ReferenciaSegredo(TipoReferenciaSegredo Tipo, string Alvo, string Escrito, Posicao Posicao);

/// <summary>Origem ou destino.</summary>
internal sealed record ConexaoBanco(ProvedorBanco Provedor, ReferenciaSegredo Conexao, string? BancoEsperado, Posicao Posicao);

/// <summary>Transformador escrito como <c>nome@versao</c>; a versão ausente fica nula (o registro resolve para 1).</summary>
internal sealed record ReferenciaTransformador(string Nome, int? Versao, Posicao Posicao, string Local);

/// <summary>
/// Parâmetro de transformador como texto: booleanos e inteiros do YAML na forma canônica (<c>true</c>, <c>55</c>), lista
/// como texto separado por vírgula. Guarda a posição e o caminho da chave, para o erro da fábrica apontar o parâmetro.
/// </summary>
internal sealed record ParametroRegra(string Nome, string Valor, Posicao Posicao, string Local);

/// <summary>Políticas que podem ser escritas na raiz, no domínio ou na coluna; nulo quando não escrita naquele nível.</summary>
internal sealed record PoliticasDeEntrada(PoliticaEntradaInvalida? EntradaInvalida, PoliticaEntradaMalformada? EntradaMalformada)
{
    internal static PoliticasDeEntrada Nenhuma { get; } = new(null, null);
}

/// <summary>Políticas da raiz (<c>politica</c>); nulo quando omitida, para o padrão e a precedência ficarem com quem usa.</summary>
internal sealed record PoliticasGerais(PoliticasDeEntrada Entrada, Consistencia? Consistencia, int? Paralelismo);

/// <summary>Domínio declarado em <c>dominios</c> ou numa coluna (o do <c>id.permutar</c>).</summary>
internal sealed record RegraDominio(
    string Nome,
    ReferenciaTransformador Transformador,
    IReadOnlyList<ParametroRegra> Parametros,
    PoliticasDeEntrada Politicas,
    Posicao Posicao,
    string Local);

/// <summary>Nome de tabela ou coluna como escrito, sem resolver maiúsculas (o dialeto resolve no planejar).</summary>
internal sealed record Identificador(string Nome, bool EntreAspas);

/// <summary>Tabela com o esquema obrigatório (<c>public.clientes</c>, <c>'"Vendas"."Clientes"'</c>).</summary>
internal sealed record NomeTabelaEscrito(Identificador Esquema, Identificador Tabela);

/// <summary>Regra de uma coluna.</summary>
internal sealed record RegraColunaEscrita(
    Identificador Coluna,
    TipoRegraColuna Tipo,
    string? Dominio,
    ReferenciaTransformador? Transformador,
    Identificador? DerivarDe,
    IReadOnlyList<ParametroRegra> Parametros,
    PoliticasDeEntrada Politicas,
    bool Sensivel,
    string? Justificativa,
    OrigemRegra Origem,
    Posicao Posicao,
    string Local);

/// <summary>
/// Regra de uma tabela, com as colunas na ordem do arquivo e o caminho até ela no YAML (JSON Pointer). A ação ausente
/// fica <see cref="AcaoTabela.Copiar"/>, com <c>AcaoEscrita</c> falso.
/// </summary>
internal sealed record RegraTabelaEscrita(NomeTabelaEscrito Nome, AcaoTabela Acao, bool AcaoEscrita, IReadOnlyList<RegraColunaEscrita> Colunas, Posicao Posicao, string Local);
