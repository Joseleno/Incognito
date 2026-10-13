```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9550)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                     | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |-----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| HmacReaproveitado          | 157.563 ns | 2.5709 ns | 2.4048 ns |  1.00 |    0.02 |      - |         - |          NA |
| HmacDeUmaVez               | 282.381 ns | 1.2711 ns | 1.0614 ns |  1.79 |    0.03 |      - |         - |          NA |
| AesEncryptEcbDeUmBloco     | 318.679 ns | 5.2588 ns | 5.6269 ns |  2.02 |    0.05 | 0.0043 |      88 B |          NA |
| AesReaproveitadoUmBloco    |  37.205 ns | 0.5437 ns | 0.4820 ns |  0.24 |    0.00 |      - |         - |          NA |
| AesReaproveitado16Blocos   |   3.588 ns | 0.0441 ns | 0.0368 ns |  0.02 |    0.00 |      - |         - |          NA |
| AesReaproveitado1024Blocos |   1.845 ns | 0.0275 ns | 0.0244 ns |  0.01 |    0.00 |      - |         - |          NA |
