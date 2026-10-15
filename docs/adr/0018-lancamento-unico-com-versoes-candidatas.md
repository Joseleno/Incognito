# ADR 0018 — Lançamento único `1.0.0`, com versões candidatas internas

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0011](0011-nucleo-sem-dependencias.md) (pacotes na mesma versão), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (vetores fixos e a exceção antes do congelamento da API), [ADR 0015](0015-apenas-net10.md) (SDK fixado e lock files), [ADR 0023](0023-gitflow-sem-historico-linear.md) (branches e tags), [ADR 0024](0024-cadeia-de-fornecimento-e-publicacao.md) (estrutura do release e publicação)

## Contexto

O Incognito sai em seis pacotes (núcleo, motor, dois dialetos, EF Core e a ferramenta de linha de comando), que só
servem para copiar bancos quando estão todos prontos. Em 15/10/2026, só o núcleo funciona; os outros são esqueletos.
O trabalho até a `1.0.0` é feito em etapas (aqui chamadas de marcos): a fundação do repositório, o núcleo, o motor
com o PostgreSQL, o SQL Server, a descoberta com o EF Core e a ferramenta de linha de comando, o monitoramento e o
benchmark, e o endurecimento antes do lançamento.

Um pacote enviado ao nuget.org, em regra, não sai mais de lá: dá para tirá-lo da listagem, não apagá-lo. Quem
instalar uma versão intermediária guarda pseudônimos produzidos por ela, e o projeto passaria a ter duas escolhas
ruins: tratar como contrato as saídas de um produto incompleto (ADR 0013) ou mudá-las em silêncio para quem já as usa.

Ao mesmo tempo, publicar só no fim tem um risco próprio: o caminho de release (testes, empacotamento, versão, SBOM,
attestation de origem, validação dos pacotes, Trusted Publishing) seria executado de verdade pela primeira vez
no dia do lançamento. E o repositório é público desde o início: os pacotes de um marco precisam ser conferíveis por
quem quiser testá-los, sem passar pelo NuGet.

## Decisão

1. **Nada vai ao NuGet antes da `1.0.0`.** A primeira publicação é a `1.0.0`, com todos os pacotes de uma vez e na
   mesma versão.
2. **Versões candidatas internas.** Uma tag `vX.Y.Z-rc.N` executa o release inteiro, menos o envio: conferência da
   assinatura da tag, restauração travada, build, toda a suíte de testes, a exigência do catálogo normativo revisto,
   `dotnet pack`, a conferência de versão, quantidade e SBOM dos pacotes, a attestation de origem e a validação
   dos pacotes. Os pacotes ficam como artefato do workflow (`pacotes-<versão>`); não vão ao nuget.org e não viram
   GitHub Release.
3. **Um ensaio por marco, pelo menos.** Cada marco termina num ensaio verde, completado pela instalação dos pacotes do
   artefato num projeto limpo, a partir de um feed local. Os números só crescem; um número usado não volta.
4. **Tag imutável.** Toda tag é anotada e assinada por uma chave SSH de assinatura do dono do repositório. O Ruleset
   do GitHub impede apagar ou mover tags `v*`. Falha transitória no ensaio (runner, rede) vira nova execução do mesmo
   workflow; defeito no código ou nos pacotes vira correção por pull request e a tag `rc` seguinte.
5. **Versão pela tag.** O MinVer calcula a versão a partir da tag (`v1.0.0-rc.2` gera `1.0.0-rc.2`; sem tag, a base é
   `1.0.0-alpha.0.N`). Nenhum arquivo de projeto guarda o número da versão, e o release confere que todo pacote saiu
   com a versão da tag.
6. **Só a tag de lançamento publica.** `vX.Y.Z` com X maior ou igual a 1 é lançamento: tem de estar no histórico da
   `main` e, com aprovação no environment `nuget`, é enviada ao nuget.org por Trusted Publishing e vira GitHub
   Release. `vX.Y.Z-rc.N` é ensaio, com qualquer X. Qualquer outra tag, inclusive `vX.Y.Z` com X = 0, para o
   workflow.
7. **A `1.0.0` repete um ensaio.** Depois do merge do `release/<versão>` na `main`, a última versão candidata e a
   `v1.0.0` vão no mesmo merge commit da `main`: o que foi ensaiado é exatamente o que a `main` guarda. Essa versão
   candidata fica fora da `develop` até a volta da `main` para a `develop` (ADR 0023). Pelo menos uma versão
   candidata passa por testadores externos antes do lançamento.
8. **Até a `1.0.0`, nenhuma saída é contrato.** Pseudônimos e sequências do `GeradorBr` com semente podem mudar entre
   versões candidatas, e o README e o CHANGELOG dizem isso. Um vetor fixo da versão 1 só muda pela exceção do
   ADR 0013 (item 10): ADR próprio e aprovação explícita, até o congelamento da API pública.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Alfas ou betas no NuGet a cada marco (`0.1.0-alpha.1`, `0.2.0-alpha.1`) | Em regra, o pacote não sai do nuget.org depois de enviado: tirado da listagem, continua instalável pela versão exata. Cada pré-lançamento criaria usuários com pseudônimos guardados de um produto incompleto: ou as saídas viram contrato cedo demais, ou mudam para quem já as usa |
