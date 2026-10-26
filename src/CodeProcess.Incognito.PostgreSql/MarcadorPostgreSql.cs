using System.Globalization;
using Npgsql;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// O formato do marcador no comentário do banco (<c>COMMENT ON DATABASE</c>):
/// <c>incognito-destino:1:&lt;identificador em minúsculas&gt;:&lt;aaaa-mm-ddThh:mm:ssZ&gt;</c>. A situação sai do servidor: o
/// texto só chega ao processo quando começa pelo prefixo e tem o tamanho exato do formato, e o de um comentário alheio
/// nunca.
/// </summary>
/// <remarks>
/// O comentário fica no catálogo compartilhado (<c>pg_shdescription</c>), pelo oid do banco: <c>CREATE DATABASE ...
/// TEMPLATE</c> e <c>pg_dump</c> sem <c>--create</c> não o levam; a cópia física, o snapshot, a recuperação a um ponto no
/// tempo e o <c>ALTER DATABASE ... RENAME</c> levam.
/// </remarks>
internal static class FormatoDoMarcador
{
    /// <summary>O começo de todo marcador do Incognito, de qualquer versão.</summary>
    internal const string Prefixo = "incognito-destino:";

    /// <summary>O tamanho do marcador da versão 1.</summary>
    internal const int Tamanho = 77;

    private const string Versao = "1:";
    private const string FormatoDaData = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// A situação (0 ausente, 1 com o prefixo, 3 alheio) e o texto, só quando tem o prefixo e o tamanho exato, a partir do
    /// oid do banco em <c>d</c>. Um comentário vazio não existe: o PostgreSQL o apaga.
    /// </summary>
    internal const string Colunas = $"""
        (SELECT CASE WHEN m.texto IS NULL THEN 0 WHEN pg_catalog.starts_with(m.texto, '{Prefixo}') THEN 1 ELSE 3 END
         FROM (SELECT pg_catalog.shobj_description(d.oid, 'pg_database') AS texto) m),
        (SELECT CASE WHEN pg_catalog.starts_with(m.texto, '{Prefixo}') AND pg_catalog.length(m.texto) = {TamanhoEmTexto} THEN m.texto END
         FROM (SELECT pg_catalog.shobj_description(d.oid, 'pg_database') AS texto) m)
        """;

    // O tamanho como texto, para a consulta (uma constante interpolada só aceita texto); igual a Tamanho.
    private const string TamanhoEmTexto = "77";

    /// <summary>O texto do marcador: só o prefixo, a versão, o identificador e a data, nada vindo de fora.</summary>
    /// <param name="identificador">O identificador.</param>
    /// <param name="gravadoEm">Quando, em UTC e sem fração de segundo.</param>
    /// <returns>O texto.</returns>
    internal static string Texto(Guid identificador, DateTimeOffset gravadoEm) =>
        Prefixo + Versao + identificador.ToString("D", CultureInfo.InvariantCulture) + ":" + gravadoEm.UtcDateTime.ToString(FormatoDaData, CultureInfo.InvariantCulture);

    /// <summary>O marcador lido das duas colunas de <see cref="Colunas"/>.</summary>
    /// <param name="leitor">O leitor, na linha da consulta.</param>
    /// <param name="situacao">A posição da coluna da situação; o texto vem na seguinte.</param>
    /// <returns>O marcador.</returns>
    internal static MarcadorLido Ler(NpgsqlDataReader leitor, int situacao) => leitor.GetInt32(situacao) switch
    {
        0 => new MarcadorLido { Situacao = SituacaoMarcador.Ausente },
        3 => new MarcadorLido { Situacao = SituacaoMarcador.Alheio },
        _ => leitor.IsDBNull(situacao + 1) || !TentarLer(leitor.GetString(situacao + 1), out var identificador, out var gravadoEm)
            ? new MarcadorLido { Situacao = SituacaoMarcador.Invalido }
            : new MarcadorLido { Situacao = SituacaoMarcador.Valido, Identificador = identificador, GravadoEm = gravadoEm },
    };

