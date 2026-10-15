```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|---------------------- |------------:|----------:|----------:|------------:|-------:|----------:|
| Cpf                   |  1,873.8 ns |  12.89 ns |  10.77 ns |  1,870.8 ns |      - |      48 B |
| CpfNumerico           |  1,872.9 ns |  35.53 ns |  31.50 ns |  1,868.8 ns |      - |         - |
| CnpjNumerico          |  1,938.4 ns |  39.91 ns | 112.58 ns |  1,885.7 ns |      - |      56 B |
| CnpjAlfanumerico      |  1,919.7 ns |  17.94 ns |  15.91 ns |  1,919.7 ns |      - |      56 B |
| CnpjComOrdemPermutada |  1,865.9 ns |  18.58 ns |  15.51 ns |  1,866.0 ns |      - |      56 B |
| Telefone              |  1,847.9 ns |  11.31 ns |  10.02 ns |  1,846.5 ns |      - |      56 B |
| Email                 |    421.1 ns |  11.87 ns |  33.47 ns |    417.4 ns | 0.0038 |      88 B |
| Cep                   |    375.3 ns |   9.10 ns |  25.68 ns |    369.3 ns | 0.0019 |      40 B |
| Identificador         |  2,017.4 ns |  59.24 ns | 168.06 ns |  2,022.7 ns |      - |         - |
| ChavePix              |  1,435.7 ns |  32.82 ns |  95.22 ns |  1,411.2 ns |      - |      69 B |
| ChaveDfeNfe           |  6,031.8 ns | 116.22 ns | 325.89 ns |  5,983.5 ns |      - |     112 B |
| ChaveDfeCte           |  6,040.5 ns | 118.84 ns | 223.21 ns |  6,061.9 ns |      - |     112 B |
| ChaveDfeNf3e          |  5,849.4 ns | 115.40 ns | 240.89 ns |  5,781.5 ns |      - |     112 B |
| ChaveDfeNff           | 11,270.4 ns | 222.12 ns | 377.17 ns | 11,300.9 ns |      - |     112 B |
| FachadaCpf            |  1,986.7 ns |  39.46 ns | 101.16 ns |  1,967.2 ns |      - |      48 B |
