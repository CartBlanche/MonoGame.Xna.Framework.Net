using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// What a networking plugin (UDP/LAN, Steam, Epic, Game Center, Play Games...) supplies. A back-end
    /// only moves datagrams and finds sessions; everything else, from lobby and readiness to reliability
    /// and relaying, is <see cref="NetworkSession"/>, the same on every back-end.
    /// </summary>
    public interface INetworkBackend
    {
        /// <summary>Name shown in logs and diagnostics, e.g. "Steam".</summary>
        string Name { get; }

        /// <summary>
        /// Whether this back-end carries sessions of this type. <see cref="NetworkSession"/> refuses the
        /// others with a <see cref="NotSupportedException"/> naming the back-end. (A LAN back-end carries
        /// SystemLink; an online one carries PlayerMatch and Ranked. Local sessions never reach a back-end.)
        /// </summary>
        bool SupportsSessionType(NetworkSessionType sessionType);

        /// <summary>
        /// Raised, from any thread, when the local user accepts an invitation in the platform's UI. The
        /// framework holds it until the game's next <c>Update()</c> and raises
        /// <see cref="NetworkSession.InviteAccepted"/> there. A back-end without invitations never raises it.
        /// </summary>
        event EventHandler<InviteReceivedEventArgs> InviteReceived;

        /// <summary>
        /// Creates a transport for one session. Called once per hosted or joined session; the session
        /// binds it and disposes it.
        /// </summary>
        IPeerTransport CreateTransport();

        /// <summary>
        /// Makes a hosted session discoverable. Completes once advertising has started, or faults if it
        /// cannot start; advertising then carries on in the background until <paramref name="stopToken"/>
        /// is cancelled (the session ended). <paramref name="session"/> can be asked at any time what to say.
        /// <paramref name="transport"/> is the one <see cref="CreateTransport"/> made for this session, for
        /// back-ends whose transport has to be told to accept connections.
        /// </summary>
        Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken);

        /// <summary>
        /// Finds sessions that can be joined. Each result carries the <see cref="PeerId"/> of its host.
        /// <see cref="NetworkSession.FindAsync"/> filters what is returned by the caller's search properties.
        /// </summary>
        Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            CancellationToken cancellationToken);

        /// <summary>
        /// Anything the back-end must do before the session connects to the host, such as entering the
        /// back-end's own lobby. Returns a handle the session disposes when it ends (to leave that lobby
        /// again), or null if there is nothing to undo. <paramref name="transport"/> is the one
        /// <see cref="CreateTransport"/> made for the session being joined.
        /// </summary>
        Task<IDisposable?> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A hosted session, as far as a back-end advertising it needs to see.
    /// </summary>
    public interface ISessionAdvertiser
    {
        /// <summary>The session as it is now. Ask again later: gamers join and leave.</summary>
        SessionAdvertisement GetAdvertisement();
    }

    /// <summary>A snapshot of what to tell the world about a hosted session.</summary>
    public sealed class SessionAdvertisement
    {
        /// <summary>The session's unique id, as joiners and searchers see it.</summary>
        public string SessionId { get; internal set; } = string.Empty;
        /// <summary>The game's <see cref="NetworkSession.GameId"/> when the session was created, empty if it set none. A back-end with no way to tell games apart on its own should announce it and ignore sessions with another.</summary>
        public string GameId { get; internal set; } = string.Empty;
        /// <summary>The type of the session.</summary>
        public NetworkSessionType SessionType { get; internal set; }
        /// <summary>The host's gamertag, or null if it has none to show.</summary>
        public string? HostGamertag { get; internal set; }
        /// <summary>The most gamers the session holds, public and private slots together.</summary>
        public int MaxGamers { get; internal set; }
        /// <summary>How many of <see cref="MaxGamers"/> are private slots, which are kept for invited gamers.</summary>
        public int PrivateGamerSlots { get; internal set; }
        /// <summary>The number of gamers in the session now, local gamers included.</summary>
        public int CurrentGamerCount { get; internal set; }

        /// <summary>True if joiners must know a password, which back-ends should show so searchers can ask for it.</summary>
        public bool IsPasswordProtected { get; internal set; }

        /// <summary>The session's properties, keyed as <see cref="NetworkSessionProperties"/> slots ("0" to "7") or by name.</summary>
        public IReadOnlyDictionary<string, object> Properties { get; internal set; } = new Dictionary<string, object>();

        /// <summary>True once the session is over or no longer taking gamers, so advertising should stop.</summary>
        public bool IsClosed { get; internal set; }
    }
}
