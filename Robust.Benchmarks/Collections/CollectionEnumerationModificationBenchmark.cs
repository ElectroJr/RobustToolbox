using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

/// <summary>
/// Benchmark that checks whether it is better to use struct or class values when enumerating and modifying data in a
/// dictionary or list.
/// </summary>
[MemoryDiagnoser]
public class CollectionEnumerationModificationBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public void BenchmarkClassDictionary()
    {
        foreach (var data in ClassDictionary.Values)
        {
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void BenchmarkStructDictionary()
    {
        foreach (var key in StructDictionary.Keys)
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, key);
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void BenchmarkClassList()
    {
        foreach (ref var data in CollectionsMarshal.AsSpan(ClassList))
        {
            data.C = data.A + data.B;
        }
    }

    [Benchmark]
    public void BenchmarkStructList()
    {
        foreach (ref var data in CollectionsMarshal.AsSpan(StructList))
        {
            data.C = data.A + data.B;
        }
    }
}
