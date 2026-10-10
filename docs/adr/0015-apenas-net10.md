# ADR 0015 — Apenas `net10.0`

- Status: aceito
- Data: 2026-10-10
- Relacionados: [ADR 0001](0001-projeto-em-portugues.md)

## Contexto

Todos os pacotes do Incognito saem juntos, na mesma versão, e a primeira publicação no NuGet é a 1.0.0, prevista
para 2027. Cada framework a mais multiplica a matriz de build e de testes e limita a API que o código pode usar.

As datas de fim de suporte publicadas pela Microsoft são:

| Versão | Tipo | Fim do suporte |
| --- | --- | --- |
| .NET 8 | LTS | 2026-11-10 |
| .NET 9 | STS | 2026-11-10 |
| .NET 10 | LTS | 2028-11-14 |

O .NET 8 e o .NET 9 deixam de ter suporte antes do lançamento. Um pacote que os incluísse estrearia mirando
runtimes sem correções de segurança.

O desenho do núcleo usa API recente da biblioteca base: `HKDF` (desde o .NET 5), `FrozenDictionary` (desde o
.NET 8) e as sobrecargas com `Span<T>` das APIs de criptografia e de codificação. Parte disso existiria em
frameworks antigos por pacotes de compatibilidade da Microsoft (o `FrozenDictionary` está no
`System.Collections.Immutable`), mas o `HKDF` não tem pacote oficial, e o núcleo não tem nenhuma dependência de
pacote.

O SDK entra na decisão por outro motivo: o núcleo é compatível com AOT, e por isso o `packages.lock.json` dele
registra o `Microsoft.NET.ILLink.Tasks` na versão do runtime do SDK usado. Com outro SDK, o lock muda, e a
restauração travada do CI falha.

## Decisão

1. Todos os projetos (pacotes, testes e ferramentas internas) têm um único alvo: `net10.0`, definido uma vez no
   `Directory.Build.props`.
2. O SDK do .NET 10 fica fixado numa versão exata no `global.json` (`rollForward: disable`). Isso vale para quem
   compila o repositório, não para quem usa os pacotes.
3. A ferramenta de linha de comando é distribuída como `dotnet tool`, dependente do runtime do .NET 10 e sem AOT na
   1.0: o `Microsoft.Data.SqlClient` e o YamlDotNet usam reflexão, e o ganho seria pequeno para uma ferramenta de CI.
   Ela mantém o roll-forward padrão do .NET: roda nas atualizações do .NET 10, não num runtime de versão principal
   mais nova sem que quem a instala peça.
4. Um framework novo como alvo, seja uma versão futura do .NET ou um framework antigo, só entra com um ADR novo
   que substitua este.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| `net8.0` e `net10.0` | O .NET 8 sai do suporte antes da 1.0. Dobraria a matriz de build e testes por um runtime que já estaria fora de suporte no lançamento |
| `netstandard2.0` no núcleo | Alcançaria aplicações em .NET Framework, um público real: sistemas legados também têm bases com dados pessoais. Descartado pelo custo: o `HKDF` teria de ser reimplementado, e o resto viria de dependências de pacote que o núcleo não tem. O caso legado continua atendido pela ferramenta de linha de comando, que roda fora do processo da aplicação; perde-se só o uso da biblioteca dentro de uma aplicação .NET Framework |
| Acompanhar também as versões STS (por exemplo, `net11.0`) | Uma biblioteca compilada para `net10.0` já pode ser referenciada por projetos em `net10.0` ou superior; um alvo a mais só se justifica se houver API nova necessária |

## Consequências

- Uma única matriz de build e testes, e o código pode usar toda a API do .NET 10 sem compilação condicional.
- Quem está no .NET 8 ou no .NET 9 não consegue referenciar os pacotes.
- A ferramenta pede o .NET 10 na máquina que a usa: o SDK para instalar com `dotnet tool install` (um SDK 9 recusa
  o pacote) e o runtime para executar. Num CI isso significa instalar o .NET 10 ao lado dos que já existem, sem
  mudar o projeto de quem usa. Numa máquina só com um runtime mais novo, quem instala pode pedir
  `dotnet tool install --allow-roll-forward`, por sua conta.
- Atualizar o SDK é uma mudança própria, com os lock files regenerados.

## Evidência e verificação

- Datas de fim de suporte: política de suporte do .NET
  (https://dotnet.microsoft.com/platform/support/policy/dotnet-core), conferidas em 2026-10-10.
- O alvo único está no `Directory.Build.props`, e o SDK exato no `global.json`.
- O CI restaura com `dotnet restore --locked-mode`: um SDK diferente do fixado muda o lock do núcleo e quebra o build.
- Em 2026-10-10, `dotnet tool install` com o SDK 9 recusou o pacote da ferramenta gerado para `net10.0`.

## Gatilho de revisão

Revisar quando sair o .NET 12 (LTS, previsto para novembro de 2027) ou até maio de 2028, seis meses antes do fim do
suporte do .NET 10, o que vier primeiro.
