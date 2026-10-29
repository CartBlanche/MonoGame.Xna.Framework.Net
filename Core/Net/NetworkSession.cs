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
    public class NetworkSession : IDisposable, IAsyncDisposable, ISessionAdvertiser
    {
        // Event for received messages
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        private readonly List<NetworkGamer> gamers;
        private readonly GamerCollection gamerCollection;
        private readonly NetworkSessionType sessionType;
        private readonly int maxGamers;
        private readonly int privateGamerSlots;
        private readonly Dictionary<string, PeerId> gamerPeers;

        // Reliable delivery over UDP: one channel per remote gamer (see ReliableChannel). Everything sent
        // to a gamer goes through it, as reliable or unreliable, and is unwrapped again on arrival.
        private readonly Dictionary<string, ReliableChannel> reliableChannels = new Dictionary<string, ReliableChannel>();
        private readonly object reliableLock = new object();
        private readonly Stopwatch reliableClock = Stopwatch.StartNew();
        private Timer reliableTimer;
        private const int ReliableTickMilliseconds = 50;
        private readonly object lockObject = new object();

        private LocalNetworkGamer localGamer;
        private NetworkMachine localMachine;
        private readonly Dictionary<string, NetworkMachine> remoteMachines = new Dictionary<string, NetworkMachine>();
        private IPeerTransport networkTransport;
        private IDisposable backendMembership;   // e.g. the back-end's lobby, left when the session ends
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

        /// <summary>
        /// Raised, from <see cref="Update"/>, for each gamer that joins. A gamer who joined before
        /// <see cref="Update"/> first ran, the local gamers included, is raised by that first call; one who
        /// is already in the session when a handler is added is not raised again.
        /// </summary>
        public event EventHandler<GamerJoinedEventArgs> GamerJoined;

        public event EventHandler<GamerLeftEventArgs> GamerLeft;
        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded;

        // Static event for invite acceptance
        public static event EventHandler<InviteAcceptedEventArgs> InviteAccepted;

        /// <summary>
        /// Allows changing the network transport implementation.
        /// </summary>
        public IPeerTransport NetworkTransport
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
        private NetworkSession(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots, bool isHost, IPeerTransport transport = null)
        {
            // Register message types (can be moved to static constructor)
            NetworkMessageRegistry.Register<PlayerMoveMessage>(1);

            this.sessionType = sessionType;
            this.maxGamers = maxGamers;
            this.privateGamerSlots = privateGamerSlots;
            this.isHost = isHost;
            this.sessionId = Guid.NewGuid().ToString();
            this.sessionState = NetworkSessionState.Creating;
            TrackLive(this);

            gamers = new List<NetworkGamer>();
            gamerCollection = new GamerCollection(gamers);
            gamerPeers = new Dictionary<string, PeerId>();

            networkTransport = transport ?? new UdpTransport(SystemLinkSessionManager.GamePort);
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

            // Start the receive loop for every session that talks to others over a transport
            if (sessionType != NetworkSessionType.Local)
            {
                // Bind immediately so our address is stable and reachable
                if (!networkTransport.IsBound)
                {
                    try { networkTransport.Bind(); } catch { }
                }
                receiveTask = Task.Run(() => ReceiveLoopAsync(cancellationTokenSource.Token));
            }
        }

        // Internal constructor for SystemLink join
        internal NetworkSession(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots, bool isHost, string sessionId, IPeerTransport transport = null)
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
        /// Gets or sets whether the session may pick a new host when the host goes. Host migration is not
        /// implemented, so whatever this is set to, clients end the session with
        /// <see cref="NetworkSessionEndReason.HostEndedSession"/> when the host leaves.
        /// </summary>
        public bool AllowHostMigration { get; set; } = false;

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
        public static async Task<NetworkSession> CreateAsync(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots, IDictionary<string, object> sessionProperties, CancellationToken cancellationToken = default)
        {
            if (maxLocalGamers < 1 || maxLocalGamers > 4)
                throw new ArgumentOutOfRangeException(nameof(maxLocalGamers));
            if (privateGamerSlots < 0 || privateGamerSlots > maxGamers)
                throw new ArgumentOutOfRangeException(nameof(privateGamerSlots));

            NetworkSession session;
            if (sessionType == NetworkSessionType.Local)
            {
                // Local session: in-memory only
                await Task.Delay(5, cancellationToken);
                session = new NetworkSession(sessionType, maxGamers, privateGamerSlots, true);
                session.sessionState = NetworkSessionState.Lobby;
                // Register in local session list for FindAsync
                LocalSessionRegistry.RegisterSession(session);
            }
            else
            {
                // Every other type is carried by the back-end, which supplies the transport and makes the
                // session discoverable
                var backend = RequireBackendFor(sessionType);
                session = new NetworkSession(sessionType, maxGamers, privateGamerSlots, true, backend.CreateTransport());
                session.sessionState = NetworkSessionState.Lobby;
                session.StartConnectionMonitoring();
                session.logger?.LogInfo($"Started connection monitoring for session {session.sessionId}");

                // set before advertising: the advertisement carries them
                if (sessionProperties != null)
                    foreach (var kvp in sessionProperties)
                        session.SessionProperties[kvp.Key] = kvp.Value;

                try
                {
                    // the session's own token, so advertising stops when the session does
                    await backend.AdvertiseAsync(session, session.networkTransport, session.cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
            }

            // what the host asked for is what searches see and what joiners get
            if (sessionProperties != null)
                foreach (var kvp in sessionProperties)
                    session.SessionProperties[kvp.Key] = kvp.Value;
            return session;
        }

        /// <summary>
        /// Synchronous wrapper for CreateAsync (for XNA compatibility).
        /// </summary>
        public static NetworkSession Create(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots, NetworkSessionProperties sessionProperties)
        {
            return CreateAsync(sessionType, maxLocalGamers, maxGamers, privateGamerSlots, sessionProperties?.ToDictionary()).GetAwaiter().GetResult();
        }

        // A search matches a session that has every property the search sets, with the same value. No
        // properties (null or empty) matches everything.
        private static bool MatchesSearch(AvailableNetworkSession session, IDictionary<string, object> search)
        {
            if (search == null)
                return true;
            foreach (var wanted in search)
            {
                if (!session.SessionProperties.TryGetValue(wanted.Key, out var actual) || !Equals(actual, wanted.Value))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Asynchronously finds available network sessions.
        /// </summary>
        public static async Task<AvailableNetworkSessionCollection> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, IDictionary<string, object> sessionProperties, CancellationToken cancellationToken = default)
        {
            if (sessionType == NetworkSessionType.Local)
            {
                await Task.Delay(5, cancellationToken);
                // Return sessions in local registry
                var localSessions = LocalSessionRegistry.FindSessions(maxLocalGamers).Where(s => MatchesSearch(s, sessionProperties)).ToList();
                return new AvailableNetworkSessionCollection(localSessions);
            }

            var backend = RequireBackendFor(sessionType);
            var found = await backend.FindAsync(sessionType, maxLocalGamers, cancellationToken).ConfigureAwait(false);
            // the back-end lists what it has; the search properties narrow it
            return new AvailableNetworkSessionCollection(found.Where(s => s.SessionType == sessionType && MatchesSearch(s, sessionProperties)).ToList());
        }

        private static INetworkBackend RequireBackendFor(NetworkSessionType sessionType)
        {
            var backend = NetworkServiceProvider.Backend;
            if (!backend.SupportsSessionType(sessionType))
                throw new NotSupportedException($"The {backend.Name} back-end does not support {sessionType} sessions.");
            return backend;
        }

        /// <summary>
        /// Synchronous wrapper for FindAsync (for XNA compatibility).
        /// </summary>
        public static AvailableNetworkSessionCollection Find(NetworkSessionType sessionType, int maxLocalGamers, NetworkSessionProperties searchProperties)
        {
            return FindAsync(sessionType, maxLocalGamers, searchProperties?.ToDictionary()).GetAwaiter().GetResult();
        }


        /// <summary>
        /// Asynchronously joins an available network session.
        /// </summary>
        public static async Task<NetworkSession> JoinAsync(AvailableNetworkSession availableSession, CancellationToken cancellationToken = default)
        {
            if (availableSession == null)
                throw new ArgumentNullException(nameof(availableSession));

            if (availableSession.SessionType == NetworkSessionType.Local)
            {
                // Attach to local session
                var localSession = LocalSessionRegistry.GetSessionById(availableSession.SessionId);
                if (localSession == null)
                    throw new NetworkSessionJoinException(NetworkSessionJoinError.SessionNotFound);

                // Add local gamer
                var localGamer = new LocalNetworkGamer(localSession, Guid.NewGuid().ToString(), false, SignedInGamer.Current?.Gamertag ?? "Player");
                localSession.AddGamer(localGamer);
                return localSession;
            }

            // Connect to the host through the back-end
            var backend = RequireBackendFor(availableSession.SessionType);
            var joinTransport = backend.CreateTransport();
            IDisposable membership = null;
            NetworkSession joinedSession;
            try
            {
                membership = await backend.PrepareJoinAsync(availableSession, joinTransport, cancellationToken).ConfigureAwait(false);
                joinedSession = await SystemLinkSessionManager.JoinSessionAsync(availableSession, joinTransport, cancellationToken);
                if (joinedSession == null)
                    throw new NetworkSessionJoinException(NetworkSessionJoinError.SessionNotFound);
            }
            catch
            {
                membership?.Dispose();
                joinTransport.Dispose();
                throw;
            }

            joinedSession.backendMembership = membership;
            return joinedSession;
        }

        /// <summary>
        /// Joins the session the user was invited to: the invitation they last accepted in the platform's
        /// own UI, which is announced by <see cref="InviteAccepted"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">There is no invitation waiting.</exception>
        public static Task<NetworkSession> JoinInvitedAsync(IEnumerable<SignedInGamer> localGamers, object state = null, CancellationToken cancellationToken = default)
        {
            if (localGamers == null || !localGamers.Any())
                throw new ArgumentException("At least one local gamer must be provided.", nameof(localGamers));

            AvailableNetworkSession invite = NetworkServiceProvider.TakePendingInvite()
                ?? throw new InvalidOperationException("There is no invitation to join. Wait for NetworkSession.InviteAccepted.");

            return JoinAsync(invite, cancellationToken);
        }

        // sessions that are alive on this machine, to tell whether an invitation is to one we are already in
        private static readonly List<WeakReference<NetworkSession>> liveSessions = new List<WeakReference<NetworkSession>>();

        private static void TrackLive(NetworkSession session)
        {
            lock (liveSessions)
            {
                liveSessions.RemoveAll(w => !w.TryGetTarget(out var s) || s.disposed);
                liveSessions.Add(new WeakReference<NetworkSession>(session));
            }
        }

        /// <summary>
        /// Raises <see cref="InviteAccepted"/> for an invitation. Returns true if it is to a session this
        /// machine is already in.
        /// </summary>
        internal static bool RaiseInviteAccepted(AvailableNetworkSession invite)
        {
            bool isCurrent;
            lock (liveSessions)
                isCurrent = liveSessions.Any(w => w.TryGetTarget(out var s) && !s.disposed && s.sessionId == invite.SessionId);

            InviteAccepted?.Invoke(null, new InviteAcceptedEventArgs(SignedInGamer.Current, isCurrent));
            return isCurrent;
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
            // an invitation accepted in the platform's UI is raised from any session's Update
            NetworkServiceProvider.DispatchInvites();

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
            var msg = new ReadinessUpdateMessage { GamerId = gamer.GamerId, IsReady = gamer.IsReady };
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
            var target = gamers.FirstOrDefault(g => g.GamerId == update.GamerId);
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
                    SendTo(e.RemotePeer, rejectWriter.GetData());
                    return;
                }

                // The host asked for no new players once the game has started (AllowJoinInProgress).
                // Somebody who is already in and merely repeats their request (a lost acceptance)
                // is still let through.
                if (!AllowJoinInProgress && sessionState == NetworkSessionState.Playing)
                {
                    bool alreadyJoined;
                    lock (lockObject)
                        alreadyJoined = gamers.Any(g => g.GamerId == joinRequest.GamerId);

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
                        SendTo(e.RemotePeer, rejectWriter.GetData());
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
                    SendTo(e.RemotePeer, rejectWriter.GetData());
                    return;
                }

                // Phase 1 FIX: Check if gamer already exists (from previous retry)
                // CRITICAL: Keep the check and add in the same lock to prevent race conditions
                NetworkGamer existingGamer = null;
                bool isNewGamer = false;
                
                logger?.LogInfo($"Processing join request from {joinRequest.Gamertag} (ID: {joinRequest.GamerId}), gamer count: {AllGamers.Count}");
                
                lock (lockObject)
                {
                    existingGamer = gamers.FirstOrDefault(g => g.GamerId == joinRequest.GamerId);
                    
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

                // A retry must come from the address the gamer joined from. Otherwise anybody who knew a
                // gamer's id (it is in everyone's roster) could take over that gamer's connection.
                if (!isNewGamer && PeerOf(existingGamer) != e.RemotePeer)
                {
                    logger?.LogWarning($"Join request for {existingGamer.Gamertag} from {e.RemotePeer} refused: that gamer joined from another address");
                    var taken = new PacketWriter();
                    new JoinRejectedMessage { ErrorCode = NetworkSessionJoinError.SessionNotJoinable, Reason = "A gamer with that id is already in the session" }.Serialize(taken);
                    SendTo(e.RemotePeer, taken.GetData());
                    return;
                }
                
                // Register endpoint outside lock (uses its own lock internally)
                RegisterGamerEndpoint(existingGamer, e.RemotePeer);
                
                // Notify connection monitor for new gamers only
                if (isNewGamer)
                {
                    connectionMonitor?.OnGamerJoined(existingGamer);
                    OnGamerJoined(existingGamer);
                }

                // Tell the gamers already here about the newcomer, so every machine knows every gamer
                if (isNewGamer)
                {
                    var announce = new PacketWriter();
                    new GamerJoinedMessage { Id = existingGamer.Id, GamerId = existingGamer.GamerId, Gamertag = existingGamer.Gamertag }.Serialize(announce);
                    SendMessageToAll(announce, SendDataOptions.Reliable, existingGamer);
                }

                // Send JoinAcceptedMessage back to the sender (always send, even on retry), with the
                // ids the host chose and who else is in the session
                var joinAccepted = new JoinAcceptedMessage
                {
                    SessionId = sessionId,
                    HostGamerId = Host.GamerId,
                    HostGamertag = Host.Gamertag,
                    HostId = Host.Id,
                    YourId = existingGamer.Id
                };
                lock (lockObject)
                {
                    foreach (var other in gamers.Where(g => !g.IsHost && g != existingGamer))
                        joinAccepted.Others.Add(new JoinAcceptedMessage.RosterEntry { Id = other.Id, GamerId = other.GamerId, Gamertag = other.Gamertag });
                }
                var writer = new PacketWriter();
                joinAccepted.Serialize(writer);
                SendTo(e.RemotePeer, writer.GetData());
                logger?.LogInfo($"Sent JoinAcceptedMessage to {joinRequest.Gamertag}");
            }
            else if (e.Message is JoinAcceptedMessage joinAccepted)
            {
                // Client receives confirmation from host; ensure host is present and mapped. Only the
                // address the request went to may answer it: anybody else's "acceptance" is a forgery.
                if (!IsHost && (!JoinTargetPeer.IsValid || e.RemotePeer == JoinTargetPeer))
                {
                    logger?.LogInfo($"Received JoinAcceptedMessage from host {joinAccepted.HostGamertag}");
                    
                    var existingHost = gamers.FirstOrDefault(g => g.IsHost);
                    if (existingHost != null && existingHost.GamerId != joinAccepted.HostGamerId)
                    {
                        // Remove synthetic host
                        RemoveGamer(existingHost);
                        existingHost = null;
                    }

                    var host = existingHost ?? new NetworkGamer(this, joinAccepted.HostGamerId, isLocal: false, isHost: true, gamertag: joinAccepted.HostGamertag);
                    host.AssignId(joinAccepted.HostId);
                    foreach (var mine in LocalGamers)
                        mine.AssignId(joinAccepted.YourId);
                    if (existingHost == null)
                        AddGamer(host);
                    RegisterGamerEndpoint(host, e.RemotePeer);

                    foreach (var entry in joinAccepted.Others)
                        AddPeer(entry.Id, entry.GamerId, entry.Gamertag);
                    
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
                if (sessionState == NetworkSessionState.Joining && (!JoinTargetPeer.IsValid || e.RemotePeer == JoinTargetPeer))
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
                    if (e.RemotePeer.IsValid)
                        sourceGamer = gamers.FirstOrDefault(g => gamerPeers.TryGetValue(g.GamerId, out var ep) && ep == e.RemotePeer);
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
                // A client may only say it is ready itself; the host may say it of anyone (it relays them).
                var readinessSender = GamerAt(e.RemotePeer);
                bool allowed = IsHost
                    ? readinessSender != null && readinessSender.GamerId == readiness.GamerId
                    : IsFromHost(e.RemotePeer);
                if (!allowed)
                {
                    logger?.LogWarning($"Ignored a readiness update for {readiness.GamerId} from {e.RemotePeer}");
                    return;
                }

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
                        if (e.RemotePeer.IsValid)
                            sourceGamer = gamers.FirstOrDefault(g => gamerPeers.TryGetValue(g.GamerId, out var ep) && ep == e.RemotePeer);
                    }
                    SendMessageToAll(writer, SendDataOptions.Reliable, sourceGamer);
                }
            }
            else if (e.Message is HeartbeatMessage heartbeat)
            {
                // A heartbeat keeps its sender alive, so it must be the sender's own: not a stranger's, and
                // not one claiming to be somebody else.
                if (GamerAt(e.RemotePeer)?.GamerId == heartbeat.GamerId)
                    connectionMonitor?.OnHeartbeatReceived(heartbeat.GamerId, heartbeat);
            }
            else if (e.Message is HeartbeatReplyMessage heartbeatReply)
            {
                if (GamerAt(e.RemotePeer)?.GamerId == heartbeatReply.GamerId)
                {
                    // Phase 1: Handle heartbeat reply for RTT calculation
                    connectionMonitor?.OnHeartbeatReplyReceived(heartbeatReply.GamerId, heartbeatReply);

                    // Update diagnostics
                    var rtt = ConnectionHealthMonitor.RoundtripSince(heartbeatReply.RequestTimestamp);
                    if (rtt.HasValue)
                        diagnostics?.RecordRtt(rtt.Value);
                }
            }
            else if (e.Message is GamerJoinedMessage gamerJoined)
            {
                // the host tells us about a gamer who joined after us
                if (!IsHost && IsFromHost(e.RemotePeer))
                    AddPeer(gamerJoined.Id, gamerJoined.GamerId, gamerJoined.Gamertag);
            }
            else if (e.Message is GamerLeavingMessage leaveMessage)
            {
                // Phase 2: Handle graceful leave - immediate gamer removal
                logger?.LogInfo($"Received leave notification from gamer {leaveMessage.GamerId}: {leaveMessage.Reason}");
                
                NetworkGamer leavingGamer = null;
                lock (lockObject)
                {
                    leavingGamer = gamers.FirstOrDefault(g => g.GamerId == leaveMessage.GamerId);
                }
                
                // Only a gamer may announce its own departure (to the host), or the host a gamer's (to the
                // clients): nobody can remove somebody else
                bool authorised = leavingGamer != null && !leavingGamer.IsLocal &&
                    (IsHost ? PeerOf(leavingGamer) == e.RemotePeer : IsFromHost(e.RemotePeer));

                if (leavingGamer != null && !authorised)
                {
                    logger?.LogWarning($"Ignored a leave notice for {leavingGamer.Gamertag} from {e.RemotePeer}");
                }
                else if (leavingGamer != null)
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
            else if (e.Message is RemovedFromSessionMessage)
            {
                if (!IsHost && IsFromHost(e.RemotePeer))
                {
                    logger?.LogWarning("The host removed this machine from the session");
                    OnSessionEnded(NetworkSessionEndReason.RemovedByHost);
                }
            }
            else if (e.Message is SessionStateMessage stateMessage)
            {
                // Phase 2: Handle session state synchronization from host. Only the host decides the
                // state; a forged "the game has started" from anybody else is ignored.
                if (!IsHost && IsFromHost(e.RemotePeer))
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
            Raise(() => GamerJoined?.Invoke(this, new GamerJoinedEventArgs(gamer)));
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
                    GamerId = localGamer.GamerId,
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
                            if (gamerPeers.TryGetValue(gamer.GamerId, out var endpoint))
                            {
                                SendTo(endpoint, data);
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
                try { backendMembership?.Dispose(); } catch { /* the back-end's lobby is best effort */ }
                
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
                try { backendMembership?.Dispose(); } catch { /* the back-end's lobby is best effort */ }
                    
                if (receiveTask != null)
                {
                    await Task.WhenAny(receiveTask, Task.Delay(1000));
                }
                
                try { cancellationTokenSource?.Dispose(); } catch { /* Already disposed */ }
                
                // Raise SessionEnded event (may cause re-entrant Dispose call, now safe)
                OnSessionEnded(NetworkSessionEndReason.ClientSignedOut, callerThread: true);
            }
        }

        internal byte[] SerializeSessionPropertiesBinary() => SerializePropertiesBinary(SessionProperties);

        internal static byte[] SerializePropertiesBinary(IEnumerable<KeyValuePair<string, object>> properties)
        {
            var list = properties.ToList();
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(list.Count);
                foreach (var kvp in list)
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
                writer.WriteLengthPrefixed(SerializeSessionPropertiesBinary());
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
                        var propertiesData = reader.ReadLengthPrefixedBytes();
                        DeserializeSessionPropertiesBinary(propertiesData);
                    }
                }
            }
        }
        internal void AcceptGamer(NetworkGamer gamer)
        {
            AddGamer(gamer);
        }

        internal void EvictGamer(NetworkGamer gamer, NetworkSessionEndReason hostGoneReason = NetworkSessionEndReason.HostEndedSession)
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

                OnSessionEnded(hostGoneReason);
            }
        }

        internal void RemoveMachine(NetworkMachine machine)
        {
            if (!IsHost)
                throw new InvalidOperationException("Only the host can remove a machine from the session.");
            if (machine.IsLocal)
                throw new InvalidOperationException("The host cannot remove itself; leave the session instead.");

            var removed = new PacketWriter();
            new RemovedFromSessionMessage { Reason = "Removed by the host" }.Serialize(removed);
            byte[] notice = removed.GetData();

            foreach (NetworkGamer gamer in machine.Gamers.ToList())
            {
                // outside the channel, as a plain datagram, because the channel goes with the gamer.
                // A few copies, since nothing resends it.
                for (int i = 0; i < 3; i++)
                    SendRawToGamer(gamer.GamerId, notice);
                RemoveGamer(gamer);
            }
        }

        /// <summary>
        /// What a back-end advertising this session needs to tell the world.
        /// </summary>
        SessionAdvertisement ISessionAdvertiser.GetAdvertisement()
        {
            int count;
            string hostName;
            lock (lockObject)
            {
                count = gamers.Count;
                hostName = gamers.FirstOrDefault(g => g.IsHost)?.Gamertag;
            }

            return new SessionAdvertisement
            {
                SessionId = sessionId,
                SessionType = sessionType,
                HostGamertag = hostName,
                MaxGamers = maxGamers,
                PrivateGamerSlots = privateGamerSlots,
                CurrentGamerCount = count,
                Properties = new Dictionary<string, object>(SessionProperties),
                // a full session stops being advertised, as does one that has ended
                IsClosed = disposed || sessionState == NetworkSessionState.Ended || count >= maxGamers
            };
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

        // The next free byte id: ids are not reused while the gamer holding one is in the session
        private byte nextGamerId;
        internal byte AllocateGamerId()
        {
            lock (lockObject)
            {
                for (int attempts = 0; attempts < 256; attempts++)
                {
                    byte candidate = nextGamerId++;
                    if (!gamers.Any(g => g.HasShortId && g.Id == candidate))
                        return candidate;
                }
                throw new InvalidOperationException("All 256 gamer ids are in use.");
            }
        }

        // One machine for all the local gamers, and one per remote gamer
        internal NetworkMachine MachineOf(NetworkGamer gamer)
        {
            lock (lockObject)
            {
                if (gamer.IsLocal)
                    return localMachine ??= new NetworkMachine(this, isLocal: true);
                if (!remoteMachines.TryGetValue(gamer.GamerId, out var machine))
                    remoteMachines[gamer.GamerId] = machine = new NetworkMachine(this, isLocal: false);
                return machine;
            }
        }

        private void RemoveGamer(NetworkGamer gamer)
        {
            if (gamer == null) return;
            lock (lockObject)
            {
                gamers.Remove(gamer);
                gamerPeers.Remove(gamer.GamerId);
                remoteMachines.Remove(gamer.GamerId);
            }
            lock (reliableLock)
                reliableChannels.Remove(gamer.GamerId);
            // Phase 1: Notify connection monitor
            connectionMonitor?.OnGamerLeft(gamer);
            OnGamerLeft(gamer);

            // the other clients cannot see this gamer's connection drop, so the host tells them
            if (IsHost && !gamer.IsLocal && !disposed && sessionState != NetworkSessionState.Ended)
            {
                var notice = new PacketWriter();
                new GamerLeavingMessage { GamerId = gamer.GamerId, Reason = "Left the session" }.Serialize(notice);
                SendMessageToAll(notice, SendDataOptions.Reliable, gamer);
            }
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

        private PeerId? PeerOf(NetworkGamer gamer)
        {
            lock (lockObject)
                return gamerPeers.TryGetValue(gamer.GamerId, out var peer) ? peer : (PeerId?)null;
        }

        // a datagram as it is, outside any channel, to a peer
        private void SendTo(PeerId peer, byte[] data)
        {
            networkTransport.Send(peer, data, data.Length);
        }

        private bool IsFromHost(PeerId endpoint)
        {
            if (!endpoint.IsValid)
                return false;
            NetworkGamer host;
            lock (lockObject)
                host = gamers.FirstOrDefault(g => g.IsHost && !g.IsLocal);
            return host != null && PeerOf(host) == endpoint;
        }

        // the gamer a datagram came from, going by the address it came from, or null if nobody here has that address
        private NetworkGamer GamerAt(PeerId peer)
        {
            if (!peer.IsValid)
                return null;
            lock (lockObject)
                return gamers.FirstOrDefault(g => !g.IsLocal && gamerPeers.TryGetValue(g.GamerId, out var p) && p == peer);
        }

        // the address a joiner sent its join request to: the only one that may answer it
        internal PeerId JoinTargetPeer { get; set; }

        // a gamer the host told us about: not directly connected, reached through the host
        private void AddPeer(byte id, string gamerId, string gamertag)
        {
            NetworkGamer peer;
            lock (lockObject)
            {
                var existing = gamers.FirstOrDefault(g => g.GamerId == gamerId);
                if (existing != null)
                {
                    existing.AssignId(id);
                    return;
                }
                peer = new NetworkGamer(this, gamerId, isLocal: false, isHost: false, gamertag: gamertag);
                peer.AssignId(id);
                gamers.Add(peer);
            }
            OnGamerJoined(peer);
        }

        // The payload starts with its kind, so game data and framework messages can never be mistaken
        // for each other, whatever the first byte of the game data is.
        private void SendPayload(NetworkGamer gamer, byte kind, byte[] data, int dataLength, SendDataOptions options)
        {
            if (gamer == null || data == null) return;

            // A gamer we have no address for is another client, only reachable through the host. Game data
            // is passed on by the host; the framework's own messages are only ever sent to a direct peer.
            if (!IsHost && !gamer.IsLocal && PeerOf(gamer) == null)
            {
                NetworkGamer host;
                lock (lockObject)
                    host = gamers.FirstOrDefault(g => g.IsHost && !g.IsLocal);

                if (kind == PayloadKind.Application && host != null)
                {
                    byte[] relayed = new byte[dataLength + 2];
                    relayed[0] = (byte)options;
                    relayed[1] = gamer.Id;
                    Buffer.BlockCopy(data, 0, relayed, 2, dataLength);
                    SendPayload(host, PayloadKind.Relay, relayed, relayed.Length, options);
                }
                return;
            }

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
            PeerId endpoint;
            lock (lockObject)
            {
                if (!gamerPeers.TryGetValue(gamerId, out endpoint))
                    return;
            }

            try
            {
                networkTransport.Send(endpoint, data, data.Length);
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
                if (!reliableChannels.TryGetValue(gamer.GamerId, out ReliableChannel channel))
                {
                    string id = gamer.GamerId;
                    channel = new ReliableChannel(datagram => SendRawToGamer(id, datagram));
                    channel.Faulted += reason => OnChannelFaulted(id, reason);
                    channel.RoundtripMeasured += seconds => gamer.UpdateRoundtripTime(TimeSpan.FromSeconds(seconds));
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
                gamer = gamers.FirstOrDefault(g => g.GamerId == gamerId);

            if (gamer == null || disposed)
                return;

            logger?.LogWarning($"Removing {gamer.Gamertag}: {reason}");
            EvictGamer(gamer, NetworkSessionEndReason.Disconnected);
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
                return reliableChannels.TryGetValue(gamer.GamerId, out ReliableChannel channel)
                    ? TimeSpan.FromSeconds(channel.OldestUnackedAge(ReliableNow))
                    : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Internally associates a remote gamer with an endpoint (used by SystemLink join/handshake).
        /// </summary>
        internal void RegisterGamerEndpoint(NetworkGamer gamer, PeerId endpoint)
        {
            if (gamer == null || !endpoint.IsValid) return;
            lock (lockObject)
            {
                gamerPeers[gamer.GamerId] = endpoint;
            }
        }

        /// <summary>
        /// Handles one datagram from the network: unwraps the reliable channel it belongs to, then
        /// treats what comes out as a framework message or as game data, according to its kind.
        /// </summary>
        internal void ProcessIncomingDatagram(byte[] data, PeerId senderEndpoint)
        {
            if (data == null || data.Length == 0)
                return;

            // Identify sender by endpoint mapping
            NetworkGamer senderGamer = null;
            lock (lockObject)
            {
                senderGamer = gamers.FirstOrDefault(g => gamerPeers.TryGetValue(g.GamerId, out var ep) && ep == senderEndpoint);
            }

            // anything from a gamer shows they are still connected
            if (senderGamer != null)
                connectionMonitor?.OnPeerActivity(senderGamer.GamerId);

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

        private void ProcessPayload(byte[] payload, PeerId senderEndpoint, NetworkGamer senderGamer)
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

                case PayloadKind.Relay:
                    // a client asks us, the host, to pass data on to another client
                    if (IsHost && payload.Length >= 3)
                        RelayToGamer(senderGamer, (SendDataOptions)payload[1], payload[2], payload, 3);
                    break;

                case PayloadKind.Forwarded:
                    // the host passes on data from another client
                    if (!IsHost && senderGamer != null && senderGamer.IsHost && payload.Length >= 2)
                    {
                        NetworkGamer from;
                        lock (lockObject)
                            from = gamers.FirstOrDefault(g => !g.IsLocal && g.HasShortId && g.Id == payload[1]);
                        if (from == null)
                            break;
                        byte[] forwarded = new byte[payload.Length - 2];
                        Buffer.BlockCopy(payload, 2, forwarded, 0, forwarded.Length);
                        Raise(() =>
                        {
                            foreach (var local in LocalGamers)
                                local.EnqueueIncomingPacket(forwarded, from);
                        });
                    }
                    break;

                default:
                    logger?.LogWarning($"Dropped a packet of unknown kind 0x{payload[0]:X2} from {senderGamer?.Gamertag}");
                    break;
            }
        }

        // The host passes a client's game data on to its target: to a client as Forwarded, or to the host's own gamer
        private void RelayToGamer(NetworkGamer from, SendDataOptions options, byte targetId, byte[] payload, int offset)
        {
            if (from == null || from.IsLocal)
                return;

            NetworkGamer target;
            lock (lockObject)
                target = gamers.FirstOrDefault(g => g.HasShortId && g.Id == targetId);
            if (target == null || target == from)
                return;

            int length = payload.Length - offset;
            if (target.IsLocal)
            {
                byte[] copy = new byte[length];
                Buffer.BlockCopy(payload, offset, copy, 0, length);
                Raise(() => ((LocalNetworkGamer)target).EnqueueIncomingPacket(copy, from));
                return;
            }

            byte[] forward = new byte[length + 1];
            forward[0] = from.Id;
            Buffer.BlockCopy(payload, offset, forward, 1, length);
            SendPayload(target, PayloadKind.Forwarded, forward, forward.Length, options);
        }

        // a registered message starting at offset: the first byte is its type id and it must use up every byte
        private void ProcessMessage(byte[] data, int offset, PeerId senderEndpoint)
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
        // a transport that keeps failing is as good as gone: this many failures in a row end the session
        private const int MaxConsecutiveReceiveFailures = 10;

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            int failures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] data;
                PeerId sender;
                try
                {
                    // Ensure the network transport is bound before receiving data
                    if (!networkTransport.IsBound)
                    {
                        networkTransport.Bind();
                    }

                    (data, sender) = await networkTransport.ReceiveAsync();
                    failures = 0;
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
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    logger?.LogError($"ReceiveLoop error: {ex.Message}", ex);
                    if (++failures >= MaxConsecutiveReceiveFailures)
                    {
                        logger?.LogError("The transport keeps failing: ending the session");
                        if (sessionState != NetworkSessionState.Ended)
                            OnSessionEnded(NetworkSessionEndReason.NetworkError);
                        break;
                    }

                    // back off, so a transport that fails at once does not become a busy loop
                    try { await Task.Delay(50 * failures, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (data.Length == 0)
                    continue;

                try
                {
                    // Phase 1: Record received packet in diagnostics
                    diagnostics?.RecordPacketReceived(data.Length);

                    ProcessIncomingDatagram(data, sender);
                }
                catch (Exception ex)
                {
                    // one bad datagram must not end the session
                    logger?.LogError($"Processing a datagram failed: {ex.Message}", ex);
                }
            }
        }
    }
}