| Versões candidatas no NuGet como pré-lançamento | O mesmo problema, só que com o nome `rc`; e a versão candidata deixaria de ser ensaio para virar distribuição |
| Feed do GitHub Packages para as candidatas | O feed NuGet do GitHub Packages exige autenticação até para ler pacote público, e seria um segundo caminho de envio, com outra credencial, que não ensaia o do nuget.org |
| Publicar só no fim, sem ensaio | O release seria executado de verdade pela primeira vez no dia do lançamento, com tudo o que não foi exercitado antes |
| Apagar e recriar a tag quando o ensaio acha defeito | Haveria dois conjuntos de pacotes diferentes com a mesma versão, ambos com attestation, e quem baixou o primeiro não saberia que ele não corresponde mais à tag. O Ruleset não permite, e um número de versão candidata não custa nada |
| Versão escrita num arquivo do repositório | Duas fontes da verdade; a tag e o arquivo podem divergir. Com o MinVer a tag é a única, e o release confere o resultado |

## Consequências

- Ninguém usa os pacotes pelo NuGet antes da `1.0.0`: a validação externa vem de testadores com versões candidatas,
  no marco de endurecimento. Falhas que só apareceriam com uso real chegam mais tarde; o risco é aceito.
- O ensaio exercita tudo antes do job `publicar`, mas não ele: o envio ao nuget.org e a criação do GitHub Release só
  rodam de verdade na `1.0.0`. O login por Trusted Publishing é validado antes, por um disparo manual do próprio
  `release.yml` (job `validar-publicacao`), que só obtém a credencial temporária. O envio usa `--skip-duplicate`: se
  falhar no meio, a nova execução não para nos pacotes que já foram.
- A tag fica para sempre, inclusive a de um ensaio que falhou. A numeração não acompanha os marcos: o
  `v1.0.0-rc.0` (2026-10-09) e o `v1.0.0-rc.1` (2026-10-10) ensaiaram o mesmo marco, a fundação do repositório; o
  `v1.0.0-rc.2` (2026-10-15) ensaiou o núcleo (datas no horário de Brasília).
- O artefato do workflow expira pelo prazo de retenção do GitHub: a versão candidata não é distribuição durável, e
  quem testa baixa os pacotes nessa janela. A origem é conferível com `gh attestation verify`.
- Cada ensaio exige o catálogo normativo revisto nos 30 dias antes da data, em UTC, do commit da tag, como o
  lançamento.
- Entre duas tags, o MinVer gera versões como `1.0.0-rc.2.N` (N commits depois da tag), sempre abaixo de `1.0.0`.
- Depois da `1.0.0`, o mesmo workflow serve para as versões seguintes: `vX.Y.Z-rc.N` continua sendo ensaio.

## Evidência e verificação

- [`release.yml`](../../.github/workflows/release.yml): disparado por tag `v*`; a estrutura dos jobs está no
  [ADR 0024](0024-cadeia-de-fornecimento-e-publicacao.md). O job `publicar` tem
  `if: needs.preparar.outputs.tipo == 'lancamento'`: numa tag `rc`, é pulado.
- [`classificar-tag.sh`](../../.github/scripts/classificar-tag.sh): `vX.Y.Z` com X ≥ 1 é `lancamento`,
  `vX.Y.Z-rc.N` é `ensaio`, o resto é erro (inclusive `v0.9.0`, `-beta`, `-RC` e zero à esquerda).
- [`testar-release.sh`](../../.github/scripts/testar-release.sh), no job `rapido` do
  [`ci.yml`](../../.github/workflows/ci.yml): confere a classificação das tags, a lista fechada de jobs do release, que
  só `publicar` envia pacotes e cria GitHub Release, que ele está condicionado à tag de lançamento, e que só
  `publicar` e `validar-publicacao` obtêm credencial do nuget.org.
- [`src/Directory.Build.props`](../../src/Directory.Build.props): `MinVerTagPrefix` `v`, `MinVerMinimumMajorMinor`
  `1.0` e o SBOM em cada pacote.
- Tags existentes em 15/10/2026: `v1.0.0-rc.0`, `v1.0.0-rc.1` e `v1.0.0-rc.2`, todas anotadas e com assinatura SSH,
  em merge commits da `develop`.
- O aviso de estabilidade está no topo do [`README.md`](../../README.md) e em "Não publicado" do
  [`CHANGELOG.md`](../../CHANGELOG.md); a exigência do catálogo no ensaio, no
  [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- O Ruleset das tags `v*` (sem exclusão nem alteração) é configuração do repositório no GitHub, conferível nas regras
  do repositório.

## Gatilho de revisão

- A publicação da `1.0.0`: decidir se as versões seguintes ganham versões candidatas públicas no NuGet.
- Testadores do marco de endurecimento não conseguirem usar os pacotes do artefato (expiração, falta de feed): aí um
  feed para as candidatas volta à mesa.
- Uma falha no envio da `1.0.0` que o ensaio não tinha como pegar: aí o envio passa a ser ensaiado contra outro feed.
