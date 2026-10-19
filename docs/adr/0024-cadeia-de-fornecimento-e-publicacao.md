# ADR 0024 — Cadeia de fornecimento travada e publicação por Trusted Publishing

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0015](0015-apenas-net10.md) (SDK fixado e lock do núcleo), [ADR 0023](0023-gitflow-sem-historico-linear.md) (tags e branches), [ADR 0018](0018-lancamento-unico-com-versoes-candidatas.md) (lançamento único `1.0.0`, com versões candidatas internas)

## Contexto

O Incognito lê bases com dados pessoais e roda com credenciais de banco. Um pacote comprometido na cadeia dele, seja
uma dependência, uma action do CI ou o próprio pacote publicado, chega a esses dados. Os pontos de entrada são
conhecidos: uma versão nova de pacote resolvida sem ninguém ver, uma tag de action movida para outro código, um
segredo de longa duração vazado, um job com permissão de escrita rodando código de terceiros, um pacote publicado sem
prova de onde veio.

Algumas restrições do ambiente pesaram:

- A trava da restauração (`RestoreLockedMode`) poderia ficar no MSBuild, condicionada à variável `GITHUB_ACTIONS`. O
  Dependabot também roda no GitHub Actions, com essa variável, e precisa regenerar os lock files: com a trava herdada,
  a restauração dele falha (NU1004). O problema de lock file no Dependabot para NuGet está relatado em
  https://github.com/dependabot/dependabot-core/issues/13474.
- No nuget.org, a política de Trusted Publishing fica presa ao dono do repositório, ao repositório, ao nome do arquivo
  de workflow e ao environment, quando informado (aqui, `nuget`). Um workflow à parte só para validar a política exigiria outra política e não provaria
  a do workflow que publica.
- O lock do núcleo guarda o `Microsoft.NET.ILLink.Tasks` na versão do runtime do SDK (ADR 0015): atualizar o SDK muda
  o lock e exige regenerá-lo.

## Decisão

### Dependências

1. Toda versão de pacote fica no `Directory.Packages.props` (gerenciamento central, com fixação das transitivas);
   projetos não declaram versão.
2. Todo projeto versionado tem `packages.lock.json` versionado ao lado, ligado pelo `RestorePackagesWithLockFile` do
   `Directory.Build.props` da raiz. A trava fica nos workflows, não no MSBuild: todo `dotnet restore` dos workflows
   `ci`, `noturno`, `benchmark` e `release` usa `--locked-mode`.
3. O script `.github/scripts/conferir-dependencias.sh`, no job `rapido`, reprova projeto sem lock versionado e, em
   qualquer arquivo do MSBuild, `NuGetLockFilePath`, `RestorePackagesWithLockFile` fora do `Directory.Build.props` da
   raiz, `RestoreLockedMode`, `RestoreForceEvaluate`, `VersionOverride`, `PackageDownload`, `HintPath`, `Reference`,
   `Analyzer`, `UsingTask`, `DownloadFile` e `Exec`: o que baixaria pacote fora do grafo, referenciaria binário por
   caminho ou executaria código próprio no build.
4. As ferramentas do repositório (o validador de pacotes e o Stryker) ficam no `.config/dotnet-tools.json`, em versão
   exata e sem roll-forward. O SDK fica fixado no `global.json` (ADR 0015).
5. **Dependabot** (`.github/dependabot.yml`), semanal, às segundas às 06:00 de Brasília, para os ecossistemas `nuget`
   (diretórios `/` e `/benchmarks`) e `github-actions` (`/`):
   - versões menores e correções chegam juntas, num pull request por ecossistema (grupos `nuget` e `actions`);
   - no NuGet, cada versão maior chega num pull request próprio, um por dependência para os dois diretórios (grupo
     `nuget-maior`), e o BenchmarkDotNet, ainda na série 0.x, sempre num grupo só dele (`benchmarks`); nas actions,
     a versão maior chega fora do grupo, uma por pull request;
   - uma versão só é proposta depois de 7 dias publicada (`cooldown`); atualizações de segurança não esperam;
   - sem os rótulos padrão (`labels: []`), porque os do repositório são em português;
   - sem o ecossistema do SDK: subir o `global.json` é um pull request próprio, com os lock files regenerados.

   Os alertas e as atualizações de segurança do Dependabot ficam ligados no repositório.

### Workflows

6. Toda action é fixada pelo SHA completo do commit, com a versão em comentário (`uses: dono/action@<40 hex> # vX.Y.Z`),
   o formato que o Dependabot atualiza. O script `.github/scripts/conferir-actions-fixadas.sh`, no job `rapido`,
   reprova qualquer outra forma, e o repositório exige a fixação por SHA nas configurações do Actions.
7. Permissões mínimas: no nível do workflow, `{}` no `release`, no `noturno` e no `benchmark` e só leitura do conteúdo
   nos outros; cada job pede só o que usa. Todo checkout roda com `persist-credentials: false`.
