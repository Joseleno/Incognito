# ADR 0022 — Mutação noturna do núcleo em fatias, com limite de 80% por fatia

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (máquina de referência dos benchmarks), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (geração de vetores recusada sob a mutação), [ADR 0024](0024-cadeia-de-fornecimento-e-publicacao.md) (actions fixadas e restauração travada)

## Contexto

Os testes do núcleo (`CodeProcess.Incognito`) provam o que conferem, mas não dizem o que deixariam passar. O núcleo
concentra o código em que um erro pequeno muda a saída sem quebrar nada à vista: dígitos verificadores, a permutação
Feistel, as máscaras e a derivação das chaves. O teste de mutação mede isso: altera o código (troca um operador, remove
um comando) e confere se algum teste falha. A meta é que pelo menos 80% das alterações sejam detectadas.

As medições de 14/10/2026, na máquina dos benchmarks do ADR 0003 (Intel Core i9-14900HX, Windows 11), com o
Stryker.NET 5.0.0, o runner do Microsoft Testing Platform (MTP, o que o projeto usa, fixado no `global.json`) e 4
execuções em paralelo, mostraram as restrições:

- O núcleo tem 2.857 mutantes criados e 2.247 testados, 2 deles com erro de execução (os outros não compilam ou o
  próprio Stryker os ignora).
- Com a cobertura teste a teste (`perTest`) e a suíte inteira, a captura quebra: instrumentado, o código fica cerca de
  10 vezes mais lento, um teste que passa de ~30 s é dado como esgotado e, daí em diante, quase todo teste seguinte
  falha na captura (mais de 100 testes "Dubious").
- Sem a cobertura teste a teste (`coverage-analysis: all`), cada mutante roda a suíte inteira; 4 em paralelo passam do
  tempo limite, e o Stryker conta o tempo esgotado ("Timeout") como detectado. Numa amostra de 372 mutantes da
  criptografia, 86 foram Timeout e o placar deu 98,15%; com `perTest` e sem os testes pesados, os mesmos 372 deram
  91,82%, com 24 sobreviventes que antes eram Timeout. Rechecar os 88 sobreviventes do núcleo com a suíte inteira, sem
  análise de cobertura (`off`), deu Timeout em 87, com o tempo padrão e com 60 s a mais.
- Com `perTest` e sem os testes pesados, a captura funciona e o núcleo inteiro levou 56 min: 94,03% no placar do
  Stryker e 86,9% contando só os mortos.
- O filtro de testes do Stryker (`test-case-filter`) vale só para o runner do VSTest, não para o MTP: o que roda sob
  a mutação precisa ser escolhido pelos próprios testes.
- Na criptografia, boa parte dos Timeouts é real: remover um comando de descarte faz os testes de corrida girarem até
  o prazo, e mutantes em laços não terminam. Dobrar o `timeout-ratio` (de 2 para 4) quase não os mudou (de 77 para 74
  na mesma parte do núcleo).

O runner hospedado do GitHub tem 4 vCPU: o núcleo inteiro num job só passaria de 2 h, e o objetivo é que a rodada
caiba em 30 a 45 min.

## Decisão

1. **Ferramenta.** Stryker.NET na versão fixada no manifesto `.config/dotnet-tools.json` (hoje 5.0.0, sem
   roll-forward), só no projeto do núcleo, com a configuração versionada em
   `testes/CodeProcess.Incognito.Testes/stryker-config.json`: runner `mtp`, `coverage-analysis: perTest`,
   `concurrency` 4, `break-on-initial-test-failure`, `timeout-ratio` 2, `additional-timeout` 10.000 ms,
   `ignore-methods` `*Exception.ctor` (os textos passados a construtores de exceção) e limites `high` 90, `low` 80 e
   `break` 80.
2. **Gatilhos.** O workflow `noturno` roda todo dia às 06:17 UTC (03:17 em Brasília, fora do começo da hora), por
   disparo manual e em pull request que mude a própria mutação ou o que ela usa: o `noturno.yml`, o
   `stryker-config.json`, o `Mutacao.cs`, o `.config/dotnet-tools.json`, o `global.json`, os
   `Directory.Build.props` da raiz, de `src` e de `testes`, os `.csproj` do núcleo e dos testes e o
   `packages.lock.json` dos testes. Mudança no código do núcleo ou num arquivo de teste comum não dispara.
3. **Oito fatias em paralelo** (`fail-fast: false`), cada uma com o seu limite: `telefone`; `pix-email-cep`;
   `chave-dfe-cpf`; `numero-dfe-cnpj`; `feistel` (permutação, HMAC de semente, subchaves, conjunto excluído e
   variantes); `particionada-chave` (permutação particionada, `ChaveIncognito` e `Caracteres`); `gerador`
   (`GeradorBr`, `Geracao/` e a fachada `Pseudonimizador`); e `restante`, que é `**/*.cs` menos os padrões das outras
   sete, para um arquivo novo do núcleo sempre cair em alguma. O job `fatias`, de segundos, roda antes e reprova se um
   padrão não casar com nenhum arquivo, se um padrão se repetir, se as exclusões da `restante` não forem exatamente os
   padrões das outras ou se um arquivo cair em duas fatias.
