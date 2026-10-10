# Registros de decisão (ADR)

Cada decisão de arquitetura, de compatibilidade ou de processo que afeta quem usa ou contribui com o Incognito fica
registrada aqui, num arquivo `NNNN-titulo-curto.md` escrito a partir do [`modelo.md`](modelo.md), no mesmo pull
request que toma a decisão. Preferência de estilo sem efeito para quem usa ou contribui não precisa de ADR.

- Os números são atribuídos quando o assunto é identificado, antes de o ADR existir, e nunca são reutilizados. A
  tabela abaixo lista todos, inclusive os ainda não escritos. Um número reservado cuja decisão deixar de ser
  necessária ganha um arquivo com status "descartado" e o motivo.
- Depois do merge, um ADR só recebe correção de digitação, de link ou de fato que não altera a decisão. Fato novo
  (um custo descoberto, uma medição) entra numa seção "Atualizações" no fim, com data, sem reescrever o corpo.
  Mudança de decisão vira ADR novo, e o antigo passa a "substituído pelo ADR NNNN".

| ADR | Decisão | Status |
| --- | --- | --- |
| [0001](0001-projeto-em-portugues.md) | Projeto inteiramente em português | aceito |
| 0002 | Cópia com transformação, em vez de alterar o próprio banco ou copiar para arquivo | a registrar |
| 0003 | Permutação Feistel modular com HMAC e HKDF | a registrar |
| 0004 | Modos `efemero` e `reproduzivel` | a registrar |
| 0005 | Remover e recriar chaves estrangeiras com diário, em vez de desligá-las | a registrar |
| 0006 | Leitura em snapshot compartilhado | a registrar |
| 0007 | EF Core como fonte de regras, não como motor de cópia | a registrar |
| 0008 | Dialetos em pacotes separados, com contrato experimental e suíte de conformidade | a registrar |
| 0009 | Destino com esquema pré-existente e cópia só entre bancos do mesmo tipo | a registrar |
| 0010 | Falha segura para coluna suspeita sem regra | a registrar |
| 0011 | Núcleo sem dependências; configuração e execução no pacote `Motor` | a registrar |
| 0012 | Atualização normativa monitorada no repositório, sem rede em tempo de execução | a registrar |
| 0013 | Versão do transformador fixada na configuração e vetores fixos como contrato | a registrar |
| 0014 | Entrada inválida tratada preservando a invalidade | a registrar |
| [0015](0015-apenas-net10.md) | Apenas `net10.0` | aceito |
| 0016 | Proteção do destino por marcador e recusa de origem marcada | a registrar |
| 0017 | Contratos de extensão como classes abstratas e tipos do modelo sem construtor público | a registrar |
| 0018 | Lançamento único `1.0.0`, com versões candidatas internas | a registrar |
| 0019 | Chave de acesso de documentos fiscais eletrônicos: transformador e classificação do emitente | a registrar |
