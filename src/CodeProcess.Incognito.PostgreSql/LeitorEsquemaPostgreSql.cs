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
/// política nem o conteúdo dos large objects. Das expressões da chave de partição só saem, extraídos no servidor, os
/// números das colunas citadas.
/// </remarks>
internal sealed class LeitorEsquemaPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : LeitorEsquema
{
    // Os esquemas lidos: fora os do sistema (pg_catalog, pg_toast e os temporários começam com pg_, que o usuário não pode
    // criar; as tabelas temporárias ficam de fora por eles), o information_schema e o do diário do Incognito. Pelo prefixo,
    // sem LIKE: o escape do LIKE depende de standard_conforming_strings.
    private const string FiltroDeEsquema = "NOT pg_catalog.starts_with(n.nspname, 'pg_') AND n.nspname NOT IN ('information_schema', 'incognito')";

    private const string Tabelas = $"""
        WITH tabelas AS (
            SELECT c.oid
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'f') AND {FiltroDeEsquema})
        """;

    // A primeira mãe é a de menor inhseqno (o banco não renumera: com NO INHERIT da primeira, as outras guardam 2, 3...),
    // a da partição e a da herdeira; as outras mães da herança múltipla saem à parte. O inhdetachpending marca a partição
    // com DETACH ... CONCURRENTLY interrompido. A extensão que adotou a tabela, pelo pg_depend.
    private const string ConsultaTabelas = Tabelas + """
        SELECT c.oid, n.nspname, c.relname, c.relkind, c.relispartition, c.relrowsecurity, pn.nspname, p.relname, coalesce(i.inhdetachpending, false),
               (SELECT e.extname
                FROM pg_catalog.pg_depend d
                JOIN pg_catalog.pg_extension e ON e.oid = d.refobjid
                WHERE d.classid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.objid = c.oid
                  AND d.refclassid = 'pg_catalog.pg_extension'::pg_catalog.regclass AND d.deptype = 'e'
                LIMIT 1)
        FROM tabelas
        JOIN pg_catalog.pg_class c ON c.oid = tabelas.oid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN LATERAL (
            SELECT m.inhparent, m.inhdetachpending
            FROM pg_catalog.pg_inherits m
            WHERE m.inhrelid = c.oid
            ORDER BY m.inhseqno
            LIMIT 1) i ON true
        LEFT JOIN pg_catalog.pg_class p ON p.oid = i.inhparent
        LEFT JOIN pg_catalog.pg_namespace pn ON pn.oid = p.relnamespace
        """;

    private const string ConsultaOutrosPais = Tabelas + """
        SELECT i.inhrelid, pn.nspname, p.relname
        FROM pg_catalog.pg_inherits i
        JOIN pg_catalog.pg_class p ON p.oid = i.inhparent
        JOIN pg_catalog.pg_namespace pn ON pn.oid = p.relnamespace
        WHERE i.inhrelid IN (SELECT oid FROM tabelas)
          AND i.inhseqno > (SELECT pg_catalog.min(m.inhseqno) FROM pg_catalog.pg_inherits m WHERE m.inhrelid = i.inhrelid)
        ORDER BY i.inhrelid, i.inhseqno
        """;

    // As colunas da chave de partição: as que são elementos da chave, com a posição, e as citadas pelas expressões, sem
    // posição. Da expressão sai só o número de cada coluna citada (o varattno dos nós Var), extraído no servidor: nenhum
    // texto da expressão sai do banco, e 0 é a linha inteira.
    private const string ConsultaChavesDeParticao = Tabelas + """
        SELECT pt.partrelid, k.coluna::integer, k.ordem::integer
        FROM pg_catalog.pg_partitioned_table pt
        CROSS JOIN LATERAL pg_catalog.unnest(pt.partattrs::pg_catalog.int2[]) WITH ORDINALITY AS k(coluna, ordem)
        WHERE pt.partrelid IN (SELECT oid FROM tabelas) AND k.coluna <> 0
        UNION ALL
        SELECT pt.partrelid, e.citada[1]::integer, NULL
        FROM pg_catalog.pg_partitioned_table pt
        CROSS JOIN LATERAL pg_catalog.regexp_matches(pt.partexprs::text, ':varattno (-?[0-9]+)', 'g') AS e(citada)
        WHERE pt.partrelid IN (SELECT oid FROM tabelas)
        """;

