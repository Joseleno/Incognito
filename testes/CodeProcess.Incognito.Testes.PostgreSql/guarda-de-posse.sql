-- A guarda da posse do destino, independente do leitor: os objetos de esquema do usuário que ainda são do administrador,
-- fora os membros de extensão. Os arrays e os tipos de linha seguem o tipo e a tabela, conferidos por eles. Vazia
-- depois da posse-do-destino.sql; um objeto novo no esquema de conformidade que ela não passe aparece aqui.

WITH administrador AS (
    SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user
),
membros AS (
    SELECT classid, objid FROM pg_catalog.pg_depend WHERE deptype = 'e'
),
objetos (classe, objeto, esquema, dono, descricao) AS (
    SELECT 'pg_catalog.pg_namespace'::regclass, n.oid, n.oid, n.nspowner, 'esquema ' || n.nspname
    FROM pg_catalog.pg_namespace n
    UNION ALL
    SELECT 'pg_catalog.pg_class'::regclass, c.oid, c.relnamespace, c.relowner, 'relação ' || c.oid::regclass::text
    FROM pg_catalog.pg_class c
    UNION ALL
    SELECT 'pg_catalog.pg_type'::regclass, t.oid, t.typnamespace, t.typowner, 'tipo ' || t.oid::regtype::text
    FROM pg_catalog.pg_type t
    WHERE t.typrelid = 0 AND NOT (t.typelem <> 0 AND t.typsubscript = 'pg_catalog.array_subscript_handler'::regproc)
    UNION ALL
    SELECT 'pg_catalog.pg_proc'::regclass, p.oid, p.pronamespace, p.proowner, 'rotina ' || p.oid::regprocedure::text
    FROM pg_catalog.pg_proc p
    UNION ALL
    SELECT 'pg_catalog.pg_collation'::regclass, k.oid, k.collnamespace, k.collowner, 'colação ' || k.collname
    FROM pg_catalog.pg_collation k
    UNION ALL
    SELECT 'pg_catalog.pg_statistic_ext'::regclass, s.oid, s.stxnamespace, s.stxowner, 'estatística ' || s.stxname
    FROM pg_catalog.pg_statistic_ext s
    UNION ALL
    SELECT 'pg_catalog.pg_operator'::regclass, o.oid, o.oprnamespace, o.oprowner, 'operador ' || o.oid::regoperator::text
    FROM pg_catalog.pg_operator o
    UNION ALL
    SELECT 'pg_catalog.pg_opclass'::regclass, o.oid, o.opcnamespace, o.opcowner, 'classe de operador ' || o.opcname
    FROM pg_catalog.pg_opclass o
    UNION ALL
    SELECT 'pg_catalog.pg_opfamily'::regclass, o.oid, o.opfnamespace, o.opfowner, 'família de operador ' || o.opfname
    FROM pg_catalog.pg_opfamily o
    UNION ALL
    SELECT 'pg_catalog.pg_conversion'::regclass, o.oid, o.connamespace, o.conowner, 'conversão ' || o.conname
    FROM pg_catalog.pg_conversion o
    UNION ALL
    SELECT 'pg_catalog.pg_ts_config'::regclass, o.oid, o.cfgnamespace, o.cfgowner, 'configuração de busca ' || o.cfgname
    FROM pg_catalog.pg_ts_config o
    UNION ALL
    SELECT 'pg_catalog.pg_ts_dict'::regclass, o.oid, o.dictnamespace, o.dictowner, 'dicionário de busca ' || o.dictname
    FROM pg_catalog.pg_ts_dict o
)
SELECT o.descricao
FROM objetos o
JOIN pg_catalog.pg_namespace n ON n.oid = o.esquema
WHERE o.dono = (SELECT oid FROM administrador)
  AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
  AND (o.classe, o.objeto) NOT IN (SELECT classid, objid FROM membros)
UNION ALL
SELECT 'large object ' || l.oid
FROM pg_catalog.pg_largeobject_metadata l
WHERE l.lomowner = (SELECT oid FROM administrador)
ORDER BY 1;
