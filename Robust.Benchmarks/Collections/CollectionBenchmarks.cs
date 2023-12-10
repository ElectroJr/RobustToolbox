using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Robust.Shared.GameObjects;

namespace Robust.Benchmarks.Collections;

public abstract class CollectionBenchmarks
{
    public const int N = 1000;

    public sealed class DataClass
    {
        public int A;
        public int B;
        public int C;
    }

    public struct DataStruct
    {
        public int A;
        public int B;
        public int C;
    }

    public Dictionary<EntityUid, DataClass> ClassDictionary = new();
    public Dictionary<EntityUid, DataStruct> StructDictionary = new();

    public List<DataClass> ClassList = new();
    public List<DataStruct> StructList = new();

    public HashSet<EntityUid> KeySet = new();
    public List<EntityUid> KeyList = new();
    public EntityUid[] DictionaryKeys = new EntityUid[N];
    public EntityUid[] ExtraDictionaryKeys = new EntityUid[2*N];
    public int[] ListKeys = new int[N];

    [GlobalSetup]
    public void Setup()
    {
        for (int i = 0; i < N; i++)
        {
            ClassList.Add(new());
            StructList.Add(new());
            ListKeys[i] = i;

            var k = new EntityUid(i);
            DictionaryKeys[i] = k;
            ExtraDictionaryKeys[i] = k;
            KeySet.Add(k);
        }

        for (int i = N; i < 2*N; i++)
        {
            ExtraDictionaryKeys[i] = new EntityUid(i);
        }

        void Shuffle<T>(T[] arr)
        {
            var rng = new Random(42);
            var n = arr.Length;
            while (n > 1)
            {
                n -= 1;
                var k = rng.Next(n + 1);
                (arr[k], arr[n]) = (arr[n], arr[k]);
            }
        }

        Shuffle(ListKeys);
        Shuffle(DictionaryKeys);
        Shuffle(ExtraDictionaryKeys);

        foreach (var k in DictionaryKeys)
        {
            ClassDictionary[k] = new();
            StructDictionary[k] = new();
        }

        KeyList = StructDictionary.Keys.ToList();
    }
}
