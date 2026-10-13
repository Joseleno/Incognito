# Benchmark de 13/10/2026: Intel Core i9-14900HX

Máquina de desenvolvimento declarada como referência do gatilho do [ADR 0003](../../../docs/adr/0003-permutacao-feistel-com-hmac.md).

- Intel Core i9-14900HX (8 núcleos P com 2 threads cada e 16 núcleos E; 32 processadores lógicos), notebook na tomada.
- Windows 11 (10.0.26300), plano de energia "Equilibrado" no sistema; o BenchmarkDotNet aplica "Alto desempenho" durante a medição e volta ao "Equilibrado" no fim (as duas trocas ficam no log de cada execução). A criptografia é a do sistema (CNG).
- .NET SDK 10.0.401, runtime 10.0.12, RyuJIT x86-64-v3, BenchmarkDotNet 0.15.8 com o job padrão e afinidade (o relatório o nomeia como um job próprio).
- Afinidade num processador lógico de núcleo P (máscara `1`: a primeira thread de um núcleo P com HT; a irmã fica livre para outras cargas); a vazão com várias threads nos 16 processadores lógicos dos núcleos P (máscara `0xFFFF`), sem garantia de uma thread por núcleo: o número inclui o hyper-threading e a queda de frequência com vários núcleos ativos.
- Cada caso rodou num só processo; entre processos os tempos variam até cerca de 15% (o `id.permutar`, com 1,76 µs, saiu abaixo da permutação crua que ele chama, com 1,97 µs).
- Entradas geradas antes da medição com semente fixa (`GeradorBr` nos documentos, nos transformadores e na vazão; `SequenciaXoshiro` na permutação e no protótipo; bytes aleatórios na rodada). O custo é sempre por valor (`OperationsPerInvoke`), com lotes de 1.024, salvo na geração (1), na rodada (1, 16 e 1.024 blocos) e na vazão (8.192). A chave é gerada na hora: `ChaveIncognito.Gerar` nos transformadores e na vazão, bytes aleatórios (zerados depois) na permutação, na rodada e no protótipo.
- Os arquivos são o `*-report-github.md` e o `*-report.csv` de `BenchmarkDotNet.Artifacts/results/`, copiados sem o prefixo do namespace e com fim de linha LF. Depois da medição, o código dos benchmarks ganhou só conferências no `GlobalSetup` (entradas pseudonimizadas, bijeção e dependência do protótipo) e a zeragem das chaves, fora do que é medido.

Comandos:

```
dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --exporters github csv --affinity 1 --filter "*Permutacao*" "*Rodada*" "*PrototipoAes*" "*Transformador*" "*Documento*"
dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --exporters github csv --affinity 65535 --filter "*Vazao*"
```
