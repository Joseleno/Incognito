# ADR 0004 — Modos `efemero` e `reproduzivel`

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (permutação com chave), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (versão do transformador e vetores fixos), ADR 0011 (núcleo sem dependências; configuração e execução no pacote `Motor`, a registrar)

## Contexto

O Incognito troca dados pessoais por valores do mesmo formato com funções que dependem de uma chave secreta: a
permutação Feistel com HMAC e as sementes por HMAC, com subchaves derivadas por HKDF (ADR 0003). Com a mesma chave,
o mesmo domínio, a mesma versão do transformador e o mesmo ambiente, um valor dá sempre o mesmo pseudônimo, em
qualquer tabela; e quem tem a chave desfaz os valores permutados em microssegundos, porque a permutação tem inversa,
e recalcula os demais. A decisão é quem fica com essa chave e por quanto tempo.

Dois usos reais puxam em sentidos opostos:

- Cópia avulsa para desenvolvimento, homologação ou treinamento, em que ninguém precisa repetir os mesmos
  pseudônimos depois. Guardar uma chave que reverte a cópia inteira é risco sem benefício.
- Cópias que precisam bater entre si: o QA que reproduz um defeito com o mesmo cliente fictício em execuções
  diferentes, extrações repetidas da mesma base, bases diferentes que compartilham CPF ou CNPJ e precisam continuar
  ligadas, e o uso da biblioteca em testes e jobs. Sem a mesma chave, cada execução dá pseudônimos novos.

O enquadramento jurídico pesa no nome. A LGPD define o dado anonimizado (art. 5º, III) e não o considera dado
pessoal, salvo quando a anonimização puder ser revertida com meios próprios ou com esforços razoáveis, avaliados por
fatores objetivos como custo e tempo (art. 12, caput e §1º). A pseudonimização é definida no art. 13, §4º, no contexto de
estudos em saúde pública: o dado só volta a ser associado à pessoa com informação adicional mantida separadamente pelo
controlador. Mesmo com a chave descartada, uma cópia normalmente leva identificadores internos, datas e demais
atributos que a ligam à produção pelos meios próprios do controlador; o descarte da chave não decide sozinho se o
resultado é anonimizado. A primeira versão do desenho chamava os modos de `anonimizar` e `pseudonimizar`, e o primeiro
nome prometia um efeito jurídico que a ferramenta não entrega. O Guia de Anonimização e Pseudonimização da ANPD ainda
está em elaboração (Estudo Preliminar de dezembro de 2023; Tomada de Subsídios nº 1/2024).

## Decisão

1. Há dois modos de execução, nomeados pelo mecanismo e não pelo efeito jurídico: `efemero` e `reproduzivel`,
   escolhidos na configuração da cópia (`modo`).
2. **`efemero`**: a chave é gerada na memória, com o gerador criptográfico do sistema, no início da execução; nunca é
   gravada nem exportada, e é zerada ao final. Dentro da execução, o mesmo valor dá o mesmo pseudônimo em todas as
   tabelas, e as chaves estrangeiras continuam batendo; entre execuções, os pseudônimos não têm relação. A
   configuração com `chave` nesse modo é recusada, e o relatório não mostra impressão digital de chave.
3. **`reproduzivel`**: a chave é de quem executa e é obrigatória, lida por referência (variável de ambiente ou
   arquivo), nunca escrita por extenso na configuração. Mesma entrada, mesma chave, mesmo domínio, mesma versão do
   transformador e mesmo ambiente dão a mesma saída em qualquer tabela, banco ou execução. O relatório mostra a
   impressão digital da chave, que a identifica sem permitir recuperá-la.
4. Nos dois modos, o `ambiente` entra em todas as derivações de valores: a mesma chave em ambientes diferentes dá
   pseudônimos sem relação. A impressão digital da chave não depende do ambiente. A documentação recomenda uma chave por
   ambiente.
