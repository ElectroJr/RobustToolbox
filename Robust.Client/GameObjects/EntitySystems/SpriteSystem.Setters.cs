using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Robust.Client.GameObjects;

// This partial class contains various public methods for setting sprite component data.
public sealed partial class SpriteSystem
{
    public void SetScale(Entity<SpriteComponent?> sprite, Vector2 value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetScale(sprite.Comp.Sprite, value);
    }

    public void SetRotation(Entity<SpriteComponent?> sprite, Angle value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetRotation(sprite.Comp.Sprite, value);
    }

    public void SetOffset(Entity<SpriteComponent?> sprite, Vector2 value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetOffset(sprite.Comp.Sprite, value);
    }

    public void SetVisible(Entity<SpriteComponent?> sprite, bool value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetVisible(sprite.Comp.Sprite, value);
    }

    public void SetDrawDepth(Entity<SpriteComponent?> sprite, int value)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return;

        sprite.Comp.DrawDepth = value;
    }

    public void SetColor(Entity<SpriteComponent?> sprite, Color value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetColor(sprite.Comp.Sprite, value);
    }

    /// <summary>
    /// Modify a sprites base RSI. This is the RSI that is used by any RSI layers that do not specify their own.
    /// Note that changing the base RSI may result in existing layers having an invalid state. This will not log errors
    /// under the assumption that the states of each layer will be updated after the base RSI has changed.
    /// </summary>
    public void SetBaseRsi(Entity<SpriteComponent?> sprite, RSI? value)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetRsi(sprite.Comp.Sprite, value);
    }

    public void SetContainerOccluded(Entity<SpriteComponent?> sprite, bool value)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp, false))
            return;

        sprite.Comp.ContainerOccluded = value;
        _tree.QueueTreeUpdate(sprite!);
    }

    public void LayerSetShaders(Entity<SpriteComponent?> sprite, params ReadOnlySpan<ProtoId<ShaderPrototype>> shaders)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetShaders(sprite.Comp.Sprite, shaders);
    }

    public void LayerSetShaders(Entity<SpriteComponent?> sprite, params ShaderInstance[] shaders)
    {
        if (_query.Resolve(sprite.Owner, ref sprite.Comp))
            LayerSetShaders(sprite.Comp.Sprite, shaders);
    }
}
