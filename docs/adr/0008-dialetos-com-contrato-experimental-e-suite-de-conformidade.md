# ADR 0008 — Dialetos em pacotes separados, com contrato experimental e suíte de conformidade

- Status: aceito
- Data: 2026-10-18
- Relacionados: [ADR 0011](0011-nucleo-sem-dependencias.md) (dialetos isolados, cada um com o próprio driver), [ADR 0017](0017-contratos-de-extensao-e-tipos-do-modelo.md) (contratos de extensão como classes abstratas)

## Contexto

O Incognito copia bases PostgreSQL e SQL Server, e outros bancos (MySQL, MariaDB, Oracle) ficam abertos a
contribuição. Cada banco tem catálogo, carga em lote, leitura consistente, bloqueio e restrições próprios; o motor
precisa do mesmo comportamento de todos eles. O ADR 0011 já separou os dialetos em pacotes, cada um com o próprio
driver, sem que um referencie o outro.

Faltava decidir três coisas: a forma do contrato que o motor usa para falar com um banco, como provar que um
dialeto o cumpre e o que acontece com o contrato até a `1.0.0`, quando o primeiro dialeto ainda nem existe e o segundo
só chega depois.

Os bancos não têm os mesmos recursos: partição é tabela no PostgreSQL e esquema de partição no SQL Server; herança e
large objects só existem no PostgreSQL; tabela temporal com histórico, máscara dinâmica e CDC só no SQL Server. Uma
suíte que pulasse o que um banco não tem esconderia justamente o leitor que inventa um recurso.

## Decisão

1. **Contrato no motor, experimental até a `1.0`.** O contrato fica no namespace `CodeProcess.Incognito.Dialetos` do
   pacote `CodeProcess.Incognito.Motor`, com todos os tipos públicos marcados `[Experimental("INCX001")]`. Quem
   implementa ou consome o contrato suprime o `INCX001` no próprio projeto, nunca num arquivo de configuração geral.
   Nenhum tipo estável do motor expõe tipo do contrato.
2. **Classes abstratas, como todo contrato de extensão (ADR 0017).** O ponto de entrada é o `ProvedorDialeto` (o nome
   usado na configuração e a conexão); a origem e o destino são tipos separados (`DialetoOrigem` e `DialetoDestino`),
   para o uso errado ser erro de compilação, sobre uma base comum (`DialetoBanco`: esquema, identidade e sintaxe). O
   descarte é um método-modelo (`DisposeAsync` chama `DisposeAsyncCore` uma vez só).
3. **O contrato cresce com cada parte implementada.** Entra agora o que a suíte já confere: a leitura do esquema (com o
   modelo do esquema), a identidade do banco e a citação de nomes. Bloqueio, marcador, diário, restrições, leitura dos
   dados e gravação entram com a tarefa que os implementa, junto com os testes da suíte que os provam. A leitura do
   esquema devolve tabelas, sequences e objetos residuais em ordem ordinal de esquema e nome, para o plano e o relatório
   saírem iguais entre leituras.
4. **A string de conexão é um `SegredoConexao`.** O `ToString` nunca a mostra, o depurador não a mostra na exibição
   padrão, e `Revelar` é o único ponto de leitura, para o dialeto entregá-la ao driver. Quando o motor passar a chamar
   os dialetos, uma exceção de dialeto que não seja `IncognitoException` será tratada como falha sem mensagem: as do
   driver podem trazer a string de conexão ou valores.
5. **Uma conexão de controle por banco.** O dialeto abre na conexão uma conexão de controle (metadados e, quando
   entrarem no contrato, bloqueio e diário) e não usa as partes de metadados em paralelo; as operações sobre dados,
   quando entrarem no contrato, abrirão as próprias conexões.
6. **Suíte de conformidade herdada, para os dialetos do repositório.** A suíte é a classe abstrata
   `ConformidadeDialetoTestes<TFixture>`, numa biblioteca de testes sem testes próprios e que não vira pacote; cada
   dialeto do repositório a herda numa classe concreta vazia do próprio projeto de testes e passa em tudo, sem teste
   pulado. Publicar a suíte para dialetos de fora é decisão futura.
