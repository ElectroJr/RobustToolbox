using System;
using Lidgren.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

#nullable disable

namespace Robust.Shared.Network.Messages.Handshake
{
    internal sealed class MsgEncryptionResponse : NetMessage
    {
        public override string MsgName => string.Empty;

        public override MsgGroups MsgGroup => MsgGroups.Core;

        public Guid UserId;
        public byte[] SealedData;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            UserId = buffer.ReadGuid();
            SealedData = new byte[NetManager.EncryptionResponseLength];
            buffer.ReadBytes(SealedData);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(UserId);
            DebugTools.Assert(SealedData.Length == NetManager.EncryptionResponseLength);
            buffer.Write(SealedData);
        }
    }
}
