using System;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Robust.Shared.Serialization.TypeSerializers.Implementations;

/// <summary>
/// This type serializer exists so that when reading an abstract <see cref="BaseLayerData"/>, it defaults to reading it
/// as a <see cref="RsiLayerData"/> without having to specify a yaml !type tag.
/// </summary>
[TypeSerializer]
public sealed class LayerDataSerializer : ITypeSerializer<BaseLayerData, MappingDataNode>
{
    public ValidationNode Validate(
        ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        DebugTools.AssertNull(node.Tag);
        return serializationManager.ValidateNode<RsiLayerData>(node, context);
    }

    public BaseLayerData Read(
        ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<BaseLayerData>? instanceProvider = null)
    {
        // If the node had a type-tag, it should be using the data definition serializer for that concrete type.
        DebugTools.AssertNull(node.Tag);
        return serializationManager.Read<RsiLayerData>(node, hookCtx, context, notNullableOverride: true);
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        BaseLayerData value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        // This should never be called, it should be using the data definition serializer for concrete types.
        throw new NotSupportedException();
    }
}

