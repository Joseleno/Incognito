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
| [0003](0003-permutacao-feistel-com-hmac.md) | Permutação Feistel modular com HMAC e HKDF | aceito |
| [0004](0004-modos-efemero-e-reproduzivel.md) | Modos `efemero` e `reproduzivel` | aceito |
| 0005 | Remover e recriar chaves estrangeiras com diário, em vez de desligá-las | a registrar |
| 0006 | Leitura em snapshot compartilhado | a registrar |
| 0007 | EF Core como fonte de regras, não como motor de cópia | a registrar |
| 0008 | Dialetos em pacotes separados, com contrato experimental e suíte de conformidade | a registrar |
| 0009 | Destino com esquema pré-existente e cópia só entre bancos do mesmo tipo | a registrar |
| 0010 | Falha segura para coluna suspeita sem regra | a registrar |
| [0011](0011-nucleo-sem-dependencias.md) | Núcleo sem dependências; configuração e execução no pacote `Motor` | aceito |
| 0012 | Atualização normativa monitorada no repositório, sem rede em tempo de execução | a registrar |
| [0013](0013-versao-do-transformador-e-vetores-fixos.md) | Versão do transformador fixada na configuração e vetores fixos como contrato | aceito |
| [0014](0014-entrada-invalida-preservando-a-invalidade.md) | Entrada inválida tratada preservando a invalidade | aceito |
| [0015](0015-apenas-net10.md) | Apenas `net10.0` | aceito |
| 0016 | Proteção do destino por marcador e recusa de origem marcada | a registrar |
| [0017](0017-contratos-de-extensao-e-tipos-do-modelo.md) | Contratos de extensão como classes abstratas e tipos do modelo sem construtor público | aceito |
| [0018](0018-lancamento-unico-com-versoes-candidatas.md) | Lançamento único `1.0.0`, com versões candidatas internas | aceito |
| [0019](0019-chave-de-acesso-de-dfe.md) | Chave de acesso de DF-e pseudonimizada por partes, com o emitente classificado por campos preservados | aceito |
| [0020](0020-dados-de-teste-e-identificadores.md) | Dados de teste só sintéticos, com identificadores do gerador e âncoras didáticas | aceito |
| [0021](0021-normalizacao-de-texto-propria.md) | Normalização de texto própria e congelada, sem ICU nem cultura | aceito |
| [0022](0022-mutacao-noturna-por-fatias.md) | Mutação noturna do núcleo em fatias, com limite de 80% por fatia | aceito |
| [0023](0023-gitflow-sem-historico-linear.md) | Gitflow com `develop` e `main`, sem histórico linear | aceito |
| [0024](0024-cadeia-de-fornecimento-e-publicacao.md) | Cadeia de fornecimento travada e publicação por Trusted Publishing | aceito |
