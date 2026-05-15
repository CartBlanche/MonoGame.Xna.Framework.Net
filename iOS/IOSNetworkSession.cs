using Microsoft.Xna.Framework;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Adapter over NetworkSession used by the iOS backend.
    /// Networking transport remains SystemLink/UDP in this initial cross-platform vertical slice.
    /// </summary>
    public sealed class IOSNetworkSession : INetworkSession
    {
        private NetworkSession innerSession;
        private ILocalNetworkGamer localGamerAdapter;
        private readonly Dictionary<string, INetworkGamer> gamerAdapters = new(StringComparer.Ordinal);
        private bool disposed;

        public IReadOnlyList<INetworkGamer> AllGamers
        {
            get
            {
                if (innerSession == null)
                {
                    return new List<INetworkGamer>();
                }

                return innerSession.AllGamers.Select(AdaptGamer).ToList();
            }
        }

        public ILocalNetworkGamer LocalGamer
        {
            get
            {
                if (innerSession == null)
                {
                    return null;
                }

                var local = innerSession.LocalGamers.FirstOrDefault();
                if (local == null)
                {
                    return null;
                }

                localGamerAdapter ??= new LocalNetworkGamerAdapter(local);
                return localGamerAdapter;
            }
        }

        public NetworkSessionState State => innerSession?.SessionState ?? NetworkSessionState.Creating;

        public string SessionId => innerSession?.sessionId;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.MessageReceived += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.MessageReceived -= value;
                }
            }
        }

        public event EventHandler<GamerJoinedEventArgs> GamerJoined
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.GamerJoined += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.GamerJoined -= value;
                }
            }
        }

        public event EventHandler<GamerLeftEventArgs> GamerLeft
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.GamerLeft += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.GamerLeft -= value;
                }
            }
        }

        public event EventHandler<GameStartedEventArgs> GameStarted
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.GameStarted += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.GameStarted -= value;
                }
            }
        }

        public event EventHandler<GameEndedEventArgs> GameEnded
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.GameEnded += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.GameEnded -= value;
                }
            }
        }

        public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded
        {
            add
            {
                if (innerSession != null)
                {
                    innerSession.SessionEnded += value;
                }
            }
            remove
            {
                if (innerSession != null)
                {
                    innerSession.SessionEnded -= value;
                }
            }
        }

        public async Task CreateAsync(NetworkSessionType sessionType, int maxGamers, int privateGamerSlots)
        {
            ThrowIfDisposed();

            var session = await NetworkSession.CreateAsync(
                sessionType,
                maxLocalGamers: 1,
                maxGamers: maxGamers,
                privateGamerSlots: privateGamerSlots,
                sessionProperties: null).ConfigureAwait(false);

            ReplaceInnerSession(session);
        }

        public async Task JoinAsync(string hostAddress)
        {
            ThrowIfDisposed();

            AvailableNetworkSession session = null;
            for (var attempt = 0; attempt < 8 && session == null; attempt++)
            {
                var availableSessions = await NetworkSession.FindAsync(
                    NetworkSessionType.SystemLink,
                    maxLocalGamers: 1,
                    sessionProperties: null).ConfigureAwait(false);

                session = availableSessions.FirstOrDefault(s =>
                    string.Equals(s.HostEndpoint?.ToString(), hostAddress, StringComparison.OrdinalIgnoreCase));

                if (session == null)
                {
                    await Task.Delay(150).ConfigureAwait(false);
                }
            }

            if (session == null)
            {
                throw new InvalidOperationException($"Session not found: {hostAddress}");
            }

            var joined = await NetworkSession.JoinAsync(session).ConfigureAwait(false);
            ReplaceInnerSession(joined);
        }

        public void SendMessage(INetworkMessage message, INetworkGamer recipient)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (recipient == null)
                throw new ArgumentNullException(nameof(recipient));
            if (innerSession == null)
                throw new InvalidOperationException("Session not initialized.");

            var networkGamer = innerSession.AllGamers.FirstOrDefault(g => g.Id == recipient.Id);
            if (networkGamer == null)
            {
                return;
            }

            var writer = new PacketWriter();
            message.Serialize(writer);
            innerSession.SendDataToGamer(networkGamer, writer, SendDataOptions.Reliable);
        }

        public void BroadcastMessage(INetworkMessage message)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (innerSession == null)
                throw new InvalidOperationException("Session not initialized.");

            var writer = new PacketWriter();
            message.Serialize(writer);
            innerSession.SendToAll(writer, SendDataOptions.Reliable);
        }

        public void Update(GameTime gameTime)
        {
            ThrowIfDisposed();
            innerSession?.Update();
        }

        public async Task CloseAsync()
        {
            if (disposed)
            {
                return;
            }

            if (innerSession != null)
            {
                await innerSession.DisposeAsync().ConfigureAwait(false);
                innerSession = null;
            }

            gamerAdapters.Clear();
            localGamerAdapter = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CloseAsync().GetAwaiter().GetResult();
            disposed = true;
        }

        private void ReplaceInnerSession(NetworkSession session)
        {
            innerSession = session ?? throw new ArgumentNullException(nameof(session));
            gamerAdapters.Clear();
            localGamerAdapter = null;
        }

        private INetworkGamer AdaptGamer(NetworkGamer gamer)
        {
            if (gamer == null)
            {
                return null;
            }

            if (gamerAdapters.TryGetValue(gamer.Id, out var existing))
            {
                return existing;
            }

            INetworkGamer adapted = gamer.IsLocal
                ? new LocalNetworkGamerAdapter((LocalNetworkGamer)gamer)
                : new NetworkGamerAdapter(gamer);

            gamerAdapters[gamer.Id] = adapted;
            return adapted;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(IOSNetworkSession));
            }
        }
    }
}