using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Represents an available network session that can be joined.
    /// </summary>
    public class AvailableNetworkSession
    {
        public AvailableNetworkSession(
            string sessionName,
            string hostGamertag,
            int currentGamerCount,
            int openPublicGamerSlots,
            int openPrivateGamerSlots,
            NetworkSessionType sessionType,
            IDictionary<string, object> sessionProperties,
            string sessionId,
            PeerId hostPeer = default,
            bool isPasswordProtected = false)
        {
            SessionName = sessionName;
            HostGamertag = hostGamertag;
            CurrentGamerCount = currentGamerCount;
            OpenPublicGamerSlots = openPublicGamerSlots;
            OpenPrivateGamerSlots = openPrivateGamerSlots;
            SessionType = sessionType;
            SessionProperties = new Dictionary<string, object>(sessionProperties);
            SessionId = sessionId;
            HostPeer = hostPeer;
            IsPasswordProtected = isPasswordProtected;
        }
        /// <summary>
        /// Gets the unique session ID.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Gets the name of the session.
        /// </summary>
        public string SessionName { get; }

        /// <summary>
        /// Gets the gamertag of the host.
        /// </summary>
        public string HostGamertag { get; }

        /// <summary>
        /// Gets the current number of gamers in the session.
        /// </summary>
        public int CurrentGamerCount { get; }

        /// <summary>
        /// Gets the number of open public gamer slots.
        /// </summary>
        public int OpenPublicGamerSlots { get; }

        /// <summary>
        /// Gets the number of open private gamer slots.
        /// </summary>
        public int OpenPrivateGamerSlots { get; }

        /// <summary>
        /// Gets the type of the session.
        /// </summary>
        public NetworkSessionType SessionType { get; }

        /// <summary>
        /// Gets the session properties.
        /// </summary>
        public IDictionary<string, object> SessionProperties { get; }

        /// <summary>
        /// Gets the quality of service information.
        /// </summary>
        public QualityOfService QualityOfService { get; internal set; } = new QualityOfService();

    /// <summary>
    /// Gets the host's network endpoint for SystemLink (LAN) sessions.
    /// </summary>
    public PeerId HostPeer { get; }

    /// <summary>
    /// Gets whether the host requires a password to join; pass it to <see cref="NetworkSession.JoinAsync(AvailableNetworkSession, string, System.Threading.CancellationToken)"/>.
    /// </summary>
    public bool IsPasswordProtected { get; }
    }
}