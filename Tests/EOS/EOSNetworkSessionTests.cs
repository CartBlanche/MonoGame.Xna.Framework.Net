using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSNetworkSessionTests
    {
        [SetUp]
        public void Setup()
        {
            EOSRuntime.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            EOSRuntime.Shutdown();
        }

        [Test]
        [Category("Smoke")]
        public async Task EOSFactory_HostFindAndJoin_EndToEnd()
        {
            var smokeEnabled = string.Equals(
                Environment.GetEnvironmentVariable("MGNET_EOS_SMOKE"),
                "1",
                StringComparison.Ordinal);

            if (!smokeEnabled)
            {
                Assert.Ignore("EOSFactory_HostFindAndJoin_EndToEnd: Requires MGNET_EOS_SMOKE=1 and valid EOS runtime configuration.");
            }

            EOSRuntime.Initialize(initialPlayerId: "eos-1", initialGamertag: "EOSHost");

            var factory = new EOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);

                SessionInfo sessionInfo = null;
                for (var attempt = 0; attempt < 60 && sessionInfo == null; attempt++)
                {
                    host.Update(new GameTime());
                    client.Update(new GameTime());

                    var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink)).ToList();
                    sessionInfo = sessions.FirstOrDefault();
                    if (sessionInfo == null)
                    {
                        await Task.Delay(100);
                    }
                }

                Assert.That(sessionInfo, Is.Not.Null);

                await client.JoinAsync(sessionInfo.JoinAddress);

                for (var attempt = 0; attempt < 80; attempt++)
                {
                    host.Update(new GameTime());
                    client.Update(new GameTime());

                    if (host.AllGamers.Count == 2 && client.AllGamers.Count == 2)
                    {
                        break;
                    }

                    await Task.Delay(100);
                }

                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
                Assert.That(client.AllGamers.Count, Is.EqualTo(2));
                Assert.That(host.State, Is.EqualTo(NetworkSessionState.Lobby));
                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Lobby));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }
    }
}
