using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using Pidgin;
using Robust.Shared.EntitySerialization.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.EntitySerialization;

/// <summary>
/// This class provides methods for deserializing entities from yaml. It provides some more control over
/// serialization than the methods provided by <see cref="EntitySerializationSystem"/>.
/// </summary>
internal sealed class EntityDeserializer : ISerializationContext, IEntityLoadContext,
    ITypeSerializer<EntityUid, ValueDataNode>
{
    private const int BackwardsVersion = 3;

    public SerializationManager.SerializerProvider SerializerProvider { get; } = new();

    public readonly EntityManager EntMan;
    public readonly IGameTiming Timing;
    private readonly ISawmill _log;
    private readonly ISerializationManager _seriMan;
    private readonly IComponentFactory _factory;
    private Stopwatch _stopwatch = new();

    public DeserializationOptions Options;

    /// <summary>
    /// Serialized entity data that is going to be read.
    /// </summary>
    public MappingDataNode Data = new();

    /// <summary>
    /// Subset of <see cref="Data"/> relevant to each entity.
    /// </summary>
    public readonly Dictionary<EntityUid, MappingDataNode> EntityData = new();

    public LoadResult Result = new();

    public readonly Dictionary<int, string> TileMap = new();
    public readonly Dictionary<string, IComponent> CurrentReadingEntityComponents = new();
    public HashSet<string> CurrentlyIgnoredComponents = new();
    public string? CurrentComponent;
    public readonly HashSet<EntityUid> ToDelete = new();

    /// <summary>
    /// Entities that need to be flagged as map-initialized. This will not actually run map-init logic, this is for
    /// loading entities that have already been map-initialized and just need to be flagged as such.
    /// </summary>
    public readonly HashSet<EntityUid> PostMapInit = new();

    public readonly Dictionary<int, EntityUid> UidMap = new();

    public readonly List<EntityUid> SortedEntities = new();

    /// <summary>
    /// Are we currently iterating prototypes or entities for writing.
    /// </summary>
    public bool WritingReadingPrototypes { get; private set; }

    public Dictionary<string, string> RenamedPrototypes = new();
    public HashSet<string> DeletedPrototypes = new();
    private readonly IPrototypeManager _proto;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;

    public EntityDeserializer(
        EntityManager entMan,
        IGameTiming timing,
        IPrototypeManager proto,
        IComponentFactory factory,
        ISerializationManager seriMan,
        SharedMapSystem map,
        SharedTransformSystem xform,
        ISawmill log)
    {
        EntMan = entMan;
        Timing = timing;
        _log = log;
        _factory = factory;
        _seriMan = seriMan;
        _proto = proto;
        _map = map;
        _xform = xform;
        SerializerProvider.RegisterSerializer(this);
    }

    /// <summary>
    /// Prepare to load entities from a file. This will load in the metdata, tile-map, and validate that all referenced
    /// entity prototypes exists
    /// </summary>
    public bool Setup(
        MappingDataNode data,
        DeserializationOptions options,
        Dictionary<string, string>? renamedPrototypes,
        HashSet<string>? deletedPrototypes)
    {
        Reset();
        Data = data;
        Options = options;
        RenamedPrototypes = renamedPrototypes ?? RenamedPrototypes;
        DeletedPrototypes = deletedPrototypes ?? DeletedPrototypes;

        ReadMetadata();
        if (Result.Version < BackwardsVersion)
        {
            _log.Error(
                $"Cannot handle this map file version, found v{Result.Version} and require at least v{BackwardsVersion}");
            return false;
        }

        if (!VerifyPrototypes())
            return false;

        ReadTileMap();
        return true;
    }


    /// <summary>
    /// Allocate entities, load the per-entity serialized data, and populate the various entity collections.
    /// </summary>
    public void CreateEntities()
    {
        // Alloc entities, and populate the yaml uid -> EntityUid maps
        AllocateEntities();

        // Load the prototype data onto entities, e.g. transform parents, etc.
        LoadEntities();

        // Read the list of maps, grids, and orphan entities
        ReadMapsAndGrids();

        // grids prior to engine v175 might've been serialized with empty chunks which now throw debug asserts.
        RemoveEmptyChunks();

        // Assign MapSaveTileMapComponent to all read grids. This is used to avoid large file diffs if the tile map changes.
        StoreGridTileMap();

        // Separate maps and orphaned entities out from the "true" null-space entities.
        ProcessNullspaceEntities();

        CheckCategory();
    }

    /// <summary>
    /// Finish entity startup & initialization, and delete any invalid entities
    /// </summary>
    public void StartEntities()
    {
        AdoptGrids();
        PauseMaps();
        BuildEntityHierarchy();
        StartEntitiesInternal();
        SetPostMapinit();
        MapinitializeEntities();
        ProcessDeletions();
    }

    private void ReadMetadata()
    {
        var meta = Data.Get<MappingDataNode>("meta");
        Result.Version = meta.Get<ValueDataNode>("format").AsInt();

        if (meta.TryGet<ValueDataNode>("engineVersion", out var engVer))
            Result.EngineVersion = engVer.Value;

        if (meta.TryGet<ValueDataNode>("forkId", out var forkId))
            Result.ForkId = forkId.Value;

        if (meta.TryGet<ValueDataNode>("forkVersion", out var forkVer))
            Result.ForkVersion = forkVer.Value;

        if (meta.TryGet<ValueDataNode>("time", out var timeNode) && DateTime.TryParse(timeNode.Value, out var time))
            Result.Time = time;

        if (meta.TryGet<ValueDataNode>("category", out var catNode) &&
            Enum.TryParse<Category>(catNode.Value, out var res))
            Result.Category = res;
    }

    private bool VerifyPrototypes()
    {
        _stopwatch.Restart();
        var fail = false;
        var key = Result.Version >= 4 ? "proto" : "type";
        var entities =Data.Get<SequenceDataNode>("entities");

        foreach (var metaDef in entities.Cast<MappingDataNode>())
        {
            if (!metaDef.TryGet<ValueDataNode>(key, out var typeNode))
                continue;

            var type = typeNode.Value;
            if (string.IsNullOrWhiteSpace(type))
                continue;

            if (RenamedPrototypes.TryGetValue(type, out var newType))
                type = newType;

            if (DeletedPrototypes.Contains(type))
            {
                _log.Warning("Map contains an obsolete/removed prototype: {0}. This may cause unexpected errors.", type);
                continue;
            }

            if (_proto.HasIndex<EntityPrototype>(type))
                continue;

            _log.Error("Missing prototype for map: {0}", type);
            fail = true;
        }

        _log.Debug($"Verified entities in {_stopwatch.Elapsed}");

        if (!fail)
            return true;

        _log.Error("Found missing prototypes in map file. Missing prototypes have been dumped to logs.");
        return false;
    }

    private void ReadTileMap()
    {
        // Load tile mapping so that we can map the stored tile IDs into the ones actually used at runtime.
        _stopwatch.Restart();
        var tileMap = Data!.Get<MappingDataNode>("tilemap");
        var migrations = new Dictionary<string, string>();
        foreach (var proto in _proto.EnumeratePrototypes<TileAliasPrototype>())
        {
            migrations.Add(proto.ID, proto.Target);
        }

        foreach (var (key, value) in tileMap.Children)
        {
            var tileId = ((ValueDataNode) key).AsInt();
            var tileDefName = ((ValueDataNode) value).Value;
            if (migrations.TryGetValue(tileDefName, out var @new))
                tileDefName = @new;

            TileMap.Add(tileId, tileDefName);
        }

        _log.Debug($"Read tilemap in {_stopwatch.Elapsed}");
    }

    private void AllocateEntities()
    {
        _stopwatch.Restart();

        if (Result.Version >= 4)
        {
            var metaEntities = Data.Get<SequenceDataNode>("entities");

            foreach (var metaDef in metaEntities.Cast<MappingDataNode>())
            {
                string? type = null;
                var deletedPrototype = false;
                if (metaDef.TryGet<ValueDataNode>("proto", out var typeNode)
                    && !string.IsNullOrWhiteSpace(typeNode.Value))
                {
                    if (DeletedPrototypes.Contains(typeNode.Value))
                    {
                        deletedPrototype = true;
                        if (_proto.HasIndex<EntityPrototype>(typeNode.Value))
                            type = typeNode.Value;
                    }
                    else if (RenamedPrototypes.TryGetValue(typeNode.Value, out var newType))
                        type = newType;
                    else
                        type = typeNode.Value;
                }

                var entities = (SequenceDataNode) metaDef["entities"];
                EntityPrototype? proto = null;

                if (type != null)
                    _proto.TryIndex(type, out proto);

                foreach (var entityDef in entities.Cast<MappingDataNode>())
                {
                    var uid = entityDef.Get<ValueDataNode>("uid").AsInt();

                    var entity = EntMan.AllocEntity(proto);
                    Result.Entities.Add(entity);
                    UidMap.Add(uid, entity);
                    EntityData.Add(entity, entityDef);

                    if (entityDef.TryGet<ValueDataNode>("mapInit", out var initNode)
                        && initNode.Value == "true")
                    {
                        PostMapInit.Add(entity);
                    }

                    if (deletedPrototype)
                        ToDelete.Add(entity);
                    else if (Options.StoreYamlUids)
                        EntMan.AddComponent<YamlUidComponent>(entity).Uid = uid;
                }
            }
        }
        else
        {
            var entities = Data.Get<SequenceDataNode>("entities");

            foreach (var entityDef in entities.Cast<MappingDataNode>())
            {
                EntityUid entity;
                if (entityDef.TryGet<ValueDataNode>("type", out var typeNode))
                {
                    if (DeletedPrototypes.Contains(typeNode.Value))
                    {
                        entity = EntMan.AllocEntity(null);
                        ToDelete.Add(entity);
                    }
                    else if (RenamedPrototypes.TryGetValue(typeNode.Value, out var newType))
                    {
                        _proto.TryIndex<EntityPrototype>(newType, out var prototype);
                        entity = EntMan.AllocEntity(prototype);
                    }
                    else
                    {
                        _proto.TryIndex<EntityPrototype>(typeNode.Value, out var prototype);
                        entity = EntMan.AllocEntity(prototype);
                    }
                }
                else
                {
                    entity = EntMan.AllocEntity(null);
                }

                var uid = entityDef.Get<ValueDataNode>("uid").AsInt();
                Result.Entities.Add(entity);
                UidMap.Add(uid, entity);
                EntityData.Add(entity, entityDef);

                if (Options.StoreYamlUids)
                    EntMan.AddComponent<YamlUidComponent>(entity).Uid = uid;
            }
        }

        _log.Debug($"Allocated {EntityData.Count} entities in {_stopwatch.Elapsed}");
    }

    private void LoadEntities()
    {
        _stopwatch.Restart();
        foreach (var (entity, data) in EntityData)
        {
            try
            {
                LoadEntity(entity, MetaData(entity));
            }
            catch (Exception e)
            {
#if !EXCEPTION_TOLERANCE
                throw;
#endif
                ToDelete.Add(entity);
                var yamlIndex = EntityData[entity].Get<ValueDataNode>("uid").AsInt();
                _log.Error($"Encountered error while loading entity. Yaml uid: {yamlIndex}. Loaded loaded entity: {EntMan.ToPrettyString(entity)}. Error:\n{e}.");
            }
        }

        _log.Debug($"Loaded {EntityData.Count} entities in {_stopwatch.Elapsed}");
    }

    private void LoadEntity(EntityUid uid, MetaDataComponent meta)
    {
        CurrentReadingEntityComponents.Clear();
        CurrentlyIgnoredComponents.Clear();

        if (Data.TryGet("components", out SequenceDataNode? componentList))
        {
            var prototype = meta.EntityPrototype;
            CurrentReadingEntityComponents.EnsureCapacity(componentList.Count);
            foreach (var compData in componentList.Cast<MappingDataNode>())
            {
                var datanode = compData.Copy();
                datanode.Remove("type");
                var value = ((ValueDataNode)compData["type"]).Value;
                if (!_factory.TryGetRegistration(value, out var reg))
                {
                    if (!_factory.IsIgnored(value))
                        _log.Error($"Encountered unregistered component ({value}) while loading entity {EntMan.ToPrettyString(uid)}");
                    continue;
                }

                var compType = reg.Type;
                if (prototype?.Components != null && prototype.Components.TryGetValue(value, out var protoData))
                {
                    datanode = _seriMan.PushCompositionWithGenericNode(
                            compType,
                            [protoData.Mapping],
                            datanode,
                            this);
                }

                CurrentComponent = value;
                CurrentReadingEntityComponents[value] = (IComponent) _seriMan.Read(compType, datanode, this)!;
                CurrentComponent = null;
            }
        }

        if (Data.TryGet("missingComponents", out SequenceDataNode? missingComponentList))
            CurrentlyIgnoredComponents = missingComponentList.Cast<ValueDataNode>().Select(x => x.Value).ToHashSet();

        EntityPrototype.LoadEntity((uid, meta), _factory, EntMan, _seriMan, this);

        if (CurrentlyIgnoredComponents.Count > 0)
            meta.LastComponentRemoved = Timing.CurTick;
    }

    private void ReadMapsAndGrids()
    {
        if (Result.Version < 7)
        {
            ReadMapsAndGridsFallback();
            return;
        }

        var maps = Data.Get<SequenceDataNode>("maps");
        foreach (var node in maps)
        {
            var yamlId = ((ValueDataNode) node).AsInt();
            var uid = UidMap[yamlId];
            if (TryComp(uid, out MapComponent? map))
                Result.Maps.Add((uid, map));
            else
                _log.Error($"Missing map entity: {EntMan.ToPrettyString(uid)}");
        }

        var grids = Data.Get<SequenceDataNode>("grids");
        foreach (var node in grids)
        {
            var yamlId = ((ValueDataNode) node).AsInt();
            var uid = UidMap[yamlId];
            if (TryComp(uid, out MapGridComponent? grid))
                Result.Grids.Add((uid, grid));
            else
                _log.Error($"Missing grid entity: {EntMan.ToPrettyString(uid)}");
        }

        var orphans = Data.Get<SequenceDataNode>("orphans");
        foreach (var node in orphans)
        {
            var yamlId = ((ValueDataNode) node).AsInt();
            var uid = UidMap[yamlId];

            if (EntMan.HasComponent<MapComponent>(uid) || Transform(uid).ParentUid.IsValid())
                _log.Error($"Entity {EntMan.ToPrettyString(uid)} was incorrectly labelled as an orphan?");
            else
                Result.Orphans.Add(uid);
        }
    }

    private void ReadMapsAndGridsFallback()
    {
        foreach (var uid in Result.Entities)
        {
            if (TryComp(uid, out MapComponent? map))
                Result.Maps.Add((uid, map));

            if (TryComp(uid, out MapGridComponent? grid))
                Result.Grids.Add((uid, grid));
        }
    }

    private void RemoveEmptyChunks()
    {
        var gridQuery = EntMan.GetEntityQuery<MapGridComponent>();
        foreach (var uid in EntityData.Keys)
        {
            if (!gridQuery.TryGetComponent(uid, out var gridComp))
                continue;

            foreach (var (index, chunk) in gridComp.Chunks)
            {
                if (chunk.FilledTiles > 0)
                    continue;

                _log.Warning(
                    $"Encountered empty chunk while deserializing map. Grid: {EntMan.ToPrettyString(uid)}. Chunk index: {index}");
                gridComp.Chunks.Remove(index);
            }
        }
    }

    private void StoreGridTileMap()
    {
        foreach (var entity in Result.Grids)
        {
            EntMan.EnsureComponent<MapSaveTileMapComponent>(entity).TileMap = TileMap;
        }
    }

    private void BuildEntityHierarchy()
    {
        _stopwatch.Restart();
        var processed = new HashSet<EntityUid>(Result.Entities.Count);

        foreach (var ent in Result.Entities)
        {
            BuildEntityHierarchy(ent, processed);
        }

        _log.Debug($"Built entity hierarchy for {Result.Entities.Count} entities in {_stopwatch.Elapsed}");
    }

    /// <summary>
    /// Validate that the category read from the metadata section is correct
    /// </summary>
    private void CheckCategory()
    {
        switch (Result.Category)
        {
            case Category.Map:
                if (Result.Maps.Count == 1)
                    return;
                _log.Error($"Expected file to contain a single map, but instead found: {Result.Maps.Count}");
                break;

            case Category.Grid:
                if (Result.Maps.Count == 0 && Result.Grids.Count == 1)
                    return;
                _log.Error($"Expected file to contain a single grid, but instead found: {Result.Grids.Count}");
                break;

            case Category.Entity:
                if (Result.Maps.Count == 0 && Result.Grids.Count == 0 && Result.Orphans.Count == 1)
                    return;
                _log.Error($"Expected file to contain a orphaned entity, but instead found: {Result.Orphans.Count}");
                break;

            default:
                return;
        }

        Result.Category = Category.Unknown;
    }

    /// <summary>
    /// In case there are any "orphaned" grids, we want to ensure that they all have a map before we initialize them,
    /// as grids in null-space are not yet supported.
    /// </summary>
    private void AdoptGrids()
    {
        foreach (var grid in Result.Grids)
        {
            if (EntMan.HasComponent<MapComponent>(grid.Owner))
                continue;

            var xform = Transform(grid.Owner);
            if (xform.ParentUid.IsValid())
                continue;

            DebugTools.Assert(Result.Orphans.Contains(grid.Owner));
            _log.Error($"Encountered orphaned grid. Automatically creating a map for the grid.");
            var map = _map.CreateUninitializedMap();

            Result.Maps.Add(map);
            Result.Orphans.Remove(grid.Owner);
            xform._parent = map.Owner;
            DebugTools.Assert(!xform._mapIdInitialized);
        }
    }

    private void PauseMaps()
    {
        if (!Options.PauseMaps)
            return;

        foreach (var ent in Result.Maps)
        {
            _map.SetPaused(ent!, true);
        }
    }

    private void BuildEntityHierarchy(EntityUid uid, HashSet<EntityUid> processed)
    {
        // If we've already added it then skip.
        if (!processed.Add(uid))
            return;

        if (!TryComp(uid, out TransformComponent? xform))
            return;

        // Ensure parent is done first.
        var parent = xform.ParentUid;
        if (xform.ParentUid == EntityUid.Invalid)
            Result.RootNodes.Add(uid);
        else
            BuildEntityHierarchy(parent, processed);

        if (Result.Entities.Contains(uid))
            SortedEntities.Add(uid);
    }

    private void ProcessNullspaceEntities()
    {
        foreach (var uid in Result.RootNodes)
        {
            if (EntMan.HasComponent<MapComponent>(uid))
            {
                DebugTools.Assert(Result.Maps.Any(x => x.Owner == uid));
                continue;
            }

            if (Result.Orphans.Contains(uid))
                continue;

            Result.NullspaceEntities.Add(uid);

            // Null-space grids are not yet supported.
            // So it shouldn't have been possible to save a grid without it being flagged as an orphan.
            DebugTools.Assert(!EntMan.HasComponent<MapGridComponent>(uid));
        }
    }

    private void StartEntitiesInternal()
    {
        _stopwatch.Restart();
        var metaQuery = EntMan.GetEntityQuery<MetaDataComponent>();
        foreach (var uid in SortedEntities)
        {
            StartupEntity(uid, metaQuery.GetComponent(uid));
        }
        _log.Debug($"Started up {Result.Entities.Count} entities in {_stopwatch.Elapsed}");
    }

    private void StartupEntity(EntityUid uid, MetaDataComponent metadata)
    {
        ResetNetTicks(uid, metadata);
        EntMan.InitializeEntity(uid, metadata);
        EntMan.StartEntity(uid);
    }

    private void ResetNetTicks(EntityUid uid, MetaDataComponent metadata)
    {
        var data = EntityData[uid];
        if (!data.TryGet("components", out SequenceDataNode? componentList))
            return;

        if (metadata.EntityPrototype is not { } prototype)
            return;

        foreach (var component in metadata.NetComponents.Values)
        {
            var compName = _factory.GetComponentName(component.GetType());

            if (componentList.Cast<MappingDataNode>().Any(p => ((ValueDataNode) p["type"]).Value == compName))
            {
                if (prototype.Components.ContainsKey(compName))
                {
                    // This component is modified by the map so we have to send state.
                    // Though it's still in the prototype itself so creation doesn't need to be sent.
                    component.ClearCreationTick();
                }

                continue;
            }

            // This component is not modified by the map file,
            // so the client will have the same data after instantiating it from prototype ID.
            component.ClearTicks();
        }
    }

    private void SetPostMapinit()
    {
        if (Result.Version < 7)
        {
            SetPostMapinitFallback();
            return;
        }

        if (PostMapInit.Count == 0)
            return;

        _stopwatch.Restart();

        foreach (var uid in PostMapInit)
        {
            if (!TryComp(uid, out MetaDataComponent? meta))
                continue;

            DebugTools.Assert(meta.EntityLifeStage == EntityLifeStage.Initialized);
            meta.EntityLifeStage = EntityLifeStage.MapInitialized;
        }

        _log.Debug($"Finished flagging mapinit in {_stopwatch.Elapsed}");
    }

    private void SetPostMapinitFallback()
    {
        var metadata = Data.Get<MappingDataNode>("meta");
        if (metadata.TryGet<ValueDataNode>("postmapinit", out var mapInitNode) && !mapInitNode.AsBool())
            return;

        foreach (var uid in Result.Entities)
        {
            if (!TryComp(uid, out MetaDataComponent? meta))
                continue;


            DebugTools.Assert(meta.EntityLifeStage == EntityLifeStage.Initialized);
            meta.EntityLifeStage = EntityLifeStage.MapInitialized;
        }
    }

    private  void MapinitializeEntities()
    {
        if (!Options.InitializeMaps)
        {
            foreach (var ent in Result.Maps)
            {
                if (MetaData(ent.Owner).EntityLifeStage < EntityLifeStage.MapInitialized)
                    _map.SetPaused(ent!, true);
            }

            return;
        }

        foreach (var ent in Result.Maps)
        {
            if (!ent.Comp.MapInitialized)
                _map.InitializeMap(ent!, unpause: !Options.PauseMaps);
        }
    }

    private void ProcessDeletions()
    {
        foreach (var uid in ToDelete)
        {
            EntMan.DeleteEntity(uid);
            Result.Entities.Remove(uid);
        }
    }

    public void Reset()
    {
        Options = DeserializationOptions.Default;
        Data = new();
        Result = new();
        EntityData.Clear();
        ToDelete.Clear();
        PostMapInit.Clear();
        DeletedPrototypes.Clear();
        RenamedPrototypes.Clear();
        CurrentReadingEntityComponents.Clear();
        CurrentlyIgnoredComponents.Clear();
        SortedEntities.Clear();
        CurrentComponent = null;
        UidMap.Clear();
    }

    // Create custom object serializers that will correctly allow data to be overriden by the map file.
    bool IEntityLoadContext.TryGetComponent(string componentName, [NotNullWhen(true)] out IComponent? component)
    {
        return CurrentReadingEntityComponents.TryGetValue(componentName, out component);
    }

    public IEnumerable<string> GetExtraComponentTypes()
    {
        return CurrentReadingEntityComponents.Keys;
    }

    public bool ShouldSkipComponent(string compName)
    {
        return CurrentlyIgnoredComponents.Contains(compName);
    }

    public MetaDataComponent MetaData(EntityUid uid) => EntMan.GetComponent<MetaDataComponent>(uid);
    public TransformComponent Transform(EntityUid uid) => EntMan.GetComponent<TransformComponent>(uid);
    public bool TryComp<T>(EntityUid uid, [NotNullWhen(true)]out T? c) where T : IComponent
        => EntMan.TryGetComponent(uid, out c);

    #region ITypeSerializer

    ValidationNode ITypeValidator<EntityUid, ValueDataNode>.Validate(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context)
    {
        if (node.Value is "null" or "invalid")
            return new ValidatedValueNode(node);

        if (!int.TryParse(node.Value, out _))
            return new ErrorNode(node, "Invalid EntityUid");

        return new ValidatedValueNode(node);
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        EntityUid value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        return value.IsValid()
            ? new ValueDataNode(value.Id.ToString(CultureInfo.InvariantCulture))
            : new ValueDataNode("invalid");
    }

    EntityUid ITypeReader<EntityUid, ValueDataNode>.Read(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context,
        ISerializationManager.InstantiationDelegate<EntityUid>? _)
    {
        if (node.Value == "invalid" && CurrentComponent == "Transform")
            return EntityUid.Invalid;

        if (int.TryParse(node.Value, out var val) && UidMap.TryGetValue(val, out var entity))
            return entity;

        _log.Error($"Invalid yaml entity id: '{val}'");
        return EntityUid.Invalid;
    }

    [MustUseReturnValue]
    public EntityUid Copy(
        ISerializationManager serializationManager,
        EntityUid source,
        EntityUid target,
        bool skipHook,
        ISerializationContext? context = null)
    {
        return new(source.Id);
    }

    #endregion
}
