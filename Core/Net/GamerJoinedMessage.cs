namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Sent by the host to the gamers already in the session when another gamer joins, so that every
    /// machine knows every gamer, not only the host.
    /// </summary>
    public class GamerJoinedMessage : INetworkMessage
    {
        public byte MessageType => 11;

        /// <summary>The <see cref="NetworkGamer.Id"/> the host gave the new gamer.</summary>
        public byte Id { get; set; }

        public string GamerId { get; set; }

        public string Gamertag { get; set; }

        public void Serialize(PacketWriter writer)
        {
            writer.Write(MessageType);
            writer.Write(Id);
            writer.Write(GamerId ?? string.Empty);
            writer.Write(Gamertag ?? string.Empty);
        }

        public void Deserialize(PacketReader reader)
        {
            Id = reader.ReadByte();
            GamerId = reader.ReadString();
            Gamertag = reader.ReadString();
        }
    }
}
