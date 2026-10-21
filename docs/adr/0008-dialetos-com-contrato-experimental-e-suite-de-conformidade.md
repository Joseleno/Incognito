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
- O compilador acusa `INCX001` em código não experimental que use o contrato, inclusive dentro do motor, fora da pasta
  interna dele (`Motor/Interno/`, só tipos internos, com o pragma justificado em cada arquivo, conferida por
  `A_pasta_interna_do_motor_so_tem_tipos_internos_do_namespace_dela`): um tipo do contrato sem `[Experimental]` ou um
  tipo estável que exponha o contrato não compila.
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
- 2026-10-19: o modelo do esquema ganha a **semântica** que o `planejar` usa para decidir se um pseudônimo cabe na
  coluna e se a unicidade continua valendo: colação efetiva da coluna, as marcas do próprio tipo (ignora a caixa, ignora
  os espaços finais), comprimento declarado na unidade do banco, precisão e escala, faixa do inteiro, as colunas que uma
  coluna gerada cita e se uma restrição do tipo vale para a coluna, e as propriedades do banco (versão, codificação,
  colação padrão). Três princípios valem para todo campo novo: **nulo é o pior caso** (propriedade nova em tipo que já
  existe é anulável ou lista, e uma regra de arquitetura congela as antigas); **nenhum texto opaco sai do servidor**
  (só nomes de catálogo, números e marcas; uma regra de arquitetura exige uma lista aprovada para toda propriedade que é
  ou contém texto, e outra proíbe no dialeto PostgreSQL as funções e colunas do catálogo que devolvem definição,
  locale, comentário, estatística, opção ou segredo); e **o modelo não tem dicionário nem lista de valor** (a linhagem
  da identidade é a exceção nomeada). O comportamento de uma colação não determinística é **medido no servidor** com
  textos sintéticos, nunca deduzido do locale (que o próprio dono grava como texto livre e que regras de ordenação
  contradizem), e só quando a medida não pode abortar a leitura; as marcas medidas são o mínimo garantido, não o limite.
  Na suíte: o inventário passa a ser **total** (a situação, o tipo canônico e a semântica de toda coluna, declarados à
  mão a partir do script, e os objetos residuais), a lista de tipos escolhidos dá lugar à **cobertura da matriz** (todo
  valor de situação, tipo canônico e unidade aparece em alguma coluna ou é declarado inexistente com o motivo), entram
  os fatos das propriedades do banco e do **sentinela** (que deixa o projeto do dialeto e procura, nos dois lados e
  também com o administrador do servidor lendo, as formas literal, em bytes decimais e em hexadecimal), e a
  consistência separa as regras de coerência, que o motor repetirá tratando o nulo como pior caso, das de presença, que
  valem só para os dialetos do repositório. A perturbação do modelo gera sempre um valor válido e diferente (o `bool?`
  em três estados, os objetos pela anulabilidade declarada) e calcula a alteração antes de rodar a suíte.
- 2026-10-20: o modelo do esquema ganha os **objetos da tabela** que um pseudônimo pode fazer colidir ou violar. Os
  índices únicos entram **inclusive o da chave primária** (marcado) e o inválido ou em construção: o motor exige que os
  pseudônimos não colidam em todo índice listado, e não há campo de imposição, porque o único consumidor trata todos
  como impostos (um índice que só o pai tem, criado com `ON ONLY`, não impõe nada e mesmo assim é listado). Cada
  elemento da chave traz a coluna (nula num elemento de expressão) e a **colação do índice**, que pode diferir da
  coluna; das expressões e do predicado, só as colunas citadas. Os `CHECK`s, as exclusões (também a chave com
  sobreposição proibida) e as triggers de usuário entram com as colunas citadas e as marcas (imposto, ligada, herdada);
  o estado exato de uma trigger fica para o diário do dialeto. A chave estrangeira diz se o banco a confere agora e se
  as linhas que existiam quando a conferência foi ligada foram validadas, pela **família** dela (a original e as cópias
  nas partições, dos dois lados); nenhuma das duas marcas prova que não há linha órfã, o que só a verificação depois da
  carga confere. O objeto residual diz as materialized views que lê, passando pelas regras de leitura das views, se a
  leitura passa por função ou operador de usuário e se tem dados, tudo pelas dependências do catálogo, nunca pelo texto
  das regras; um ciclo entre materialized views (que o banco aceita, por views) sai das dependências e as deixa
  incompletas, para a atualização se repetir até nada mudar. Numa particionada, o índice anexado, o `CHECK` e a
  exclusão copiados numa partição ficam no pai, onde valem. As listas novas ficam vazias quando não há, o caso
  otimista; a suíte confere as dos dialetos do repositório pelo inventário, **inteiras e na ordem de nome**. A
  consistência confere a coerência, que o motor repetirá (no máximo um índice primário, igual à chave primária; colunas
  citadas que existem, na ordem da tabela; trigger herdada só em partição; dependências só de materialized views do
  esquema, em ordem e sem ciclo; nenhuma dependência nem marca de materialized view em outro residual), e, só nos
  dialetos do repositório, a presença (a chave primária tem índice primário, salvo numa partição ou com sobreposição
  proibida; o elemento de coluna de texto tem colação; o elemento de expressão tem as colunas citadas).
