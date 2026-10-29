using System;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Event arguments for when a network message is received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the received message.
        /// </summary>
        public INetworkMessage Message { get; }

        /// <summary>
        /// Gets the peer that sent the message.
        /// </summary>
        public PeerId RemotePeer { get; }

        /// <summary>
        /// Gets the gamer who sent the message.
        /// </summary>
        public NetworkGamer Sender { get; internal set; }

        /// <summary>
        /// Initializes a new instance of MessageReceivedEventArgs.
        /// </summary>
        public MessageReceivedEventArgs(INetworkMessage message, PeerId remotePeer)
        {
            Message = message;
            RemotePeer = remotePeer;
            Sender = null;
        }
    }
}
