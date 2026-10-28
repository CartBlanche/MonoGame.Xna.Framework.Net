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
    }
}
