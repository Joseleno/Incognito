```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9550)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                | Mean      | Error    | StdDev   | Median    | Gen0   | Allocated |
|---------------------- |----------:|---------:|---------:|----------:|-------:|----------:|
| ValidarCpf            |  19.78 ns | 0.376 ns | 0.351 ns |  19.79 ns |      - |         - |
| ValidarCnpj           |  24.22 ns | 0.474 ns | 0.486 ns |  24.26 ns |      - |         - |
| ValidarChaveDfe       |  36.86 ns | 0.551 ns | 0.488 ns |  36.89 ns |      - |         - |
| GerarCpf              |  47.14 ns | 1.006 ns | 2.837 ns |  45.73 ns | 0.0024 |      48 B |
| GerarCnpjAlfanumerico |  48.00 ns | 0.354 ns | 0.295 ns |  48.00 ns | 0.0029 |      56 B |
| GerarChaveDfe         | 136.11 ns | 1.744 ns | 1.546 ns | 135.41 ns | 0.0057 |     112 B |
