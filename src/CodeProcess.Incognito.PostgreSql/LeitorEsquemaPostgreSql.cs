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
/// política, de índice e de restrição, condição e argumentos de trigger, texto de regra de reescrita, locale e regras de
/// colação nem o conteúdo dos large objects. Das expressões da chave de partição e dos índices e dos predicados só saem,
/// extraídos no servidor, os números das colunas citadas; dos <c>CHECK</c>s e das exclusões, as colunas da chave da
/// restrição; das colunas geradas e das materialized views, as dependências, pelo <c>pg_depend</c>. O comportamento de uma
/// colação não determinística é medido no servidor com textos sintéticos, nunca deduzido do locale. As permissões do login
/// saem só como marcas, pelas funções de privilégio (que leem o catálogo atual, não o instante da transação), sempre por oid.
/// </remarks>
internal sealed class LeitorEsquemaPostgreSql(ConexaoPostgreSql conexao, Action lancarSeDescartado) : LeitorEsquema
{
    // Os esquemas lidos: fora os do sistema (pg_catalog, pg_toast e os temporários começam com pg_, que o usuário não pode
    // criar; as tabelas temporárias ficam de fora por eles), o information_schema e o do diário do Incognito. Pelo prefixo,
    // sem LIKE: o escape do LIKE depende de standard_conforming_strings.
    private const string FiltroDeEsquema = "NOT pg_catalog.starts_with(n.nspname, 'pg_') AND n.nspname NOT IN ('information_schema', 'incognito')";

    private const string DefinicaoDasTabelas = $"""
        tabelas AS (
            SELECT c.oid
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'f') AND {FiltroDeEsquema})
        """;

    private const string Tabelas = "WITH " + DefinicaoDasTabelas;

    // Para as consultas que precisam de uma CTE recursiva depois da das tabelas.
    private const string TabelasERecursiva = "WITH RECURSIVE " + DefinicaoDasTabelas + ",";

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

    // O estado de cada chave estrangeira, pela família dela: a original e as cópias que o banco cria nas partições (dos dois
    // lados). Imposta se nenhuma trigger de integridade da família está desabilitada ou só de réplica (e, no 18, se nenhuma
    // está NOT ENFORCED, que não tem trigger); validada se todas foram validadas; desligada sem restauração se alguma trigger
    // de integridade da família saiu do estado de origem (O), inclusive para sempre (A): só o superuser a muda, e a chave
    // recriada pelo dono volta com todas em O.
    private const string ConsultaEstadoDasChavesInicio = TabelasERecursiva + """
        familia AS (
            SELECT k.oid, k.oid AS raiz
            FROM pg_catalog.pg_constraint k
            WHERE k.contype = 'f' AND k.conparentid = 0 AND k.conrelid IN (SELECT oid FROM tabelas)
            UNION ALL
            SELECT c.oid, f.raiz
            FROM pg_catalog.pg_constraint c
            JOIN familia f ON c.conparentid = f.oid)
        SELECT f.raiz AS restricao,
               NOT EXISTS (SELECT 1
                           FROM familia g
                           JOIN pg_catalog.pg_trigger t ON t.tgconstraint = g.oid
                           WHERE g.raiz = f.raiz AND t.tgenabled NOT IN ('O', 'A'))
        """;

    private const string EstadoDasChavesImposta18 = " AND pg_catalog.bool_and(c.conenforced)";

    private const string ConsultaEstadoDasChavesFim = """
         AS imposta,
               pg_catalog.bool_and(c.convalidated) AS validada,
               EXISTS (SELECT 1
                       FROM familia g
                       JOIN pg_catalog.pg_trigger t ON t.tgconstraint = g.oid
                       WHERE g.raiz = f.raiz AND t.tgenabled <> 'O') AS sem_restauracao
        FROM familia f
        JOIN pg_catalog.pg_constraint c ON c.oid = f.oid
        GROUP BY f.raiz
        """;

    // Os índices únicos, também o da chave primária e o inválido, sem os de exclusão (que são restrições de exclusão) e sem
    // os anexados ao índice de uma particionada (ficam no pai). A chave sai inteira, com as colunas só incluídas, e o cliente
    // toma as primeiras; das expressões e do predicado, só os números das colunas citadas, extraídos no servidor.
    private const string ConsultaIndicesUnicos = Tabelas + """
        SELECT i.indrelid AS tabela, x.relname AS nome, i.indisprimary AS primario, i.indnullsnotdistinct AS nulos_iguais, i.indnkeyatts AS tamanho,
               i.indkey::pg_catalog.int2[] AS colunas, i.indcollation::pg_catalog.oid[] AS colacoes,
               i.indexprs IS NOT NULL AS tem_expressoes, i.indpred IS NOT NULL AS parcial,
               ARRAY(SELECT DISTINCT m[1]::pg_catalog.int2 FROM pg_catalog.regexp_matches(i.indexprs::text, ':varattno (-?[0-9]+)', 'g') AS m) AS das_expressoes,
               ARRAY(SELECT DISTINCT m[1]::pg_catalog.int2 FROM pg_catalog.regexp_matches(i.indpred::text, ':varattno (-?[0-9]+)', 'g') AS m) AS do_predicado
        FROM pg_catalog.pg_index i
        JOIN pg_catalog.pg_class x ON x.oid = i.indexrelid
        WHERE i.indisunique AND NOT i.indisexclusion AND i.indrelid IN (SELECT oid FROM tabelas)
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h WHERE h.inhrelid = i.indexrelid)
        """;

    // Os CHECKs (os NOT NULL do 18 também estão em pg_constraint, com outro tipo). Numa partição, só os locais: os herdados
    // do pai valem pelo pai. Sem colunas: o CHECK constante; 0: a linha inteira.
    private const string ConsultaChecksInicio = Tabelas + """
        SELECT k.conrelid AS tabela, k.conname AS nome, k.conkey AS colunas
        """;

