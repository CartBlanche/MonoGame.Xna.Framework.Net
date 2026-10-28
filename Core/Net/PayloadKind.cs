namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// The first byte of everything that comes out of a gamer's <see cref="ReliableChannel"/>: what the
    /// rest of the payload is. Keeping the two apart means a game packet can start with any byte at all
    /// without ever being taken for a framework message.
    /// </summary>
    internal static class PayloadKind
    {
        /// <summary>Game data, exactly as passed to SendData.</summary>
        public const byte Application = 0x31;

        /// <summary>A serialized <see cref="INetworkMessage"/>: type id, then its fields.</summary>
        public const byte Message = 0x32;

        /// <summary>
        /// Game data a client asks the host to pass on to another client: options, target id, then the data.
        /// Clients only talk to the host, so this is how one client's data reaches another.
        /// </summary>
        public const byte Relay = 0x33;

        /// <summary>Game data the host passes on to a client: the original sender's id, then the data.</summary>
        public const byte Forwarded = 0x34;
    }
}
