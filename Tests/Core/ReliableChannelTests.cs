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
            public readonly List<byte[]> PayloadsAtB = new List<byte[]>();

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
                    B.Receive(d, d.Length, now, p => { PayloadsAtB.Add(p); if (p.Length >= 4) ReceivedByB.Add(BitConverter.ToInt32(p, 0)); });
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
        public void SequenceNumbers_WrapAroundWithoutLosingMessages_OnALossyReorderingNetwork()
        {
            // the lockstep test above has a perfect network; here the wrap happens while packets are lost,
            // duplicated and reordered, so the ordering arithmetic is exercised across the wrap
            const int count = 70000;
            var wire = new Wire(0.2, 11);

            double now = 0;
            int sent = 0;
            for (int step = 0; step < 400000 && wire.ReceivedByB.Count < count; step++)
            {
                now += 0.016;
                for (int k = 0; k < 3 && sent < count; k++)
                {
                    byte[] payload = BitConverter.GetBytes(sent++);
                    wire.A.SendReliable(payload, payload.Length, now);
                }
                wire.Pump(now);
            }

            Assert.That(wire.ReceivedByB.Count, Is.EqualTo(count));
            Assert.That(wire.ReceivedByB, Is.EqualTo(Enumerable.Range(0, count)));
        }

        // ---- sequenced (InOrder without Reliable) ----

        [Test]
        public void Sequenced_DropsWhatIsOlderThanWhatWasAlreadyDelivered()
        {
            var wire = new List<byte[]>();
            var sender = new ReliableChannel(wire.Add);
            var receiver = new ReliableChannel(d => { });
            for (byte i = 0; i < 4; i++)
                sender.SendSequenced(new byte[] { i }, 1);

            var got = new List<byte>();
            // arrives out of order: 1, 0, 3, 3 again, 2
            foreach (int index in new[] { 1, 0, 3, 3, 2 })
                receiver.Receive(wire[index], wire[index].Length, 0, p => got.Add(p[0]));

            Assert.That(got, Is.EqualTo(new byte[] { 1, 3 }), "only newer data is delivered, once");
            Assert.That(sender.PendingCount, Is.EqualTo(0), "nothing is resent");
        }

        [Test]
        public void Sequenced_KeepsWorkingAcrossTheSequenceWrap()
        {
            var wire = new List<byte[]>();
            var sender = new ReliableChannel(wire.Add);
            var receiver = new ReliableChannel(d => { });
            int delivered = 0;

            for (int i = 0; i < 70000; i++)
            {
                sender.SendSequenced(new byte[] { 1 }, 1);
                receiver.Receive(wire[wire.Count - 1], wire[wire.Count - 1].Length, 0, p => delivered++);
            }

            Assert.That(delivered, Is.EqualTo(70000));
        }

        // ---- fragmentation ----

        static byte[] Message(int index, int size)
        {
            var bytes = new byte[size];
            new Random(index).NextBytes(bytes);
            BitConverter.GetBytes(index).CopyTo(bytes, 0);
            return bytes;
        }

        [TestCase(0.0)]
        [TestCase(0.3)]
        public void LargeMessages_AreSplitUpAndPutBackTogether_InOrderWithSmallOnes(double loss)
        {
            var wire = new Wire(loss, 5);
            var sent = new List<byte[]>();
            int[] sizes = { 10, 5000, 1100, 1101, 2200, 20, 70000, 4 };
            for (int i = 0; i < sizes.Length; i++)
                sent.Add(Message(i, sizes[i]));

            double now = 0;
            foreach (byte[] message in sent)
                wire.A.SendReliable(message, message.Length, now);

            for (int step = 0; step < 200000 && wire.PayloadsAtB.Count < sent.Count; step++)
            {
                now += 0.016;
                wire.Pump(now);
            }

            Assert.That(wire.PayloadsAtB.Count, Is.EqualTo(sent.Count));
            for (int i = 0; i < sent.Count; i++)
                Assert.That(wire.PayloadsAtB[i], Is.EqualTo(sent[i]), "message " + i + " (" + sent[i].Length + " bytes)");
        }

        [Test]
        public void AMessageThatFitsOneDatagram_IsNotFragmented()
        {
            var wire = new List<byte[]>();
            var channel = new ReliableChannel(wire.Add);

            channel.SendReliable(new byte[ReliableChannel.MaxPacketPayload], ReliableChannel.MaxPacketPayload, 0);
            Assert.That(wire.Count, Is.EqualTo(1));
            Assert.That(wire[0][0], Is.EqualTo(ReliableChannel.ChannelReliable));

            wire.Clear();
            channel.SendReliable(new byte[ReliableChannel.MaxPacketPayload + 1], ReliableChannel.MaxPacketPayload + 1, 0);
            Assert.That(wire.Count, Is.EqualTo(2));
            Assert.That(wire.All(d => d[0] == ReliableChannel.ChannelFragment), Is.True);
            Assert.That(wire[0][3], Is.EqualTo(0), "not the last fragment");
            Assert.That(wire[1][3], Is.EqualTo(1), "the last fragment");
        }

        [Test]
        public void MessageSizes_AreLimited()
        {
            var channel = new ReliableChannel(d => { });

            Assert.Throws<ArgumentOutOfRangeException>(() => channel.SendUnreliable(new byte[ReliableChannel.MaxPacketPayload + 1], ReliableChannel.MaxPacketPayload + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.SendSequenced(new byte[ReliableChannel.MaxPacketPayload + 1], ReliableChannel.MaxPacketPayload + 1));
            Assert.DoesNotThrow(() => channel.SendUnreliable(new byte[ReliableChannel.MaxPacketPayload], ReliableChannel.MaxPacketPayload));
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.SendReliable(new byte[ReliableChannel.MaxMessageSize + 1], ReliableChannel.MaxMessageSize + 1, 0));
        }

        [Test]
        public void ARunawayFragmentedMessage_FaultsTheChannel()
        {
            var channel = new ReliableChannel(d => { });
            string fault = null;
            channel.Faulted += reason => fault = reason;
            int delivered = 0;

            // fragments that never end: more than a message may hold
            int fragments = ReliableChannel.MaxMessageSize / ReliableChannel.MaxPacketPayload + 5;
            for (int i = 0; i < fragments && fault == null; i++)
            {
                var datagram = new byte[4 + ReliableChannel.MaxPacketPayload];
                datagram[0] = ReliableChannel.ChannelFragment;
                datagram[1] = (byte)i;
                datagram[2] = (byte)(i >> 8);
                datagram[3] = 0;
                channel.Receive(datagram, datagram.Length, 0, p => delivered++);
            }

            Assert.That(fault, Is.Not.Null);
            Assert.That(delivered, Is.EqualTo(0));
        }

        // ---- send window and bounded queue ----

        [Test]
        public void OnlyAWindowOfPacketsIsInFlight_TheRestWaitForAcknowledgements()
        {
            var wire = new List<byte[]>();
            var channel = new ReliableChannel(wire.Add);

            for (int i = 0; i < 1000; i++)
                channel.SendReliable(new byte[] { 1 }, 1, 0);

            Assert.That(channel.PendingCount, Is.EqualTo(ReliableChannel.SendWindow));
            Assert.That(channel.QueuedCount, Is.EqualTo(1000 - ReliableChannel.SendWindow));
            Assert.That(wire.Count, Is.EqualTo(ReliableChannel.SendWindow));

            // the peer takes the first 100: that many more are let out
            wire.Clear();
            channel.Receive(new byte[] { ReliableChannel.ChannelAck, 100, 0 }, 3, 0, p => { });

            Assert.That(wire.Count, Is.EqualTo(100));
            Assert.That(channel.QueuedCount, Is.EqualTo(1000 - ReliableChannel.SendWindow - 100));
        }

        [Test]
        public void ResendsAreCappedPerUpdate_OldestFirst()
        {
            var wire = new List<byte[]>();
            var channel = new ReliableChannel(wire.Add);
            for (int i = 0; i < ReliableChannel.SendWindow; i++)
                channel.SendReliable(BitConverter.GetBytes(i), 4, 0);
            wire.Clear();

            channel.Update(1.0);        // everything is overdue

            Assert.That(wire.Count, Is.EqualTo(ReliableChannel.MaxResendsPerUpdate));
            for (int i = 0; i < wire.Count; i++)
                Assert.That(BitConverter.ToInt32(wire[i], 3), Is.EqualTo(i), "the oldest are resent first");

            // the next update carries on with the ones after
            wire.Clear();
            channel.Update(1.0);
            Assert.That(BitConverter.ToInt32(wire[0], 3), Is.EqualTo(ReliableChannel.MaxResendsPerUpdate));
        }

        [Test]
        public void SelectiveAcks_StopTheSenderResendingWhatArrivedOutOfOrder()
        {
            var toReceiver = new List<byte[]>();
            var toSender = new List<byte[]>();
            var sender = new ReliableChannel(toReceiver.Add);
            var receiver = new ReliableChannel(toSender.Add);
            for (int i = 0; i < 6; i++)
                sender.SendReliable(BitConverter.GetBytes(i), 4, 0);

            // packets 0, 1 and 4 are lost; 2, 3 and 5 arrive
            var delivered = new List<int>();
            foreach (int arrived in new[] { 2, 3, 5 })
                receiver.Receive(toReceiver[arrived], toReceiver[arrived].Length, 0, p => delivered.Add(BitConverter.ToInt32(p, 0)));
            Assert.That(delivered, Is.Empty, "nothing can be delivered before packet 0");

            receiver.Update(0);
            sender.Receive(toSender[0], toSender[0].Length, 0, p => { });
            Assert.That(sender.PendingCount, Is.EqualTo(3), "2, 3 and 5 are confirmed, so only 0, 1 and 4 are outstanding");

            toReceiver.Clear();
            sender.Update(1.0);
            Assert.That(toReceiver.Select(d => BitConverter.ToInt32(d, 3)), Is.EqualTo(new[] { 0, 1, 4 }), "only the missing packets are resent");
        }

        [Test]
        public void AcksWithoutABitmap_StillWork()
        {
            var wire = new List<byte[]>();
            var sender = new ReliableChannel(wire.Add);
            for (int i = 0; i < 5; i++)
                sender.SendReliable(BitConverter.GetBytes(i), 4, 0);

            sender.Receive(new byte[] { ReliableChannel.ChannelAck, 3, 0 }, 3, 0, p => { });

            Assert.That(sender.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void APeerThatNeverAcknowledges_FaultsTheChannelOnce_AndNothingMoreIsSent()
        {
            var wire = new List<byte[]>();
            var channel = new ReliableChannel(wire.Add);
            var faults = new List<string>();
            channel.Faulted += faults.Add;

            for (int i = 0; i < ReliableChannel.SendWindow + ReliableChannel.MaxQueuedPackets + 10; i++)
                channel.SendReliable(new byte[] { 1 }, 1, 0);

            Assert.That(faults.Count, Is.EqualTo(1));
            Assert.That(channel.QueuedCount, Is.LessThanOrEqualTo(ReliableChannel.MaxQueuedPackets));

            int sentSoFar = wire.Count;
            channel.SendReliable(new byte[] { 1 }, 1, 0);
            channel.Update(10);
            Assert.That(wire.Count, Is.EqualTo(sentSoFar));
            Assert.That(faults.Count, Is.EqualTo(1));
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
            Assert.That(ReliableChannel.IsChannelByte(ReliableChannel.ChannelSequenced), Is.True);
            Assert.That(ReliableChannel.IsChannelByte(ReliableChannel.ChannelFragment), Is.True);
            Assert.That(ReliableChannel.IsChannelByte(0x26), Is.False);
            Assert.That(ReliableChannel.IsChannelByte(PayloadKind.Application), Is.False);
            Assert.That(ReliableChannel.IsChannelByte(PayloadKind.Message), Is.False);
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
                while (DateTime.UtcNow < deadline && (a.PendingCount > 0 || a.QueuedCount > 0))
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