7. **Catálogo de casos declarado pela fixture.** O que o esquema de conformidade tem é declarado pela fixture de cada
   dialeto (`EsquemaDeConformidade`), caso a caso: o recurso existe (com o que se espera ler) ou não existe (com o
   motivo). Inexistente é conferido: a suíte exige que o leitor não invente o recurso. Todo caso é obrigatório, então
   um caso novo obriga todo dialeto a declará-lo. Além dos casos, a suíte exige o esquema lido consistente (pai e
   histórico existentes, chave estrangeira balanceada e com colunas que existem, nomes sem repetição, tipo canônico só
   em coluna suportada), a mesma checagem que o motor fará ao receber o esquema.
8. **A suíte é provada contra um dialeto falso em todo pull request.** Os testes do motor rodam a suíte por reflexão
   contra um dialeto em memória: o falso certo passa em tudo, e cada defeito injetado derruba exatamente os testes que
   precisam pegá-lo, por asserção e com a mensagem do defeito. A suíte só tem fatos sem parâmetros, sem pulo nem
   execução explícita, na categoria de testes de banco.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Interfaces (`IDialetoBanco`) | Contraria o ADR 0017: um membro novo quebra quem implementa, e cada dialeto reescreveria o descarte |
| Um tipo só para origem e destino, com as partes de destino lançando exceção na origem | O erro só aparece na execução, e um dialeto de fora fica com metade das partes lançando exceção |
| Declarar agora todas as partes, com os membros que a especificação já descreve | O modelo que cada parte precisa (permissões, semântica de colação, estados das restrições) só se conhece ao implementá-la; o contrato ficaria adivinhado e seria reescrito |
| Uma descrição neutra única do esquema de teste | Partição, herança, temporal, segurança por linha e máscara não se representam igual nos dois bancos; viraria um pulo por banco |
| Provar a suíte com um projeto de testes do falso fora do CI | A prova deixaria de rodar em todo pull request |
| Publicar a suíte como pacote já na `1.0.0` | Ainda não há dialeto de fora; o pacote viraria contrato antes de a suíte se estabilizar com o segundo dialeto |
| Contrato sem ordem na leitura do esquema | Cada consumidor ordenaria por conta própria, e um banco sem `ORDER BY` daria plano e relatório diferentes entre execuções |
| `string` para a conexão | O segredo apareceria em qualquer `ToString`, interpolação ou depurador |

## Consequências

- Um dialeto do repositório implementa as classes abstratas e herda a suíte; a fixture dele declara o que o banco tem.
  Um dialeto de fora implementa o mesmo contrato, mas ainda não tem a suíte como pacote.
- Quem consome o contrato antes da `1.0` aceita acompanhar as mudanças (`INCX001`).
- Cada tarefa que acrescenta uma parte ao contrato acrescenta também os testes da suíte e os defeitos do falso que os
  provam.
- O falso e o mapa de defeitos viram manutenção: um teste novo na suíte sem defeito que o derrube quebra o mapa.
- O congelamento da API (antes da `1.0.0`) decide se o contrato sai de experimental.

## Evidência e verificação

- `SuiteDeConformidadeTestes` (nos testes do motor): o falso certo passa em tudo, nas variantes com e sem os recursos
  opcionais; cada defeito derruba exatamente os testes dele, por asserção; todo teste da suíte é derrubado por algum
  defeito; a suíte só tem fatos sem parâmetros, sem pulo, na categoria de banco.
- `RegrasDeArquiteturaTestes.O_contrato_de_dialeto_e_experimental_e_so_tem_classes_seladas_ou_abstratas` e
  `Nenhum_tipo_estavel_do_motor_expoe_o_contrato_de_dialeto`.
- `ProjetosDeTesteTestes.A_suite_de_conformidade_e_biblioteca_com_classes_de_teste_abstratas`,
  `O_INCX001_so_e_suprimido_nos_projetos_que_consomem_o_contrato_de_dialeto` e
  `So_os_dialetos_usam_a_suite_de_conformidade` (com a exceção dos testes do motor, para o falso).
