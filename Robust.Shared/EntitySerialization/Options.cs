using System.Numerics;
using JetBrains.Annotations;
using Robust.Shared.EntitySerialization.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Shared.EntitySerialization;

[PublicAPI]
public record struct SerializationOptions
{
    public static readonly SerializationOptions Default = new();

    /// <summary>
    /// What to do when serializing the entity uid of an entity that is not currently being serialized. E.g., what
    /// happens when serializing a map that has entities with components that store references to a null-space entity.
    ///
    /// Note that this does not affect the treatment of <see cref="TransformComponent.ParentUid"/>, which will never
    /// auto-include parents.
    /// </summary>
    public MissingEntityBehaviour MissingEntityBehaviour = MissingEntityBehaviour.Error;

    /// <summary>
    /// Whether or not to log an error when serializing an entity without it parent.
    /// </summary>
    public bool ErrorOnOrphan = true;

    /// <summary>
    /// Whether or not to log a warning when auto-including entities while serializing. See <see cref="MissingEntityBehaviour"/>.
    /// </summary>
    public bool WarnOnAutoInclude = true;

    /// <summary>
    /// If true, the serializer will log an error if it encounters a post map-init entity.
    /// </summary>
    public bool ExpectPreInit;

    public Category Category;

    public SerializationOptions()
    {
    }
}

[PublicAPI]
public record struct DeserializationOptions()
{
    public static readonly DeserializationOptions Default = new();

    /// <summary>
    /// If true, each loaded entity will get a <see cref="YamlUidComponent"/> that stores the uid that the entity
    /// had in the yaml file. This is used to maintain consistent entity labelling on subsequent saves.
    /// </summary>
    public bool StoreYamlUids = false;

    /// <summary>
    /// If true, all maps that get created while loading this file will get map-initialized.
    /// </summary>
    public bool InitializeMaps = false;

    /// <summary>
    /// If true, all maps that get created while loading this file will get paused.
    /// Note that this will not automatically unpause maps that were saved while paused.
    /// </summary>
    public bool PauseMaps = false;
}

/// <summary>
/// Superset of <see cref="EntitySerialization.DeserializationOptions"/> that contain information relevant to loading maps & grids.
/// </summary>
public struct MapLoadOptions()
{
    public static readonly MapLoadOptions Default = new();

    /// <summary>
    /// If specified, all orphaned entities and the children of all loaded maps will be re-parented to the target map.
    /// I.e., this will merge map contents onto an existing map. This will also cause any maps that get loaded to
    /// delete themselves after their children have been moved.
    /// </summary>
    public MapId? TargetMap = null;

    /// <summary>
    /// Offset to apply to the position of any loaded entities that are directly parented to a map.
    /// </summary>
    public Vector2 Offset;

    /// <summary>
    /// Rotation to apply to the position & local rotation of any loaded entities that are directly parented to a map.
    /// </summary>
    public Angle Rotation;

    /// <summary>
    /// Options to use when deserializing entities.
    /// </summary>
    public DeserializationOptions DeserializationOptions = DeserializationOptions.Default;

    /// <summary>
    /// When loading a single map, this will attempt to force the map to use the given map id.
    /// </summary>
    public MapId? ForceMapId;

    public Category? ExpectedCategory;
}
