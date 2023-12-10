using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

/// <summary>
/// Benchmark that checks whether it is better to use struct or class values when fetching and modifying data in a
/// dictionary or list.
/// </summary>
[MemoryDiagnoser]
public class CollectionModificationBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public void ClassDictionaryGet()
    {
        foreach (ref var key in DictionaryKeys.AsSpan())
        {
            var data = ClassDictionary[key];
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void StructDictionaryRef()
    {
        foreach (ref var key in DictionaryKeys.AsSpan())
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, key);
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void ClassListRef()
    {
        var list = CollectionsMarshal.AsSpan(ClassList);
        foreach (ref var key in ListKeys.AsSpan())
        {
            ref var data = ref list[key];
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void StructListRef()
    {
        var list = CollectionsMarshal.AsSpan(StructList);
        foreach (ref var key in ListKeys.AsSpan())
        {
            ref var data = ref list[key];
            data.C = data.A + data.B;
        }
    }
}
