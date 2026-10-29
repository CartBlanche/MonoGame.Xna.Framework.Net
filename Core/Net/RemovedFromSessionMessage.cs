namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Sent by the host to a machine it has removed with <see cref="NetworkMachine.RemoveFromSession"/>.
    /// </summary>
    public class RemovedFromSessionMessage : INetworkMessage
    {
        public byte MessageType => 12;

        public string Reason { get; set; } = string.Empty;

        public void Serialize(PacketWriter writer)
        {
            writer.Write(MessageType);
            writer.Write(Reason ?? string.Empty);
        }

        public void Deserialize(PacketReader reader)
        {
            Reason = reader.ReadString();
        }
    }
}