- 2026-10-20: o esquema ganha as **permissões do login** que o leu, numa subárvore só (`EsquemaBanco.Acesso`), a única
  que a comparação entre a origem e o destino deixa de fora: o leitor da origem e o dono do destino veem o mesmo
  esquema com permissões diferentes. As capacidades são **pela operação do motor**, não pelos verbos do banco (ler todas
  as colunas, gravar as da carga, gravar a identidade como veio, esvaziar, referenciar, alterar as restrições, desligar
  as triggers, limpar um objeto residual, ajustar uma sequence, marcar o banco, criar o esquema do diário), cada uma
  exigindo também o acesso ao esquema do objeto, e a **segurança por linha efetiva** para o login (a leitura sai filtrada;
  a carga em massa falha), não o que a tabela declara. As permissões vêm do catálogo atual, não do instante da leitura: o
  objeto que some no meio deixa as capacidades sem resposta, nunca "sim", e a segurança por linha dele é decidida pelo
  instante da leitura. Uma regra de arquitetura mantém os campos do login dentro dessa subárvore, pelo tipo e pelos
  nomes. A identidade passa a dizer o **transporte**: cifrado só quando a conexão exige a cifra (o que o servidor diz
  só rebaixa), o servidor verificado só com o nome conferido e se o servidor está na própria máquina, por endereço de
  loopback, `localhost` ou socket, nunca por prefixo do nome. O formato monetário da sessão passa a ser imposto (`C`) e
  conferido na abertura, em vez de lido: o modelo não ganha o único texto livre que o login poderia gravar. Na suíte, um fato confere as
  permissões de cada lado contra um padrão com exceções por objeto, toda tabela, todo objeto residual e toda sequence
  na ordem do esquema, e outro confere o transporte.
- 2026-10-20: o motor consome o contrato numa pasta interna (`Motor/Interno/`, namespace `CodeProcess.Incognito.Interno`),
  a única do motor, fora do contrato, em que o `INCX001` é suprimido, só por pragma justificado em cada arquivo; todo tipo dali é
  interno, e a constante do diagnóstico não é usada fora do contrato (ela marcaria um tipo como experimental e fugiria
  da busca pelo texto). Ali, um envoltório é o único que chama os pontos de extensão e traduz o que o dialeto lança (ADR
  0017, item 8). O contrato ganha `DialetoRecusouException`, para o dialeto recusar a configuração ou o ambiente pelo tipo
  (saída 2), nunca pela faixa do código; a suíte ganha um fato que conecta, nos dois lados, com uma string que o dialeto
  recusa (dada pela fixture, com um sentinela) e confere o tipo, a faixa do código e que a recusa não traz o que estava na
  string (a falta da interna é garantia do tipo, que não tem construtor com ela). O `ProvedorDialeto`
  passa a selar `ToString`, `Equals` e `GetHashCode` (texto do tipo e igualdade por referência): o motor guarda o
  provedor, e a leitura do IL não vê o receptor de uma chamada herdada de `Object`, então nenhum dialeto pode pôr código
  ali. O motor repete a coerência do esquema lido (`INC3002`, falha do dialeto), e um teste
  diferencial classifica cada defeito do falso que a consistência da suíte pega: coerência (o motor também recusa) ou só
  presença e formato (o motor trata o campo nulo como o pior caso).
- 2026-10-21: o contrato ganha `DialetoSql.CompararNome`, que diz, pela regra do banco daquela conexão, se um nome
  escrito na configuração designa um nome do catálogo (`CorrespondenciaNome`: corresponde, não corresponde ou
  indeterminada). É um predicado, e não uma dobra canônica, porque no SQL Server a igualdade segue a colação do catálogo;
  a resposta indeterminada deixa o dialeto falhar fechado quando a dobra depende do locale do servidor (o motor recusa o
  nome, nunca escolhe), e o casamento exato nunca vence. O motor também a usa para parear a origem com o destino (o nome
  de um lado, entre aspas, contra o do outro) e com nomes fixos, como os esquemas reservados, que podem não existir no
  catálogo. O motor só a chama pelo envoltório, que transforma um valor fora do enum em falha do dialeto. O contrato ganha também `EsquemaBanco.ObjetosNoEsquemaReservado`, os objetos do esquema
  `incognito`, cujas tabelas continuam fora das tabelas lidas: nulo é o pior caso (o dialeto não informa) e vazio quer
  dizer que não há nada, ao contrário das listas dos objetos da tabela, porque aqui o vazio otimista esconderia tabela ou
  dado no esquema reservado. Na suíte, um fato confere as comparações de nome que a fixture declara (com pelo menos um
  "corresponde" e um "não corresponde"), o inventário passa a conferir os objetos do esquema reservado na ordem e o fato do esquema
  `incognito` exige a lista; o falso ganha os defeitos `DobraComToLowerInvariant`, `IgnoraAspas` e
  `EscondeEsquemaReservado`.
