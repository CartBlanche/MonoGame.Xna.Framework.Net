namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Represents a local network gamer.
	/// </summary>
	public class LocalNetworkGamer : NetworkGamer
    {
        internal LocalNetworkGamer(NetworkSession session, string id, bool isHost, string gamertag)
            : base(session, id, true, isHost, gamertag)
        {
        }
    }
}