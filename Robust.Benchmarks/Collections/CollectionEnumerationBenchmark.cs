using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

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
