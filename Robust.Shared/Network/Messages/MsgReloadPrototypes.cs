using System;
using Lidgren.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Robust.Shared.Network.Messages
{
    public sealed class MsgReloadPrototypes : NetMessage
    {
        // Arbitrary upper limit to prevent deserializing huge arrays.
        // Note that this is FILES not prototypes.
        public const int MaxPrototypeFiles = 50_000;

        public override MsgGroups MsgGroup => MsgGroups.Command;

        public ResPath[] Paths = default!;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var count = buffer.ReadInt32();
            if (count > MaxPrototypeFiles)
                throw new ArgumentException($"Too many prototypes being reloaded. Count: {count}, Max: {MaxPrototypeFiles}");

            Paths = new ResPath[count];

            for (var i = 0; i < count; i++)
            {
                Paths[i] = new ResPath(buffer.ReadString());
            }
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            if (Paths.Length > MaxPrototypeFiles)
                throw new ArgumentException($"Too many prototypes being reloaded. Count: {Paths.Length}, Max: {MaxPrototypeFiles}");
            buffer.Write(Paths.Length);

            foreach (var path in Paths)
            {
                buffer.Write(path.ToString());
            }
        }
    }
}
