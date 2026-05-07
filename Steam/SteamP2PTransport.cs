using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Net.Steam;
using Steamworks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Steam P2P transport implementation backed by SteamNetworking legacy P2P APIs.
    /// Uses synthetic IPv6 endpoints to keep compatibility with existing endpoint-based session code.
    /// </summary>
    public sealed class SteamP2PTransport : INetworkTransport
    {
        private const int DefaultChannel = 0;
        private static readonly byte[] EndpointPrefix = new byte[] { 0xFD, 0x00, 0x4D, 0x47, 0x4E, 0x45, 0x54, 0x00 };

        private readonly ConcurrentDictionary<string, CSteamID> endpointToSteamId = new ConcurrentDictionary<string, CSteamID>();
        private readonly ConcurrentDictionary<ulong, IPEndPoint> steamIdToEndpoint = new ConcurrentDictionary<ulong, IPEndPoint>();

        private volatile bool disposed;
        private volatile bool isBound;

        public bool IsBound => isBound;

        public IPEndPoint LocalEndPoint
        {
            get
            {
                if (!isBound || !SteamRuntime.IsInitialized)
                {
                    return null;
                }

                return ToEndpoint(SteamUser.GetSteamID());
            }
        }

        public void Bind()
        {
            ThrowIfDisposed();
            isBound = true;
        }

        public void Bind(int port)
        {
            ThrowIfDisposed();
            isBound = true;
        }

        public void Send(byte[] data, IPEndPoint endpoint)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Send(data, data.Length, endpoint);
        }

        public void Send(byte[] data, int length, IPEndPoint endpoint)
        {
            ThrowIfDisposed();

            if (data == null) throw new ArgumentNullException(nameof(data));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            if (!SteamRuntime.IsInitialized) throw new InvalidOperationException("Steam runtime is not initialized.");

            if (!TryGetSteamId(endpoint, out var recipient))
            {
                throw new InvalidOperationException($"No SteamID mapping for endpoint {endpoint}");
            }

            var localSteamId = SteamUser.GetSteamID();
            if (recipient == localSteamId)
            {
                throw new InvalidOperationException(
                    "Steam P2P recipient resolves to the local SteamID. " +
                    "Running multiple instances under the same Steam account is not a supported peer configuration.");
            }

            var payload = length == data.Length ? data : CopyPrefix(data, length);
            var ok = SteamNetworking.SendP2PPacket(recipient, payload, (uint)payload.Length, EP2PSend.k_EP2PSendReliable, DefaultChannel);
            if (!ok)
            {
                throw new InvalidOperationException($"Steam P2P send failed to recipient {recipient.m_SteamID}");
            }
        }

        public Task SendAsync(byte[] data, IPEndPoint endpoint)
        {
            Send(data, endpoint);
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] data, int length, IPEndPoint endpoint)
        {
            Send(data, length, endpoint);
            return Task.CompletedTask;
        }

        public (byte[] data, IPEndPoint sender) Receive()
        {
            return ReceiveAsync().GetAwaiter().GetResult();
        }

        public async Task<(byte[] data, IPEndPoint sender)> ReceiveAsync()
        {
            ThrowIfDisposed();

            while (!disposed)
            {
                if (!isBound)
                {
                    Bind();
                }

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
                        {
                            Array.Resize(ref buffer, (int)bytesRead);
                        }

                        var endpoint = RememberRemoteEndpoint(remote);
                        return (buffer, endpoint);
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

        public static IPEndPoint ToEndpoint(CSteamID steamId)
        {
            var bytes = new byte[16];
            Array.Copy(EndpointPrefix, 0, bytes, 0, EndpointPrefix.Length);

            var steamBytes = BitConverter.GetBytes(steamId.m_SteamID);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(steamBytes);
            }

            Array.Copy(steamBytes, 0, bytes, 8, 8);
            return new IPEndPoint(new IPAddress(bytes), 31338);
        }

        public static bool TryParseEndpoint(IPEndPoint endpoint, out CSteamID steamId)
        {
            steamId = default;
            if (endpoint == null || endpoint.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            var bytes = endpoint.Address.GetAddressBytes();
            if (bytes.Length != 16)
            {
                return false;
            }

            for (var i = 0; i < EndpointPrefix.Length; i++)
            {
                if (bytes[i] != EndpointPrefix[i])
                {
                    return false;
                }
            }

            var steamBytes = new byte[8];
            Array.Copy(bytes, 8, steamBytes, 0, 8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(steamBytes);
            }

            var steamIdValue = BitConverter.ToUInt64(steamBytes, 0);
            steamId = new CSteamID(steamIdValue);
            return steamId.IsValid();
        }

        private IPEndPoint RememberRemoteEndpoint(CSteamID remote)
        {
            if (steamIdToEndpoint.TryGetValue(remote.m_SteamID, out var existing))
            {
                return existing;
            }

            var endpoint = ToEndpoint(remote);
            steamIdToEndpoint.TryAdd(remote.m_SteamID, endpoint);
            endpointToSteamId.TryAdd(endpoint.ToString(), remote);
            return endpoint;
        }

        private bool TryGetSteamId(IPEndPoint endpoint, out CSteamID steamId)
        {
            if (endpointToSteamId.TryGetValue(endpoint.ToString(), out steamId))
            {
                return true;
            }

            if (TryParseEndpoint(endpoint, out steamId))
            {
                endpointToSteamId.TryAdd(endpoint.ToString(), steamId);
                steamIdToEndpoint.TryAdd(steamId.m_SteamID, endpoint);
                return true;
            }

            return false;
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
            {
                throw new ObjectDisposedException(nameof(SteamP2PTransport));
            }
        }
    }
}
