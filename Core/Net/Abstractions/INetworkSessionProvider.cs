using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Extended factory contract that can produce concrete <see cref="NetworkSession"/> instances
    /// directly, bypassing the built-in UDP switch-statement logic inside
    /// <see cref="NetworkSession.CreateAsync"/>, <see cref="NetworkSession.FindAsync"/>, and
    /// <see cref="NetworkSession.JoinAsync"/>.
    ///
    /// When <see cref="NetworkServiceProvider.SessionFactory"/> also implements this interface
    /// those three static methods will delegate entirely to the provider, enabling alternative
    /// transport / discovery back-ends (Steam, Epic, etc.) without any changes in game code.
    /// </summary>
    public interface INetworkSessionProvider
    {
        /// <summary>Creates a new hosted session using this back-end.</summary>
        Task<NetworkSession> CreateSessionAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            int maxGamers,
            int privateGamerSlots,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default);

        /// <summary>Finds available sessions using this back-end's discovery mechanism.</summary>
        Task<AvailableNetworkSessionCollection> FindSessionsAsync(
            NetworkSessionType sessionType,
            int maxLocalGamers,
            IDictionary<string, object> sessionProperties,
            CancellationToken cancellationToken = default);

        /// <summary>Joins an available session found via <see cref="FindSessionsAsync"/>.</summary>
        Task<NetworkSession> JoinSessionAsync(
            AvailableNetworkSession availableSession,
            CancellationToken cancellationToken = default);
    }
}
