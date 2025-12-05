using System;
using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Sprite;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects;

// This partial class contains methods for applying yaml layer data.
public sealed partial class SpriteSystem
{
    public void LayerSetData(Entity<SpriteComponent?> sprite, int index, BaseLayerData data)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetData(layer, data);
    }

    public void LayerSetData(Entity<SpriteComponent?> sprite, LayerKey key, BaseLayerData data)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetData(layer, data);
    }

    /// <summary>
    /// Apply the yaml layer data to a layer.
    /// </summary>
    public void LayerSetData(BaseLayer layer, BaseLayerData data)
    {
        switch (data)
        {
            case ShaderLayerData shaderData:
                if (layer is ShaderLayer shaderLayer)
                    SetData(shaderLayer, shaderData);
                else
                    Log.Error("Attempted to apply shader data to a non-shader layer");
                break;

            case LayerData layerData:
                if (layer is Layer layerLayer)
                    SetData(layerLayer, layerData);
                else
                    Log.Error("Attempted to apply layer data to a non-simple layer");
                break;

            case RsiLayerData rsiData:
                if (layer is RsiLayer rsiLayer)
                    SetData(rsiLayer, rsiData);
                else
                    Log.Error("Attempted to rsi layer data to a non rsi layer");
                break;

            case LayerCollectionData collectionData:
                if (layer is LayerCollection layerCollection)
                    SetData(layerCollection, collectionData);
                else
                    Log.Error("Attempted to apply collection data to a non collection layer");
                break;

            default:
                throw new NotImplementedException();
        }
    }

    private void SetData(LayerCollection layer, LayerCollectionData data)
    {
        SetData((BaseLayer)layer, data);

        layer.DrawTogether = data.DrawTogether ?? layer.DrawTogether;
        layer.Granular = data.GranularLayersRendering ?? layer.Granular;

        if (data.Layers != null)
        {
            foreach (var childData in data.Layers)
            {
                AddLayer(layer, childData);
            }
        }

        if (data.RsiPath != null)
            RecursivelyRefreshState(layer);
    }

    private void RecursivelyRefreshState(BaseLayer layer)
    {
        if (layer is RsiLayer rsiLayer)
        {
            LayerSetRsiState(rsiLayer, rsiLayer.StateId, refresh: true);
            return;
        }

        if (layer is not LayerCollection collection)
            return;

        foreach (var child in collection.Layers)
        {
            if (child.RsiOverride == null)
                RecursivelyRefreshState(child);
        }
    }

    private void SetData(ShaderLayer layer, ShaderLayerData data)
    {
        layer.Key = data.Key ??  layer.Key;
        layer.ParameterTexture = data.ParameterTexture ?? layer.ParameterTexture;
        layer.ParameterUV = data.ParameterUV ?? layer.ParameterUV;
        SetData((RsiLayer) layer, data);
    }

    private void SetData(RsiLayer layer, RsiLayerData data)
    {
        layer.AnimationBehaviour = data.AnimationBehaviour ?? layer.AnimationBehaviour;
        layer.AutoAnimated = data.AutoAnimated ?? layer.AutoAnimated;
        layer.StateId = data.State ?? layer.StateId;

        if (!string.IsNullOrWhiteSpace(data.TexturePath))
            LayerSetTexture(layer, new ResPath(data.TexturePath));

        SetData((BaseLayer) layer, data);

        if (data.State != null || data.RsiPath != null)
            LayerSetRsiState(layer, layer.StateId, refresh: true);
    }

    private void SetData(Layer layer, LayerData data)
    {
        SetData((RsiLayer) layer, data);
        LayerSetShader(layer, data.Shader);
    }

    private void SetData(BaseLayer layer, BaseLayerData data)
    {
        layer.Scale = data.Scale ?? layer.Scale;
        layer.Rotation = data.Rotation ?? layer.Rotation;
        layer.Offset = data.Offset ?? layer.Offset;
        layer.Color = data.Color ?? layer.Color;
        layer.Visible = data.Visible ?? layer.Visible;
        layer.DirOverride = data.DirOverride ?? layer.DirOverride; // TODO SPRITE data cannot reset to null.
        layer.DirOffset = data.DirOffset ?? layer.DirOffset;
        layer.DirBehaviour = data.DirBehaviour ?? layer.DirBehaviour;
        layer.NoLighting = data.UnShaded ?? layer.NoLighting;
        layer.Strategy = data.RenderingStrategy ?? layer.Strategy;
        layer.DrawDepth = data.DrawDepth ?? layer.DrawDepth;
        layer.DirectionalDrawDepths = data.DirectionalDrawDepths?.ToArray() ?? layer.DirectionalDrawDepths;

        layer.UpdateTransform();
        layer.InvalidateCache();

        // TODO CLAUDIA shared ShaderPrototype
        var postShaders = data.PostShaders?.Select(x => new ProtoId<ShaderPrototype>(x)).ToArray();
        LayerSetPostShaders(layer, postShaders);

        if (!string.IsNullOrWhiteSpace(data.RsiPath))
        {
            var path = TextureRoot / data.RsiPath;
            if (_resourceCache.TryGetResource(path, out RSIResource? resource))
                layer.RsiOverride = resource.RSI;
            else
                Log.Error($"Unable to load layer RSI '{path}'");
        }

        if (data.MapKeys == null || layer.Owner is not { } collection)
            return;

        var index = collection.Layers.IndexOf(layer);
        foreach (var key in data.MapKeys)
        {
            if (!collection.LayerMap.TryAdd(key, index) && collection.LayerMap[key] != index)
                Log.Error($"Duplicate layer map key definition: {key}");
        }
    }
}