8. O CodeQL analisa o C# e os próprios workflows (`build-mode: none`) em pull request e push para a `develop` e a
   `main` e toda semana. O OpenSSF Scorecard roda na `develop`, o branch padrão (o único aceito para publicar o
   resultado), em push, toda semana e quando muda uma regra de proteção.

### Publicação

9. **Tags.** O workflow `release` roda no push de tag `v*`. O job `preparar` recusa tag não anotada, tag cujo nome
   interno difere do nome da ref e tag que não foi assinada por uma chave SSH de assinatura cadastrada no GitHub pelo
   dono do repositório (`ssh-keygen -Y verify` contra as chaves dele, além da verificação do GitHub). Só
   `vX.Y.Z` com X ≥ 1 é lançamento, e precisa estar no histórico da `main`; `vX.Y.Z-rc.N` é ensaio; qualquer outra tag
   para o workflow.
10. **Pacotes sem credencial.** O job `pacotes` só lê o conteúdo: restaura travado, sem cache de NuGet, compila,
    testa, empacota e confere que há um `.nupkg` e um `.snupkg` por projeto de `src/`, todos na versão da tag, e que
    cada `.nupkg` traz o SBOM (SPDX 2.2, em `_manifest/`, gerado pelo `Microsoft.Sbom.Targets`). O build é
    determinístico, com Source Link e símbolos.
11. **Attestation num job à parte.** O job `atestar` é o único com a identidade OIDC antes da publicação: baixa os
    pacotes e roda só a action de attestation de origem (`actions/attest-build-provenance`), sobre todos os arquivos.
12. **Validação dos pacotes.** O job `validar-pacotes`, só com leitura, roda o `meziantou.validate-nuget-package` da
    versão fixada no manifesto de ferramentas sobre cada `.nupkg`: build determinístico, Source Link, símbolos e
    metadados.
13. **Publicação.** O job `publicar` só roda em tag de lançamento, depois de `preparar`, `pacotes`, `atestar` e
    `validar-pacotes`, no environment `nuget`. Ele obtém uma credencial temporária do nuget.org por Trusted Publishing
    (`NuGet/login`, com OIDC); nenhuma chave de API fica guardada no repositório, só o nome de usuário do nuget.org, no
    segredo `NUGET_USER`. Envia os pacotes com o SDK do `global.json` e `--skip-duplicate`, e cria o GitHub Release com
    `--verify-tag`.
14. **Environment `nuget`.** Exige a aprovação do mantenedor, administradores não o contornam, e só aceita execuções
    da `develop`, da `main` e de tags `v*`.
15. **Validação da política no próprio `release.yml`.** O disparo manual do `release` roda só o job
    `validar-publicacao`, no environment `nuget`, que obtém a credencial temporária e não envia nada. No nuget.org há
    uma única política: `release.yml`, environment `nuget`.
16. **Estrutura conferida.** O script `.github/scripts/testar-release.sh`, no job `rapido`, reprova o `release.yml`
    se a lista de jobs mudar (é fechada), se o `publicar` deixar de depender dos outros quatro ou de exigir tag de
    lançamento, se houver `id-token: write` ou permissão de escrita fora de `atestar`, `publicar` e
    `validar-publicacao`, environment fora de `publicar` e `validar-publicacao`, permissão no nível do workflow, se
    algum job além do `publicar` enviar pacotes ou criar Release, ou se algum além dele e do `validar-publicacao`
    obtiver credencial do nuget.org (a busca não diferencia maiúsculas). Também confere a classificação das tags e a
    presença das conferências de assinatura e da `main` no `preparar`.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| `RestoreLockedMode` no `Directory.Build.props`, condicionado ao CI | O Dependabot roda com `GITHUB_ACTIONS=true`, herda a trava e não consegue regenerar os lock files |
| Restaurar sem lock file, só com as versões centrais | Fixa as versões, mas não o hash do conteúdo, que o lock file guarda; um pacote com o mesmo número e outro conteúdo, vindo de outra fonte, passaria |
| Actions por tag (`@v7`) | Uma tag pode ser movida para outro código sem que o repositório perceba |
| Chave de API do nuget.org em segredo | Credencial de longa duração, que vaza com um log ou um job comprometido; o Trusted Publishing troca a identidade do workflow por uma credencial temporária |
| Workflow à parte para validar o Trusted Publishing | A política fica presa ao nome do arquivo: ele precisaria da sua própria política e não provaria a do `release.yml` |
| Attestation no job que compila ou no que publica | No que compila, o código de terceiros do build (pacotes, testes, ferramentas) teria acesso à identidade OIDC; no `publicar`, que só roda no lançamento, as versões candidatas ficariam sem attestation |
| Dependabot também para o SDK | Subir o `global.json` sem regenerar os lock files quebra a restauração travada |
| Atualizações sem espera (`cooldown`) | Esperar 7 dias dá tempo de uma versão maliciosa ser descoberta e retirada antes de chegar aqui; as atualizações de segurança não esperam |

