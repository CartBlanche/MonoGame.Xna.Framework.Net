using System;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Network message indicating a gamer's readiness state changed.
    /// </summary>
    internal class ReadinessUpdateMessage : INetworkMessage
    {
        // Reserve message id 4
        public byte MessageType => 4;

        public string GamerId { get; set; } = string.Empty;
        public bool IsReady { get; set; }

        public void Serialize(PacketWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write(MessageType);
            writer.Write(GamerId);
            writer.Write(IsReady);
        }

        public void Deserialize(PacketReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            // Reader is positioned after the type byte
            GamerId = reader.ReadString();
            IsReady = reader.ReadBoolean();
        }
    }
}
