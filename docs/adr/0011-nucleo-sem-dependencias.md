# ADR 0011 — Núcleo sem dependências; configuração e execução no pacote `Motor`

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0001](0001-projeto-em-portugues.md) (namespace raiz e nomes que não colidem), [ADR 0004](0004-modos-efemero-e-reproduzivel.md) (o motor é dono da chave no `efemero`), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (fachada fixa na versão 1), [ADR 0015](0015-apenas-net10.md) (alvo único e SDK fixado), [ADR 0008](0008-dialetos-com-contrato-experimental-e-suite-de-conformidade.md) (dialetos em pacotes separados, com contrato experimental e suíte de conformidade), [ADR 0017](0017-contratos-de-extensao-e-tipos-do-modelo.md) (contratos de extensão como classes abstratas)

## Contexto

O Incognito tem dois públicos. Um quer só gerar, validar ou pseudonimizar documentos brasileiros dentro da própria
aplicação ou dos testes, sem banco. O outro copia uma base inteira e precisa de configuração em YAML, plano, executor,
relatório, um dialeto com o driver do banco e, muitas vezes, da ferramenta de linha de comando.

Se tudo ficasse num pacote só, o primeiro público herdaria o que só o segundo usa:

- **O leitor de YAML.** O YamlDotNet usa reflexão no modo comum; o modo compatível com AOT exige um gerador de
  código e tipos preparados para ele, e o núcleo é compatível com AOT e trimming, com avisos tratados como erro. E
  quem instala o pacote para gerar um CPF de teste não deveria receber um leitor de YAML.
- **Os drivers.** O Npgsql e o Microsoft.Data.SqlClient têm árvores de dependência próprias. Quem usa PostgreSQL não
  deveria carregar o driver do SQL Server, e um alerta de vulnerabilidade num driver não deveria atingir quem nem
  conecta a banco.
- **A cadeia de fornecimento.** O núcleo guarda a chave e faz a criptografia. Cada dependência dele seria código de
  terceiros no mesmo processo que a chave, para todo mundo que instala o pacote.

A ferramenta trata dado pessoal e promete não fazer nenhuma chamada de rede além dos bancos; a única exceção prevista
é a verificação de versão no NuGet, da ferramenta de linha de comando, desligada por padrão. A promessa é mais fácil
de conferir quando os pacotes que leem e gravam os dados nem referenciam a pilha HTTP.

Falta decidir como o motor chega aos internos do núcleo (a permutação, os pseudonimizadores de cada documento, os
desfechos e as políticas). O `InternalsVisibleTo` resolveria, mas deixaria o motor preso a código que o analisador da
API pública não rastreia, e quem escreve um transformador próprio não teria o mesmo caminho.

Quando esta decisão foi tomada (15/10/2026), só o núcleo tinha código. Os pacotes `Motor`, `PostgreSql`,
`SqlServer`, `EntityFrameworkCore` e `Cli` existiam como esqueletos, e as regras abaixo já eram testadas, para valerem
desde a primeira linha deles.

## Decisão

1. **Núcleo sem dependências.** O pacote `CodeProcess.Incognito` depende só da biblioteca base do .NET: nenhum
   pacote de terceiros. Ele traz os dados brasileiros (gerador, validador, pseudonimizadores), a chave, a derivação,
   a permutação e o catálogo normativo, e vai trazer os transformadores embutidos (item 8). Ferramentas de build declaradas com
   `PrivateAssets="all"` (o analisador da API pública, o MinVer e o gerador de SBOM) não são dependências do pacote.
2. **Só o núcleo é compatível com AOT** (`IsAotCompatible`), o que liga os analisadores de trimming e AOT; com avisos
   como erro, um aviso deles quebra o build.
3. **Configuração e execução no `Motor`.** Configuração YAML, plano, executor, contrato de dialeto, diário, relatório
   e `AddIncognito` ficam no `CodeProcess.Incognito.Motor`, que depende do núcleo. O motor não referencia driver de
   banco nem EF Core (`Npgsql`, `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore` e os assemblies com esses
   prefixos).
4. **Dialetos isolados.** `CodeProcess.Incognito.PostgreSql` e `CodeProcess.Incognito.SqlServer` dependem do motor e
   do próprio driver (que entra com o código do dialeto), e nenhum referencia o outro. O
   `CodeProcess.Incognito.EntityFrameworkCore` depende do motor. A ferramenta `CodeProcess.Incognito.Cli` junta o motor e os dois dialetos.
5. **Sem HTTP nos pacotes que tocam os dados.** Núcleo, motor e dialetos não referenciam `System.Net.Http`. O
   `EntityFrameworkCore`, que lê o modelo e não os dados, e a ferramenta de linha de comando, que fará a
   verificação de versão, ficam fora da regra.
