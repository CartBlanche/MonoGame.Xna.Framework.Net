using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net.Steam
{
    /// <summary>
    /// Carries datagrams between Steam users over Steam P2P (Steam Datagram Relay under the hood).
    /// A peer is a SteamID. Reliability is the framework's reliable channel's job, so everything is
    /// sent unreliably; the one thing Steam adds is NAT traversal and relaying.
    /// </summary>
    public sealed class SteamP2PTransport : IPeerTransport
    {
        private const int DefaultChannel = 0;
        private const string PeerPrefix = "steam:";

        private volatile bool disposed;
        private volatile bool isBound;

        public bool IsBound => isBound;

        public PeerId LocalPeer => isBound && SteamRuntime.IsInitialized ? ToPeer(SteamUser.GetSteamID()) : PeerId.None;

        /// <summary>The peer that stands for a Steam user.</summary>
        public static PeerId ToPeer(CSteamID steamId) => new PeerId(PeerPrefix + steamId.m_SteamID);

        /// <summary>The Steam user a peer stands for, if it is a Steam peer.</summary>
        public static bool TryGetSteamId(PeerId peer, out CSteamID steamId)
        {
            steamId = default;
            if (!peer.IsValid || !peer.Value.StartsWith(PeerPrefix, StringComparison.Ordinal))
                return false;
            if (!ulong.TryParse(peer.Value.Substring(PeerPrefix.Length), out var raw))
                return false;
            steamId = new CSteamID(raw);
            return steamId.IsValid();
        }

        public void Bind()
        {
            ThrowIfDisposed();
            isBound = true;
        }

        public void Send(PeerId peer, byte[] data, int length)
        {
            ThrowIfDisposed();

            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            if (!SteamRuntime.IsInitialized) throw new InvalidOperationException("Steam runtime is not initialized.");

            if (!TryGetSteamId(peer, out var recipient))
                throw new ArgumentException($"{peer} is not a Steam peer.", nameof(peer));

            if (recipient == SteamUser.GetSteamID())
            {
                throw new InvalidOperationException(
                    "Steam P2P recipient resolves to the local SteamID. " +
                    "Running multiple instances under the same Steam account is not a supported peer configuration.");
            }

            var payload = length == data.Length ? data : CopyPrefix(data, length);
            if (!SteamNetworking.SendP2PPacket(recipient, payload, (uint)payload.Length, EP2PSend.k_EP2PSendUnreliable, DefaultChannel))
                throw new InvalidOperationException($"Steam P2P send failed to recipient {recipient.m_SteamID}");
        }

        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            ThrowIfDisposed();

            while (!disposed)
            {
                if (!isBound)
                    Bind();

                if (!SteamRuntime.IsInitialized)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    continue;
                }

                if (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, DefaultChannel) && packetSize > 0)
                {
                    var buffer = new byte[packetSize];
                    if (SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID remote, DefaultChannel) && bytesRead > 0)
                    {
                        if (bytesRead != packetSize)
                            Array.Resize(ref buffer, (int)bytesRead);

                        // a stranger's first packet is a request to talk: accept it
                        return (buffer, ToPeer(remote));
                    }
                }

                await Task.Delay(5).ConfigureAwait(false);
            }

            throw new ObjectDisposedException(nameof(SteamP2PTransport));
        }

        public void Dispose()
        {
            disposed = true;
            isBound = false;
        }

        private static byte[] CopyPrefix(byte[] source, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(source, 0, copy, 0, length);
            return copy;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SteamP2PTransport));
        }
    }
}
