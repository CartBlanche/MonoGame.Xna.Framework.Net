using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Net;

namespace Microsoft.Xna.Framework.Net
{
    public class JoinAcceptedMessage : INetworkMessage
    {
        /// <summary>A gamer already in the session, as the joiner needs to know them.</summary>
        public struct RosterEntry
        {
            public byte Id { get; set; }
            public string GamerId { get; set; }
            public string Gamertag { get; set; }
        }

        public byte MessageType => 3;
        public string SessionId { get; set; } = string.Empty;
        public string HostGamerId { get; set; } = string.Empty;
        public string HostGamertag { get; set; } = string.Empty;

        /// <summary>The host's <see cref="NetworkGamer.Id"/>: the host decides the ids for everyone.</summary>
        public byte HostId { get; set; }

        /// <summary>The <see cref="NetworkGamer.Id"/> the host gave the joiner.</summary>
        public byte YourId { get; set; }

        /// <summary>The key for the link between this joiner and the host.</summary>
        public byte[] LinkKey { get; set; } = Array.Empty<byte>();

        /// <summary>True if <see cref="LinkKey"/> is sealed under the session password; false if it is the key itself.</summary>
        public bool KeyIsSealed { get; set; }

        /// <summary>The other gamers already in the session (not the host, not the joiner).</summary>
        public List<RosterEntry> Others { get; set; } = new List<RosterEntry>();

        public void Serialize(PacketWriter writer)
        {
            writer.Write(MessageType);
            writer.Write(SessionId);
            writer.Write(HostGamerId);
            writer.Write(HostGamertag);
            writer.Write(HostId);
            writer.Write(YourId);
            writer.Write(KeyIsSealed);
            writer.WriteLengthPrefixed(LinkKey ?? Array.Empty<byte>());
            writer.Write((byte)Others.Count);
            foreach (RosterEntry entry in Others)
            {
                writer.Write(entry.Id);
                writer.Write(entry.GamerId);
                writer.Write(entry.Gamertag);
            }
        }

        public void Deserialize(PacketReader reader)
        {
            // Reader is positioned after the type byte
            SessionId = reader.ReadString();
            HostGamerId = reader.ReadString();
            HostGamertag = reader.ReadString();
            HostId = reader.ReadByte();
            YourId = reader.ReadByte();
            KeyIsSealed = reader.ReadBoolean();
            LinkKey = reader.ReadLengthPrefixedBytes();
            int count = reader.ReadByte();
            Others = new List<RosterEntry>(count);
            for (int i = 0; i < count; i++)
                Others.Add(new RosterEntry { Id = reader.ReadByte(), GamerId = reader.ReadString(), Gamertag = reader.ReadString() });
        }
    }
}
