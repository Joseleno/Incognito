using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A situação de cada tipo do PostgreSQL na matriz de tipos e a representação canônica dos suportados. O tipo embutido é
/// reconhecido pelo OID, que é fixo, nunca pelo nome: um <c>public.uuid</c> ou um domínio chamado <c>text</c> não passam
/// por embutidos.
/// </summary>
internal static class ClassificacaoDeTipos
{
    // OIDs fixos dos tipos embutidos (pg_type.dat).
    private const uint Bool = 16;
    private const uint Bytea = 17;
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
        // O destino recalcula a coluna gerada: fica fora da cópia, qualquer que seja o tipo.
        if (geracao != GeracaoColuna.Nenhuma)
        {
            return (SituacaoTipo.SuportadoPorExclusao, null);
        }

        // Foreign table e large objects (a coluna oid e o tipo lo da extensão lo, um domínio sobre oid): não suportados.
        if (tabelaEstrangeira || tipo.Oid == Oid || (tipo.Extensao == "lo" && tipo.Nome == "lo"))
        {
            return (SituacaoTipo.NaoSuportado, null);
        }

        // O citext compara sem caixa, mas o valor é texto: o transformador de e-mail serve.
        if (tipo.Extensao == "citext" && tipo.Nome == "citext" && tipo.Categoria == 'b')
        {
            return (SituacaoTipo.Suportado, TipoColuna.Texto);
        }

        // Arrays, domínios (o CHECK do domínio pode recusar o pseudônimo), enums, ranges, multiranges e compostos.
        if (tipo.Array || tipo.Categoria != 'b')
        {
            return (SituacaoTipo.ComRessalva, null);
        }

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
}

/// <summary>O que o catálogo diz do tipo de uma coluna.</summary>
/// <param name="Oid">O OID do tipo.</param>
/// <param name="Nome">O nome do tipo (<c>typname</c>), só para os tipos de extensão.</param>
/// <param name="Categoria">O <c>typtype</c>: <c>b</c> base, <c>d</c> domínio, <c>e</c> enum, <c>r</c> range, <c>m</c> multirange, <c>c</c> composto, <c>p</c> pseudo.</param>
/// <param name="Array">Se é array (o <c>typsubscript</c> dos arrays de verdade).</param>
/// <param name="Extensao">A extensão dona do tipo, se há.</param>
internal readonly record struct TipoLido(uint Oid, string Nome, char Categoria, bool Array, string? Extensao);
