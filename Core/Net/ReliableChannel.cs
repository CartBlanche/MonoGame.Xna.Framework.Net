using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Delivery guarantees between two peers over UDP: reliable and ordered, ordered but lossy
    /// (newest wins), and plain unreliable.
    ///
    /// UDP drops, duplicates and reorders datagrams, and SendDataOptions mean nothing to it, so this
    /// adds what the options promise. Reliable payloads carry a sequence number, are resent until the
    /// peer acknowledges them, and are handed to the receiver exactly once and in order. Payloads larger
    /// than one datagram are split into fragments that travel as consecutive reliable packets and are
    /// put back together before delivery. At most <see cref="SendWindow"/> packets are in flight; the
    /// rest wait in a bounded queue, and a peer that lets it overflow is reported through
    /// <see cref="Faulted"/>.
    ///
    /// The channel knows nothing about sockets or sessions: datagrams leave through the
    /// sendDatagram delegate and incoming ones are passed to <see cref="Receive"/>. It is safe to
    /// call from several threads (the receive loop, the game thread and the resend timer).
    ///
    /// Datagram layout:
    ///   unreliable  [0x21][payload]
    ///   reliable    [0x22][seq lo][seq hi][payload]
    ///   ack         [0x23][next expected seq lo][hi][bitmap, 0 to 32 bytes]
    ///   sequenced   [0x24][seq lo][seq hi][payload]
    ///   fragment    [0x25][seq lo][seq hi][flags][payload]       flags bit 0: last fragment
    /// An ack says the peer has everything before "next expected", and the bitmap says which of the
    /// packets after it have also arrived (bit 0 of byte 0 is next expected + 1), so the sender resends
    /// only what is really missing. The first bytes stay clear of the framework message ids (1..10).
    /// </summary>
    // the MemoryStream it owns holds no resource that needs releasing, so the channel is not IDisposable
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "MemoryStream holds no unmanaged resource")]
    internal sealed class ReliableChannel
    {
        public const byte ChannelUnreliable = 0x21;
        public const byte ChannelReliable = 0x22;
        public const byte ChannelAck = 0x23;
        public const byte ChannelSequenced = 0x24;
        public const byte ChannelFragment = 0x25;

        const byte FlagLastFragment = 1;

        // seconds before an unacknowledged packet is sent again
        public const double ResendInterval = 0.1;

        // the most packets resent on one Update, oldest first, so a bad link is not flooded with resends
        public const int MaxResendsPerUpdate = 32;

        // the most that goes in one datagram. Small enough to cross a typical internet path (and VPNs and
        // tunnels) without IP fragmentation, which drops the whole datagram if any piece is lost
        public const int MaxPacketPayload = 1100;

        // the largest message that can be sent, whole or in fragments
        public const int MaxMessageSize = 1 << 20;

        // reliable packets in flight at once. The receiver only holds this many out-of-order packets
        public const int SendWindow = 256;

        // how far ahead of the next expected packet out of order packets are held back
        const int MaxEarlyPackets = SendWindow;

        // one bit for each packet that can be held early (next expected + 1 up to + 255)
        const int MaxAckBitmapBytes = (MaxEarlyPackets - 1 + 7) / 8;

        // reliable packets waiting for room in the window. A peer that falls this far behind has stopped answering
        public const int MaxQueuedPackets = 8192;

        class OutgoingPacket
        {
            public byte Channel;
            public byte Flags;
            public byte[] Payload = Array.Empty<byte>();
        }

        class PendingPacket
        {
            public ushort Sequence;
            public byte[] Datagram = Array.Empty<byte>();
            public double FirstSent;
            public double LastSent;
        }

        class IncomingPacket
        {
            public byte Channel;
            public byte Flags;
            public byte[] Payload = Array.Empty<byte>();
        }

        readonly object sync = new object();
        readonly Action<byte[]> sendDatagram;

        // sending
        ushort nextSendSequence;
        readonly Queue<OutgoingPacket> outgoing = new Queue<OutgoingPacket>();
        readonly List<PendingPacket> pending = new List<PendingPacket>();
        ushort nextSequencedSend;

        // receiving
        ushort nextExpectedSequence;
        readonly Dictionary<ushort, IncomingPacket> early = new Dictionary<ushort, IncomingPacket>();
        MemoryStream? reassembly;
        bool ackDue;
        ushort lastSequencedReceived;
        bool anySequencedReceived;

        bool faulted;

        public ReliableChannel(Action<byte[]> sendDatagram)
        {
            this.sendDatagram = sendDatagram ?? throw new ArgumentNullException(nameof(sendDatagram));
        }

        /// <summary>
        /// Raised, once, when the channel gives up on the peer: it stopped acknowledging until the send queue
        /// overflowed, or it sent something that breaks the protocol. The channel sends nothing after this.
        /// Raised outside the channel's lock, on whichever thread noticed.
        /// </summary>
        public event Action<string>? Faulted;

        /// <summary>
        /// Raised with the smoothed roundtrip time, in seconds, each time an acknowledgement measures one.
        /// Measured from the data itself, so it works over any transport.
        /// </summary>
        public event Action<double>? RoundtripMeasured;

        double smoothedRoundtrip;

        /// <summary>
        /// True if a datagram starting with this byte belongs to a channel
        /// </summary>
        public static bool IsChannelByte(byte first)
        {
            return first >= ChannelUnreliable && first <= ChannelFragment;
        }

        /// <summary>
        /// Reliable packets that the peer has not acknowledged yet
        /// </summary>
        public int PendingCount
        {
            get { lock (sync) return pending.Count; }
        }

        /// <summary>
        /// Reliable packets still waiting for room in the send window
        /// </summary>
        public int QueuedCount
        {
            get { lock (sync) return outgoing.Count; }
        }

        /// <summary>
        /// Seconds the oldest unacknowledged packet has been waiting (0 if there is none).
        /// A large value means the peer is gone or the link is badly broken.
        /// </summary>
        public double OldestUnackedAge(double now)
        {
            lock (sync)
                return pending.Count == 0 ? 0 : now - pending[0].FirstSent;
        }

        /// <summary>
        /// Send a payload that may be lost or arrive out of order. Limited to one datagram.
        /// </summary>
        public void SendUnreliable(byte[] payload, int length)
        {
            CheckFitsInOneDatagram(length);

            byte[] datagram = new byte[length + 1];
            datagram[0] = ChannelUnreliable;
            Buffer.BlockCopy(payload, 0, datagram, 1, length);
            sendDatagram(datagram);
        }

        /// <summary>
        /// Send a payload that may be lost, but that is never delivered after a newer one: the receiver
        /// drops anything older than what it has already delivered. Limited to one datagram.
        /// </summary>
        public void SendSequenced(byte[] payload, int length)
        {
            CheckFitsInOneDatagram(length);

            byte[] datagram = new byte[length + 3];
            datagram[0] = ChannelSequenced;
            lock (sync)
            {
                ushort sequence = nextSequencedSend++;
                datagram[1] = (byte)sequence;
                datagram[2] = (byte)(sequence >> 8);
            }
            Buffer.BlockCopy(payload, 0, datagram, 3, length);
            sendDatagram(datagram);
        }

        /// <summary>
        /// Send a payload that is delivered exactly once and in order, however large (up to
        /// <see cref="MaxMessageSize"/>): it is split into fragments if it does not fit one datagram.
        /// </summary>
        public void SendReliable(byte[] payload, int length, double now)
        {
            if (length > MaxMessageSize)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "A message can be at most " + MaxMessageSize + " bytes, this one is " + length + ".");

            List<byte[]>? toSend;
            lock (sync)
            {
                if (faulted)
                    return;

                int chunks = Math.Max(1, (length + MaxPacketPayload - 1) / MaxPacketPayload);
                if (outgoing.Count + chunks > MaxQueuedPackets)
                {
                    faulted = true;
                    toSend = null;
                }
                else
                {
                    if (chunks == 1)
                    {
                        outgoing.Enqueue(new OutgoingPacket { Channel = ChannelReliable, Payload = Slice(payload, 0, length) });
                    }
                    else
                    {
                        // enqueued together under the lock, so the fragments get consecutive sequence numbers
                        for (int i = 0; i < chunks; i++)
                        {
                            int offset = i * MaxPacketPayload;
                            int count = Math.Min(MaxPacketPayload, length - offset);
                            outgoing.Enqueue(new OutgoingPacket
                            {
                                Channel = ChannelFragment,
                                Flags = i == chunks - 1 ? FlagLastFragment : (byte)0,
                                Payload = Slice(payload, offset, count),
                            });
                        }
                    }
                    toSend = Admit(now);
                }
            }

            if (toSend == null)
            {
                RaiseFaulted("The peer stopped acknowledging: " + MaxQueuedPackets + " packets are waiting to be sent.");
                return;
            }

            foreach (byte[] datagram in toSend)
                sendDatagram(datagram);
        }

        /// <summary>
        /// Process a datagram from the peer. Payloads that are ready for the application are passed
        /// to deliver, in order, after the channel's lock is released.
        /// </summary>
        public void Receive(byte[] datagram, int length, double now, Action<byte[]> deliver)
        {
            if (length < 1)
                return;

            List<byte[]>? ready = null;
            List<byte[]>? toSend = null;
            string? fault = null;
            double rttSample = 0;

            lock (sync)
            {
                switch (datagram[0])
                {
                    case ChannelUnreliable:
                        ready = new List<byte[]> { Slice(datagram, 1, length - 1) };
                        break;

                    case ChannelSequenced:
                        if (length >= 3)
                        {
                            ushort sequence = (ushort)(datagram[1] | (datagram[2] << 8));
                            if (!anySequencedReceived || IsBefore(lastSequencedReceived, sequence))
                            {
                                anySequencedReceived = true;
                                lastSequencedReceived = sequence;
                                ready = new List<byte[]> { Slice(datagram, 3, length - 3) };
                            }
                            // otherwise it is older than something already delivered, or a duplicate
                        }
                        break;

                    case ChannelReliable:
                        if (length >= 3)
                            ready = ReceiveReliable(
                                (ushort)(datagram[1] | (datagram[2] << 8)),
                                new IncomingPacket { Channel = ChannelReliable, Payload = Slice(datagram, 3, length - 3) },
                                out fault);
                        break;

                    case ChannelFragment:
                        if (length >= 4)
                            ready = ReceiveReliable(
                                (ushort)(datagram[1] | (datagram[2] << 8)),
                                new IncomingPacket { Channel = ChannelFragment, Flags = datagram[3], Payload = Slice(datagram, 4, length - 4) },
                                out fault);
                        break;

                    case ChannelAck:
                        if (length >= 3)
                        {
                            rttSample = ReceiveAck((ushort)(datagram[1] | (datagram[2] << 8)), datagram, 3, Math.Min(length - 3, MaxAckBitmapBytes), now);
                            // the window has room again for what was waiting
                            toSend = Admit(now);
                        }
                        break;
                }
            }

            if (fault != null)
                RaiseFaulted(fault);

            if (rttSample > 0)
                RoundtripMeasured?.Invoke(rttSample);

            if (toSend != null)
                foreach (byte[] queued in toSend)
                    sendDatagram(queued);

            if (ready != null)
                foreach (byte[] payload in ready)
                    deliver(payload);
        }

        /// <summary>
        /// Resend packets that have not been acknowledged and acknowledge what arrived.
        /// Call regularly (every few tens of milliseconds).
        /// </summary>
        public void Update(double now)
        {
            List<byte[]>? resend = null;
            byte[]? ack = null;

            lock (sync)
            {
                if (faulted)
                    return;

                foreach (PendingPacket p in pending)
                {
                    if (now - p.LastSent >= ResendInterval)
                    {
                        p.LastSent = now;
                        (resend ?? (resend = new List<byte[]>())).Add(p.Datagram);
                        if (resend.Count >= MaxResendsPerUpdate)
                            break;
                    }
                }

                if (ackDue)
                {
                    ackDue = false;
                    ack = BuildAck();
                }
            }

            if (resend != null)
                foreach (byte[] datagram in resend)
                    sendDatagram(datagram);
            if (ack != null)
                sendDatagram(ack);
        }

        // moves queued packets into the send window, giving them their sequence numbers. Call with the lock held.
        List<byte[]> Admit(double now)
        {
            List<byte[]>? datagrams = null;

            // the window is measured from the oldest packet still unacknowledged, as that is where the
            // receiver is waiting; packets it has already confirmed out of order don't hold it open
            while (outgoing.Count > 0 && (pending.Count == 0 || (ushort)(nextSendSequence - pending[0].Sequence) < SendWindow))
            {
                OutgoingPacket packet = outgoing.Dequeue();
                ushort sequence = nextSendSequence++;

                int header = packet.Channel == ChannelFragment ? 4 : 3;
                byte[] datagram = new byte[header + packet.Payload.Length];
                datagram[0] = packet.Channel;
                datagram[1] = (byte)sequence;
                datagram[2] = (byte)(sequence >> 8);
                if (packet.Channel == ChannelFragment)
                    datagram[3] = packet.Flags;
                Buffer.BlockCopy(packet.Payload, 0, datagram, header, packet.Payload.Length);

                pending.Add(new PendingPacket
                {
                    Sequence = sequence,
                    Datagram = datagram,
                    FirstSent = now,
                    LastSent = now,
                });
                (datagrams ?? (datagrams = new List<byte[]>())).Add(datagram);
            }

            return datagrams ?? new List<byte[]>();
        }

        List<byte[]>? ReceiveReliable(ushort sequence, IncomingPacket packet, out string? fault)
        {
            fault = null;

            // always acknowledge, even duplicates: the earlier ack may have been lost
            ackDue = true;

            ushort ahead = (ushort)(sequence - nextExpectedSequence);

            if (ahead == 0)
            {
                List<byte[]> ready = new List<byte[]>();
                Complete(packet, ready, ref fault);
                nextExpectedSequence++;

                // anything that arrived early and is now in order follows
                IncomingPacket? next;
                while (fault == null && early.TryGetValue(nextExpectedSequence, out next))
                {
                    early.Remove(nextExpectedSequence);
                    Complete(next, ready, ref fault);
                    nextExpectedSequence++;
                }

                return ready;
            }

            if (ahead < MaxEarlyPackets)
            {
                // arrived before an earlier packet: hold it until the gap is filled
                early[sequence] = packet;
            }

            // otherwise it is a duplicate of something already delivered
            return null;
        }

        // a packet that is now in order: whole messages are ready, fragments are collected until the last one
        void Complete(IncomingPacket packet, List<byte[]> ready, ref string? fault)
        {
            if (packet.Channel == ChannelReliable)
            {
                // a whole message in the middle of a fragmented one: the peer is broken
                if (reassembly != null)
                {
                    fault = "The peer interrupted a fragmented message.";
                    reassembly = null;
                    faulted = true;
                    return;
                }
                ready.Add(packet.Payload);
                return;
            }

            if (reassembly == null)
                reassembly = new MemoryStream();

            if (reassembly.Length + packet.Payload.Length > MaxMessageSize)
            {
                fault = "The peer sent a message larger than " + MaxMessageSize + " bytes.";
                reassembly = null;
                faulted = true;
                return;
            }

            reassembly.Write(packet.Payload, 0, packet.Payload.Length);
            if ((packet.Flags & FlagLastFragment) != 0)
            {
                ready.Add(reassembly.ToArray());
                reassembly = null;
            }
        }

        // Returns the smoothed roundtrip time in seconds if this ack measured one, else 0.
        // Only a packet that was never resent can be timed: an ack for a resent one could be
        // answering either send.
        double ReceiveAck(ushort nextExpected, byte[] datagram, int bitmapStart, int bitmapLength, double now)
        {
            double sample = -1;

            // the peer has everything before nextExpected, and what the bitmap marks after it
            pending.RemoveAll(p =>
            {
                bool acked = IsBefore(p.Sequence, nextExpected);
                if (!acked)
                {
                    int bit = (ushort)(p.Sequence - nextExpected) - 1;
                    acked = bit >= 0 && bit < bitmapLength * 8 && (datagram[bitmapStart + bit / 8] & (1 << (bit % 8))) != 0;
                }

                if (acked && p.LastSent == p.FirstSent)
                    sample = now - p.FirstSent;   // the newest such packet is the freshest measurement
                return acked;
            });

            if (sample < 0)
                return 0;

            smoothedRoundtrip = smoothedRoundtrip == 0 ? sample : smoothedRoundtrip * 0.875 + sample * 0.125;
            return Math.Max(smoothedRoundtrip, double.Epsilon);
        }

        // [ack][next expected][which packets after it are held early]. Call with the lock held.
        byte[] BuildAck()
        {
            byte[] bitmap = new byte[MaxAckBitmapBytes];
            int used = 0;
            foreach (ushort sequence in early.Keys)
            {
                int bit = (ushort)(sequence - nextExpectedSequence) - 1;
                if (bit < 0 || bit >= MaxAckBitmapBytes * 8)
                    continue;
                bitmap[bit / 8] |= (byte)(1 << (bit % 8));
                used = Math.Max(used, bit / 8 + 1);
            }

            byte[] ack = new byte[3 + used];
            ack[0] = ChannelAck;
            ack[1] = (byte)nextExpectedSequence;
            ack[2] = (byte)(nextExpectedSequence >> 8);
            Buffer.BlockCopy(bitmap, 0, ack, 3, used);
            return ack;
        }

        // true if a comes before b (sequence numbers wrap around)
        static bool IsBefore(ushort a, ushort b)
        {
            return a != b && (ushort)(b - a) < 0x8000;
        }

        static void CheckFitsInOneDatagram(int length)
        {
            if (length > MaxPacketPayload)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "Unreliable data is limited to " + MaxPacketPayload + " bytes in one packet, this is " + length +
                    ". Send it reliably and it will be split up.");
        }

        static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] copy = new byte[count];
            Buffer.BlockCopy(source, offset, copy, 0, count);
            return copy;
        }

        void RaiseFaulted(string reason)
        {
            Action<string>? handler;
            lock (sync)
            {
                faulted = true;
                handler = Faulted;
                Faulted = null;                 // once
            }
            handler?.Invoke(reason);
        }
    }
}
