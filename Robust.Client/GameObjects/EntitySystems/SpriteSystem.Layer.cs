using System;
using System.Diagnostics.CodeAnalysis;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects;

// This partial class contains various public methods for managing a sprite's layers.
// This setter methods for modifying a layer's properties are in a separate file.
public sealed partial class SpriteSystem
{
    public bool LayerExists(Entity<SpriteComponent?> sprite, int index)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return false;

        return index > 0 && index < sprite.Comp.Layers.Count;
    }

    [Obsolete("Use ResolveLayer or the override without a bool argument")]
    public bool TryGetLayer(
        Entity<SpriteComponent?> sprite,
        int index,
        [NotNullWhen(true)] out BaseLayer? layer,
        bool logMissing)
    {
        return logMissing
            ? ResolveLayer(sprite, index, out layer)
            : TryGetLayer(sprite, index, out layer);
    }

    /// <summary>
    /// Attempt to get the layer corresponding to the given index.
    /// </summary>
    public bool TryGetLayer<T>(
        Entity<SpriteComponent?> sprite,
        int index,
        [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.TryGetLayer(index, out layer);
    }

    /// <inheritdoc cref="ResolveLayer(Entity{SpriteComponent?},int,out BaseLayer?)"/>
    public bool ResolveLayer<T>(Entity<SpriteComponent?> sprite, int index, [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.ResolveLayer(index, out layer);
    }

    /// <summary>
    /// Attempt to resolve the layer corresponding to the given index. This will log an error if there is no layer
    /// with the given index.
    /// </summary>
    public bool ResolveLayer(Entity<SpriteComponent?> sprite, int index, [NotNullWhen(true)] out BaseLayer? layer)
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.ResolveLayer(index, out layer);
    }

    public bool RemoveLayer(Entity<SpriteComponent?> sprite, int index, bool logMissing = true)
    {
        return RemoveLayer(sprite.Owner, index, out _, logMissing);
    }

    public bool RemoveLayer(
        Entity<SpriteComponent?> sprite,
        int index,
        out BaseLayer? layer,
        bool logMissing = true)
    {
        layer = null;
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp, logMissing))
            return false;

        if (sprite.Comp.Sprite.RemoveLayer(index, out layer))
            return true;

        if (!logMissing)
            return true;

        Log.Error($"Layer index '{index}' on entity {ToPrettyString(sprite)} does not exist. Trace:\n{Environment.StackTrace}");
        return false;
    }

    #region AddLayer

    /// <summary>
    /// Add the given sprite layer. If an index is specified, this will insert the layer with the given index, resulting
    /// in all other layers being reshuffled.
    /// </summary>
    public int AddLayer(Entity<SpriteComponent?> sprite, BaseLayer layer, int? index = null)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        return sprite.Comp.Sprite.AddLayer(layer, index);
    }

    /// <summary>
    /// Add a layer corresponding to the given RSI state.
    /// </summary>
    /// <param name="sprite">The sprite</param>
    /// <param name="stateId">The RSI state</param>
    /// <param name="rsi">The RSI to use. If not specified, it will default to using <see cref="SpriteComponent.BaseRSI"/></param>
    /// <param name="index">The layer index to use for the new sprite.</param>
    /// <returns></returns>
    public int AddRsiLayer(Entity<SpriteComponent?> sprite, RSI.StateId stateId, RSI? rsi = null, int? index = null)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        var layer = new Layer(this, _tree, EntityManager, Log);
        index = AddLayer(sprite, layer, index);

        if (rsi != null)
            LayerSetRsi(layer, rsi, stateId);
        else
            LayerSetRsiState(layer, stateId);

        return index.Value;
    }

    /// <summary>
    /// Add a layer corresponding to the given RSI state.
    /// </summary>
    /// <param name="sprite">The sprite</param>
    /// <param name="state">The RSI state</param>
    /// <param name="path">The path to the RSI.</param>
    /// <param name="index">The layer index to use for the new sprite.</param>
    /// <returns></returns>
    public int AddRsiLayer(Entity<SpriteComponent?> sprite, RSI.StateId state, ResPath path, int? index = null)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        if (!_resourceCache.TryGetResource<RSIResource>(TextureRoot / path, out var res))
            Log.Error($"Unable to load RSI '{path}'. Trace:\n{Environment.StackTrace}");

        if (path.Extension != "rsi")
            Log.Error($"Expected rsi path but got '{path}'?");

        return AddRsiLayer(sprite, state, res?.RSI, index);
    }

    public int AddTextureLayer(Entity<SpriteComponent?> sprite, ResPath path, int? index = null)
    {
        if (_resourceCache.TryGetResource<TextureResource>(TextureRoot / path, out var texture))
            return AddTextureLayer(sprite, texture.Texture, index);

        if (path.Extension == "rsi")
            Log.Error($"Expected texture but got rsi '{path}', did you mean 'sprite:' instead of 'texture:'?");
        else
            Log.Error($"Unable to load texture '{path}'. Trace:\n{Environment.StackTrace}");

        return AddTextureLayer(sprite, _resourceCache.GetFallback<TextureResource>(), index);
    }

    public int AddTextureLayer(Entity<SpriteComponent?> sprite, Texture texture, int? index = null)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        var layer = new Layer(this, _tree, EntityManager, Log);
        return AddLayer(sprite, layer, index);
    }

    public int AddLayer(Entity<SpriteComponent?> sprite, SpriteSpecifier specifier, int? newIndex = null)
    {
        return specifier switch
        {
            SpriteSpecifier.Texture tex => AddTextureLayer(sprite, tex.TexturePath, newIndex),
            SpriteSpecifier.Rsi rsi => AddRsiLayer(sprite, rsi.RsiState, rsi.RsiPath, newIndex),
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Add a new sprite layer and populate it using the provided layer data.
    /// </summary>
    public int AddLayer(Entity<SpriteComponent?> sprite, BaseLayerData data, int? index = null)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        return AddLayer(sprite.Comp.Sprite, data, index);
    }

    public int AddLayer(LayerCollection parent, BaseLayerData data, int? index = null)
    {
        switch (data)
        {
            case ShaderLayerData shaderData:
                return AddLayer(parent, shaderData, index);
            case RsiLayerData rsiData:
                return AddLayer(parent, rsiData, index);
            case LayerCollectionData collectionData:
                return AddLayer(parent, collectionData, index);
            default:
                throw new NotImplementedException();
        }
    }

    public int AddLayer(LayerCollection parent, ShaderLayerData data, int? index = null)
    {
        var layer = new ShaderLayer(this, _tree, EntityManager, Log);
        index = parent.AddLayer(layer, index);
        SetData(layer, data);
        return index.Value;
    }

    public int AddLayer(LayerCollection parent, RsiLayerData data, int? index = null)
    {
        var layer = new Layer(this, _tree, EntityManager, Log);
        index = parent.AddLayer(layer, index);
        LayerSetData(layer, data);
        return index.Value;
    }

    public int AddLayer(LayerCollection parent, LayerCollectionData data, int? index = null)
    {
        var layer = new LayerCollection(this, _tree, EntityManager, Log);
        index = parent.AddLayer(layer, index);
        SetData(layer, data);
        return index.Value;
    }

    /// <summary>
    /// Add a blank sprite layer. This effectively just exists to reserve a layer index. and to allow layer keys to
    /// be assigned before the layer is actually instantiated.
    /// </summary>
    public int AddBlankLayer(Entity<SpriteComponent?> sprite, out Layer? layer, int? index = null)
    {
        layer = new Layer(this, _tree, EntityManager, Log);
        return AddLayer(sprite, layer, index);
    }

    /// <inheritdoc cref="AddBlankLayer(Entity{SpriteComponent?},out Layer?,int?)"/>
    public int AddBlankLayer(Entity<SpriteComponent?> sprite, int? index = null)
        => AddBlankLayer(sprite, out _, index);

    #endregion

    public int LayerGetDirectionCount(BaseLayer layer) => layer.DirectionCount;
}
