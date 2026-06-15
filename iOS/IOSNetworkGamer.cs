namespace Microsoft.Xna.Framework.Net.iOS
{
    public class IOSNetworkGamer : INetworkGamer
    {
        internal IOSNetworkGamer(string playerId, string gamertag, bool isLocal, bool isHost)
        {
            PlayerId = playerId;
            Id = playerId ?? Guid.NewGuid().ToString();
            Gamertag = string.IsNullOrWhiteSpace(gamertag) ? "IOSPlayer" : gamertag;
            IsLocal = isLocal;
            IsHost = isHost;
        }

        internal string PlayerId { get; }
        public string Id { get; }
        public string Gamertag { get; }
        public bool IsLocal { get; }
        public bool IsHost { get; protected set; }
        public bool IsReady { get; set; }
        public TimeSpan RoundtripTime { get; internal set; }
        public object Tag { get; set; }
    }

    public sealed class IOSLocalNetworkGamer : IOSNetworkGamer, ILocalNetworkGamer
    {
        internal IOSLocalNetworkGamer(string playerId, string gamertag, bool isHost)
            : base(playerId, gamertag, isLocal: true, isHost: isHost)
        {
        }

        bool ILocalNetworkGamer.IsHost
        {
            get => IsHost;
            set
            {
                if (value != IsHost)
                    throw new InvalidOperationException("Cannot change IsHost after session creation.");
            }
        }

        bool ILocalNetworkGamer.IsReady
        {
            get => IsReady;
            set => IsReady = value;
        }
    }
}
