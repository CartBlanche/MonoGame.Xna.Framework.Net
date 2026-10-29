using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>The framework logs through <see cref="NetworkLogging.Logger"/> unless a session is given its own logger.</summary>
    [TestFixture]
    public class LoggingTests
    {
        private sealed class CapturingLogger : INetworkLogger
        {
            public readonly List<string> Lines = new List<string>();
            public void LogInfo(string message) { lock (Lines) Lines.Add("info: " + message); }
            public void LogWarning(string message) { lock (Lines) Lines.Add("warn: " + message); }
            public void LogError(string message, Exception ex = null) { lock (Lines) Lines.Add("error: " + message); }
            public bool Saw(string text) { lock (Lines) return Lines.Any(l => l.Contains(text)); }
        }

        private sealed class NullTransport : IPeerTransport
        {
            public bool IsBound => true;
            public PeerId LocalPeer => PeerId.None;
            public void Bind() { }
            public void Send(PeerId peer, byte[] data, int length) { }
            public Task<(byte[] Data, PeerId Sender)> ReceiveAsync() => new TaskCompletionSource<(byte[], PeerId)>().Task;
            public void Dispose() { }
        }

        private INetworkLogger original;

        [SetUp]
        public void SetUp() => original = NetworkLogging.Logger;

        [TearDown]
        public void TearDown() => NetworkLogging.Logger = original;

        private static async Task<NetworkSession> HostAsync()
        {
            var host = await NetworkSession.CreateAsync(NetworkSessionType.Local, 1, 4, 0, new Dictionary<string, object>());
            host.NetworkTransport = new NullTransport();
            host.ProtectLinks = false;
            return host;
        }

        private static void Join(NetworkSession host) =>
            host.DispatchIncomingMessage(new MessageReceivedEventArgs(
                new JoinRequestMessage { GamerId = "p2", Gamertag = "Player 2" }, UdpTransport.ToPeer(new IPEndPoint(IPAddress.Loopback, 40001))));

        [Test]
        public async Task ASession_LogsThroughTheFrameworkLogger_EvenOneChosenAfterTheSessionWasMade()
        {
            var host = await HostAsync();
            var logger = new CapturingLogger();
            NetworkLogging.Logger = logger;

            Join(host);

            Assert.That(logger.Saw("JoinAcceptedMessage"), Is.True);
        }

        [Test]
        public async Task ASessionWithItsOwnLogger_DoesNotUseTheFrameworkLogger()
        {
            var host = await HostAsync();
            var shared = new CapturingLogger();
            var own = new CapturingLogger();
            NetworkLogging.Logger = shared;
            host.Logger = own;

            Join(host);

            Assert.That(own.Saw("JoinAcceptedMessage"), Is.True);
            Assert.That(shared.Lines, Is.Empty);
        }

        [Test]
        public async Task SettingTheLoggerToNull_SilencesTheFramework_WithoutThrowing()
        {
            var host = await HostAsync();
            NetworkLogging.Logger = null;

            Assert.That(NetworkLogging.Logger, Is.InstanceOf<NullNetworkLogger>());
            Assert.DoesNotThrow(() => Join(host));
        }
    }
}
