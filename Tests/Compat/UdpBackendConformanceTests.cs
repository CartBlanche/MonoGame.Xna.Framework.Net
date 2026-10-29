using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>The default LAN back-end against the shared back-end scenarios.</summary>
    [TestFixture]
    public class UdpBackendConformanceTests : BackendConformanceTests
    {
        protected override INetworkBackend CreateBackend() => new UdpBackend();
    }
}