## Consequências

- Todo pacote novo ou atualizado exige `dotnet restore --force-evaluate` e os lock files no mesmo commit; um lock
  desatualizado quebra o CI.
- Chegam pull requests do Dependabot toda semana, contra a `develop`, e passam pelo mesmo CI.
- O que fica fora do lock: a restauração própria do CodeQL, a do projeto que o BenchmarkDotNet gera ao medir e as
  ferramentas do `.config/dotnet-tools.json`, fixadas por versão exata, não por hash. Nenhum deles vai para os pacotes.
- O `release` restaura o manifesto de ferramentas inteiro no `validar-pacotes` e baixa também o Stryker, sem
  executá-lo (ADR 0022).
- Nenhum lançamento sai sem a aprovação do mantenedor no environment, nem sem tag assinada por ele: a publicação não é
  automática.
- A assinatura é exigida da tag, não dos commits.
- A política do nuget.org depende do nome do arquivo `release.yml` e do environment `nuget`: renomear um dos dois
  exige outra política e outra validação.
- As versões candidatas geram os pacotes, o SBOM e a attestation como artefato do workflow, sem envio ao nuget.org; a
  origem de um pacote baixado se confere com `gh attestation verify <arquivo>.nupkg --repo Joseleno/Incognito`.

## Evidência e verificação

- Dependências: `Directory.Packages.props`, `Directory.Build.props`, os `packages.lock.json` de cada projeto,
  `.config/dotnet-tools.json`, `global.json` e `.github/scripts/conferir-dependencias.sh`.
- Workflows: `.github/workflows/ci.yml` (job `rapido` com `testar-release.sh`, `conferir-actions-fixadas.sh` e
  `conferir-dependencias.sh`), `release.yml`, `noturno.yml`, `benchmark.yml`, `codeql.yml` e `scorecard.yml`;
  `.github/dependabot.yml`.
- SBOM e versão: `src/Directory.Build.props` (`GenerateSBOM`, MinVer, símbolos e Source Link); build determinístico no
  `Directory.Build.props` da raiz.
- Configurações do repositório no GitHub, que não são arquivo versionado, conferidas pela API em 15/10/2026: fixação
  por SHA exigida no Actions; alertas e atualizações de segurança do Dependabot ligados; environment `nuget` com
  aprovação obrigatória do mantenedor, sem contorno por administradores, para `develop`, `main` e `v*`; um único
  segredo, `NUGET_USER`.
- Execuções do `release` (datas no horário de Brasília): o disparo manual de 09/10/2026 obteve a credencial
  temporária (`validar-publicacao` verde); os ensaios `v1.0.0-rc.0` (09/10/2026), `v1.0.0-rc.1` (10/10/2026) e
  `v1.0.0-rc.2` (15/10/2026) terminaram verdes.
- Como contribuir com pacotes e lock files: seção "Desenvolvimento" do [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Gatilho de revisão

- O primeiro lançamento `1.0.0`: conferir o envio por Trusted Publishing e o GitHub Release, e registrar numa seção
  "Atualizações".
- O Dependabot passar a regenerar lock files com a trava herdada do MSBuild, ou o NuGet ganhar trava por hash para as
  ferramentas locais.
- Mudança no Trusted Publishing do nuget.org que altere o que a política prende.
- Um segundo mantenedor com permissão de publicar.

## Atualizações

- 2026-10-19: o item 5 ganha o ecossistema `docker`, para as imagens do PostgreSQL dos testes de banco
  (`testes/CodeProcess.Incognito.Testes.PostgreSql/imagens/<versão>/Dockerfile`, uma pasta por versão principal). Cada
  `Dockerfile` tem só o `FROM` com tag e digest (`postgres:18.6-trixie@sha256:...`), lido pela fixture dos testes e
  nunca construído; a fixture recusa imagem sem digest. O Dependabot atualiza a versão menor e o digest, todas as
  imagens num pull request (grupo `imagens`), com o mesmo `cooldown`, e nunca a versão principal (`ignore` de
  `version-update:semver-major`), porque a versão principal de cada pasta é a que ela testa. A tag leva o sistema da
  imagem (`-trixie`), para uma troca da glibc, que muda a ordem das colações, chegar visível num pull request. O job
  `integracao` roda os testes sem o Ryuk do Testcontainers (imagem sem digest com acesso ao socket do Docker), num
  runner descartado no fim.
- 2026-10-19: no release, os testes de banco saem do job `pacotes` para um job próprio, `testes-de-banco` (só
  leitura, PostgreSQL 15 e 18, sem o Ryuk), e o `publicar` passa a esperá-lo: as dependências dos testes de banco
  (Testcontainers, o cliente do Docker e o que eles trazem) não executam no job que gera os pacotes. O item 10 continua:
  o `pacotes` testa sem banco e empacota.
