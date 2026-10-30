using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// What a third party can do with INetworkMessage from outside the library: only the public API is used here.
    /// </summary>
    [TestFixture]
    public class CustomMessageTests
    {
        const byte CustomId = 200;

        public class ScoreMessage : INetworkMessage
        {
            public byte MessageType => CustomId;
            public int Score { get; set; }
            public void Serialize(PacketWriter writer) { writer.Write(MessageType); writer.Write(Score); }
            public void Deserialize(PacketReader reader) { Score = reader.ReadInt32(); }
        }

        [Test]
        public void ACustomMessage_CanBeRegistered_AndCreatedByItsId()
        {
            NetworkMessageRegistry.Register<ScoreMessage>(CustomId);
            NetworkMessageRegistry.Register<ScoreMessage>(CustomId);   // again: harmless

            Assert.That(NetworkMessageRegistry.CreateMessage(CustomId), Is.TypeOf<ScoreMessage>());
        }

        [Test]
        public void ACustomMessage_CannotTakeAnIdBelow128()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkMessageRegistry.Register<ScoreMessage>(2));
        }

        public class LowIdMessage : INetworkMessage
        {
            public byte MessageType => 5;
            public void Serialize(PacketWriter writer) { writer.Write(MessageType); }
            public void Deserialize(PacketReader reader) { }
        }

        [Test]
        public async Task AMessageFromTheHost_ArrivesAtTheClient_AsMessageReceived()
        {
            NetworkMessageRegistry.Register<ScoreMessage>(CustomId);
            using (var lan = new SessionHarness())
            {
                NetworkSession host = await lan.StartHostAsync();
                NetworkSession client = await lan.JoinClientAsync();
                Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2 && client.AllGamers.Count == 2));

                ScoreMessage received = null;
                NetworkGamer sender = null;
                client.MessageReceived += (s, e) => { if (e.Message is ScoreMessage m) { received = m; sender = e.Sender; } };

                host.SendMessage(new ScoreMessage { Score = 42 }, SendDataOptions.ReliableInOrder);

                Assert.That(await lan.WaitUntilAsync(() => received != null), "MessageReceived never fired for the custom message");
                Assert.That(received.Score, Is.EqualTo(42));
                Assert.That(sender, Is.Not.Null, "the message does not say who sent it");
                Assert.That(sender.IsHost, Is.True);
            }
        }

        [Test]
        public async Task AMessageFromAClient_ArrivesAtTheHost_AndToOneGamerToo()
        {
            NetworkMessageRegistry.Register<ScoreMessage>(CustomId);
            using (var lan = new SessionHarness())
            {
                NetworkSession host = await lan.StartHostAsync();
                NetworkSession client = await lan.JoinClientAsync();
                Assert.That(await lan.WaitUntilAsync(() => host.AllGamers.Count == 2 && client.AllGamers.Count == 2));

                ScoreMessage atHost = null;
                ScoreMessage atClient = null;
                NetworkGamer senderAtHost = null;
                host.MessageReceived += (s, e) => { if (e.Message is ScoreMessage m) { atHost = m; senderAtHost = e.Sender; } };
                client.MessageReceived += (s, e) => { if (e.Message is ScoreMessage m) atClient = m; };

                client.SendMessage(new ScoreMessage { Score = 7 }, SendDataOptions.ReliableInOrder, client.AllGamers.Host);
                Assert.That(await lan.WaitUntilAsync(() => atHost != null), "the host never got the client's message");
                Assert.That(atHost.Score, Is.EqualTo(7));
                Assert.That(senderAtHost, Is.SameAs(host.RemoteGamers[0]), "the host cannot tell which client sent it");

                host.SendMessage(new ScoreMessage { Score = 9 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
                Assert.That(await lan.WaitUntilAsync(() => atClient != null), "the client never got the host's message");
                Assert.That(atClient.Score, Is.EqualTo(9));
            }
        }

        [Test]
        public async Task AClient_CannotSendAMessageToAnotherClient_AndOnlyCustomIdsCanBeSent()
        {
            using (var lan = new SessionHarness())
            {
                NetworkSession host = await lan.StartHostAsync(maxGamers: 4);
                NetworkSession a = await lan.JoinClientAsync();
                NetworkSession b = await lan.JoinClientAsync();
                Assert.That(await lan.WaitUntilAsync(() => lan.All.All(x => x.AllGamers.Count == 3)));

                NetworkGamer bSeenByA = a.RemoteGamers.Single(g => !g.IsHost);
                Assert.Throws<InvalidOperationException>(() => a.SendMessage(new ScoreMessage(), SendDataOptions.Reliable, bSeenByA));
                Assert.Throws<ArgumentException>(() => host.SendMessage(new LowIdMessage(), SendDataOptions.Reliable));
            }
        }
    }
}
