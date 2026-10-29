using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Real sockets on the loopback: a host and a joiner with a session password, and somebody else on the wire.
    /// </summary>
    [TestFixture]
    public class SecureLinkTests
    {
        /// <summary>A UDP transport that remembers everything it sent, to see what an eavesdropper would.</summary>
        private sealed class SniffedTransport : IPeerTransport
        {
            private readonly UdpTransport inner = new UdpTransport();
            public readonly List<byte[]> Sent = new List<byte[]>();
            public bool IsBound => inner.IsBound;
            public PeerId LocalPeer => inner.LocalPeer;
            public int Port => inner.LocalEndPoint.Port;
            public void Bind() => inner.Bind();
            public void Send(PeerId peer, byte[] data, int length)
            {
                lock (Sent) Sent.Add(data.Take(length).ToArray());
                inner.Send(peer, data, length);
            }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => inner.ReceiveAsync();
            public void Dispose() => inner.Dispose();
            public byte[][] Snapshot() { lock (Sent) return Sent.ToArray(); }
        }

        private sealed class LoopbackBackend : INetworkBackend
        {
            public readonly List<SniffedTransport> Transports = new List<SniffedTransport>();
            public SniffedTransport HostTransport;
            public ISessionAdvertiser Host;

            public string Name => "Loopback";
            public bool SupportsSessionType(NetworkSessionType sessionType) => true;
            public event EventHandler<InviteReceivedEventArgs> InviteReceived { add { } remove { } }

            public IPeerTransport CreateTransport()
            {
                var transport = new SniffedTransport();
                lock (Transports) Transports.Add(transport);
                return transport;
            }

            public Task AdvertiseAsync(ISessionAdvertiser session, IPeerTransport transport, CancellationToken stopToken)
            {
                Host = session;
                HostTransport = (SniffedTransport)transport;
                transport.Bind();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<AvailableNetworkSession>> FindAsync(NetworkSessionType sessionType, int maxLocalGamers, CancellationToken cancellationToken)
            {
                SessionAdvertisement ad = Host.GetAdvertisement();
                var found = new AvailableNetworkSession("s", ad.HostGamertag, ad.CurrentGamerCount, ad.MaxGamers - ad.CurrentGamerCount, 0,
                    sessionType, new Dictionary<string, object>(), ad.SessionId,
                    UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, HostTransport.Port)), ad.IsPasswordProtected);
                return Task.FromResult<IReadOnlyList<AvailableNetworkSession>>(new List<AvailableNetworkSession> { found });
            }

            public Task<IDisposable> PrepareJoinAsync(AvailableNetworkSession session, IPeerTransport transport, CancellationToken cancellationToken) =>
                Task.FromResult<IDisposable>(null);
        }

        private INetworkBackend original;
        private LoopbackBackend backend;
        private readonly List<NetworkSession> sessions = new List<NetworkSession>();

        [SetUp]
        public void SetUp()
        {
            original = NetworkServiceProvider.Backend;
            backend = new LoopbackBackend();
            NetworkServiceProvider.SetBackend(backend);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var session in sessions)
                session.Dispose();
            sessions.Clear();
            NetworkServiceProvider.SetBackend(original);
        }

        private async Task<NetworkSession> HostAsync(string password)
        {
            var host = password == null
                ? await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null)
                : await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, null, password);
            sessions.Add(host);
            return host;
        }

        private async Task<AvailableNetworkSession> FindAsync() =>
            (await backend.FindAsync(NetworkSessionType.SystemLink, 1, CancellationToken.None)).Single();

        private async Task<NetworkSession> JoinAsync(string password)
        {
            var joined = await NetworkSession.JoinAsync(await FindAsync(), password);
            sessions.Add(joined);
            return joined;
        }

        private static async Task<bool> PumpUntil(Func<bool> done, params NetworkSession[] toUpdate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var session in toUpdate)
                    session.Update();
                if (done())
                    return true;
                await Task.Delay(10);
            }
            return false;
        }

        private static byte[] Receive(NetworkSession session)
        {
            var buffer = new byte[256];
            int length = session.LocalGamers[0].ReceiveData(buffer, out _);
            return buffer.Take(length).ToArray();
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                    return true;
            return false;
        }

        [Test]
        public async Task AdvertisementSaysWhetherThereIsAPassword()
        {
            await HostAsync("hunter2");
            Assert.That((await FindAsync()).IsPasswordProtected, Is.True);
        }

        [Test]
        public async Task AnOpenSession_IsNotPasswordProtected()
        {
            await HostAsync(null);
            Assert.That((await FindAsync()).IsPasswordProtected, Is.False);
        }

        [Test]
        public async Task JoiningWithThePassword_Works_AndDataFlowsBothWays()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(client.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            host.LocalGamers[0].SendData(new byte[] { 1, 2, 3, 4 }, SendDataOptions.ReliableInOrder, host.RemoteGamers[0]);
            Assert.That(await PumpUntil(() => client.LocalGamers[0].IsDataAvailable, host, client), Is.True);
            Assert.That(Receive(client), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

            client.LocalGamers[0].SendData(new byte[] { 9, 8, 7 }, SendDataOptions.ReliableInOrder, client.Host);
            Assert.That(await PumpUntil(() => host.LocalGamers[0].IsDataAvailable, host, client), Is.True);
            Assert.That(Receive(host), Is.EqualTo(new byte[] { 9, 8, 7 }));
        }

        [Test]
        public async Task JoiningWithTheWrongPassword_IsRefused()
        {
            var host = await HostAsync("hunter2");
            var thrown = Assert.ThrowsAsync<NetworkSessionJoinException>(() => JoinAsync("nope"));
            Assert.That(thrown.JoinError, Is.EqualTo(NetworkSessionJoinError.WrongPassword));
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task JoiningWithoutAPassword_IsToldOneIsNeeded()
        {
            var host = await HostAsync("hunter2");
            var thrown = Assert.ThrowsAsync<NetworkSessionJoinException>(() => JoinAsync(null));
            Assert.That(thrown.JoinError, Is.EqualTo(NetworkSessionJoinError.PasswordRequired));
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task OnTheWire_NeitherThePassword_NorTheGameData_AppearsInTheClear()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            byte[] secret = Encoding.ASCII.GetBytes("TOP-SECRET-PAYLOAD");
            host.LocalGamers[0].SendData(secret, SendDataOptions.ReliableInOrder);
            Assert.That(await PumpUntil(() => client.LocalGamers[0].IsDataAvailable, host, client), Is.True);

            byte[][] everything;
            lock (backend.Transports) everything = backend.Transports.SelectMany(t => t.Snapshot()).ToArray();
            Assert.That(everything, Is.Not.Empty);
            Assert.That(everything.Any(d => Contains(d, secret)), Is.False, "game data crossed the wire unencrypted");
            Assert.That(everything.Any(d => Contains(d, Encoding.UTF8.GetBytes("hunter2"))), Is.False, "the password crossed the wire");
        }

        [Test]
        public async Task AStranger_CannotInjectData_IntoAProtectedLink()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            // an off-path forger copies what a real datagram looks like but cannot make it authenticate
            var hostEndpoint = new IPEndPoint(IPAddress.Loopback, backend.HostTransport.Port);
            var random = new Random(7);
            using (var stranger = new UdpClient(0))
            {
                for (int i = 0; i < 200; i++)
                {
                    var forged = new byte[random.Next(LinkProtector.Overhead, 200)];
                    random.NextBytes(forged);
                    forged[0] = LinkProtector.FrameType;
                    stranger.Send(forged, forged.Length, hostEndpoint);
                }
            }

            await PumpUntil(() => false, host, client);   // let it all arrive (the pump runs out its 5 seconds)
            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False);
            Assert.That(host.SessionState, Is.EqualTo(NetworkSessionState.Lobby));
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task AClientThatSaysGoodbye_IsGoneFromTheHostAtOnce([Values(false, true)] bool dispose)
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            var left = new List<string>();
            host.GamerLeft += (s, e) => left.Add(e.Gamer.Gamertag);

            if (dispose)
                client.Dispose();
            else
                client.Leave("bye");

            Assert.That(await PumpUntil(() => left.Count == 1, host), Is.True, "a goodbye must reach the host within a moment, not after a timeout");
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task AHostThatDisposes_EndsTheClientsSessionAtOnce()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            NetworkSessionEndReason? reason = null;
            client.SessionEnded += (s, e) => reason = e.EndReason;

            host.Dispose();

            Assert.That(await PumpUntil(() => reason != null, client), Is.True, "the client should not have to wait for a timeout");
            Assert.That(reason, Is.EqualTo(NetworkSessionEndReason.HostEndedSession));
        }

        [Test]
        public async Task AClientThatCrashes_IsDroppedByTheHost_AndTheHostIsTold()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            var left = new List<string>();
            host.GamerLeft += (s, e) => left.Add(e.Gamer.Gamertag);

            // a crash: the process is gone, so nothing more is sent and no goodbye is said
            client.NetworkTransport.Dispose();

            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (left.Count == 0 && DateTime.UtcNow < deadline)
            {
                host.Update();
                await Task.Delay(50);
            }

            Assert.That(left, Has.Count.EqualTo(1), "the host never noticed the client had gone");
            Assert.That(host.RemoteGamers.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task ATamperedDatagram_IsDropped_AndTheLinkKeepsWorking()
        {
            var host = await HostAsync("hunter2");
            var client = await JoinAsync("hunter2");
            Assert.That(await PumpUntil(() => host.RemoteGamers.Count == 1, host, client), Is.True);

            // a copy of a real client-to-host datagram with one bit flipped, sent from the client's own address
            client.LocalGamers[0].SendData(new byte[] { 5, 5, 5 }, SendDataOptions.ReliableInOrder);
            Assert.That(await PumpUntil(() => host.LocalGamers[0].IsDataAvailable, host, client), Is.True);
            Receive(host);

            var clientTransport = backend.Transports.Single(t => t != backend.HostTransport);
            byte[] real = clientTransport.Snapshot().Last(d => d[0] == LinkProtector.FrameType);
            var tampered = (byte[])real.Clone();
            tampered[tampered.Length / 2] ^= 0x01;
            clientTransport.Send(UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, backend.HostTransport.Port)), tampered, tampered.Length);

            await Task.Delay(200);
            host.Update();
            Assert.That(host.LocalGamers[0].IsDataAvailable, Is.False);

            client.LocalGamers[0].SendData(new byte[] { 6 }, SendDataOptions.ReliableInOrder);
            Assert.That(await PumpUntil(() => host.LocalGamers[0].IsDataAvailable, host, client), Is.True);
            Assert.That(Receive(host), Is.EqualTo(new byte[] { 6 }));
        }
    }
}
