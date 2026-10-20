using System.Data;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// A leitura do esquema pelo <c>pg_catalog</c> (nunca o <c>information_schema</c>, que só mostra o que o usuário tem
/// privilégio), numa transação só de leitura em <c>REPEATABLE READ</c>: todas as consultas veem o mesmo instante.
/// </summary>
/// <remarks>
/// Nomes saem das colunas do catálogo, nunca de um cast para <c>regclass</c> (que depende do caminho de busca). Nada que
/// guarde valor de linha é lido: nem estatísticas, comentários, texto de default, limites de partição, expressões de
/// política, locale e regras de colação nem o conteúdo dos large objects. Das expressões da chave de partição só saem,
/// extraídos no servidor, os números das colunas citadas; das colunas geradas, as citadas, pelo <c>pg_depend</c>. O
/// comportamento de uma colação não determinística é medido no servidor com textos sintéticos, nunca deduzido do locale.
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
    // domínios), o fim da cadeia inteira, o primeiro modificador que não é -1 (o da coluna, depois o de cada domínio: o
    // domínio de domínio e a coluna de domínio guardam -1) e se algum domínio da cadeia tem CHECK. A colação da coluna já
    // é a efetiva (a do domínio ou a do COLLATE). E o tipo da coluna sem default próprio, porque o default do domínio vale
    // para ela: só o do tipo da coluna (o domínio criado sobre outro copia o default dele na criação, e o banco não sobe a
    // cadeia).
    private const string ConsultaColunasInicio = Tabelas + """
        SELECT a.attrelid AS tabela, a.attnum AS numero, a.attname AS nome, pg_catalog.format_type(a.atttypid, NULL) AS tipo_nativo,
               a.attnotnull AS nao_nulo, a.attgenerated AS geracao, a.attidentity AS identidade, a.attcollation AS colacao,
               t.oid AS tipo, t.typname AS nome_do_tipo, t.typtype AS categoria,
               (SELECT e.extname
                FROM pg_catalog.pg_depend d
                JOIN pg_catalog.pg_extension e ON e.oid = d.refobjid
                WHERE d.classid = 'pg_catalog.pg_type'::pg_catalog.regclass AND d.objid = t.oid
                  AND d.refclassid = 'pg_catalog.pg_extension'::pg_catalog.regclass AND d.deptype = 'e'
                LIMIT 1) AS extensao,
               CASE WHEN t.typtype = 'd' THEN pg_catalog.format_type(k.base, NULL) END AS tipo_base, k.final AS final,
               f.typname = 'citext' AND f.typtype = 'b' AND EXISTS (
                   SELECT 1
                   FROM pg_catalog.pg_depend d
                   JOIN pg_catalog.pg_extension e ON e.oid = d.refobjid
                   WHERE d.classid = 'pg_catalog.pg_type'::pg_catalog.regclass AND d.objid = f.oid
                     AND d.refclassid = 'pg_catalog.pg_extension'::pg_catalog.regclass AND d.deptype = 'e' AND e.extname = 'citext') AS final_citext,
               k.modificador AS modificador, k.restrita AS restrita,
               CASE WHEN NOT a.atthasdef THEN t.oid END AS tipo_sem_default
        """;

    // No 18, o attnotnull também vale para um NOT NULL ainda não validado, e a coluna pode ter NULL; antes do 18 não há
    // NOT NULL em pg_constraint, e a cláusula dá falso.
    private const string ColunaNaoNuloInvalido = """
        ,
               EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k
                       WHERE k.conrelid = a.attrelid AND k.contype = 'n' AND NOT k.convalidated AND a.attnum = ANY (k.conkey)) AS nao_nulo_invalido
        """;

    private const string ConsultaColunasFim = """

        FROM tabelas
        JOIN pg_catalog.pg_attribute a ON a.attrelid = tabelas.oid
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        CROSS JOIN LATERAL (
            WITH RECURSIVE cadeia (tipo, passo, so_dominios, modificador) AS (
                SELECT a.atttypid, 0, true, a.atttypmod
                UNION ALL
                SELECT CASE WHEN b.typtype = 'd' THEN b.typbasetype ELSE b.typelem END, c.passo + 1, c.so_dominios AND b.typtype = 'd',
                       CASE WHEN c.modificador <> -1 THEN c.modificador WHEN b.typtype = 'd' THEN b.typtypmod ELSE -1 END
                FROM cadeia c
                JOIN pg_catalog.pg_type b ON b.oid = c.tipo
                WHERE b.typtype = 'd' OR b.typsubscript = 'pg_catalog.array_subscript_handler'::pg_catalog.regproc
            )
            SELECT (SELECT c.tipo FROM cadeia c WHERE c.so_dominios ORDER BY c.passo DESC LIMIT 1) AS base,
                   (SELECT c.tipo FROM cadeia c ORDER BY c.passo DESC LIMIT 1) AS final,
                   (SELECT c.modificador FROM cadeia c ORDER BY c.passo DESC LIMIT 1) AS modificador,
                   EXISTS (SELECT 1 FROM cadeia c JOIN pg_catalog.pg_constraint r ON r.contypid = c.tipo WHERE r.contype = 'c') AS restrita
        ) k
        JOIN pg_catalog.pg_type f ON f.oid = k.final
        WHERE a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attrelid, a.attnum
        """;

    // As colunas que uma coluna gerada cita, pelas dependências da expressão dela (pg_attrdef): uma normal ('n') sobre cada
    // coluna citada da própria tabela. A expressão (adbin) nunca é lida.
    private const string ConsultaReferenciasDaGeracao = Tabelas + """
        SELECT ad.adrelid AS tabela, ad.adnum AS coluna, d.refobjsubid AS citada
        FROM pg_catalog.pg_attrdef ad
        JOIN pg_catalog.pg_depend d ON d.classid = 'pg_catalog.pg_attrdef'::pg_catalog.regclass AND d.objid = ad.oid
        WHERE d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid = ad.adrelid AND d.deptype = 'n' AND d.refobjsubid > 0
          AND ad.adrelid IN (SELECT oid FROM tabelas)
        """;

    // A codificação pelo número (0 é SQL_ASCII, 6 é UTF8) e o nome dela, de um conjunto fechado; o limite do tipo name. Nunca
    // o locale do banco: só o provedor e, no 16+, se há regras.
    private const string ConsultaBancoInicio = """
        SELECT pg_catalog.pg_encoding_to_char(d.encoding) AS codificacao, d.encoding AS numero, pg_catalog.pg_encoding_max_length(d.encoding) AS bytes,
               d.datlocprovider AS provedor, pg_catalog.current_setting('max_identifier_length')::integer AS maximo_identificador
        """;

    private const string BancoRegras16 = ", d.daticurules IS NOT NULL AS regras";

    private const string BancoRegras15 = ", false AS regras";

    private const string ConsultaBancoFim = """

        FROM pg_catalog.pg_database d
        WHERE d.datname = pg_catalog.current_database()
        """;

    // As colações usadas pelas colunas: nome, esquema e se o login tem USAGE no esquema (sem ele, a medida falharia e
    // abortaria a transação). Nunca o locale.
    private const string ConsultaColacoesInicio = """
        SELECT k.oid AS oid, k.collname AS nome, n.nspname AS esquema, k.collprovider AS provedor, k.collisdeterministic AS deterministica,
               pg_catalog.has_schema_privilege(k.collnamespace, 'USAGE') AS acesso
        """;

    private const string ColacaoRegras16 = ", k.collicurules IS NOT NULL AS regras";

    private const string ColacaoRegras15 = ", false AS regras";

    private const string ConsultaColacoesFim = """

        FROM pg_catalog.pg_collation k
        JOIN pg_catalog.pg_namespace n ON n.oid = k.collnamespace
        WHERE k.oid = ANY ($1)
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

    // Os números das codificações (pg_wchar.h): sem codificação declarada e UTF-8.
    private const int SqlAscii = 0;
    private const int Utf8 = 6;

    private static readonly DialetoSqlPostgreSql _sql = new();

    // Os provedores de locale do banco que o dialeto conhece: libc, ICU e, no 17+, o embutido; as colações têm também o 'd',
    // a padrão do banco.
    private static readonly char[] _provedoresDoBanco = ['c', 'i', 'b'];
    private static readonly char[] _provedoresDeColacao = ['d', 'c', 'i', 'b'];

    // Um objeto só para os large objects do banco inteiro.
    private static readonly NomeObjeto _objetosGrandes = new("pg_catalog", "pg_largeobject");

    public override async Task<EsquemaBanco> LerAsync(CancellationToken cancelamento)
    {
        lancarSeDescartado();
        var transacao = await TransacaoDeLeitura.AbrirAsync(conexao.Conexao, cancelamento).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            var banco = await LerBancoAsync(cancelamento).ConfigureAwait(false);
            var tabelas = await LerTabelasAsync(cancelamento).ConfigureAwait(false);
            var outrosPais = await LerOutrosPaisAsync(cancelamento).ConfigureAwait(false);
            var colunas = await LerColunasAsync(tabelas, banco, cancelamento).ConfigureAwait(false);
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
                Propriedades = banco.Propriedades,
            };
        }
    }

    // A versão pelo número que a conexão já conferiu, nunca pelo texto (que pode trazer o nome de um derivado).
    private async Task<BancoLido> LerBancoAsync(CancellationToken cancelamento)
    {
        var dezesseis = conexao.Versao / 10000 >= 16;
        await using var comando = new NpgsqlCommand(ConsultaBancoInicio + (dezesseis ? BancoRegras16 : BancoRegras15) + ConsultaBancoFim, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        var numero = leitor.GetInt32(leitor.GetOrdinal("numero"));
        if (!_provedoresDoBanco.Contains(leitor.GetChar(leitor.GetOrdinal("provedor"))))
        {
            throw CatalogoDesconhecido("o provedor de locale do banco");
        }

        // A colação padrão do banco é sempre determinística: as marcas são falsas sem medida.
        var colacao = new ColacaoEsquema
        {
            Nome = "default",
            Deterministica = true,
            TemRegras = leitor.GetBoolean(leitor.GetOrdinal("regras")),
            IgnoraCaixa = false,
            IgnoraAcento = false,
            IgnoraPontuacao = false,
        };

        // Sem codificação declarada, o Incognito grava UTF-8 cru: até 4 bytes por caractere.
        var propriedades = new PropriedadesBanco
        {
            Versao = new Version(conexao.Versao / 10000, conexao.Versao % 10000),
            Codificacao = new CodificacaoTexto
            {
                Nome = leitor.GetString(leitor.GetOrdinal("codificacao")),
                Definida = numero != SqlAscii,
                Unicode = numero == Utf8,
                BytesPorCaractereMaximo = numero == SqlAscii ? 4 : leitor.GetInt32(leitor.GetOrdinal("bytes")),
            },
            Colacao = colacao,
        };
        return new BancoLido(propriedades, numero == Utf8, leitor.GetInt32(leitor.GetOrdinal("maximo_identificador")));
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

    // As colunas por nome de campo (a consulta cresce sem renumerar), depois a colação e as referências da geração de cada
    // uma, que dependem de todas as colunas lidas.
    private async Task<Dictionary<uint, List<ColunaLida>>> LerColunasAsync(Dictionary<uint, TabelaLida> tabelas, BancoLido banco, CancellationToken cancelamento)
    {
        var brutas = new List<ColunaBruta>();
        await using (var comando = new NpgsqlCommand(ConsultaColunasInicio + ColunaNaoNuloInvalido + ConsultaColunasFim, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            var campos = new CamposDaColuna(leitor);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                brutas.Add(campos.Ler(leitor));
            }
        }

        var referencias = await LerReferenciasDaGeracaoAsync(cancelamento).ConfigureAwait(false);
        var colacoes = await LerColacoesAsync([.. brutas.Select(bruta => bruta.Colacao).Where(oid => oid != 0).Distinct()], banco, cancelamento).ConfigureAwait(false);

        var colunas = new Dictionary<uint, List<ColunaLida>>();
        foreach (var daTabela in brutas.GroupBy(bruta => bruta.Tabela))
        {
            var ordenadas = daTabela.OrderBy(bruta => bruta.Numero).ToArray();
            var nomes = ordenadas.ToDictionary(bruta => bruta.Numero, bruta => bruta.Nome);
            colunas[daTabela.Key] = [.. ordenadas.Select(bruta => Montar(bruta, tabelas[bruta.Tabela], nomes, referencias, colacoes, banco))];
        }

        return colunas;
    }

    private static ColunaLida Montar(
        ColunaBruta bruta, TabelaLida tabela, Dictionary<short, string> nomes, ILookup<(uint Tabela, short Coluna), short> referencias,
        Dictionary<uint, ColacaoEsquema> colacoes, BancoLido banco)
    {
        var geracao = bruta.Geracao switch
        {
            's' => GeracaoColuna.Armazenada,
            'v' => GeracaoColuna.Virtual,
            _ => GeracaoColuna.Nenhuma,
        };
        var identidade = bruta.Identidade switch
        {
            'a' => IdentidadeColuna.Sempre,
            'd' => IdentidadeColuna.PorPadrao,
            _ => IdentidadeColuna.Nenhuma,
        };
        var (situacao, canonico) = ClassificacaoDeTipos.Classificar(bruta.Tipo, geracao, tabela.Estrangeira);
        var semantica = ClassificacaoDeTipos.Semantica(bruta.Tipo.Final, bruta.FinalCitext, bruta.Modificador, !banco.Propriedades.Codificacao.Definida, banco.MaximoIdentificador);

        // As citadas pela geração, na ordem da tabela; um número que não é coluna viva deixa as referências incompletas. A
        // geração pela linha inteira o banco recusa.
        ReferenciasDaExpressao? daGeracao = null;
        if (geracao != GeracaoColuna.Nenhuma)
        {
            var citadas = referencias[(bruta.Tabela, bruta.Numero)].Distinct().Order().ToArray();
            daGeracao = new ReferenciasDaExpressao
            {
                Colunas = [.. citadas.Where(nomes.ContainsKey).Select(numero => nomes[numero])],
                Incompletas = !citadas.All(nomes.ContainsKey),
            };
        }

        // Anulável se a coluna não tem NOT NULL validado. O NOT NULL de um domínio não conta: o banco aceita NULL numa
        // coluna de domínio NOT NULL em casos documentados (subconsulta vazia, junção externa).
        var coluna = new ColunaEsquema
        {
            Nome = bruta.Nome,
            TipoNativo = bruta.TipoNativo,
            Situacao = situacao,
            Tipo = canonico,
            Anulavel = !(bruta.NaoNulo && !bruta.NaoNuloInvalido),
            Geracao = geracao,
            Identidade = identidade,
            TipoBase = bruta.TipoBase,
            Colacao = bruta.Colacao == 0 ? null : colacoes[bruta.Colacao],
            IgnoraCaixaPeloTipo = semantica.IgnoraCaixa,
            IgnoraEspacoFinalPeloTipo = semantica.IgnoraEspacoFinal,
            Comprimento = semantica.Comprimento,
            Precisao = semantica.Precisao,
            Escala = semantica.Escala,
            Faixa = semantica.Faixa,
            ReferenciasDaGeracao = daGeracao,
            RestritaPeloTipo = bruta.Restrita,
        };
        return new ColunaLida(bruta.Numero, coluna, bruta.TipoSemDefault);
    }

    private async Task<ILookup<(uint Tabela, short Coluna), short>> LerReferenciasDaGeracaoAsync(CancellationToken cancelamento)
    {
        var lidas = new List<(uint Tabela, short Coluna, short Citada)>();
        await using var comando = new NpgsqlCommand(ConsultaReferenciasDaGeracao, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            lidas.Add((leitor.GetFieldValue<uint>(0), leitor.GetInt16(1), (short)leitor.GetInt32(2)));
        }

        return lidas.ToLookup(lida => (lida.Tabela, lida.Coluna), lida => lida.Citada);
    }

    // A colação padrão do banco vale para o provedor 'd'. A determinística não ignora nada; a não determinística é medida no
    // servidor só quando a medida não pode abortar a transação nem mentir: sem regras do usuário (que igualam quaisquer
    // caracteres), num banco UTF-8 (onde os textos da medida existem) e com USAGE no esquema da colação. Fora disso, as
    // marcas ficam nulas, o pior caso.
    private async Task<Dictionary<uint, ColacaoEsquema>> LerColacoesAsync(uint[] oids, BancoLido banco, CancellationToken cancelamento)
    {
        var lidas = new List<(uint Oid, string Nome, string Esquema, char Provedor, bool Deterministica, bool Acesso, bool TemRegras)>();
        var dezesseis = conexao.Versao / 10000 >= 16;
        await using (var comando = new NpgsqlCommand(ConsultaColacoesInicio + (dezesseis ? ColacaoRegras16 : ColacaoRegras15) + ConsultaColacoesFim, conexao.Conexao))
        {
            comando.Parameters.Add(new NpgsqlParameter { Value = oids, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Oid });
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var acesso = leitor.GetOrdinal("acesso");
                lidas.Add((
                    leitor.GetFieldValue<uint>(leitor.GetOrdinal("oid")),
                    leitor.GetString(leitor.GetOrdinal("nome")),
                    leitor.GetString(leitor.GetOrdinal("esquema")),
                    leitor.GetChar(leitor.GetOrdinal("provedor")),
                    leitor.GetBoolean(leitor.GetOrdinal("deterministica")),
                    !leitor.IsDBNull(acesso) && leitor.GetBoolean(acesso),
                    leitor.GetBoolean(leitor.GetOrdinal("regras"))));
            }
        }

        var colacoes = new Dictionary<uint, ColacaoEsquema>();
        foreach (var lida in lidas)
        {
            if (!_provedoresDeColacao.Contains(lida.Provedor))
            {
                throw CatalogoDesconhecido("o provedor de uma colação");
            }

            if (lida.Provedor == 'd')
            {
                colacoes[lida.Oid] = banco.Propriedades.Colacao;
                continue;
            }

            var medida = lida.Deterministica ? (false, false, false)
                : !lida.TemRegras && banco.Utf8 && lida.Acesso ? await MedirAsync(lida.Esquema, lida.Nome, lida.Oid, cancelamento).ConfigureAwait(false)
                : ((bool?)null, (bool?)null, (bool?)null);
            colacoes[lida.Oid] = new ColacaoEsquema
            {
                Nome = lida.Nome,
                Esquema = lida.Esquema,
                Deterministica = lida.Deterministica,
                TemRegras = lida.TemRegras,
                IgnoraCaixa = medida.Item1,
                IgnoraAcento = medida.Item2,
                IgnoraPontuacao = medida.Item3,
            };
        }

        return colacoes;
    }

    // O único SQL montado com nomes do catálogo, citados. Só saem booleanos de textos sintéticos. A guarda olhou o oid no
    // instante da leitura, e o COLLATE resolve o nome no catálogo atual: o to_regcollation confere que o nome ainda é o
    // mesmo oid. Qualquer erro vira uma recusa sem a mensagem do banco.
    private Task<(bool?, bool?, bool?)> MedirAsync(string esquema, string nome, uint oid, CancellationToken cancelamento) =>
        SemErroDoBancoAsync(esquema, nome, async () =>
        {
            var citada = _sql.Citar(new NomeObjeto(esquema, nome));
            var medida = $"SELECT 'a' = 'A' COLLATE {citada}, 'a' = 'á' COLLATE {citada}, 'a-b.c' = 'abc' COLLATE {citada}, pg_catalog.to_regcollation($1)::pg_catalog.oid = $2";
            await using var comando = new NpgsqlCommand(medida, conexao.Conexao);
            comando.Parameters.Add(new NpgsqlParameter { Value = citada, NpgsqlDbType = NpgsqlDbType.Text });
            comando.Parameters.Add(new NpgsqlParameter { Value = oid, NpgsqlDbType = NpgsqlDbType.Oid });
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
            if (leitor.IsDBNull(3) || !leitor.GetBoolean(3))
            {
                throw ErroDaMedida(esquema, nome);
            }

            return ((bool?)leitor.GetBoolean(0), (bool?)leitor.GetBoolean(1), (bool?)leitor.GetBoolean(2));
        });

    /// <summary>
    /// Roda a medida de uma colação e troca qualquer erro do banco (cuja mensagem, detalhe e contexto podem trazer valores)
    /// pela recusa só com o nome da colação, sem a exceção original.
    /// </summary>
    /// <typeparam name="T">O resultado da medida.</typeparam>
    /// <param name="esquema">O esquema da colação.</param>
    /// <param name="nome">O nome da colação.</param>
    /// <param name="medir">A medida.</param>
    /// <returns>O resultado.</returns>
    internal static async Task<T> SemErroDoBancoAsync<T>(string esquema, string nome, Func<Task<T>> medir)
    {
        try
        {
            return await medir().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            throw ErroDaMedida(esquema, nome);
        }
    }

    // O nome citado e com os caracteres de controle escapados: um nome com quebra de linha não forja linhas no registro.
    private static IncognitoException ErroDaMedida(string esquema, string nome) => new(
        CodigosPostgreSql.MedidaDeColacao,
        $"A medida do comportamento da colação {Visivel(_sql.Citar(new NomeObjeto(esquema, nome)))} falhou ou a colação mudou durante a leitura do esquema; leia de novo.");

    private static string Visivel(string texto) =>
        string.Concat(texto.Select(caractere => char.IsControl(caractere) ? "\\u" + ((int)caractere).ToString("X4", CultureInfo.InvariantCulture) : caractere.ToString()));

    private static IncognitoException CatalogoDesconhecido(string campo) => new(
        CodigosPostgreSql.CatalogoDesconhecido,
        $"O catálogo do PostgreSQL tem um valor que o Incognito não conhece em {campo} (versão mais nova do servidor?).");

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

    // O que a leitura da origem e do destino tira do banco inteiro.
    private sealed record BancoLido(PropriedadesBanco Propriedades, bool Utf8, int MaximoIdentificador);

    // Uma linha da consulta das colunas, antes da colação e das referências da geração.
    private sealed record ColunaBruta(
        uint Tabela, short Numero, string Nome, string TipoNativo, bool NaoNulo, bool NaoNuloInvalido, char Geracao, char Identidade, uint Colacao,
        TipoLido Tipo, string? TipoBase, bool FinalCitext, int Modificador, bool Restrita, uint? TipoSemDefault);

    // Os campos da consulta das colunas, pelo nome, procurados uma vez.
    private sealed class CamposDaColuna(NpgsqlDataReader leitor)
    {
        private readonly int _tabela = leitor.GetOrdinal("tabela");
        private readonly int _numero = leitor.GetOrdinal("numero");
        private readonly int _nome = leitor.GetOrdinal("nome");
        private readonly int _tipoNativo = leitor.GetOrdinal("tipo_nativo");
        private readonly int _naoNulo = leitor.GetOrdinal("nao_nulo");
        private readonly int _naoNuloInvalido = leitor.GetOrdinal("nao_nulo_invalido");
        private readonly int _geracao = leitor.GetOrdinal("geracao");
        private readonly int _identidade = leitor.GetOrdinal("identidade");
        private readonly int _colacao = leitor.GetOrdinal("colacao");
        private readonly int _tipo = leitor.GetOrdinal("tipo");
        private readonly int _nomeDoTipo = leitor.GetOrdinal("nome_do_tipo");
        private readonly int _categoria = leitor.GetOrdinal("categoria");
        private readonly int _extensao = leitor.GetOrdinal("extensao");
        private readonly int _tipoBase = leitor.GetOrdinal("tipo_base");
        private readonly int _final = leitor.GetOrdinal("final");
        private readonly int _finalCitext = leitor.GetOrdinal("final_citext");
        private readonly int _modificador = leitor.GetOrdinal("modificador");
        private readonly int _restrita = leitor.GetOrdinal("restrita");
        private readonly int _tipoSemDefault = leitor.GetOrdinal("tipo_sem_default");

        internal ColunaBruta Ler(NpgsqlDataReader linha) => new(
            linha.GetFieldValue<uint>(_tabela),
            linha.GetInt16(_numero),
            linha.GetString(_nome),
            linha.GetString(_tipoNativo),
            linha.GetBoolean(_naoNulo),
            linha.GetBoolean(_naoNuloInvalido),
            linha.GetChar(_geracao),
            linha.GetChar(_identidade),
            linha.GetFieldValue<uint>(_colacao),
            new TipoLido(
                linha.GetFieldValue<uint>(_tipo),
                linha.GetString(_nomeDoTipo),
                linha.GetChar(_categoria),
                linha.IsDBNull(_extensao) ? null : linha.GetString(_extensao),
                linha.GetFieldValue<uint>(_final)),
            linha.IsDBNull(_tipoBase) ? null : linha.GetString(_tipoBase),
            linha.GetBoolean(_finalCitext),
            linha.GetInt32(_modificador),
            linha.GetBoolean(_restrita),
            linha.IsDBNull(_tipoSemDefault) ? null : linha.GetFieldValue<uint>(_tipoSemDefault));
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
