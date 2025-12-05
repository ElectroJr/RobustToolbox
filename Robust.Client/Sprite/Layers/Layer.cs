using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;

namespace Robust.Client.Sprite.Layers;

// This really just exists to differentiate between ShaderLayer & normal layers, and so that normal
// layers can be sealed.

/// <summary>
/// This is an ordinary layer that is used to draw an RSI state or texture.
/// </summary>
[Access(typeof(SpriteComponent), typeof(SpriteSystem), typeof(BaseLayer))]
public sealed class Layer : RsiLayer
{
    /// <summary>
    /// The shader that will be used to draw the texture for this layer.
    /// </summary>
    public ShaderInstance? Shader;

    internal Layer(SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log) : base(system, tree, entMan, log)
    {
    }

    internal Layer(Layer toClone) : base(toClone)
    {
    }

    internal override BaseLayer Clone() => new Layer(this);
}
