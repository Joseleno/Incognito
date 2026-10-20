using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A situação de cada tipo do PostgreSQL na matriz de tipos e a representação canônica dos suportados. O tipo embutido é
/// reconhecido pelo OID, que é fixo, nunca pelo nome: um <c>public.uuid</c> ou um domínio chamado <c>text</c> não passam
/// por embutidos.
/// </summary>
/// <remarks>
/// A cadeia desce só por domínios e arrays: um <c>oid</c> ou <c>lo</c> dentro de um composto, de um range ou de um
/// multirange fica com ressalva, não como não suportado (o conteúdo dos large objects continua no residual, e a coluna com
/// ressalva só aceita <c>nulo</c> ou <c>fixo</c>).
/// </remarks>
internal static class ClassificacaoDeTipos
{
    // OIDs fixos dos tipos embutidos (pg_type.dat).
    private const uint Bool = 16;
    private const uint Bytea = 17;
    private const uint Char = 18;
    private const uint Name = 19;
    private const uint Int8 = 20;
    private const uint Int2 = 21;
    private const uint Int4 = 23;
    private const uint Text = 25;
    private const uint Oid = 26;
    private const uint Bpchar = 1042;
    private const uint Varchar = 1043;
    private const uint Date = 1082;
    private const uint Time = 1083;
    private const uint Timestamp = 1114;
    private const uint Timestamptz = 1184;
    private const uint Interval = 1186;
    private const uint Timetz = 1266;
    private const uint Numeric = 1700;
    private const uint Uuid = 2950;

    /// <summary>Classifica a coluna.</summary>
    /// <param name="tipo">O que o catálogo diz do tipo da coluna.</param>
    /// <param name="geracao">Como o banco gera a coluna.</param>
    /// <param name="tabelaEstrangeira">Se a coluna é de uma foreign table.</param>
    /// <returns>A situação e o tipo canônico (só em suportado, e nem todo suportado tem).</returns>
    internal static (SituacaoTipo Situacao, TipoColuna? Tipo) Classificar(TipoLido tipo, GeracaoColuna geracao, bool tabelaEstrangeira)
    {
        // O destino recalcula a coluna gerada: fica fora da cópia, qualquer que seja o tipo, também numa foreign table.
        if (geracao != GeracaoColuna.Nenhuma)
        {
            return (SituacaoTipo.SuportadoPorExclusao, null);
        }

        // Foreign table e large objects: o oid, e o que acaba nele pela cadeia de domínios e de arrays (o tipo lo da
        // extensão lo é um domínio sobre oid; um domínio sobre oid, oid[] e lo[] também).
        if (tabelaEstrangeira || tipo.Final == Oid)
        {
            return (SituacaoTipo.NaoSuportado, null);
        }

        // O citext compara sem caixa, mas o valor é texto: o transformador de e-mail serve. Só o tipo base da extensão: um
        // domínio que a extensão adotou, com o mesmo nome ou outro, é domínio.
        if (tipo.Extensao == "citext" && tipo.Nome == "citext" && tipo.Categoria == 'b')
        {
            return (SituacaoTipo.Suportado, TipoColuna.Texto);
        }

        // Pelo OID do próprio tipo: arrays, domínios (o CHECK do domínio pode recusar o pseudônimo), enums, ranges,
        // multiranges e compostos têm OID próprio e caem no fim, com ressalva.
        return tipo.Oid switch
        {
            Int2 or Int4 or Int8 => (SituacaoTipo.Suportado, TipoColuna.Inteiro),
            Numeric => (SituacaoTipo.Suportado, TipoColuna.Decimal),
            Text or Varchar or Bpchar => (SituacaoTipo.Suportado, TipoColuna.Texto),
            Bool => (SituacaoTipo.Suportado, TipoColuna.Booleano),
            Date => (SituacaoTipo.Suportado, TipoColuna.Data),
            Timestamp => (SituacaoTipo.Suportado, TipoColuna.DataHora),
            Timestamptz => (SituacaoTipo.Suportado, TipoColuna.DataHoraComFuso),
            Uuid => (SituacaoTipo.Suportado, TipoColuna.Uuid),
            Bytea => (SituacaoTipo.Suportado, TipoColuna.Binario),

            // Suportados sem representação canônica: cópia direta, nenhum transformador os aceita.
            Time or Timetz or Interval => (SituacaoTipo.Suportado, null),

            // json, jsonb, xml, money, tipos de extensão e os que a matriz não cita (real, double precision, inet, bit...).
            _ => (SituacaoTipo.ComRessalva, null),
        };
    }

