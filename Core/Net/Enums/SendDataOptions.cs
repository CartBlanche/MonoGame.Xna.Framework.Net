namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Options for sending data over the network. Combine <see cref="Chat"/> with the others.
	/// </summary>
	[Flags]
	public enum SendDataOptions
    {
        /// <summary>
        /// No special options - unreliable delivery.
        /// </summary>
        None = 0,

        /// <summary>
        /// Reliable delivery - guarantees the data will arrive.
        /// </summary>
        Reliable = 1,

        /// <summary>
        /// In-order delivery - data arrives in the order sent.
        /// </summary>
        InOrder = 2,

        /// <summary>
        /// Reliable and in-order delivery.
        /// </summary>
        ReliableInOrder = Reliable | InOrder,

        /// <summary>
        /// Marks the data as chat text. XNA sent chat unencrypted, as international rules required; here every
        /// datagram on a link is authenticated, so the flag is accepted for source compatibility and changes nothing.
        /// </summary>
        Chat = 4
    }
}
