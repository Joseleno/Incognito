-- O banco do diário: as chaves estrangeiras e as triggers que o diário remove, desliga e restaura. O banco é do
-- administrador; os objetos, do dono do destino, que só ganha o direito de criar esquema nele (o diário). Sem o esquema
-- incognito: é o diário que o cria.

SET ROLE incognito_dono;

CREATE SCHEMA loja;
CREATE TABLE loja.lojas (id integer PRIMARY KEY, codigo text UNIQUE, a integer, b integer, UNIQUE (a, b));
INSERT INTO loja.lojas VALUES (1, 'um', 1, 1), (2, 'dois', 2, 2);

-- Validada com ações e adiável; composta com MATCH FULL; de texto (a do órfão); e uma não validada.
CREATE TABLE loja.vendas (
    id integer PRIMARY KEY,
    loja integer,
    codigo text,
    a integer,
    b integer,
    CONSTRAINT fk_vendas_lojas FOREIGN KEY (loja) REFERENCES loja.lojas (id) ON UPDATE SET NULL ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT fk_vendas_par FOREIGN KEY (a, b) REFERENCES loja.lojas (a, b) MATCH FULL,
    CONSTRAINT fk_vendas_codigo FOREIGN KEY (codigo) REFERENCES loja.lojas (codigo)
);
ALTER TABLE loja.vendas ADD CONSTRAINT fk_vendas_pendente FOREIGN KEY (loja) REFERENCES loja.lojas (id) NOT VALID;
INSERT INTO loja.vendas VALUES (1, 1, 'um', 1, 1);

-- A da outra definição, numa tabela comum.
CREATE TABLE loja.compras (id integer PRIMARY KEY, loja integer, CONSTRAINT fk_compras_lojas FOREIGN KEY (loja) REFERENCES loja.lojas (id) ON DELETE RESTRICT);
INSERT INTO loja.compras VALUES (1, 2);

-- De particionada: do 15 ao 17, o banco não a aceita sem validar, e a recriação valida direto.
CREATE TABLE loja.pedidos (loja integer NOT NULL, id integer NOT NULL, PRIMARY KEY (loja, id), CONSTRAINT fk_pedidos_lojas FOREIGN KEY (loja) REFERENCES loja.lojas (id)) PARTITION BY LIST (loja);
CREATE TABLE loja.pedidos_1 PARTITION OF loja.pedidos FOR VALUES IN (1);
CREATE TABLE loja.pedidos_2 PARTITION OF loja.pedidos FOR VALUES IN (2);
INSERT INTO loja.pedidos VALUES (1, 1), (2, 1);

-- Não imposta, no 18.
CREATE TABLE loja.vinculo (loja integer);
DO $$
BEGIN
    IF current_setting('server_version_num')::integer >= 180000 THEN
        EXECUTE 'ALTER TABLE loja.vinculo ADD CONSTRAINT fk_vinculo_solto FOREIGN KEY (loja) REFERENCES loja.lojas (id) NOT ENFORCED';
    END IF;
END
$$;

-- Triggers em cada estado numa tabela comum; na particionada, a cópia desligada em duas partições (uma antes da mãe na
-- ordem dos nomes, que religar a mãe sem ONLY tornaria a ligar) e uma trigger própria noutra; na mãe de herança, só a
-- dela (a da filha é de outra árvore).
CREATE FUNCTION loja.disparar() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN RETURN NEW; END$$;
CREATE TABLE loja.disparos (id integer);
CREATE TRIGGER tg_ligada BEFORE INSERT ON loja.disparos FOR EACH ROW EXECUTE FUNCTION loja.disparar();
CREATE TRIGGER tg_desligada BEFORE INSERT ON loja.disparos FOR EACH ROW EXECUTE FUNCTION loja.disparar();
CREATE TRIGGER tg_replica BEFORE INSERT ON loja.disparos FOR EACH ROW EXECUTE FUNCTION loja.disparar();
CREATE TRIGGER tg_sempre BEFORE INSERT ON loja.disparos FOR EACH ROW EXECUTE FUNCTION loja.disparar();
ALTER TABLE loja.disparos DISABLE TRIGGER tg_desligada;
ALTER TABLE loja.disparos ENABLE REPLICA TRIGGER tg_replica;
ALTER TABLE loja.disparos ENABLE ALWAYS TRIGGER tg_sempre;
CREATE TRIGGER tg_pedidos BEFORE INSERT ON loja.pedidos FOR EACH ROW EXECUTE FUNCTION loja.disparar();
CREATE TRIGGER tg_pedidos_1_propria BEFORE INSERT ON loja.pedidos_1 FOR EACH ROW EXECUTE FUNCTION loja.disparar();
ALTER TABLE loja.pedidos_2 DISABLE TRIGGER tg_pedidos;
CREATE TABLE loja.arquivo_de_pedidos PARTITION OF loja.pedidos FOR VALUES IN (3);
ALTER TABLE loja.arquivo_de_pedidos DISABLE TRIGGER tg_pedidos;
CREATE TABLE loja.pessoa (id integer);
CREATE TABLE loja.cliente () INHERITS (loja.pessoa);
CREATE TRIGGER tg_pessoa BEFORE INSERT ON loja.pessoa FOR EACH ROW EXECUTE FUNCTION loja.disparar();
CREATE TRIGGER tg_cliente BEFORE INSERT ON loja.cliente FOR EACH ROW EXECUTE FUNCTION loja.disparar();

RESET ROLE;