- `ContratoDeDialetoTestes`: o segredo nunca aparece no texto, nomes comparados sem ignorar a caixa, modelo imutável.
- `PublicAPI.Unshipped.txt` do motor, com o prefixo `[INCX001]` em cada membro do contrato.
- O compilador acusa `INCX001` em código não experimental que use o contrato, inclusive dentro do motor: um tipo do
  contrato sem `[Experimental]` ou um tipo estável que exponha o contrato não compila.
- A sanitização das exceções de dialeto (item 4) e a conexão de controle (item 5) são verificadas pelas tarefas
  que as implementam: os testes do motor que chamam os dialetos e os da suíte para bloqueio e diário.

## Gatilho de revisão

- O primeiro dialeto real (PostgreSQL) e o segundo (SQL Server): o contrato e o catálogo de casos são revistos em
  cada um.
- O congelamento da API pública antes da `1.0.0` (o contrato sai ou não de experimental).
- O primeiro dialeto de fora que não consiga declarar um caso do catálogo.
- O primeiro pedido de um dialeto de fora para usar a suíte (publicá-la como pacote).

## Atualizações

- 2026-10-19: o primeiro dialeto, `CodeProcess.Incognito.PostgreSql` (PostgreSQL 15 em diante), implementa o contrato
  e herda a suíte numa classe selada. Os testes de arquitetura
  `Cada_dialeto_implementado_herda_a_suite_numa_unica_classe_selada` e `Os_projetos_de_dialeto_so_tem_testes_de_banco`
  exigem exatamente uma classe dessas por dialeto implementado e só testes de banco no projeto do dialeto. A fixture
  sobe um contêiner por execução (fixture de assembly), com três logins (o administrador monta; a origem é lida por um
  login sem superusuário que nunca é dono; o destino é de um dono sem superusuário), e clona um banco-modelo por teste.
  O dialeto impõe por cima da string de conexão o que o Incognito exige (sem detalhe de erro do servidor, sem registro
  de parâmetros, sem pool na conexão de controle, em UTF-8, sem senha em claro, só o `pg_catalog` no caminho de busca
  e, na origem, transações só de leitura), confere a sessão depois de abrir e recusa mais de um servidor. Os testes do
  dialeto rodam no job `integracao` do CI, nas versões suportadas, e no release.
- 2026-10-19: a fixture declara também o **inventário** do esquema de conformidade, escrito à mão a partir do script
  que o cria (nunca copiado da saída do leitor): toda tabela, com tipo, pais, chave de partição, estrangeira, extensão,
  segurança por linha, colunas na ordem (tipo nativo, anulável, identity, geração, tipo base), chave primária e chaves
  estrangeiras inteiras (colunas, tabela e colunas referenciadas), e toda sequence com as colunas que a usam. O fato
  `O_esquema_e_lido_sem_perdas` compara a leitura por nome, nos dois lados: **origem e destino têm o mesmo esquema**
  (dois bancos-modelo; no do destino a posse passa ao dono, conferida por uma guarda que não depende do leitor, e um
  terceiro papel sem login fica com alguns objetos), a origem lida pelo usuário só de leitura e o destino pelo dono, e o
  destino tem de sair igual à origem em tudo o que o modelo diz (a situação dos tipos inclusive, que o inventário não
  traz: ela fica para o modelo semântico). A consistência confere também as outras mães e o tipo delas, a chave de
  partição (sem coluna, só marcada incompleta) e chaves estrangeiras sem repetição; a leitura estável compara as duas
  leituras pelas propriedades públicas do modelo; e a classe derivada da suíte precisa ser selada e de primeiro nível. O
  projeto de testes do dialeto confere no próprio assembly compilado a classe da suíte e a categoria de cada teste (a
  regra de arquitetura lê o texto, que um `<Compile Remove>` ou um comentário enganam), e o banco-modelo confere,
  antes dos testes, que o sentinela está em cada lugar do esquema que o declara. Além do mapa de defeitos, os testes do
  motor alteram **cada propriedade de cada tipo do modelo** do esquema, por reflexão, num objeto do banco falso completo,
  e reordenam as listas cuja ordem é do contrato, exigindo que algum fato da suíte falhe citando o objeto: um campo novo
  do modelo sem fato que o confira quebra sozinho, e a reconstrução sem alteração passa em tudo.
