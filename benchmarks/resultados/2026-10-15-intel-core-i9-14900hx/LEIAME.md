# Benchmark de 15/10/2026: Intel Core i9-14900HX

Conferência de regressão na preparação da versão candidata do núcleo (`v1.0.0-rc.2`), contra a medição de
[13/10/2026](../2026-10-13-intel-core-i9-14900hx/LEIAME.md), na mesma máquina de referência do
[ADR 0003](../../../docs/adr/0003-permutacao-feistel-com-hmac.md). Desde 13/10, o código medido não mudou: só entraram
no núcleo o catálogo normativo e comentários.

- Mesma máquina e mesmas condições da medição de 13/10 (notebook na tomada, plano "Equilibrado" no sistema e "Alto
  desempenho" aplicado pelo BenchmarkDotNet durante a medição, mesmas afinidades). Mudaram o patch do Windows
  (10.0.26300.9711) e o do runtime (10.0.13); o SDK continua o 10.0.401.
- Comandos iguais aos de 13/10, com `--artifacts` apontando para fora do repositório. Os arquivos sem sufixo vêm
  dessa rodada, com um processo por caso.
- Critério: mediana por caso (média quando o relatório não traz a mediana) contra a de 13/10; um caso acima de 10% é
  medido de novo com 3 processos (`--launchCount 3`) antes de ser chamado de regressão, porque entre processos os
  tempos variam até cerca de 15%.
- Na rodada de um processo, 38 dos 41 casos não passaram de +10%; 30 ficaram abaixo de 13/10, nove deles mais de
  10% abaixo. Passaram de 10% o
  e-mail (+14,8%), o `id.permutar` (+15,2%; em 13/10 ele saiu abaixo da permutação crua que chama) e a vazão do CPF
  com 8 threads (+18,9%).
- Os arquivos `*-remedicao-*` são a medição desses casos com 3 processos, com o CPF como controle: e-mail 382,0 ns
  (+5,0%), `id.permutar` 1.795,2 ns (+2,3%), CPF 2.052,4 ns (−0,2%) e vazão do CPF com 8 threads 388,5 ns (+0,9%).
  Nenhuma regressão acima de 10%.

Comandos da remedição (também com `--artifacts` apontando para fora do repositório):

```
dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --exporters github csv --affinity 1 --launchCount 3 --filter "*TransformadorBenchmarks.Email" "*TransformadorBenchmarks.Identificador" "*TransformadorBenchmarks.Cpf"
dotnet run -c Release --project benchmarks/CodeProcess.Incognito.Benchmarks -- --exporters github csv --affinity 65535 --launchCount 3 --filter "*Vazao*"
```
