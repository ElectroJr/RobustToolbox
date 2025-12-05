using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;

namespace Robust.Client.GameObjects;

public sealed partial class SpriteSystem
{
    private void OnPreInit(Entity<SpriteComponent> ent, ref ComponentPreInitEvent args)
    {
        InitializeSprite(ent);
    }

    private void InitializeSprite(Entity<SpriteComponent> ent)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ent.Comp.SpriteInternal != null)
            return;

        ent.Comp.Sys = this;
        ent.Comp.SpriteInternal = new(ent, this, _tree, EntityManager, Log);
        ref var data = ref ent.Comp.InitData;

        if (ent.Comp.NoRot == true)
            data.RenderingStrategy = LayerRenderingStrategy.NoRotation;
        else if (ent.Comp.SnapCardinalsInternal == true)
            data.RenderingStrategy = LayerRenderingStrategy.SnapToCardinals;
        else
            data.RenderingStrategy =  LayerRenderingStrategy.Default;

        // Insert default layer
        if (data.Layers == null && (ent.Comp.State != null || ent.Comp.Texture != null))
        {
            data.Layers =
            [
                new RsiLayerData
                {
                    TexturePath = ent.Comp.Texture,
                    State = ent.Comp.State,
                }
            ];
        }

        LayerSetData(ent.Comp.Sprite, data);
    }

    /// <summary>
    /// Resets the sprite's animated layers to align with a given time (in seconds).
    /// </summary>
    public void SetAutoAnimateSync(SpriteComponent sprite, double time)
    {
        foreach (var baseLayer in sprite.Layers)
        {
            if (baseLayer is not RsiLayer {Animated: true} layer)
                continue;

            layer.AnimationTimeLeft = (float) -(time % layer.State!.TotalDelay);
            layer.AnimationFrame = 0;
        }
    }

    public void CopySprite(Entity<SpriteComponent?> source, Entity<SpriteComponent?> target)
    {
        if (!Resolve(source.Owner, ref source.Comp))
            return;

        if (!Resolve(target.Owner, ref target.Comp))
            return;

        CopySprite(source.Comp, target!);
    }

    public void CopySprite(SpriteComponent source, Entity<SpriteComponent> target)
    {
        target.Comp.SpriteInternal = source.Sprite.Clone(target);
        target.Comp.DrawDepth = source.DrawDepth;
        target.Comp.IsInert = source.IsInert;
        target.Comp.RenderOrder = source.RenderOrder;
    }

    /// <summary>
    /// Adds a sprite to a queue that will update <see cref="SpriteComponent.IsInert"/> next frame.
    /// </summary>
    public void QueueUpdateIsInert(Entity<SpriteComponent> sprite)
    {
        if (sprite.Comp.InertUpdateQueued)
            return;

        sprite.Comp.InertUpdateQueued = true;
        _animationUpdateQueue.Enqueue(sprite);
    }

    /// <summary>
    /// Adds a sprite to a queue that will update <see cref="SpriteComponent.IsInert"/> next frame.
    /// </summary>
    internal void QueueFrameUpdate(EntityUid uid, SpriteComponent sprite)
    {
        if (!sprite.IsInert && !sprite.UpdateQueued)
        {
            sprite.UpdateQueued = true;
            _queuedFrameUpdate.Add(uid);
        }
    }
}
