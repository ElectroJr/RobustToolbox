using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.Light;

/// <summary>
/// Prototype for a point light mask.
/// </summary>
[Prototype]
public sealed partial class LightMaskPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = string.Empty;

    /// <summary>
    /// UV coordinates for the portion of the light mask atlas that corresponds to this prototype.
    /// </summary>
    [ViewVariables]
    internal Box2 TextureBox;

    /// <summary>
    /// Texture path. Null implies white/default mask.
    /// </summary>
    [DataField]
    public SpriteSpecifier.Texture? Texture;
}
