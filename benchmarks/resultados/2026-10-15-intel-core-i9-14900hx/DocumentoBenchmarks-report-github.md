```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                | Mean      | Error    | StdDev   | Gen0   | Allocated |
|---------------------- |----------:|---------:|---------:|-------:|----------:|
| ValidarCpf            |  15.23 ns | 0.158 ns | 0.148 ns |      - |         - |
| ValidarCnpj           |  22.90 ns | 0.415 ns | 0.554 ns |      - |         - |
| ValidarChaveDfe       |  33.10 ns | 0.434 ns | 0.362 ns |      - |         - |
| GerarCpf              |  40.47 ns | 0.745 ns | 0.622 ns | 0.0024 |      48 B |
| GerarCnpjAlfanumerico |  44.35 ns | 0.747 ns | 0.698 ns | 0.0029 |      56 B |
| GerarChaveDfe         | 120.98 ns | 1.581 ns | 1.479 ns | 0.0057 |     112 B |
