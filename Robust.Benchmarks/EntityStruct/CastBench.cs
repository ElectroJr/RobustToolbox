using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;

namespace Robust.Benchmarks.EntityStruct;

// Is it faster to explicitly access the Owner or Comp fields than to use the implicit casts?
[Virtual]
public class CastBench
{
    public Entity<NumberComponent>[] Entity = default!;

    [Params(10, 100, 1000)]
    public int N;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Entity = new Entity<NumberComponent>[N];
        for (var i = 0; i < N; i++)
        {
            Entity[i] = new(EntityUid.Invalid, new());
        }
    }

    [Benchmark(Baseline = true)]
    public int PropertyAccess()
    {
        var total = 0;
        foreach (var ent in Entity)
        {
            total += Sum(ent.Owner, ent.Comp);
        }
        return total;
    }

    [Benchmark]
    public int ImplicitCast()
    {
        var total = 0;
        foreach (var ent in Entity)
        {
            total += Sum(ent, ent);
        }
        return total;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Sum(EntityUid uid, NumberComponent comp)
    {
        return uid.Id + comp.Number;
    }
}