5. O enquadramento na LGPD fica só na documentação, com estes textos, sem alteração:

   | Modo | Referência | Texto |
   | --- | --- | --- |
   | `efemero` | Art. 12 (anonimização) como objetivo possível, não garantido | "Substitui identificadores diretos por valores gerados com chave aleatória descartada ao fim da execução. A substituição é irreversível pela ferramenta. A base resultante, porém, normalmente continua sendo dado pessoal para o controlador, porque identificadores internos, datas e demais atributos copiados permitem vinculá-la à produção por meios próprios (art. 12, caput e §1º; art. 5º, III). Só pode ser considerada anonimizada após avaliação de risco de reidentificação feita pelo controlador." |
   | `reproduzivel` | Art. 13, §4º (pseudonimização), por analogia | "Pseudonimização no sentido do art. 13, §4º, conceito que a lei define no contexto de estudos em saúde pública e que é aplicado aqui por analogia. O resultado é dado pessoal. A chave é a informação adicional que permite reverter todos os valores e deve ser mantida separada, em ambiente controlado, nunca no ambiente de destino." |

6. Nenhum texto do projeto (README, documentação, documentação XML, mensagens, relatórios, descrição dos pacotes)
   afirma que o uso da ferramenta cumpre a LGPD, assegura conformidade ou produz dado anonimizado por certificação,
   nem nada equivalente. O texto
   padrão do README diz que a ferramenta não garante anonimização nem conformidade e não constitui aconselhamento
   jurídico.
7. A fachada `Pseudonimizador` do núcleo não tem modo: exige uma `ChaveIncognito` e um `ambiente`, e a documentação
   dela usa o enquadramento do `reproduzivel` (o resultado continua sendo dado pessoal; quem tem a chave reverte todos
   os valores; a instância é tão sensível quanto a chave). O `efemero`, com as garantias do item 2, é do motor de cópia
   (pacote `CodeProcess.Incognito.Motor`), que é dono da chave durante a execução. Quem usa a biblioteca, gera uma
   chave com `ChaveIncognito.Gerar()` e a descarta reproduz o mecanismo por conta própria, sem a recusa de `chave` na
   configuração nem o relatório, e o núcleo não chama isso de `efemero`.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Um modo só, sempre com a chave de quem executa | Obriga a criar, guardar e proteger uma chave que reverte a cópia inteira até nas cópias avulsas, em que ninguém precisa repetir os pseudônimos. A chave guardada é o maior risco do modo |
| Um modo só, sempre com chave descartada | Perde a reprodução de defeitos entre execuções, a consistência entre extrações e entre bancos que compartilham documentos e o uso da biblioteca em testes e jobs |
| Valores aleatórios sem chave no modo descartável | Colidem (CPFs sorteados entre 10⁹ bases se repetem pelo paradoxo do aniversário) e não dão à mesma pessoa o mesmo pseudônimo em tabelas diferentes: quebram chaves primárias, restrições de unicidade e chaves estrangeiras |
| Nomes jurídicos (`anonimizar` e `pseudonimizar`) | O modo de chave descartada não garante dado anonimizado: identificadores internos, datas e demais atributos copiados ligam a cópia à produção por meios próprios do controlador (art. 12, caput e §1º; art. 5º, III). O nome prometia um efeito que depende de uma avaliação de risco do controlador, não da ferramenta |
| Modo na fachada do núcleo | A fachada não tem o ciclo de uma execução (início, fim, configuração, relatório). Um "efêmero" ali seria só uma chave gerada e descartada com o objeto, sem as garantias do item 2, com o mesmo nome do modo do motor. A chave obrigatória deixa claro o que a fachada é |

## Consequências

- No `efemero`, nem a ferramenta nem quem executa revertem os pseudônimos depois da execução, e duas cópias efêmeras
  da mesma origem não se ligam pelos pseudônimos. A base resultante, porém, normalmente continua sendo dado pessoal
  (item 5); a avaliação de risco é do controlador.
- No `efemero`, uma cópia interrompida não pode ser completada com os mesmos pseudônimos: executar de novo recomeça
  com outra chave. Transformadores com ruído aleatório perdem proteção em liberações repetidas, porque a média de
  várias cópias com ruído novo se aproxima do valor real; isso fica declarado na documentação desses transformadores.
