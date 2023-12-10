using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.Collections;

/// <summary>
/// Compare enumeration of hashsets and dictionary keys
/// </summary>
public class KeyEnumerationBenchmark : CollectionBenchmarks
{
    [Benchmark(Baseline = true)]
    public int DictionaryKeyEnumeration()
    {
        var total = 0;
        foreach (var k in StructDictionary.Keys)
        {
            total += k.Id;
        }
        return total;
    }

    [Benchmark]
    public int KeySetEnumeration()
    {
        var total = 0;
        foreach (var k in KeySet)
        {
            total += k.Id;
        }
        return total;
    }

    [Benchmark]
    public int KeyListEnumeration()
    {
        var total = 0;
        foreach (ref var key in CollectionsMarshal.AsSpan(KeyList))
        {
            total += key.Id;
        }
        return total;
    }
}
