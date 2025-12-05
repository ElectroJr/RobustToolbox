using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Sprite;

namespace Robust.Shared.GameObjects;

/// <summary>
/// This data-definition contains data used to populate a client-side sprite layer.
/// </summary>
/// <remarks>
/// This class is separate from the actual layer class both because it needs to be in shared, and because all the
/// fields are nullable, such that the class can be used to easily update a subset of a layer's fields.
/// </remarks>
[Serializable, NetSerializable, ImplicitDataDefinitionForInheritors]
public abstract partial class BaseLayerData
{
    [DataField] public Vector2? Scale;
    [DataField] public Angle? Rotation;
    [DataField] public Vector2? Offset;
    [DataField] public Color? Color;
    [DataField] public bool? Visible;

    /// <summary>
    /// This can be used to override the RSI direction that is being used to draw this layer and any of its children.
    /// The direction is usually obtained from an entity's rotation relative to the camera's eye.
    /// </summary>
    /// <remarks>
    /// If this is a layer collection, the override also apply to all child layers, unless they specify their own
    /// override or <see cref="DirOffset"/>.
    /// </remarks>
    [DataField] public RsiDirection? DirOverride;

    /// <summary>
    /// This can be used to apply an offset to the Rsi direction that will be used to render a layer.
    /// </summary>
    /// <remarks>
    /// This offset will be applied after <see cref="DirOverride"/>.
    /// </remarks>
    /// <remarks>
    /// If this is a layer collection, the offset will also apply to all child layers.
    /// </remarks>
    public DirectionOffset? DirOffset;

    /// <inheritdoc cref="DirectionBehaviour"/>
    public DirectionBehaviour? DirBehaviour;

    /// <summary>
    /// If true, the layer and any of its children will not have the normal lighting shader applied.
    /// </summary>
    [DataField] public bool? UnShaded;

    /// <summary>
    /// The layer's rendering strategy. For more information, see <see cref="LayerRenderingStrategy"/>.
    /// if not specified, it will default to the parent's strategy or <see cref="LayerRenderingStrategy.Default"/>.
    /// </summary>
    /// <remarks>
    /// This only has an effect if the owning layer collection has <see cref="LayerCollectionData.GranularLayersRendering"/> enabled.
    /// </remarks>
    [DataField] public LayerRenderingStrategy? RenderingStrategy;

    /// <summary>
    /// The layer's draw depth. This can be used to sort layers within a layer collection.
    /// </summary>
    /// <remarks>
    /// If this has a value, any collection that this layer is added to automatically becomes a sorted collection.
    /// </remarks>
    [DataField] public int? DrawDepth;

    /// <summary>
    /// The layer's direction-dependent draw depth. This allows one layers to be drawn at various depths depending on
    /// the <see cref="RsiDirection"/>.
    /// </summary>
    [DataField] public int[]? DirectionalDrawDepths;

    /// <summary>
    /// The path to the RSI that will be used by this layer. If this is a layer collection, this is the fallback
    /// RSI that will be used by this layer's children.
    /// </summary>
    [DataField("sprite")] public string? RsiPath;

    /// <summary>
    /// List of sprite layer keys that should be mapped to this layer.
    /// </summary>
    [DataField("map")] public HashSet<LayerKey>? MapKeys;

    /// <summary>
    /// List of post-processing shaders.
    /// </summary>
    [DataField] public string[]? PostShaders;
    // TODO CLAUDIA move shader prototype to shared?
    // I want to use ProtoId<ShaderPrototype> for yaml validation
}

/// <inheritdoc cref="BaseLayerData"/>
[Serializable, NetSerializable, Virtual]
public partial class RsiLayerData : BaseLayerData
{
    [DataField("texture")] public string? TexturePath;
    [DataField] public string? State;

    /// <summary>
    /// Control how an animated RSI behaves when the animation has finished.
    /// </summary>
    [DataField] public AnimationBehaviour? AnimationBehaviour;

    /// <summary>
    /// Whether the sprite system should automatically animate an animated RSI state.
    /// </summary>
    [DataField] public bool? AutoAnimated;
}

[Serializable, NetSerializable]
public sealed partial class LayerData : RsiLayerData
{
    [DataField] public string? Shader;
}

[Obsolete("Use LayerData")]
[Serializable, NetSerializable]
public sealed partial class PrototypeLayerData : RsiLayerData
{
    // Look I just wana rename the class for consistency, but it requires a lot of content changes, so its just obsoleted for now.
}

/// <inheritdoc cref="BaseLayerData"/>
[Serializable, NetSerializable]
public sealed partial class LayerCollectionData : BaseLayerData
{
    [DataField] public List<BaseLayerData>? Layers;

    /// <summary>
    /// If true, all the layers that belong to this collection will first be drawn to a separate render target
    /// before being drawn all at once as if this was a single layer.
    /// </summary>
    /// <remarks>
    /// This is always implicitly true if the collection uses any shaders.
    /// </remarks>
    /// <remarks>
    /// This is mainly useful if you want to apply the <see cref="Color"/> modifier to the combined layers, as opposed
    /// to modulating each layer individually. E.g., if you wanted to draw multiple overlapping layers and have a final
    /// result with 50% transparency, you would need to enable this option.
    /// </remarks>
    [DataField] public bool? DrawTogether;

    /// <summary>
    /// Whether layer can specify their own <see cref="LayerRenderingStrategy"/>.
    /// </summary>
    [DataField] public bool? GranularLayersRendering;
}

/// <inheritdoc cref="BaseLayerData"/>
[Serializable, NetSerializable]
public sealed partial class ShaderLayerData : RsiLayerData
{
    /// <summary>
    /// The map key of the layer that will have its shader modified.
    /// </summary>
    [DataField] public LayerKey? Key;

    /// <summary>
    /// The name of the shader parameter that will receive the actual selected texture.
    /// </summary>
    [DataField] public string? ParameterTexture;

    /// <summary>
    /// The name of the shader parameter that will receive UVs to select the sprite in <see cref="ParameterTexture"/>.
    /// </summary>
    [DataField] public string? ParameterUV;
}

[Serializable, NetSerializable]
public enum LayerRenderingStrategy
{
    Default,

    /// <summary>
    /// This strategy exists to allow 1-directional layers to mimic a 4-directional RSI state, causing that layer to
    /// look the same when viewed from any cardinal direction. This just works by effectively shifting the layer's
    /// rotation based on the angle from which the entity is being viewed.
    /// </summary>
    SnapToCardinals,

    /// <summary>
    /// This strategy will cause the layer to be rendered as if it's entity's world rotation relative to the screen's
    /// eye is 0.
    /// </summary>
    /// <remarks>
    /// Note that for 4- or 8- directional sprites, the relative rotation is still used to choose the RSI
    /// state.
    /// </remarks>
    /// <remarks>
    /// As an example, this is used to ensure that players/mobs are always standing upright, though their sprite
    /// will still change based on the direction that a mob is looking in.
    /// </remarks>
    NoRotation
}

[Serializable, NetSerializable]
public enum AnimationBehaviour : byte
{
    /// <summary>
    /// When an RSI animation finishes, it will start looping from the beginning.
    /// </summary>
    Loop,

    /// <summary>
    /// When an RSI animation finishes, it will reverse and play backwards.
    /// </summary>
    Cycle,

    /// <summary>
    /// Stops an RSI animation once it finishes.
    /// </summary>
    Once,
}
