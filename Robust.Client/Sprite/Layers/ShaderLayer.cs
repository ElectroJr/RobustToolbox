using System.Numerics;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Sprite;

namespace Robust.Client.Sprite.Layers;

// This class inherits from Rsi Layer so that RSI, Rsi state, texture, and animations are all handled for us.
// But it doesn't actually need a lot of layer functionality like transforms, colours, shaders, so this is kinda dubious
// inheritance guff. But overall, this involves a lot less "atrocities to god being committed" than before.

/// <summary>
/// This is a shader-configuration layer. It only exists to modify shader parameters for some other layer
/// within the same <see cref="LayerCollection"/>.
/// </summary>
[Access(typeof(SpriteComponent), typeof(SpriteSystem), typeof(BaseLayer))]
public sealed class ShaderLayer : RsiLayer
{
    /// <inheritdoc cref="ShaderLayerData.Key"/>
    public LayerKey Key;

    /// <inheritdoc cref="ShaderLayerData.ParameterTexture"/>
    public string? ParameterTexture;

    /// <inheritdoc cref="ShaderLayerData.ParameterUV"/>
    public string? ParameterUV;

    public override bool Drawn => false;
    protected override Box2 CalculateLocalBounds() => default;
    public override Vector2 GetSize() => default;
    public override Vector2i GetPixelSize() => default;

    internal ShaderLayer(SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log) :
        base(system, tree, entMan, log)
    {
    }

    internal ShaderLayer(ShaderLayer toClone) : base(toClone)
    {
        Key = toClone.Key;
        ParameterTexture = toClone.ParameterTexture;
        ParameterUV = toClone.ParameterUV;
    }

    internal override BaseLayer Clone() => new ShaderLayer(this);
}
