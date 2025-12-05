using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;

namespace Robust.Client.Sprite.Layers;

/// <summary>
/// This is a layer collection that is associated with an entity's sprite component.
/// </summary>
[Access(typeof(SpriteComponent), typeof(SpriteSystem), typeof(BaseLayer))]
public sealed class SpriteLayer : LayerCollection
{
    private Entity<SpriteComponent> _entity;
    public override Entity<SpriteComponent>? GetEntity() => _entity;
    internal override void InvalidateCache()
    {
        base.InvalidateCache();
        if (!_entity.Owner.IsValid())
            return; // Entity prototype sprite

        System.QueueUpdateIsInert(_entity);
        Tree.QueueTreeUpdate(_entity);
    }

    internal SpriteLayer(Entity<SpriteComponent> entity, SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log) :
        base(system, tree, entMan, log)
    {
        _entity = entity;
    }

    internal SpriteLayer(SpriteLayer toClone) : base(toClone)
    {
        _entity = toClone._entity;
    }

    internal override BaseLayer Clone() => new SpriteLayer(this);

    internal SpriteLayer Clone(Entity<SpriteComponent> newEntity)
    {
        var clone = (SpriteLayer)Clone();
        clone._entity = newEntity;
        return clone;
    }
}
