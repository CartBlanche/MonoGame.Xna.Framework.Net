namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// A host's answer to a join request for a password-protected session: a salt to turn the password
    /// into a key with, and a one-time nonce to prove knowledge of it against.
    /// </summary>
    internal class PasswordChallengeMessage : INetworkMessage
    {
        public byte MessageType => 13;

        public byte[] Salt { get; set; } = Array.Empty<byte>();

        public byte[] Nonce { get; set; } = Array.Empty<byte>();

        public void Serialize(PacketWriter writer)
        {
            writer.Write(MessageType);
            writer.WriteLengthPrefixed(Salt);
            writer.WriteLengthPrefixed(Nonce);
        }

        public void Deserialize(PacketReader reader)
        {
            Salt = reader.ReadLengthPrefixedBytes();
            Nonce = reader.ReadLengthPrefixedBytes();
        }
    }
}
