using System;
using System.Numerics;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Robust.Client.GameObjects;

// This partial class contains code related to updating a sprites bounding boxes and its position in the sprite tree.
public sealed partial class SpriteSystem
{
    /// <summary>
    /// Gets a sprite's bounding box in world coordinates.
    /// </summary>
    public Box2Rotated CalculateBounds(Entity<SpriteComponent> sprite, Vector2 worldPos, Angle worldRot, Angle eyeRot)
    {
        return CalculateBounds(sprite.Comp.Sprite, worldPos, worldRot, eyeRot);
    }

    public Box2Rotated CalculateBounds(LayerCollection layer, Vector2 worldPos, Angle worldRot, Angle eyeRot)
    {
        // fast check for invisible sprites
        if (!layer.Visible || layer.Layers.Count == 0)
            return new Box2Rotated(new Box2(worldPos, worldPos), Angle.Zero, worldPos);

        // We need to modify world rotation so that it lies between 0 and 2pi.
        // This matters for 4 or 8 directional sprites deciding which quadrant (octant?) they lie in.
        // the 0->2pi convention is set by the sprite-rendering code that selects the layers.
        // See RenderInternal().

        worldRot = worldRot.Reduced();
        if (worldRot.Theta < 0)
            worldRot = new Angle(worldRot.Theta + Math.Tau);

        // Next, what we do is take the box2 and apply the sprite's transform, and then the entity's transform. We
        // could do this via Matrix3.TransformBox, but that only yields bounding boxes. So instead we manually
        // transform our box by the combination of these matrices:

        var finalRotation = layer.Strategy == LayerRenderingStrategy.NoRotation
            ? layer.Rotation - eyeRot
            : layer.Rotation + worldRot;

        var bounds = layer.GetLocalBounds();
        bounds = bounds.Scale(layer.Scale);

        // slightly faster path if offset == 0 (true for 99.9% of sprites)
        if (layer.Offset == Vector2.Zero)
            return new Box2Rotated(bounds.Translated(worldPos), finalRotation, worldPos);

        var adjustedOffset = layer.Strategy == LayerRenderingStrategy.NoRotation
            ? (-eyeRot).RotateVec(layer.Offset)
            : worldRot.RotateVec(layer.Offset);

        var position = adjustedOffset + worldPos;
        return new Box2Rotated(bounds.Translated(position), finalRotation, position);
    }

    public Box2 GetLocalBounds(Entity<SpriteComponent> ent)
    {
        return ent.Comp.Sprite.GetLocalBounds();
    }
}
