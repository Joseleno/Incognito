# Changelog

Todas as mudanças relevantes para quem usa os pacotes ficam registradas aqui.
O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota o [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Adicionado

- `ChaveIncognito`: chave secreta no formato `inc1_` seguido de 43 caracteres base64url, com geração, leitura que recusa qualquer outro formato, impressão digital e zeragem ao descartar.
- `IncognitoException`: erro com código estável, sem valores de coluna nem a chave na mensagem.
- `ValidadorBr`: validação de CPF, CNPJ numérico e alfanumérico (com ou sem máscara) e chave de acesso de DF-e de 44 posições, pela estrutura e pelos dígitos verificadores; recusa CPF e CNPJ com todos os caracteres iguais; sobrecargas com `ReadOnlySpan<char>` que não alocam.
