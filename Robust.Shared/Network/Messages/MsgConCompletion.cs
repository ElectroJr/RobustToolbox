using System;
using Lidgren.Network;
using Robust.Shared.Serialization;

namespace Robust.Shared.Network.Messages;

#nullable disable

public sealed class MsgConCompletion : NetMessage
{
    public const int MaxCompletions = 100;
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public int Seq { get; set; }
    public string[] Args { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Seq = buffer.ReadInt32();

        var len = Math.Min(buffer.ReadVariableInt32(), MaxCompletions);
        Args = new string[len];
        for (var i = 0; i < len; i++)
        {
            Args[i] = buffer.ReadString();
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Seq);

        var len = Math.Min(Args.Length, MaxCompletions);
        buffer.WriteVariableInt32(len);
        for (var i = 0; i < len; i++)
        {
            buffer.Write(Args[i]);
        }
    }
}
