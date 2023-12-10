using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

public class CollectionContainsBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public int HashSetContains()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            if (KeySet.Contains(key))
                total += 1;
            else
                total -= 1;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryContains()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            if (StructDictionary.ContainsKey(key))
                total += 1;
            else
                total -= 1;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryTryGetUnused()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            if (StructDictionary.TryGetValue(key, out _))
                total += 1;
            else
                total -= 1;
        }
        return total;
    }


    [Benchmark]
    public int DictionaryTryGet()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            if (StructDictionary.TryGetValue(key, out var value))
                total += value.A;
            else
                total -= 1;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryMarshalUnused()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, key);
            if (!Unsafe.IsNullRef(ref entry))
                total += 1;
            else
                total -= 1;
        }
        return total;
    }

    [Benchmark]
    public int DictionaryMarshal()
    {
        var total = 0;
        foreach (ref var key in ExtraDictionaryKeys.AsSpan())
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, key);
            if (!Unsafe.IsNullRef(ref entry))
                total += entry.A;
            else
                total -= 1;
        }
        return total;
    }
}