4. **Limite.** Cada fatia passa com pelo menos 80% no placar do Stryker, em que o Timeout conta como detectado; abaixo
   disso, o Stryker sai com erro. A rodada só vale se, depois dela: o relatório e os logs existirem; nenhum log trouxer
   "Dubious", falha de captura da cobertura ou mutante não executado; não houver mutante pendente; fora os da
   inicialização estática, todo morto ou sobrevivente tiver os testes que o cobrem e todo morto tiver quem o matou,
   dentro deles; houver ao menos um mutante testado; e no máximo 25% dos testados forem Timeout.
5. **Concorrência.** As fatias `feistel` e `particionada-chave` rodam com 2 execuções em paralelo (campo
   `concorrencia` da matriz, passado em `--concurrency`); as outras, com 4. Na primeira rodada no runner, com 4
   execuções nas 4 vCPU, essas duas fatias passaram do teto de Timeout (59 de 221 e 57 de 223), contra 17% medidos sem
   disputa.
6. **Testes sob a mutação.** A suíte roda com `INCOGNITO_MUTACAO=1`, e com a variável ligada nenhum teste deve passar
   de ~1 s (regra da revisão, não conferida por teste; `testes/CodeProcess.Incognito.Testes/Mutacao.cs`): onde o tamanho é parâmetro (domínio, células,
   repetições), o teste usa `Mutacao.Tamanho(normal, sobMutacao)`, escolhido para cobrir as mesmas linhas, inclusive a
   corrida do descarte. São pulados, cada grupo com o seu motivo: os estatísticos pesados (`Mutacao.Motivo`), cujos
   valores críticos dependem do tamanho da amostra; os de alocação (`Mutacao.MotivoAlocacao`), porque o código
   instrumentado aloca; e os que leem o IL do núcleo (`Mutacao.MotivoIl`), que o Stryker reescreve. A geração de
   vetores fixos é recusada sob a mutação. Fora dela, nada muda.
7. **Guarda da variável.** No CI, a variável só vale no workflow de nome `noturno`: o teste
   `MutacaoTestes.No_CI_a_mutacao_so_vale_no_workflow_noturno` reprova com `GITHUB_ACTIONS=true`, a variável ligada e
   outro workflow, em todo job que roda a suíte do núcleo. Renomear o workflow quebra a guarda: o teste reprova na
   rodada inicial do Stryker e o noturno inteiro falha; o `noturno.yml` avisa isso na primeira linha.
8. **Exclusões.** Equivalentes e mensagens ficam como sobreviventes no relatório; nenhum arquivo de `src/` desliga a
   mutação (comentário `Stryker disable`) nem usa `ExcludeFromCodeCoverage`, que o Stryker também respeita, fora do
   inventário de `MutacaoTestes.So_os_dados_do_catalogo_normativo_ficam_fora_da_mutacao`. Hoje o inventário tem um
   arquivo, `Normas/ReferenciasNormativas.cs`, só com dados. Acrescentar outro muda o teste, à vista na revisão.
9. **Relatório.** Cada fatia publica o artefato `mutacao-<fatia>` (relatórios HTML, JSON e Markdown, os logs do
   Stryker e a saída do console), guardado por 30 dias, e o resumo do job traz o relatório em Markdown e a conta só com
   os mortos. A conferência, o resumo e o artefato saem também quando o limite reprova.
10. **Tempo.** O passo da mutação tem limite de 75 min e o job, de 90, provisórios até as primeiras rodadas agendadas
    medirem o tempo no runner.
11. **Fora do noturno.** O benchmark não faz parte da rodada: ele é medido na máquina de referência do ADR 0003, e o
    workflow `benchmark`, manual, mede no runner como número informativo.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| O núcleo inteiro num job só | Passaria de 2 h no runner, contra a meta de 30 a 45 min |
| Quatro fatias | Ficariam com 468 a 632 mutantes testados cada uma, e a da criptografia é a mais lenta por mutante; com oito, cada fatia tem de 221 a 312 |
| Cobertura sem teste a teste (`all`), com a suíte inteira | Cada mutante roda a suíte inteira, os paralelos esgotam o tempo e o Timeout infla o placar até perder o sentido |
| Pular todos os testes pesados, sem encolher | Já medido (94%), mas tira da medida a corrida do descarte, as bijeções exaustivas e a propriedade da chave de DF-e; encolhidos, eles continuam cobrindo as mesmas linhas |
| Escolher os testes pelo filtro do Stryker | O `test-case-filter` só vale para o VSTest; o projeto roda no MTP |
| Limite só com os mortos | Os Timeouts da criptografia são reais (dobrar o `timeout-ratio` não os tirou), e as duas fatias dela ficam abaixo de 80% nessa conta; o teto de 25% limita quanto o Timeout pode pesar |
| Concorrência 4 em todas as fatias | Nas da criptografia, a disputa pelas 4 vCPU leva o Timeout acima do teto |
| Exigir o `noturno` como check do pull request | Com o filtro por caminho, um check obrigatório que não dispara fica pendente e trava o merge; sem o filtro, todo pull request esperaria cerca de 30 min |
| Desligar a mutação nos equivalentes com comentário | Esconderia lógica sem teste; os equivalentes ficam visíveis como sobreviventes, e o placar passa com folga |

