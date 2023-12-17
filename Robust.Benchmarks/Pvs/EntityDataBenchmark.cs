using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Robust.Benchmarks.Pvs;

/// <summary>
/// This benchmark checks whether it is better to store the per-session entity data as a class or struct, and whether
/// its better to store them in session-specific dictionaries, or in session-indexed arrays on the metadata component.
/// </summary>
public class EntityDataBenchmark
{
    /// <summary>
    /// Number of entities in PVS range.
    /// </summary>
    public const int InView = 4000;

    /// <summary>
    /// Number of "dirty" entities in view. This would be both dirty entities, and entities that just entered PVS range.
    /// </summary>
    public const int DirtyCount = 250;

    /// <summary>
    /// Total number of entities.
    /// </summary>
    public const int N = InView * 10;

    /// <summary>
    /// Total number of "players" to use for the player-indexed array.
    /// </summary>
    public const int Players = 80;

    /// <summary>
    /// The session index for the "current" player
    /// </summary>
    public int PlayerId = 42;

    public struct DataStruct
    {
        public bool Dirty;
        public EntityUid Uid;
        public GameTick LastSent;
        public GameTick LastAcked;
        public MetaData MetaData;
    }

    public sealed class DataClass
    {
        public bool Dirty;
        public EntityUid Uid;
        public GameTick LastSent;
        public GameTick LastAcked;
        public MetaData MetaData = default!;
    }

    public sealed class MetaData
    {
        public GameTick LastModified;
        public DataClass?[] SessionData = new DataClass[Players];
    }

    public GameTick CurTick = new(42);

    // session specific dictionaries that keep track of what data has been sent to a player about a specific entity.
    public Dictionary<EntityUid, DataStruct> StructDictionary = new(N);
    public Dictionary<EntityUid, DataClass> ClassDictionary = new(N);

    /// <summary>
    /// Entities visible in pvs range. I.e., the results of RecursivelyAddTreeNode()
    /// </summary>
    public EntityUid[] VisibleEntities = default!;

    /// <summary>
    /// Variant of <see cref="VisibleEntities"/> where the metadata component is included in the pvs tree
    /// </summary>
    public (EntityUid, MetaData)[] VisibleEntitiesWithMeta = default!;

    public List<EntityUid> ToSendUid = new(InView);
    public List<EntityUid> LastSentUid = new(InView);
    public List<EntityUid> AckedUid = new(InView);
    public List<DataClass> ToSendClass = new(InView);
    public List<DataClass> LastSentClass = new(InView);
    public List<DataClass> AckedClass = new(InView);

