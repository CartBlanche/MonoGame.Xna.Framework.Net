using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Provides a UDP-based implementation of the <see cref="INetworkTransport"/> interface for sending and receiving network data.
    /// </summary>
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

        public void Send(PeerId peer, byte[] data, int length)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (length < 0 || length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            IPEndPoint endpoint = ToEndPoint(peer) ?? throw new ArgumentException($"{peer} is not a UDP peer.", nameof(peer));

            udpClient.Send(data, length, endpoint);
        }

        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            var result = await udpClient.ReceiveAsync().ConfigureAwait(false);
            return (result.Buffer, ToPeer(result.RemoteEndPoint));
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            udpClient?.Close();
            udpClient?.Dispose();
        }

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

        public bool IsBound => isBound;

        /// <summary>The socket's address, or null before binding.</summary>
        public IPEndPoint? LocalEndPoint => isBound ? udpClient.Client.LocalEndPoint as IPEndPoint : null;

        public PeerId LocalPeer => LocalEndPoint is IPEndPoint local ? ToPeer(local) : PeerId.None;
    }
}
