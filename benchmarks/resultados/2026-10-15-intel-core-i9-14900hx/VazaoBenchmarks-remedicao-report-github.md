```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-CQTREO : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000001111111111111111  LaunchCount=3  

```
| Method | Threads | Mean       | Error    | StdDev   | Gen0   | Allocated |
|------- |-------- |-----------:|---------:|---------:|-------:|----------:|
| **Cpf**    | **1**       | **1,913.1 ns** | **24.27 ns** | **72.32 ns** | **0.0019** |      **48 B** |
| **Cpf**    | **4**       |   **570.0 ns** |  **7.05 ns** | **14.09 ns** | **0.0019** |      **48 B** |
| **Cpf**    | **8**       |   **388.5 ns** |  **5.63 ns** | **10.57 ns** | **0.0024** |      **48 B** |