    // O tipo nativo pelo format_type sem o modificador (character varying, não character varying(20)); a extensão dona do
    // tipo pelo pg_depend. A cadeia do tipo, do domínio ao tipo base e do array ao elemento: o tipo base (o fim dos
    // domínios) e o fim da cadeia inteira. E o tipo da coluna sem default próprio, porque o default do domínio vale para
    // ela: só o do tipo da coluna (o domínio criado sobre outro copia o default dele na criação, e o banco não sobe a
    // cadeia).
    private const string ConsultaColunasInicio = Tabelas + """
        SELECT a.attrelid, a.attnum, a.attname, pg_catalog.format_type(a.atttypid, NULL), a.attnotnull, a.attgenerated, a.attidentity,
               t.oid, t.typname, t.typtype,
               (SELECT e.extname
                FROM pg_catalog.pg_depend d
                JOIN pg_catalog.pg_extension e ON e.oid = d.refobjid
                WHERE d.classid = 'pg_catalog.pg_type'::pg_catalog.regclass AND d.objid = t.oid
                  AND d.refclassid = 'pg_catalog.pg_extension'::pg_catalog.regclass AND d.deptype = 'e'
                LIMIT 1),
               CASE WHEN t.typtype = 'd' THEN pg_catalog.format_type(k.base, NULL) END, k.final,
               CASE WHEN NOT a.atthasdef THEN t.oid END
        """;

    // No 18, o attnotnull também vale para um NOT NULL ainda não validado, e a coluna pode ter NULL; antes do 18 não há
    // NOT NULL em pg_constraint, e a cláusula dá falso.
    private const string ColunaNaoNuloInvalido = """
        ,
               EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k
                       WHERE k.conrelid = a.attrelid AND k.contype = 'n' AND NOT k.convalidated AND a.attnum = ANY (k.conkey))
        """;

    private const string ConsultaColunasFim = """

        FROM tabelas
        JOIN pg_catalog.pg_attribute a ON a.attrelid = tabelas.oid
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        CROSS JOIN LATERAL (
            WITH RECURSIVE cadeia (tipo, passo, so_dominios) AS (
                SELECT a.atttypid, 0, true
                UNION ALL
                SELECT CASE WHEN b.typtype = 'd' THEN b.typbasetype ELSE b.typelem END, c.passo + 1, c.so_dominios AND b.typtype = 'd'
                FROM cadeia c
                JOIN pg_catalog.pg_type b ON b.oid = c.tipo
                WHERE b.typtype = 'd' OR b.typsubscript = 'pg_catalog.array_subscript_handler'::pg_catalog.regproc
            )
            SELECT (SELECT c.tipo FROM cadeia c WHERE c.so_dominios ORDER BY c.passo DESC LIMIT 1) AS base,
                   (SELECT c.tipo FROM cadeia c ORDER BY c.passo DESC LIMIT 1) AS final
        ) k
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

    // As colunas que tiram valor de uma sequence: pelo default da coluna (o pg_attrdef depende da sequence), pelo default
    // de um domínio (o pg_type depende dela; vale para as colunas sem default próprio) e pela identity (a sequence depende
    // da coluna, internamente). O OWNED BY sozinho não conta: sem default, a coluna não tira valor da sequence.
    private const string ConsultaSequences = $"""
        SELECT s.oid, n.nspname, s.relname, u.tabela, u.coluna, u.dominio
        FROM pg_catalog.pg_class s
        JOIN pg_catalog.pg_namespace n ON n.oid = s.relnamespace
        LEFT JOIN LATERAL (
            SELECT ad.adrelid AS tabela, ad.adnum::integer AS coluna, NULL::pg_catalog.oid AS dominio
            FROM pg_catalog.pg_depend d
            JOIN pg_catalog.pg_attrdef ad ON ad.oid = d.objid
            WHERE d.classid = 'pg_catalog.pg_attrdef'::pg_catalog.regclass
              AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid = s.oid
            UNION
            SELECT d.refobjid, d.refobjsubid, NULL
            FROM pg_catalog.pg_depend d
            WHERE d.classid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.objid = s.oid
              AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.deptype = 'i'
            UNION
            SELECT NULL, NULL, d.objid
            FROM pg_catalog.pg_depend d
            WHERE d.classid = 'pg_catalog.pg_type'::pg_catalog.regclass
              AND d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid = s.oid
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
            var outrosPais = await LerOutrosPaisAsync(cancelamento).ConfigureAwait(false);
            var colunas = await LerColunasAsync(tabelas, cancelamento).ConfigureAwait(false);
            var chavesDeParticao = await LerChavesDeParticaoAsync(tabelas, colunas, cancelamento).ConfigureAwait(false);
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
                        OutrosPais = outrosPais.TryGetValue(tabela.Oid, out var maes) ? maes : [],
                        ChaveParticao = chavesDeParticao.GetValueOrDefault(tabela.Oid),
                        Estrangeira = tabela.Estrangeira,
                        Extensao = tabela.Extensao,
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
            var extensao = leitor.IsDBNull(9) ? null : leitor.GetString(9);
            tabelas.Add(oid, new TabelaLida(oid, nome, tipo, pai, tipoDeRelacao == 'f', tipoDeRelacao == 'p', leitor.GetBoolean(5), extensao));
        }

