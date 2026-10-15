# ADR 0023 — Gitflow com `develop` e `main`, sem histórico linear

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0024](0024-cadeia-de-fornecimento-e-publicacao.md) (publicação a partir da tag), [ADR 0018](0018-lancamento-unico-com-versoes-candidatas.md) (lançamento único `1.0.0`, com versões candidatas internas)

## Contexto

O Incognito separa o que está em desenvolvimento do que foi lançado: a `main` guarda só o código lançado, com a tag
de cada lançamento no merge commit que o trouxe, e a `develop` é a linha de integração, onde ficam, em geral, as tags
das versões candidatas (`vX.Y.Z-rc.N`), que ensaiam o release sem publicar nada (ADR 0018).

A versão dos pacotes vem das tags, pelo MinVer: ele procura a tag `v*` mais próxima entre os ancestrais do commit
compilado e calcula a versão a partir dela; sem tag alcançável, a versão é `1.0.0-alpha.0.N`. Por isso, depois de
cada lançamento, o commit da tag na `main` precisa virar ancestral da `develop`, ou a `develop` continua calculando a
versão a partir de uma tag antiga, como se o lançamento não tivesse acontecido. O workflow `release` também exige que
a tag de lançamento esteja no histórico da `main`.

A regra de histórico linear do GitHub só aceita squash ou rebase no merge. Os dois criam commits novos: a volta da
`main` para a `develop` não levaria o commit da tag, e o release seguinte, da `develop` para a `main`, reapresentaria
mudanças que a `main` já tem por outros commits. O gitflow precisa de merge commit, nas duas direções.

O projeto tem um mantenedor, que não pode aprovar o próprio pull request no GitHub.

## Decisão

1. **Branches.** A `main` guarda apenas o código lançado, e cada lançamento é uma tag num merge commit dela. A `develop` é a linha de
   integração e o branch padrão do repositório. O trabalho acontece em branches criados a partir da `develop`
   (`feature/<descricao-curta>`, `fix/<assunto>`, `docs/<assunto>`), com pull request para a `develop`. Um lançamento
   passa por `release/<versao>`, criado a partir da `develop`, com pull request para a `main`; depois, a `main` volta
   para a `develop` por pull request. A correção de uma versão lançada passa por `hotfix/<versao>`, criado a partir
   da `main`, com pull request para a `main`, versão candidata antes da tag da correção e, depois, a volta da `main`
   para a `develop` por pull request.
2. **Ruleset da `main` e da `develop`**, sem exceção para ninguém, inclusive administradores:
   - toda mudança entra por pull request, com 0 aprovações exigidas e as conversas resolvidas;
   - o único método de merge é o merge commit;
   - o job `rapido` do workflow `ci` é check obrigatório;
   - o branch não pode ser apagado nem receber force-push;
   - não há regra de histórico linear.
3. **Ruleset das tags `v*`**, também sem exceção: uma tag de versão não pode ser apagada, movida nem reescrita.
4. A tag de lançamento (`vX.Y.Z`) vai no merge commit da `main` que trouxe o `release/<versao>` ou o
   `hotfix/<versao>`, junto com a última versão candidata (ADR 0018), e o release confere que ela está no histórico
   da `main`. As outras versões candidatas ficam na `develop`.
5. O release faz o checkout com o histórico completo (`fetch-depth: 0`): o job `preparar` precisa dele para conferir
   que a tag de lançamento está na `main`, e o MinVer, para achar a tag entre os ancestrais do commit.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Histórico linear, com squash | O commit da tag não chega à `develop` na volta, e o MinVer na `develop` deixa de ver o lançamento; a `main` e a `develop` divergem a cada release |
| Histórico linear, com rebase | Reescreve os commits ao aplicá-los: os SHAs da `main` e da `develop` deixam de ser os mesmos, com o mesmo efeito sobre a tag |
| Um branch só (`main`), sem `develop` | Uma versão lançada não poderia receber correção sem levar junto o trabalho ainda não lançado; com a `main` guardando só o que foi lançado, a correção sai de um `hotfix/` a partir dela |
| Uma aprovação obrigatória por pull request | Com um mantenedor, que não pode aprovar o próprio pull request, nada entraria; a garantia vem do check obrigatório e da ausência de exceção no Ruleset |
| Exceção para administradores no Ruleset | Permitiria enviar direto para a `main` ou a `develop`, ou mover uma tag publicada, sem rastro de pull request |

## Consequências

- O histórico da `main` e da `develop` tem merge commits e não é uma linha só; `git log --first-parent` mostra a
  sequência de pull requests.
- Cada lançamento custa dois pull requests: `release/<versao>` (ou `hotfix/<versao>`) para a `main` e a volta da
  `main` para a `develop`.
- O mantenedor também passa por pull request e pelo check `rapido` para mudar a `main` ou a `develop`.
- Os checks além do `rapido` (os outros jobs do `ci`, o CodeQL e o `noturno`) são informativos: uma falha neles não
  impede o merge e precisa ser acompanhada.
- O check obrigatório não exige o branch atualizado com a base antes do merge; o push na `develop` roda o `ci` de novo
  sobre o resultado do merge.
- Squash e rebase continuam habilitados nas configurações do repositório, mas o Ruleset os recusa na `main` e na
  `develop`.
- Até o primeiro lançamento, a `main` fica parada no começo do projeto, e as tags existentes são todas de versões
  candidatas, na `develop`.

## Evidência e verificação

- Branches e pull requests: seções "Branches" e "Commits e pull requests" do
  [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- MinVer: `src/Directory.Build.props` (`MinVerTagPrefix` `v`, `MinVerMinimumMajorMinor` 1.0).
- Release: `.github/workflows/release.yml` (passo "Conferir que a tag de lançamento está na main" e `fetch-depth: 0`
  nos jobs `preparar` e `pacotes`), e `.github/scripts/testar-release.sh`, no job `rapido`, reprova se o passo da
  conferência sair do job `preparar`.
- `.github/workflows/ci.yml`: o job `rapido` é o exigido pelo Ruleset.
- Os Rulesets são configuração do repositório no GitHub, não arquivo versionado. Conferidos pela API em 15/10/2026:
  "main e develop" (`refs/heads/main` e `refs/heads/develop`; pull request com 0 aprovações, conversas resolvidas e
  só `merge`; check `rapido`; sem exclusão nem force-push; nenhuma exceção) e "tags de versão" (`refs/tags/v*`; sem
  exclusão, alteração nem force-push; nenhuma exceção). Branch padrão: `develop`.
- Em 15/10/2026, as tags `v1.0.0-rc.0`, `v1.0.0-rc.1` e `v1.0.0-rc.2` estão na `develop`, e nenhuma na `main`.

## Gatilho de revisão

- Um segundo mantenedor com permissão de merge: passa a valer exigir uma aprovação.
- A troca do MinVer por outra forma de calcular a versão que não dependa da ancestralidade das tags.
- O primeiro lançamento, para conferir na prática a volta da `main` para a `develop` e a versão calculada depois dela;
  o resultado entra numa seção "Atualizações".