    // Lê pelas posições fixas e só aceita o texto canônico: o que o Incognito gravaria com o mesmo identificador e data.
    private static bool TentarLer(string texto, out Guid identificador, out DateTimeOffset gravadoEm)
    {
        identificador = Guid.Empty;
        gravadoEm = default;
        const int Inicio = 20;
        if (texto.Length != Tamanho || !texto.StartsWith(Prefixo + Versao, StringComparison.Ordinal) || texto[Inicio + 36] != ':')
        {
            return false;
        }

        if (!Guid.TryParseExact(texto.AsSpan(Inicio, 36), "D", out var lido) || lido == Guid.Empty
            || !DateTimeOffset.TryParseExact(texto.AsSpan(Inicio + 37), FormatoDaData, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var data))
        {
            return false;
        }

        if (!string.Equals(Texto(lido, data), texto, StringComparison.Ordinal))
        {
            return false;
        }

        (identificador, gravadoEm) = (lido, data);
        return true;
    }
}

/// <summary>
/// A gravação do marcador por <c>COMMENT ON DATABASE</c>, que só o dono do banco (ou quem herda o papel dele) pode fazer.
/// Grava só onde não há comentário, confirma e relê numa transação nova.
/// </summary>
internal sealed class MarcadorDestinoPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : MarcadorDestino
{
    private const string Consulta = $"""
        SELECT pg_catalog.current_database(), {FormatoDoMarcador.Colunas}
        FROM pg_catalog.pg_database d
        WHERE d.datname = pg_catalog.current_database()
        """;

    public override async Task GravarAsync(Guid identificador, DateTimeOffset gravadoEm, CancellationToken cancelamento)
    {
        if (identificador == Guid.Empty)
        {
            throw new ArgumentException("O identificador do marcador não pode ser vazio.", nameof(identificador));
        }

        if (gravadoEm.Offset != TimeSpan.Zero || gravadoEm.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException("A data do marcador precisa estar em UTC, sem fração de segundo.", nameof(gravadoEm));
        }

        lancarSeDescartado();
        var texto = FormatoDoMarcador.Texto(identificador, gravadoEm);
        var transacao = await conexao.Conexao.BeginTransactionAsync(cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            string banco;
            await using (var ler = new NpgsqlCommand(Consulta, conexao.Conexao, transacao))
            {
                await using var leitor = await ler.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
                await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
                banco = leitor.GetString(0);
                if (leitor.GetInt32(1) != 0)
                {
                    throw new DialetoRecusouException(
                        CodigosPostgreSql.BancoJaComentado,
                        "O banco do destino já tem um comentário (COMMENT ON DATABASE) ou um marcador: o Incognito nunca o sobrescreve. Apague o comentário do banco antes de preparar o destino.");
                }
            }

            // O COMMENT não aceita parâmetro: o literal sai só do prefixo, da versão, do identificador e da data, sem aspas.
            await using (var gravar = new NpgsqlCommand($"COMMENT ON DATABASE {CitacaoPostgreSql.Citar(banco)} IS '{texto}'", conexao.Conexao, transacao))
            {
                try
                {
                    await gravar.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
                }
                catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.InsufficientPrivilege)
                {
                    throw new DialetoRecusouException(
                        CodigosPostgreSql.MarcadorSemPrivilegio,
                        "Só o dono do banco do destino pode gravar o marcador (COMMENT ON DATABASE): prepare o destino com esse login.");
                }
            }

            await transacao.CommitAsync(cancelamento).ConfigureAwait(false);
        }

        var releitura = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (releitura.ConfigureAwait(false))
        {
            await using var reler = new NpgsqlCommand(Consulta, conexao.Conexao);
            await using var leitor = await reler.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
            var relido = FormatoDoMarcador.Ler(leitor, 1);
            if (relido.Situacao != SituacaoMarcador.Valido || relido.Identificador != identificador || relido.GravadoEm != gravadoEm)
            {
                throw new IncognitoException(CodigosPostgreSql.MarcadorNaoConfere, "O marcador relido do destino, depois de gravado, não confere com o gravado.");
            }
        }
    }
}
