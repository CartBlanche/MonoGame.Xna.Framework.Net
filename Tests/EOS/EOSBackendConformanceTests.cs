using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The EOS back-end against the shared scenarios. Without an initialised runtime it falls back to
    /// the LAN, which is what runs here; with EOS up, two sessions would be the same account, so the
    /// scenarios that need two sessions are skipped.
    /// </summary>
    [TestFixture]
    public class EOSBackendConformanceTests : BackendConformanceTests
    {
        [SetUp]
        public void ShutDownEos() => EOSRuntime.Shutdown();

        protected override INetworkBackend CreateBackend() => new EOSBackend();

        protected override bool CanRunTwoSessionsInOneProcess => !EOSRuntime.IsInitialized;

        protected override bool CarriesSessionProperties => !EOSRuntime.IsInitialized;
    }
}
