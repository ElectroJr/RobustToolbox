using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Robust.Shared.Analyzers;

namespace Robust.Benchmarks;

[Virtual]
[MemoryDiagnoser]
public class InterfaceBenchmark
{
    public HashSet<string> Tags = ["Wall", "Door", "Hat", "Foo", "Bar"];
    public string[] TagArray = ["Hat", "Bar"];
    public List<string> TagList = ["Hat", "Bar"];

    [Benchmark(Baseline = true)]
    public bool HasTagsArray()
    {
        return HasTagsArray(TagArray);
    }

    [Benchmark]
    public bool HasTagsList()
    {
        return HasTagsList(TagList);
    }

    [Benchmark]
    public bool HasTagsIEnumerableArray()
    {
        return HasTagsIEnumerable(TagArray);
    }

    [Benchmark]
    public bool HasTagsIEnumerableList()
    {
        return HasTagsIEnumerable(TagList);
    }

    private bool HasTagsArray(string[] tags)
    {
        foreach (var tag in tags)
        {
            if (!Tags.Contains(tag))
                return false;
        }

        return true;
    }

    private bool HasTagsList(List<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!Tags.Contains(tag))
                return false;
        }

        return true;
    }

    private bool HasTagsIEnumerable(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!Tags.Contains(tag))
                return false;
        }

        return true;
    }
}
