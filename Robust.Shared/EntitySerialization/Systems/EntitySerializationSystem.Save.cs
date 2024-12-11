using System;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Robust.Shared.EntitySerialization.Systems;

public sealed partial class EntitySerializationSystem
{
    /// <inheritdoc cref="EntitySerializer.OnIsSerializeable"/>
    public event EntitySerializer.IsSerializableDelegate? OnIsSerializable;

    /// <summary>
    /// Recursively serialize the given entity and its children.
    /// </summary>
    public (MappingDataNode, Category) SerializeEntityRecursive(EntityUid uid, SerializationOptions? options = null)
    {
        _stopwatch.Restart();
        _serializer.Reset();
        if (options != null)
        {
            _serializer.Options = options.Value;
        }
        else
        {
            var preInit = LifeStage(uid) < EntityLifeStage.MapInitialized;
            _serializer.Options = SerializationOptions.Default with {ExpectPreInit = preInit};
        }

        _serializer.SerializeEntityRecursive(uid);
        Log.Debug($"Serialized {_serializer.EntityData.Count} entities in {_stopwatch.Elapsed}");

        var data = _serializer.Write();
        var cat = _serializer.GetCategory();

        _serializer.Reset();
        return (data, cat);
    }

    public Category Save(EntityUid uid, string ymlPath, SerializationOptions? options = null)
    {
        if (!Exists(uid))
            throw new Exception($"{uid} does not exist.");

        var ev = new BeforeSaveEvent(uid, Transform(uid).MapUid);
        RaiseLocalEvent(ev);

        Log.Debug($"Saving entity {ToPrettyString(uid)} to {ymlPath}");

        var data = SerializeEntityRecursive(uid, options);
        var document = new YamlDocument(data.Item1.ToYaml());

        var resPath = new ResPath(ymlPath).ToRootedPath();
        _resourceManager.UserData.CreateDir(resPath.Directory);

        using var writer = _resourceManager.UserData.OpenWriteText(resPath);
        {
            var stream = new YamlStream {document};
            stream.Save(new YamlMappingFix(new Emitter(writer)), false);
        }

        Log.Info($"Saved {ToPrettyString(uid)} to {ymlPath}");

        return data.Item2;
    }

    public void SaveMap(MapId mapId, string ymlPath, SerializationOptions? options = null)
    {
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
        {
            Log.Error($"Unable to find map {mapId}");
            return;
        }

        SaveMap(mapUid.Value, ymlPath, options);
    }

    public void SaveMap(EntityUid map, string ymlPath, SerializationOptions? options = null)
    {
        if (!HasComp<MapComponent>(map))
        {
            Log.Error($"{ToPrettyString(map)} is not a map.");
            return;
        }

        var opts = options ?? SerializationOptions.Default;
        opts.Category = Category.Map;

        var cat = Save(map, ymlPath, opts);
        if (cat != Category.Map)
            Log.Error($"Failed to save {ToPrettyString(map)} as a map. Output: {cat}");
    }

    public void SaveGrid(EntityUid grid, string ymlPath, SerializationOptions? options = null)
    {
        if (!HasComp<MapGridComponent>(grid))
        {
            Log.Error($"{ToPrettyString(grid)} is not a grid.");
            return;
        }

        if (HasComp<MapComponent>(grid))
        {
            Log.Error($"{ToPrettyString(grid)} is a map.");
            return;
        }

        var opts = options ?? SerializationOptions.Default;
        opts.Category = Category.Grid;

        var cat = Save(grid, ymlPath, opts);
        if (cat != Category.Grid)
            Log.Error($"Failed to save {ToPrettyString(grid)} as a grid. Output: {cat}");
    }
}