## Consequências

- O núcleo tem uma medida diária da força dos testes, por fatia, com o relatório de cada mutante.
- **O `noturno` não é check obrigatório.** Uma reprovação aparece na aba Actions e não bloqueia merge; quem mantém o
  projeto precisa olhar as rodadas agendadas.
- **Uma mudança no código do núcleo não roda a mutação no pull request.** Os caminhos do gatilho não incluem os `.cs`
  de `src/` nem os testes comuns: o efeito aparece na rodada agendada seguinte. Um teste novo pesado também só aparece
  ali, como falha da captura, e a mensagem aponta o `Mutacao.cs`.
- **O Timeout infla o placar.** Na rodada de 15/10/2026 no runner (a do pull request do catálogo normativo), contando
  só os mortos, as duas fatias da criptografia ficam abaixo de 80%:

  | Fatia | Testados | Placar do Stryker | Só os mortos | Timeout | Tempo do job |
  | --- | --- | --- | --- | --- | --- |
  | `telefone` | 312 | 97,4% | 94,9% | 2,6% | 10 min |
  | `pix-email-cep` | 302 | 98,3% | 91,1% | 7,3% | 10 min |
  | `chave-dfe-cpf` | 285 | 98,9% | 93,0% | 6,0% | 13 min |
  | `numero-dfe-cnpj` | 287 | 97,2% | 89,2% | 8,0% | 17 min |
  | `feistel` | 221 | 96,4% | 77,8% | 18,6% | 31 min |
  | `particionada-chave` | 223 | 92,8% | 71,3% | 21,5% | 28 min |
  | `gerador` | 268 | 94,8% | 88,4% | 6,3% | 6 min |
  | `restante` | 301 | 99,7% | 92,4% | 7,3% | 22 min |

  A `particionada-chave` ficou a 3,5 pontos do teto de 25%: variação do runner pode reprovar a rodada sem defeito novo.
- Os testes pesados medem tamanhos menores sob a mutação; o tamanho normal continua valendo em todo o resto do CI.
- A cobertura teste a teste não atribui a nenhum teste o código da inicialização estática: um mutante ali tende a
  sobreviver mesmo quando os testes o matariam. O erro é para menos, nunca infla o placar.
- O runner do Stryker para o MTP ainda é prévia: uma versão nova pode quebrar a captura, e as conferências do item 4
  fazem a fatia falhar em vez de passar com placar falso.
- Em repositório público, o GitHub desliga o agendamento depois de 60 dias sem atividade no repositório; é preciso
  reativá-lo na aba Actions.

## Evidência e verificação

- Workflow: `.github/workflows/noturno.yml` (gatilhos, job `fatias`, matriz, conferências, resumo e artefato).
- Configuração: `testes/CodeProcess.Incognito.Testes/stryker-config.json` e `.config/dotnet-tools.json`.
- Testes: `testes/CodeProcess.Incognito.Testes/Mutacao.cs`,
  `MutacaoTestes.No_CI_a_mutacao_so_vale_no_workflow_noturno` e
  `MutacaoTestes.So_os_dados_do_catalogo_normativo_ficam_fora_da_mutacao`; os usos de `Mutacao.Tamanho`,
  `Mutacao.Motivo`, `Mutacao.MotivoAlocacao` e `Mutacao.MotivoIl` nos testes do núcleo; a recusa em
  `GeracaoDeVetoresTestes`.
- Rodadas no runner em 14 e 15/10/2026 (horário de Brasília): as do pull request que criou o workflow (na primeira,
  as duas fatias da criptografia reprovaram no teto de Timeout com 4 execuções em paralelo; na última, com 2, as oito passaram, com
  placar entre 93,7% e 99,3%) e a da tabela acima (execução 38763048940, todas acima de 80%, cerca de 31 min de
  relógio). Os números da tabela saem do `mutation-report.json` de cada fatia; os artefatos expiram em 30 dias.
- Medições locais de 14/10/2026, citadas no contexto; os relatórios dessas rodadas não estão versionados.
- Como contribuir com a variável ligada: seção "Mutação noturna" do [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Gatilho de revisão

- As primeiras rodadas agendadas: com o tempo medido, os limites provisórios de 75 e 90 min baixam, e isso entra numa
  seção "Atualizações".
- Uma fatia passar do teto de 25% de Timeout, do limite de tempo ou de 45 min de forma recorrente.
- Uma versão maior do Stryker.NET, o runner do MTP sair da prévia ou passar a dar o tempo de cada teste.
- Um arquivo novo no inventário de exclusões de `MutacaoTestes`.
