using Microsoft.Xna.Framework.Net.iOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The iOS back-end against the shared scenarios. Game Center has no session browsing, so with the
    /// runtime initialised searches find nothing and these scenarios are skipped; without it the back-end
    /// falls back to the LAN, which is what runs here.
    /// </summary>
    [TestFixture]
    public class IOSBackendConformanceTests : BackendConformanceTests
    {
        protected override INetworkBackend CreateBackend() => new IOSBackend();

        protected override bool CanRunTwoSessionsInOneProcess => !IOSRuntime.IsInitialized;

        protected override bool CarriesSessionProperties => !IOSRuntime.IsInitialized;
    }
}
