using Microsoft.Xna.Framework.GamerServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Represents a player in a network session.
    /// </summary>
    public class NetworkGamer : INetworkGamer
    {
        private readonly NetworkSession session;
        private readonly string id;
        private byte? shortId;
        private readonly bool isLocal;
        private readonly bool isHost;
        private string gamertag;
        private bool isReady;
        private object tag;

        // Network performance properties
        private TimeSpan roundtripTime = TimeSpan.Zero;

        /// <summary>
        /// Gets the identifier for this gamer within the session, as XNA does: a byte, unique among the
        /// gamers of this session as this machine sees them.
        /// </summary>
        public byte Id
        {
            get
            {
                if (shortId == null)
                    shortId = session?.AllocateGamerId() ?? 0;
                return shortId.Value;
            }
        }

        /// <summary>
        /// Gets the globally unique identifier for this gamer, the one that goes on the wire.
        /// </summary>
        public string GamerId => id;

        string INetworkGamer.Id => id;

        internal bool HasShortId => shortId != null;

        /// <summary>
        /// Gets whether this gamer is the local player.
        /// </summary>
        public bool IsLocal => isLocal;

        /// <summary>
        /// Gets whether this gamer is the host of the session.
        /// </summary>
        public bool IsHost => isHost;

        /// <summary>
        /// Gets or sets the gamertag for this gamer.
        /// </summary>
        public string Gamertag
        {
            get => gamertag;
            set => gamertag = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets or sets whether this gamer is ready to start the game.
        /// </summary>
        public bool IsReady
        {
            get => isReady;
            set
            {
                if (isReady != value)
                {
                    isReady = value;
                    // Notify session of readiness change if this is the local gamer
                    if (isLocal)
                    {
                        session?.NotifyReadinessChanged(this);
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets custom data associated with this gamer.
        /// </summary>
        public object Tag
        {
            get => tag;
            set => tag = value;
        }

        /// <summary>
        /// Gets the machine for this network gamer.
        /// </summary>
        public NetworkMachine Machine => session?.MachineOf(this);

        /// <summary>
        /// Gets whether data is available to be received from this gamer.
        /// </summary>
        public bool IsDataAvailable
        {
            get
            {
                lock (incomingPackets)
                {
                    return incomingPackets.Count > 0;
                }
            }
        }

        /// <summary>
        /// Gets the SignedInGamer associated with this NetworkGamer.
        /// </summary>
        public SignedInGamer SignedInGamer => isLocal ? SignedInGamer.Current : null;

        /// <summary>
        /// Gets whether this gamer is muted by the local user.
        /// </summary>
        public bool IsMutedByLocalUser => false; // Mock implementation

        /// <summary>
        /// Gets whether this gamer is currently talking.
        /// </summary>
        public bool IsTalking => false; // Mock implementation

        /// <summary>
        /// Gets whether this gamer has voice capabilities.
        /// </summary>
        public bool HasVoice => false; // Mock implementation

        // Add to NetworkGamer class
        private readonly Queue<(byte[] Data, NetworkGamer Sender)> incomingPackets = new Queue<(byte[], NetworkGamer)>();

        internal NetworkGamer(NetworkSession session, string id, bool isLocal, bool isHost, string gamertag)
        {
            this.session = session;
            this.id = id;
            this.isLocal = isLocal;
            this.isHost = isHost;
            this.gamertag = gamertag;
        }

        /// <summary>
        /// Receives data from this gamer.
        /// </summary>
        /// <param name="data">Array to receive the data into.</param>
        /// <param name="sender">The gamer who sent the data.</param>
        /// <returns>The number of bytes received.</returns>
        public int ReceiveData(byte[] data, out NetworkGamer sender)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            sender = null;

            if (!TryDequeueIncomingPacket(out var packet, data.Length))
                return 0;

            sender = packet.Sender;
            Array.Copy(packet.Data, data, packet.Data.Length);
            return packet.Data.Length;
        }

        public int ReceiveData(byte[] data, int offset, out NetworkGamer sender)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || offset >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be within the bounds of the data array.");

            sender = null;

            if (!TryDequeueIncomingPacket(out var packet, data.Length - offset))
                return 0;

            sender = packet.Sender;
            Array.Copy(packet.Data, 0, data, offset, packet.Data.Length);
            return packet.Data.Length;
        }

        /// <summary>
        /// Receives data from this gamer using PacketReader.
        /// </summary>
        /// <param name="data">Array to receive the data into.</param>
        /// <param name="sender">The gamer who sent the data.</param>
        /// <returns>The number of bytes received.</returns>
        public int ReceiveData(PacketReader reader, out NetworkGamer sender)
        {
            // Ensure the session is valid
            if (session == null)
                throw new InvalidOperationException("Network session is not initialized.");

            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            sender = null;

            if (!TryDequeueIncomingPacket(out var packet))
                return 0;

            sender = packet.Sender;
            reader.Reset(packet.Data);
            return packet.Data.Length;
        }

        /// <summary>
        /// Sends data to other gamers in the session.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        public void SendData(byte[] data, SendDataOptions options)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (!Enum.IsDefined(typeof(SendDataOptions), options))
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid send data option.");

            SendDataInternal(data, options, session.AllGamers);
        }

        /// <summary>
        /// Sends data to specific gamers in the session.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        /// <param name="recipients">The gamers to send to.</param>
        public void SendData(byte[] data, SendDataOptions options, IEnumerable<NetworkGamer> recipients)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (!Enum.IsDefined(typeof(SendDataOptions), options))
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid send data option.");

            SendDataInternal(data, options, recipients);
        }

        /// <summary>
        /// Sends data using PacketWriter to specific recipients.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        /// <param name="recipients">The gamers to send to.</param>
        public void SendData(PacketWriter data, SendDataOptions options, IEnumerable<NetworkGamer> recipients)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "PacketWriter cannot be null.");

            if (!Enum.IsDefined(typeof(SendDataOptions), options))
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid send data option.");

            if (recipients == null)
                throw new ArgumentNullException(nameof(recipients));

            int serializedLength;
            byte[] serializedData = data.RentData(out serializedLength);
            try
            {
                SendDataInternal(serializedData, serializedLength, options, recipients);
            }
            finally
            {
                PacketWriter.ReturnRentedData(serializedData);
            }

            data.Position = 0; // Reset position after sending
        }

        /// <summary>
        /// Sends data using PacketWriter.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        public void SendData(PacketWriter data, SendDataOptions options)
        {
            SendData(data, options, session.AllGamers);
        }

        /// <summary>
        /// Sends data using PacketWriter to a specific recipient.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        /// <param name="recipient">The gamer to send to.</param>
        public void SendData(PacketWriter data, SendDataOptions options, NetworkGamer recipient)
        {
            SendData(data, options, new[] { recipient });
        }

        /// <summary>
        /// Sends byte array data to a specific recipient.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="options">Send options.</param>
        /// <param name="recipient">The gamer to send to.</param>
        public void SendData(byte[] data, SendDataOptions options, NetworkGamer recipient)
        {
            SendData(data, options, new[] { recipient });
        }

        /// <summary>
        /// Gets the current round-trip time for network communication with this gamer.
        /// </summary>
        public TimeSpan RoundtripTime => roundtripTime;

        /// <summary>
        /// How long the oldest reliable data sent to this gamer has been waiting for an
        /// acknowledgement (zero when nothing is waiting). A large value means the gamer has
        /// stopped answering.
        /// </summary>
        public TimeSpan UnacknowledgedAge => session.GetUnacknowledgedAge(this);

        /// <summary>
        /// Updates the round-trip time (internal use).
        /// </summary>
        internal void UpdateRoundtripTime(TimeSpan newRoundtripTime)
        {
            roundtripTime = newRoundtripTime;
        }

        public override string ToString()
        {
            return $"{Gamertag} ({(IsLocal ? "Local" : "Remote")}, {(IsHost ? "Host" : "Player")})";
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkGamer other && GamerId == other.GamerId;
        }

        public override int GetHashCode()
        {
            return GamerId.GetHashCode();
        }

        /// <summary>
        /// Implicit conversion from NetworkGamer to SignedInGamer.
        /// </summary>
        public static implicit operator SignedInGamer(NetworkGamer gamer)
        {
            return gamer?.SignedInGamer;
        }

        private void SendDataInternal(byte[] data, SendDataOptions options, IEnumerable<NetworkGamer> recipients)
        {
            SendDataInternal(data, data?.Length ?? 0, options, recipients);
        }

        private void SendDataInternal(byte[] data, int dataLength, SendDataOptions options, IEnumerable<NetworkGamer> recipients)
        {
            switch (session.SessionType)
            {
                case NetworkSessionType.SystemLink:
                    // Everyone but the sender: a gamer never receives what it sent itself, as in XNA
                    foreach (var recipient in recipients)
                    {
                        if (recipient == null || recipient == this)
                            continue;
                        session.SendDataToGamer(recipient, data, dataLength, options);
                    }
                    break;

                case NetworkSessionType.Local:
                    // Direct in-process delivery to every local gamer (split-screen scenario).
                    // Reliable/in-order semantics are trivially satisfied.
                    var delivered = new HashSet<NetworkGamer>();
                    foreach (var recipient in recipients)
                    {
                        if (recipient == null || recipient == this || !recipient.IsLocal)
                            continue;
                        if (!delivered.Add(recipient))
                            continue; // avoid duplicate enqueue if recipients enumerates same gamer twice

                        var clone = new byte[dataLength];
                        Buffer.BlockCopy(data, 0, clone, 0, dataLength);
                        recipient.EnqueueIncomingPacket(clone, this);
                    }
                    break;

                case NetworkSessionType.PlayerMatch:
                    // Placeholder for future implementation
                    throw new NotImplementedException("PlayerMatch session type is not yet supported.");

                case NetworkSessionType.Ranked:
                    // Placeholder for future implementation
                    throw new NotImplementedException("Ranked session type is not yet supported.");

                default:
                    throw new ArgumentOutOfRangeException(nameof(session.SessionType), $"Unsupported session type: {session.SessionType}");
            }
        }

        /// <summary>
        /// The most packets held for a gamer that never calls ReceiveData. Past this the oldest are dropped.
        /// </summary>
        internal const int MaxIncomingPackets = 8192;

        internal void EnqueueIncomingPacket(byte[] data, NetworkGamer sender)
        {
            bool dropped = false;
            lock (incomingPackets)
            {
                if (incomingPackets.Count >= MaxIncomingPackets)
                {
                    incomingPackets.Dequeue();   // the oldest is the least useful to a game that has fallen behind
                    dropped = true;
                }
                incomingPackets.Enqueue((data, sender));
            }

            if (dropped)
                session?.Diagnostics?.RecordPacketDropped();
        }

        // room is how many bytes the caller can take. A packet that does not fit is left in the queue
        // and an exception says how big a buffer it needs: truncating would hand the game corrupt data.
        private bool TryDequeueIncomingPacket(out (byte[] Data, NetworkGamer Sender) packet, int room = int.MaxValue)
        {
            lock (incomingPackets)
            {
                if (incomingPackets.Count == 0)
                {
                    packet = default;
                    return false;
                }

                int needed = incomingPackets.Peek().Data.Length;
                if (needed > room)
                    throw new ArgumentException(
                        $"The buffer holds {room} bytes but the next packet is {needed} bytes. It stays queued; read it with a larger buffer.");

                packet = incomingPackets.Dequeue();
                return true;
            }
        }
    }
}