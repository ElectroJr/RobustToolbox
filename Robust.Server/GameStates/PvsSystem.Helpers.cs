using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Robust.Server.GameStates;

// This partial class contains miscellaneous convenience functions
internal sealed partial class PvsSystem
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref EntityData GetOrNewEntityData(
        Dictionary<NetEntity, EntityData> entityData,
        NetEntity entity,
        EntityUid uid,
        MetaDataComponent meta)
    {
        ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(entityData, entity, out var exists);
        if (!exists)
            data = new((uid, meta));
        return ref data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref EntityData GetOrNewEntityData(Dictionary<NetEntity, EntityData> entityData, NetEntity entity)
    {
        ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(entityData, entity, out var exists);
        if (!exists)
            data = new(GetEntityData(entity));
        return ref data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref EntityData GetEntityData(Dictionary<NetEntity, EntityData> entityData, NetEntity entity)
    {
        DebugTools.Assert(entityData.ContainsKey(entity));
        ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(entityData, entity, out _);
        DebugTools.AssertNotNull(data.Entity.Comp);
        return ref data;
    }
}