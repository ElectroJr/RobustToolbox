using Lidgren.Network;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Robust.Shared.Upload;

public sealed class NetworkResourceUploadMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableUnordered;
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public byte[] Data = default!;
    public ResPath RelativePath { get; set; } = ResPath.Self;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var dataLength = buffer.ReadVariableInt32();

        // Prevent allocation of huge byte[].
        // TODO allow message handlers to inspect messages before deserializing?
        // Or just add per-message size limits.
        if (!IoCManager.Resolve<SharedNetworkResourceManager>().CanUpload(dataLength))
            return;

        Data = buffer.ReadBytes(dataLength);
        RelativePath = new ResPath(buffer.ReadString());
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Data!.Length);
        buffer.Write(Data);
        buffer.Write(RelativePath.ToString());
    }
}
