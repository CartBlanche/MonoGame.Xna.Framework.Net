using Microsoft.Xna.Framework.Net.EOS;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class EOSRealRuntimeSmokeTests
    {
        private const byte TestMessageType = 247;

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
        public async Task EOSNetworking_ReliableMessage_Smoke()
        {
            var smokeEnabled = string.Equals(
                Environment.GetEnvironmentVariable("MGNET_EOS_SMOKE"),
                "1",
                StringComparison.Ordinal);

            if (!smokeEnabled)
            {
                Assert.Ignore("EOSNetworking_ReliableMessage_Smoke: Requires MGNET_EOS_SMOKE=1 and valid EOS runtime configuration.");
            }

            NetworkMessageRegistry.Register<TestReliableMessage>(TestMessageType);
            EOSRuntime.Initialize(initialPlayerId: "eos-smoke", initialGamertag: "EOSSmoke");

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

                var receivedPayload = (string)null;
                client.MessageReceived += (_, args) =>
                {
                    if (args.Message is TestReliableMessage message)
                    {
                        receivedPayload = message.Payload;
                    }
                };

                host.BroadcastMessage(new TestReliableMessage { Payload = "eos-smoke" });

                for (var attempt = 0; attempt < 20 && receivedPayload == null; attempt++)
                {
                    host.Update(new Microsoft.Xna.Framework.GameTime());
                    client.Update(new Microsoft.Xna.Framework.GameTime());
                    if (receivedPayload == null)
                    {
                        await Task.Delay(50);
                    }
                }

                Assert.That(receivedPayload, Is.EqualTo("eos-smoke"));
            }
            finally
            {
                await client.CloseAsync();
                await host.CloseAsync();
                EOSRuntime.Shutdown();
            }
        }
    }
}
