```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19042.1081/20H2/October2020Update)
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.202
  [Host]     : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2


```
| Method              | N    | Mean        | Error     | StdDev    | Ratio | RatioSD |
|-------------------- |----- |------------:|----------:|----------:|------:|--------:|
| **ExplicitConstructor** | **10**   |    **21.47 ns** |  **0.295 ns** |  **0.276 ns** |  **1.00** |    **0.02** |
| TupleCast           | 10   |    21.96 ns |  0.462 ns |  0.719 ns |  1.02 |    0.04 |
|                     |      |             |           |           |       |         |
| **ExplicitConstructor** | **100**  |   **214.08 ns** |  **3.587 ns** |  **3.356 ns** |  **1.00** |    **0.02** |
| TupleCast           | 100  |   212.86 ns |  4.256 ns |  7.226 ns |  0.99 |    0.04 |
|                     |      |             |           |           |       |         |
| **ExplicitConstructor** | **1000** | **2,050.25 ns** | **40.257 ns** | **49.439 ns** |  **1.00** |    **0.03** |
| TupleCast           | 1000 | 2,142.07 ns | 42.452 ns | 80.769 ns |  1.05 |    0.05 |
