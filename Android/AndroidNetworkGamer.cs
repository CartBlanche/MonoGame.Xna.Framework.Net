namespace Microsoft.Xna.Framework.Net.Android
{
    public class AndroidNetworkGamer : INetworkGamer
    {
        internal AndroidNetworkGamer(string endpointId, string gamertag, bool isLocal, bool isHost)
        {
            EndpointId = endpointId;
            Id = endpointId ?? Guid.NewGuid().ToString();
            Gamertag = string.IsNullOrWhiteSpace(gamertag) ? "AndroidPlayer" : gamertag;
            IsLocal = isLocal;
            IsHost = isHost;
        }

        internal string EndpointId { get; }
        public string Id { get; }
        public string Gamertag { get; }
        public bool IsLocal { get; }
        public bool IsHost { get; protected set; }
        public bool IsReady { get; set; }
        public TimeSpan RoundtripTime { get; internal set; }
        public object Tag { get; set; }
    }

    public sealed class AndroidLocalNetworkGamer : AndroidNetworkGamer, ILocalNetworkGamer
    {
        internal AndroidLocalNetworkGamer(string endpointId, string gamertag, bool isHost)
            : base(endpointId, gamertag, isLocal: true, isHost: isHost)
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
