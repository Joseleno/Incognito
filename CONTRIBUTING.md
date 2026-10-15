# Como contribuir

Obrigado pelo interesse. O Incognito está em desenvolvimento e ainda não tem versão publicada. Abrir uma issue ou uma discussão antes de escrever código evita trabalho perdido.

## Regras do projeto

- **Nunca dados reais.** Testes, exemplos, issues, pull requests e documentação usam apenas dados sintéticos. Identificadores como CPF, CNPJ e chaves de acesso de documentos fiscais vêm do gerador do próprio projeto, com semente registrada. Um identificador que exista em algum cadastro não entra no repositório, nem mesmo um publicado como "de teste" ou "de homologação". A única exceção são os exemplos didáticos que a Receita Federal publica em material sobre o cálculo do dígito verificador, usados apenas como âncora em `testes/ancoras/`.
- **Nenhuma saída revela dado tratado.** Mensagens, logs, exceções, relatórios e snapshots de teste identificam apenas tabela, coluna e número da linha, nunca o valor da coluna nem a chave.
- **Vetores fixos são contrato.** Se uma mudança altera a saída de um transformador, ela cria uma nova versão dele (por exemplo, `br.cpf@2`) e mantém a anterior. Um vetor existente em `testes/vetores/` nunca é editado para fazer um teste passar.
- **Tudo em português**: código, testes, mensagens, exceções, documentação XML e commits, com identificadores sem acento. Inglês apenas em palavras reservadas, tipos da biblioteca base, sufixo `Async`, padrão `Add*` no ponto de entrada da injeção de dependência e nomes de produtos, formatos e siglas. As regras completas, inclusive os nomes que a API evita, estão no [ADR 0001](docs/adr/0001-projeto-em-portugues.md).
- **O núcleo não depende de pacotes de terceiros.** O motor não depende de driver de banco nem do EF Core, e os dialetos não se referenciam. Os testes de arquitetura garantem essas regras.
- **A API pública é rastreada.** Membro público novo entra no `PublicAPI.Unshipped.txt` do projeto; sem isso, o build falha.
- **Decisões de projeto viram ADR** em [`docs/adr/`](docs/adr/README.md), no mesmo pull request que toma a decisão.
- **Sem promessa jurídica.** Nenhum texto do projeto promete conformidade com a LGPD nem anonimização garantida; o enquadramento jurídico usa os textos que já estão no README.

## Desenvolvimento

Requisitos: o SDK do .NET 10 na versão exata fixada no `global.json` e, para os testes de banco, Docker.

```
dotnet build -c Release
dotnet test
dotnet test --filter-not-trait "Categoria=Integracao"    # sem Docker
dotnet test --filter-not-trait "Categoria=Integracao" --filter-not-trait "Categoria=Lento"    # o que roda em todo PR
dotnet format --verify-no-changes
dotnet build benchmarks/Incognito.Benchmarks.slnx -c Release    # a solução dos benchmarks, à parte
dotnet format benchmarks/Incognito.Benchmarks.slnx --verify-no-changes
```

Cada projeto tem um `packages.lock.json` versionado, e o CI restaura com `dotnet restore --locked-mode`: um lock desatualizado quebra o build. Ao adicionar, remover ou atualizar um pacote (sempre no `Directory.Packages.props`), rode `dotnet restore --force-evaluate` e inclua os `packages.lock.json` alterados no commit. Projetos não usam `VersionOverride`, `PackageDownload`, referência a DLL por caminho nem tarefas próprias do MSBuild; o CI recusa.

### Vetores fixos

Cada arquivo de `testes/vetores/` (`transformador@versão.json`) é contrato: os casos que já estão lá nunca mudam; casos novos só se acrescentam. As receitas ficam em `testes/CodeProcess.Incognito.Testes/Vetores/ReceitasDeVetores.cs`, e cada par registra de onde veio a entrada (semente do `GeradorBr`, regra de construção ou permutação inversa). Uma referência independente em Python, só com a biblioteca padrão, confere todos os arquivos no CI, e uma guarda recusa a mudança de um caso que já existe na base do pull request.

Para acrescentar um caso, inclua-o na receita, gere a partir da raiz do repositório e confira com a referência (no Windows, `py -3 -I` no lugar de `python3 -I`):

```
INCOGNITO_GERAR_VETORES="br.cpf@1.json" dotnet test --project testes/CodeProcess.Incognito.Testes -c Release --filter-class "CodeProcess.Incognito.Testes.GeracaoDeVetoresTestes"
python3 -I testes/referencia/conferir_vetores.py --autoteste --base develop
```

Uma versão nova de um transformador (`br.cpf@2`) precisa da implementação nos dois lados: o executor em `ExecutorDeCaso` e o esquema em `EsquemaDeVetores`, no C#, e a classe com os parâmetros em `PARAMETROS` e `ARQUIVOS`, no Python. Sem isso, o arquivo da versão nova é recusado em vez de conferido com o algoritmo da anterior.

### Testes lentos e estatísticos

