using System;

namespace Robust.Shared.EntitySerialization;

/// <summary>
/// This attribute will cause a component to be ignored while serializing an entity to yaml.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NonSerializedComponentAttribute : Attribute;
