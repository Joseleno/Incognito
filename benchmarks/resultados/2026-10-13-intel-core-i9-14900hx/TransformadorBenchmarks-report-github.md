```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9550)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-RLBEIC : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Affinity=00000000000000000000000000000001  

```
| Method                | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|---------------------- |------------:|----------:|----------:|------------:|-------:|----------:|
| Cpf                   |  2,057.7 ns |  39.03 ns |  38.33 ns |  2,055.9 ns |      - |      48 B |
| CpfNumerico           |  2,112.4 ns |  39.64 ns | 101.61 ns |  2,091.5 ns |      - |         - |
| CnpjNumerico          |  2,020.5 ns |  39.13 ns |  49.49 ns |  2,003.2 ns |      - |      56 B |
| CnpjAlfanumerico      |  2,004.0 ns |  39.84 ns |  98.47 ns |  1,964.5 ns |      - |      56 B |
| CnpjComOrdemPermutada |  1,944.2 ns |  26.86 ns |  22.43 ns |  1,944.2 ns |      - |      56 B |
| Telefone              |  1,873.0 ns |  36.79 ns |  40.89 ns |  1,862.4 ns |      - |      56 B |
| Email                 |    363.5 ns |   5.70 ns |   5.06 ns |    363.7 ns | 0.0038 |      88 B |
| Cep                   |    351.0 ns |   6.43 ns |   7.41 ns |    352.2 ns | 0.0019 |      40 B |
| Identificador         |  1,755.3 ns |  20.39 ns |  18.08 ns |  1,755.1 ns |      - |         - |
| ChavePix              |  1,313.3 ns |  19.72 ns |  17.48 ns |  1,315.3 ns | 0.0038 |      69 B |
| ChaveDfeNfe           |  5,532.3 ns |  39.77 ns |  31.05 ns |  5,529.3 ns |      - |     112 B |
| ChaveDfeCte           |  5,944.0 ns | 114.74 ns | 236.95 ns |  5,856.3 ns |      - |     112 B |
| ChaveDfeNf3e          |  5,666.9 ns | 107.63 ns | 105.71 ns |  5,681.6 ns |      - |     112 B |
| ChaveDfeNff           | 10,932.1 ns | 215.57 ns | 180.01 ns | 10,967.5 ns |      - |     112 B |
| FachadaCpf            |  1,908.2 ns |  37.95 ns |  55.63 ns |  1,900.8 ns |      - |      48 B |
