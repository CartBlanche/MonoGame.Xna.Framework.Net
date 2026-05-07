using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Net.Steam;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class SteamNetworkSessionTests
    {
        private const byte TestMessageType = 240;

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
            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);
        }

        [Test]
        public async Task SteamFactory_HostJoinAndReliableMessage_EndToEnd()
        {
            var factory = new SteamNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            var hostStartedCount = 0;
            var clientStartedCount = 0;

            host.GameStarted += (_, __) => hostStartedCount++;
            client.GameStarted += (_, __) => clientStartedCount++;

            try
            {
                await host.CreateAsync(NetworkSessionType.PlayerMatch, maxGamers: 4, privateGamerSlots: 0);

                var sessions = (await factory.FindSessionsAsync(NetworkSessionType.PlayerMatch)).ToList();
                Assert.That(sessions.Count, Is.EqualTo(1));

                await client.JoinAsync(sessions[0].JoinAddress);

                Assert.That(host.AllGamers.Count, Is.EqualTo(2));
                Assert.That(client.AllGamers.Count, Is.EqualTo(2));
                Assert.That(host.State, Is.EqualTo(NetworkSessionState.Playing));
                Assert.That(client.State, Is.EqualTo(NetworkSessionState.Playing));
                Assert.That(hostStartedCount, Is.EqualTo(1));
                Assert.That(clientStartedCount, Is.EqualTo(1));

                var receivedPayload = (string)null;
                client.MessageReceived += (sender, args) =>
                {
                    if (args.Message is TestReliableMessage testMessage)
                    {
                        receivedPayload = testMessage.Payload;
                    }
                };

                host.BroadcastMessage(new TestReliableMessage { Payload = "steam-e2e" });
                client.Update(new GameTime());

                Assert.That(receivedPayload, Is.EqualTo("steam-e2e"));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
            }
        }

        [Test]
        public void SteamFactory_StrictMode_FindSessionsWithoutSteam_Throws()
        {
            var factory = new SteamNetworkSessionFactory(fallbackMode: SteamFallbackMode.Strict);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await factory.FindSessionsAsync(NetworkSessionType.PlayerMatch);
            });
        }

        [Test]
        public async Task SteamSession_StrictMode_CreateWithoutSteam_Throws()
        {
            var session = new SteamNetworkSession(SteamFallbackMode.Strict);

            try
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await session.CreateAsync(NetworkSessionType.PlayerMatch, maxGamers: 4, privateGamerSlots: 0);
                });
            }
            finally
            {
                await session.CloseAsync();
            }
        }

        [Test]
        public async Task SteamFactory_WhenHostCloses_ClientEndsWithHostEndedSession()
        {
            var factory = new SteamNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            NetworkSessionEndReason? clientEndReason = null;

            client.SessionEnded += (_, args) => clientEndReason = args.EndReason;

            try
            {
                await host.CreateAsync(NetworkSessionType.PlayerMatch, maxGamers: 4, privateGamerSlots: 0);
                var sessions = (await factory.FindSessionsAsync(NetworkSessionType.PlayerMatch)).ToList();
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
        public async Task SteamFactory_WhenClientCloses_HostStaysActiveAndGetsGamerLeft()
        {
            var factory = new SteamNetworkSessionFactory();
            var host = factory.CreateSession();
            var client = factory.CreateSession();
            var hostGamerLeftCount = 0;

            host.GamerLeft += (_, __) => hostGamerLeftCount++;

            try
            {
                await host.CreateAsync(NetworkSessionType.PlayerMatch, maxGamers: 4, privateGamerSlots: 0);
                var sessions = (await factory.FindSessionsAsync(NetworkSessionType.PlayerMatch)).ToList();
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
    }
}
