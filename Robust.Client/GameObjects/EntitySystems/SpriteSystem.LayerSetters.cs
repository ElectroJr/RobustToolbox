using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Sprite;
using Robust.Shared.Utility;
using static Robust.Client.Graphics.RSI;

namespace Robust.Client.GameObjects;

// This partial class contains various public methods for modifying a layer's properties.
public sealed partial class SpriteSystem
{
    #region SpriteSpecifier

    public void LayerSetSprite(Entity<SpriteComponent?> sprite, int index, SpriteSpecifier specifier)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetSprite(layer, specifier);
    }

    public void LayerSetSprite(Entity<SpriteComponent?> sprite, LayerKey key, SpriteSpecifier specifier)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetSprite(layer, specifier);
    }

    public void LayerSetSprite(RsiLayer rsiLayer, SpriteSpecifier specifier)
    {
        switch (specifier)
        {
            case SpriteSpecifier.Texture tex:
                LayerSetTexture(rsiLayer, tex.TexturePath);
                break;

            case SpriteSpecifier.Rsi rsi:
                LayerSetRsi(rsiLayer, rsi.RsiPath, rsi.RsiState);
                break;

            default:
                throw new NotImplementedException();
        }
    }

    #endregion

    #region Texture

    public void LayerSetTexture(Entity<SpriteComponent?> sprite, int index, Texture? texture)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetTexture(layer, texture);
    }

    public void LayerSetTexture(Entity<SpriteComponent?> sprite, LayerKey key, Texture? texture)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetTexture(layer, texture);
    }

    public void LayerSetTexture(RsiLayer rsiLayer, Texture? texture)
    {
        rsiLayer.Texture = texture;
        rsiLayer.InvalidateCache();
    }

    public void LayerSetTexture(Entity<SpriteComponent?> sprite, int index, ResPath path)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetTexture(layer, path);
    }

    public void LayerSetTexture(Entity<SpriteComponent?> sprite, LayerKey key, ResPath path)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetTexture(layer, path);
    }

    public void LayerSetTexture(RsiLayer rsiLayer, ResPath path)
    {
        if (_resourceCache.TryGetResource<TextureResource>(TextureRoot / path, out var texture))
        {
            LayerSetTexture(rsiLayer, texture);
            return;
        }

        if (path.Extension == "rsi")
            Log.Error($"Expected texture but got rsi '{path}', did you mean 'sprite:' instead of 'texture:'?");
        Log.Error($"Unable to load texture '{path}'. Trace:\n{Environment.StackTrace}");
        LayerSetTexture(rsiLayer, _resourceCache.GetFallback<TextureResource>());
    }

    #endregion

    #region RsiState

    public void LayerSetRsiState(Entity<SpriteComponent?> sprite, int index, StateId state, bool refresh = false)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetRsiState(layer, state, refresh);
    }

    public void LayerSetRsiState(Entity<SpriteComponent?> sprite, LayerKey key, StateId state, bool refresh = false)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetRsiState(layer, state, refresh);
    }

    [Obsolete("Use specific layer subtype")]
    public void LayerSetRsiState(BaseLayer layer, StateId id, bool refresh = false, bool log = true)
    {
        LayerSetRsiState((RsiLayer) layer, id, refresh, log);
    }

    public void LayerSetRsiState(RsiLayer rsiLayer, StateId id, bool refresh = false, bool log = true)
    {
        if (rsiLayer.StateId == id && !refresh)
            return;

        rsiLayer.StateId = id;
        rsiLayer.State = null;
        rsiLayer.AnimationFrame = 0;
        rsiLayer.AnimationTime = 0;
        rsiLayer.AnimationTimeLeft = 0;

        if (!id.IsValid || rsiLayer.GetRsi() is not { } rsi)
        {
            rsiLayer.InvalidateCache();
            return;
        }

        if (!rsi.TryGetState(id, out var state))
        {
            state = GetFallbackState();
            if (log)
                Log.Error($"{ToPrettyString(rsiLayer.GetEntity())} attempted to set unknown RSI state {id}. Trace:\n{Environment.StackTrace}");
        }

        rsiLayer.State = state;
        rsiLayer.AnimationTimeLeft = state.GetDelay(0);
        rsiLayer.InvalidateCache();
    }

    #endregion

    #region Rsi

    public void LayerSetRsi(Entity<SpriteComponent?> sprite, int index, RSI? rsi, StateId? state = null)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetRsi(layer, rsi, state);
    }

    public void LayerSetRsi(Entity<SpriteComponent?> sprite, LayerKey key, RSI? rsi, StateId? state = null)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetRsi(layer, rsi, state);
    }

    public void LayerSetRsi(BaseLayer layer, RSI? rsi, StateId? state = null)
    {
        layer.RsiOverride = rsi;
        RecursivelyRefreshState(layer);
    }

    public void LayerSetRsi(Entity<SpriteComponent?> sprite, int index, ResPath rsi, StateId? state = null)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetRsi(layer, rsi, state);
    }

    public void LayerSetRsi(Entity<SpriteComponent?> sprite, LayerKey key, ResPath rsi, StateId? state = null)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetRsi(layer, rsi, state);
    }

    public void LayerSetRsi(BaseLayer layer, ResPath rsi, StateId? state = null)
    {
        if (_resourceCache.TryGetResource<RSIResource>(TextureRoot / rsi, out var res))
        {
            LayerSetRsi(layer, res.RSI, state);
            return;
        }

        if (layer is RsiLayer rsiLayer)
        {
            Log.Error($"Unable to load RSI '{rsi}' for entity {ToPrettyString(layer.GetEntity())}. Trace:\n{Environment.StackTrace}");
            rsiLayer.State = GetFallbackState();
            rsiLayer.AnimationFrame = 0;
            rsiLayer.AnimationTime = 0;
            rsiLayer.AnimationTimeLeft = rsiLayer.State.GetDelay(0);
            rsiLayer.InvalidateCache();
        }
        else
        {
            // Layer collection needs to refresh all children
            // TODO SPRITE
            throw new NotImplementedException();
        }
    }

    #endregion

    #region Scale

    public void LayerSetScale(Entity<SpriteComponent?> sprite, int index, Vector2 value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetScale(layer, value);
    }

    public void LayerSetScale(Entity<SpriteComponent?> sprite, LayerKey key, Vector2 value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetScale(layer, value);
    }

    public void LayerSetScale(BaseLayer? layer, Vector2 value)
    {
        if (layer == null)
            return;

        if (layer.Scale.EqualsApprox(value))
            return;

        if (MathF.Abs(value.X) < MinScale
            || MathF.Abs(value.Y) < MinScale
            || float.IsNaN(value.X)
            || float.IsNaN(value.Y))
        {
            Log.Error($"Attempted to set scale to invalid value. Entity: {ToPrettyString(layer.GetEntity())}. Value: {value}");
            return;
        }

        layer.Scale = value;
        layer.UpdateTransform();
        layer.InvalidateCache();
    }

    #endregion

    #region Rotation

    public void LayerSetRotation(Entity<SpriteComponent?> sprite, int index, Angle value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetRotation(layer, value);
    }

    public void LayerSetRotation(Entity<SpriteComponent?> sprite, LayerKey key, Angle value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetRotation(layer, value);
    }

    public void LayerSetRotation(BaseLayer layer, Angle value)
    {
        if (layer.Rotation.EqualsApprox(value))
            return;

        if (double.IsNaN(value.Theta))
        {
            Log.Error($"Attempted to set scale to invalid angle. Entity: {ToPrettyString(layer.GetEntity())}. Value: {value}");
            return;
        }

        layer.Rotation = value;
        layer.UpdateTransform();
        layer.InvalidateCache();
    }

    #endregion

    #region Offset

    public void LayerSetOffset(Entity<SpriteComponent?> sprite, int index, Vector2 value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetOffset(layer, value);
    }

    public void LayerSetOffset(Entity<SpriteComponent?> sprite, LayerKey key, Vector2 value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetOffset(layer, value);
    }

    public void LayerSetOffset(BaseLayer layer, Vector2 value)
    {
        if (layer.Offset.EqualsApprox(value))
            return;

        if (float.IsNaN(value.X) || float.IsNaN(value.Y))
        {
            Log.Error($"Attempted to set offset to invalid value. Entity: {ToPrettyString(layer.GetEntity())}. Value: {value}");
            return;
        }

        layer.Offset = value;
        layer.UpdateTransform();
        layer.InvalidateCache();
    }

    #endregion

    #region Visible

    public void LayerSetVisible(Entity<SpriteComponent?> sprite, int index, bool value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetVisible(layer, value);
    }

    public void LayerSetVisible(Entity<SpriteComponent?> sprite, LayerKey key, bool value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetVisible(layer, value);
    }

    public void LayerSetVisible(BaseLayer layer, bool value)
    {
        if (layer.Visible == value)
            return;

        layer.Visible = value;
        layer.InvalidateCache();
    }

    #endregion

    #region Color

    public void LayerSetColor(Entity<SpriteComponent?> sprite, int index, Color value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetColor(layer, value);
    }

    public void LayerSetColor(Entity<SpriteComponent?> sprite, LayerKey key, Color value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetColor(layer, value);
    }

    public void LayerSetColor(BaseLayer layer, Color value)
    {
        layer.Color = value;
    }

    #endregion

    #region Direction

    public void LayerSetDirOffset(Entity<SpriteComponent?> sprite, int index, DirectionOffset value)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetDirOffset(layer, value);
    }

    public void LayerSetDirOffset(Entity<SpriteComponent?> sprite, LayerKey key, DirectionOffset value)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetDirOffset(layer, value);
    }

    public void LayerSetDirOffset(RsiLayer rsiLayer, DirectionOffset value)
    {
        rsiLayer.DirOffset = value;
    }

    public void LayerSetDirOffset(Entity<SpriteComponent?> sprite, int index, DirectionBehaviour value)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetDirOffset(layer, value);
    }

    public void LayerSetDirOffset(Entity<SpriteComponent?> sprite, LayerKey key, DirectionBehaviour value)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetDirOffset(layer, value);
    }

    public void LayerSetDirOffset(RsiLayer rsiLayer, DirectionBehaviour value)
    {
        rsiLayer.DirBehaviour = value;
    }
    #endregion

    #region AnimationTime

    public void LayerSetAnimationTime(Entity<SpriteComponent?> sprite, int index, float value)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetAnimationTime(layer, value);
    }

    public void LayerSetAnimationTime(Entity<SpriteComponent?> sprite, LayerKey key, float value)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetAnimationTime(layer, value);
    }

    public void LayerSetAnimationTime(RsiLayer rsiLayer, float value)
    {
        if (rsiLayer.State == null)
            return;

        if (value > rsiLayer.AnimationTime)
        {
            // Handle advancing differently from going backwards.
            rsiLayer.AnimationTimeLeft -= (value - rsiLayer.AnimationTime);
        }
        else
        {
            // Going backwards we re-calculate from zero.
            // Definitely possible to optimize this for going backwards but I'm too lazy to figure that out.
            rsiLayer.AnimationTimeLeft = -value + rsiLayer.State.GetDelay(0);
            rsiLayer.AnimationFrame = 0;
        }

        rsiLayer.AnimationTime = value;
        rsiLayer.AdvanceFrameAnimation();
    }

    #endregion

    #region Animation

    public void LayerSetAnimationBehaviour(Entity<SpriteComponent?> sprite, int index, AnimationBehaviour value)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetAnimationBehaviour(layer, value);
    }

    public void LayerSetAnimationBehaviour(Entity<SpriteComponent?> sprite, LayerKey key, AnimationBehaviour value)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetAnimationBehaviour(layer, value);
    }

    public void LayerSetAnimationBehaviour(RsiLayer rsiLayer, AnimationBehaviour value)
    {
        if (rsiLayer.AnimationBehaviour == value)
            return;

        rsiLayer.AnimationBehaviour = value;
        rsiLayer.InvalidateCache();
    }

    public void LayerSetAutoAnimated(Entity<SpriteComponent?> sprite, int index, bool value)
    {
        if (ResolveLayer(sprite, index, out RsiLayer? layer))
            LayerSetAutoAnimated(layer, value);
    }

    public void LayerSetAutoAnimated(Entity<SpriteComponent?> sprite, LayerKey key, bool value)
    {
        if (ResolveLayer(sprite, key, out RsiLayer? layer))
            LayerSetAutoAnimated(layer, value);
    }

    public void LayerSetAutoAnimated(RsiLayer rsiLayer, bool value)
    {
        if (rsiLayer.AutoAnimated == value)
            return;

        rsiLayer.AutoAnimated = value;
        rsiLayer.InvalidateCache();
    }

    #endregion

    #region LayerSetRenderingStrategy

    public void LayerSetRenderingStrategy(Entity<SpriteComponent?> sprite, int index, LayerRenderingStrategy value)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetRenderingStrategy(layer, value);
    }

    public void LayerSetRenderingStrategy(Entity<SpriteComponent?> sprite, LayerKey key, LayerRenderingStrategy value)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetRenderingStrategy(layer, value);
    }

    public void LayerSetRenderingStrategy(BaseLayer layer, LayerRenderingStrategy value)
    {
        layer.Strategy = value;
        layer.InvalidateCache();
    }

    #endregion

    #region Shader

    public void LayerSetShader(Entity<SpriteComponent?> sprite, int index, ShaderInstance? shader)
    {
        if (ResolveLayer(sprite, index, out Layer? layer))
            LayerSetShader(layer, shader);
    }

    public void LayerSetShader(Entity<SpriteComponent?> sprite, LayerKey key, ShaderInstance? shader)
    {
        if (ResolveLayer(sprite, key, out Layer? layer))
            LayerSetShader(layer, shader);
    }

    public void LayerSetShader(Layer layer, ShaderInstance? shader)
    {
        layer.Shader = shader;
    }

    public void LayerSetShader(Entity<SpriteComponent?> sprite, int index, ProtoId<ShaderPrototype>? shader)
    {
        if (ResolveLayer(sprite, index, out Layer? layer))
            LayerSetShader(layer, shader);
    }

    public void LayerSetShader(Entity<SpriteComponent?> sprite, LayerKey key, ProtoId<ShaderPrototype>? shader)
    {
        if (ResolveLayer(sprite, key, out Layer? layer))
            LayerSetShader(layer, shader);
    }

    public void LayerSetShader(Layer layer, ProtoId<ShaderPrototype>? shader)
    {
        // This is here for backwards compatibility, layers can just directly set the data field now
        if (shader == UnshadedId.Id)
        {
            layer.NoLighting = true;
            layer.PostShaders = null;
            return;
        }

        _proto.Resolve(shader, out var prototype);
        LayerSetShader(layer, prototype?.Instance());
    }

    #endregion

    #region PostShaders

    public void LayerSetPostShaders(Entity<SpriteComponent?> sprite, int index, params ShaderInstance[]? shaders)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetPostShaders(layer, shaders);
    }

    public void LayerSetPostShaders(Entity<SpriteComponent?> sprite, LayerKey key, params ShaderInstance[]? shaders)
    {
        if (ResolveLayer(sprite, key, out var layer))
            LayerSetPostShaders(layer, shaders);
    }

    public void LayerSetPostShaders(BaseLayer layer, params ShaderInstance[]? shaders)
    {
        layer.PostShaders = shaders;
    }

    public void LayerSetPostShaders(Entity<SpriteComponent?> sprite, int index, params ReadOnlySpan<ProtoId<ShaderPrototype>> shaders)
    {
        if (ResolveLayer(sprite, index, out BaseLayer? layer))
            LayerSetPostShaders(layer, shaders);
    }

    public void LayerSetPostShaders(Entity<SpriteComponent?> sprite, LayerKey key, params ReadOnlySpan<ProtoId<ShaderPrototype>> shaders)
    {
        if (ResolveLayer(sprite, key, out BaseLayer? layer))
            LayerSetPostShaders(layer, shaders);
    }

    public void LayerSetPostShaders(BaseLayer layer, params ReadOnlySpan<ProtoId<ShaderPrototype>> shaders)
    {
        if (shaders.Length == 0)
        {
            layer.PostShaders = null;
            return;
        }

        Array.Resize(ref layer.PostShaders, shaders.Length);
        for (var i = 0; i < shaders.Length; i++)
        {
            var id = shaders[i];
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            if (layer.PostShaders[i]?.Prototype == id)
                continue;

            if (_proto.Resolve(id, out ShaderPrototype? prototype))
                layer.PostShaders[i] = prototype.Instance();
        }
    }

    #endregion
}
