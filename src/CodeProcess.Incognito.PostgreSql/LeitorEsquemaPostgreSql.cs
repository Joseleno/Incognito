using System.Data;
using Npgsql;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A leitura do esquema pelo <c>pg_catalog</c> (nunca o <c>information_schema</c>, que só mostra o que o usuário tem
/// privilégio), numa transação só de leitura em <c>REPEATABLE READ</c>: todas as consultas veem o mesmo instante.
/// </summary>
/// <remarks>
/// Nomes saem das colunas do catálogo, nunca de um cast para <c>regclass</c> (que depende do caminho de busca). Nada que
/// guarde valor de linha é lido: nem estatísticas, comentários, texto de default, limites de partição, expressões de
/// política nem o conteúdo dos large objects.
/// </remarks>
internal sealed class LeitorEsquemaPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : LeitorEsquema
{
    // Os esquemas lidos: fora os do sistema (pg_catalog, pg_toast e os temporários começam com pg_, que o usuário não pode
    // criar), o information_schema e o do diário do Incognito; fora as tabelas temporárias. Pelo prefixo, sem LIKE: o
    // escape do LIKE depende de standard_conforming_strings.
    private const string FiltroDeEsquema = "NOT pg_catalog.starts_with(n.nspname, 'pg_') AND n.nspname NOT IN ('information_schema', 'incognito')";

    private const string Tabelas = $"""
        WITH tabelas AS (
            SELECT c.oid
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'f')
              AND c.relpersistence <> 't'
              AND {FiltroDeEsquema})
        """;

    // A primeira mãe (inhseqno = 1) é a da partição e a da herdeira; herança múltipla aponta a primeira. O
    // inhdetachpending marca a partição com DETACH ... CONCURRENTLY interrompido.
    private const string ConsultaTabelas = Tabelas + """
        SELECT c.oid, n.nspname, c.relname, c.relkind, c.relispartition, c.relrowsecurity, pn.nspname, p.relname, coalesce(i.inhdetachpending, false)
        FROM tabelas
        JOIN pg_catalog.pg_class c ON c.oid = tabelas.oid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_catalog.pg_inherits i ON i.inhrelid = c.oid AND i.inhseqno = 1
        LEFT JOIN pg_catalog.pg_class p ON p.oid = i.inhparent
        LEFT JOIN pg_catalog.pg_namespace pn ON pn.oid = p.relnamespace
        """;

    // O tipo nativo pelo format_type sem o modificador (character varying, não character varying(20)); a extensão dona do
    // tipo pelo pg_depend.
    private const string ConsultaColunasInicio = Tabelas + """
        SELECT a.attrelid, a.attnum, a.attname, pg_catalog.format_type(a.atttypid, NULL), a.attnotnull, a.attgenerated, a.attidentity,
               t.oid, t.typname, t.typtype, t.typsubscript = 'pg_catalog.array_subscript_handler'::pg_catalog.regproc,
               (SELECT e.extname
                FROM pg_catalog.pg_depend d
                JOIN pg_catalog.pg_extension e ON e.oid = d.refobjid
                WHERE d.classid = 'pg_catalog.pg_type'::pg_catalog.regclass AND d.objid = t.oid
                  AND d.refclassid = 'pg_catalog.pg_extension'::pg_catalog.regclass AND d.deptype = 'e'
                LIMIT 1)
        """;

    // No 18, o attnotnull também vale para um NOT NULL ainda não validado, e a coluna pode ter NULL.
    private const string ColunaNaoNuloInvalido18 = """
        ,
               EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k
                       WHERE k.conrelid = a.attrelid AND k.contype = 'n' AND NOT k.convalidated AND a.attnum = ANY (k.conkey))
        """;

    private const string ConsultaColunasFim = """

        FROM tabelas
        JOIN pg_catalog.pg_attribute a ON a.attrelid = tabelas.oid
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        WHERE a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attrelid, a.attnum
        """;

    private const string ConsultaChavesPrimarias = Tabelas + """
        SELECT k.conrelid, a.attname
        FROM pg_catalog.pg_constraint k
        CROSS JOIN LATERAL pg_catalog.unnest(k.conkey) WITH ORDINALITY AS u(coluna, ordem)
        JOIN pg_catalog.pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.coluna
        WHERE k.contype = 'p' AND k.conrelid IN (SELECT oid FROM tabelas)
        ORDER BY k.conrelid, u.ordem
        """;

    // Só as FKs com conparentid = 0: as que uma FK de particionada cria em cada partição (dos dois lados) apontam a
    // original; a declarada direto numa partição também tem 0 e fica, presa à partição.
    private const string ConsultaChavesEstrangeiras = Tabelas + """
        SELECT k.conrelid, k.oid, k.conname, a.attname, rn.nspname, r.relname, ra.attname
        FROM pg_catalog.pg_constraint k
        CROSS JOIN LATERAL ROWS FROM (pg_catalog.unnest(k.conkey), pg_catalog.unnest(k.confkey)) WITH ORDINALITY AS u(coluna, referenciada, ordem)
        JOIN pg_catalog.pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.coluna
        JOIN pg_catalog.pg_class r ON r.oid = k.confrelid
        JOIN pg_catalog.pg_namespace rn ON rn.oid = r.relnamespace
        JOIN pg_catalog.pg_attribute ra ON ra.attrelid = k.confrelid AND ra.attnum = u.referenciada
        WHERE k.contype = 'f' AND k.conparentid = 0 AND k.conrelid IN (SELECT oid FROM tabelas)
        ORDER BY k.conrelid, k.conname, k.oid, u.ordem
        """;

    // As colunas que tiram valor de uma sequence: pelo default (o pg_attrdef depende da sequence) e pela posse do serial e
    // da identity (a sequence depende da coluna).
    private const string ConsultaSequences = $"""
        SELECT s.oid, n.nspname, s.relname, u.tabela, u.coluna
        FROM pg_catalog.pg_class s
        JOIN pg_catalog.pg_namespace n ON n.oid = s.relnamespace
        LEFT JOIN LATERAL (
            SELECT ad.adrelid AS tabela, ad.adnum::integer AS coluna
            FROM pg_catalog.pg_depend d
            JOIN pg_catalog.pg_attrdef ad ON ad.oid = d.objid
            WHERE d.classid = 'pg_catalog.pg_attrdef'::pg_catalog.regclass
              AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid = s.oid
            UNION
            SELECT d.refobjid, d.refobjsubid
            FROM pg_catalog.pg_depend d
            WHERE d.classid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.objid = s.oid
              AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.deptype IN ('a', 'i') AND d.refobjsubid > 0
        ) u ON true
        WHERE s.relkind = 'S' AND {FiltroDeEsquema}
        """;

    // Os large objects pelos metadados, legíveis por todos; o conteúdo (pg_largeobject) nunca.
    private const string ConsultaResiduais = $"""
        SELECT n.nspname, c.relname
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'm' AND {FiltroDeEsquema};
        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_largeobject_metadata)
        """;

    private static readonly DialetoSqlPostgreSql _sql = new();

    // Um objeto só para os large objects do banco inteiro.
    private static readonly NomeObjeto _objetosGrandes = new("pg_catalog", "pg_largeobject");

    public override async Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        lancarSeDescartado();
        var transacao = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            var tabelas = await LerTabelasAsync(cancelamento).ConfigureAwait(false);
            var colunas = await LerColunasAsync(tabelas, cancelamento).ConfigureAwait(false);
            var chavesPrimarias = await LerChavesPrimariasAsync(cancelamento).ConfigureAwait(false);
            var chavesEstrangeiras = await LerChavesEstrangeirasAsync(cancelamento).ConfigureAwait(false);
            var sequences = await LerSequencesAsync(tabelas, colunas, cancelamento).ConfigureAwait(false);
            var residuais = await LerResiduaisAsync(cancelamento).ConfigureAwait(false);

            return new EsquemaBanco
            {
                Tabelas = tabelas.Values
                    .Select(tabela => new TabelaEsquema
                    {
                        Nome = tabela.Nome,
                        Tipo = tabela.Tipo,
                        Pai = tabela.Pai,
                        Colunas = colunas.TryGetValue(tabela.Oid, out var dela) ? dela.Select(coluna => coluna.Modelo).ToArray() : [],
                        ChavePrimaria = chavesPrimarias.TryGetValue(tabela.Oid, out var chave) ? chave : [],
                        ChavesEstrangeiras = chavesEstrangeiras.TryGetValue(tabela.Oid, out var estrangeiras) ? estrangeiras : [],
                        SegurancaDeLinha = tabela.SegurancaDeLinha,
                    })
                    .OrderBy(tabela => tabela.Nome, OrdemDeNomes.Instancia)
                    .ToArray(),
                Sequences = sequences,
                ObjetosResiduais = residuais,
            };
        }
    }

    private async Task<Dictionary<uint, TabelaLida>> LerTabelasAsync(CancellationToken cancelamento)
    {
        var tabelas = new Dictionary<uint, TabelaLida>();
        await using var comando = new NpgsqlCommand(ConsultaTabelas, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tipoDeRelacao = leitor.GetChar(3);
            var particao = leitor.GetBoolean(4);
            var pai = leitor.IsDBNull(6) ? null : new NomeObjeto(leitor.GetString(6), leitor.GetString(7));
            var nome = new NomeObjeto(leitor.GetString(1), leitor.GetString(2));

            // A leitura pelo pai já não enxerga as linhas dela, e ela ainda é partição: copiada pelo pai, as linhas sumiriam.
            if (leitor.GetBoolean(8))
            {
                throw new IncognitoException(
                    CodigosPostgreSql.ParticaoPendente,
                    $"A partição {nome} tem um DETACH ... CONCURRENTLY interrompido; termine-o com ALTER TABLE {_sql.Citar(pai!)} DETACH PARTITION {_sql.Citar(nome)} FINALIZE antes.");
            }

            // A partição intermediária (particionada que é partição de outra) é partição: o pai é o imediato; a raiz, o
            // motor calcula.
            var tipo = particao ? TipoTabela.Particao
                : tipoDeRelacao == 'p' ? TipoTabela.Particionada
                : pai is not null ? TipoTabela.Herdeira
                : TipoTabela.Comum;
            var oid = leitor.GetFieldValue<uint>(0);
            tabelas.Add(oid, new TabelaLida(oid, nome, tipo, pai, tipoDeRelacao == 'f', leitor.GetBoolean(5)));
        }

        return tabelas;
    }

    private async Task<Dictionary<uint, List<ColunaLida>>> LerColunasAsync(Dictionary<uint, TabelaLida> tabelas, CancellationToken cancelamento)
    {
        var dezoito = conexao.Versao >= 180000;
        var consulta = ConsultaColunasInicio + (dezoito ? ColunaNaoNuloInvalido18 : "") + ConsultaColunasFim;
        var colunas = new Dictionary<uint, List<ColunaLida>>();
        await using var comando = new NpgsqlCommand(consulta, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tabela = leitor.GetFieldValue<uint>(0);
            var geracao = leitor.GetChar(5) switch
            {
                's' => GeracaoColuna.Armazenada,
                'v' => GeracaoColuna.Virtual,
                _ => GeracaoColuna.Nenhuma,
            };
            var identidade = leitor.GetChar(6) switch
            {
                'a' => IdentidadeColuna.Sempre,
                'd' => IdentidadeColuna.PorPadrao,
                _ => IdentidadeColuna.Nenhuma,
            };
            var tipo = new TipoLido(
                leitor.GetFieldValue<uint>(7),
                leitor.GetString(8),
                leitor.GetChar(9),
                leitor.GetBoolean(10),
                leitor.IsDBNull(11) ? null : leitor.GetString(11));
            var (situacao, canonico) = ClassificacaoDeTipos.Classificar(tipo, geracao, tabelas[tabela].Estrangeira);

            // Anulável se a coluna não tem NOT NULL validado. O NOT NULL de um domínio não conta: o banco aceita NULL numa
            // coluna de domínio NOT NULL em casos documentados (subconsulta vazia, junção externa).
            var naoNulo = leitor.GetBoolean(4) && !(dezoito && leitor.GetBoolean(12));
            var coluna = new ColunaEsquema
            {
                Nome = leitor.GetString(2),
                TipoNativo = leitor.GetString(3),
                Situacao = situacao,
                Tipo = canonico,
                Anulavel = !naoNulo,
                Geracao = geracao,
                Identidade = identidade,
            };
            if (!colunas.TryGetValue(tabela, out var daTabela))
            {
                colunas[tabela] = daTabela = [];
            }

            daTabela.Add(new ColunaLida(leitor.GetInt16(1), coluna));
        }

        return colunas;
    }

    private async Task<Dictionary<uint, List<string>>> LerChavesPrimariasAsync(CancellationToken cancelamento)
    {
        var chaves = new Dictionary<uint, List<string>>();
        await using var comando = new NpgsqlCommand(ConsultaChavesPrimarias, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tabela = leitor.GetFieldValue<uint>(0);
            if (!chaves.TryGetValue(tabela, out var colunas))
            {
                chaves[tabela] = colunas = [];
            }

            colunas.Add(leitor.GetString(1));
        }

        return chaves;
    }

    private async Task<Dictionary<uint, List<ChaveEstrangeira>>> LerChavesEstrangeirasAsync(CancellationToken cancelamento)
    {
        var lidas = new List<(uint Tabela, uint Restricao, string Nome, string Coluna, NomeObjeto Referenciada, string ColunaReferenciada)>();
        await using (var comando = new NpgsqlCommand(ConsultaChavesEstrangeiras, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                lidas.Add((
                    leitor.GetFieldValue<uint>(0),
                    leitor.GetFieldValue<uint>(1),
                    leitor.GetString(2),
                    leitor.GetString(3),
                    new NomeObjeto(leitor.GetString(4), leitor.GetString(5)),
                    leitor.GetString(6)));
            }
        }

        // A consulta já vem na ordem da chave dentro de cada restrição.
        return lidas
            .GroupBy(lida => (lida.Tabela, lida.Restricao))
            .GroupBy(restricao => restricao.Key.Tabela)
            .ToDictionary(
                tabela => tabela.Key,
                tabela => tabela.Select(restricao => new ChaveEstrangeira
                {
                    Nome = restricao.First().Nome,
                    Colunas = restricao.Select(lida => lida.Coluna).ToArray(),
                    Referenciada = restricao.First().Referenciada,
                    ColunasReferenciadas = restricao.Select(lida => lida.ColunaReferenciada).ToArray(),
                }).OrderBy(chave => chave.Nome, StringComparer.Ordinal).ToList());
    }

    private async Task<SequenceEsquema[]> LerSequencesAsync(
        Dictionary<uint, TabelaLida> tabelas, Dictionary<uint, List<ColunaLida>> colunas, CancellationToken cancelamento)
    {
        var lidas = new Dictionary<uint, (NomeObjeto Nome, List<ReferenciaColuna> Colunas)>();
        await using (var comando = new NpgsqlCommand(ConsultaSequences, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var oid = leitor.GetFieldValue<uint>(0);
                if (!lidas.TryGetValue(oid, out var sequence))
                {
                    lidas[oid] = sequence = (new NomeObjeto(leitor.GetString(1), leitor.GetString(2)), []);
                }

                if (leitor.IsDBNull(3))
                {
                    continue;
                }

                // O default copiado para cada partição depende da mesma sequence: só a coluna da particionada conta. Coluna
                // de tabela fora da leitura também fica de fora.
                var tabela = leitor.GetFieldValue<uint>(3);
                var numero = leitor.GetInt32(4);
                if (tabelas.TryGetValue(tabela, out var lida) && lida.Tipo != TipoTabela.Particao
                    && colunas.TryGetValue(tabela, out var daTabela) && daTabela.Find(coluna => coluna.Numero == numero) is { } coluna)
                {
                    var referencia = new ReferenciaColuna(lida.Nome, coluna.Modelo.Nome);
                    if (!sequence.Colunas.Contains(referencia))
                    {
                        sequence.Colunas.Add(referencia);
                    }
                }
            }
        }

        return lidas.Values
            .Select(sequence => new SequenceEsquema
            {
                Nome = sequence.Nome,
                Colunas = sequence.Colunas
                    .OrderBy(coluna => coluna.Tabela, OrdemDeNomes.Instancia)
                    .ThenBy(coluna => coluna.Coluna, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(sequence => sequence.Nome, OrdemDeNomes.Instancia)
            .ToArray();
    }

    private async Task<ObjetoResidual[]> LerResiduaisAsync(CancellationToken cancelamento)
    {
        var residuais = new List<ObjetoResidual>();
        await using var comando = new NpgsqlCommand(ConsultaResiduais, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            residuais.Add(new ObjetoResidual { Nome = new NomeObjeto(leitor.GetString(0), leitor.GetString(1)), Tipo = TipoObjetoResidual.VisaoMaterializada });
        }

        await leitor.NextResultAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        if (leitor.GetBoolean(0))
        {
            residuais.Add(new ObjetoResidual { Nome = _objetosGrandes, Tipo = TipoObjetoResidual.ObjetosGrandes });
        }

        return residuais.OrderBy(objeto => objeto.Nome, OrdemDeNomes.Instancia).ToArray();
    }

    private sealed record TabelaLida(uint Oid, NomeObjeto Nome, TipoTabela Tipo, NomeObjeto? Pai, bool Estrangeira, bool SegurancaDeLinha);

    private sealed record ColunaLida(short Numero, ColunaEsquema Modelo);
}

/// <summary>A ordem ordinal por esquema e nome, a do contrato (independente da colação do banco).</summary>
internal sealed class OrdemDeNomes : IComparer<NomeObjeto>
{
    internal static readonly OrdemDeNomes Instancia = new();

    public int Compare(NomeObjeto? x, NomeObjeto? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        var esquema = string.CompareOrdinal(x.Esquema, y.Esquema);
        return esquema != 0 ? esquema : string.CompareOrdinal(x.Nome, y.Nome);
    }
}

/// <summary>Uma transação só de leitura em <c>REPEATABLE READ</c>, desfeita no fim (nada foi escrito).</summary>
internal sealed class TransacaoDeLeitura : IAsyncDisposable
{
    private readonly NpgsqlTransaction _transacao;

    private TransacaoDeLeitura(NpgsqlTransaction transacao) => _transacao = transacao;

    internal static async Task<TransacaoDeLeitura> AbrirAsync(NpgsqlConnection conexao, CancellationToken cancelamento)
    {
        var transacao = await conexao.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancelamento).ConfigureAwait(false);
        try
        {
            // Explícita em toda leitura de metadados, também no destino, além do padrão da sessão da origem.
            await using var comando = new NpgsqlCommand("SET TRANSACTION READ ONLY", conexao, transacao);
            await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
            return new TransacaoDeLeitura(transacao);
        }
        catch
        {
            await transacao.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // O descarte sem confirmação desfaz a transação.
    public ValueTask DisposeAsync() => _transacao.DisposeAsync();
}
