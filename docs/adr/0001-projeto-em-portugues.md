# ADR 0001 — Projeto inteiramente em português

- Status: aceito
- Data: 2026-10-10
- Relacionados: [ADR 0015](0015-apenas-net10.md)

## Contexto

O Incognito copia bases de dados substituindo dados pessoais por dados brasileiros válidos. O público é quem
desenvolve sistemas para o Brasil, e o domínio é brasileiro de ponta a ponta: LGPD, CPF, CNPJ alfanumérico, chave
de acesso de NF-e e demais documentos fiscais eletrônicos, regras da Receita Federal e da ANPD.

O padrão do ecossistema .NET é escrever código e documentação em inglês, e quem desenvolve no Brasil lê inglês no
dia a dia. O custo de seguir o padrão aqui não é de acesso, é de vocabulário. Os termos do domínio vêm de leis,
normas e manuais técnicos que só têm valor oficial em português (a LGPD, as resoluções da ANPD, os manuais dos
documentos fiscais), e traduzi-los perde precisão: `Issuer` não diz o que "emitente" diz numa chave de NF-e. Cada
conceito traduzido obriga quem usa a traduzir de volta para conferir com a norma.

A escolha de idioma alcança tudo o que é público: tipos e membros da API, comandos e mensagens da CLI, chaves do
arquivo de configuração, exceções, logs, relatórios e documentação. Precisa ser tomada antes da primeira linha de
código: depois da 1.0, mudar o idioma da API pública exigiria uma versão principal nova.

## Decisão

### Idioma

1. **Tudo em português:**

   | O quê | Como |
   | --- | --- |
   | README, documentação, ADRs, issues, commits, pull requests, CHANGELOG | Português |
   | Namespaces, tipos, métodos, propriedades, parâmetros, enums | Português, sem acento |
   | Comandos, opções e mensagens da CLI | Português |
   | Chaves do arquivo de configuração | Português, camelCase sem acento |
   | Mensagens de erro, exceções, logs, documentação XML | Português |
   | Nomes dos transformadores | Português, minúsculas com hífen e versão (`br.nome-completo@1`) |
   | Relatórios (JSON, Markdown, SARIF) | Português |

2. **Identificadores sem acento**, mesmo que o C# aceite acentos (`Configuracao`, `TipoDadoPessoal`).
3. **Exceções, por serem convenção da plataforma ou nomes próprios:**
   - palavras reservadas do C# e tipos da biblioteca base (`Task`, `CancellationToken`, `DbDataReader`);
   - o sufixo `Async`;
   - o padrão `Add*` só no ponto de entrada da injeção de dependência (`AddIncognito`), com os métodos encadeados
     em português (`UsarPostgreSql`, `AdicionarTransformador`);
   - nomes de produtos, formatos e siglas (PostgreSQL, EF Core, JSON, SARIF, `Pix`, `Nfe`, `Dfe`). `Cpf` e `Cnpj`
     entram como parte de nomes de membros ou de tipos compostos, como `Pseudonimizador.Cpf`, nunca como nome de
     tipo isolado (item 4).

### Nomes que não colidem com o código de quem usa

4. Projetos brasileiros já têm esses nomes em português, e a API não pode disputá-los:
   - nenhum tipo chamado `Cpf`, `Cnpj` ou `Email`, comuns como value objects em projetos com DDD;
   - classes utilitárias com sufixo `Br` (`GeradorBr`, `ValidadorBr`), e não `Gerador` ou `Validador`;
   - nenhum tipo chamado `Dominio`, que colide com o namespace homônimo de projetos com DDD;
   - o namespace raiz `CodeProcess.Incognito` só com a superfície de uso direto (`GeradorBr`, `ValidadorBr`,
     `Pseudonimizador`, `ChaveIncognito`, `DominioPseudonimo`, `IncognitoException`); o resto em sub-namespaces
     (`.Transformadores`, `.Configuracao`, `.Execucao`, `.Dialetos`).

### Testes

5. Nomes de teste em português com `_` entre as palavras (`Todas_as_colunas_suspeitas_devem_estar_classificadas`),
   com a regra CA1707 desligada só nos projetos de teste.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Tudo em inglês, como a maior parte do ecossistema .NET | Os termos do domínio fiscal e jurídico perdem precisão traduzidos, e a referência normativa continuaria em português: o projeto teria duas línguas, com a tradução a cargo de quem usa |
| Híbrido: API em inglês com os termos do domínio em português (`Cpf`, `InscricaoEstadual`); documentação, CLI e mensagens em português | Reduz a barreira para quem não lê português, mas cria em cada nome novo a pergunta "este termo é do domínio?", decidida caso a caso em cada pull request. "Tudo em português, com lista fechada de exceções" é mais simples de aplicar e de revisar |
| Identificadores com acento | O C# aceita, mas teclados sem acentuação, busca e ferramentas de linha de comando lidam mal com eles, e quem usa teria de digitar `Configuração` para chamar a API |

## Consequências

- A API, a CLI e as mensagens usam o mesmo vocabulário das normas: uma mensagem de erro aponta o conceito com o
  nome que a legislação usa.
- Custos aceitos:
  - a descoberta por busca em inglês ("anonymization", "data masking") fica reduzida, porque o pacote é descrito
    em português;
  - logs e stack traces misturam português (Incognito) e inglês (.NET, EF Core, drivers de banco);
  - quem não lê português, inclusive equipes estrangeiras que atendem o Brasil, tem mais dificuldade para
    contribuir.
- A revisão de cada pull request inclui o idioma: texto novo em inglês fora das exceções é corrigido antes do
  merge.

## Evidência e verificação

- A verificação é a revisão de cada pull request. Todo nome público novo aparece no diff do
  `PublicAPI.Unshipped.txt` do projeto, que o build exige, então nenhum nome entra na API sem ser visto.
- A regra CA1707 está desligada só no `testes/.editorconfig`.

## Gatilho de revisão

Só antes da 1.0 e com ADR novo. Depois dela, mudar o idioma da API pública exige uma versão principal nova.
