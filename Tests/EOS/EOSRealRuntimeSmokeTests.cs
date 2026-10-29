using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSRealRuntimeSmokeTests
    {
        [Test]
        [Category("Smoke")]
        public void EOSRuntime_Initialize_RunCallbacks_Shutdown_Smoke()
        {
            var smokeEnabled = string.Equals(
                Environment.GetEnvironmentVariable("MGNET_EOS_SMOKE"),
                "1",
                StringComparison.Ordinal);

            if (!smokeEnabled)
            {
                Assert.Ignore("EOSRuntime_Initialize_RunCallbacks_Shutdown_Smoke: Requires MGNET_EOS_SMOKE=1 and valid EOS runtime configuration.");
            }

            Assert.That(EOSRuntime.Initialize(), Is.True);
            EOSRuntime.RunCallbacks();
            EOSRuntime.Shutdown();
            Assert.That(EOSRuntime.IsInitialized, Is.False);
        }

        [Test]
        [Category("Smoke")]
        public async Task EOSBackend_HostsALobby_AndClosesIt_Smoke()
        {
            var smokeEnabled = string.Equals(
                Environment.GetEnvironmentVariable("MGNET_EOS_SMOKE"),
                "1",
                StringComparison.Ordinal);

            if (!smokeEnabled)
            {
                Assert.Ignore("EOSBackend_HostsALobby_AndClosesIt_Smoke: Requires MGNET_EOS_SMOKE=1 and valid EOS runtime configuration.");
            }

            // Finding and joining the lobby needs a second Epic account (EOS will not connect an account to
            // itself), so this checks the half one account can: create a lobby through the back-end, then close it.
            EOSRuntime.Initialize(initialPlayerId: "eos-smoke", initialGamertag: "EOSSmoke");
            var original = NetworkServiceProvider.Backend;
            NetworkServiceProvider.SetBackend(new EOSBackend(EOSFallbackMode.Strict));

            NetworkSession host = null;
            try
            {
                host = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null);
                Assert.That(host.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            }
            finally
            {
                host?.Dispose();
                NetworkServiceProvider.SetBackend(original);
                EOSRuntime.Shutdown();
            }
        }
    }
}
