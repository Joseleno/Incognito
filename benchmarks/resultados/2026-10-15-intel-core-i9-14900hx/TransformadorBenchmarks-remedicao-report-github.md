```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-DULTKL : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  LaunchCount=3  

```
| Method        | Mean       | Error    | StdDev    | Median     | Gen0   | Allocated |
|-------------- |-----------:|---------:|----------:|-----------:|-------:|----------:|
| Cpf           | 2,088.2 ns | 29.85 ns | 150.74 ns | 2,052.4 ns |      - |      48 B |
| Email         |   382.2 ns |  4.02 ns |   8.82 ns |   382.0 ns | 0.0038 |      88 B |
| Identificador | 1,817.9 ns | 25.55 ns |  49.83 ns | 1,795.2 ns |      - |         - |