- A zeragem da chave é de melhor esforço: o valor fica num `byte[]` fixado na memória e zerado no descarte, mas o
  contexto nativo do HMAC e as subchaves derivadas guardam cópias enquanto existem, e a chave lida de variável de
  ambiente já existe como texto imutável no processo.
- No `reproduzivel`, quem tem a chave reverte todos os valores de todas as cópias feitas com ela no mesmo ambiente: a
  chave fica fora do destino, e o vazamento dela compromete todas essas cópias. Trocar a chave, o ambiente ou o nome
  canônico de um domínio muda toda a saída.
- No `reproduzivel`, o mesmo pseudônimo em várias extrações do mesmo ambiente permite cruzá-las; é o que o modo
  promete. Ambientes diferentes e domínios versionados separam o que não deve se cruzar.
- Todo texto novo que descreve um modo usa o texto do item 5; a revisão de cada pull request confere isso.

## Evidência e verificação

- Chave: `ChaveIncognito.Gerar()` usa o `RandomNumberGenerator`, e `ChaveIncognitoTestes` prende as garantias
  (`Chaves_geradas_sao_diferentes`, `Descartar_zera_o_valor_e_impede_o_uso`, `ToString_mostra_so_a_mascara`,
  `Impressao_digital_sao_os_8_primeiros_bytes_do_HMAC_da_subchave_de_impressao`).
- Ambiente em todas as derivações: `SubchavesTestes.Dominios_versoes_variantes_e_ambientes_diferentes_dao_chaves_de_dominio_diferentes`
  e o vetor fixo `testes/vetores/derivacao@1.json`, conferido pelo C# e pela referência em Python
  (`testes/referencia/conferir_vetores.py`).
- Fachada sempre com chave e ambiente: `PseudonimizadorTestes` (`Construtor_recusa_chave_ou_ambiente_nulos_e_chave_descartada`,
  `Ambientes_diferentes_dao_pseudonimos_diferentes_e_o_mesmo_ambiente_o_mesmo`, `Chave_descartada_recusa_uso`).
  A reprodutibilidade entre processos e sistemas é provada por
  `VetoresFixosTestes.A_fachada_da_o_mesmo_que_os_vetores_com_os_dominios_padrao`, que roda no job `rapido` (Linux) e
  no job `nucleo-so` (Windows e macOS) do `ci.yml`.
- Textos: o texto padrão de LGPD está no `README.md`, e a documentação XML do `Pseudonimizador` usa o enquadramento do
  `reproduzivel`. `DocumentacaoTestes` (projeto de testes de arquitetura) reprova as afirmações proibidas em todo `.md`
  versionado; não cobre negações disfarçadas, a documentação XML, a descrição dos pacotes nem as mensagens, que ficam
  com a revisão de cada pull request.
- Referência normativa: a LGPD está no `CatalogoNormativo` (identificador `lgpd`), com a data da última revisão.
- O `efemero` e a escolha do modo são verificados quando o motor de cópia existir: a validação da configuração recusa
  `chave` com `modo: efemero` e exige `chave` em `reproduzivel`; duas execuções `reproduzivel` com a mesma chave dão os
  mesmos valores; duas execuções `efemero` dão valores diferentes, com as chaves estrangeiras íntegras em cada uma; o
  relatório do `efemero` não mostra impressão digital. Os testes entram numa seção "Atualizações" deste ADR.

## Gatilho de revisão

- Publicação do Guia de Anonimização e Pseudonimização da ANPD, ou de regulamento da ANPD sobre anonimização ou
  pseudonimização.
- Alteração dos arts. 5º, 12 ou 13 da LGPD.
- Decisão da ANPD ou de tribunal brasileiro sobre a identificabilidade de dado pseudonimizado para quem o recebe.
- Pedido de um terceiro modo (por exemplo, chave da execução guardada para auditoria) ou de modo na fachada do núcleo.
