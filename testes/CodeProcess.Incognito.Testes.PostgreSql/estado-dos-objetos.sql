-- O estado dos objetos do esquema de conformidade que o script não garante sozinho, lido pelo administrador nos dois
-- bancos-modelo depois dos passos fora do script: o índice CONCURRENTLY que falhou ficou inválido, e as triggers de
-- integridade e de usuário estão no estado que o caso declara (a posse do destino não pode mudá-lo). Devolve os casos fora
-- do estado; vazia num banco-modelo certo.

SELECT caso
FROM (VALUES
    ('índice CONCURRENTLY inválido', EXISTS (
        SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = 'loja.ix_duplicadas_v'::regclass AND NOT i.indisvalid)),
    ('chave com as triggers da tabela desabilitadas', EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_constraint k ON k.oid = t.tgconstraint
        WHERE k.conname = 'fk_desligado' AND t.tgrelid = 'loja.vinculo_desligado'::regclass AND t.tgenabled = 'D')),
    ('índice só do pai inválido', EXISTS (
        SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = 'loja.ix_lotes_so_pai'::regclass AND NOT i.indisvalid)),
    ('chave com as quatro triggers só de réplica', (
        SELECT pg_catalog.count(*) FILTER (WHERE t.tgenabled = 'R') = 4 AND pg_catalog.count(*) = 4
        FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_constraint k ON k.oid = t.tgconstraint
        WHERE k.conname = 'fk_replica')),
    ('chave desligada só numa partição', EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = 'loja.remessas_2'::regclass AND t.tgisinternal AND t.tgenabled = 'D')
        AND EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = 'loja.remessas_1'::regclass AND t.tgisinternal)
        AND NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = 'loja.remessas_1'::regclass AND t.tgenabled <> 'O')),
    ('triggers de usuário em cada estado', (
        SELECT pg_catalog.string_agg(t.tgname || '=' || t.tgenabled::text, ',' ORDER BY t.tgname)
        FROM pg_catalog.pg_trigger t WHERE t.tgrelid = 'loja.disparos'::regclass) = 'tg_desligada=D,tg_esvaziar=O,tg_ligada=O,tg_replica=R,tg_sempre=A')
) AS c (caso, certo)
WHERE NOT certo
ORDER BY caso;
