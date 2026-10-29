using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>The places this library's shape was brought into line with XNA 4.0 (see COMPATIBILITY.md).</summary>
    [TestFixture]
    public class XnaShapeTests
    {
        private sealed class RecordingTransport : IPeerTransport
        {
            public readonly List<byte[]> Sent = new List<byte[]>();
            public bool IsBound => true;
            public PeerId LocalPeer => PeerId.None;
            public void Bind() { }
            public void Send(PeerId peer, byte[] data, int length) { lock (Sent) Sent.Add(data.Take(length).ToArray()); }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => new TaskCompletionSource<(byte[], PeerId)>().Task;
            public void Dispose() { }
            public int Count { get { lock (Sent) return Sent.Count; } }
        }

        private static PeerId Peer(int port) => UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, port));

        private static Task<(NetworkSession Host, RecordingTransport Transport, NetworkGamer Remote)> HostWithGamerAsync()
        {
            // a session that talks over a transport (a Local one delivers in memory)
            var transport = new RecordingTransport();
            var host = new NetworkSession(NetworkSessionType.SystemLink, 8, 0, true, "s", transport);
            host.sessionState = NetworkSessionState.Lobby;
            host.ProtectLinks = false;
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(new JoinRequestMessage { GamerId = "p2", Gamertag = "P2" }, Peer(40001)));
            host.Update();
            return Task.FromResult((host, transport, host.RemoteGamers.Single(g => g.GamerId == "p2")));
        }

        [Test]
        public async Task Chat_CanBeCombinedWithTheDeliveryOptions_AndNothingElseIsAccepted()
        {
            var (host, transport, _) = await HostWithGamerAsync();
            int before = transport.Count;

            Assert.DoesNotThrow(() => host.LocalGamers[0].SendData(new byte[] { 1, 2 }, SendDataOptions.Reliable | SendDataOptions.Chat));
            Assert.DoesNotThrow(() => host.LocalGamers[0].SendData(new byte[] { 1, 2 }, SendDataOptions.ReliableInOrder | SendDataOptions.Chat));
            Assert.DoesNotThrow(() => host.LocalGamers[0].SendData(new byte[] { 1, 2 }, SendDataOptions.Chat));
            Assert.That(transport.Count, Is.GreaterThan(before), "a chat packet must still be sent");

            Assert.Throws<ArgumentOutOfRangeException>(() => host.LocalGamers[0].SendData(new byte[] { 1 }, (SendDataOptions)8));
        }

        [Test]
        public async Task ResetReady_MarksEveryoneNotReady_AndTellsThem()
        {
            var (host, transport, remote) = await HostWithGamerAsync();
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(new ReadinessUpdateMessage { GamerId = "p2", IsReady = true }, Peer(40001)));
            host.LocalGamers[0].IsReady = true;
            Assert.That(remote.IsReady, Is.True);
            Assert.That(host.IsEveryoneReady, Is.True);
            int before = transport.Count;

            host.ResetReady();

            Assert.That(remote.IsReady, Is.False);
            Assert.That(host.LocalGamers[0].IsReady, Is.False);
            Assert.That(host.IsEveryoneReady, Is.False);
            Assert.That(transport.Count, Is.GreaterThan(before), "the other machines must be told");
        }

        [Test]
        public async Task ResetReady_IsForTheHostOnly_AndNotAfterDisposal()
        {
            var client = new NetworkSession(NetworkSessionType.SystemLink, 4, 0, false, "s", new RecordingTransport());
            Assert.Throws<InvalidOperationException>(() => client.ResetReady());

            var (host, _, _) = await HostWithGamerAsync();
            host.Dispose();
            Assert.Throws<ObjectDisposedException>(() => host.ResetReady());
        }

        [Test]
        public async Task ANetworkGamer_IsAGamer()
        {
            var (_, _, remote) = await HostWithGamerAsync();
            Gamer asGamer = remote;

            Assert.That(asGamer.Gamertag, Is.EqualTo("P2"));
            Assert.That(asGamer.DisplayName, Is.EqualTo("P2"));
            asGamer.Tag = "mine";
            Assert.That(remote.Tag, Is.EqualTo("mine"));
        }

        [Test]
        public void QualityOfService_UsesXnasNames()
        {
            var qos = new QualityOfService();
            Assert.That(qos.AverageRoundtripTime, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(qos.MinimumRoundtripTime, Is.LessThanOrEqualTo(qos.AverageRoundtripTime));
            Assert.That(qos.BytesPerSecondDownstream, Is.GreaterThanOrEqualTo(0));
            Assert.That(qos.BytesPerSecondUpstream, Is.GreaterThanOrEqualTo(0));
            Assert.That(qos.IsAvailable, Is.True);
        }

        [Test]
        public void TheseTypes_LiveInGamerServices_AsInXna()
        {
            const string gamerServices = "Microsoft.Xna.Framework.GamerServices";
            Assert.That(typeof(NetworkException).Namespace, Is.EqualTo(gamerServices));
            Assert.That(typeof(GamerPrivilegeException).Namespace, Is.EqualTo(gamerServices));
            Assert.That(typeof(InviteAcceptedEventArgs).Namespace, Is.EqualTo(gamerServices));
        }

        [Test]
        public async Task HostChanged_CanBeSubscribedTo_AndItsArgumentsCarryBothHosts()
        {
            var (host, _, remote) = await HostWithGamerAsync();
            host.HostChanged += (sender, e) => { };

            var args = new HostChangedEventArgs(remote, host.Host);
            Assert.That(args.NewHost, Is.SameAs(remote));
            Assert.That(args.OldHost, Is.SameAs(host.Host));
        }
    }
}
