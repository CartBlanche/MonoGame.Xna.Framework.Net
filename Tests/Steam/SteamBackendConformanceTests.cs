using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The Steam back-end against the shared scenarios. Without a running Steam client it falls back to
    /// the LAN, which is what runs here; with Steam running, two sessions would be the same account, so
    /// the scenarios that need two sessions are skipped.
    /// </summary>
    [TestFixture]
    public class SteamBackendConformanceTests : BackendConformanceTests
    {
        protected override INetworkBackend CreateBackend() => new SteamBackend();

        protected override bool CanRunTwoSessionsInOneProcess => !SteamRuntime.IsInitialized;

        protected override bool CarriesSessionProperties => !SteamRuntime.IsInitialized;
    }
}
