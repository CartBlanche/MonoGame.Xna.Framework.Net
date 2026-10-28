using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class ReliableChannelTests
    {
        // Two channels joined by a simulated network that can lose, duplicate and reorder datagrams
        private sealed class Wire
        {
            public readonly ReliableChannel A, B;
            public readonly List<int> ReceivedByB = new List<int>();

            private readonly Random random;
            private readonly List<byte[]> toA = new List<byte[]>();
            private readonly List<byte[]> toB = new List<byte[]>();
            private readonly double loss;

            public Wire(double loss, int seed)
            {
                this.loss = loss;
                random = new Random(seed);
                A = new ReliableChannel(d => Send(toB, d));
                B = new ReliableChannel(d => Send(toA, d));
            }

            private void Send(List<byte[]> queue, byte[] datagram)
            {
                if (random.NextDouble() < loss)
                    return;
                queue.Add(datagram);
                if (random.NextDouble() < 0.1)
                    queue.Add(datagram);            // duplicate
            }

            public void Pump(double now)
            {
                foreach (byte[] d in Take(toB))
                    B.Receive(d, d.Length, now, p => ReceivedByB.Add(BitConverter.ToInt32(p, 0)));
                foreach (byte[] d in Take(toA))
                    A.Receive(d, d.Length, now, p => { });
                A.Update(now);
                B.Update(now);
            }

            // a random handful of what is in flight, so datagrams arrive out of order
            private List<byte[]> Take(List<byte[]> queue)
            {
                var batch = queue.OrderBy(_ => random.Next()).Take(10).ToList();
                foreach (byte[] d in batch)
                    queue.Remove(d);
                return batch;
            }
        }

        [TestCase(0.0)]
        [TestCase(0.3)]
        [TestCase(0.6)]
        public void ReliableMessages_ArriveExactlyOnceAndInOrder(double loss)
        {
            const int count = 3000;
            var wire = new Wire(loss, 7);

            double now = 0;
            int sent = 0;
            for (int step = 0; step < 200000 && wire.ReceivedByB.Count < count; step++)
            {
                now += 0.016;
                for (int k = 0; k < 3 && sent < count; k++)
                {
                    byte[] payload = BitConverter.GetBytes(sent++);
                    wire.A.SendReliable(payload, payload.Length, now);
                }
                wire.Pump(now);
            }

            Assert.That(wire.ReceivedByB, Is.EqualTo(Enumerable.Range(0, count)));
        }

        [Test]
        public void SequenceNumbers_WrapAroundWithoutLosingMessages()
        {
            ReliableChannel a = null, b = null;
            int delivered = 0;
            a = new ReliableChannel(d => b.Receive(d, d.Length, 0, p => delivered++));
            b = new ReliableChannel(d => a.Receive(d, d.Length, 0, p => { }));

            const int count = 70000;      // more than the 65536 values of the sequence number
            for (int i = 0; i < count; i++)
            {
                byte[] payload = BitConverter.GetBytes(i);
                a.SendReliable(payload, payload.Length, 0);
                if (i % 50 == 0)
                {
                    b.Update(0);
                    a.Update(0);
                }
            }
            b.Update(0);
            a.Update(0);

            Assert.That(delivered, Is.EqualTo(count));
            Assert.That(a.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void UnreliableMessages_PassStraightThrough()
        {
            var wire = new List<byte[]>();
            var sender = new ReliableChannel(wire.Add);
            var receiver = new ReliableChannel(d => { });

            sender.SendUnreliable(new byte[] { 9, 8, 7, 0, 0 }, 3);    // only the first 3 bytes count
            Assert.That(sender.PendingCount, Is.EqualTo(0));

            var got = new List<byte[]>();
            receiver.Receive(wire[0], wire[0].Length, 0, got.Add);
            Assert.That(got.Single(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        }

        [Test]
        public void ASilentPeer_MakesTheOldestUnackedAgeGrow()
        {
            var channel = new ReliableChannel(d => { });
            Assert.That(channel.OldestUnackedAge(100), Is.EqualTo(0));

            channel.SendReliable(new byte[] { 1 }, 1, 10);
            Assert.That(channel.OldestUnackedAge(15), Is.EqualTo(5).Within(1e-6));
        }

        [Test]
        public void UnacknowledgedMessages_AreResent()
        {
            int sends = 0;
            var channel = new ReliableChannel(d => sends++);

            channel.SendReliable(new byte[] { 1 }, 1, 0);
            Assert.That(sends, Is.EqualTo(1));

            channel.Update(ReliableChannel.ResendInterval / 2);
            Assert.That(sends, Is.EqualTo(1), "too soon to resend");

            channel.Update(ReliableChannel.ResendInterval + 0.01);
            Assert.That(sends, Is.EqualTo(2));
        }

        [Test]
        public void GarbageDatagrams_AreIgnored()
        {
            var channel = new ReliableChannel(d => { });
            int delivered = 0;

            channel.Receive(new byte[0], 0, 0, p => delivered++);
            channel.Receive(new byte[] { ReliableChannel.ChannelReliable }, 1, 0, p => delivered++);
            channel.Receive(new byte[] { ReliableChannel.ChannelAck, 1 }, 2, 0, p => delivered++);
            channel.Receive(new byte[] { 0x7F, 1, 2, 3 }, 4, 0, p => delivered++);

            Assert.That(delivered, Is.EqualTo(0));
        }

        [Test]
        public void ChannelBytes_StayClearOfTheFrameworkMessageIds()
        {
            for (byte b = 0; b <= 10; b++)
                Assert.That(ReliableChannel.IsChannelByte(b), Is.False, "byte " + b);

            Assert.That(ReliableChannel.IsChannelByte(ReliableChannel.ChannelUnreliable), Is.True);
            Assert.That(ReliableChannel.IsChannelByte(ReliableChannel.ChannelReliable), Is.True);
            Assert.That(ReliableChannel.IsChannelByte(ReliableChannel.ChannelAck), Is.True);
            Assert.That(ReliableChannel.IsChannelByte(0x24), Is.False);
        }

        [Test]
        public void SendingReceivingAndResending_FromDifferentThreads_StillDeliversEverythingInOrder()
        {
            // the receive loop, the game thread and the resend timer all use a channel at once
            const int count = 5000;
            ReliableChannel a = null, b = null;
            var received = new List<int>();
            var receivedLock = new object();

            a = new ReliableChannel(d => b.Receive(d, d.Length, Now(), p => { lock (receivedLock) received.Add(BitConverter.ToInt32(p, 0)); }));
            b = new ReliableChannel(d => a.Receive(d, d.Length, Now(), p => { }));

            using (var stop = new CancellationTokenSource())
            {
                var ticker = Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        a.Update(Now());
                        b.Update(Now());
                        Thread.Sleep(1);
                    }
                });

                for (int i = 0; i < count; i++)
                {
                    byte[] payload = BitConverter.GetBytes(i);
                    a.SendReliable(payload, payload.Length, Now());
                }

                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline && a.PendingCount > 0)
                    Thread.Sleep(5);

                stop.Cancel();
                ticker.Wait();
            }

            lock (receivedLock)
                Assert.That(received, Is.EqualTo(Enumerable.Range(0, count)));
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static double Now() { return Clock.Elapsed.TotalSeconds; }
    }
}