        return tabelas;
    }

    private async Task<Dictionary<uint, List<NomeObjeto>>> LerOutrosPaisAsync(CancellationToken cancelamento)
    {
        var outrosPais = new Dictionary<uint, List<NomeObjeto>>();
        await using var comando = new NpgsqlCommand(ConsultaOutrosPais, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tabela = leitor.GetFieldValue<uint>(0);
            if (!outrosPais.TryGetValue(tabela, out var maes))
            {
                outrosPais[tabela] = maes = [];
            }

            maes.Add(new NomeObjeto(leitor.GetString(1), leitor.GetString(2)));
        }

        return outrosPais;
    }

    // Toda particionada (também a intermediária) tem chave. Primeiro os elementos da chave, na ordem; depois as colunas das
    // expressões, na ordem do catálogo. A linha inteira (0) ou um número que não é coluna da tabela deixa a chave incompleta;
    // a particionada pode não ter coluna nenhuma (a chave pela linha inteira não impede o DROP COLUMN).
    private async Task<Dictionary<uint, ChaveParticao>> LerChavesDeParticaoAsync(
        Dictionary<uint, TabelaLida> tabelas, Dictionary<uint, List<ColunaLida>> colunas, CancellationToken cancelamento)
    {
        var citadas = new List<(uint Tabela, int Coluna, int? Ordem)>();
        await using (var comando = new NpgsqlCommand(ConsultaChavesDeParticao, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                citadas.Add((leitor.GetFieldValue<uint>(0), leitor.GetInt32(1), leitor.IsDBNull(2) ? null : leitor.GetInt32(2)));
            }
        }

        var chaves = new Dictionary<uint, ChaveParticao>();
        foreach (var tabela in tabelas.Values.Where(tabela => tabela.Particionada))
        {
            var daTabela = colunas.GetValueOrDefault(tabela.Oid) ?? [];
            var numeros = citadas.Where(citada => citada.Tabela == tabela.Oid && citada.Ordem is not null).OrderBy(citada => citada.Ordem).Select(citada => citada.Coluna)
                .Concat(citadas.Where(citada => citada.Tabela == tabela.Oid && citada.Ordem is null).Select(citada => citada.Coluna).Order())
                .Distinct()
                .ToArray();
            var nomes = numeros.Select(numero => daTabela.Find(coluna => coluna.Numero == numero)?.Modelo.Nome).ToArray();
            chaves.Add(tabela.Oid, new ChaveParticao
            {
                Colunas = nomes.OfType<string>().ToArray(),
                ColunasIncompletas = numeros.Length == 0 || nomes.Contains(null),
            });
        }

        return chaves;
    }

    private async Task<Dictionary<uint, List<ColunaLida>>> LerColunasAsync(Dictionary<uint, TabelaLida> tabelas, CancellationToken cancelamento)
    {
        var colunas = new Dictionary<uint, List<ColunaLida>>();
        await using var comando = new NpgsqlCommand(ConsultaColunasInicio + ColunaNaoNuloInvalido + ConsultaColunasFim, conexao.Conexao);
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
                leitor.IsDBNull(10) ? null : leitor.GetString(10),
                leitor.GetFieldValue<uint>(12));
            var (situacao, canonico) = ClassificacaoDeTipos.Classificar(tipo, geracao, tabelas[tabela].Estrangeira);

            // Anulável se a coluna não tem NOT NULL validado. O NOT NULL de um domínio não conta: o banco aceita NULL numa
            // coluna de domínio NOT NULL em casos documentados (subconsulta vazia, junção externa).
            var naoNulo = leitor.GetBoolean(4) && !leitor.GetBoolean(14);
            var coluna = new ColunaEsquema
            {
                Nome = leitor.GetString(2),
                TipoNativo = leitor.GetString(3),
                Situacao = situacao,
                Tipo = canonico,
                Anulavel = !naoNulo,
                Geracao = geracao,
                Identidade = identidade,
                TipoBase = leitor.IsDBNull(11) ? null : leitor.GetString(11),
            };
            if (!colunas.TryGetValue(tabela, out var daTabela))
            {
                colunas[tabela] = daTabela = [];
            }

            daTabela.Add(new ColunaLida(leitor.GetInt16(1), coluna, leitor.IsDBNull(13) ? null : leitor.GetFieldValue<uint>(13)));
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

        // A consulta já vem na ordem do nome e, dentro de cada restrição, na ordem da chave.
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
                }).ToList());
    }

    private async Task<SequenceEsquema[]> LerSequencesAsync(
        Dictionary<uint, TabelaLida> tabelas, Dictionary<uint, List<ColunaLida>> colunas, CancellationToken cancelamento)
    {
        // As colunas que usam cada sequence, antes de tirar as das partições; só colunas das tabelas lidas.
        var nomes = new Dictionary<uint, NomeObjeto>();
        var usos = new HashSet<(uint Sequence, uint Tabela, string Coluna)>();
        await using (var comando = new NpgsqlCommand(ConsultaSequences, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var oid = leitor.GetFieldValue<uint>(0);
                nomes.TryAdd(oid, new NomeObjeto(leitor.GetString(1), leitor.GetString(2)));
                if (!leitor.IsDBNull(3))
                {
                    var tabela = leitor.GetFieldValue<uint>(3);
                    var numero = leitor.GetInt32(4);
                    if (colunas.GetValueOrDefault(tabela)?.Find(coluna => coluna.Numero == numero) is { } coluna)
                    {
                        usos.Add((oid, tabela, coluna.Modelo.Nome));
                    }
                }
                else if (!leitor.IsDBNull(5))
                {
                    // O default do domínio vale para as colunas dele sem default próprio.
                    var dominio = leitor.GetFieldValue<uint>(5);
                    foreach (var (tabela, daTabela) in colunas)
                    {
                        foreach (var coluna in daTabela.Where(coluna => coluna.TipoSemDefault == dominio))
                        {
                            usos.Add((oid, tabela, coluna.Modelo.Nome));
                        }
                    }
                }
            }
        }

        // O default copiado do pai para a partição usa a mesma sequence: só a coluna do pai conta. A partição com sequence
        // própria (anexada já com ela) aparece.
        var porNome = tabelas.Values.ToDictionary(tabela => tabela.Nome);
        bool CopiaDoPai((uint Sequence, uint Tabela, string Coluna) uso) =>
            tabelas[uso.Tabela] is { Tipo: TipoTabela.Particao, Pai: { } pai } && porNome.TryGetValue(pai, out var doPai)
            && usos.Contains((uso.Sequence, doPai.Oid, uso.Coluna));

        return nomes
            .Select(sequence => new SequenceEsquema
            {
                Nome = sequence.Value,
                Colunas = usos
                    .Where(uso => uso.Sequence == sequence.Key && !CopiaDoPai(uso))
                    .Select(uso => new ReferenciaColuna(tabelas[uso.Tabela].Nome, uso.Coluna))
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

    private sealed record TabelaLida(
        uint Oid, NomeObjeto Nome, TipoTabela Tipo, NomeObjeto? Pai, bool Estrangeira, bool Particionada, bool SegurancaDeLinha, string? Extensao);

    // O tipo da coluna, se ela não tem default próprio: o default do domínio, se há, vale para ela.
    private sealed record ColunaLida(short Numero, ColunaEsquema Modelo, uint? TipoSemDefault);
}

/// <summary>A ordem ordinal por esquema e nome, a do contrato (independente da colação do banco).</summary>
internal sealed class OrdemDeNomes : IComparer<NomeObjeto>
{
    internal static readonly OrdemDeNomes Instancia = new();

    // Nulo antes de tudo, pelo CompareOrdinal.
    public int Compare(NomeObjeto? x, NomeObjeto? y)
    {
        var esquema = string.CompareOrdinal(x?.Esquema, y?.Esquema);
        return esquema != 0 ? esquema : string.CompareOrdinal(x?.Nome, y?.Nome);
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
