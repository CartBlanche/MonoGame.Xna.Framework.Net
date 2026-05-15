using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net.iOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class IOSNetworkSessionTests
    {
        private const byte TestMessageType = 245;

        private sealed class TestReliableMessage : INetworkMessage
        {
            public byte MessageType => TestMessageType;

            public string Payload { get; set; }

            public void Serialize(PacketWriter writer)
            {
                writer.Write(Payload ?? string.Empty);
            }

            public void Deserialize(PacketReader reader)
            {
                Payload = reader.ReadString();
            }
        }

        [SetUp]
        public void Setup()
        {
            IOSRuntime.Shutdown();
            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);
        }

        [TearDown]
        public void TearDown()
        {
            IOSRuntime.Shutdown();
        }

        [Test]
        public async Task IOSFactory_HostJoinAndReliableMessage_EndToEnd()
        {
            IOSRuntime.Initialize(initialPlayerId: "ios-1", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
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

                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
                Assert.That(client.AllGamers.Count, Is.EqualTo(2));
                Assert.That(host.State, Is.EqualTo(NetworkSessionState.Lobby));
                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Lobby));

                var receivedPayload = (string)null;
                client.MessageReceived += (_, args) =>
                {
                    if (args.Message is TestReliableMessage message)
                    {
                        receivedPayload = message.Payload;
                    }
                };

                host.BroadcastMessage(new TestReliableMessage { Payload = "ios-e2e" });
                client.Update(new GameTime());

                Assert.That(receivedPayload, Is.EqualTo("ios-e2e"));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }

        [Test]
        public void IOSFactory_StrictMode_FindSessionsWithoutRuntime_Throws()
        {
            var factory = new IOSNetworkSessionFactory(fallbackMode: IOSFallbackMode.Strict);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await factory.FindSessionsAsync(NetworkSessionType.SystemLink);
            });
        }

        [Test]
        public void IOSFactory_StrictMode_CreateSessionWithoutRuntime_Throws()
        {
            var factory = new IOSNetworkSessionFactory(fallbackMode: IOSFallbackMode.Strict);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await factory.CreateSessionAsync(
                    NetworkSessionType.SystemLink,
                    maxLocalGamers: 1,
                    maxGamers: 4,
                    privateGamerSlots: 0,
                    sessionProperties: null);
            });
        }

        [Test]
        public async Task IOSFactory_WhenHostCloses_ClientEndsWithHostEndedSession()
        {
            IOSRuntime.Initialize(initialPlayerId: "ios-2", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            NetworkSessionEndReason? clientEndReason = null;

            client.SessionEnded += (_, args) => clientEndReason = args.EndReason;

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);
                var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink)).ToList();
                await client.JoinAsync(sessions[0].JoinAddress);

                await host.CloseAsync();

                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Ended));
                Assert.That(clientEndReason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }

        [Test]
        public async Task IOSFactory_WhenClientCloses_HostStaysActiveAndGetsGamerLeft()
        {
            IOSRuntime.Initialize(initialPlayerId: "ios-3", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            var hostGamerLeftCount = 0;

            host.GamerLeft += (_, __) => hostGamerLeftCount++;

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);
                var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink)).ToList();
                await client.JoinAsync(sessions[0].JoinAddress);

                await client.CloseAsync();

                Assert.That(host.State, Is.Not.EqualTo(NetworkSessionState.Ended));
                Assert.That(hostGamerLeftCount, Is.EqualTo(1));
                Assert.That(host.AllGamers.Count, Is.EqualTo(1));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }

        [Test]
        public async Task IOSProvider_CreateSession_DisablesHostMigration()
        {
            var factory = new IOSNetworkSessionFactory();

            var session = await factory.CreateSessionAsync(
                NetworkSessionType.SystemLink,
                maxLocalGamers: 1,
                maxGamers: 4,
                privateGamerSlots: 0,
                sessionProperties: null);

            try
            {
                Assert.That(session.AllowHostMigration, Is.False);
            }
            finally
            {
                await session.DisposeAsync();
            }
        }
    }
}