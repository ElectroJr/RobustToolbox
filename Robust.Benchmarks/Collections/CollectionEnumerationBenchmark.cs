using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

/*
BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19042
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET Core SDK=7.0.306
  [Host]     : .NET Core 7.0.9 (CoreCLR 7.0.923.32018, CoreFX 7.0.923.32018), X64 RyuJIT
  DefaultJob : .NET Core 7.0.9 (CoreCLR 7.0.923.32018, CoreFX 7.0.923.32018), X64 RyuJIT

|                  Method |        Mean |    Error |   StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------ |------------:|---------:|---------:|------:|--------:|------:|------:|------:|----------:|
|        ClassArrayAsSpan |    774.1 ns |  0.16 ns |  0.13 ns |  1.00 |    0.00 |     - |     - |     - |         - |
|     ClassArrayAsSpanRef |    793.5 ns |  0.27 ns |  0.24 ns |  1.02 |    0.00 |     - |     - |     - |         - |
|           ClassArrayFor |  1,023.5 ns |  0.16 ns |  0.13 ns |  1.32 |    0.00 |     - |     - |     - |         - |
|       ClassArrayForeach |    769.0 ns |  0.18 ns |  0.16 ns |  0.99 |    0.00 |     - |     - |     - |         - |
|       StructArrayAsSpan |    847.3 ns |  0.30 ns |  0.27 ns |  1.09 |    0.00 |     - |     - |     - |         - |
|    StructArrayAsSpanRef |    844.8 ns |  0.32 ns |  0.28 ns |  1.09 |    0.00 |     - |     - |     - |         - |
|          StructArrayFor |    857.0 ns |  0.27 ns |  0.23 ns |  1.11 |    0.00 |     - |     - |     - |         - |
|      StructArrayForeach |    848.4 ns |  0.50 ns |  0.44 ns |  1.10 |    0.00 |     - |     - |     - |         - |
|         ClassListAsSpan |    774.2 ns |  0.10 ns |  0.09 ns |  1.00 |    0.00 |     - |     - |     - |         - |
|      ClassListAsSpanRef |    796.0 ns |  0.20 ns |  0.17 ns |  1.03 |    0.00 |     - |     - |     - |         - |
|            ClassListFor |  1,128.0 ns |  0.17 ns |  0.15 ns |  1.46 |    0.00 |     - |     - |     - |         - |
|        ClassListForeach |  1,127.2 ns |  0.26 ns |  0.24 ns |  1.46 |    0.00 |     - |     - |     - |         - |
|        StructListAsSpan |    857.6 ns |  1.59 ns |  1.41 ns |  1.11 |    0.00 |     - |     - |     - |         - |
|     StructListAsSpanRef |    848.4 ns |  0.12 ns |  0.11 ns |  1.10 |    0.00 |     - |     - |     - |         - |
|           StructListFor |  1,026.3 ns |  2.07 ns |  1.73 ns |  1.33 |    0.00 |     - |     - |     - |         - |
|       StructListForeach |  1,923.1 ns |  0.51 ns |  0.46 ns |  2.48 |    0.00 |     - |     - |     - |         - |
|  ClassDictionaryForeach |  3,984.2 ns |  1.03 ns |  0.86 ns |  5.15 |    0.00 |     - |     - |     - |         - |
|   ClassDictionaryValues |  3,633.6 ns |  1.35 ns |  1.19 ns |  4.69 |    0.00 |     - |     - |     - |         - |
| StructDictionaryForeach | 10,584.9 ns | 18.49 ns | 17.29 ns | 13.67 |    0.02 |     - |     - |     - |         - |
|  StructDictionaryValues |  2,841.1 ns |  2.37 ns |  1.98 ns |  3.67 |    0.00 |     - |     - |     - |         - |
*/

[MemoryDiagnoser]
public class CollectionEnumerationBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public int ClassArrayAsSpan()
    {
        var total = 0;
        foreach (var data in ClassArray.AsSpan())
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassArrayAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in ClassArray.AsSpan())
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassArrayFor()
    {
        var total = 0;
        for (var i = 0; i < ClassArray.Length; i++)
        {
            var data = ClassArray[i];
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassArrayForeach()
    {
        var total = 0;
        foreach (var data in ClassArray)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructArrayAsSpan()
    {
        var total = 0;
        foreach (var data in StructArray.AsSpan())
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructArrayAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in StructArray.AsSpan())
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructArrayFor()
    {
        var total = 0;
        for (var i = 0; i < StructArray.Length; i++)
        {
            var data = StructArray[i];
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructArrayForeach()
    {
        var total = 0;
        foreach (var data in StructArray)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassListAsSpan()
    {
        var total = 0;
        foreach (var data in CollectionsMarshal.AsSpan(ClassList))
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassListAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in CollectionsMarshal.AsSpan(ClassList))
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassListFor()
    {
        var total = 0;
        for (var i = 0; i < ClassList.Count; i++)
        {
            var data = ClassList[i];
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassListForeach()
    {
        var total = 0;
        foreach (var data in ClassList)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructListAsSpan()
    {
        var total = 0;
        foreach (var data in CollectionsMarshal.AsSpan(StructList))
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructListAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in CollectionsMarshal.AsSpan(StructList))
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructListFor()
    {
        var total = 0;
        for (var i = 0; i < StructList.Count; i++)
        {
            var data = StructList[i];
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructListForeach()
    {
        var total = 0;
        foreach (var data in StructList)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassDictionaryForeach()
    {
        var total = 0;
        foreach (var (_, data) in ClassDictionary)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int ClassDictionaryValues()
    {
        var total = 0;
        foreach (var data in ClassDictionary.Values)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructDictionaryForeach()
    {
        var total = 0;
        foreach (var (_, data) in StructDictionary)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }

    [Benchmark]
    public int StructDictionaryValues()
    {
        var total = 0;
        foreach (var data in StructDictionary.Values)
        {
            total += data.A + data.B + data.C;
        }
        return total;
    }
}
