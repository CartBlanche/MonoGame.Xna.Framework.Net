using System.Diagnostics;
using Microsoft.Xna.Framework.Net;

namespace DataPath;

/// <summary>
/// One player's side of the test. Everyone sends everyone the same four kinds of data for a while, then says
/// what it sent, and then checks what arrived against that. Only the public XNA-shaped API is used.
/// </summary>
public static class Run
{
    static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs this player's side. The host waits for <paramref name="expectedPeers"/> others to be in the session and
    /// then tells everyone to begin; the others wait for that.
    /// </summary>
    public static async Task<NodeReport> RunAsync(NetworkSession session, Options options, int expectedPeers, TextWriter? log = null)
    {
        LocalNetworkGamer me = session.LocalGamers[0];
        var peers = new Dictionary<string, PeerStats>();
        var reader = new PacketReader();
        int durationMs = -1;

        PeerStats StatsFor(NetworkGamer who)
        {
            if (!peers.TryGetValue(who.GamerId, out PeerStats? s))
                peers[who.GamerId] = s = new PeerStats(who.GamerId, who.Gamertag);
            return s;
        }

        // takes in everything that has arrived
        void Pump()
        {
            session.Update();
            while (me.IsDataAvailable)
            {
                me.ReceiveData(reader, out NetworkGamer? sender);
                if (sender == null)
                    continue;
                PeerStats stats = StatsFor(sender);

                switch ((Kind)reader.ReadByte())
                {
                    case Kind.Start:
                        durationMs = reader.ReadInt32();
                        break;

                    case Kind.Position:
                    {
                        uint seq = reader.ReadUInt32();
                        var at = reader.ReadVector2();
                        long sentAt = reader.ReadInt64();
                        if (!stats.PositionsSeen.Add(seq)) stats.PositionDuplicates++;
                        else stats.Positions++;
                        if (at != Wire.PositionAt(seq)) stats.PositionsWrong++;
                        if (options.SameProcess)
                        {
                            double ms = (Stopwatch.GetTimestamp() - sentAt) * 1000.0 / Stopwatch.Frequency;
                            stats.LatencyMinMs = Math.Min(stats.LatencyMinMs, ms);
                            stats.LatencySumMs += ms;
                            stats.LatencySamples++;
                        }
                        break;
                    }

                    case Kind.Sequenced:
                    {
                        uint seq = reader.ReadUInt32();
                        if (stats.Sequenced > 0 && seq <= stats.LastSequenced) stats.SequencedBackwards++;
                        stats.LastSequenced = seq;
                        stats.Sequenced++;
                        break;
                    }

                    case Kind.Event:
                    {
                        uint seq = reader.ReadUInt32();
                        string text = reader.ReadString();
                        if (seq != stats.NextEvent || text != "event " + seq) stats.EventsOutOfOrder++;
                        stats.NextEvent = seq + 1;
                        stats.Events++;
                        break;
                    }

                    case Kind.Blob:
                    {
                        uint seed = reader.ReadUInt32();
                        int length = reader.ReadInt32();
                        byte[] bytes = reader.ReadBytes(length);
                        uint sum = reader.ReadUInt32();
                        if (bytes.Length != length || Wire.Checksum(bytes) != sum || sum != Wire.Checksum(Wire.MakeBlob(seed)))
                            stats.BlobsCorrupt++;
                        stats.Blobs++;
                        break;
                    }

                    case Kind.Done:
                        stats.SentPositions = reader.ReadUInt32();
                        stats.SentSequenced = reader.ReadUInt32();
                        stats.SentEvents = reader.ReadUInt32();
                        stats.SentBlobs = reader.ReadUInt32();
                        stats.DoneReceived = true;
                        break;
                }
            }
        }

        // 1. begin together: the host waits for everyone to be in the session, then says go
        var waiting = Stopwatch.StartNew();
        if (session.IsHost)
        {
            while (session.RemoteGamers.Count < expectedPeers && waiting.Elapsed < StartTimeout)
            {
                Pump();
                await Task.Delay(10).ConfigureAwait(false);
            }
            durationMs = (int)(options.Seconds * 1000);
            var start = new PacketWriter();
            start.Write((byte)Kind.Start);
            start.Write(durationMs);
            me.SendData(start, SendDataOptions.ReliableInOrder);
        }
        else
        {
            while (durationMs < 0 && waiting.Elapsed < StartTimeout)
            {
                Pump();
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        var report = new NodeReport(me.Gamertag, session.IsHost);
        if (durationMs < 0)
        {
            report.Failures.Add("the host never said to begin");
            return report;
        }

        // 2. this machine's network gets worse on purpose, if asked
        session.SimulatedPacketLoss = options.Loss;
        session.SimulatedLatency = TimeSpan.FromMilliseconds(options.LatencyMs);

        // 3. send for a while
        uint positions = 0, sequenced = 0, events = 0, blobs = 0;
        double nextPosition = 0, nextSequenced = 0, nextEvent = 0;
        bool blobSent = false;
        var running = Stopwatch.StartNew();
        while (running.ElapsedMilliseconds < durationMs)
        {
            double t = running.Elapsed.TotalSeconds;

            if (t >= nextPosition)
            {
                nextPosition += 1.0 / options.PositionHz;
                uint seq = ++positions;
                var w = new PacketWriter();
                w.Write((byte)Kind.Position);
                w.Write(seq);
                w.Write(Wire.PositionAt(seq));
                w.Write(Stopwatch.GetTimestamp());
                me.SendData(w, SendDataOptions.None);
            }

            if (t >= nextSequenced)
            {
                nextSequenced += 1.0 / options.SequencedHz;
                var w = new PacketWriter();
                w.Write((byte)Kind.Sequenced);
                w.Write(++sequenced);
                me.SendData(w, SendDataOptions.InOrder);
            }

            if (t >= nextEvent)
            {
                nextEvent += 1.0 / options.EventHz;
                uint seq = ++events;
                var w = new PacketWriter();
                w.Write((byte)Kind.Event);
                w.Write(seq);
                w.Write("event " + seq);
                me.SendData(w, SendDataOptions.ReliableInOrder);
            }

            if (!blobSent && t >= 1.0)
            {
                blobSent = true;
                blobs++;
                uint seed = 7;
                byte[] bytes = Wire.MakeBlob(seed);
                var w = new PacketWriter();
                w.Write((byte)Kind.Blob);
                w.Write(seed);
                w.Write(bytes.Length);
                w.Write(bytes);
                w.Write(Wire.Checksum(bytes));
                me.SendData(w, SendDataOptions.ReliableInOrder);
            }

            Pump();
            await Task.Delay(2).ConfigureAwait(false);
        }

        // 4. say what was sent, then wait for everyone else to say the same
        var done = new PacketWriter();
        done.Write((byte)Kind.Done);
        done.Write(positions);
        done.Write(sequenced);
        done.Write(events);
        done.Write(blobs);
        me.SendData(done, SendDataOptions.ReliableInOrder);

        var finishing = Stopwatch.StartNew();
        TimeSpan finishTimeout = TimeSpan.FromSeconds(20) + TimeSpan.FromMilliseconds(options.LatencyMs * 20);
        while (finishing.Elapsed < finishTimeout)
        {
            Pump();
            var others = session.AllGamers.Where(g => !g.IsLocal).ToList();
            if (others.Count >= expectedPeers && others.All(g => peers.TryGetValue(g.GamerId, out PeerStats? p) && p.DoneReceived))
                break;
            await Task.Delay(10).ConfigureAwait(false);
        }

        // 5. check it all
        var expected = session.AllGamers.Where(g => !g.IsLocal).Select(g => (g.GamerId, g.Gamertag)).ToList();
        if (expected.Count < expectedPeers)
            report.Failures.Add($"only {expected.Count} of the {expectedPeers} other players were still in the session at the end");
        report.Evaluate(peers.Values, expected, options);
        log?.WriteLine($"{me.Gamertag}: sent {positions} positions, {sequenced} in-order, {events} events, {blobs} large message");
        return report;
    }
}
