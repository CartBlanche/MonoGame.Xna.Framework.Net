using Epic.OnlineServices;

namespace Microsoft.Xna.Framework.Net.EOS
{
    public class EOSNetworkGamer : INetworkGamer
    {
        internal EOSNetworkGamer(ProductUserId productUserId, string gamertag, bool isLocal, bool isHost)
        {
            ProductUserId = productUserId;
            Id = productUserId?.ToString() ?? Guid.NewGuid().ToString();
            Gamertag = string.IsNullOrWhiteSpace(gamertag) ? "EOSPlayer" : gamertag;
            IsLocal = isLocal;
            IsHost = isHost;
        }

        internal ProductUserId ProductUserId { get; }

        public string Id { get; }
        public string Gamertag { get; }
        public bool IsLocal { get; }
        public bool IsHost { get; protected set; }
        public bool IsReady { get; set; }
        public TimeSpan RoundtripTime { get; internal set; }
        public object Tag { get; set; }

        internal void SetHost(bool value) => IsHost = value;
    }

    public sealed class EOSLocalNetworkGamer : EOSNetworkGamer, ILocalNetworkGamer
    {
        internal EOSLocalNetworkGamer(ProductUserId productUserId, string gamertag, bool isHost)
            : base(productUserId, gamertag, isLocal: true, isHost: isHost)
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
