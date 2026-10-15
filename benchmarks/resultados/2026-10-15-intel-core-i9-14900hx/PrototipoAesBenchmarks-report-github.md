```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                 | Mean       | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |-----------:|---------:|---------:|------:|--------:|----------:|------------:|
| FeistelComHmac         | 1,759.4 ns | 26.38 ns | 27.09 ns |  1.00 |    0.02 |         - |          NA |
| FeistelComAesPorRodada |   675.0 ns |  4.17 ns |  3.48 ns |  0.38 |    0.01 |         - |          NA |
