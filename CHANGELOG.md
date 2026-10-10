# Changelog

Todas as mudanças relevantes para quem usa os pacotes ficam registradas aqui.
O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota o [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Adicionado

- `ChaveIncognito`: chave secreta no formato `inc1_` seguido de 43 caracteres base64url, com geração, leitura que recusa qualquer outro formato, impressão digital e zeragem ao descartar.
- `IncognitoException`: erro com código estável, sem valores de coluna nem a chave na mensagem.
