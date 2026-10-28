using System;
using System.Collections.Concurrent;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using System.IO;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Represents a network session for multiplayer gaming.
    /// </summary>
    public class NetworkSession : IDisposable, IAsyncDisposable
    {
        // Event for received messages
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        private readonly List<NetworkGamer> gamers;
        private readonly GamerCollection gamerCollection;
        private readonly NetworkSessionType sessionType;
        private readonly int maxGamers;
        private readonly int privateGamerSlots;
        private readonly Dictionary<string, IPEndPoint> gamerEndpoints;

        // Reliable delivery over UDP: one channel per remote gamer (see ReliableChannel). Everything sent
        // to a gamer goes through it, as reliable or unreliable, and is unwrapped again on arrival.
        private readonly Dictionary<string, ReliableChannel> reliableChannels = new Dictionary<string, ReliableChannel>();
        private readonly object reliableLock = new object();
        private readonly Stopwatch reliableClock = Stopwatch.StartNew();
        private Timer reliableTimer;
        private const int ReliableTickMilliseconds = 50;
        private readonly object lockObject = new object();

        private LocalNetworkGamer localGamer;
        private INetworkTransport networkTransport;
        internal NetworkSessionState sessionState;
        internal bool disposed;
        private bool isHost;
        internal string sessionId;
        private Task receiveTask;
        private CancellationTokenSource cancellationTokenSource;

        // Phase 1 additions
        private ConnectionHealthMonitor connectionMonitor;
        internal ConnectionHealthMonitor ConnectionMonitor => connectionMonitor;
        private NetworkDiagnostics diagnostics;
        private INetworkLogger logger;

        // Events raised by the network and timer threads are queued here and raised from Update()
        private readonly ConcurrentQueue<Action> pendingEvents = new ConcurrentQueue<Action>();
        private int sessionEndedRaised;

        /// <summary>
        /// Gets or sets whether events are raised from <see cref="Update"/> (the default, as XNA did) or
        /// straight away on the thread that produced them.
        /// </summary>
        public EventDispatchMode EventDispatchMode { get; set; } = EventDispatchMode.OnUpdate;

        // Events
        public event EventHandler<GameStartedEventArgs> GameStarted;
        public event EventHandler<GameEndedEventArgs> GameEnded;

        private event EventHandler<GamerJoinedEventArgs> gamerJoined;
        private bool isGamerJoinedSubscribed = false;
        public event EventHandler<GamerJoinedEventArgs> GamerJoined
        {
            add
            {
                lock (lockObject)
                {
                    gamerJoined += value;
                    isGamerJoinedSubscribed = true;

                    // Notify pending gamers if this is the first subscription
                    NotifyPendingGamers();
                }
            }
            remove
            {
                lock (lockObject)
                {
                    gamerJoined -= value;
                    isGamerJoinedSubscribed = gamerJoined != null;
                }
            }
        }

        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        // Static event for invite acceptance
        public static event EventHandler<InviteAcceptedEventArgs> InviteAccepted;

        /// <summary>
        /// Allows changing the network transport implementation.
        /// </summary>
        public INetworkTransport NetworkTransport
        {
            get => networkTransport;
            set => networkTransport = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets network diagnostics for this session.
        /// </summary>
        public NetworkDiagnostics Diagnostics => diagnostics;

        /// <summary>
        /// Gets or sets the network logger.
        /// </summary>
        public INetworkLogger Logger
        {
            get => logger;
            set => logger = value ?? new NullNetworkLogger();
        }

        /// <summary>
        /// Initializes a new NetworkSession.
        /// </summary>
        private NetworkSession(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots, bool isHost, INetworkTransport transport = null)
        {
            // Register message types (can be moved to static constructor)
            NetworkMessageRegistry.Register<PlayerMoveMessage>(1);

            this.sessionType = sessionType;
            this.maxGamers = maxGamers;
            this.privateGamerSlots = privateGamerSlots;
            this.isHost = isHost;
            this.sessionId = Guid.NewGuid().ToString();
            this.sessionState = NetworkSessionState.Creating;

            gamers = new List<NetworkGamer>();
            gamerCollection = new GamerCollection(gamers);
            gamerEndpoints = new Dictionary<string, IPEndPoint>();

            networkTransport = transport ?? new UdpTransport();
            cancellationTokenSource = new CancellationTokenSource();

            // Phase 1: Initialize diagnostics and logging
            diagnostics = new NetworkDiagnostics();
            logger = new ConsoleNetworkLogger();
            connectionMonitor = new ConnectionHealthMonitor();

            // Add local gamer
            var gamerGuid = Guid.NewGuid().ToString();
            var localGamer = new LocalNetworkGamer(this, gamerGuid, isHost, $"{SignedInGamer.Current?.Gamertag ?? "Player"}_{gamerGuid.Substring(0, 8)}");
            this.localGamer = localGamer;
            AddGamer(localGamer);

            // Start receive loop for SystemLink sessions
            if (sessionType == NetworkSessionType.SystemLink)
            {
                // Bind immediately so our endpoint is stable and reachable
                if (!networkTransport.IsBound)
                {
                    try
                    {
                        // Try binding to the well-known game port; fall back to transport default if not UDP.
                        if (networkTransport is UdpTransport udpTransport)
                            udpTransport.Bind(31338);
                        else
                            networkTransport.Bind();
                    }
                    catch
                    {
                        try { networkTransport.Bind(); } catch { }
                    }
                }
                receiveTask = Task.Run(() => ReceiveLoopAsync(cancellationTokenSource.Token));
            }
        }

        // Internal constructor for SystemLink join
        internal NetworkSession(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots, bool isHost, string sessionId, INetworkTransport transport = null)
            : this(sessionType, maxGamers, privateGamerSlots, isHost, transport)
        {
            this.sessionId = sessionId;
            // Don't set state here - let JoinSessionAsync set it to Joining, then acceptance sets it to Lobby
            // The main constructor already starts the receive loop for SystemLink sessions
        }

        /// <summary>
        /// Gets all gamers in the session.
        /// </summary>
        public GamerCollection AllGamers => gamerCollection;

        /// <summary>
        /// Gets local gamers in the session.
        /// </summary>
        public LocalGamerCollection LocalGamers
        {
            get
            {
                var localGamers = gamers.Where(g => g.IsLocal).OfType<LocalNetworkGamer>().ToList();
                return new LocalGamerCollection(localGamers);
            }
        }

        /// <summary>
        /// Gets remote gamers in the session.
        /// </summary>
        public GamerCollection RemoteGamers
        {
            get
            {
                var remoteGamers = gamers.Where(g => !g.IsLocal).ToList();
                return new GamerCollection(remoteGamers);
            }
        }

        /// <summary>
        /// Gets the host gamer.
        /// </summary>
        public NetworkGamer Host => AllGamers.Host;

        /// <summary>
        /// Gets whether this machine is the host.
        /// </summary>
        public bool IsHost => isHost;

        /// <summary>
        /// Gets whether everyone is ready to start the game.
        /// </summary>
        public bool IsEveryoneReady => AllGamers.All(g => g.IsReady);

        /// <summary>
        /// Gets the maximum number of gamers.
        /// </summary>
        public int MaxGamers => maxGamers;

        /// <summary>
        /// Gets the number of private gamer slots.
        /// </summary>
        public int PrivateGamerSlots => privateGamerSlots;

        /// <summary>
        /// Gets the session type.
        /// </summary>
        public NetworkSessionType SessionType => sessionType;

        /// <summary>
        /// Gets the current session state.
        /// </summary>
        public NetworkSessionState SessionState => sessionState;

        /// <summary>
        /// Gets the bytes per second being sent.
        /// </summary>
        public int BytesPerSecondSent
        {
            get
            {
                var uptime = diagnostics?.Uptime.TotalSeconds ?? 0;
                if (uptime <= 0)
                    return 0;
                return (int)(diagnostics.BytesSent / uptime);
            }
        }

        /// <summary>
        /// Gets the bytes per second being received.
        /// </summary>
        public int BytesPerSecondReceived
        {
            get
            {
                var uptime = diagnostics?.Uptime.TotalSeconds ?? 0;
                if (uptime <= 0)
                    return 0;
                return (int)(diagnostics.BytesReceived / uptime);
            }
        }

        /// <summary>
        /// Gets whether the session allows host migration.
        /// </summary>
        public bool AllowHostMigration { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the session allows gamers to join during gameplay. When false,
        /// the host refuses new gamers once <see cref="StartGame"/> has been called (they fail to
        /// join with <see cref="NetworkSessionJoinError.SessionNotJoinable"/>). Defaults to true.
        /// </summary>
        public bool AllowJoinInProgress { get; set; } = true;

        // set when the host answers our join request with a rejection (see JoinSessionAsync)
        internal volatile string joinRejectedReason;
        internal NetworkSessionJoinError joinRejectedError;

        private IDictionary<string, object> sessionProperties = new Dictionary<string, object>();
        /// <summary>
        /// Gets or sets custom session properties.
        /// </summary>
        public IDictionary<string, object> SessionProperties
        {
            get => sessionProperties;
            set
            {
                sessionProperties = value ?? throw new ArgumentNullException(nameof(value));

                // Automatically broadcast changes if this machine is the host
                if (IsHost)
                {
                    BroadcastSessionProperties();
                }
            }
        }

        // Simulation properties for testing network conditions
        private TimeSpan simulatedLatency = TimeSpan.Zero;
        private float simulatedPacketLoss = 0.0f;

        /// <summary>
        /// Gets or sets the simulated network latency for testing purposes.
        /// </summary>
        public TimeSpan SimulatedLatency
        {
            get => simulatedLatency;
            set => simulatedLatency = value;
        }

        /// <summary>
        /// Gets or sets the simulated packet loss percentage for testing purposes.
        /// </summary>
        public float SimulatedPacketLoss
        {
            get => simulatedPacketLoss;
            set => simulatedPacketLoss = Math.Max(0.0f, Math.Min(1.0f, value));
        }

        /// <summary>
        /// Cancels all ongoing async operations for this session.
        /// </summary>
        public void Cancel()
        {
            cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Asynchronously creates a new network session.
        /// </summary>
        /// <summary>
        /// Creates a SystemLink (UDP) session directly, bypassing factory delegation.
        /// Intended for use by <see cref="INetworkSessionProvider"/> implementations that
        /// want to layer additional discovery (e.g. Steam lobbies) on top of UDP transport.
        /// </summary>
        public static Task<NetworkSession> CreateSystemLinkSessionAsync(
            NetworkSessionType sessionType,
            int maxGamers,
            int privateGamerSlots,
            CancellationToken cancellationToken = default)
        {
            return CreateSystemLinkSessionAsync(sessionType, maxGamers, privateGamerSlots, transport: null, advertiseOnLan: true, cancellationToken);
        }

        internal static Task<NetworkSession> CreateSystemLinkSessionAsync(
            NetworkSessionType sessionType,
            int maxGamers,
            int privateGamerSlots,
            INetworkTransport transport,
            bool advertiseOnLan,
            CancellationToken cancellationToken = default)
        {
            var session = new NetworkSession(sessionType, maxGamers, privateGamerSlots, isHost: true, transport);
            session.sessionState = NetworkSessionState.Lobby;
            session.StartConnectionMonitoring();
            if (advertiseOnLan)
                _ = SystemLinkSessionManager.AdvertiseSessionAsync(session, session.cancellationTokenSource.Token);
            return Task.FromResult(session);
        }

        /// <summary>
        /// Joins a SystemLink (UDP) session directly, bypassing factory delegation.
        /// Intended for use by <see cref="INetworkSessionProvider"/> implementations.
        /// </summary>
        public static async Task<NetworkSession> JoinSystemLinkSessionAsync(
            AvailableNetworkSession availableSession,
            CancellationToken cancellationToken = default)
        {
            return await JoinSystemLinkSessionAsync(availableSession, transport: null, cancellationToken);
        }

        internal static async Task<NetworkSession> JoinSystemLinkSessionAsync(
            AvailableNetworkSession availableSession,
            INetworkTransport transport,
            CancellationToken cancellationToken = default)
        {
            var joinedSession = await SystemLinkSessionManager.JoinSessionAsync(availableSession, transport, cancellationToken);
            if (joinedSession == null)
                throw new NetworkSessionJoinException(NetworkSessionJoinError.SessionNotFound);
            return joinedSession;
        }

        public static async Task<NetworkSession> CreateAsync(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots, IDictionary<string, object> sessionProperties, CancellationToken cancellationToken = default)
        {
            if (maxLocalGamers < 1 || maxLocalGamers > 4)
                throw new ArgumentOutOfRangeException(nameof(maxLocalGamers));
            if (privateGamerSlots < 0 || privateGamerSlots > maxGamers)
                throw new ArgumentOutOfRangeException(nameof(privateGamerSlots));

            // Delegate to the configured back-end when one is set.
            if (NetworkServiceProvider.IsConfigured && NetworkServiceProvider.SessionFactory is INetworkSessionProvider provider)
                return await provider.CreateSessionAsync(sessionType, maxLocalGamers, maxGamers, privateGamerSlots, sessionProperties, cancellationToken);

            NetworkSession session = null;
            switch (sessionType)
            {
                case NetworkSessionType.Local:
                    // Local session: in-memory only
                    await Task.Delay(5, cancellationToken);
                    session = new NetworkSession(sessionType, maxGamers, privateGamerSlots, true);
                    session.sessionState = NetworkSessionState.Lobby;
                    // Register in local session list for FindAsync
                    LocalSessionRegistry.RegisterSession(session);
                    break;
                case NetworkSessionType.SystemLink:
                    // SystemLink: start UDP listener and broadcast session
                    session = new NetworkSession(sessionType, maxGamers, privateGamerSlots, true);
                    session.sessionState = NetworkSessionState.Lobby;
                    // Phase 1: Start connection monitoring for SystemLink sessions
                    session.StartConnectionMonitoring();
                    session.logger?.LogInfo($"Started connection monitoring for session {session.sessionId}");
                    // Use the session's own cancellation token, not the CreateAsync parameter
                    _ = SystemLinkSessionManager.AdvertiseSessionAsync(session, session.cancellationTokenSource.Token); // Fire-and-forget
                    break;
                default:
                    // Not implemented
                    throw new NotSupportedException($"SessionType {sessionType} not supported yet.");
            }
            return session;
        }

        /// <summary>
        /// Synchronous wrapper for CreateAsync (for XNA compatibility).
        /// </summary>
        public static NetworkSession Create(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots, IDictionary<string, object> sessionProperties)
        {
            return CreateAsync(sessionType, maxLocalGamers, maxGamers, privateGamerSlots, sessionProperties).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously finds available network sessions.
        /// </summary>
        public static async Task<AvailableNetworkSessionCollection> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, IDictionary<string, object> sessionProperties, CancellationToken cancellationToken = default)
        {
            // Delegate to the configured back-end when one is set.
            if (NetworkServiceProvider.IsConfigured && NetworkServiceProvider.SessionFactory is INetworkSessionProvider provider)
                return await provider.FindSessionsAsync(sessionType, maxLocalGamers, sessionProperties, cancellationToken);

            switch (sessionType)
            {
                case NetworkSessionType.Local:
                    await Task.Delay(5, cancellationToken);
                    // Return sessions in local registry
                    var localSessions = LocalSessionRegistry.FindSessions(maxLocalGamers).ToList();
                    return new AvailableNetworkSessionCollection(localSessions);
                case NetworkSessionType.SystemLink:
                    // Discover sessions via UDP broadcast
                    var systemLinkSessions = (await SystemLinkSessionManager.DiscoverSessionsAsync(maxLocalGamers, cancellationToken)).ToList();
                    return new AvailableNetworkSessionCollection(systemLinkSessions);
                default:
                    throw new NotSupportedException($"SessionType {sessionType} not supported yet.");
            }
        }

        /// <summary>
        /// Synchronous wrapper for FindAsync (for XNA compatibility).
        /// </summary>
        public static AvailableNetworkSessionCollection Find(NetworkSessionType sessionType, int maxLocalGamers, IDictionary<string, object> sessionProperties)
        {
            return FindAsync(sessionType, maxLocalGamers, sessionProperties).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously joins an available network session.
        /// </summary>
        public static async Task<NetworkSession> JoinAsync(AvailableNetworkSession availableSession, CancellationToken cancellationToken = default)
        {
            // Delegate to the configured back-end when one is set.
            if (NetworkServiceProvider.IsConfigured && NetworkServiceProvider.SessionFactory is INetworkSessionProvider provider)
                return await provider.JoinSessionAsync(availableSession, cancellationToken);

            switch (availableSession.SessionType)
            {
                case NetworkSessionType.Local:
                    // Attach to local session
                    var localSession = LocalSessionRegistry.GetSessionById(availableSession.SessionId);
                    if (localSession == null)
                        throw new NetworkSessionJoinException(NetworkSessionJoinError.SessionNotFound);

                    // Add local gamer
                    var localGamer = new LocalNetworkGamer(localSession, Guid.NewGuid().ToString(), false, SignedInGamer.Current?.Gamertag ?? "Player");
                    localSession.AddGamer(localGamer);
                    return localSession;
                case NetworkSessionType.SystemLink:
                    // Connect to host via network
                    var joinedSession = await SystemLinkSessionManager.JoinSessionAsync(availableSession, cancellationToken);
                    if (joinedSession == null)
                        throw new NetworkSessionJoinException(NetworkSessionJoinError.SessionNotFound);

                    return joinedSession;
                default:
                    throw new NotSupportedException($"SessionType {availableSession.SessionType} not supported yet.");
            }
        }

        public static Task<NetworkSession> JoinInvitedAsync(IEnumerable<SignedInGamer> localGamers, object state = null, CancellationToken cancellationToken = default)
        {
            if (localGamers == null || !localGamers.Any())
                throw new ArgumentException("At least one local gamer must be provided.", nameof(localGamers));

            // Simulate invite acceptance logic
            var inviteAcceptedEventArgs = new InviteAcceptedEventArgs(localGamers.First(), true);
            InviteAccepted?.Invoke(null, inviteAcceptedEventArgs);

            if (!inviteAcceptedEventArgs.IsSignedInGamer)
                throw new InvalidOperationException("The gamer is not signed in.");

            // Simulate finding the session associated with the invite
            var availableSession = new AvailableNetworkSession(
                sessionName: "InvitedSession",
                hostGamertag: "HostPlayer",
                currentGamerCount: 1,
                openPublicGamerSlots: 3,
                openPrivateGamerSlots: 1,
                sessionType: NetworkSessionType.PlayerMatch,
                sessionProperties: new Dictionary<string, object>(),
                sessionId: Guid.NewGuid().ToString()
            );

            // Join the session
            var joinedSession = SystemLinkSessionManager.JoinSessionAsync(availableSession, cancellationToken);
            return joinedSession;
        }

        /// <summary>
        /// Creates a new network session synchronously with default properties.
        /// </summary>
        public static NetworkSession Create(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers)
        {
            return CreateAsync(sessionType, maxLocalGamers, maxGamers, 0, new Dictionary<string, object>()).GetAwaiter().GetResult();
        }

        /// <summary>

        /// <summary>
        /// Finds available network sessions synchronously with default properties.
        /// </summary>
        public static AvailableNetworkSessionCollection Find(NetworkSessionType sessionType, int maxLocalGamers)
        {
            return FindAsync(sessionType, maxLocalGamers, new Dictionary<string, object>()).GetAwaiter().GetResult();
        }

        /// <summary>

        /// <summary>
        /// Joins an available network session synchronously.
        /// </summary>
        public static NetworkSession Join(AvailableNetworkSession availableSession)
        {
            return JoinAsync(availableSession).GetAwaiter().GetResult();
        }

        public static NetworkSession JoinInvited(IEnumerable<SignedInGamer> localGamers, object state = null)
        {
            return JoinInvitedAsync(localGamers, state).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the network session.
        /// </summary>
        public void Update()
        {
            if (disposed)
                return;

            // Process any pending in-memory messages only for Local sessions
            if (sessionType == NetworkSessionType.Local)
                ProcessIncomingMessages();

            DrainEvents();
        }

        // Runs what the network threads queued, on the caller's thread. A handler may dispose the
        // session, so stop as soon as that happens.
        private void DrainEvents()
        {
            while (!disposed && pendingEvents.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    logger?.LogError("A session event handler threw", ex);
                }
            }
        }

        // Queues an event, game data delivery included, for Update(); or runs it now in Immediate mode
        private void Raise(Action action)
        {
            if (EventDispatchMode == EventDispatchMode.Immediate)
                action();
            else
                pendingEvents.Enqueue(action);
        }

        /// <summary>
        /// Sends data to all gamers in the session.
        /// </summary>
        public void SendToAll(PacketWriter writer, SendDataOptions options)
        {
            SendToAll(writer, options, localGamer);
        }

        /// <summary>
        /// Sends data to all gamers except the specified sender.
        /// </summary>
        public void SendToAll(PacketWriter writer, SendDataOptions options, NetworkGamer sender)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            int payloadLength;
            var payload = writer.RentData(out payloadLength);
            try
            {
                lock (lockObject)
                {
                    foreach (var gamer in gamers)
                    {
                        if (gamer != sender && !gamer.IsLocal)
                        {
                            SendDataToGamer(gamer, payload, payloadLength, options);
                        }
                    }
                }
            }
            finally
            {
                PacketWriter.ReturnRentedData(payload);
            }
        }

        /// <summary>
        /// Phase 2: Broadcasts session state change to all gamers.
        /// </summary>
        private void BroadcastSessionState(NetworkSessionState newState, string reason)
        {
            if (!IsHost)
                return; // Only host broadcasts state changes

            logger?.LogInfo($"Broadcasting session state change: {newState} - {reason}");

            var stateMessage = new SessionStateMessage
            {
                NewState = newState,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Reason = reason
            };

            var writer = new PacketWriter();
            stateMessage.Serialize(writer);
            SendMessageToAll(writer, SendDataOptions.Reliable, null);
        }

        /// <summary>
        /// Starts the game.
        /// </summary>
        public void StartGame()
        {
            if (sessionState == NetworkSessionState.Lobby)
            {
                sessionState = NetworkSessionState.Playing;
                OnGameStarted();

                // Phase 2: Broadcast state change
                if (IsHost)
                {
                    BroadcastSessionState(NetworkSessionState.Playing, "Host started game");
                }
            }
        }

        /// <summary>
        /// Ends the game and returns to lobby.
        /// </summary>
        public void EndGame()
        {
            if (sessionState == NetworkSessionState.Playing)
            {
                sessionState = NetworkSessionState.Lobby;
                OnGameEnded();

                // Phase 2: Broadcast state change
                if (IsHost)
                {
                    BroadcastSessionState(NetworkSessionState.Lobby, "Game ended");
                }
            }
        }

        /// <summary>
        /// Notifies when a gamer's readiness changes.
        /// </summary>
        internal void NotifyReadinessChanged(NetworkGamer gamer)
        {
            if (gamer == null) return;

            // Build message once
            var msg = new ReadinessUpdateMessage { GamerId = gamer.Id, IsReady = gamer.IsReady };
            var writer = new PacketWriter();
            msg.Serialize(writer);

            if (IsHost)
            {
                // Host applies locally and broadcasts to all others
                ApplyReadinessUpdate(msg);
                SendMessageToAll(writer, SendDataOptions.Reliable, gamer);
            }
            else
            {
                // Client sends to host only
                var host = gamers.FirstOrDefault(g => g.IsHost);
                if (host != null)
                {
                    SendMessageToGamer(host, writer.GetData(), SendDataOptions.Reliable);
                }
            }
        }

        private void ApplyReadinessUpdate(ReadinessUpdateMessage update)
        {
            if (update == null) return;
            var target = gamers.FirstOrDefault(g => g.Id == update.GamerId);
            if (target != null)
            {
                // Set without re-notifying session for remote gamers
                target.IsReady = update.IsReady;
            }
        }

        private void OnMessageReceived(MessageReceivedEventArgs e)
        {
            if (e.Message is JoinRequestMessage joinRequest)
            {
                // Phase 1: Check protocol version
                if (joinRequest.ProtocolVersion != JoinRequestMessage.CURRENT_PROTOCOL_VERSION)
                {
                    logger?.LogWarning($"Join request from {joinRequest.Gamertag} has protocol version {joinRequest.ProtocolVersion}, expected {JoinRequestMessage.CURRENT_PROTOCOL_VERSION}");
                    
                    var rejection = new JoinRejectedMessage
                    {
                        ErrorCode = NetworkSessionJoinError.ProtocolVersionMismatch,
                        Reason = $"Protocol version mismatch. Host: v{JoinRequestMessage.CURRENT_PROTOCOL_VERSION}, Client: v{joinRequest.ProtocolVersion}"
                    };
                    var rejectWriter = new PacketWriter();
                    rejection.Serialize(rejectWriter);
                    networkTransport.Send(rejectWriter.GetData(), e.RemoteEndPoint);
                    return;
                }

                // The host asked for no new players once the game has started (AllowJoinInProgress).
                // Somebody who is already in and merely repeats their request (a lost acceptance)
                // is still let through.
                if (!AllowJoinInProgress && sessionState == NetworkSessionState.Playing)
                {
                    bool alreadyJoined;
                    lock (lockObject)
                        alreadyJoined = gamers.Any(g => g.Id == joinRequest.GamerId);

                    if (!alreadyJoined)
                    {
                        logger?.LogWarning($"Join request from {joinRequest.Gamertag} rejected: the game has already started");

                        var rejection = new JoinRejectedMessage
                        {
                            ErrorCode = NetworkSessionJoinError.SessionNotJoinable,
                            Reason = "The game has already started"
                        };
                        var rejectWriter = new PacketWriter();
                        rejection.Serialize(rejectWriter);
                        networkTransport.Send(rejectWriter.GetData(), e.RemoteEndPoint);
                        return;
                    }
                }

                // Phase 1: Check if session is full
                if (AllGamers.Count >= MaxGamers)
                {
                    logger?.LogWarning($"Join request from {joinRequest.Gamertag} rejected: session is full ({AllGamers.Count}/{MaxGamers})");
                    
                    var rejection = new JoinRejectedMessage
                    {
                        ErrorCode = NetworkSessionJoinError.SessionFull,
                        Reason = "Session is full"
                    };
                    var rejectWriter = new PacketWriter();
                    rejection.Serialize(rejectWriter);
                    networkTransport.Send(rejectWriter.GetData(), e.RemoteEndPoint);
                    return;
                }

                // Phase 1 FIX: Check if gamer already exists (from previous retry)
                // CRITICAL: Keep the check and add in the same lock to prevent race conditions
                NetworkGamer existingGamer = null;
                bool isNewGamer = false;
                
                logger?.LogInfo($"Processing join request from {joinRequest.Gamertag} (ID: {joinRequest.GamerId}), gamer count: {AllGamers.Count}");
                
                lock (lockObject)
                {
                    existingGamer = gamers.FirstOrDefault(g => g.Id == joinRequest.GamerId);
                    
                    if (existingGamer == null)
                    {
                        // New gamer - create and add atomically while holding lock
                        logger?.LogInfo($"Accepting join request from {joinRequest.Gamertag} (ID: {joinRequest.GamerId})");
                        var newGamer = new NetworkGamer(this, joinRequest.GamerId, isLocal: false, isHost: false, gamertag: joinRequest.Gamertag);
                        gamers.Add(newGamer);
                        existingGamer = newGamer;
                        isNewGamer = true;
                    }
                    else
                    {
                        // Gamer already joined (this is a retry) - just resend acceptance
                        logger?.LogInfo($"Join request from {joinRequest.Gamertag} is a retry (gamer already exists), resending acceptance");
                    }
                }
                
                // Register endpoint outside lock (uses its own lock internally)
                RegisterGamerEndpoint(existingGamer, e.RemoteEndPoint);
                
                // Notify connection monitor for new gamers only
                if (isNewGamer)
                {
                    connectionMonitor?.OnGamerJoined(existingGamer);
                    OnGamerJoined(existingGamer);
                }

                // Send JoinAcceptedMessage back to the sender (always send, even on retry)
                var joinAccepted = new JoinAcceptedMessage
                {
                    SessionId = sessionId,
                    HostGamerId = Host.Id,
                    HostGamertag = Host.Gamertag
                };
                var writer = new PacketWriter();
                joinAccepted.Serialize(writer);
                networkTransport.Send(writer.GetData(), e.RemoteEndPoint);
                logger?.LogInfo($"Sent JoinAcceptedMessage to {joinRequest.Gamertag}");
            }
            else if (e.Message is JoinAcceptedMessage joinAccepted)
            {
                // Client receives confirmation from host; ensure host is present and mapped
                if (!IsHost)
                {
                    logger?.LogInfo($"Received JoinAcceptedMessage from host {joinAccepted.HostGamertag}");
                    
                    var existingHost = gamers.FirstOrDefault(g => g.IsHost);
                    if (existingHost != null && existingHost.Id != joinAccepted.HostGamerId)
                    {
                        // Remove synthetic host
                        RemoveGamer(existingHost);
                        existingHost = null;
                    }

                    var host = existingHost ?? new NetworkGamer(this, joinAccepted.HostGamerId, isLocal: false, isHost: true, gamertag: joinAccepted.HostGamertag);
                    if (existingHost == null)
                        AddGamer(host);
                    RegisterGamerEndpoint(host, e.RemoteEndPoint);
                    
                    // Phase 1: Transition to Lobby state when join is accepted
                    sessionState = NetworkSessionState.Lobby;
                    logger?.LogInfo("Successfully joined session, now in Lobby state");
                }
            }
            else if (e.Message is JoinRejectedMessage joinRejected)
            {
                // Phase 1: Handle join rejection
                logger?.LogError($"Join rejected: {joinRejected.Reason} (Error: {joinRejected.ErrorCode})");

                // Session state remains in Joining; JoinSessionAsync notices the rejection and
                // fails with the host's reason instead of waiting for a timeout
                if (sessionState == NetworkSessionState.Joining)
                {
                    joinRejectedError = joinRejected.ErrorCode;
                    joinRejectedReason = joinRejected.Reason;       // written last: readers check this one
                }
            }
            else if (e.Message is PlayerMoveMessage moveMessage)
            {
                // Identify sender by endpoint mapping
                NetworkGamer sourceGamer = null;
                lock (lockObject)
                {
                    if (e.RemoteEndPoint != null)
                        sourceGamer = gamers.FirstOrDefault(g => gamerEndpoints.TryGetValue(g.Id, out var ep) && ep.Equals(e.RemoteEndPoint));
                }

                if (sourceGamer != null)
                {
                    // Update position (mock) and broadcast to others
                    Debug.WriteLine($"Player {sourceGamer.Gamertag} moved to ({moveMessage.X}, {moveMessage.Y}, {moveMessage.Z})");

                    // Only the host rebroadcasts to others
                    if (IsHost)
                    {
                        var writer = new PacketWriter();
                        moveMessage.Serialize(writer);
                        SendMessageToAll(writer, SendDataOptions.Reliable, sourceGamer);
                    }
                }
            }
            else if (e.Message is ReadinessUpdateMessage readiness)
            {
                // Apply update and, if host, rebroadcast to others
                ApplyReadinessUpdate(readiness);
                if (IsHost)
                {
                    var writer = new PacketWriter();
                    readiness.Serialize(writer);

                    // Determine sender gamer from endpoint mapping (if available)
                    NetworkGamer sourceGamer = null;
                    lock (lockObject)
                    {
                        if (e.RemoteEndPoint != null)
                            sourceGamer = gamers.FirstOrDefault(g => gamerEndpoints.TryGetValue(g.Id, out var ep) && ep.Equals(e.RemoteEndPoint));
                    }
                    SendMessageToAll(writer, SendDataOptions.Reliable, sourceGamer);
                }
            }
            else if (e.Message is HeartbeatMessage heartbeat)
            {
                // Phase 1: Handle heartbeat from remote gamer
                connectionMonitor?.OnHeartbeatReceived(heartbeat.GamerId, heartbeat);
            }
            else if (e.Message is HeartbeatReplyMessage heartbeatReply)
            {
                // Phase 1: Handle heartbeat reply for RTT calculation
                connectionMonitor?.OnHeartbeatReplyReceived(heartbeatReply.GamerId, heartbeatReply);
                
                // Update diagnostics
                var rtt = TimeSpan.FromMilliseconds(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - heartbeatReply.RequestTimestamp);
                diagnostics?.RecordRtt(rtt);
            }
            else if (e.Message is GamerLeavingMessage leaveMessage)
            {
                // Phase 2: Handle graceful leave - immediate gamer removal
                logger?.LogInfo($"Received leave notification from gamer {leaveMessage.GamerId}: {leaveMessage.Reason}");
                
                NetworkGamer leavingGamer = null;
                lock (lockObject)
                {
                    leavingGamer = gamers.FirstOrDefault(g => g.Id == leaveMessage.GamerId);
                }
                
                if (leavingGamer != null)
                {
                    logger?.LogInfo($"Removing {leavingGamer.Gamertag} from session (graceful leave)");
                    // EvictGamer also ends the session on a client whose host is the one leaving
                    EvictGamer(leavingGamer);
                }
                else
                {
                    logger?.LogWarning($"Received leave notification for unknown gamer {leaveMessage.GamerId}");
                }
            }
            else if (e.Message is SessionStateMessage stateMessage)
            {
                // Phase 2: Handle session state synchronization from host
                if (!IsHost)
                {
                    logger?.LogInfo($"Received session state update: {stateMessage.NewState} - {stateMessage.Reason}");
                    
                    // Update local session state to match host
                    var previousState = sessionState;
                    sessionState = stateMessage.NewState;
                    
                    // Raise appropriate events based on state transition
                    if (previousState == NetworkSessionState.Lobby && stateMessage.NewState == NetworkSessionState.Playing)
                    {
                        OnGameStarted();
                    }
                    else if (previousState == NetworkSessionState.Playing && stateMessage.NewState == NetworkSessionState.Lobby)
                    {
                        OnGameEnded();
                    }
                    
                    logger?.LogInfo($"Session state changed from {previousState} to {sessionState}");
                }
            }

            // Raise the MessageReceived event
            Raise(() => MessageReceived?.Invoke(this, e));
        }

        private void OnGameStarted()
        {
            Raise(() => GameStarted?.Invoke(this, new GameStartedEventArgs()));
        }

        private void OnGameEnded()
        {
            Raise(() => GameEnded?.Invoke(this, new GameEndedEventArgs()));
        }

        private void OnGamerJoined(NetworkGamer gamer)
        {
            Raise(() => gamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer)));
        }

        private void OnGamerLeft(NetworkGamer gamer)
        {
            Raise(() => GamerLeft?.Invoke(this, new GamerLeftEventArgs(gamer)));
        }

        // The session ends once: whichever reason gets here first is the one reported.
        // A session the caller disposes raises it on their own thread, so it is never queued.
        private void OnSessionEnded(NetworkSessionEndReason reason, bool callerThread = false)
        {
            sessionState = NetworkSessionState.Ended;
            if (Interlocked.Exchange(ref sessionEndedRaised, 1) != 0)
                return;

            void Notify() => SessionEnded?.Invoke(this, new NetworkSessionEndedEventArgs(reason));
            if (callerThread)
                Notify();
            else
                Raise(Notify);
        }

        /// <summary>
        /// Phase 2: Gracefully leaves the session with immediate notification to all gamers.
        /// Sends GamerLeavingMessage to all remote gamers before disposing.
        /// </summary>
        /// <param name="reason">Optional reason for leaving (e.g., "User quit").</param>
        public void Leave(string reason = "User left session")
        {
            if (disposed)
                return;

            logger?.LogInfo($"Leaving session: {reason}");

            // Send leave notification to all remote gamers
            var localGamer = LocalGamers.FirstOrDefault();
            if (localGamer != null && networkTransport != null && networkTransport.IsBound)
            {
                var leaveMessage = new GamerLeavingMessage
                {
                    GamerId = localGamer.Id,
                    Reason = reason
                };

                var writer = new PacketWriter();
                leaveMessage.Serialize(writer);
                var data = writer.GetData();

                // Send to all remote gamers
                lock (lockObject)
                {
                    foreach (var gamer in gamers.Where(g => !g.IsLocal))
                    {
                        try
                        {
                            if (gamerEndpoints.TryGetValue(gamer.Id, out var endpoint))
                            {
                                networkTransport.Send(data, endpoint);
                                logger?.LogInfo($"Sent leave notification to {gamer.Gamertag}");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogError($"Failed to send leave notification to {gamer.Gamertag}", ex);
                        }
                    }
                }

            }

            // Now dispose normally
            Dispose();
        }

        /// <summary>
        /// Disposes the network session.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true; // Set BEFORE cleanup to prevent re-entry
                
                try { cancellationTokenSource?.Cancel(); } catch { /* Already disposed */ }
                
                // Phase 1: Stop connection monitoring
                connectionMonitor?.StopMonitoring();
                StopReliableChannels();
                
                // Dispose transport first to unblock ReceiveAsync
                networkTransport?.Dispose();
                
                try { receiveTask?.Wait(1000); } catch { /* ignore */ }
                
                try { cancellationTokenSource?.Dispose(); } catch { /* Already disposed */ }
                
                // Raise SessionEnded event (may cause re-entrant Dispose call, now safe)
                OnSessionEnded(NetworkSessionEndReason.ClientSignedOut, callerThread: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true; // Set BEFORE cleanup to prevent re-entry
                
                try { cancellationTokenSource?.Cancel(); } catch { /* Already disposed */ }
                
                // Phase 1: Stop connection monitoring
                connectionMonitor?.StopMonitoring();
                StopReliableChannels();
                
                // Dispose transport first to unblock any pending ReceiveAsync
                if (networkTransport is IAsyncDisposable asyncTransport)
                    await asyncTransport.DisposeAsync();
                else
                    networkTransport?.Dispose();
                    
                if (receiveTask != null)
                {
                    await Task.WhenAny(receiveTask, Task.Delay(1000));
                }
                
                try { cancellationTokenSource?.Dispose(); } catch { /* Already disposed */ }
                
                // Raise SessionEnded event (may cause re-entrant Dispose call, now safe)
                OnSessionEnded(NetworkSessionEndReason.ClientSignedOut, callerThread: true);
            }
        }

        internal byte[] SerializeSessionPropertiesBinary()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(SessionProperties.Count);
                foreach (var kvp in SessionProperties)
                {
                    writer.Write(kvp.Key);
                    // Write type info and value
                    if (kvp.Value is int i)
                    {
                        writer.Write((byte)1); // type marker
                        writer.Write(i);
                    }
                    else if (kvp.Value is bool b)
                    {
                        writer.Write((byte)2);
                        writer.Write(b);
                    }
                    else if (kvp.Value is string s)
                    {
                        writer.Write((byte)3);
                        writer.Write(s ?? "");
                    }
                    // Add more types as needed
                    else
                    {
                        writer.Write((byte)255); // unknown type
                        writer.Write(kvp.Value?.ToString() ?? "");
                    }
                }
                return ms.ToArray();
            }
        }

        internal void DeserializeSessionPropertiesBinary(byte[] data)
        {
            var parsed = DeserializeSessionPropertiesStatic(data);
            SessionProperties.Clear();
            foreach (var kvp in parsed)
                SessionProperties[kvp.Key] = kvp.Value;
        }

        internal static Dictionary<string, object> DeserializeSessionPropertiesStatic(byte[] data)
        {
            var result = new Dictionary<string, object>();
            if (data == null || data.Length < 4)
                return result;
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString();
                    byte type = reader.ReadByte();
                    object value = null;
                    switch (type)
                    {
                        case 1: value = reader.ReadInt32(); break;
                        case 2: value = reader.ReadBoolean(); break;
                        case 3: value = reader.ReadString(); break;
                        default: value = reader.ReadString(); break;
                    }
                    result[key] = value;
                }
            }
            return result;
        }

        private void BroadcastSessionProperties()
        {
            if (sessionType == NetworkSessionType.Local)
            {
                var writer = new PacketWriter();
                writer.Write("SessionPropertiesUpdate");
                writer.Write(SerializeSessionPropertiesBinary());
                SendToAll(writer, SendDataOptions.Reliable);
            }
        }

        private void ProcessIncomingMessages()
        {
            foreach (var gamer in gamers)
            {
                while (gamer.IsDataAvailable)
                {
                    var reader = new PacketReader();
                    gamer.ReceiveData(reader, out var sender);

                    var messageType = reader.ReadString();
                    if (messageType == "SessionPropertiesUpdate")
                    {
                        var propertiesData = reader.ReadBytes();
                        DeserializeSessionPropertiesBinary(propertiesData);
                    }
                }
            }
        }
        private void NotifyPendingGamers()
        {
            if (isGamerJoinedSubscribed && sessionState == NetworkSessionState.Lobby)
            {
                // Notify all pending gamers that they can join
                foreach (var gamer in gamers/*.Where(g => !g.IsLocal && !g.IsReady)*/)
                {
                    gamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer));
                }
            }
        }

        internal void AcceptGamer(NetworkGamer gamer)
        {
            AddGamer(gamer);
        }

        internal void EvictGamer(NetworkGamer gamer)
        {
            if (gamer == null)
            {
                return;
            }

            var wasHost = gamer.IsHost;
            var wasLocal = gamer.IsLocal;

            RemoveGamer(gamer);

            if (sessionState == NetworkSessionState.Ended)
            {
                return;
            }

            // Host migration is currently not implemented. If a non-local host is removed
            // (disconnect/timeout), non-host clients end the session deterministically.
            if (wasHost && !wasLocal && !IsHost)
            {
                if (AllowHostMigration)
                {
                    logger?.LogWarning("AllowHostMigration is enabled, but host migration is not implemented. Ending session with HostEndedSession.");
                }

                OnSessionEnded(NetworkSessionEndReason.HostEndedSession);
            }
        }

        internal void StartConnectionMonitoring()
        {
            connectionMonitor?.StartMonitoring(this);
        }

        internal void DispatchIncomingMessage(MessageReceivedEventArgs e)
        {
            OnMessageReceived(e);
        }

        private void AddGamer(NetworkGamer gamer)
        {
            if (gamer == null) return;
            lock (lockObject)
            {
                gamers.Add(gamer);
            }
            OnGamerJoined(gamer);
        }

        private void RemoveGamer(NetworkGamer gamer)
        {
            if (gamer == null) return;
            lock (lockObject)
            {
                gamers.Remove(gamer);
                gamerEndpoints.Remove(gamer.Id);
            }
            lock (reliableLock)
                reliableChannels.Remove(gamer.Id);
            // Phase 1: Notify connection monitor
            connectionMonitor?.OnGamerLeft(gamer);
            OnGamerLeft(gamer);
        }

        /// <summary>
        /// Sends data to a specific gamer.
        /// </summary>
        internal void SendDataToGamer(NetworkGamer gamer, PacketWriter writer, SendDataOptions options)
        {
            if (gamer == null || writer == null) return;

            int length;
            var rented = writer.RentData(out length);
            try
            {
                SendDataToGamer(gamer, rented, length, options);
            }
            finally
            {
                PacketWriter.ReturnRentedData(rented);
            }
        }

        internal void SendDataToGamer(NetworkGamer gamer, byte[] data, SendDataOptions options)
        {
            SendDataToGamer(gamer, data, data?.Length ?? 0, options);
        }

        /// <summary>
        /// Sends game data (what the game passed to SendData) to a gamer. The framework never looks inside it.
        /// </summary>
        internal void SendDataToGamer(NetworkGamer gamer, byte[] data, int dataLength, SendDataOptions options)
        {
            SendPayload(gamer, PayloadKind.Application, data, dataLength, options);
        }

        /// <summary>
        /// Sends a serialized INetworkMessage (the framework's own messages, and registered custom ones) to a gamer.
        /// </summary>
        internal void SendMessageToGamer(NetworkGamer gamer, byte[] data, SendDataOptions options)
        {
            SendPayload(gamer, PayloadKind.Message, data, data?.Length ?? 0, options);
        }

        /// <summary>
        /// Sends a serialized INetworkMessage to every remote gamer except one.
        /// </summary>
        internal void SendMessageToAll(PacketWriter writer, SendDataOptions options, NetworkGamer except)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            int length;
            byte[] data = writer.RentData(out length);
            try
            {
                NetworkGamer[] recipients;
                lock (lockObject)
                    recipients = gamers.Where(g => g != except && !g.IsLocal).ToArray();

                foreach (NetworkGamer gamer in recipients)
                    SendPayload(gamer, PayloadKind.Message, data, length, options);
            }
            finally
            {
                PacketWriter.ReturnRentedData(data);
            }
        }

        // The payload starts with its kind, so game data and framework messages can never be mistaken
        // for each other, whatever the first byte of the game data is.
        private void SendPayload(NetworkGamer gamer, byte kind, byte[] data, int dataLength, SendDataOptions options)
        {
            if (gamer == null || data == null) return;

            byte[] payload = new byte[dataLength + 1];
            payload[0] = kind;
            Buffer.BlockCopy(data, 0, payload, 1, dataLength);

            // UDP gives no delivery guarantees, so the data goes through the gamer's channel, which
            // resends reliable data until it is acknowledged and delivers it once and in order
            ReliableChannel channel = GetReliableChannel(gamer);
            if ((options & SendDataOptions.Reliable) != 0)
                channel.SendReliable(payload, payload.Length, ReliableNow);
            else if ((options & SendDataOptions.InOrder) != 0)
                channel.SendSequenced(payload, payload.Length);
            else
                channel.SendUnreliable(payload, payload.Length);
        }

        // sends a datagram as it is, to the endpoint of the gamer with this id
        private void SendRawToGamer(string gamerId, byte[] data)
        {
            IPEndPoint endpoint;
            lock (lockObject)
            {
                if (!gamerEndpoints.TryGetValue(gamerId, out endpoint))
                    return;
            }

            try
            {
                networkTransport.Send(data, data.Length, endpoint);
                // Phase 1: Record sent packet in diagnostics
                diagnostics?.RecordPacketSent(data.Length);
            }
            catch (Exception ex)
            {
                // Phase 1: Use logger
                logger?.LogError($"Failed to send data to gamer {gamerId}: {ex.Message}", ex);
            }
        }

        private double ReliableNow => reliableClock.Elapsed.TotalSeconds;

        private ReliableChannel GetReliableChannel(NetworkGamer gamer)
        {
            lock (reliableLock)
            {
                if (!reliableChannels.TryGetValue(gamer.Id, out ReliableChannel channel))
                {
                    string id = gamer.Id;
                    channel = new ReliableChannel(datagram => SendRawToGamer(id, datagram));
                    channel.Faulted += reason => OnChannelFaulted(id, reason);
                    reliableChannels[id] = channel;

                    // resends and acknowledgements need a clock; start it with the first channel
                    if (reliableTimer == null && !disposed)
                        reliableTimer = new Timer(_ => TickReliableChannels(), null,
                            ReliableTickMilliseconds, ReliableTickMilliseconds);
                }
                return channel;
            }
        }

        // the channel gave up on a gamer (stopped acknowledging, or broke the protocol): it is as good as gone
        private void OnChannelFaulted(string gamerId, string reason)
        {
            NetworkGamer gamer;
            lock (lockObject)
                gamer = gamers.FirstOrDefault(g => g.Id == gamerId);

            if (gamer == null || disposed)
                return;

            logger?.LogWarning($"Removing {gamer.Gamertag}: {reason}");
            EvictGamer(gamer);
        }

        private void TickReliableChannels()
        {
            if (disposed)
                return;

            ReliableChannel[] channels;
            lock (reliableLock)
                channels = reliableChannels.Values.ToArray();

            double now = ReliableNow;
            foreach (ReliableChannel channel in channels)
            {
                try { channel.Update(now); }
                catch (Exception ex) { logger?.LogError($"Reliable channel update failed: {ex.Message}", ex); }
            }
        }

        private void StopReliableChannels()
        {
            lock (reliableLock)
            {
                reliableTimer?.Dispose();
                reliableTimer = null;
                reliableChannels.Clear();
            }
        }

        /// <summary>
        /// How long the oldest reliable data sent to a gamer has been waiting to be acknowledged
        /// (zero when nothing is waiting). A large value means the gamer has stopped answering.
        /// </summary>
        internal TimeSpan GetUnacknowledgedAge(NetworkGamer gamer)
        {
            lock (reliableLock)
            {
                return reliableChannels.TryGetValue(gamer.Id, out ReliableChannel channel)
                    ? TimeSpan.FromSeconds(channel.OldestUnackedAge(ReliableNow))
                    : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Internally associates a remote gamer with an endpoint (used by SystemLink join/handshake).
        /// </summary>
        internal void RegisterGamerEndpoint(NetworkGamer gamer, IPEndPoint endpoint)
        {
            if (gamer == null || endpoint == null) return;
            lock (lockObject)
            {
                gamerEndpoints[gamer.Id] = endpoint;
            }
        }

        /// <summary>
        /// Handles one datagram from the network: unwraps the reliable channel it belongs to, then
        /// treats what comes out as a framework message or as game data, according to its kind.
        /// </summary>
        internal void ProcessIncomingDatagram(byte[] data, IPEndPoint senderEndpoint)
        {
            if (data == null || data.Length == 0)
                return;

            // Identify sender by endpoint mapping
            NetworkGamer senderGamer = null;
            lock (lockObject)
            {
                senderGamer = gamers.FirstOrDefault(g => gamerEndpoints.TryGetValue(g.Id, out var ep) && ep.Equals(senderEndpoint));
            }

            // anything from a gamer shows they are still connected
            if (senderGamer != null)
                connectionMonitor?.OnPeerActivity(senderGamer.Id);

            if (senderGamer != null && ReliableChannel.IsChannelByte(data[0]))
            {
                GetReliableChannel(senderGamer).Receive(data, data.Length, ReliableNow,
                    payload => ProcessPayload(payload, senderEndpoint, senderGamer));
            }
            else
            {
                // the handshake (join request, acceptance, rejection) and the leave notice are sent
                // as they are, outside the channel: a registered message and nothing else
                ProcessMessage(data, 0, senderEndpoint);
            }
        }

        private void ProcessPayload(byte[] payload, IPEndPoint senderEndpoint, NetworkGamer senderGamer)
        {
            if (payload.Length == 0)
                return;

            switch (payload[0])
            {
                case PayloadKind.Application:
                    // game data, handed to the game as it was sent. Enqueue for all local gamers
                    // so they can receive it via ReceiveData()
                    byte[] data = new byte[payload.Length - 1];
                    Buffer.BlockCopy(payload, 1, data, 0, data.Length);
                    Raise(() =>
                    {
                        foreach (var local in LocalGamers)
                            local.EnqueueIncomingPacket(data, senderGamer);
                    });
                    break;

                case PayloadKind.Message:
                    ProcessMessage(payload, 1, senderEndpoint);
                    break;

                default:
                    logger?.LogWarning($"Dropped a packet of unknown kind 0x{payload[0]:X2} from {senderGamer?.Gamertag}");
                    break;
            }
        }

        // a registered message starting at offset: the first byte is its type id and it must use up every byte
        private void ProcessMessage(byte[] data, int offset, IPEndPoint senderEndpoint)
        {
            if (data.Length <= offset)
                return;

            var message = NetworkMessageRegistry.CreateMessage(data[offset]);
            if (message == null)
            {
                logger?.LogWarning($"Dropped a message of unknown type {data[offset]} from {senderEndpoint}");
                return;
            }

            try
            {
                var reader = new PacketReader(data);
                reader.Position = offset + 1;
                message.Deserialize(reader);
                if (reader.BytesRemaining != 0)
                {
                    logger?.LogWarning($"Dropped a {message.GetType().Name} from {senderEndpoint}: {reader.BytesRemaining} unexpected bytes");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"Dropped a malformed {message.GetType().Name} from {senderEndpoint}: {ex.Message}");
                return;
            }

            OnMessageReceived(new MessageReceivedEventArgs(message, senderEndpoint));
        }

        // Modern async receive loop for SystemLink
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Ensure the network transport is bound before receiving data
                    if (!networkTransport.IsBound)
                    {
                        networkTransport.Bind();
                    }

                    var (data, senderEndpoint) = await networkTransport.ReceiveAsync();
                    if (data.Length > 0)
                    {
                        // Phase 1: Record received packet in diagnostics
                        diagnostics?.RecordPacketReceived(data.Length);

                        ProcessIncomingDatagram(data, senderEndpoint);
                    }
                }
                catch (ObjectDisposedException) 
                { 
                    // Session disposed - exit gracefully
                    break; 
                }
                catch (OperationCanceledException)
                {
                    // Cancellation token triggered - exit gracefully
                    break;
                }
                catch (System.Net.Sockets.SocketException sex) when (sex.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted)
                {
                    // Socket operation canceled during shutdown - exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    // Only log unexpected errors
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        logger?.LogError($"ReceiveLoop error: {ex.Message}", ex);
                    }
                    // Exit if cancellation was requested
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
            }
        }
    }
}