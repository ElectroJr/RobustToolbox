using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

/// <summary>
/// Checks whether <see cref="CollectionsMarshal.GetValueRefOrNullRef{T1,T2}"/>
/// or <see cref="CollectionsMarshal.GetValueRefOrAddDefault{T1,T2}"/> is faster.
///
/// Apparently they are all basically identical.
/// </summary>
public class CollectionsMarshalBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public int DictionaryGet()
    {
        var total = 0;
        foreach (ref var key in DictionaryKeys.AsSpan())
        {
            var data = StructDictionary[key];
            total += data.A;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryNullRef()
    {
        var total = 0;
        foreach (ref var key in DictionaryKeys.AsSpan())
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, key);
            total += data.A;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryDefaultRef()
    {
        var total = 0;
        foreach (ref var key in DictionaryKeys.AsSpan())
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(StructDictionary, key, out _);
            total += data.A;
        }
        return total;
    }
}
