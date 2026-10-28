using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Reliable, ordered delivery (plus a plain unreliable channel) between two peers over UDP.
    /// 
    /// UDP drops, duplicates and reorders datagrams, and SendDataOptions.Reliable means nothing to it,
    /// so this adds what the option promises. Reliable payloads carry a sequence number, are resent
    /// until the peer acknowledges them, and are handed to the receiver exactly once and in order.
    /// 
    /// The channel knows nothing about sockets or sessions: datagrams leave through the
    /// sendDatagram delegate and incoming ones are passed to <see cref="Receive"/>. It is safe to
    /// call from several threads (the receive loop, the game thread and the resend timer).
    /// 
    /// Datagram layout:
    ///   unreliable  [0x21][payload]
    ///   reliable    [0x22][seq lo][seq hi][payload]
    ///   ack         [0x23][next expected seq lo][hi]
    /// The first bytes stay clear of the framework message ids (1..10).
    /// </summary>
    internal sealed class ReliableChannel
    {
        public const byte ChannelUnreliable = 0x21;
        public const byte ChannelReliable = 0x22;
        public const byte ChannelAck = 0x23;

        // seconds before an unacknowledged packet is sent again
        public const double ResendInterval = 0.1;

        // how far ahead of the next expected packet out of order packets are held back
        const int MaxEarlyPackets = 256;

        class PendingPacket
        {
            public ushort Sequence;
            public byte[] Datagram;
            public double FirstSent;
            public double LastSent;
        }

        readonly object sync = new object();
        readonly Action<byte[]> sendDatagram;

        ushort nextSendSequence;
        readonly List<PendingPacket> pending = new List<PendingPacket>();

        ushort nextExpectedSequence;
        readonly Dictionary<ushort, byte[]> early = new Dictionary<ushort, byte[]>();
        bool ackDue;

        public ReliableChannel(Action<byte[]> sendDatagram)
        {
            this.sendDatagram = sendDatagram ?? throw new ArgumentNullException(nameof(sendDatagram));
        }

        /// <summary>
        /// True if a datagram starting with this byte belongs to a channel
        /// </summary>
        public static bool IsChannelByte(byte first)
        {
            return first >= ChannelUnreliable && first <= ChannelAck;
        }

        /// <summary>
        /// Reliable packets that the peer has not acknowledged yet
        /// </summary>
        public int PendingCount
        {
            get { lock (sync) return pending.Count; }
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
        /// Send a payload that may be lost or arrive out of order
        /// </summary>
        public void SendUnreliable(byte[] payload, int length)
        {
            byte[] datagram = new byte[length + 1];
            datagram[0] = ChannelUnreliable;
            Buffer.BlockCopy(payload, 0, datagram, 1, length);
            sendDatagram(datagram);
        }

        /// <summary>
        /// Send a payload that is delivered exactly once and in order
        /// </summary>
        public void SendReliable(byte[] payload, int length, double now)
        {
            byte[] datagram;
            lock (sync)
            {
                ushort sequence = nextSendSequence++;

                datagram = new byte[length + 3];
                datagram[0] = ChannelReliable;
                datagram[1] = (byte)sequence;
                datagram[2] = (byte)(sequence >> 8);
                Buffer.BlockCopy(payload, 0, datagram, 3, length);

                pending.Add(new PendingPacket
                {
                    Sequence = sequence,
                    Datagram = datagram,
                    FirstSent = now,
                    LastSent = now,
                });
            }

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

            List<byte[]> ready = null;

            lock (sync)
            {
                switch (datagram[0])
                {
                    case ChannelUnreliable:
                        ready = new List<byte[]> { CopyPayload(datagram, 1, length) };
                        break;

                    case ChannelReliable:
                        if (length >= 3)
                            ready = ReceiveReliable(
                                (ushort)(datagram[1] | (datagram[2] << 8)),
                                CopyPayload(datagram, 3, length));
                        break;

                    case ChannelAck:
                        if (length >= 3)
                            ReceiveAck((ushort)(datagram[1] | (datagram[2] << 8)));
                        break;
                }
            }

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
            List<byte[]> resend = null;
            byte[] ack = null;

            lock (sync)
            {
                foreach (PendingPacket p in pending)
                {
                    if (now - p.LastSent >= ResendInterval)
                    {
                        p.LastSent = now;
                        (resend ?? (resend = new List<byte[]>())).Add(p.Datagram);
                    }
                }

                if (ackDue)
                {
                    ackDue = false;
                    ack = new byte[]
                    {
                        ChannelAck,
                        (byte)nextExpectedSequence,
                        (byte)(nextExpectedSequence >> 8),
                    };
                }
            }

            if (resend != null)
                foreach (byte[] datagram in resend)
                    sendDatagram(datagram);
            if (ack != null)
                sendDatagram(ack);
        }

        List<byte[]> ReceiveReliable(ushort sequence, byte[] payload)
        {
            // always acknowledge, even duplicates: the earlier ack may have been lost
            ackDue = true;

            ushort ahead = (ushort)(sequence - nextExpectedSequence);

            if (ahead == 0)
            {
                List<byte[]> ready = new List<byte[]> { payload };
                nextExpectedSequence++;

                // anything that arrived early and is now in order follows
                byte[] next;
                while (early.TryGetValue(nextExpectedSequence, out next))
                {
                    early.Remove(nextExpectedSequence);
                    ready.Add(next);
                    nextExpectedSequence++;
                }

                return ready;
            }

            if (ahead < MaxEarlyPackets)
            {
                // arrived before an earlier packet: hold it until the gap is filled
                early[sequence] = payload;
            }

            // otherwise it is a duplicate of something already delivered
            return null;
        }

        void ReceiveAck(ushort nextExpected)
        {
            // the peer has everything before nextExpected
            pending.RemoveAll(p => IsBefore(p.Sequence, nextExpected));
        }

        // true if a comes before b (sequence numbers wrap around)
        static bool IsBefore(ushort a, ushort b)
        {
            return a != b && (ushort)(b - a) < 0x8000;
        }

        static byte[] CopyPayload(byte[] datagram, int offset, int length)
        {
            byte[] payload = new byte[length - offset];
            Buffer.BlockCopy(datagram, offset, payload, 0, payload.Length);
            return payload;
        }
    }
}