6. **Namespace raiz só com a superfície de uso direto** (ADR 0001, item 4): em 15/10/2026, `ChaveIncognito`,
   `GeradorBr`, `ValidadorBr`, `Pseudonimizador` e `IncognitoException`. O resto fica em sub-namespaces: `.Normas` é
   o único público nessa data; `.Criptografia`, `.Documentos`, `.Geracao` e `.Pseudonimizacao` são internos.
7. **`InternalsVisibleTo` só para o que nunca vira pacote:** o projeto de testes do núcleo
   (`CodeProcess.Incognito.Testes`) e os benchmarks (`CodeProcess.Incognito.Benchmarks`, com `IsPackable` falso)
   veem os internos do núcleo; o projeto de testes do motor (`CodeProcess.Incognito.Testes.Motor`), os do motor.
   Nenhum pacote publicado enxerga os internos de outro.
8. **O motor usa o núcleo só pela API pública.** Ele não usa a fachada `Pseudonimizador`, que tem domínios, versão e
   políticas fixos para o uso direto, nem `InternalsVisibleTo`. Planejado para a etapa do motor (nomes provisórios):
   as fábricas embutidas de cada transformador (`br.cpf@1` e os demais) ficam no núcleo, num sub-namespace próprio,
   consumindo os internos, e são expostas pelo contrato público de extensão (uma fábrica de transformador e os
   contextos de criação e de transformação); os desfechos e as políticas passam a um sub-namespace público. O motor e quem escreve um
   transformador próprio usam o mesmo contrato.
9. **Verificação por teste próprio**, sobre as referências que o compilador gravou em cada assembly
   (`Assembly.GetReferencedAssemblies()`), sem biblioteca de testes de arquitetura de terceiros.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Núcleo com o YamlDotNet (configuração no núcleo) | Quem usa só o gerador ou o validador herdaria um leitor de YAML e uma dependência de terceiros no processo da chave; e, para continuar compatível com AOT, o núcleo teria de usar o modo com gerador de código do YamlDotNet em todos os tipos da configuração |
| Pacote único com núcleo, motor e os dois drivers | Quem usa PostgreSQL carregaria o driver do SQL Server, e vice-versa; quem usa só o núcleo carregaria os dois. Uma vulnerabilidade em qualquer driver viraria alerta para todo mundo |
| Drivers no motor, dialetos só como código | Mesmo problema do pacote único, deslocado para o motor: todo dialeto carregaria todos os drivers |
| `InternalsVisibleTo` do núcleo para o motor | Os internos não passam pelo analisador da API pública: uma mudança neles quebraria o motor sem aviso no build do núcleo. Quem escreve um transformador próprio não teria acesso ao que o motor usa |
| Motor sobre a fachada `Pseudonimizador` | A fachada fixa domínios, versão 1 e políticas (ADR 0013, item 5). O motor precisa de domínio, versão, políticas e parâmetros por coluna; ampliar a fachada misturaria a API de uso direto com a de configuração |
| Biblioteca de testes de arquitetura (como NetArchTest) | Uma dependência a mais para quatro regras de referência que cabem em poucas linhas de reflexão |
| Conferir as dependências no `.nuspec` gerado | Pegaria a referência declarada sem uso no IL (limitação abaixo), mas o `.nuspec` só existe depois do `dotnet pack`, que roda no release e não no CI de pull request. O lock file cobre o mesmo caso na revisão |

## Consequências

- Quem usa só o núcleo instala um pacote sem dependências, utilizável em aplicação publicada com AOT ou trimming.
- O que um pacote de terceiros resolveria no núcleo vira código próprio, testado aqui.
- Copiar uma base exige um pacote de dialeto (que traz o motor) ou a ferramenta de linha de comando, que já traz os
  dois dialetos.
- Todos os pacotes saem na mesma versão: o motor e os dialetos dependem do núcleo, e um não evolui sem o outro.
- A etapa do motor fica maior: o núcleo precisa expor as fábricas, os contextos, os desfechos e as políticas como API
  pública, rastreada pelo analisador e mantida depois da `1.0.0`. Um interno de que o motor precise vira API pública,
  ou fica no núcleo atrás de uma fábrica.
- **Limitação conhecida da verificação.** `GetReferencedAssemblies` lista só os assemblies cujo código ou metadado é
  usado pelo IL. Um `ProjectReference` ou `PackageReference` sem uso não aparece, mas vira dependência do pacote
  mesmo assim. Em 15/10/2026 isso acontecia: o motor, ainda sem código, declarava o núcleo como dependência no
  `.nuspec`, e o assembly dele não o referenciava. Assim, um pacote de terceiros acrescentado ao núcleo e ainda não usado passaria por
  `Nucleo_nao_referencia_pacotes_de_terceiros`, e uma referência de projeto sem uso entre os dialetos passaria por
  `Dialetos_nao_se_referenciam`. Há mais dois furos: a regra do núcleo compara só o nome com os assemblies do
  framework compartilhado, então um pacote com o mesmo nome de um assembly do framework (como as versões fora de
  banda do `System.Text.Json`) passa; e a busca por `System.Net.Http` percorre só os assemblies deste repositório,
  não o que os drivers e os pacotes de terceiros usam por dentro. Esses casos aparecem no `packages.lock.json` do projeto, que o CI exige
  versionado e atualizado: pacote novo e referência de projeto entram no diff, e são pegos na revisão, não por teste.

