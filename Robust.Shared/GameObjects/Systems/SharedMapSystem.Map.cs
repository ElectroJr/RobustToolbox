using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Utility;

namespace Robust.Shared.GameObjects;

public abstract partial class SharedMapSystem
{
    protected int LastMapId;

    private void InitializeMap()
    {
        SubscribeLocalEvent<MapComponent, ComponentAdd>(OnComponentAdd);
        SubscribeLocalEvent<MapComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<MapComponent, ComponentStartup>(OnCompStartup);
        SubscribeLocalEvent<MapComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MapComponent, ComponentShutdown>(OnMapRemoved);
        SubscribeLocalEvent<MapComponent, ComponentHandleState>(OnMapHandleState);
        SubscribeLocalEvent<MapComponent, ComponentGetState>(OnMapGetState);
    }

    public bool MapExists([NotNullWhen(true)] MapId? mapId)
    {
        return mapId != null && Maps.ContainsKey(mapId.Value);
    }

    public EntityUid GetMap(MapId mapId)
    {
        return Maps[mapId];
    }

    /// <summary>
    /// Get the entity UID for a map, or <see cref="EntityUid.Invalid"/> if the map doesn't exist.
    /// </summary>
    /// <param name="mapId">The ID of the map to look up.</param>
    /// <returns>
    /// The entity UID of the map entity with the specific map ID,
    /// or <see cref="EntityUid.Invalid"/> if the map doesn't exist.
    /// </returns>
    /// <seealso cref="GetMap"/>
    public EntityUid GetMapOrInvalid(MapId? mapId)
    {
        if (TryGetMap(mapId, out var uid))
            return uid.Value;

        return EntityUid.Invalid;
    }

    public bool TryGetMap([NotNullWhen(true)] MapId? mapId, [NotNullWhen(true)] out EntityUid? uid)
    {
        if (mapId == null || !Maps.TryGetValue(mapId.Value, out var map))
        {
            uid = null;
            return false;
        }

        uid = map;
        return true;
    }

    private void OnMapHandleState(EntityUid uid, MapComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not MapComponentState state)
            return;

        DebugTools.AssertEqual(component.MapId, state.MapId);
        component.LightingEnabled = state.LightingEnabled;
        component.MapInitialized = state.Initialized;
        component.MapPaused = state.MapPaused;
    }

    private void OnMapGetState(EntityUid uid, MapComponent component, ref ComponentGetState args)
    {
        args.State = new MapComponentState(component.MapId, component.LightingEnabled, component.MapPaused, component.MapInitialized);
    }

    protected abstract MapId GetNextMapId();
    private void OnComponentAdd(EntityUid uid, MapComponent component, ComponentAdd args)
    {
        // ordered startups when
        EnsureComp<PhysicsMapComponent>(uid);
        EnsureComp<GridTreeComponent>(uid);
        EnsureComp<MovedGridsComponent>(uid);
    }

    private void OnCompInit(EntityUid uid, MapComponent component, ComponentInit args)
    {
        if (component.MapId == MapId.Nullspace)
            component.MapId = GetNextMapId();
        if (!Maps.TryAdd(component.MapId, uid))
        {
            if (Maps[component.MapId] != uid)
            {
                QueueDel(uid);
                throw new Exception($"Attempted to initialize a map {ToPrettyString(uid)} with a duplicate map id {component.MapId}");
            }
        }

        DebugTools.AssertEqual(component.MapId.IsClientSide, IsClientSide(uid));
        var msg = new MapChangedEvent(uid, component.MapId, true);
        RaiseLocalEvent(uid, msg, true);

        var ev = new MapCreatedEvent(uid, component.MapId);
        RaiseLocalEvent(uid, ev, true);
    }

    private void OnMapInit(EntityUid uid, MapComponent component, MapInitEvent args)
    {
        DebugTools.Assert(!component.MapInitialized);
        component.MapInitialized = true;
        Dirty(uid, component);
    }

    private void OnCompStartup(EntityUid uid, MapComponent component, ComponentStartup args)
    {
        // un-initialized maps are always paused.
        component.MapPaused |= !component.MapInitialized;

        if (!component.MapPaused)
            return;

        // Recursively pause all entities on the map
        component.MapPaused = false;
        SetPaused(uid, true);
    }

    private void OnMapRemoved(EntityUid uid, MapComponent component, ComponentShutdown args)
    {
        DebugTools.Assert(component.MapId != MapId.Nullspace);
        Maps.Remove(component.MapId);

        var msg = new MapChangedEvent(uid, component.MapId, false);
        RaiseLocalEvent(uid, msg, true);

        var ev = new MapRemovedEvent(uid, component.MapId);
        RaiseLocalEvent(uid, ev, true);
    }

    /// <summary>
    ///     Creates a new map, automatically assigning a map id.
    /// </summary>
    public EntityUid CreateMap(out MapId mapId, bool runMapInit = true)
    {
        mapId = GetNextMapId();
        var uid = CreateMap(mapId, runMapInit);
        return uid;
    }

    /// <inheritdoc cref="CreateMap(out Robust.Shared.Map.MapId,bool)"/>
    public EntityUid CreateMap(bool runMapInit = true) => CreateMap(out _, runMapInit);

    /// <summary>
    ///     Creates a new map with the specified map id.
    /// </summary>
    /// <exception cref="ArgumentException">Throws if an invalid or already existing map id is provided.</exception>
    public EntityUid CreateMap(MapId mapId, bool runMapInit = true)
    {
        if (Maps.ContainsKey(mapId))
            throw new ArgumentException($"Map with id {mapId} already exists");

        if (mapId == MapId.Nullspace)
            throw new ArgumentException($"Cannot create a null-space map");

        if (_netManager.IsServer && mapId.IsClientSide)
            throw new ArgumentException($"Attempted to create a client-side map on the server?");

        if (_netManager.IsClient && _netManager.IsConnected && !mapId.IsClientSide)
            throw new ArgumentException($"Attempted to create a client-side map entity with a non client-side map ID?");

        var (uid, map, meta) = CreateUninitializedMap();
        DebugTools.AssertEqual(map.MapId, MapId.Nullspace);
        map.MapId = mapId;

        // Initialize components. this should add the map id to the collections.
        EntityManager.InitializeEntity(uid, meta);
        EntityManager.StartEntity(uid);
        DebugTools.AssertEqual(Maps[mapId], uid);

        if (runMapInit)
            InitializeMap((uid, map));
        else
            SetPaused((uid, map), true);

        return uid;
    }

    /// <summary>
    ///     Creates an uninitialized map..
    /// </summary>
    public Entity<MapComponent, MetaDataComponent> CreateUninitializedMap()
    {
        var uid = EntityManager.CreateEntityUninitialized(null, out var meta);
        _meta.SetEntityName(uid, $"Map Entity", meta);
        return (uid, AddComp<MapComponent>(uid), meta);
    }

    public void DeleteMap(MapId mapId)
    {
        if (TryGetMap(mapId, out var uid))
            Del(uid);
    }

    public IEnumerable<MapId> GetAllMapIds()
    {
        return Maps.Keys;
    }
}