    private const string ChecksImposto18 = ", k.conenforced AS imposto";

    private const string ChecksImposto15 = ", true AS imposto";

    private const string ConsultaChecksFim = """

        FROM pg_catalog.pg_constraint k
        JOIN pg_catalog.pg_class c ON c.oid = k.conrelid
        WHERE k.contype = 'c' AND (k.conislocal OR NOT c.relispartition) AND k.conrelid IN (SELECT oid FROM tabelas)
        """;

    // As restrições de exclusão (no 18, também a chave primária ou única com WITHOUT OVERLAPS), sem as cópias nas partições.
    // As colunas: as da chave da restrição (sem as só incluídas; 0 é um elemento de expressão) e as citadas pelas expressões
    // e pelo predicado do índice dela.
    private const string ConsultaExclusoesInicio = Tabelas + """
        SELECT k.conrelid AS tabela, k.conname AS nome, k.conkey AS colunas,
               ARRAY(SELECT DISTINCT m[1]::pg_catalog.int2 FROM pg_catalog.regexp_matches(i.indexprs::text, ':varattno (-?[0-9]+)', 'g') AS m) AS das_expressoes,
               ARRAY(SELECT DISTINCT m[1]::pg_catalog.int2 FROM pg_catalog.regexp_matches(i.indpred::text, ':varattno (-?[0-9]+)', 'g') AS m) AS do_predicado
        FROM pg_catalog.pg_constraint k
        JOIN pg_catalog.pg_index i ON i.indexrelid = k.conindid
        WHERE k.conparentid = 0 AND k.conrelid IN (SELECT oid FROM tabelas)
        """;

    private const string ExclusoesTipo18 = " AND (k.contype = 'x' OR (k.contype IN ('p', 'u') AND k.conperiod))";

    private const string ExclusoesTipo15 = " AND k.contype = 'x'";

    // As triggers de usuário (sem as internas, de integridade e de unicidade adiada): o nome, o estado e se é a cópia da
    // trigger da particionada. Nunca a condição, os argumentos nem a função.
    private const string ConsultaTriggers = Tabelas + """
        SELECT t.tgrelid AS tabela, t.tgname AS nome, t.tgenabled AS estado, t.tgparentid <> 0 AS herdada
        FROM pg_catalog.pg_trigger t
        WHERE NOT t.tgisinternal AND t.tgrelid IN (SELECT oid FROM tabelas)
        """;

    // Se a tabela está numa publicação que replica a inserção ou o esvaziamento: pela publicação de todas as tabelas (só as
    // persistentes, comuns e particionadas: a estrangeira e a unlogged nunca são publicadas), pela própria tabela ou por uma
    // acima dela na árvore, e pelo esquema dela ou de uma acima. Pelos catálogos base, nunca pela pg_publication_tables, que
    // traz o texto do filtro de linha. O esquema conta também quando só o de cima o publica: o pior caso. As publicadas
    // diretamente saem uma vez, e os ancestrais só são procurados nas partições: com milhares de partições, a consulta por
    // tabela e por publicação passava de meio segundo.
    private const string ConsultaPublicacoes = Tabelas + "," + """
        publicacoes AS (
            SELECT p.oid, p.puballtables
            FROM pg_catalog.pg_publication p
            WHERE p.pubinsert OR p.pubtruncate),
        diretas AS (
            SELECT r.prrelid AS oid
            FROM pg_catalog.pg_publication_rel r
            JOIN publicacoes p ON p.oid = r.prpubid
            UNION
            SELECT c.oid
            FROM pg_catalog.pg_publication_namespace s
            JOIN publicacoes p ON p.oid = s.pnpubid
            JOIN pg_catalog.pg_class c ON c.relnamespace = s.pnnspid)
        SELECT c.oid AS tabela,
               c.relkind IN ('r', 'p') AND c.relpersistence = 'p'
                 AND (EXISTS (SELECT 1 FROM publicacoes WHERE puballtables)
                      OR c.oid IN (SELECT oid FROM diretas)
                      OR (c.relispartition AND EXISTS (SELECT 1 FROM pg_catalog.pg_partition_ancestors(c.oid) a WHERE a.relid IN (SELECT oid FROM diretas)))) AS publicada
        FROM tabelas
        JOIN pg_catalog.pg_class c ON c.oid = tabelas.oid
        """;

    // Se as assinaturas são legíveis pelo login: o PostgreSQL revoga do público só o texto da conexão, mas um servidor
    // endurecido pode revogar o resto; ilegíveis, a assinatura de cada tabela fica sem resposta (o pior caso).
    private const string ConsultaAssinaturasLegiveis = """
        SELECT pg_catalog.has_table_privilege('pg_catalog.pg_subscription_rel', 'SELECT')
               AND pg_catalog.has_column_privilege('pg_catalog.pg_subscription', 'oid', 'SELECT')
               AND pg_catalog.has_column_privilege('pg_catalog.pg_subscription', 'subdbid', 'SELECT')
        """;

    // Se a tabela está numa assinatura deste banco, ligada ou não (a desligada aplica o que se acumulou ao ser ligada), em
    // qualquer estado da sincronização. O pg_subscription é do servidor inteiro, e o pg_subscription_rel, de cada banco: o
    // banco clonado de outro leva as linhas da assinatura do original, que não grava nele. Nunca o texto da conexão.
    private const string ConsultaAssinaturas = Tabelas + "," + """
        assinadas AS (
            SELECT r.srrelid AS oid
            FROM pg_catalog.pg_subscription_rel r
            JOIN pg_catalog.pg_subscription s ON s.oid = r.srsubid
            WHERE s.subdbid = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database()))
        SELECT tabelas.oid AS tabela, tabelas.oid IN (SELECT oid FROM assinadas) AS assinada
        FROM tabelas
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
        SELECT c.oid, n.nspname, c.relname, c.relispopulated
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'm' AND {FiltroDeEsquema};
        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_largeobject_metadata)
        """;

