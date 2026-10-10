# CodeProcess.Incognito

[![ci](https://github.com/Joseleno/Incognito/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/Joseleno/Incognito/actions/workflows/ci.yml?query=branch%3Adevelop)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/Joseleno/Incognito/badge)](https://scorecard.dev/viewer/?uri=github.com/Joseleno/Incognito)

Biblioteca NuGet e ferramenta de linha de comando em .NET 10 que copia bases PostgreSQL e SQL Server de produção para homologação, desenvolvimento e CI substituindo dados pessoais por dados brasileiros válidos: CPF, CNPJ alfanumérico, telefone, chaves de acesso de NF-e e demais DF-e.

Gratuita, open source (MIT) e inteiramente em português.

> **Em desenvolvimento.** Nenhum pacote foi publicado ainda. A primeira publicação no NuGet será a `1.0.0`, com todos os pacotes de uma vez.

## LGPD

Executar o Incognito é tratamento de dados pessoais: a leitura da base de origem (inclusive a amostragem do comando `descobrir`) precisa de base legal e finalidade compatível. A ferramenta reduz a exposição em ambientes não produtivos e apoia os princípios de necessidade, segurança, prevenção e prestação de contas (art. 6º, III, VII, VIII e X), as medidas dos arts. 46 (inclusive §2º) e 49, e os controles ISO/IEC 27001:2022 A.8.11, A.8.31 e A.8.33. Não garante anonimização nem conformidade. Este projeto não constitui aconselhamento jurídico.

## Compilar

Requer o SDK do .NET 10 (versão fixada em `global.json`).

```
dotnet build -c Release
```
