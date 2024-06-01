using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Robust.Shared.Analyzers;

namespace Robust.Benchmarks;

[MemoryDiagnoser]
[Virtual]
public class ParamsBenchmark
{
    public HashSet<string> Tags = ["Wall", "Door", "Hat", "Foo", "Bar"];
    public string Tag = "Hat";

    [Benchmark(Baseline = true)]
    public bool HasTag()
    {
        return HasTag(Tag);
    }

    [Benchmark]
    public bool HasTagsParams()
    {
        return HasTagsParams(Tag);
    }

    private bool HasTag(string tag)
    {
        return Tags.Contains(tag);
    }

    private bool HasTagsParams(params string[] tags)
    {
        foreach (var tag in tags)
        {
            if (!Tags.Contains(tag))
                return false;
        }

        return true;
    }
}
