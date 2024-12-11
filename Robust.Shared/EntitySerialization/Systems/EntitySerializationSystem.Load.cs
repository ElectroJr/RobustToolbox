using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using Vector2 = System.Numerics.Vector2;

namespace Robust.Shared.EntitySerialization.Systems;

public sealed partial class EntitySerializationSystem
{
    /// <summary>
    /// Tries to load entities from a yaml file.
    /// </summary>
    /// <param name="file">The file to load.</param>
    /// <param name="result">Data class containing information about the loaded entities</param>
    /// <param name="options">Optional Options for configuring loading behaviour.</param>
    public bool TryLoad(ResPath file, [NotNullWhen(true)] out LoadResult? result, MapLoadOptions? options = null)
    {
        result = null;

        if (!TryReadFile(file, out var data))
            return false;

        _stopwatch.Restart();
        var ev = new BeforeEntityReadEvent();
        RaiseLocalEvent(ev);

        var opts = options ?? MapLoadOptions.Default;

        if (opts.TargetMap is { } targetId && !_mapSystem.MapExists(targetId))
            throw new Exception($"Target map {targetId} does not exist");

        if (_mapSystem.MapExists(opts.ForceMapId))
            throw new Exception($"Target map already exists");

        if (!_deserializer.Setup(data, opts.DeserializationOptions, ev.RenamedPrototypes, ev.DeletedPrototypes))
        {
            Log.Debug($"Failed to loaded map in {_stopwatch.Elapsed}");
            return false;
        }

        _deserializer.CreateEntities();

        if (opts.ExpectedCategory is { } exp && exp != _deserializer.Result.Category)
        {
            // Did someone try to load a map file as a grid or vice versa?
            Log.Error($"File does not contain the expected data. Expected {exp} but got {_deserializer.Result.Category}");
        }

        // Reparent entities if loading entities onto an existing map.
        var reparented = new HashSet<EntityUid>();
        MergeMaps(opts, reparented);
        SetMapId(opts);

        // Apply any offsets & rotations specified by the load options
        ApplyTransform(opts);

        _deserializer.StartEntities();

        if (opts.TargetMap is {} map)
            MapinitalizeReparented(reparented, map);

        result = _deserializer.Result;
        _deserializer.Reset();

        Log.Debug($"Loaded map in {_stopwatch.Elapsed}");
        return true;
    }

    private void SetMapId(MapLoadOptions opts)
    {
        if (opts.ForceMapId is not {} id)
            return;

        if (!_deserializer.Result.Maps.TryFirstOrNull(out var map))
        {
            Log.Error($"File contained no maps?");
            return;
        }

        DebugTools.AssertEqual(map.Value.Comp.MapId, MapId.Nullspace);
        map.Value.Comp.MapId = id;
    }

    /// <summary>
    /// Tries to load a file and return a list of grid and maps
    /// </summary>
    /// <remarks>
    /// Note that even if no grid is found, this function may have created maps while loading the file.
    /// </remarks>
    public bool TryLoad(
        ResPath file,
        [NotNullWhen(true)] out HashSet<Entity<MapComponent>>? maps,
        [NotNullWhen(true)] out HashSet<Entity<MapGridComponent>>? grids,
        MapLoadOptions? options = null)
    {
        grids = null;
        maps = null;
        if (!TryLoad(file, out var data, options))
            return false;

        maps = data.Maps;
        grids = data.Grids;
        return true;
    }

    /// <summary>
    /// Tries to load a grid entity from a file. This returns false if the file contains no grids.
    /// If the file contains more than one grid, this just returns the first that gets found.
    /// </summary>
    /// <remarks>
    /// Note that even if no grid is found, this function may have created maps while loading the file.
    /// </remarks>
    public bool TryLoadGrid(
        MapId map,
        ResPath path,
        [NotNullWhen(true)] out Entity<MapGridComponent>? grid,
        DeserializationOptions? options = null,
        Vector2 offset = default,
        Angle rot = default)
    {
        var opts = new MapLoadOptions
        {
            TargetMap = map,
            Offset = offset,
            Rotation = rot,
            DeserializationOptions = options ?? DeserializationOptions.Default,
            ExpectedCategory = Category.Grid
        };

        grid = null;
        if (!TryLoad(path, out _, out var grids, opts))
            return false;

        DebugTools.AssertEqual(grids.Count, 1);
        grid = grids.FirstOrNull();
        return grid != null;
    }

    public bool TryLoadMap(
        MapId mapId,
        ResPath path,
        [NotNullWhen(true)] out Entity<MapComponent>? map,
        DeserializationOptions? options = null,
        Vector2 offset = default,
        Angle rot = default)
    {
        var opts = new MapLoadOptions
        {
            Offset = offset,
            Rotation = rot,
            DeserializationOptions = options ?? DeserializationOptions.Default,
            ExpectedCategory = Category.Map
        };

        if (_mapSystem.MapExists(mapId))
            opts.TargetMap = mapId;
        else
            opts.ForceMapId = mapId;

        map = null;
        if (!TryLoad(path, out var maps, out _, opts))
            return false;

        DebugTools.AssertEqual(maps.Count, 1);
        map = maps.FirstOrNull();
        return map != null;
    }

    private bool TryGetReader(ResPath resPath, [NotNullWhen(true)] out TextReader? reader)
    {
        if (_resourceManager.UserData.Exists(resPath))
        {
            // Log warning if file exists in both user and content data.
            if (_resourceManager.ContentFileExists(resPath))
                Log.Warning("Reading map user data instead of content");

            reader = _resourceManager.UserData.OpenText(resPath);
            return true;
        }

        if (_resourceManager.TryContentFileRead(resPath, out var contentReader))
        {
            reader = new StreamReader(contentReader);
            return true;
        }

        Log.Error($"File not found: {resPath}");
        reader = null;
        return false;
    }

