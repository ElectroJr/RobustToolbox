﻿using Robust.Shared.GameObjects;
using Robust.Shared.Upload;

namespace Robust.Shared.EntitySerialization;

/// <summary>
/// This enum is used to indicate the type of entity data that was written to a file. The actual format of the file does
/// not change, but it helps avoid mistakes like accidentally using a map file when trying to load a single grid.
/// </summary>
public enum Category : byte
{
    Unknown,

    /// <summary>
    /// File should contain a single root entity, its children, and maybe some null-space entities.
    /// </summary>
    Entity,

    /// <summary>
    /// File should contain a single grid, its children, and maybe some null-space entities.
    /// </summary>
    Grid,

    /// <summary>
    /// File should contain a single map, its children, and maybe some null-space entities.
    /// </summary>
    Map,

    /// <summary>
    /// File is a full game save, and will in general contain one or more maps likely a few null-space entities.
    /// </summary>
    /// <remarks>
    /// The file might also contain additional yaml entries for things like prototypes uploaded via
    /// <see cref="IGamePrototypeLoadManager"/>, and might contain references to additional resources that need to be
    /// loaded (e.g., files uploaded using <see cref="SharedNetworkResourceManager"/>).
    /// </remarks>
    Save,
}

public enum MissingEntityBehaviour
{
    /// <summary>
    /// Log an error and replace the reference with <see cref="EntityUid.Invalid"/>
    /// </summary>
    Error,

    /// <summary>
    /// Ignore the reference, replace it with <see cref="EntityUid.Invalid"/>
    /// </summary>
    Ignore,

    /// <summary>
    /// Automatically include & serialize the referenced entity. Note that this means that the missing entity's
    /// parents will also be included, however this will not include other children. E.g., if serializing a grid that
    /// references an entity on the map, this will also cause the map to get serialized, but will not necessarily
    /// serialize everything on the map.
    /// </summary>
    /// <remarks>
    /// This is primarily intended to make it easy to auto-include information carrying null-space entities. E.g., the
    /// "minds" of players, or entities that represent power or gas networks on a grid. Note that a full game save
    /// should still try to explicitly include all relevant entities, as this could still easily fail to auto-include
    /// relevant entities if they are not explicitly referenced by some other entity.
    ///
    /// Note that this might unexpectedly change the <see cref="Category"/>. I.e., trying to serialize a grid might
    /// accidentally lead to serializing a (partial?) map file.
    /// </remarks>
    AutoInclude,

    /// <summary>
    /// Variant of <see cref="AutoInclude"/> that will also automatically include the children of any entities that
    /// that are automatically included.
    /// </summary>
    AutoIncludeChildren,
}
