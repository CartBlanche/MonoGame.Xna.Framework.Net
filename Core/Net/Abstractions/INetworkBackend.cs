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
        /// Creates a transport for one session. Called once per hosted or joined session; the session
        /// binds it and disposes it.
        /// </summary>
        IPeerTransport CreateTransport();

        /// <summary>
        /// Makes a hosted session discoverable. Completes once advertising has started, or faults if it
        /// cannot start; advertising then carries on in the background until <paramref name="stopToken"/>
        /// is cancelled (the session ended). <paramref name="session"/> can be asked at any time what to say.
        /// </summary>
        Task AdvertiseAsync(ISessionAdvertiser session, CancellationToken stopToken);

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
        /// back-end's own lobby. Most back-ends have nothing to do.
        /// </summary>
        Task PrepareJoinAsync(AvailableNetworkSession session, CancellationToken cancellationToken);
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
        public string SessionId { get; internal set; }
        public NetworkSessionType SessionType { get; internal set; }
        public string HostGamertag { get; internal set; }
        public int MaxGamers { get; internal set; }
        public int PrivateGamerSlots { get; internal set; }
        public int CurrentGamerCount { get; internal set; }

        /// <summary>The session's properties, keyed as <see cref="NetworkSessionProperties"/> slots ("0" to "7") or by name.</summary>
        public IReadOnlyDictionary<string, object> Properties { get; internal set; }

        /// <summary>True once the session is over or no longer taking gamers, so advertising should stop.</summary>
        public bool IsClosed { get; internal set; }
    }
}
