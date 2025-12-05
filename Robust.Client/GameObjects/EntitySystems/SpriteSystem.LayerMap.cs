using System;
using System.Diagnostics.CodeAnalysis;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Sprite;

namespace Robust.Client.GameObjects;

// This partial class contains various public methods for manipulating layer mappings.
public sealed partial class SpriteSystem
{
    /// <summary>
    /// Map a layer key to a layer index.
    /// </summary>
    public void LayerMapSet(Entity<SpriteComponent?> sprite, LayerKey key, int index)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return;

        if (index < 0 || index >= sprite.Comp.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        sprite.Comp.Sprite.LayerMap[key] = index;
    }

    /// <summary>
    /// Map a layer key to a layer index.
    /// </summary>
    public void LayerMapAdd(Entity<SpriteComponent?> sprite, LayerKey key, int index)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return;

        if (index < 0 || index >= sprite.Comp.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        sprite.Comp.Sprite.LayerMap.Add(key, index);
    }

    /// <summary>
    /// Remove a layer key mapping.
    /// </summary>
    public bool LayerMapRemove(Entity<SpriteComponent?> sprite, LayerKey key)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return false;

        return sprite.Comp.Sprite.LayerMap.Remove(key);
    }

    /// <summary>
    /// Remove a layer key mapping.
    /// </summary>
    public bool LayerMapRemove(Entity<SpriteComponent?> sprite, LayerKey key, out int index)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            return sprite.Comp.Sprite.LayerMap.Remove(key, out index);

        index = 0;
        return false;
    }

    /// <summary>
    /// Attempt to resolve a layer key mapping.
    /// </summary>
    [Obsolete("Use LayerMapResolve or the override without a bool argument")]
    public bool LayerMapTryGet(Entity<SpriteComponent?> sprite, LayerKey key, out int index, bool logMissing)
    {
        return logMissing
            ? LayerMapResolve(sprite, key, out index)
            : LayerMapTryGet(sprite, key, out index);
    }

    /// <summary>
    /// Attempt to get the layer index corresponding to the given key.
    /// </summary>
    public bool LayerMapTryGet(Entity<SpriteComponent?> sprite, LayerKey key, out int index)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            return sprite.Comp.Sprite.LayerMap.TryGetValue(key, out index);

        index = 0;
        return false;
    }

    /// <summary>
    /// Attempt to resolve the layer index corresponding to the given key. This will log an error if there is no layer
    /// with the given key
    /// </summary>
    public bool LayerMapResolve(Entity<SpriteComponent?> sprite, LayerKey key, out int index)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            return sprite.Comp.Sprite.ResolveKey(key, out index);

        index = 0;
        return false;

    }

    /// <inheritdoc cref="TryGetLayer(Entity{SpriteComponent?},LayerKey, out BaseLayer?)"/>
    [Obsolete("Use ResolveLayer or the override without a bool argument")]
    public bool TryGetLayer(Entity<SpriteComponent?> sprite, LayerKey key, [NotNullWhen(true)] out BaseLayer? layer, bool logMissing)
    {
        layer = null;
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp, logMissing))
            return false;

        return logMissing
            ? sprite.Comp.Sprite.ResolveLayer(key, out layer)
            : sprite.Comp.Sprite.TryGetLayer(key, out layer);
    }

    /// <summary>
    /// Attempt to get the layer corresponding to the given key.
    /// </summary>
    public bool TryGetLayer(Entity<SpriteComponent?> sprite, LayerKey key, [NotNullWhen(true)] out BaseLayer? layer)
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.TryGetLayer(key, out layer);
    }

    /// <inheritdoc cref="TryGetLayer(Entity{SpriteComponent?},LayerKey, out BaseLayer?)"/>
    public bool TryGetLayer<T>(Entity<SpriteComponent?> sprite, LayerKey key, [NotNullWhen(true)] out T? layer)
        where T : BaseLayer
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.TryGetLayer(key, out layer);
    }

    /// <summary>
    /// Attempt to resolve the layer corresponding to the given key. This will log an error if there is no layer
    /// with the given key.
    /// </summary>
    public bool ResolveLayer(Entity<SpriteComponent?> sprite, LayerKey key, [NotNullWhen(true)] out BaseLayer? layer)
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.ResolveLayer(key, out layer);
    }

    /// <inheritdoc cref="ResolveLayer(Entity{SpriteComponent?},LayerKey, out BaseLayer?)"/>
    public bool ResolveLayer<T>(Entity<SpriteComponent?> sprite, LayerKey key, [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        return _query.Resolve(sprite.Owner, ref sprite.Comp) && sprite.Comp.Sprite.ResolveLayer(key, out layer);
    }

    public int LayerMapGet(Entity<SpriteComponent?> sprite, LayerKey key)
    {
        return !_query.Resolve(sprite.Owner, ref sprite.Comp) ? -1 : sprite.Comp.LayerMap[key];
    }

    public bool LayerExists(Entity<SpriteComponent?> sprite, LayerKey key)
    {
        return _query.Resolve(sprite.Owner, ref sprite.Comp)
               && sprite.Comp.LayerMap.TryGetValue(key, out var index)
               && LayerExists(sprite, index);
    }

    /// <summary>
    /// Ensures that a layer with the given key exists and return the layer's index.
    /// If the layer does not yet exist, this will create and add a blank layer.
    /// </summary>
    public int LayerMapReserve(Entity<SpriteComponent?> sprite, LayerKey key, out BaseLayer? layer)
    {
        layer = null;
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return -1;

        if (LayerMapTryGet(sprite, key, out var layerIndex))
        {
            layer = sprite.Comp.Sprite.Layers[layerIndex];
            return layerIndex;
        }

        var index = AddBlankLayer(sprite, out var newLayer);
        layer = newLayer;
        LayerMapSet(sprite, key, index);
        return index;
    }

    /// <inheritdoc cref="LayerMapReserve(Entity{SpriteComponent?},LayerKey,out BaseLayer?)"/>
    public int LayerMapReserve(SpriteComponent comp, LayerKey key, out BaseLayer layer)
    {
        // This non Entity<T> variant mainly exists so that there is an override where the out layer is not nullable.

        if (comp.Sprite.LayerMap.TryGetValue(key, out var index))
        {
            layer = comp.Layers[index];
            return index;
        }

        layer = new Layer(this, _tree, EntityManager, Log);
        index = comp.Sprite.AddLayer(layer, index);
        comp.Sprite.LayerMap[key] = index;
        return index;
    }

    /// <inheritdoc cref="LayerMapReserve(Entity{SpriteComponent?},LayerKey,out BaseLayer?)"/>
    public int LayerMapReserve(Entity<SpriteComponent?> sprite, LayerKey key) => LayerMapReserve(sprite, key, out _);

    public bool RemoveLayer(Entity<SpriteComponent?> sprite, LayerKey key, bool logMissing = true)
        => RemoveLayer(sprite, key, out _, logMissing);

    public bool RemoveLayer(
        Entity<SpriteComponent?> sprite,
        LayerKey key,
        [NotNullWhen(true)] out BaseLayer? layer,
        bool logMissing = true)
    {
        layer = null;
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp, logMissing))
            return false;

        int index;
        if (logMissing)
        {
            if (!LayerMapResolve(sprite, key, out index))
                return false;
        }
        else
        {
            if (!LayerMapTryGet(sprite, key, out index))
                return false;
        }

        return RemoveLayer(sprite, index, out layer, logMissing);
    }
}