    public Dictionary<EntityUid, MetaData> MetaQuery = new(N);

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);

        var entities = new EntityUid[N];
        for (var i = 0; i < N; i++)
        {
            entities[i] = new EntityUid(i);
        }

        // Shuffle array, just in case the order in which they are added to the dictionary matters.
        Shuffle(entities, rng);

        foreach (var ent in entities)
        {
            var meta = new MetaData();
            var data = ClassDictionary[ent] = new DataClass() {Uid = ent, MetaData = meta};
            StructDictionary[ent] = new() {Uid = ent, MetaData = meta};
            meta.LastModified = CurTick - 1;
            meta.SessionData[PlayerId] = data;
            MetaQuery[ent] = meta;
        }

        // Randomly take some number of entities as currently "in view"
        Shuffle(entities, rng);
        VisibleEntities = entities.Take(InView).ToArray();
        VisibleEntitiesWithMeta = VisibleEntities.Select(x => (x, Meta: ClassDictionary[x].MetaData)).ToArray();

        // Of those, mark a subset as dirty.
        foreach (var ent in entities.Take(DirtyCount))
        {
            ClassDictionary[ent].MetaData.LastModified = CurTick;
        }

        // re-shuffle visible entities, so that the dirty entities are randomly distributed
        Shuffle(VisibleEntities, rng);

        ToSendClass = new(VisibleEntities.Select(x => ClassDictionary[x]));
        LastSentClass = new(ToSendClass);
        AckedClass = new(ToSendClass);
        ToSendUid = new(VisibleEntities);
        LastSentUid = new(VisibleEntities);
        AckedUid = new(VisibleEntities);
    }

    static void Shuffle<T>(T[] arr, Random rng)
    {
        var n = arr.Length;
        while (n > 1)
        {
            n -= 1;
            var k = rng.Next(n + 1);
            (arr[k], arr[n]) = (arr[n], arr[k]);
        }
    }

    private void Tick()
    {
        // "next tick". This probably does nothing, but this is here just in case dotnet somehow something that makes
        // the benchmark non-representative.
        var uid = AckedUid;
        AckedUid = LastSentUid;
        LastSentUid = ToSendUid;
        ToSendUid = uid;

        var classes = AckedClass;
        AckedClass = LastSentClass;
        LastSentClass = ToSendClass;
        ToSendClass = classes;
    }

    private void DoUidAck()
    {
        foreach (var ent in AckedUid)
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, ent);
            if (Unsafe.IsNullRef(ref data))
                continue;

            data.LastAcked= CurTick;
        }
    }

    private void DoClassAck()
    {
        foreach (var data in CollectionsMarshal.AsSpan(AckedClass))
        {
            data.LastAcked= CurTick;
        }
    }

    private void ProcessUidLeave()
    {
        foreach (var ent in LastSentUid)
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, ent);
            if (Unsafe.IsNullRef(ref data))
                continue;

            data.LastSent = default;
            data.LastAcked = default;
        }
    }

    private void ProcessClassLeave()
    {
        // Next tick we process PVS-departures. Though here we just use it to reset data.LastSent.
        foreach (var data in CollectionsMarshal.AsSpan(LastSentClass))
        {
            data.LastSent = default;
            data.LastAcked = default;
        }
    }

    private void AssertUid(List<MetaData> states)
    {
        if (ToSendUid.Count != InView)
            throw new Exception("Wrong view count");

        if (states.Count != DirtyCount)
            throw new Exception("Wrong dirty count");
    }

    private void AssertClass(List<MetaData> states)
    {
        if (ToSendClass.Count != InView)
            throw new Exception("Wrong view count");

        if (states.Count != DirtyCount)
            throw new Exception("Wrong dirty count");
    }

    /// <summary>
    /// Simulate using a per-entity class-valued array indexed by some session id on the metadata component.
    /// This is a variant that combines the to-send and get-state loops.
    /// </summary>
    [Benchmark(Baseline = true)]
    public List<MetaData> ClassArrayCombinedLoop()
    {
        ToSendClass.Clear();
        var entityStates = new List<MetaData>();
        foreach (var (_, metadata) in VisibleEntitiesWithMeta.AsSpan())
        {
            var data = metadata.SessionData[PlayerId];
            if (data == null)
                continue;

            if (data.LastSent == CurTick)
                continue;

            data.LastSent = CurTick;
            ToSendClass.Add(data);
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertClass(entityStates);
        ProcessClassLeave();
        DoClassAck();
        Tick();
        return entityStates;
    }

    /// <summary>
    /// Simulate using a per-entity class-valued array indexed by some session id on the metadata component.
    /// </summary>
    [Benchmark]
    public List<MetaData> ClassArraySeparateLoop()
    {
        ToSendClass.Clear();
        int totalDirty = 0;

        // Iterate over visible entities. I.e., emulate RecursivelyAddTreeNode()
        foreach (var (_, metadata) in VisibleEntitiesWithMeta.AsSpan())
        {
            var data = metadata.SessionData[PlayerId];
            if (data == null)
                continue;

            if (data.LastSent == CurTick)
                continue;

            if (data.MetaData.LastModified >= CurTick)
            {
                data.Dirty = true;
                totalDirty++;
            }

            data.LastSent = CurTick;
            ToSendClass.Add(data);
        }

        // Fetch game states
        var entityStates = new List<MetaData>(totalDirty);
        foreach (var data in CollectionsMarshal.AsSpan(ToSendClass))
        {
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertClass(entityStates);
        ProcessClassLeave();
        DoClassAck();
        Tick();
        return entityStates;
    }


    /// <summary>
    /// Simulate using a per-entity class-valued array indexed by some session id on the metadata component.
    /// This variant assumes that the meta-data component is not returned by the "RecursivelyAddTreeNode" call
    /// I.e., instead of just an array lookup, its a dictionary + array, and should be slower than just using a
    /// dictionary
    /// </summary>
    [Benchmark]
    public List<MetaData> ClassArrayCombinedLoopNoMeta()
    {
        ToSendClass.Clear();
        var entityStates = new List<MetaData>();
        foreach (var ent in VisibleEntities.AsSpan())
        {
            if (!MetaQuery.TryGetValue(ent, out var metadata))
                continue;

            var data = metadata.SessionData[PlayerId];
            if (data == null)
                continue;

            if (data.LastSent == CurTick)
                continue;

            data.LastSent = CurTick;
            ToSendClass.Add(data);
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertClass(entityStates);
        ProcessClassLeave();
        DoClassAck();
        Tick();
        return entityStates;
    }

    /// <summary>
    /// Simulate using a per-session class-valued-dictionary.
    /// </summary>
    [Benchmark]
    public List<MetaData> ClassDictCombinedLoop()
    {
        ToSendClass.Clear();
        var entityStates = new List<MetaData>();
        foreach (var ent in VisibleEntities.AsSpan())
        {
            if (!ClassDictionary.TryGetValue(ent, out var data))
                continue;

            if (data.LastSent == CurTick)
                continue;

            data.LastSent = CurTick;
            ToSendClass.Add(data);
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertClass(entityStates);
        ProcessClassLeave();
        DoClassAck();
        Tick();
        return entityStates;
    }

    /// <summary>
    /// Simulate using a per-session struct-valued-dictionary.
    /// </summary>
    [Benchmark]
    public List<MetaData> StructDictCombinedLoop()
    {
        ToSendUid.Clear();
        var entityStates = new List<MetaData>();
        foreach (var ent in VisibleEntities.AsSpan())
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, ent);
            if (Unsafe.IsNullRef(ref data))
                continue;

            if (data.LastSent == CurTick)
                continue;

            data.LastSent = CurTick;
            ToSendUid.Add(ent);
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertUid(entityStates);
        ProcessUidLeave();
        DoUidAck();
        Tick();
        return entityStates;
    }

    /// <summary>
    /// Simulate using a per-session class-valued-dictionary.
    /// </summary>
    [Benchmark]
    public List<MetaData> ClassDictSeparateLoop()
    {
        ToSendClass.Clear();
        int totalDirty = 0;

        // Iterate over visible entities. I.e., emulate RecursivelyAddTreeNode()
        foreach (var ent in VisibleEntities.AsSpan())
        {
            if (!ClassDictionary.TryGetValue(ent, out var data))
                continue;

            if (data.LastSent == CurTick)
                continue;

            if (data.MetaData.LastModified >= CurTick)
            {
                data.Dirty = true;
                totalDirty++;
            }

            data.LastSent = CurTick;
            ToSendClass.Add(data);
        }

        // Fetch game states
        var entityStates = new List<MetaData>(totalDirty);
        foreach (var data in CollectionsMarshal.AsSpan(ToSendClass))
        {
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertClass(entityStates);
        ProcessClassLeave();
        DoClassAck();
        Tick();
        return entityStates;
    }

    /// <summary>
    /// Simulate using a per-session struct-valued-dictionary.
    /// </summary>
    [Benchmark]
    public List<MetaData> StructDictSeparateLoop()
    {
        ToSendUid.Clear();
        int totalDirty = 0;

        // Iterate over visible entities. I.e., emulate RecursivelyAddTreeNode()
        foreach (var ent in VisibleEntities.AsSpan())
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, ent);
            if (Unsafe.IsNullRef(ref data))
                continue;

            if (data.LastSent == CurTick)
                continue;

            if (data.MetaData.LastModified >= CurTick)
            {
                data.Dirty = true;
                totalDirty++;
            }

            data.LastSent = CurTick;
            ToSendUid.Add(ent);
        }

        // Fetch game states
        var entityStates = new List<MetaData>(totalDirty);
        foreach (var ent in CollectionsMarshal.AsSpan(ToSendUid))
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(StructDictionary, ent);
            if (data.MetaData.LastModified >= CurTick)
                entityStates.Add(data.MetaData);
        }

        AssertUid(entityStates);
        ProcessUidLeave();
        DoUidAck();
        Tick();
        return entityStates;
    }
}
