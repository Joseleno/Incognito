```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9550)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                 | Mean       | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |-----------:|---------:|---------:|------:|--------:|----------:|------------:|
| FeistelComHmac         | 1,903.8 ns | 27.79 ns | 24.64 ns |  1.00 |    0.02 |         - |          NA |
| FeistelComAesPorRodada |   772.2 ns | 13.34 ns | 12.47 ns |  0.41 |    0.01 |         - |          NA |