    // Os objetos do esquema do diário que guardam ou descrevem dado: tabelas (também a particionada e a estrangeira), views,
    // materialized views, índices e sequences. Os nomes e o tipo; o esquema que não existe não tem nenhum.
    private const string ConsultaEsquemaReservado = """
        SELECT n.nspname, c.relname, c.relkind = 'r', c.relkind = 'i', c.relkind = 'S'
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'incognito' AND c.relkind IN ('r', 'p', 'f', 'v', 'm', 'i', 'I', 'S')
        """;

    // O que cada materialized view lê, pelas dependências das regras de reescrita dela e das regras de SELECT das views por
    // onde a leitura passa (nunca o texto das regras; as regras de INSERT, UPDATE e DELETE de uma view não são lidas pelo
    // REFRESH): as materialized views lidas e se alguma regra da cadeia depende de uma função ou de um operador, que o banco
    // não liga às tabelas que a função dele lê. A dependência da regra sobre a própria relação (no 15) fica de fora pelo
    // dono da regra.
    private const string ConsultaDependenciasDosResiduais = $"""
        WITH RECURSIVE regras (visao, regra, dona) AS (
            SELECT c.oid, r.oid, r.ev_class
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_rewrite r ON r.ev_class = c.oid
            WHERE c.relkind = 'm' AND {FiltroDeEsquema}
            UNION
            SELECT g.visao, r.oid, r.ev_class
            FROM regras g
            JOIN pg_catalog.pg_depend d ON d.classid = 'pg_catalog.pg_rewrite'::pg_catalog.regclass AND d.objid = g.regra
            JOIN pg_catalog.pg_class v ON v.oid = d.refobjid AND v.relkind = 'v'
            JOIN pg_catalog.pg_rewrite r ON r.ev_class = v.oid AND r.ev_type = '1'
            WHERE d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid <> g.dona)
        SELECT DISTINCT g.visao, d.refclassid IN ('pg_catalog.pg_proc'::pg_catalog.regclass, 'pg_catalog.pg_operator'::pg_catalog.regclass) AS funcao, l.oid AS lida
        FROM regras g
        JOIN pg_catalog.pg_depend d ON d.classid = 'pg_catalog.pg_rewrite'::pg_catalog.regclass AND d.objid = g.regra
        LEFT JOIN pg_catalog.pg_class l ON d.refclassid = 'pg_catalog.pg_class'::pg_catalog.regclass AND l.oid = d.refobjid AND l.relkind = 'm' AND l.oid <> g.dona
        WHERE d.refclassid IN ('pg_catalog.pg_proc'::pg_catalog.regclass, 'pg_catalog.pg_operator'::pg_catalog.regclass) OR l.oid IS NOT NULL
        """;

    // As permissões do login em cada tabela, pelo catálogo atual: as funções has_* e pg_has_role não leem o instante da
    // transação. Sempre por oid (um nome citado falharia sem USAGE no esquema). A tabela que sumiu entre a leitura do esquema
    // e esta tem has_table_privilege nulo, e as capacidades dela ficam nulas; nas colunas, o IS NOT TRUE conta o nulo como
    // sem privilégio. A leitura de uma tabela sem colunas pede o SELECT na tabela. A gravação vale pelo privilégio na tabela
    // ou em toda coluna que a carga grava (as geradas ficam de fora), e a referência, na tabela ou em toda coluna. A segurança
    // por linha vale para o login pelo catálogo atual ou, se a tabela sumiu (row_security_active daria
    // não), pelo instante da transação: ligada, sem o login ignorá-la (superusuário ou BYPASSRLS) e forçada ou de outro dono.
    private const string ConsultaAcessoDasTabelas = Tabelas + """
        SELECT n.nspname AS esquema, c.relname AS nome,
               pg_catalog.has_table_privilege(c.oid, 'SELECT') IS NULL AS sumida,
               pg_catalog.has_schema_privilege(c.relnamespace, 'USAGE') AS uso,
               ARRAY(SELECT a.attname
                     FROM pg_catalog.pg_attribute a
                     WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                       AND pg_catalog.has_column_privilege(c.oid, a.attnum, 'SELECT') IS NOT TRUE
                     ORDER BY a.attnum) AS sem_leitura,
               pg_catalog.has_table_privilege(c.oid, 'SELECT') AS le_tabela,
               EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped) AS tem_colunas,
               pg_catalog.has_table_privilege(c.oid, 'INSERT')
                 OR (EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                             WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = '')
                     AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                     WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = ''
                                       AND pg_catalog.has_column_privilege(c.oid, a.attnum, 'INSERT') IS NOT TRUE)) AS insere,
               pg_catalog.has_table_privilege(c.oid, 'TRUNCATE') AS esvazia,
               pg_catalog.has_table_privilege(c.oid, 'REFERENCES')
                 OR (EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped)
                     AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                     WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                                       AND pg_catalog.has_column_privilege(c.oid, a.attnum, 'REFERENCES') IS NOT TRUE)) AS referencia,
               pg_catalog.pg_has_role(c.relowner, 'USAGE') AS dono,
               pg_catalog.row_security_active(c.oid)
                 OR (c.relrowsecurity
                     AND NOT (SELECT r.rolsuper OR r.rolbypassrls FROM pg_catalog.pg_roles r WHERE r.rolname = current_user)
                     AND (c.relforcerowsecurity OR NOT pg_catalog.pg_has_role(c.relowner, 'USAGE'))) AS por_linha
        FROM tabelas
        JOIN pg_catalog.pg_class c ON c.oid = tabelas.oid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        """;

