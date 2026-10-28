using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Hosts a SystemLink session and joins clients to it over real UDP loopback, using only the
    /// public XNA-shaped API. SystemLink uses fixed ports, so only one host can exist at a time:
    /// fixtures using this must not run in parallel (NUnit's default).
    /// </summary>
    internal sealed class SessionHarness : IDisposable
    {
        public const int DefaultTimeoutMs = 8000;

        readonly List<NetworkSession> sessions = new List<NetworkSession>();

        public NetworkSession Host { get; private set; }
        public List<NetworkSession> Clients { get; } = new List<NetworkSession>();
        public IEnumerable<NetworkSession> All => sessions;

        public static LocalNetworkGamer LocalOf(NetworkSession session) => session.LocalGamers[0];

        public async Task<NetworkSession> StartHostAsync(int maxGamers = 4, int privateSlots = 0)
        {
            Host = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, maxGamers, privateSlots,
                new Dictionary<string, object>());
            sessions.Add(Host);
            return Host;
        }

        /// <summary>Finds the host's session through discovery and joins it.</summary>
        public async Task<NetworkSession> JoinClientAsync()
        {
            AvailableNetworkSession target = await FindHostAsync();
            NetworkSession client = await NetworkSession.JoinAsync(target);
            sessions.Add(client);
            Clients.Add(client);
            return client;
        }

        public async Task<AvailableNetworkSession> FindHostAsync()
        {
            // NetworkSession has no public session id (as in XNA), so the host is recognised by its
            // gamertag, which is unique per session
            string hostGamertag = LocalOf(Host).Gamertag;
            string lastSeen = "nothing";
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(DefaultTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                using (AvailableNetworkSessionCollection found =
                    await NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, new Dictionary<string, object>()))
                {
                    AvailableNetworkSession match = found.FirstOrDefault(s => s.HostGamertag == hostGamertag);
                    if (match != null)
                        return match;
                    lastSeen = string.Join(", ", found.Select(s => s.HostGamertag));
                }
            }
            throw new TimeoutException($"The hosted session ({hostGamertag}) was never discovered. Last search found: {lastSeen}");
        }

        /// <summary>
        /// Calls Update() on every session until the condition holds, as a game loop would.
        /// </summary>
        public async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = DefaultTimeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                foreach (NetworkSession session in sessions)
                    session.Update();
                if (condition())
                    return true;
                await Task.Delay(10);
            }
            foreach (NetworkSession session in sessions)
                session.Update();
            return condition();
        }

        public void Dispose()
        {
            foreach (NetworkSession session in Enumerable.Reverse(sessions))
            {
                try { session.Dispose(); }
                catch { }
            }
            sessions.Clear();
        }
    }
}
