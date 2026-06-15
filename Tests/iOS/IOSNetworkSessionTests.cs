using System;
using System.Collections.Generic;
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

        private static bool GameKitEnabled() =>
            string.Equals(Environment.GetEnvironmentVariable("MGNET_IOS_GAMEKIT"), "1", StringComparison.Ordinal);

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
        [Category("Smoke")]
        public async Task IOSFactory_HostJoinAndReliableMessage_EndToEnd()
        {
            if (!GameKitEnabled())
                Assert.Ignore("IOSFactory_HostJoinAndReliableMessage_EndToEnd: Requires MGNET_IOS_GAMEKIT=1 and a real iOS device with Game Center authentication.");

            IOSRuntime.Initialize(initialPlayerId: "ios-1", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();

            try
            {
                // Phase 1: no session browser. Host starts matchmaking; client joins by session ID.
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);
                await client.JoinAsync(host.SessionId);

                // Wait for GKMatchmaker to route both peers and for each side to snapshot the other.
                for (var i = 0; i < 50 && (host.AllGamers.Count < 2 || client.AllGamers.Count < 2); i++)
                    await Task.Delay(100);

                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
                Assert.That(client.AllGamers.Count, Is.EqualTo(2));
                Assert.That(host.State, Is.EqualTo(NetworkSessionState.Lobby));
                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Lobby));

                var receivedPayload = (string)null;
                client.MessageReceived += (_, args) =>
                {
                    if (args.Message is TestReliableMessage message)
                        receivedPayload = message.Payload;
                };

                host.BroadcastMessage(new TestReliableMessage { Payload = "ios-e2e" });

                // Wait for the message to travel over GameKit P2P.
                for (var i = 0; i < 50 && receivedPayload == null; i++)
                    await Task.Delay(100);

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

            Assert.Throws<InvalidOperationException>(() =>
            {
                factory.FindSessionsAsync(NetworkSessionType.SystemLink);
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
        [Category("Smoke")]
        public async Task IOSFactory_WhenHostCloses_ClientEndsWithHostEndedSession()
        {
            if (!GameKitEnabled())
                Assert.Ignore("IOSFactory_WhenHostCloses_ClientEndsWithHostEndedSession: Requires MGNET_IOS_GAMEKIT=1 and a real iOS device with Game Center authentication.");

            IOSRuntime.Initialize(initialPlayerId: "ios-2", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            NetworkSessionEndReason? clientEndReason = null;

            client.SessionEnded += (_, args) => clientEndReason = args.EndReason;

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);
                await client.JoinAsync(host.SessionId);

                for (var i = 0; i < 50 && (host.AllGamers.Count < 2 || client.AllGamers.Count < 2); i++)
                    await Task.Delay(100);

                await host.CloseAsync();

                // Wait for GameKit disconnect notification to reach the client.
                for (var i = 0; i < 50 && client.State != NetworkSessionState.Ended; i++)
                    await Task.Delay(100);

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
        [Category("Smoke")]
        public async Task IOSFactory_WhenClientCloses_HostStaysActiveAndGetsGamerLeft()
        {
            if (!GameKitEnabled())
                Assert.Ignore("IOSFactory_WhenClientCloses_HostStaysActiveAndGetsGamerLeft: Requires MGNET_IOS_GAMEKIT=1 and a real iOS device with Game Center authentication.");

            IOSRuntime.Initialize(initialPlayerId: "ios-3", initialGamertag: "IOSHost");

            var factory = new IOSNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            var hostGamerLeftCount = 0;

            host.GamerLeft += (_, __) => hostGamerLeftCount++;

            try
            {
                await host.CreateAsync(NetworkSessionType.SystemLink, maxGamers: 4, privateGamerSlots: 0);
                await client.JoinAsync(host.SessionId);

                for (var i = 0; i < 50 && (host.AllGamers.Count < 2 || client.AllGamers.Count < 2); i++)
                    await Task.Delay(100);

                await client.CloseAsync();

                // Wait for GameKit disconnect notification to reach the host.
                for (var i = 0; i < 50 && hostGamerLeftCount == 0; i++)
                    await Task.Delay(100);

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

        [Test]
        public async Task IOSFactory_FindSessions_ReturnsEmpty_InPhaseOne()
        {
            IOSRuntime.Initialize(initialPlayerId: "ios-4", initialGamertag: "IOSPlayer");

            var factory = new IOSNetworkSessionFactory();
            var sessions = (await factory.FindSessionsAsync(NetworkSessionType.SystemLink)).ToList();

            Assert.That(sessions, Is.Empty);
        }
    }
}
