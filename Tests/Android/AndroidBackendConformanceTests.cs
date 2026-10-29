using Microsoft.Xna.Framework.Net.Android;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// The Android back-end against the shared scenarios. Without an Activity it falls back to the LAN.
    /// On a device, run with an Activity and two instances (an emulator pair), not from one process.
    /// </summary>
    [TestFixture]
    public class AndroidBackendConformanceTests : BackendConformanceTests
    {
        protected override INetworkBackend CreateBackend() => new AndroidBackend();

        protected override bool CanRunTwoSessionsInOneProcess => !AndroidRuntime.IsInitialized;

        protected override bool CarriesSessionProperties => !AndroidRuntime.IsInitialized;

        protected override bool StopsAdvertisingWhenFull => !AndroidRuntime.IsInitialized;
    }
}