## Evidência e verificação

- [`RegrasDeArquiteturaTestes`](../../testes/CodeProcess.Incognito.Testes.Arquitetura/RegrasDeArquiteturaTestes.cs):
  `Nucleo_nao_referencia_pacotes_de_terceiros` (toda referência do núcleo está no diretório do framework compartilhado
  `Microsoft.NETCore.App`), `Motor_nao_referencia_driver_de_banco_nem_EF_Core`, `Dialetos_nao_se_referenciam` (nas
  duas direções, seguindo as referências entre os assemblies do repositório) e
  `Nucleo_motor_e_dialetos_nao_usam_System_Net_Http`. A regra de nomes do ADR 0001 está em
  `Nenhum_tipo_nem_namespace_se_chama_Cpf_Cnpj_Email_ou_Dominio`. Rodam no job `rapido` do
  [`ci.yml`](../../.github/workflows/ci.yml), exigido pelo Ruleset, e no job `pacotes` do
  [`release.yml`](../../.github/workflows/release.yml).
- [`CodeProcess.Incognito.csproj`](../../src/CodeProcess.Incognito/CodeProcess.Incognito.csproj): `IsAotCompatible` e
  os dois `InternalsVisibleTo` do núcleo, só ali; o
  [`CodeProcess.Incognito.Motor.csproj`](../../src/CodeProcess.Incognito.Motor/CodeProcess.Incognito.Motor.csproj)
  concede o seu só ao projeto de testes do motor. Os benchmarks têm `IsPackable` falso em
  [`benchmarks/Directory.Build.props`](../../benchmarks/Directory.Build.props).
- O [lock file do núcleo](../../src/CodeProcess.Incognito/packages.lock.json) só tem ferramentas de build: o analisador
  da API pública, o MinVer, o gerador de SBOM e o `Microsoft.NET.ILLink.Tasks`, trazido pelo `IsAotCompatible`
  (ADR 0015). O job `rapido` roda [`conferir-dependencias.sh`](../../.github/scripts/conferir-dependencias.sh) (lock
  versionado em todo projeto, nada no MSBuild que escape dele) e `dotnet restore --locked-mode`.
- As referências entre projetos estão nos `.csproj` de `src/`: o motor referencia só o núcleo; os dialetos e o
  `EntityFrameworkCore`, só o motor; a ferramenta, o motor e os dois dialetos.
- A superfície pública do núcleo está em
  [`PublicAPI.Unshipped.txt`](../../src/CodeProcess.Incognito/PublicAPI.Unshipped.txt): o namespace raiz e o `.Normas`.
  Um símbolo público fora do arquivo quebra o build (RS0016, avisos como erro).
- Num `dotnet pack` local, o `.nuspec` do núcleo saiu com o grupo de dependências `net10.0` vazio, e o do motor com a
  dependência do núcleo. O assembly do motor, compilado em 2026-10-15, referenciava só o `System.Runtime`.
- O motor sem a fachada e sem `InternalsVisibleTo` é conferido na revisão: o único lugar onde o acesso aos internos
  do núcleo pode ser concedido é o `.csproj` do núcleo.

## Gatilho de revisão

- O núcleo precisar de algo que a biblioteca base do .NET não traga e que não caiba reimplementar, como uma primitiva
  criptográfica.
- O motor precisar de um interno do núcleo que não possa virar API pública nem ficar atrás de uma fábrica.
- Uma dependência proibida chegar ao `.nuspec` de um pacote gerado sem ser pega pelos testes nem pela revisão do lock
  file: aí a conferência das dependências do `.nuspec` entra no job `pacotes` do release.

## Atualizações

- 2026-10-16: o motor ganhou a leitura do `incognito.yaml` e a primeira dependência de terceiros, o YamlDotNet
  (18.1.0, MIT), usado só pelo `Parser` (eventos), sem o desserializador por reflexão nem o `YamlStream`, que estoura a
  pilha com aninhamento profundo e resolve aliases antes de qualquer validação. O modelo da configuração fica interno
  até o `Plano` decidir o que publica; os testes do motor o conferem pelo `InternalsVisibleTo` do item 7. O núcleo
  continua sem nenhuma dependência. O assembly do motor passa a referenciar o núcleo e o YamlDotNet; o que o YamlDotNet
  usa por dentro fica fora da busca por `System.Net.Http` (a limitação acima): a versão fixada não depende de nenhum
  pacote e não referencia `System.Net.Http`, conferido no lock file e na revisão de cada atualização.
