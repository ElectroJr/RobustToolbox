using Robust.Shared.ComponentTrees;
using Robust.Shared.GameObjects;
using Robust.Shared.Light;
using Robust.Shared.Physics;
using Robust.Shared.ViewVariables;

namespace Robust.Client.GameObjects;

[RegisterComponent]
public sealed partial class PointLightComponent : SharedPointLightComponent, IComponentTreeEntry<PointLightComponent>
{
    #region Component Tree

    /// <inheritdoc />
    [ViewVariables]
    public EntityUid? TreeUid { get; set; }

    /// <inheritdoc />
    [ViewVariables]
    public DynamicTree<ComponentTreeEntry<PointLightComponent>>? Tree { get; set; }

    /// <inheritdoc />
    [ViewVariables]
    public bool AddToTree => Enabled && !ContainerOccluded;

    /// <inheritdoc />
    [ViewVariables]
    public bool TreeUpdateQueued { get; set; }

    [ViewVariables]
    public LightMaskPrototype MaskPrototype = default!;

    #endregion
}
