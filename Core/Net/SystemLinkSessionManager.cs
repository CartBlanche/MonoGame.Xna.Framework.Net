using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    internal static class SystemLinkSessionManager
    {
        private const int BroadcastPort = 31337;
        internal const int GamePort = 31338; // Port for gameplay UDP traffic
        private static readonly List<AvailableNetworkSession> discoveredSessions = new List<AvailableNetworkSession>();

        public static Task AdvertiseSessionAsync(ISessionAdvertiser session, CancellationToken cancellationToken)
        {
            // Periodically broadcast session info on LAN until session is full or ended
            return Task.Run(async () =>
            {
                using (var udpClient = new UdpClient(AddressFamily.InterNetwork))
                {
                    udpClient.EnableBroadcast = true;
                    var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
                    var localhostEndpoint = new IPEndPoint(IPAddress.Loopback, BroadcastPort);

                    // 255.255.255.255 only leaves through one network adapter, so the announcement is
                    // also sent to the broadcast address of every network this machine is on
                    // (the list is refreshed now and then because adapters come and go)
                    IReadOnlyList<IPAddress> subnetBroadcasts = LanBroadcast.GetBroadcastAddresses();
                    var subnetBroadcastsRefreshed = DateTime.UtcNow;

                    Debug.WriteLine("[LAN] " + $"Starting session advertisement on port {BroadcastPort}");
                    int broadcastCount = 0;

                    SessionAdvertisement ad;
                    while (!cancellationToken.IsCancellationRequested && !(ad = session.GetAdvertisement()).IsClosed)
                    {
                        var propertiesBytes = NetworkSession.SerializePropertiesBinary(ad.Properties);
                        // Include gameplay port in the header so joiners know where to send join requests
                        var header = $"SESSION:{ad.SessionId}:{ad.MaxGamers}:{ad.PrivateGamerSlots}:{ad.CurrentGamerCount}:{(ad.IsPasswordProtected ? 1 : 0)}:{(ad.HostGamertag ?? "Host").Replace(':', '_')}:{GamePort}:";
                        var headerBytes = Encoding.UTF8.GetBytes(header);
                        var message = new byte[headerBytes.Length + propertiesBytes.Length];
                        Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);
                        Buffer.BlockCopy(propertiesBytes, 0, message, headerBytes.Length, propertiesBytes.Length);

                        // Send to broadcast address for LAN discovery
                        try
                        {
                            await udpClient.SendAsync(message, message.Length, broadcastEndpoint).ConfigureAwait(false);
                        }
                        catch (SocketException ex)
                        {
                            Debug.WriteLine("[LAN] " + $"Broadcast advertisement send failed: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[LAN] " + $"Broadcast advertisement failed: {ex.Message}");
                        }

                        if (DateTime.UtcNow - subnetBroadcastsRefreshed > TimeSpan.FromSeconds(5))
                        {
                            subnetBroadcasts = LanBroadcast.GetBroadcastAddresses();
                            subnetBroadcastsRefreshed = DateTime.UtcNow;
                        }

                        foreach (var subnetBroadcast in subnetBroadcasts)
                        {
                            try
                            {
                                await udpClient.SendAsync(message, message.Length, new IPEndPoint(subnetBroadcast, BroadcastPort)).ConfigureAwait(false);
                            }
                            catch (SocketException ex)
                            {
                                Debug.WriteLine("[LAN] " + $"Broadcast to {subnetBroadcast} failed: {ex.Message}");
                            }
                        }

                        // ALSO send to localhost for same-machine testing
                        try
                        {
                            await udpClient.SendAsync(message, message.Length, localhostEndpoint).ConfigureAwait(false);
                            Debug.WriteLine("[LAN] " + $"Also sent advertisement to localhost (127.0.0.1:{BroadcastPort})");
                        }
                        catch (SocketException ex)
                        {
                            Debug.WriteLine("[LAN] " + $"Localhost advertisement send failed: {ex.Message}");
                        }

                        broadcastCount++;
                        Debug.WriteLine("[LAN] " + $"Sent broadcast #{broadcastCount} - SessionID: {ad.SessionId}, Gamers: {ad.CurrentGamerCount}/{ad.MaxGamers}");

                        await Task.Delay(750, cancellationToken).ConfigureAwait(false); // Broadcast every 750ms for faster discovery
                    }

                    Debug.WriteLine("[LAN] " + "Stopped broadcasting. Cancelled=" + cancellationToken.IsCancellationRequested);
                }
            }, cancellationToken);
        }

        public static async Task<IEnumerable<AvailableNetworkSession>> DiscoverSessionsAsync(int maxLocalGamers, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DISCOVERY] Starting session discovery on port {BroadcastPort}");

            // Use dictionary to deduplicate sessions by ID (in case we receive multiple broadcasts from same host)
            var sessionsDict = new Dictionary<string, AvailableNetworkSession>();

            try
            {
                using (var udpClient = new UdpClient(AddressFamily.InterNetwork))
                {
                    // Enable port reuse for multiple instances on same machine
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));

                    Debug.WriteLine($"[DISCOVERY] Successfully bound to port {BroadcastPort}");

                    udpClient.EnableBroadcast = true;
                    // DON'T set ReceiveTimeout - it interferes with ReceiveAsync()

                    // Listen for 1.5 seconds to catch at least 1 broadcast cycle (hosts broadcast every 1 second)
                    // Reduced from 2.5s for faster discovery while still being reliable
                    var startTime = DateTime.UtcNow;
                    var endTime = startTime.AddSeconds(1.5);
                    int receiveAttempts = 0;
                    int packetsReceived = 0;

                    // Only one receive is ever outstanding. Starting a new ReceiveAsync every 100ms and
                    // abandoning the old one when the timeout wins leaves orphaned receives behind, and
                    // the next datagram is handed to the oldest orphan, so it is lost.
                    Task<UdpReceiveResult> pendingReceive = null;

                    while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            receiveAttempts++;
                            // Try to receive with reduced timeout for faster response
                            var receiveTask = pendingReceive ?? (pendingReceive = udpClient.ReceiveAsync());
                            var timeoutTask = Task.Delay(100, cancellationToken);
                            var completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

                            if (completedTask == receiveTask)
                            {
                                pendingReceive = null;
                                var result = await receiveTask.ConfigureAwait(false);
                                var buffer = result.Buffer;
                                packetsReceived++;

                                Debug.WriteLine($"[DISCOVERY] Received packet #{packetsReceived} from {result.RemoteEndPoint} ({buffer.Length} bytes)");

                                // Find the header delimiter (the last colon of the header)
                                int headerEnd = 0;
                                int colonCount = 0;
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    if (buffer[i] == (byte)':')
                                    {
                                        colonCount++;
                                        if (colonCount == 8)
                                        {
                                            headerEnd = i + 1; // header ends after the 7th colon (includes game port)
                                            break;
                                        }
                                    }
                                }

                                if (colonCount == 8)
                                {
                                    var headerString = Encoding.UTF8.GetString(buffer, 0, headerEnd);
                                    Debug.WriteLine($"[DISCOVERY] Parsed header: {headerString}");

                                    var parts = headerString.Split(':');
                                    var sessionId = parts[1];
                                    // anybody on the LAN can broadcast garbage: a number that does not parse is a bad packet, not a crash
                                    if (!int.TryParse(parts[2], out var maxGamers) || !int.TryParse(parts[3], out var privateSlots)
                                        || !int.TryParse(parts[4], out var currentGamers) || !int.TryParse(parts[5], out var flags)
                                        || !int.TryParse(parts[7], out var gamePort) || maxGamers < 1 || maxGamers > 1000
                                        || privateSlots < 0 || currentGamers < 0 || gamePort < 1 || gamePort > 65535)
                                    {
                                        Debug.WriteLine("[DISCOVERY] Ignored an advertisement with bad numbers");
                                        continue;
                                    }
                                    var hostGamertag = parts[6];

                                    // Binary session properties start after headerEnd
                                    var propertiesBytes = new byte[buffer.Length - headerEnd];
                                    Buffer.BlockCopy(buffer, headerEnd, propertiesBytes, 0, propertiesBytes.Length);

                                    // Parse session properties without creating a full NetworkSession (avoids resource leaks,
                                    // and port 31338 contention)
                                    var sessionProperties = NetworkSession.DeserializeSessionPropertiesStatic(propertiesBytes);

                                    var hostEndpoint = new IPEndPoint(result.RemoteEndPoint.Address, gamePort);

                                    // Add to dictionary (will replace if we get multiple broadcasts from same session)
                                    sessionsDict[sessionId] = new AvailableNetworkSession(
                                        sessionName: "SystemLinkSession",
                                        hostGamertag: hostGamertag,
                                        currentGamerCount: currentGamers,
                                        openPublicGamerSlots: Math.Max(0, maxGamers - privateSlots - currentGamers),
                                        openPrivateGamerSlots: privateSlots,
                                        sessionType: NetworkSessionType.SystemLink,
                                        sessionProperties: sessionProperties,
                                        sessionId: sessionId,
                                        hostPeer: UdpTransport.ToPeer(hostEndpoint),
                                        isPasswordProtected: flags == 1);

                                    Debug.WriteLine($"[DISCOVERY] Added session: {hostGamertag} ({sessionId})");
                                }
                                else
                                {
                                    Debug.WriteLine($"[DISCOVERY] Invalid packet - expected 8 colons, found {colonCount}");
                                }
                            }
                            // If timeout occurs, continue listening until endTime
                        }
                        catch (SocketException ex)
                        {
                            Debug.WriteLine($"[DISCOVERY] SocketException: {ex.Message}");
                            // Socket timeout or other network error - continue listening
                        }
                        catch (ObjectDisposedException)
                        {
                            Debug.WriteLine("[DISCOVERY] Socket disposed");
                            // Socket was disposed (shouldn't happen, but handle gracefully)
                            break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[DISCOVERY] Unexpected error: {ex.GetType().Name} - {ex.Message}");
                        }
                    }

                    // the socket is about to be disposed; the receive still pending on it will fault, so observe that
                    pendingReceive?.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

                    var elapsed = DateTime.UtcNow - startTime;
                    Debug.WriteLine($"[DISCOVERY] Completed after {elapsed.TotalSeconds:F2}s. Attempts: {receiveAttempts}, Packets: {packetsReceived}, Sessions: {sessionsDict.Count}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISCOVERY] Fatal error: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
            }

            return sessionsDict.Values;
        }

        public static Task<NetworkSession> JoinSessionAsync(AvailableNetworkSession availableSession, CancellationToken cancellationToken)
        {
            return JoinSessionAsync(availableSession, transport: null, cancellationToken);
        }

        public static async Task<NetworkSession> JoinSessionAsync(AvailableNetworkSession availableSession, IPeerTransport transport, CancellationToken cancellationToken, string password = null)
        {
            // Reliable join with timeout and retry
            const int MAX_RETRIES = 3;
            const int TIMEOUT_MS = 300; // Faster timeout for LAN (reduced from 500ms)

            Debug.WriteLine($"[JOIN] Starting join process for session {availableSession.SessionId}");

            // Create client session in Joining state
            var session = new NetworkSession(availableSession.SessionType,
                availableSession.OpenPublicGamerSlots + availableSession.CurrentGamerCount + availableSession.OpenPrivateGamerSlots,
                availableSession.OpenPrivateGamerSlots,
                false,
                availableSession.SessionId,
                transport);
            session.sessionState = NetworkSessionState.Joining; // Use new Joining state

            // Copy session properties from AvailableNetworkSession to NetworkSession
            foreach (var kvp in availableSession.SessionProperties)
                session.SessionProperties[kvp.Key] = kvp.Value;

            // Bind client transport on join so it can receive packets
            if (!session.NetworkTransport.IsBound)
            {
                session.NetworkTransport.Bind();
            }

            if (!availableSession.HostPeer.IsValid)
            {
                throw new NetworkSessionJoinException("Host endpoint is null", NetworkSessionJoinError.SessionNotFound);
            }

            // only the host's address may answer our join request
            session.JoinTargetPeer = availableSession.HostPeer;
            session.JoinPassword = password;

            // The host gamer is added when the host accepts us and says who it is

            // Send join request with retry logic
            for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
            {
                Debug.WriteLine($"[JOIN] Sending join request (attempt {attempt + 1}/{MAX_RETRIES})");

                // Use the session's own local gamer
                var localGamer = session.LocalGamers.FirstOrDefault();
                if (localGamer == null)
                    throw new InvalidOperationException("No local gamer found in session");

                Debug.WriteLine($"[JOIN] Sending as gamer: {localGamer.Gamertag} (ID: {localGamer.GamerId})");

                var joinRequest = new JoinRequestMessage
                {
                    GamerId = localGamer.GamerId,
                    Gamertag = localGamer.Gamertag,
                    ProtocolVersion = JoinRequestMessage.CURRENT_PROTOCOL_VERSION
                };
                var writer = new PacketWriter();
                joinRequest.Serialize(writer);
                var joinBytes = writer.GetData();
                session.NetworkTransport.Send(availableSession.HostPeer, joinBytes, joinBytes.Length);

                // Wait for response (NetworkSession.OnMessageReceived will update state to Lobby if accepted)
                var waitStart = DateTime.UtcNow;
                while ((DateTime.UtcNow - waitStart).TotalMilliseconds < TIMEOUT_MS)
                {
                    if (session.sessionState == NetworkSessionState.Lobby)
                    {
                        Debug.WriteLine($"[JOIN] Successfully joined session after {attempt + 1} attempt(s)");
                        // Start connection monitoring for client
                        session.StartConnectionMonitoring();
                        return session;
                    }

                    // The host refused us (session full, game already started, wrong version):
                    // report why straight away instead of retrying until a timeout
                    if (session.joinRejectedReason != null)
                    {
                        var error = session.joinRejectedError;
                        var reason = session.joinRejectedReason;
                        Debug.WriteLine($"[JOIN] Rejected by host: {reason} ({error})");
                        session.Dispose();
                        throw new NetworkSessionJoinException(reason, error);
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Debug.WriteLine($"[JOIN] Attempt {attempt + 1} timed out");
            }

            // After MAX_RETRIES attempts, give up
            Debug.WriteLine($"[JOIN] Failed to join session after {MAX_RETRIES} attempts");
            session.Dispose();
            throw new NetworkSessionJoinException(
                $"Failed to join session after {MAX_RETRIES} attempts. Host may be unreachable or session is full.",
                NetworkSessionJoinError.Timeout);
        }
    }
}