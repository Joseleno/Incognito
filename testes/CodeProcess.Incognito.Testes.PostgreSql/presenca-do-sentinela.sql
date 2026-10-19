-- A presença do sentinela em cada lugar que o esquema de conformidade declara, lida pelo administrador: o canário só prova
-- que o texto não sai do dialeto se ele estiver mesmo lá. Devolve os lugares sem ele; vazia num banco-modelo certo. As
-- expressões passam pelo pg_get_expr, que mostra as constantes (o texto do nó as guarda em bytes).

SELECT lugar
FROM (VALUES
    ('default de coluna', EXISTS (
        SELECT 1 FROM pg_catalog.pg_attrdef d
        WHERE d.adrelid = 'loja.tipos'::regclass AND pg_catalog.pg_get_expr(d.adbin, d.adrelid) LIKE '%zqSENTINELAqz%')),
    ('default de domínio', EXISTS (
        SELECT 1 FROM pg_catalog.pg_type t WHERE t.oid = 'loja.codigo'::regtype AND t.typdefault LIKE '%zqSENTINELAqz%')),
    ('valor de coluna acrescentada (attmissingval)', EXISTS (
        SELECT 1 FROM pg_catalog.pg_attribute a
        WHERE a.attrelid = 'loja.tipos'::regclass AND a.attmissingval::text LIKE '%zqSENTINELAqz%')),
    ('CHECK', EXISTS (
        SELECT 1 FROM pg_catalog.pg_constraint k
        WHERE k.contype = 'c' AND k.conrelid = 'loja.tipos'::regclass AND pg_catalog.pg_get_constraintdef(k.oid) LIKE '%zqSENTINELAqz%')),
    ('predicado de índice', EXISTS (
        SELECT 1 FROM pg_catalog.pg_index i
        WHERE i.indexrelid = 'loja.ix_tipos_curto'::regclass AND pg_catalog.pg_get_expr(i.indpred, i.indrelid) LIKE '%zqSENTINELAqz%')),
    ('predicado de exclusão', EXISTS (
        SELECT 1 FROM pg_catalog.pg_constraint k
        WHERE k.contype = 'x' AND k.conrelid = 'loja.reservas'::regclass AND pg_catalog.pg_get_constraintdef(k.oid) LIKE '%zqSENTINELAqz%')),
    ('expressão da chave de partição', EXISTS (
        SELECT 1 FROM pg_catalog.pg_partitioned_table p
        WHERE p.partrelid = 'loja.agenda'::regclass AND pg_catalog.pg_get_expr(p.partexprs, p.partrelid) LIKE '%zqSENTINELAqz%')),
    ('limite de partição', EXISTS (
        SELECT 1 FROM pg_catalog.pg_class c
        WHERE c.oid = 'loja.regioes_sentinela'::regclass AND pg_catalog.pg_get_expr(c.relpartbound, c.oid) LIKE '%zqSENTINELAqz%')),
    ('expressão de coluna gerada', EXISTS (
        SELECT 1 FROM pg_catalog.pg_attrdef d
        WHERE d.adrelid = 'loja.calculos'::regclass AND pg_catalog.pg_get_expr(d.adbin, d.adrelid) LIKE '%zqSENTINELAqz%')),
    ('comentário de tabela', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_class'::regclass AND o.objoid = 'loja.tipos'::regclass AND o.objsubid = 0 AND o.description LIKE '%zqSENTINELAqz%')),
    ('comentário de coluna', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_class'::regclass AND o.objoid = 'loja.tipos'::regclass AND o.objsubid > 0 AND o.description LIKE '%zqSENTINELAqz%')),
    ('comentário de índice', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_class'::regclass AND o.objoid = 'loja.ix_tipos_curto'::regclass AND o.description LIKE '%zqSENTINELAqz%')),
    ('comentário de restrição', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_constraint'::regclass AND o.description LIKE '%zqSENTINELAqz%')),
    ('comentário de trigger', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_trigger'::regclass AND o.description LIKE '%zqSENTINELAqz%')),
    ('comentário de colação', EXISTS (
        SELECT 1 FROM pg_catalog.pg_description o
        WHERE o.classoid = 'pg_catalog.pg_collation'::regclass AND o.description LIKE '%zqSENTINELAqz%')),
    ('linhas', EXISTS (SELECT 1 FROM loja.tipos WHERE texto LIKE '%zqSENTINELAqz%')),
    ('estatística comum (valores mais comuns)', EXISTS (
        SELECT 1 FROM pg_catalog.pg_stats s
        WHERE s.schemaname = 'loja' AND s.tablename = 'tipos' AND s.most_common_vals::text LIKE '%zqSENTINELAqz%')),
    ('estatística comum (histograma)', EXISTS (
        SELECT 1 FROM pg_catalog.pg_stats s
        WHERE s.schemaname = 'loja' AND s.tablename = 'tipos' AND s.histogram_bounds::text LIKE '%zqSENTINELAqz%')),
    ('estatística estendida', EXISTS (
        SELECT 1 FROM pg_catalog.pg_stats_ext s
        WHERE s.statistics_schemaname = 'loja' AND s.statistics_name = 'est_tipos' AND s.most_common_vals::text LIKE '%zqSENTINELAqz%')),
    ('large object', EXISTS (
        SELECT 1 FROM pg_catalog.pg_largeobject_metadata l
        WHERE pg_catalog.convert_from(pg_catalog.lo_get(l.oid), 'UTF8') = 'zqSENTINELAqz')),
    ('política de segurança por linha', EXISTS (
        SELECT 1 FROM pg_catalog.pg_policy p
        WHERE p.polrelid = 'loja.segredos'::regclass AND pg_catalog.pg_get_expr(p.polqual, p.polrelid) LIKE '%zqSENTINELAqz%')),
    ('materialized view', pg_catalog.pg_get_viewdef('loja.resumo_vendas'::regclass) LIKE '%zqSENTINELAqz%'),
    ('corpo de função', EXISTS (
        SELECT 1 FROM pg_catalog.pg_proc p WHERE p.oid = 'loja.conferir()'::regprocedure AND p.prosrc LIKE '%zqSENTINELAqz%')),
    ('argumentos de trigger', EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger g
        WHERE g.tgrelid = 'loja.segredos'::regclass AND pg_catalog.encode(g.tgargs, 'escape') LIKE '%zqSENTINELAqz-argumento%')),
    ('WHEN de trigger', EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger g
        WHERE g.tgrelid = 'loja.segredos'::regclass AND pg_catalog.pg_get_triggerdef(g.oid) LIKE '%zqSENTINELAqz-quando%')),
    ('opções de foreign table', EXISTS (
        SELECT 1 FROM pg_catalog.pg_foreign_table f
        WHERE f.ftrelid = 'loja.leituras_arquivo'::regclass AND f.ftoptions::text LIKE '%zqSENTINELAqz%')),
    ('opções de servidor', EXISTS (
        SELECT 1 FROM pg_catalog.pg_foreign_server s WHERE s.srvname = 'remoto' AND s.srvoptions::text LIKE '%zqSENTINELAqz%')),
    ('opções de mapeamento de usuário', EXISTS (
        SELECT 1 FROM pg_catalog.pg_user_mapping m WHERE m.umoptions::text LIKE '%zqSENTINELAqz%')),
    ('locale de colação ICU', EXISTS (
        SELECT 1 FROM pg_catalog.pg_collation k
        WHERE k.collname = 'colacao_sentinela' AND pg_catalog.to_jsonb(k)::text LIKE '%zqsentin%')),
    ('regras de colação', pg_catalog.current_setting('server_version_num')::integer < 160000 OR EXISTS (
        SELECT 1 FROM pg_catalog.pg_collation k
        WHERE k.collname = 'colacao_regras' AND pg_catalog.to_jsonb(k)::text LIKE '%zqSENTINELAqz%'))
) AS l (lugar, presente)
WHERE NOT presente
ORDER BY lugar;
