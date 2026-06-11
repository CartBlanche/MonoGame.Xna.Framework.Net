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
        public async Task EOSFactory_HostFindAndJoin_EndToEnd()
        {
            EOSRuntime.Initialize(initialPlayerId: "eos-1", initialGamertag: "EOSHost");

            var factory = new EOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);

                SessionInfo sessionInfo = null;
                for (var attempt = 0; attempt < 8 && sessionInfo == null; attempt++)
                {
                    var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink)).ToList();
                    sessionInfo = sessions.FirstOrDefault();
                    if (sessionInfo == null)
                    {
                        await Task.Delay(150);
                    }
                }

                Assert.That(sessionInfo, Is.Not.Null);

                await client.JoinAsync(sessionInfo.JoinAddress);

                for (var attempt = 0; attempt < 20; attempt++)
                {
                    host.Update(new GameTime());
                    client.Update(new GameTime());

                    if (host.AllGamers.Count == 2 && client.AllGamers.Count == 2)
                    {
                        break;
                    }

                    await Task.Delay(50);
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
