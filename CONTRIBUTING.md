# Como contribuir

Obrigado pelo interesse. O Incognito está em desenvolvimento e ainda não tem versão publicada. Abrir uma issue ou uma discussão antes de escrever código evita trabalho perdido.

## Regras do projeto

- **Nunca dados reais.** Testes, exemplos, issues, pull requests e documentação usam apenas dados sintéticos. Identificadores como CPF, CNPJ e chaves de acesso de documentos fiscais vêm do gerador do próprio projeto, com semente registrada. Um identificador que exista em algum cadastro não entra no repositório, nem mesmo um publicado como "de teste" ou "de homologação". A única exceção são os exemplos didáticos que a Receita Federal publica em material sobre o cálculo do dígito verificador, usados apenas como âncora em `testes/ancoras/`.
- **Nenhuma saída revela dado tratado.** Mensagens, logs, exceções, relatórios e snapshots de teste identificam apenas tabela, coluna e número da linha, nunca o valor da coluna nem a chave.
- **Vetores fixos são contrato.** Se uma mudança altera a saída de um transformador, ela cria uma nova versão dele (por exemplo, `br.cpf@2`) e mantém a anterior. Um vetor existente em `testes/vetores/` nunca é editado para fazer um teste passar.
- **Tudo em português**: código, testes, mensagens, exceções, documentação XML e commits, com identificadores sem acento. Inglês apenas em palavras reservadas, tipos da biblioteca base, sufixo `Async`, padrão `Add*` de injeção de dependência e nomes de produtos.
- **O núcleo não depende de pacotes de terceiros.** O motor não depende de driver de banco nem do EF Core, e os dialetos não se referenciam. Os testes de arquitetura garantem essas regras.
- **A API pública é rastreada.** Membro público novo entra no `PublicAPI.Unshipped.txt` do projeto; sem isso, o build falha.
- **Decisões de projeto viram ADR** em `docs/adr/`, no mesmo pull request que toma a decisão.
- **Sem promessa jurídica.** Nenhum texto do projeto promete conformidade com a LGPD nem anonimização garantida; o enquadramento jurídico usa os textos que já estão no README.

## Desenvolvimento

Requisitos: o SDK do .NET 10 na versão exata fixada no `global.json` e, para os testes de banco, Docker.

```
dotnet build -c Release
dotnet test
dotnet test --filter-not-trait "Categoria=Integracao"    # sem Docker
dotnet format --verify-no-changes
```

Cada projeto tem um `packages.lock.json` versionado, e o CI restaura com `dotnet restore --locked-mode`: um lock desatualizado quebra o build. Ao adicionar, remover ou atualizar um pacote (sempre no `Directory.Packages.props`), rode `dotnet restore --force-evaluate` e inclua os `packages.lock.json` alterados no commit. Projetos não usam `VersionOverride`, `PackageDownload`, referência a DLL por caminho nem tarefas próprias do MSBuild; o CI recusa.

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
