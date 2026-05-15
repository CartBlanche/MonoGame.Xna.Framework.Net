using System;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class SteamRealRuntimeSmokeTests
    {
        [Test]
        [Explicit("Requires local Steam client, valid app context, and MGNET_STEAM_SMOKE=1.")]
        public void SteamRuntime_Initialize_RunCallbacks_Shutdown_Smoke()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("MGNET_STEAM_SMOKE"), "1", StringComparison.Ordinal))
            {
                Assert.Ignore("Set MGNET_STEAM_SMOKE=1 to run real Steam runtime smoke tests.");
            }

            var initialized = SteamRuntime.Initialize();
            Assert.That(initialized, Is.True, "SteamRuntime.Initialize() failed. Ensure Steam client is running with valid app context.");

            Assert.DoesNotThrow(() => SteamRuntime.RunCallbacks());

            SteamRuntime.Shutdown();
            Assert.That(SteamRuntime.IsInitialized, Is.False);
        }
    }
}
