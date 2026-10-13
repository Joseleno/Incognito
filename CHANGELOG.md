# Changelog

Todas as mudanças relevantes para quem usa os pacotes ficam registradas aqui.
O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota o [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Adicionado

- `ChaveIncognito`: chave secreta no formato `inc1_` seguido de 43 caracteres base64url, com geração, leitura que recusa qualquer outro formato, impressão digital e zeragem ao descartar.
- `IncognitoException`: erro com código estável, sem valores de coluna nem a chave na mensagem.
- `ValidadorBr`: validação de CPF, CNPJ numérico e alfanumérico (com ou sem máscara) e chave de acesso de DF-e de 44 posições, pela estrutura e pelos dígitos verificadores; recusa CPF e CNPJ com todos os caracteres iguais; sobrecargas com `ReadOnlySpan<char>` que não alocam.
- `GeradorBr`: geração de CPF e CNPJ (numérico ou alfanumérico, com ordem `0001`) válidos para dados de teste, com ou sem máscara; `new GeradorBr(semente)` repete a mesma sequência dentro da mesma versão maior, e `GeradorBr.Padrao` usa semente aleatória. Não é criptográfico, e um documento gerado pode pertencer a alguém.
  - `GeradorBr.ChaveDfe(modelo, alfanumerico)`: chave de acesso de DF-e válida dos modelos 55, 57, 58, 62, 63, 64, 65, 66 e 67, de emissão normal, com emitente CNPJ de matriz (numérico ou alfanumérico). O AAMM sai numa janela fixa, de 01/2020 a 09/2026 (com CNPJ alfanumérico, de 07/2026), então as chaves têm datas passadas; o cUF não tem relação com o emitente. O emitente pode ser um CNPJ real: não consulte as chaves geradas nos portais das SEFAZ.