Testes marcados com `[Trait("Categoria", "Lento")]` ficam fora do CI de pull request e rodam em todo push na `develop` e na `main` (job `lentos`, que exige ao menos um) e no release. Ficam só no projeto `CodeProcess.Incognito.Testes`, o único que o job `lentos` roda. Os testes estatísticos (`EstatisticaTestes`) usam semente fixa e são de fumaça contra defeitos grosseiros de desenho; cada família de conferência roda também contra controles ruins que precisam reprovar cada conferência nomeada. Nunca troque a semente para um teste passar. A propriedade da inversa do Feistel usa o CsCheck com semente aleatória: numa falha, a mensagem traz a semente (`CsCheck_Seed`) para repetir.

### Benchmarks

Os benchmarks do núcleo ficam em `benchmarks/`, numa solução própria (`benchmarks/Incognito.Benchmarks.slnx`), fora do build dos pacotes. Rodam só em Release:

```
dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --filter "*Permutacao*"
```

O CI só compila, confere a formatação e roda uma iteração de cada (`--job Dry`); o programa sai com erro se algum benchmark falhar ou se o filtro não achar nenhum. Os números de referência ficam em `benchmarks/resultados/`, com a máquina declarada: só o relatório `*-report-github.md` e o CSV, nunca o `.log`, que traz caminhos da máquina. O workflow `benchmark`, manual, mede no runner do GitHub, como número informativo.

### Mutação noturna

O workflow `noturno` roda o Stryker.NET no núcleo todo dia, em fatias, com limite de 80% de mutantes detectados por fatia, e guarda o relatório como artefato. Ele roda a suíte com `INCOGNITO_MUTACAO=1`, que encolhe os testes pesados e pula os estatísticos pesados e os de alocação (`Mutacao.cs`): o Stryker captura a cobertura teste a teste com o código instrumentado, um teste lento demais quebra a captura, e o código instrumentado aloca. Um teste novo que passe de ~1 s com a variável ligada precisa de `Mutacao.Tamanho(normal, sobMutacao)` (ou, se o tamanho não puder mudar, `SkipWhen = nameof(Mutacao.Ativa)`); fora do noturno, o CI recusa a variável. Para medir uma parte do núcleo na sua máquina, a partir de `testes/CodeProcess.Incognito.Testes`, depois de um `dotnet build -c Debug --no-incremental` (o Stryker deixa a dll mutada no `bin`):

```
INCOGNITO_MUTACAO=1 dotnet stryker -m "**/Pseudonimizacao/PseudonimizadorCep.cs"
```

### Catálogo normativo

`src/CodeProcess.Incognito/Normas/ReferenciasNormativas.cs` lista as referências normativas que o pacote considera, cada uma com a data da última revisão; a data da revisão normativa do pacote é a mais antiga delas. Para rever uma referência, abra a fonte primária, atualize a observação e a data, e diga na observação o que foi confirmado só em fonte secundária. Acrescentar ou retirar uma referência muda também a lista em `CatalogoNormativoTestes`. Nenhum transformador do núcleo lê o catálogo (um teste confere no IL): se uma mudança normativa pede outra saída, ela vira uma nova versão do transformador.

O release, inclusive o ensaio `-rc.N`, exige que a revisão mais antiga tenha no máximo 30 dias em relação à data, em UTC, do commit da tag. Como vale a mais antiga, todas as referências precisam ser revistas e redatadas antes de cada release, e um catálogo vencido só aparece no job `pacotes`, depois do build.

## Branches

- `main` guarda apenas o código lançado; cada versão é uma tag na `main`.
- `develop` é a linha de integração.
- O trabalho acontece em branches criados a partir da `develop`: `feature/<descricao-curta>`, `fix/<assunto>` ou `docs/<assunto>`.
- Versões passam por `release/<versao>`, criado a partir da `develop`, mesclado na `main` e depois de volta na `develop`.
- Ninguém envia commits direto para `main` ou `develop`: toda mudança entra por pull request.

## Commits e pull requests

- Pull requests apontam para a `develop` (só os de `release/<versao>` apontam para a `main`), e o CI precisa estar verde antes do merge.
- Mensagens de commit em português, com a primeira linha no imperativo (por exemplo, "Adicionar o transformador de CEP").
- Um assunto por pull request. Mudanças que afetam quem usa os pacotes entram no `CHANGELOG.md`, em "Não publicado".
- Ao contribuir, você concorda que sua contribuição seja licenciada sob a [licença MIT](LICENSE).

## Suporte

Suporte em regime de melhor esforço, sem prazo garantido. Apenas a última versão publicada recebe correções.

- Perguntas e ideias: [Discussions](https://github.com/Joseleno/Incognito/discussions).
- Problemas e sugestões: [issues](https://github.com/Joseleno/Incognito/issues/new/choose), pelos formulários.
- Vulnerabilidades: em privado, conforme o [`SECURITY.md`](SECURITY.md).

Todas as interações seguem o [Código de Conduta](CODE_OF_CONDUCT.md).
