namespace Robust.Shared.Graphics.RSI;

/// <summary>
///     Specifies a direction in an RSI state.
/// </summary>
/// <remarks>
///     The ordering is important as it is used to index some arrays.
/// </remarks>
public enum RsiDirection : byte
{
    South = 0,
    North = 1,
    East = 2,
    West = 3,
    SouthEast = 4,
    SouthWest = 5,
    NorthEast = 6,
    NorthWest = 7,
}

/// <summary>
///     Enum to "offset" a cardinal direction.
/// </summary>
public enum DirectionOffset : byte
{
    /// <summary>
    ///     No offset.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Rotate direction clockwise. (North -> East, etc...)
    /// </summary>
    Clockwise = 1,

    /// <summary>
    ///     Rotate direction counter-clockwise. (North -> West, etc...)
    /// </summary>
    CounterClockwise = 2,

    /// <summary>
    ///     Rotate direction 180 degrees, so flip. (North -> South, etc...)
    /// </summary>
    Flip = 3,
}

/// <summary>
/// This enum configures how a sprite layer determines the <see cref="RsiDirection"/> to use when rendering.
/// </summary>
public enum DirectionBehaviour : byte
{
    /// <summary>
    /// Use the parent layer collection's direction.
    /// </summary>
    Inherit,

    /// <summary>
    /// Compute the direction form the world rotation of the entity associated with this layer.
    /// </summary>
    /// <remarks>
    /// This is mainly useful for sprites that have the sprites of other entities embedded within them as layers,
    /// though it can also be used to ignore any direction overrides or offsets from the layer's parent collection.
    /// </remarks>
    Entity,
}