    // As permissões nas materialized views (limpar é atualizá-las: no 17+, pelo MAINTAIN; antes, sendo dono) e nas sequences
    // (ajustar é o setval, que exige UPDATE), com o USAGE no esquema e nulas para o objeto que sumiu.
    private const string ConsultaAcessoDosObjetosInicio = """
        SELECT n.nspname AS esquema, c.relname AS nome, c.relkind = 'S' AS sequence,
               pg_catalog.has_table_privilege(c.oid, 'SELECT') IS NULL AS sumida,
               pg_catalog.has_schema_privilege(c.relnamespace, 'USAGE') AS uso,
               CASE WHEN c.relkind = 'S' THEN pg_catalog.has_sequence_privilege(c.oid, 'UPDATE') ELSE
        """;

    private const string LimparVisao17 = " pg_catalog.has_table_privilege(c.oid, 'MAINTAIN')";

    private const string LimparVisao15 = " pg_catalog.pg_has_role(c.relowner, 'USAGE')";

    private const string ConsultaAcessoDosObjetosFim = $"""
         END AS pode
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('m', 'S') AND {FiltroDeEsquema}
        """;

    // As permissões no banco (marcá-lo pede ser dono; o esquema do diário, CREATE) e nos large objects: apagá-los pede o
    // modo de compatibilidade dos privilégios ou ser dono de todos (o superusuário é membro de todo papel).
    private const string ConsultaAcessoDoBanco = """
        SELECT pg_catalog.pg_has_role(d.datdba, 'USAGE') AS marca, pg_catalog.has_database_privilege(d.oid, 'CREATE') AS cria,
               pg_catalog.current_setting('lo_compat_privileges')::boolean
                 OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_largeobject_metadata m WHERE NOT pg_catalog.pg_has_role(m.lomowner, 'USAGE')) AS limpa_objetos_grandes
        FROM pg_catalog.pg_database d
        WHERE d.datname = pg_catalog.current_database()
        """;

    // Os números das codificações (pg_wchar.h): sem codificação declarada e UTF-8.
    private const int SqlAscii = 0;
    private const int Utf8 = 6;

    // Os provedores de locale do banco que o dialeto conhece: libc, ICU e, no 17+, o embutido; as colações têm também o 'd',
    // a padrão do banco.
    private static readonly char[] _provedoresDoBanco = ['c', 'i', 'b'];
    private static readonly char[] _provedoresDeColacao = ['d', 'c', 'i', 'b'];

    // Os estados de uma trigger: habilitada, desabilitada, só de réplica e sempre.
    private static readonly char[] _estadosDeTrigger = ['O', 'D', 'R', 'A'];

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
            var brutas = await LerColunasBrutasAsync(cancelamento).ConfigureAwait(false);
            var indices = await LerIndicesBrutosAsync(cancelamento).ConfigureAwait(false);

