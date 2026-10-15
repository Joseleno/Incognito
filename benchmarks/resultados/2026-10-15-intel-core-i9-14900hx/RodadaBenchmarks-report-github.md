```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                     | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |-----------:|----------:|----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| HmacReaproveitado          | 149.045 ns | 2.9895 ns | 6.8689 ns | 145.659 ns |  1.00 |    0.06 |      - |         - |          NA |
| HmacDeUmaVez               | 258.835 ns | 2.4335 ns | 1.8999 ns | 259.149 ns |  1.74 |    0.08 |      - |         - |          NA |
| AesEncryptEcbDeUmBloco     | 284.205 ns | 5.2268 ns | 4.6335 ns | 282.784 ns |  1.91 |    0.09 | 0.0043 |      88 B |          NA |
| AesReaproveitadoUmBloco    |  35.810 ns | 0.6149 ns | 0.5135 ns |  35.997 ns |  0.24 |    0.01 |      - |         - |          NA |
| AesReaproveitado16Blocos   |   3.326 ns | 0.0674 ns | 0.1069 ns |   3.327 ns |  0.02 |    0.00 |      - |         - |          NA |
| AesReaproveitado1024Blocos |   1.668 ns | 0.0224 ns | 0.0187 ns |   1.662 ns |  0.01 |    0.00 |      - |         - |          NA |
