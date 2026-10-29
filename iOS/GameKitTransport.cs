using Foundation;
using GameKit;
using System.Diagnostics;
using System.Threading.Channels;
using UIKit;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// Carries datagrams between iOS devices over a Game Center match (<c>GKMatch</c>). A peer is a
    /// Game Center game player id.
    /// </summary>
    /// <remarks>
    /// A match only exists once players have met, so a joiner cannot know the host's player id when it
    /// finds the session. <see cref="HostPeer"/> stands for "the other player in the match" until then;
    /// the host's real id is learned from its first datagram.
    /// Everything is sent reliably: GameKit's unreliable mode carries only about 1000 bytes.
    /// </remarks>
    public sealed class GameKitTransport : IPeerTransport
    {
        private const string PeerPrefix = "gk:";

        /// <summary>The host of the match, as a joiner knows it before any datagram has arrived.</summary>
        public static readonly PeerId HostPeer = new PeerId(PeerPrefix + "host");

        private readonly Channel<(byte[] Data, PeerId Sender)> incoming = Channel.CreateUnbounded<(byte[], PeerId)>();
        private readonly object gate = new();

        private GKMatch match;
        private MatchDelegate matchDelegate;
        private string hostPlayerId;
        private volatile bool disposed;
        private volatile bool isBound;

        public bool IsBound => isBound;

        public PeerId LocalPeer => PeerId.None;

        /// <summary>The peer that stands for a Game Center player.</summary>
        public static PeerId ToPeer(string gamePlayerId) => new PeerId(PeerPrefix + gamePlayerId);

        public void Bind()
        {
            ThrowIfDisposed();
            isBound = true;
        }

        /// <summary>
        /// Looks for a match under a queue name. A host only waits for players to arrive (so this returns
        /// at once); a joiner waits until it is matched, and treats the first other player as the host.
        /// </summary>
        internal async Task StartMatchAsync(string queueName, int maxPlayers, bool asHost)
        {
            ThrowIfDisposed();
            Bind();

            matchDelegate = new MatchDelegate(this);
            var request = new GKMatchRequest { MinPlayers = 2, MaxPlayers = (nint)Math.Max(2, maxPlayers), QueueName = queueName };
            var found = new TaskCompletionSource<GKMatch>(TaskCreationOptions.RunContinuationsAsynchronously);

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                GKMatchmaker.SharedMatchmaker.FindMatch(request, (matched, error) =>
                {
                    if (error != null)
                        found.TrySetException(new InvalidOperationException($"GKMatchmaker.FindMatch failed: {error.LocalizedDescription}"));
                    else if (matched == null)
                        found.TrySetException(new InvalidOperationException("GKMatchmaker returned no match."));
                    else
                        found.TrySetResult(matched);
                });
            });

            if (asHost)
            {
                _ = AttachWhenMatchedAsync(found.Task, asHost: true);
                return;
            }

            await AttachWhenMatchedAsync(found.Task, asHost: false).ConfigureAwait(false);
        }

        private async Task AttachWhenMatchedAsync(Task<GKMatch> pending, bool asHost)
        {
            GKMatch matched;
            try
            {
                matched = await pending.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NetworkLogging.Logger.LogWarning($"[GameKit] Matchmaking failed: {ex.Message}");
                if (!asHost)
                    throw;
                return;
            }

            matched.Delegate = matchDelegate;
            lock (gate)
            {
                match = matched;
                if (!asHost)
                    hostPlayerId = matched.Players?.Select(PlayerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
            }
        }

        public void Send(PeerId peer, byte[] data, int length)
        {
            ThrowIfDisposed();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

            if (!peer.IsValid || !peer.Value.StartsWith(PeerPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"{peer} is not a Game Center peer.", nameof(peer));

            GKMatch current;
            string targetId;
            lock (gate)
            {
                current = match;
                targetId = peer == HostPeer ? hostPlayerId : peer.Value.Substring(PeerPrefix.Length);
            }

            // no match yet, or the host not known yet: the datagram is lost, which a transport may do
            if (current?.Players == null || string.IsNullOrEmpty(targetId))
                return;

            var player = current.Players.FirstOrDefault(p => PlayerId(p) == targetId);
            if (player == null)
                return;

            var payload = NSData.FromArray(length == data.Length ? data : data.Take(length).ToArray());
#pragma warning disable CA1422 // the GKPlayer[] overload is the current API; only its mode enum is tagged obsolete
            current.SendData(payload, new[] { player }, GKMatchSendDataMode.Reliable, out var error);
#pragma warning restore CA1422
            if (error != null)
                NetworkLogging.Logger.LogWarning($"[GameKit] SendData failed: {error.LocalizedDescription}");
        }

        public async Task<(byte[] Data, PeerId Sender)> ReceiveAsync()
        {
            ThrowIfDisposed();
            try
            {
                return await incoming.Reader.ReadAsync().ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                throw new ObjectDisposedException(nameof(GameKitTransport));
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            GKMatch toClose;
            lock (gate)
            {
                toClose = match;
                match = null;
            }

            if (toClose != null)
            {
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    toClose.Delegate = null;
                    toClose.Disconnect();
                    GKMatchmaker.SharedMatchmaker.Cancel();
                });
            }

            incoming.Writer.TryComplete();
            isBound = false;
        }

        private static string PlayerId(GKPlayer player) => player?.GamePlayerId ?? player?.DisplayName ?? player?.Alias;

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GameKitTransport));
        }

        private sealed class MatchDelegate : GKMatchDelegate
        {
            private readonly GameKitTransport transport;

            internal MatchDelegate(GameKitTransport transport) => this.transport = transport;

            public override void DataReceivedFromPlayer(GKMatch match, NSData data, GKPlayer player)
            {
                if (data == null || data.Length == 0)
                    return;

                string id = PlayerId(player);
                if (string.IsNullOrEmpty(id))
                    return;

                transport.incoming.Writer.TryWrite((data.ToArray(), ToPeer(id)));
            }
        }
    }
}