    private bool TryReadFile(ResPath file, [NotNullWhen(true)] out MappingDataNode? data)
    {
        var resPath = file.ToRootedPath();
        data = null;

        if (!TryGetReader(resPath, out var reader))
            return false;

        Log.Info($"Loading file: {resPath}");
        _stopwatch.Restart();

        using var textReader = reader;
        var documents = DataNodeParser.ParseYamlStream(reader).ToArray();
        Log.Debug($"Loaded yml stream in {_stopwatch.Elapsed}");

        // Yes, logging errors in a "try" method is kinda shit, but it was throwing exceptions when I found it and I'm lazy.
        switch (documents.Length)
        {
            case < 1:
                Log.Error("Stream has no YAML documents.");
                return false;
            case > 1:
                Log.Error("Stream too many YAML documents. Map files store exactly one.");
                return false;
            default:
                data = (MappingDataNode) documents[0].Root;
                return true;
        }
    }

    private void ApplyTransform(MapLoadOptions opts)
    {
        if (opts.Rotation == Angle.Zero || opts.Offset == Vector2.Zero)
            return;

        // If merging onto a single map, the transformation was already applied by SwapRootNode()
        if (opts.TargetMap != null)
            return;

        foreach (var map in _deserializer.Result.Maps)
        {
            Matrix3x2 matrix;
            Angle rotation;

            // The original comment around this bit of logic was just:
            // > Smelly
            // I don't know what sloth meant by that, but I guess applying transforms to grid-maps is a no-no?
            if (HasComp<MapGridComponent>(map))
            {
                Log.Error($"Cannot load a map-grid with an offset or rotation.");
                rotation = default;
                matrix = Matrix3x2.Identity;
            }
            else
            {
                rotation = opts.Rotation;
                matrix = Matrix3Helpers.CreateTransform(opts.Offset, rotation);
            }

            var mapXform = Transform(map);
            foreach (var uid in mapXform._children)
            {
                var xform = Transform(uid);

                var rot = xform.LocalRotation + rotation;
                var pos = Vector2.Transform(xform.LocalPosition, matrix);
                _xform.SetLocalPositionRotation(uid, pos, rot, xform);

                DebugTools.Assert(!xform.NoLocalRotation || xform.LocalRotation == 0);
            }
        }
    }

    private void MapinitalizeReparented(HashSet<EntityUid> reparented, MapId targetId)
    {
        // fuck me I hate this map merging bullshit.

        if (!_mapSystem.TryGetMap(targetId, out var targetUid))
            throw new Exception($"Target map {targetId} does not exist");

        if (_mapSystem.IsInitialized(targetUid.Value))
        {
            foreach (var uid in reparented)
            {
                _mapSystem.RecursiveMapInit(uid);
            }
        }

        var paused = _mapSystem.IsPaused(targetUid.Value);
        foreach (var uid in reparented)
        {
            _mapSystem.RecursiveSetPaused(uid, paused);
        }
    }

    private void MergeMaps(MapLoadOptions opts, HashSet<EntityUid> reparented)
    {
        if (opts.TargetMap is not {} targetId)
            return;

        if (!_mapSystem.TryGetMap(targetId, out var targetUid))
            throw new Exception($"Target map {targetId} does not exist");

        _deserializer.Result.Category = Category.Unknown;
        var rotation = opts.Rotation;
        var matrix = Matrix3Helpers.CreateTransform(opts.Offset, rotation);
        var target = new Entity<TransformComponent>(targetUid.Value, Transform(targetUid.Value));

        foreach (var uid in _deserializer.Result.Orphans)
        {
            Reparent(reparented, uid, target, matrix, rotation);
        }

        _deserializer.Result.Orphans.Clear();

        foreach (var map in _deserializer.Result.Maps)
        {
            // The original comment around this bit of logic was just:
            // > Smelly
            // I don't know what sloth meant by that, but I guess loading a grid-map onto another grid-map for whatever
            // reason must be done without offsets?
            if (HasComp<MapGridComponent>(map))
            {
                Log.Error($"Cannot load a map-grid with an offset or rotation.");
                rotation = default;
                matrix = Matrix3x2.Identity;
            }
            else
            {
                rotation = opts.Rotation;
                matrix = Matrix3Helpers.CreateTransform(opts.Offset, rotation);
            }

            var mapXform = Transform(map);
            foreach (var uid in mapXform._children)
            {
                Reparent(reparented, uid, target, matrix, rotation);
            }

            DebugTools.AssertEqual(mapXform._children.Count, 0);
        }

        _deserializer.ToDelete.UnionWith(_deserializer.Result.Maps.Select(x => x.Owner));
        _deserializer.Result.Maps.Clear();
    }

    private void Reparent(
        HashSet<EntityUid> reparented,
        EntityUid uid,
        Entity<TransformComponent> target,
        in Matrix3x2 matrix,
        Angle rotation)
    {
        reparented.Add(uid);
        var xform = Transform(uid);
        var angle = xform.LocalRotation + rotation;
        var pos = Vector2.Transform(xform.LocalPosition, matrix);
        var coords = new EntityCoordinates(target.Owner, pos);
        _xform.SetCoordinates((uid, xform, MetaData(uid)), coords, rotation: angle, newParent: target.Comp);
    }
}
