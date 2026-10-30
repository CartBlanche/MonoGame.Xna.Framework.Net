using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class NetworkMessageRegistryTests
    {
        private sealed class FirstMessage : INetworkMessage
        {
            public byte MessageType => 200;
            public void Serialize(PacketWriter writer) { }
            public void Deserialize(PacketReader reader) { }
        }

        private sealed class SecondMessage : INetworkMessage
        {
            public byte MessageType => 200;
            public void Serialize(PacketWriter writer) { }
            public void Deserialize(PacketReader reader) { }
        }

        [Test]
        public void RegisteringTheSameTypeAgain_IsHarmless()
        {
            NetworkMessageRegistry.Register<FirstMessage>(200);
            Assert.DoesNotThrow(() => NetworkMessageRegistry.Register<FirstMessage>(200));
            Assert.That(NetworkMessageRegistry.CreateMessage(200), Is.InstanceOf<FirstMessage>());
        }

        [Test]
        public void ADifferentTypeOnATakenId_IsRejected()
        {
            NetworkMessageRegistry.Register<FirstMessage>(201);
            Assert.Throws<InvalidOperationException>(() => NetworkMessageRegistry.Register<SecondMessage>(201));
            Assert.That(NetworkMessageRegistry.CreateMessage(201), Is.InstanceOf<FirstMessage>());
        }

        [Test]
        public void TheFrameworksOwnIds_CannotBeTakenOver()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkMessageRegistry.Register<FirstMessage>(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkMessageRegistry.Register<FirstMessage>(127));
            Assert.That(NetworkMessageRegistry.CreateMessage(2), Is.InstanceOf<JoinRequestMessage>());
        }

        [Test]
        public void ConcurrentRegistrationAndLookup_NeverFails()
        {
            Assert.DoesNotThrow(() => Parallel.For(0, 20000, i =>
            {
                NetworkMessageRegistry.Register<FirstMessage>(202);
                Assert.That(NetworkMessageRegistry.CreateMessage(202), Is.InstanceOf<FirstMessage>());
                Assert.That(NetworkMessageRegistry.CreateMessage(7), Is.InstanceOf<HeartbeatMessage>());
            }));
        }
    }
}
