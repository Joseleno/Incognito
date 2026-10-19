-- A posse do banco-modelo do destino passa do administrador ao dono, objeto por objeto, depois do esquema de
-- conformidade: esquemas, relações (o ALTER no pai não desce às partições), tipos e domínios, funções, colações,
-- estatísticas estendidas e large objects. Nunca REASSIGN OWNED, que também passaria os bancos e os objetos
-- compartilhados. Ficam com o administrador os membros de extensão e os wrappers e servidores de foreign data (o dono
-- só ganha USAGE no servidor das foreign tables; o mapeamento de usuário não tem dono); com o terceiro papel, os objetos
-- dele; e seguem o dono da tabela as sequences ligadas a coluna (serial e identity), os índices, os tipos de linha e os
-- arrays.

GRANT USAGE ON FOREIGN SERVER arquivos TO incognito_dono;

DO $$
DECLARE
    administrador oid := (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user);
    comando text;
BEGIN
    FOR comando IN
        SELECT format('ALTER SCHEMA %I OWNER TO incognito_dono', n.nspname)
        FROM pg_catalog.pg_namespace n
        WHERE n.nspowner = administrador AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e WHERE e.classid = 'pg_catalog.pg_namespace'::regclass AND e.objid = n.oid AND e.deptype = 'e')
        UNION ALL
        SELECT format('ALTER %s %s OWNER TO incognito_dono',
                      CASE c.relkind WHEN 'f' THEN 'FOREIGN TABLE' WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' WHEN 'S' THEN 'SEQUENCE' ELSE 'TABLE' END,
                      c.oid::regclass)
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p', 'f', 'v', 'm', 'S') AND c.relowner = administrador
          AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e WHERE e.classid = 'pg_catalog.pg_class'::regclass AND e.objid = c.oid AND e.deptype = 'e')
          AND NOT (c.relkind = 'S' AND EXISTS (
              SELECT 1 FROM pg_catalog.pg_depend d
              WHERE d.classid = 'pg_catalog.pg_class'::regclass AND d.objid = c.oid
                AND d.refclassid = 'pg_catalog.pg_class'::regclass AND d.deptype IN ('a', 'i')))
        UNION ALL
        SELECT format('ALTER %s %s OWNER TO incognito_dono', CASE t.typtype WHEN 'd' THEN 'DOMAIN' ELSE 'TYPE' END, t.oid::regtype)
        FROM pg_catalog.pg_type t
        JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typtype IN ('d', 'e', 'r') AND t.typowner = administrador
          AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e WHERE e.classid = 'pg_catalog.pg_type'::regclass AND e.objid = t.oid AND e.deptype = 'e')
        UNION ALL
        SELECT format('ALTER ROUTINE %s OWNER TO incognito_dono', p.oid::regprocedure)
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        WHERE p.proowner = administrador AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e WHERE e.classid = 'pg_catalog.pg_proc'::regclass AND e.objid = p.oid AND e.deptype = 'e')
        UNION ALL
        SELECT format('ALTER COLLATION %I.%I OWNER TO incognito_dono', n.nspname, k.collname)
        FROM pg_catalog.pg_collation k
        JOIN pg_catalog.pg_namespace n ON n.oid = k.collnamespace
        WHERE k.collowner = administrador AND NOT starts_with(n.nspname, 'pg_') AND n.nspname <> 'information_schema'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e WHERE e.classid = 'pg_catalog.pg_collation'::regclass AND e.objid = k.oid AND e.deptype = 'e')
        UNION ALL
        SELECT format('ALTER STATISTICS %I.%I OWNER TO incognito_dono', n.nspname, s.stxname)
        FROM pg_catalog.pg_statistic_ext s
        JOIN pg_catalog.pg_namespace n ON n.oid = s.stxnamespace
        WHERE s.stxowner = administrador
        UNION ALL
        SELECT format('ALTER LARGE OBJECT %s OWNER TO incognito_dono', l.oid)
        FROM pg_catalog.pg_largeobject_metadata l
        WHERE l.lomowner = administrador
    LOOP
        EXECUTE comando;
    END LOOP;
END
$$;
