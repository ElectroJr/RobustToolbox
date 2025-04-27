```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19042.1081/20H2/October2020Update)
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.202
  [Host]     : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2


```
| Method         | N    | Mean        | Error     | StdDev    | Median      | Ratio | RatioSD |
|--------------- |----- |------------:|----------:|----------:|------------:|------:|--------:|
| **PropertyAccess** | **10**   |    **14.92 ns** |  **0.101 ns** |  **0.090 ns** |    **14.91 ns** |  **1.00** |    **0.01** |
| ImplicitCast   | 10   |    15.18 ns |  0.269 ns |  0.265 ns |    15.19 ns |  1.02 |    0.02 |
|                |      |             |           |           |             |       |         |
| **PropertyAccess** | **100**  |   **155.39 ns** |  **3.133 ns** |  **7.445 ns** |   **151.92 ns** |  **1.00** |    **0.07** |
| ImplicitCast   | 100  |   158.35 ns |  3.205 ns |  6.401 ns |   158.99 ns |  1.02 |    0.06 |
|                |      |             |           |           |             |       |         |
| **PropertyAccess** | **1000** | **1,484.91 ns** | **29.347 ns** | **32.619 ns** | **1,474.56 ns** |  **1.00** |    **0.03** |
| ImplicitCast   | 1000 | 1,494.51 ns | 27.749 ns | 37.044 ns | 1,486.45 ns |  1.01 |    0.03 |
