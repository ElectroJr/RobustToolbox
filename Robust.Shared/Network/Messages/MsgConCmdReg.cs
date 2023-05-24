using System;
using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    public sealed class MsgConCmdReg : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.String;

        public Command[] Commands { get; set; }

        public sealed class Command
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Help { get; set; }
        }

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var cmdCount = buffer.ReadUInt16();
            Commands = new Command[cmdCount];
            for (var i = 0; i < cmdCount; i++)
            {
                Commands[i] = new Command()
                {
                    Name = buffer.ReadString(),
                    Description = buffer.ReadString(),
                    Help = buffer.ReadString()
                };
            }
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write((UInt16)Commands.Length);
            foreach (var command in Commands)
            {
                buffer.Write(command.Name);
                buffer.Write(command.Description);
                buffer.Write(command.Help);
            }
        }
    }

    /// <summary>
    ///     Requests a <see cref="MsgConCmdReg"/> message from the server
    /// </summary>
    public sealed class MsgConCmdRequest : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.String;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {

        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }
}
