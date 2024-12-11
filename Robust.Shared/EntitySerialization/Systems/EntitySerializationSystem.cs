using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Robust.Shared.EntitySerialization.Systems;

public sealed partial class EntitySerializationSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly IReflectionManager _reflection = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IConfigurationManager _conf = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    // Changes between version 6 -> 7:
    // Added meta.category
    // added grids, maps, orphans lists
    // added postinit list
    // added client version metadata info

    private Stopwatch _stopwatch = new();

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntitySerializer _serializer = default!;
    private EntityDeserializer _deserializer = default!;

    public override void Initialize()
    {
        base.Initialize();
        var loaderLog = _log.GetSawmill("entity_deserializer");
        var writerLog = _log.GetSawmill("entity_serializer");
        loaderLog.Level = LogLevel.Info;

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _serializer = new EntitySerializer(_reflection, _tileDefManager, EntityManager, _timing, writerLog, _factory, _serManager, _conf);
        _deserializer = new EntityDeserializer(EntityManager, _timing, _prototypeManager, _factory, _serManager, _mapSystem, loaderLog);

        _serializer.OnIsSerializeable += OnIsSerializable;
    }
}
