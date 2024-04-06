using System;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Robust.Shared.Upload;

public sealed class NetworkResourceUploadMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableUnordered;
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public int Size;

    /// <summary>
    /// Size of the data after decompression. -1 implies the data is already decompressed.
    /// </summary>
    public int UncompressedSize;

    /// <summary>
    /// Compressed file data
    /// </summary>
    public byte[] Data = default!;

    public ResPath RelativePath = ResPath.Self;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Size = buffer.ReadVariableInt32();
        UncompressedSize = buffer.ReadVariableInt32();
        Data = buffer.ReadBytes(Size);
        RelativePath = new ResPath(buffer.ReadString());
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Size);
        buffer.WriteVariableInt32(UncompressedSize);
        buffer.Write(Data.AsSpan(Size));
        buffer.Write(RelativePath.ToString());
        buffer.Write(ResPath.Separator);
    }
}
