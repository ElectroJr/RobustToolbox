using Robust.Shared.GameObjects;

namespace Robust.Shared.EntitySerialization.Components;

/// <summary>
/// Added to Maps that were loaded by MapLoaderSystem. If not present then this map was created externally.
/// </summary>
[RegisterComponent, NonSerializedComponent]
public sealed partial class LoadedMapComponent : Component
{
}
