using Robust.Client.Graphics;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Sprite;
using static Robust.Client.Graphics.RSI;

namespace Robust.Client.GameObjects;

// This partial class contains various public methods for reading a layer's properties
public sealed partial class SpriteSystem
{
    #region RsiState

    /// <summary>
    /// Get the RSI state being used by the current layer. Note that the return value may be an invalid state. E.g.,
    /// this might be a texture layer that does not use RSIs.
    /// </summary>
    public StateId LayerGetRsiState(Entity<SpriteComponent?> sprite, int index)
    {
        return ResolveLayer(sprite, index, out Layer? layer) ? layer.StateId : StateId.Invalid;
    }

    /// <summary>
    /// Get the RSI state being used by the current layer. Note that the return value may be an invalid state. E.g.,
    /// this might be a texture layer that does not use RSIs.
    /// </summary>
    public StateId LayerGetRsiState(Entity<SpriteComponent?> sprite, LayerKey key, StateId state)
    {
        return ResolveLayer(sprite, key, out Layer? layer) ? layer.StateId : StateId.Invalid;
    }

    #endregion

    #region RsiState

    /// <summary>
    /// Returns the RSI being used by the layer to resolve it's RSI state. If the layer does not specify an RSI, this
    /// will just be the base RSI of the owning sprite (<see cref="SpriteComponent.BaseRSI"/>).
    /// </summary>
    public RSI? LayerGetRsi(Entity<SpriteComponent?> sprite, int index)
    {
        ResolveLayer(sprite, index, out Layer? layer);
        return layer?.GetRsi();
    }

    /// <summary>
    /// Returns the RSI being used by the layer to resolve it's RSI state. If the layer does not specify an RSI, this
    /// will just be the base RSI of the owning sprite (<see cref="SpriteComponent.BaseRSI"/>).
    /// </summary>
    public RSI? LayerGetRsi(Entity<SpriteComponent?> sprite, LayerKey key, StateId state)
    {
        ResolveLayer(sprite, key, out Layer? layer);
        return layer?.GetRsi();
    }

    #endregion

    #region Directions

    public RsiDirectionType LayerGetDirections(Entity<SpriteComponent?> sprite, int index)
    {
        return TryGetLayer(sprite, index, out Layer? layer)
            ? layer.DirectionType
            : RsiDirectionType.Dir1;
    }

    public RsiDirectionType LayerGetDirections(Entity<SpriteComponent?> sprite, LayerKey key)
    {
        return TryGetLayer(sprite, key, out Layer? layer)
            ? layer.DirectionType
            : RsiDirectionType.Dir1;
    }

    #endregion
}
