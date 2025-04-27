using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;

namespace Robust.Benchmarks.EntityStruct;

// Is it faster to create an Entity<T> using an explicit constructor, or via the implicit tuple cast?
[Virtual]
public class ConstructorBench
{
    public EntityUid[] Owner = default!;
    public NumberComponent[] Comp = default!;

    [Params(10, 100, 1000)]
    public int N;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Owner = new EntityUid[N];
        Comp = new NumberComponent[N];
        for (var i = 0; i < N; i++)
        {
            Comp[i] = new();
        }
    }

    [Benchmark(Baseline = true)]
    public int ExplicitConstructor()
    {
        var total = 0;
        for (var index = 0; index < Owner.Length; index++)
        {
            total += Sum(new(Owner[index], Comp[index]));
        }
        return total;
    }

    [Benchmark]
    public int TupleCast()
    {
        var total = 0;
        for (var index = 0; index < Owner.Length; index++)
        {
            total += Sum((Owner[index], Comp[index]));
        }

        return total;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Sum(Entity<NumberComponent> entity)
    {
        return entity.Owner.Id + entity.Comp.Number;
    }
}
