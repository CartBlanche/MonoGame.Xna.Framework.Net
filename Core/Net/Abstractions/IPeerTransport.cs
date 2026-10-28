using System;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Moves datagrams between this machine and its peers. This is all a back-end has to provide to
    /// carry a <see cref="NetworkSession"/>: lobby, readiness, reliability, ordering, fragmentation and
    /// relaying are built on top, the same for every transport.
    /// </summary>
    /// <remarks>
    /// Datagrams may be lost, duplicated or reordered, as on UDP. The framework's reliable channel
    /// makes them reliable, so a transport should not add reliability of its own (it would only add
    /// latency). A datagram of up to 1200 bytes must go through in one piece.
    /// </remarks>
    public interface IPeerTransport : IDisposable
    {
        /// <summary>True once <see cref="Bind"/> has run.</summary>
        bool IsBound { get; }

        /// <summary>This machine as its peers know it, or <see cref="PeerId.None"/> before binding or if the transport cannot tell.</summary>
        PeerId LocalPeer { get; }

        /// <summary>Starts the transport so datagrams can be sent and received.</summary>
        void Bind();

        /// <summary>Sends the first <paramref name="length"/> bytes of <paramref name="data"/> to a peer. Does not block on the peer.</summary>
        void Send(PeerId peer, byte[] data, int length);

        /// <summary>
        /// Waits for the next datagram from any peer. Must complete (by throwing
        /// <see cref="ObjectDisposedException"/>) when the transport is disposed.
        /// </summary>
        Task<(byte[] Data, PeerId Sender)> ReceiveAsync();
    }
}
