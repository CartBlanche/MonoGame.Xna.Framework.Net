using System;
using Microsoft.Xna.Framework.Net;

namespace Microsoft.Xna.Framework.Net
{
    public class JoinRequestMessage : INetworkMessage
    {
        // 2: gamer data is wrapped in reliable/unreliable channels (see ReliableChannel); older peers cannot talk to this one
        public const byte CURRENT_PROTOCOL_VERSION = 1;
        
        public byte MessageType => 2;
        public byte ProtocolVersion { get; set; } = CURRENT_PROTOCOL_VERSION;
        public string GamerId { get; set; } = string.Empty;
        public string Gamertag { get; set; } = string.Empty;

        /// <summary>Proof of knowing the session password, answering a challenge; empty on a first request.</summary>
        public byte[] Proof { get; set; } = Array.Empty<byte>();

        public void Serialize(PacketWriter writer)
        {
            writer.Write(MessageType);
            writer.Write(ProtocolVersion);
            writer.Write(GamerId);
            writer.Write(Gamertag);
            writer.WriteLengthPrefixed(Proof ?? Array.Empty<byte>());
        }

        public void Deserialize(PacketReader reader)
        {
            // Reader is positioned after the type byte
            ProtocolVersion = reader.ReadByte();
            GamerId = reader.ReadString();
            Gamertag = reader.ReadString();
            Proof = reader.ReadLengthPrefixedBytes();
        }
    }
}