    /// <summary>
    /// A semântica do tipo pelo fim da cadeia de domínios e arrays (num array, vale para cada elemento): faixa do inteiro,
    /// as duas marcas de texto, o comprimento e a precisão do decimal.
    /// </summary>
    /// <param name="final">O OID do fim da cadeia.</param>
    /// <param name="finalCitext">Se o fim da cadeia é o <c>citext</c> da extensão.</param>
    /// <param name="modificador">O primeiro modificador de tipo da cadeia que não é -1 (o da coluna, depois o de cada domínio).</param>
    /// <param name="semCodificacao">Se o banco é <c>SQL_ASCII</c>: o texto é medido em bytes.</param>
    /// <param name="maximoIdentificador">O <c>max_identifier_length</c> do servidor, o limite do tipo <c>name</c>.</param>
    /// <returns>A semântica.</returns>
    internal static SemanticaDoTipo Semantica(uint final, bool finalCitext, int modificador, bool semCodificacao, int maximoIdentificador)
    {
        var caracteres = semCodificacao ? UnidadeComprimento.Bytes : UnidadeComprimento.PontosDeCodigo;
        LimiteComprimento? comprimento = final switch
        {
            // O typmod do varchar e do char guarda o tamanho mais 4 (o cabeçalho de um varlena).
            Varchar or Bpchar => Limite(modificador == -1 ? null : modificador - 4, caracteres),
            Text => Limite(null, caracteres),
            _ when finalCitext => Limite(null, caracteres),
            Name => Limite(maximoIdentificador, UnidadeComprimento.Bytes),
            Char => Limite(1, UnidadeComprimento.Bytes),
            Bytea => Limite(null, UnidadeComprimento.Bytes),
            _ => null,
        };
        var texto = final is Text or Varchar or Bpchar or Name or Char || finalCitext;

        // O typmod do numeric: a precisão nos 16 bits altos, a escala nos 11 baixos com sinal (negativa no 15+).
        var precisao = final == Numeric && modificador != -1 ? ((modificador - 4) >> 16) & 0xFFFF : (int?)null;
        var escala = precisao is null ? (int?)null : (((modificador - 4) & 0x7FF) ^ 0x400) - 0x400;
        FaixaInteira? faixa = final switch
        {
            Int2 => new() { Minimo = short.MinValue, Maximo = short.MaxValue },
            Int4 => new() { Minimo = int.MinValue, Maximo = int.MaxValue },
            Int8 => new() { Minimo = long.MinValue, Maximo = long.MaxValue },
            _ => null,
        };
        return new(
            comprimento,
            precisao,
            escala,
            faixa,
            texto ? finalCitext : null,
            texto ? final == Bpchar : null);
    }

    private static LimiteComprimento Limite(int? valor, UnidadeComprimento unidade) => new() { Valor = valor, Unidade = unidade };
}

/// <summary>A semântica do tipo de uma coluna.</summary>
/// <param name="Comprimento">O limite de comprimento; nulo fora de texto e binário (e no <c>bit</c>, que não é medido).</param>
/// <param name="Precisao">A precisão do decimal.</param>
/// <param name="Escala">A escala do decimal.</param>
/// <param name="Faixa">A faixa do inteiro.</param>
/// <param name="IgnoraCaixa">Se o tipo ignora a caixa; nulo fora de texto.</param>
/// <param name="IgnoraEspacoFinal">Se o tipo ignora os espaços finais; nulo fora de texto.</param>
internal readonly record struct SemanticaDoTipo(
    LimiteComprimento? Comprimento, int? Precisao, int? Escala, FaixaInteira? Faixa, bool? IgnoraCaixa, bool? IgnoraEspacoFinal);

/// <summary>O que o catálogo diz do tipo de uma coluna.</summary>
/// <param name="Oid">O OID do tipo.</param>
/// <param name="Nome">O nome do tipo (<c>typname</c>), só para os tipos de extensão.</param>
/// <param name="Categoria">O <c>typtype</c>: <c>b</c> base, <c>d</c> domínio, <c>e</c> enum, <c>r</c> range, <c>m</c> multirange, <c>c</c> composto, <c>p</c> pseudo.</param>
/// <param name="Extensao">A extensão dona do tipo, se há.</param>
/// <param name="Final">O OID do fim da cadeia: do domínio ao tipo base e do array ao elemento, até não haver mais nenhum.</param>
internal readonly record struct TipoLido(uint Oid, string Nome, char Categoria, string? Extensao, uint Final);
