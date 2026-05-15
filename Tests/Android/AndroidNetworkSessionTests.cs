using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net.Android;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class AndroidNetworkSessionTests
    {
        private const byte TestMessageType = 244;

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
            AndroidRuntime.Shutdown();
            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);
        }

        [TearDown]
        public void TearDown()
        {
            AndroidRuntime.Shutdown();
        }

        [Test]
        public async Task AndroidFactory_HostJoinAndReliableMessage_EndToEnd()
        {
            AndroidRuntime.Initialize(androidActivity: null, initialPlayerId: "android-1", initialGamertag: "AndroidHost");

            var factory = new AndroidNetworkSessionFactory();
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

                host.BroadcastMessage(new TestReliableMessage { Payload = "android-e2e" });
                client.Update(new GameTime());

                Assert.That(receivedPayload, Is.EqualTo("android-e2e"));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }

        [Test]
        public void AndroidFactory_StrictMode_FindSessionsWithoutRuntime_Throws()
        {
            var factory = new AndroidNetworkSessionFactory(fallbackMode: AndroidFallbackMode.Strict);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await factory.FindSessionsAsync(NetworkSessionType.SystemLink);
            });
        }

        [Test]
        public void AndroidFactory_StrictMode_CreateSessionWithoutRuntime_Throws()
        {
            var factory = new AndroidNetworkSessionFactory(fallbackMode: AndroidFallbackMode.Strict);

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
        public async Task AndroidFactory_WhenHostCloses_ClientEndsWithHostEndedSession()
        {
            AndroidRuntime.Initialize(androidActivity: null, initialPlayerId: "android-2", initialGamertag: "AndroidHost");

            var factory = new AndroidNetworkSessionFactory();
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
        public async Task AndroidFactory_WhenClientCloses_HostStaysActiveAndGetsGamerLeft()
        {
            AndroidRuntime.Initialize(androidActivity: null, initialPlayerId: "android-3", initialGamertag: "AndroidHost");

            var factory = new AndroidNetworkSessionFactory();
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
        public async Task AndroidProvider_CreateSession_DisablesHostMigration()
        {
            var factory = new AndroidNetworkSessionFactory();

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