            // As colações das colunas e dos elementos dos índices, lidas e medidas uma vez.
            var colacoes = await LerColacoesAsync(
                [.. brutas.Select(bruta => bruta.Colacao).Concat(indices.SelectMany(indice => indice.Colacoes)).Where(oid => oid != 0).Distinct()],
                banco,
                cancelamento).ConfigureAwait(false);
            var colunas = await MontarColunasAsync(brutas, tabelas, banco, colacoes, cancelamento).ConfigureAwait(false);
            var chavesDeParticao = await LerChavesDeParticaoAsync(tabelas, colunas, cancelamento).ConfigureAwait(false);
            var chavesPrimarias = await LerChavesPrimariasAsync(cancelamento).ConfigureAwait(false);
            var chavesEstrangeiras = await LerChavesEstrangeirasAsync(cancelamento).ConfigureAwait(false);
            var indicesUnicos = MontarIndices(indices, colunas, colacoes);
            var checks = await LerChecksAsync(colunas, cancelamento).ConfigureAwait(false);
            var exclusoes = await LerExclusoesAsync(colunas, cancelamento).ConfigureAwait(false);
            var triggers = await LerTriggersAsync(cancelamento).ConfigureAwait(false);
            var publicadas = await LerPublicacoesAsync(cancelamento).ConfigureAwait(false);
            var assinadas = await LerAssinaturasAsync(cancelamento).ConfigureAwait(false);
            var sequences = await LerSequencesAsync(tabelas, colunas, cancelamento).ConfigureAwait(false);
            var residuais = await LerResiduaisAsync(cancelamento).ConfigureAwait(false);
            var reservado = await LerEsquemaReservadoAsync(cancelamento).ConfigureAwait(false);
            var triggersDeEvento = await LerTriggersDeEventoAsync(cancelamento).ConfigureAwait(false);
            var acesso = await LerAcessoAsync(conexao.Conexao, conexao.Versao, cancelamento).ConfigureAwait(false);
            var nomes = tabelas.Values.Select(tabela => tabela.Nome).OrderBy(nome => nome, OrdemDeNomes.Instancia).ToArray();

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
                        Publicada = publicadas.TryGetValue(tabela.Oid, out var publicada) ? publicada : null,
                        Assinada = assinadas.TryGetValue(tabela.Oid, out var assinada) ? assinada : null,
                        IndicesUnicos = Ordenados(indicesUnicos[tabela.Oid], indice => indice.Nome),
                        RestricoesCheck = Ordenados(checks[tabela.Oid], check => check.Nome),
                        RestricoesExclusao = Ordenados(exclusoes[tabela.Oid], exclusao => exclusao.Nome),
                        Triggers = Ordenados(triggers[tabela.Oid], trigger => trigger.Nome),
                    })
                    .OrderBy(tabela => tabela.Nome, OrdemDeNomes.Instancia)
                    .ToArray(),
                Sequences = sequences,
                ObjetosResiduais = residuais,
                ObjetosNoEsquemaReservado = reservado,
                TriggersDeEvento = triggersDeEvento,
                Propriedades = banco.Propriedades,

                // Um objeto que a leitura das permissões não achou (não deveria: as duas veem o mesmo catálogo) fica com tudo
                // nulo, o pior caso.
                Acesso = new AcessoLido
                {
                    Banco = acesso.Banco,
                    Tabelas = [.. nomes.Select(nome => acesso.Tabelas.GetValueOrDefault(nome) ?? new PermissoesTabela { Tabela = nome })],
                    Residuais =
                    [
                        .. residuais.Select(residual => new PermissoesResidual
                        {
                            Nome = residual.Nome,
                            Tipo = residual.Tipo,
                            PodeLimpar = residual.Tipo == TipoObjetoResidual.ObjetosGrandes ? acesso.LimparObjetosGrandes : acesso.Objetos.GetValueOrDefault((residual.Nome, false)),
                        }),
                    ],
                    Sequences = [.. sequences.Select(sequence => new PermissoesSequence { Nome = sequence.Nome, PodeAjustar = acesso.Objetos.GetValueOrDefault((sequence.Nome, true)) })],
                },
            };
        }
    }

    /// <summary>
    /// As permissões do login que a conexão usa, por objeto, na transação que já está aberta na conexão (a do leitor ou, no
    /// teste de duas sessões, a do teste).
    /// </summary>
    /// <param name="conexao">A conexão, com a transação aberta.</param>
    /// <param name="versao">A versão do servidor (<c>server_version_num</c>).</param>
    /// <param name="cancelamento">Cancela a leitura.</param>
    /// <returns>As permissões lidas.</returns>
    internal static async Task<AcessoBruto> LerAcessoAsync(NpgsqlConnection conexao, int versao, CancellationToken cancelamento)
    {
        var tabelas = new Dictionary<NomeObjeto, PermissoesTabela>();
        await using (var comando = new NpgsqlCommand(ConsultaAcessoDasTabelas, conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var nome = new NomeObjeto(leitor.GetString(leitor.GetOrdinal("esquema")), leitor.GetString(leitor.GetOrdinal("nome")));
                var sumida = leitor.GetBoolean(leitor.GetOrdinal("sumida"));
                var uso = Marca(leitor, "uso");
                var semLeitura = leitor.GetFieldValue<string[]>(leitor.GetOrdinal("sem_leitura"));
                bool? Pode(bool? capacidade) => sumida ? null : E(uso, capacidade);
                var insere = Pode(Marca(leitor, "insere"));
                var dono = Pode(Marca(leitor, "dono"));
                var porLinha = Marca(leitor, "por_linha");
                tabelas.Add(nome, new PermissoesTabela
                {
                    Tabela = nome,
                    PodeLer = Pode(semLeitura.Length == 0 && (leitor.GetBoolean(leitor.GetOrdinal("tem_colunas")) || Marca(leitor, "le_tabela") == true)),
                    ColunasSemLeitura = semLeitura,
                    PodeInserir = insere,
                    PodeGravarIdentidade = insere,
                    PodeEsvaziar = Pode(Marca(leitor, "esvazia")),
                    PodeReferenciar = Pode(Marca(leitor, "referencia")),
                    PodeAlterarRestricoes = dono,
                    PodeDesligarTriggers = dono,
                    LeituraFiltradaPorLinha = porLinha,
                    EscritaRestritaPorLinha = porLinha,
                });
            }
        }

        var objetos = new Dictionary<(NomeObjeto Nome, bool Sequence), bool?>();
        var consulta = ConsultaAcessoDosObjetosInicio + (versao / 10000 >= 17 ? LimparVisao17 : LimparVisao15) + ConsultaAcessoDosObjetosFim;
        await using (var comando = new NpgsqlCommand(consulta, conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                var nome = new NomeObjeto(leitor.GetString(leitor.GetOrdinal("esquema")), leitor.GetString(leitor.GetOrdinal("nome")));
                var pode = leitor.GetBoolean(leitor.GetOrdinal("sumida")) ? null : E(Marca(leitor, "uso"), Marca(leitor, "pode"));
                objetos.Add((nome, leitor.GetBoolean(leitor.GetOrdinal("sequence"))), pode);
            }
        }

        await using (var comando = new NpgsqlCommand(ConsultaAcessoDoBanco, conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
            var banco = new PermissoesBanco { PodeMarcarBanco = Marca(leitor, "marca"), PodeCriarEsquema = Marca(leitor, "cria") };
            return new AcessoBruto(banco, Marca(leitor, "limpa_objetos_grandes"), tabelas, objetos);
        }
    }

    private static bool? Marca(NpgsqlDataReader leitor, string campo)
    {
        var ordinal = leitor.GetOrdinal(campo);
        return leitor.IsDBNull(ordinal) ? null : leitor.GetBoolean(ordinal);
    }

    // As duas condições: não se uma não vale; sem resposta se uma não tem resposta.
    private static bool? E(bool? uma, bool? outra) =>
        uma == false || outra == false ? false : uma is null || outra is null ? null : true;

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
                throw new DialetoRecusouException(
                    CodigosPostgreSql.ParticaoPendente,
                    $"A partição {Visivel(CitacaoPostgreSql.Citar(nome))} tem um DETACH ... CONCURRENTLY interrompido; termine-o com ALTER TABLE {Visivel(CitacaoPostgreSql.Citar(pai!))} DETACH PARTITION {Visivel(CitacaoPostgreSql.Citar(nome))} FINALIZE antes.");
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

    // As colunas por nome de campo (a consulta cresce sem renumerar).
    private async Task<List<ColunaBruta>> LerColunasBrutasAsync(CancellationToken cancelamento)
    {
        var brutas = new List<ColunaBruta>();
        await using var comando = new NpgsqlCommand(ConsultaColunasInicio + ColunaNaoNuloInvalido + ConsultaColunasFim, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        var campos = new CamposDaColuna(leitor);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            brutas.Add(campos.Ler(leitor));
        }

        return brutas;
    }

    // A colação e as referências da geração de cada coluna, que dependem de todas as colunas lidas.
    private async Task<Dictionary<uint, List<ColunaLida>>> MontarColunasAsync(
        List<ColunaBruta> brutas, Dictionary<uint, TabelaLida> tabelas, BancoLido banco, Dictionary<uint, ColacaoEsquema> colacoes, CancellationToken cancelamento)
    {
        var referencias = await LerReferenciasDaGeracaoAsync(cancelamento).ConfigureAwait(false);
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
            var citada = CitacaoPostgreSql.Citar(new NomeObjeto(esquema, nome));
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
        $"A medida do comportamento da colação {Visivel(CitacaoPostgreSql.Citar(new NomeObjeto(esquema, nome)))} falhou ou a colação mudou durante a leitura do esquema; leia de novo.");

    private static string Visivel(string texto) =>
        string.Concat(texto.Select(caractere => char.IsControl(caractere) ? "\\u" + ((int)caractere).ToString("X4", CultureInfo.InvariantCulture) : caractere.ToString()));

    private static IncognitoException CatalogoDesconhecido(string campo) => new(
        CodigosPostgreSql.CatalogoDesconhecido,
        $"O catálogo do PostgreSQL tem um valor que o Incognito não conhece em {campo} (versão mais nova do servidor?).");

    private async Task<List<IndiceBruto>> LerIndicesBrutosAsync(CancellationToken cancelamento)
    {
        var indices = new List<IndiceBruto>();
        await using var comando = new NpgsqlCommand(ConsultaIndicesUnicos, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            // A chave sem as colunas só incluídas; a colação de cada coluna da chave (0 sem colação).
            var tamanho = leitor.GetInt16(leitor.GetOrdinal("tamanho"));
            indices.Add(new IndiceBruto(
                leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela")),
                leitor.GetString(leitor.GetOrdinal("nome")),
                leitor.GetBoolean(leitor.GetOrdinal("primario")),
                !leitor.GetBoolean(leitor.GetOrdinal("nulos_iguais")),
                [.. leitor.GetFieldValue<short[]>(leitor.GetOrdinal("colunas")).Take(tamanho)],
                [.. leitor.GetFieldValue<uint[]>(leitor.GetOrdinal("colacoes")).Take(tamanho)],
                leitor.GetBoolean(leitor.GetOrdinal("tem_expressoes")) ? leitor.GetFieldValue<short[]>(leitor.GetOrdinal("das_expressoes")) : null,
                leitor.GetBoolean(leitor.GetOrdinal("parcial")) ? leitor.GetFieldValue<short[]>(leitor.GetOrdinal("do_predicado")) : null));
        }

        return indices;
    }

    // Um elemento é coluna ou expressão (0); uma coluna da chave que não é coluna viva conta como expressão incompleta.
    private static ILookup<uint, IndiceUnico> MontarIndices(List<IndiceBruto> indices, Dictionary<uint, List<ColunaLida>> colunas, Dictionary<uint, ColacaoEsquema> colacoes) =>
        indices.ToLookup(indice => indice.Tabela, indice =>
        {
            var daTabela = colunas.GetValueOrDefault(indice.Tabela) ?? [];
            var elementos = indice.Colunas
                .Select((numero, posicao) => new ElementoIndice
                {
                    Coluna = numero == 0 ? null : daTabela.Find(coluna => coluna.Numero == numero)?.Modelo.Nome,
                    Colacao = indice.Colacoes[posicao] == 0 ? null : colacoes[indice.Colacoes[posicao]],
                })
                .ToArray();
            var semNome = indice.Colunas.Where(numero => numero != 0 && daTabela.TrueForAll(coluna => coluna.Numero != numero)).Select(_ => (short)0);
            return new IndiceUnico
            {
                Nome = indice.Nome,
                Primario = indice.Primario,
                Elementos = elementos,
                ReferenciasDasExpressoes = elementos.Any(elemento => elemento.Coluna is null) ? Referencias((indice.DasExpressoes ?? []).Concat(semNome), daTabela) : null,
                Predicado = indice.DoPredicado is { } predicado ? Referencias(predicado, daTabela) : null,
                NulosDistintos = indice.NulosDistintos,
            };
        });

    private async Task<ILookup<uint, RestricaoCheck>> LerChecksAsync(Dictionary<uint, List<ColunaLida>> colunas, CancellationToken cancelamento)
    {
        var dezoito = conexao.Versao / 10000 >= 18;
        var checks = new List<(uint Tabela, RestricaoCheck Check)>();
        await using var comando = new NpgsqlCommand(ConsultaChecksInicio + (dezoito ? ChecksImposto18 : ChecksImposto15) + ConsultaChecksFim, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tabela = leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela"));
            var citadas = leitor.GetOrdinal("colunas");
            checks.Add((tabela, new RestricaoCheck
            {
                Nome = leitor.GetString(leitor.GetOrdinal("nome")),
                Referencias = Referencias(leitor.IsDBNull(citadas) ? [] : leitor.GetFieldValue<short[]>(citadas), colunas.GetValueOrDefault(tabela) ?? []),
                Imposta = leitor.GetBoolean(leitor.GetOrdinal("imposto")),
            }));
        }

        return checks.ToLookup(check => check.Tabela, check => check.Check);
    }

    // Da chave da restrição, só as colunas: o 0 é um elemento de expressão, cujas colunas vêm das expressões do índice.
    private async Task<ILookup<uint, RestricaoExclusao>> LerExclusoesAsync(Dictionary<uint, List<ColunaLida>> colunas, CancellationToken cancelamento)
    {
        var dezoito = conexao.Versao / 10000 >= 18;
        var exclusoes = new List<(uint Tabela, RestricaoExclusao Exclusao)>();
        await using var comando = new NpgsqlCommand(ConsultaExclusoesInicio + (dezoito ? ExclusoesTipo18 : ExclusoesTipo15), conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tabela = leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela"));
            var numeros = leitor.GetFieldValue<short[]>(leitor.GetOrdinal("colunas")).Where(numero => numero != 0)
                .Concat(leitor.GetFieldValue<short[]>(leitor.GetOrdinal("das_expressoes")))
                .Concat(leitor.GetFieldValue<short[]>(leitor.GetOrdinal("do_predicado")));
            exclusoes.Add((tabela, new RestricaoExclusao { Nome = leitor.GetString(leitor.GetOrdinal("nome")), Referencias = Referencias(numeros, colunas.GetValueOrDefault(tabela) ?? []) }));
        }

        return exclusoes.ToLookup(exclusao => exclusao.Tabela, exclusao => exclusao.Exclusao);
    }

    // Ligada se dispara numa sessão comum: habilitada ('O') ou sempre ('A'); desabilitada ('D') e só de réplica ('R') não. Um
    // estado que o dialeto não conhece é recusado.
    private async Task<ILookup<uint, TriggerEsquema>> LerTriggersAsync(CancellationToken cancelamento)
    {
        var triggers = new List<(uint Tabela, TriggerEsquema Trigger)>();
        await using var comando = new NpgsqlCommand(ConsultaTriggers, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var estado = leitor.GetChar(leitor.GetOrdinal("estado"));
            if (!_estadosDeTrigger.Contains(estado))
            {
                throw CatalogoDesconhecido("o estado de uma trigger");
            }

            triggers.Add((leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela")), new TriggerEsquema
            {
                Nome = leitor.GetString(leitor.GetOrdinal("nome")),
                Ligada = estado is 'O' or 'A',
                Herdada = leitor.GetBoolean(leitor.GetOrdinal("herdada")),
            }));
        }

        return triggers.ToLookup(trigger => trigger.Tabela, trigger => trigger.Trigger);
    }

    private async Task<Dictionary<uint, bool>> LerPublicacoesAsync(CancellationToken cancelamento)
    {
        var publicadas = new Dictionary<uint, bool>();
        await using var comando = new NpgsqlCommand(ConsultaPublicacoes, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            publicadas.Add(leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela")), leitor.GetBoolean(leitor.GetOrdinal("publicada")));
        }

        return publicadas;
    }

    // Vazio quando as assinaturas não são legíveis: nenhuma tabela tem resposta.
    private async Task<Dictionary<uint, bool>> LerAssinaturasAsync(CancellationToken cancelamento)
    {
        var assinadas = new Dictionary<uint, bool>();
        await using (var legiveis = new NpgsqlCommand(ConsultaAssinaturasLegiveis, conexao.Conexao))
        {
            if (await legiveis.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                return assinadas;
            }
        }

        await using var comando = new NpgsqlCommand(ConsultaAssinaturas, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            assinadas.Add(leitor.GetFieldValue<uint>(leitor.GetOrdinal("tabela")), leitor.GetBoolean(leitor.GetOrdinal("assinada")));
        }

        return assinadas;
    }

    // As colunas citadas pelos números, na ordem da tabela, sem as de sistema (negativas); a linha inteira (0) ou um número
    // que não é coluna viva deixa as referências incompletas.
    private static ReferenciasDaExpressao Referencias(IEnumerable<short> numeros, List<ColunaLida> colunas)
    {
        var nomes = numeros.Where(numero => numero >= 0).Distinct().Order().Select(numero => colunas.Find(coluna => coluna.Numero == numero)?.Modelo.Nome).ToArray();
        return new ReferenciasDaExpressao { Colunas = [.. nomes.OfType<string>()], Incompletas = nomes.Contains(null) };
    }

    private static T[] Ordenados<T>(IEnumerable<T> objetos, Func<T, string> nome) => [.. objetos.OrderBy(nome, StringComparer.Ordinal)];

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
        var estados = new Dictionary<uint, (bool Imposta, bool Validada, bool SemRestauracao)>();
        var dezoito = conexao.Versao / 10000 >= 18;
        await using (var comando = new NpgsqlCommand(ConsultaEstadoDasChavesInicio + (dezoito ? EstadoDasChavesImposta18 : "") + ConsultaEstadoDasChavesFim, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                estados.Add(
                    leitor.GetFieldValue<uint>(leitor.GetOrdinal("restricao")),
                    (leitor.GetBoolean(leitor.GetOrdinal("imposta")), leitor.GetBoolean(leitor.GetOrdinal("validada")), leitor.GetBoolean(leitor.GetOrdinal("sem_restauracao"))));
            }
        }

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
                    Imposta = estados.TryGetValue(restricao.Key.Restricao, out var estado) ? estado.Imposta : null,
                    Validada = estados.TryGetValue(restricao.Key.Restricao, out var validacao) ? validacao.Validada : null,
                    DesligadaSemRestauracao = estados.TryGetValue(restricao.Key.Restricao, out var restauracao) ? restauracao.SemRestauracao : null,
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

    // O tipo pelo relkind: a tabela comum, o índice dela e a sequence; o resto (particionada, estrangeira, view,
    // materialized view, índice de particionada) é outro, que nunca passa por objeto do diário.
    private async Task<ObjetoReservado[]> LerEsquemaReservadoAsync(CancellationToken cancelamento)
    {
        var objetos = new List<ObjetoReservado>();
        await using var comando = new NpgsqlCommand(ConsultaEsquemaReservado, conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            var tipo = leitor.GetBoolean(2) ? TipoObjetoReservado.Tabela
                : leitor.GetBoolean(3) ? TipoObjetoReservado.Indice
                : leitor.GetBoolean(4) ? TipoObjetoReservado.Sequence
                : TipoObjetoReservado.Outro;
            objetos.Add(new ObjetoReservado { Nome = new NomeObjeto(leitor.GetString(0), leitor.GetString(1)), Tipo = tipo });
        }

        return [.. objetos.OrderBy(objeto => objeto.Nome, OrdemDeNomes.Instancia)];
    }

    // Os gatilhos de evento ligados, pelo nome (só o nome: a função e as tags não saem).
    private async Task<string[]> LerTriggersDeEventoAsync(CancellationToken cancelamento)
    {
        var nomes = new List<string>();
        await using var comando = new NpgsqlCommand("SELECT e.evtname FROM pg_catalog.pg_event_trigger e WHERE e.evtenabled <> 'D'", conexao.Conexao);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            nomes.Add(leitor.GetString(0));
        }

        return [.. nomes.Order(StringComparer.Ordinal)];
    }

    private async Task<ObjetoResidual[]> LerResiduaisAsync(CancellationToken cancelamento)
    {
        var visoes = new Dictionary<uint, (NomeObjeto Nome, bool Populada)>();
        bool objetosGrandes;
        await using (var comando = new NpgsqlCommand(ConsultaResiduais, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                visoes.Add(leitor.GetFieldValue<uint>(0), (new NomeObjeto(leitor.GetString(1), leitor.GetString(2)), leitor.GetBoolean(3)));
            }

            await leitor.NextResultAsync(cancelamento).ConfigureAwait(false);
            await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
            objetosGrandes = leitor.GetBoolean(0);
        }

        // As lidas fora dos esquemas lidos (o do diário) ficam de fora: o Incognito não as atualiza.
        var dependencias = new List<(uint Visao, bool Funcao, uint? Lida)>();
        await using (var comando = new NpgsqlCommand(ConsultaDependenciasDosResiduais, conexao.Conexao))
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                dependencias.Add((leitor.GetFieldValue<uint>(0), leitor.GetBoolean(1), leitor.IsDBNull(2) ? null : leitor.GetFieldValue<uint>(2)));
            }
        }

        // Uma view do caminho pode ler de volta a própria materialized view ou outra que a lê (o banco aceita, e o REFRESH lê
        // o conteúdo antigo): a dependência que fecha o ciclo sai, e as do ciclo ficam incompletas, atualizadas até nada mudar.
        var lidas = visoes.Keys.ToDictionary(
            visao => visao,
            visao => dependencias.Where(dependencia => dependencia.Visao == visao && dependencia.Lida is { } lida && visoes.ContainsKey(lida)).Select(dependencia => dependencia.Lida!.Value).ToHashSet());
        var residuais = visoes
            .Select(visao => new ObjetoResidual
            {
                Nome = visao.Value.Nome,
                Tipo = TipoObjetoResidual.VisaoMaterializada,
                DependeDe = lidas[visao.Key]
                    .Where(lida => !Alcanca(lidas, lida, visao.Key))
                    .Select(lida => visoes[lida].Nome)
                    .OrderBy(nome => nome, OrdemDeNomes.Instancia)
                    .ToArray(),
                DependenciasIncompletas = dependencias.Exists(dependencia => dependencia.Visao == visao.Key && dependencia.Funcao)
                    || lidas[visao.Key].Any(lida => Alcanca(lidas, lida, visao.Key)),
                Populada = visao.Value.Populada,
            })
            .ToList();
        if (objetosGrandes)
        {
            residuais.Add(new ObjetoResidual { Nome = _objetosGrandes, Tipo = TipoObjetoResidual.ObjetosGrandes });
        }

        return residuais.OrderBy(objeto => objeto.Nome, OrdemDeNomes.Instancia).ToArray();
    }

    // Se a leitura de uma materialized view chega a outra (ou a ela mesma), seguindo as que cada uma lê.
    private static bool Alcanca(Dictionary<uint, HashSet<uint>> lidas, uint inicio, uint alvo)
    {
        var vistas = new HashSet<uint>();
        var pendentes = new Stack<uint>([inicio]);
        while (pendentes.TryPop(out var atual))
        {
            if (atual == alvo)
            {
                return true;
            }

            if (vistas.Add(atual))
            {
                foreach (var proxima in lidas[atual])
                {
                    pendentes.Push(proxima);
                }
            }
        }

        return false;
    }

    // Uma linha da consulta dos índices únicos, antes das colações e dos nomes das colunas: a chave, a colação de cada
    // elemento e os números citados pelas expressões e pelo predicado (nulos se o índice não os tem).
    private sealed record IndiceBruto(uint Tabela, string Nome, bool Primario, bool NulosDistintos, short[] Colunas, uint[] Colacoes, short[]? DasExpressoes, short[]? DoPredicado);

    /// <summary>As permissões lidas, por objeto, antes de seguirem a ordem do esquema.</summary>
    /// <param name="Banco">As permissões no banco.</param>
    /// <param name="LimparObjetosGrandes">Se o login apaga os large objects.</param>
    /// <param name="Tabelas">As permissões de cada tabela.</param>
    /// <param name="Objetos">Se o login limpa cada materialized view e ajusta cada sequence (marcada).</param>
    internal sealed record AcessoBruto(
        PermissoesBanco Banco, bool? LimparObjetosGrandes, Dictionary<NomeObjeto, PermissoesTabela> Tabelas, Dictionary<(NomeObjeto Nome, bool Sequence), bool?> Objetos);

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
