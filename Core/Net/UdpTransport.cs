using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// UDP transport for LAN (SystemLink) play. A peer is an IP address and port.
    /// </summary>
    public class UdpTransport : IPeerTransport
    {
        private const string PeerPrefix = "udp:";

        private readonly UdpClient udpClient;
        private readonly int preferredPort;
        private bool isBound;

        /// <summary>
        /// Creates a transport that binds an ephemeral port.
        /// </summary>
        public UdpTransport() : this(0)
        {
        }

        /// <summary>
        /// Creates a transport that tries to bind <paramref name="preferredPort"/> and takes any free
        /// port if that is taken (a second instance on the same machine, for instance).
        /// </summary>
        public UdpTransport(int preferredPort)
        {
            this.preferredPort = preferredPort;
            udpClient = new UdpClient();
        }

        /// <summary>The peer that stands for an IP endpoint.</summary>
        public static PeerId ToPeer(IPEndPoint endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            return new PeerId(PeerPrefix + endpoint);
        }

        /// <summary>The IP endpoint a peer stands for, or null if it is not a UDP peer.</summary>
        public static IPEndPoint? ToEndPoint(PeerId peer)
        {
            if (!peer.IsValid || !peer.Value.StartsWith(PeerPrefix, StringComparison.Ordinal))
                return null;
            return IPEndPoint.TryParse(peer.Value.AsSpan(PeerPrefix.Length), out var endpoint) ? endpoint : null;
        }

        /// <summary>Sends the first <paramref name="length"/> bytes of <paramref name="data"/> as one UDP datagram to <paramref name="peer"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or longer than <paramref name="data"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="peer"/> is not a UDP peer (see <see cref="ToEndPoint"/>).</exception>
        public void Send(PeerId peer, byte[] data, int length)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (length < 0 || length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            IPEndPoint endpoint = ToEndPoint(peer) ?? throw new ArgumentException($"{peer} is not a UDP peer.", nameof(peer));

            udpClient.Send(data, length, endpoint);
        }

        /// <summary>Waits for the next datagram and returns its bytes with the sender as a UDP peer.</summary>
        /// <exception cref="ObjectDisposedException">The transport was disposed.</exception>
        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            var result = await udpClient.ReceiveAsync().ConfigureAwait(false);
            return (result.Buffer, ToPeer(result.RemoteEndPoint));
        }

        /// <summary>Closes the socket, which makes a pending <see cref="ReceiveAsync"/> fail.</summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            udpClient?.Close();
            udpClient?.Dispose();
        }

        /// <summary>Binds the preferred port on all interfaces, or any free port if that one is taken. Does nothing if already bound.</summary>
        public void Bind()
        {
            if (isBound)
                return;

            if (preferredPort != 0)
            {
                try
                {
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, preferredPort));
                    isBound = true;
                    return;
                }
                catch (SocketException)
                {
                    // taken: fall through to any free port
                }
            }

            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            isBound = true;
        }

        /// <inheritdoc/>
        public bool IsBound => isBound;

        /// <summary>The socket's address, or null before binding.</summary>
        public IPEndPoint? LocalEndPoint => isBound ? udpClient.Client.LocalEndPoint as IPEndPoint : null;

        /// <summary>The <see cref="LocalEndPoint"/> as a peer, or <see cref="PeerId.None"/> before binding.</summary>
        public PeerId LocalPeer => LocalEndPoint is IPEndPoint local ? ToPeer(local) : PeerId.None;
    }
}
