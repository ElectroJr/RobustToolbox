using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Robust.Benchmarks.EntityLists;

/*

Conclusion: Pointers can be just as fast as classes (which should basically just be pointers).
Array indices are noticeably slower, probably because of bound checks

|                   Method |        Mean |     Error |    StdDev | Ratio | RatioSD |
|------------------------- |------------:|----------:|----------:|------:|--------:|
|             ArrayIndices |    96.05 ns |  0.128 ns |  0.113 ns |  1.00 |    0.00 |
|              ClassLookup |    46.86 ns |  0.200 ns |  0.167 ns |  0.49 |    0.00 |
|             ReadWritePtr |   391.26 ns |  0.086 ns |  0.076 ns |  4.07 |    0.00 |
|          ReadWriteStruct | 4,952.20 ns | 24.745 ns | 23.147 ns | 51.54 |    0.25 |
|       ReadWriteStructPtr |    48.84 ns |  0.074 ns |  0.066 ns |  0.51 |    0.00 |
| ReadWriteStructPtrUnsafe |    45.86 ns |  0.072 ns |  0.063 ns |  0.48 |    0.00 |

*/

public unsafe class PinnedArrayBenchmark
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DataStruct
    {
        public int Uid;
        public int LastSent;
        public int LastLeft;
    }

    public sealed class DataClass
    {
        public int Uid;
        public int LastSent;
        public int LastLeft;
    }

    public const int N = 10000;
    public const int M = 100;

    public DataStruct[] Array = default!;
    public DataClass[] ClassArray = new DataClass[N];

    public List<int> Indices = new(M);
    public List<DataClass> Subset = new(M);
    public List<IntPtr> Ptrs = new(M);

    [GlobalSetup]
    public void Setup()
    {
        Array = GC.AllocateArray<DataStruct>(N, pinned: true);
        for (int i = 0; i < N; i++)
        {
            ClassArray[i] = new();
            Indices.Add(i);
        }

        Shuffle(Indices, new Random(42));
        Indices = Indices.Take(M).ToList();

        foreach (var i in Indices)
        {
            Subset.Add(ClassArray[i]);
            Ptrs.Add(Marshal.UnsafeAddrOfPinnedArrayElement(Array, i));
        }
    }

    static void Shuffle<T>(IList<T> arr, Random rng)
    {
        var n = arr.Count;
        while (n > 1)
        {
            n -= 1;
            var k = rng.Next(n + 1);
            (arr[k], arr[n]) = (arr[n], arr[k]);
        }
    }

    [Benchmark(Baseline = true)]
    public void ArrayIndices()
    {
        foreach (var index in CollectionsMarshal.AsSpan(Indices))
        {
            ref var entry = ref Array[index];
            entry.Uid = entry.LastLeft + 1;
        }
    }

    [Benchmark]
    public void ClassLookup()
    {
        foreach (var entry in CollectionsMarshal.AsSpan(Subset))
        {
            entry.Uid = entry.LastLeft + 1;
        }
    }

    [Benchmark]
    public void ReadWritePtr()
    {
        foreach (var ptr in CollectionsMarshal.AsSpan(Ptrs))
        {
            Marshal.WriteInt32(ptr, 1 + Marshal.ReadInt32(ptr, sizeof(float)));
        }
    }

    [Benchmark]
    public void ReadWriteStruct()
    {
        foreach (var ptr in CollectionsMarshal.AsSpan(Ptrs))
        {
            var entry = Marshal.PtrToStructure<DataStruct>(ptr);
            entry.Uid = entry.LastLeft + 1;
            Marshal.StructureToPtr(entry, ptr, false);
        }
    }

    [Benchmark]
    public void ReadWriteStructPtr()
    {
        foreach (var ptr in CollectionsMarshal.AsSpan(Ptrs))
        {
            var p = (DataStruct*) ptr;
            p->Uid = p->LastLeft + 1;
        }
    }

    [Benchmark]
    public void ReadWriteStructPtrUnsafe()
    {
        foreach (var ptr in CollectionsMarshal.AsSpan(Ptrs))
        {
            ref var entry = ref Unsafe.AsRef<DataStruct>((DataStruct*) ptr);
            entry.Uid = entry.LastLeft + 1;
        }
    }
}
