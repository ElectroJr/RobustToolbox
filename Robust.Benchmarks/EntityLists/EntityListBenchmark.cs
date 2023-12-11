using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Robust.Shared.GameObjects;

namespace Robust.Benchmarks.EntityLists;

/*


 Uhhh.. very confusing results here.

|         Method |     Mean |   Error |   StdDev |   Median | Ratio | RatioSD |
|--------------- |---------:|--------:|---------:|---------:|------:|--------:|
|    ArrayAsSpan | 392.5 ns | 5.75 ns |  5.38 ns | 392.4 ns |  1.00 |    0.00 |
| ArrayAsSpanRef | 366.7 ns | 5.88 ns |  5.50 ns | 365.3 ns |  0.93 |    0.02 |
|       ArrayFor | 377.4 ns | 6.63 ns |  7.63 ns | 374.5 ns |  0.96 |    0.02 |
|   ArrayForeach | 347.8 ns | 2.47 ns |  1.93 ns | 347.2 ns |  0.89 |    0.01 |
|     ListAsSpan | 308.0 ns | 4.55 ns | 10.55 ns | 304.4 ns |  0.77 |    0.01 |
|  ListAsSpanRef | 336.8 ns | 6.73 ns | 16.38 ns | 337.8 ns |  0.90 |    0.03 |
|        ListFor | 369.1 ns | 7.41 ns | 20.41 ns | 357.3 ns |  0.98 |    0.04 |
|    ListForeach | 963.4 ns | 3.58 ns |  2.99 ns | 962.3 ns |  2.46 |    0.04 |
*/

public class EntityListBenchmark
{
    public const int N = 1000;

    public List<EntityUid> List = new(N);
    public EntityUid[] Array = new EntityUid[N];

    [GlobalSetup]
    public void Setup()
    {
        for (int i = 0; i < N; i++)
        {
            var k = new EntityUid(i);
            List.Add(k);
            Array[i] = k;
        }
    }

    [Benchmark(Baseline = true)]
    public int ArrayAsSpan()
    {
        var total = 0;
        foreach (var data in Array.AsSpan())
        {
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ArrayAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in Array.AsSpan())
        {
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ArrayFor()
    {
        var total = 0;
        for (var i = 0; i < Array.Length; i++)
        {
            var data = Array[i];
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ArrayForeach()
    {
        var total = 0;
        foreach (var data in Array)
        {
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ListAsSpan()
    {
        var total = 0;
        foreach (var data in CollectionsMarshal.AsSpan(List))
        {
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ListAsSpanRef()
    {
        var total = 0;
        foreach (ref var data in CollectionsMarshal.AsSpan(List))
        {
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ListFor()
    {
        var total = 0;
        for (var i = 0; i < List.Count; i++)
        {
            var data = Array[i];
            total += data.Id;
        }
        return total;
    }

    [Benchmark]
    public int ListForeach()
    {
        var total = 0;
        foreach (var data in List)
        {
            total += data.Id;
        }
        return total;
    }
}
